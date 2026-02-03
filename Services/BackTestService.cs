using IronCondor.Constants;
using IronCondor.Infrastructure;
using IronCondor.Models;

namespace IronCondor.Services;

/// <summary>
/// Service for backtesting iron condor strategies.
/// </summary>
public class BackTestService
{
    private readonly ILogger _logger;

    public BackTestService(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Finds optimal trigger percentages by analyzing historical data to identify the best entry points.
    /// </summary>
    /// <param name="weeklyPriceBars">Historical weekly price bar data.</param>
    /// <param name="minStrike">Minimum strike price to evaluate.</param>
    /// <param name="maxStrike">Maximum strike price to evaluate.</param>
    /// <returns>List of optimal percentage points for triggering iron condor positions.</returns>
    public BackTestResult FindOptimalTriggerPercentages(List<PriceBar> weeklyPriceBars, double minStrike, double maxStrike)
    {
        _logger.LogInfo("Starting backtest analysis...");

        List<double> percents = new List<double>();

        int min = TradingParameters.MinPercentage;
        int max = TradingParameters.MaxPercentage;
        int startPercentage = min - 10;

        // Test percentages from 0 to 99
        for (int p = startPercentage; p < max + 10; p += 1)
        {
            if (p >= min && p <= max)
            {
                int success = 0;
                int total = 0;

                // Backtest over last 50 weeks
                for (int j = weeklyPriceBars.Count - TradingParameters.BackTestWindowSize; j < weeklyPriceBars.Count; j++)
                {
                    PriceBar thisWeekUnderlyingBar = weeklyPriceBars[j - 1];

                    double triggerPrice = CalculateTriggerPriceForPercentage(weeklyPriceBars.GetRange(0, j), minStrike, maxStrike, p);

                    // Check if trigger price was hit during the week
                    if (triggerPrice >= thisWeekUnderlyingBar.Low && triggerPrice <= thisWeekUnderlyingBar.High)
                    {
                        double actualSpread = Math.Abs(thisWeekUnderlyingBar.Close - triggerPrice);
                        if (actualSpread < TradingParameters.MinSpreadForSuccess * TradingParameters.Spread)
                        {
                            success++;
                        }

                        total++;
                    }
                }

                double actualChance = total > 0 ? 100 * (double)success / total : 0;
                percents.Add(actualChance);
                _logger.LogInfo($"actualChance[{p}]={actualChance:F1}");
            }
            else
            {
                percents.Add(0);
            }
        }

        // Interpolate duplicate consecutive values and track which indices were interpolated
        var interpolatedIndices = DeduplicateByValue(percents);

        double[] forwardSteps = CalculateForwardIsolationDistance(percents);
        double[] backwardSteps = CalculateBackwardIsolationDistance(percents);

        // Create combined array with index, all three values, and distance from perfect isolation (100,100)
        var combined = Enumerable.Range(0, percents.Count)
            .Select(i => new {
                Index = i,
                B = backwardSteps[i] * 100,  // Convert to percentage
                F = forwardSteps[i] * 100,   // Convert to percentage
                P = percents[i],
                D = Math.Sqrt(Math.Pow(100 - backwardSteps[i] * 100, 2) + Math.Pow(100 - forwardSteps[i] * 100, 2))
            })
            .ToList();

        // Order by distance from perfect isolation (100,100) - smaller is better
        var top10ByDistance = combined
            .OrderBy(x => x.D)
            .Take(10)
            .ToList();

        // Convert to actual percentage values
        var interestingPoints = top10ByDistance.Select(x => startPercentage + x.Index).ToList();

        _logger.LogInfo($"Top 10 by distance from (100,100): {combined.Count} → {top10ByDistance.Count}");
        int rank = 1;
        foreach (var item in top10ByDistance)
        {
            int percentage = startPercentage + item.Index;
            _logger.LogInfo($"  #{rank++}: {percentage}% (p={item.P:F1}, b={item.B:F1}%, f={item.F:F1}%, d={item.D:F1})");
        }

        return new BackTestResult
        {
            StartPercentage = startPercentage,
            Percents = percents,
            ForwardSteps = forwardSteps,
            BackwardSteps = backwardSteps,
            InterestingPoints = interestingPoints,
            InterpolatedIndices = interpolatedIndices
        };
    }

    /// <summary>
    /// Interpolates consecutive duplicate values in-place to the next different value.
    /// Example: [50, 50, 50, 50, 70] → [50, 55, 60, 65, 70]
    /// Returns the set of indices that were interpolated.
    /// </summary>
    private HashSet<int> DeduplicateByValue(List<double> percents)
    {
        var interpolatedIndices = new HashSet<int>();

        if (percents.Count == 0) return interpolatedIndices;

        int plateauStart = 0;

        for (int i = 1; i <= percents.Count; i++)
        {
            bool isEnd = i == percents.Count;
            bool isDifferentValue = !isEnd && Math.Abs(percents[i] - percents[plateauStart]) > 0.01;

            if (isEnd || isDifferentValue)
            {
                int plateauLength = i - plateauStart;

                if (plateauLength > 1)
                {
                    // Find the next different value (target for interpolation)
                    double startValue = percents[plateauStart];
                    double endValue = isEnd ? startValue : percents[i];

                    // Interpolate values between plateauStart and i (exclusive)
                    for (int j = plateauStart + 1; j < i; j++)
                    {
                        // Linear interpolation: startValue + (endValue - startValue) * (position / totalSteps)
                        double t = (double)(j - plateauStart) / plateauLength;
                        percents[j] = startValue + (endValue - startValue) * t;
                        interpolatedIndices.Add(j);
                    }
                }

                plateauStart = i;
            }
        }

        _logger.LogInfo($"Interpolated: {percents.Count} total, {interpolatedIndices.Count} values interpolated");
        return interpolatedIndices;
    }

    /// <summary>
    /// Calculates the relative distance to the first higher value going forward (right).
    /// Returns ratio of steps to available forward range (0.0 to 1.0).
    /// Used to measure how isolated a peak is from higher values ahead.
    /// </summary>
    public double[] CalculateForwardIsolationDistance(List<double> percents)
    {
        if (percents == null || percents.Count == 0)
            return Array.Empty<double>();

        int totalCount = percents.Count;
        double[] relativeSteps = new double[totalCount];

        for (int i = 0; i < totalCount; i++)
        {
            int availableForward = totalCount - i;

            // Edge case: last index has no forward range
            if (availableForward <= 0)
            {
                relativeSteps[i] = 0.0;
                continue;
            }

            int absoluteSteps = totalCount - i; // Default: distance to end if no higher point found

            // Search for the first higher point to the right
            for (int j = i + 1; j < totalCount; j++)
            {
                if (percents[j] > percents[i])
                {
                    absoluteSteps = j - i;
                    break;
                }
            }

            relativeSteps[i] = (double)absoluteSteps / availableForward;
        }

        return relativeSteps;
    }

    /// <summary>
    /// Calculates the relative distance to the first higher value going backward (left).
    /// Returns ratio of steps to available backward range (0.0 to 1.0).
    /// Used to measure how isolated a peak is from higher values behind.
    /// </summary>
    public double[] CalculateBackwardIsolationDistance(List<double> percents)
    {
        if (percents == null || percents.Count == 0)
            return Array.Empty<double>();

        double[] relativeSteps = new double[percents.Count];

        for (int i = 0; i < percents.Count; i++)
        {
            int availableBackward = i;

            // Edge case: index 0 has no backward range
            if (availableBackward <= 0)
            {
                relativeSteps[i] = 0.0;
                continue;
            }

            int absoluteSteps = i; // Default: distance to start if no higher point found

            // Search for the first higher point to the left
            for (int j = i - 1; j >= 0; j--)
            {
                if (percents[j] > percents[i])
                {
                    absoluteSteps = i - j;
                    break;
                }
            }

            relativeSteps[i] = (double)absoluteSteps / availableBackward;
        }

        return relativeSteps;
    }

    /// <summary>
    /// Calculates the trigger price for a given target success percentage during backtest.
    /// </summary>
    /// <param name="weeklyPriceBars">Historical weekly price bar data.</param>
    /// <param name="minStrike">Minimum strike price to evaluate.</param>
    /// <param name="maxStrike">Maximum strike price to evaluate.</param>
    /// <param name="targetProbability">Target success probability percentage.</param>
    /// <returns>The calculated trigger price, or 0 if no valid trigger found.</returns>
    public double CalculateTriggerPriceForPercentage(List<PriceBar> weeklyPriceBars, double minStrike, double maxStrike, double targetProbability)
    {
        // Initialize
        List<double> closeChangesFromMin = new List<double>();
        List<double> closeChangesFromMax = new List<double>();

        StrikeProbabilityMap closeChancesFromMinPointPair = new StrikeProbabilityMap();
        StrikeProbabilityMap closeChancesFromMaxPointPair = new StrikeProbabilityMap();

        PriceBar thisWeekUnderlyingBar = weeklyPriceBars[weeklyPriceBars.Count - 1];
        PriceBar lastWeekUnderlyingBar = weeklyPriceBars[weeklyPriceBars.Count - 2];

        string gap = string.Empty;

        double max = Math.Max(lastWeekUnderlyingBar.Open, lastWeekUnderlyingBar.Close);
        double min = Math.Min(lastWeekUnderlyingBar.Open, lastWeekUnderlyingBar.Close);

        // Determine GAP type
        if (thisWeekUnderlyingBar.Open < min)
            gap = GapType.Down;
        else if (thisWeekUnderlyingBar.Open > max)
            gap = GapType.Up;
        else if (thisWeekUnderlyingBar.Open >= min && thisWeekUnderlyingBar.Open <= max)
            gap = GapType.No;

        // Calculate changes based on GAP type
        for (int i = 1; i < weeklyPriceBars.Count; i++)
        {
            double weekMax = Math.Max(weeklyPriceBars[i - 1].Open, weeklyPriceBars[i - 1].Close);
            double weekMin = Math.Min(weeklyPriceBars[i - 1].Open, weeklyPriceBars[i - 1].Close);

            if (gap == GapType.Down)
            {
                if (weeklyPriceBars[i].Open < weekMin)
                {
                    closeChangesFromMin.Add((weeklyPriceBars[i].Close - weekMin) / weekMin);
                    closeChangesFromMax.Add((weeklyPriceBars[i].Close - weekMax) / weekMax);
                }
            }
            else if (gap == GapType.Up)
            {
                if (weeklyPriceBars[i].Open > weekMax)
                {
                    closeChangesFromMin.Add((weeklyPriceBars[i].Close - weekMin) / weekMin);
                    closeChangesFromMax.Add((weeklyPriceBars[i].Close - weekMax) / weekMax);
                }
            }
            else if (gap == GapType.No)
            {
                if (weeklyPriceBars[i].Open >= weekMin && weeklyPriceBars[i].Open <= weekMax)
                {
                    closeChangesFromMin.Add((weeklyPriceBars[i].Close - weekMin) / weekMin);
                    closeChangesFromMax.Add((weeklyPriceBars[i].Close - weekMax) / weekMax);
                }
            }
        }

        // Calculate chances for each strike (step = 1)
        for (double strike = minStrike; strike <= maxStrike; strike += 1)
        {
            double breakEvenFromMin = (strike - min) / min;
            double breakEvenFromMax = (strike - max) / max;

            int successCloseFromMin = 0;
            int failureCloseFromMin = 0;

            if (breakEvenFromMin != 0)
            {
                successCloseFromMin = closeChangesFromMin.Count(d => d < breakEvenFromMin);
                failureCloseFromMin = closeChangesFromMin.Count(d => d >= breakEvenFromMin);
            }

            int successCloseFromMax = 0;
            int failureCloseFromMax = 0;

            if (breakEvenFromMax != 0)
            {
                successCloseFromMax = closeChangesFromMax.Count(d => d < breakEvenFromMax);
                failureCloseFromMax = closeChangesFromMax.Count(d => d >= breakEvenFromMax);
            }

            if (breakEvenFromMin != 0 && breakEvenFromMax != 0)
            {
                if (successCloseFromMin + failureCloseFromMin > 0 && successCloseFromMax + failureCloseFromMax > 0)
                {
                    double closeFromMinChance = (double)100 * successCloseFromMin / (double)(successCloseFromMin + failureCloseFromMin);
                    double closeFromMaxChance = (double)100 * successCloseFromMax / (double)(successCloseFromMax + failureCloseFromMax);

                    closeChancesFromMinPointPair.Add(strike, closeFromMinChance);
                    closeChancesFromMaxPointPair.Add(strike, closeFromMaxChance);
                }
            }
        }

        // Find target strikes
        var targetFromMaxPointPair = closeChancesFromMaxPointPair.FirstOrDefault(p => p.Probability > targetProbability);
        var targetFromMinPointPair = closeChancesFromMinPointPair.FirstOrDefault(p => p.Probability > targetProbability);

        if (targetFromMaxPointPair == null || targetFromMinPointPair == null)
            return 0;

        double minTargetForCall = Math.Min(targetFromMaxPointPair.Strike, targetFromMinPointPair.Strike);
        double maxTargetForPut = Math.Max(targetFromMaxPointPair.Strike, targetFromMinPointPair.Strike);

        double triggerPrice = (minTargetForCall + maxTargetForPut) / 2;

        return triggerPrice;
    }
}

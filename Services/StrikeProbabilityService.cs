using IBApi;
using IronCondor.Infrastructure;
using IronCondor.Models;

namespace IronCondor.Services;

/// <summary>
/// Service for calculating strike probabilities for iron condor strategies.
/// </summary>
public class StrikeProbabilityService
{
    private readonly ILogger _logger;
    private readonly StrikeProbabilityMap _strikeProbabilitiesFromWeekLow;
    private readonly StrikeProbabilityMap _strikeProbabilitiesFromWeekHigh;

    public StrikeProbabilityService(ILogger logger)
    {
        _logger = logger;
        _strikeProbabilitiesFromWeekLow = new StrikeProbabilityMap();
        _strikeProbabilitiesFromWeekHigh = new StrikeProbabilityMap();
    }

    /// <summary>
    /// Gets the strike probability map calculated from the week's low price.
    /// </summary>
    public StrikeProbabilityMap StrikeProbabilitiesFromWeekLow => _strikeProbabilitiesFromWeekLow;

    /// <summary>
    /// Gets the strike probability map calculated from the week's high price.
    /// </summary>
    public StrikeProbabilityMap StrikeProbabilitiesFromWeekHigh => _strikeProbabilitiesFromWeekHigh;

    /// <summary>
    /// Calculates strike probabilities for the current week based on historical patterns.
    /// </summary>
    /// <param name="weeklyPriceBars">Historical weekly price bar data.</param>
    /// <param name="optionContracts">List of option contracts.</param>
    /// <param name="thisWeekBar">Current week's price bar.</param>
    /// <param name="lastWeekBar">Last week's price bar.</param>
    /// <returns>Tuple containing gap type and filtered contracts.</returns>
    public (string GapType, List<Contract> Contracts) CalculateStrikeProbabilities(
        List<PriceBar> weeklyPriceBars,
        List<Contract> optionContracts,
        PriceBar thisWeekBar,
        PriceBar lastWeekBar)
    {
        _logger.LogInfo("Calculating strike probabilities...");

        // Initialize
        List<double> closeChangesFromMin = new List<double>();
        List<double> closeChangesFromMax = new List<double>();

        _strikeProbabilitiesFromWeekLow.Clear();
        _strikeProbabilitiesFromWeekHigh.Clear();

        string gap = string.Empty;

        double max = Math.Max(lastWeekBar.Open, lastWeekBar.Close);
        double min = Math.Min(lastWeekBar.Open, lastWeekBar.Close);

        // Determine GAP type
        if (thisWeekBar.Open < min)
            gap = GapType.Down;
        else if (thisWeekBar.Open > max)
            gap = GapType.Up;
        else if (thisWeekBar.Open >= min && thisWeekBar.Open <= max)
            gap = GapType.No;

        _logger.LogInfo($"GAP type: {gap}");

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

        // Calculate chances for each strike
        List<Contract> contracts = new List<Contract>();

        for (int i = 0; i < optionContracts.Count; i++)
        {
            double breakEvenFromMin = (optionContracts[i].Strike - min) / min;
            double breakEvenFromMax = (optionContracts[i].Strike - max) / max;

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

                    _strikeProbabilitiesFromWeekLow.Add(optionContracts[i].Strike, closeFromMinChance);
                    _strikeProbabilitiesFromWeekHigh.Add(optionContracts[i].Strike, closeFromMaxChance);

                    contracts.Add(optionContracts[i]);
                }
            }
        }

        _logger.LogSuccess($"Calculated probabilities for {contracts.Count} strikes");

        return (gap, contracts);
    }
}



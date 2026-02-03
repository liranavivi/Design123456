using IronCondor.Constants;
using IronCondor.Infrastructure;
using IronCondor.Models;

namespace IronCondor.Services;

/// <summary>
/// Service for detecting and resolving conflicts between iron condor positions.
/// Conflicts occur when two trigger prices differ by exactly the Spread value.
/// </summary>
public class ConflictDetectionService
{
    private readonly ILogger _logger;

    public ConflictDetectionService(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Detects conflicts based on trigger price distance.
    /// Conflict occurs when |T1 - T2| == Spread.
    /// </summary>
    /// <param name="triggerPrices">List of trigger prices.</param>
    /// <returns>List of conflicting trigger price pairs (low, high).</returns>
    public List<(double Low, double High)> DetectConflictsByTriggerPrice(List<double> triggerPrices)
    {
        var conflicts = new List<(double, double)>();
        var sorted = triggerPrices.OrderBy(t => t).ToList();

        for (int i = 0; i < sorted.Count; i++)
        {
            for (int j = i + 1; j < sorted.Count; j++)
            {
                if (Math.Abs(sorted[j] - sorted[i]) == TradingParameters.Spread)
                {
                    conflicts.Add((sorted[i], sorted[j]));
                }
            }
        }

        return conflicts;
    }

    /// <summary>
    /// Resolves conflicts by adjusting trigger prices with minimum changes.
    /// Uses greedy approach: adjust trigger with most conflicts first.
    /// </summary>
    /// <param name="triggerPrices">List of trigger prices.</param>
    /// <returns>Dictionary mapping original trigger prices to adjusted values.</returns>
    public Dictionary<double, double> ResolveConflicts(List<double> triggerPrices)
    {
        var adjustments = new Dictionary<double, double>();
        var adjusted = triggerPrices.OrderBy(t => t).ToList();
        var original = triggerPrices.OrderBy(t => t).ToList();

        int maxIterations = 10; // Safety limit (more than enough for ≤5 triggers)

        for (int iteration = 0; iteration < maxIterations; iteration++)
        {
            // Find all current conflicts
            var conflicts = DetectConflictsByTriggerPrice(adjusted);

            if (conflicts.Count == 0)
                break; // All resolved

            // Count conflicts per trigger
            var conflictCounts = new Dictionary<double, int>();
            foreach (var t in adjusted)
                conflictCounts[t] = 0;

            foreach (var (low, high) in conflicts)
            {
                conflictCounts[low]++;
                conflictCounts[high]++;
            }

            // Find trigger with highest conflict count (prioritize middle positions)
            var toAdjust = conflictCounts
                .Where(c => c.Value > 0)
                .OrderByDescending(c => c.Value)
                .First().Key;

            int idx = adjusted.IndexOf(toAdjust);

            // Choose direction with fewer new conflicts
            double upCandidate = toAdjust + 1;
            double downCandidate = toAdjust - 1;

            int upConflicts = CountConflictsIfAdjusted(adjusted, idx, upCandidate);
            int downConflicts = CountConflictsIfAdjusted(adjusted, idx, downCandidate);

            double newValue;
            if (upConflicts < downConflicts)
            {
                newValue = upCandidate;
            }
            else if (downConflicts < upConflicts)
            {
                newValue = downCandidate;
            }
            else
            {
                // Equal conflicts: use heuristic - move away from other triggers (maximize min distance)
                newValue = ChooseDirectionByMaxSpacing(adjusted, idx, upCandidate, downCandidate);
            }

            // Apply adjustment
            adjusted[idx] = newValue;
        }

        // Build adjustment map (original → new)
        for (int i = 0; i < original.Count; i++)
        {
            if (Math.Abs(original[i] - adjusted[i]) > 0.001) // Not equal (floating point safe)
            {
                adjustments[original[i]] = adjusted[i];
            }
        }

        return adjustments;
    }

    /// <summary>
    /// Counts how many conflicts would exist if a trigger is adjusted to a new value.
    /// </summary>
    private int CountConflictsIfAdjusted(List<double> triggers, int indexToChange, double newValue)
    {
        int conflicts = 0;
        for (int i = 0; i < triggers.Count; i++)
        {
            if (i == indexToChange) continue;
            if (Math.Abs(triggers[i] - newValue) == TradingParameters.Spread)
                conflicts++;
        }
        return conflicts;
    }

    /// <summary>
    /// Chooses adjustment direction by maximizing the minimum distance to other triggers.
    /// This heuristic moves the trigger away from its neighbors to maximize spacing.
    /// </summary>
    private double ChooseDirectionByMaxSpacing(List<double> triggers, int indexToChange, double upCandidate, double downCandidate)
    {
        double minDistanceUp = double.MaxValue;
        double minDistanceDown = double.MaxValue;

        for (int i = 0; i < triggers.Count; i++)
        {
            if (i == indexToChange) continue;

            double distToUp = Math.Abs(triggers[i] - upCandidate);
            double distToDown = Math.Abs(triggers[i] - downCandidate);

            if (distToUp < minDistanceUp)
                minDistanceUp = distToUp;

            if (distToDown < minDistanceDown)
                minDistanceDown = distToDown;
        }

        // Choose direction that maximizes the minimum distance to any neighbor
        return minDistanceDown >= minDistanceUp ? downCandidate : upCandidate;
    }

    /// <summary>
    /// Displays conflicts and their resolutions to the user.
    /// </summary>
    public void DisplayConflictResolution(
        List<double> originalTriggers,
        List<(double Low, double High)> conflicts,
        Dictionary<double, double> adjustments)
    {
        var sorted = originalTriggers.OrderBy(t => t).ToList();

        Console.WriteLine();
        _logger.LogInfo($"Trigger Prices: {string.Join(", ", sorted.Select(t => t.ToString("F0")))}");

        if (conflicts.Count == 0)
        {
            Console.WriteLine();
            _logger.LogSuccess("No conflicts detected.");
            return;
        }

        // Display original conflicts
        Console.WriteLine();
        _logger.LogWarning($"Conflicts Detected ({conflicts.Count}):");
        foreach (var (low, high) in conflicts)
        {
            Console.WriteLine($"      • {low:F0} ↔ {high:F0}  (difference = {TradingParameters.Spread})");
        }

        // Display resolutions
        if (adjustments.Count > 0)
        {
            Console.WriteLine();
            _logger.LogInfo($"Resolved with {adjustments.Count} adjustment(s):");
            foreach (var adj in adjustments.OrderBy(a => a.Key))
            {
                var diff = adj.Value - adj.Key;
                var direction = diff > 0 ? "+" : "";
                Console.WriteLine($"      • {adj.Key:F0} → {adj.Value:F0}  ({direction}{diff:F0})");
            }

            // Display final trigger prices
            var resolved = originalTriggers
                .Select(t => adjustments.ContainsKey(t) ? adjustments[t] : t)
                .OrderBy(t => t);

            Console.WriteLine();
            _logger.LogSuccess($"Resolved Trigger Prices: {string.Join(", ", resolved.Select(t => t.ToString("F0")))}");
        }
    }

    /// <summary>
    /// Detects all conflicts between iron condor positions and returns detailed conflict information.
    /// </summary>
    /// <param name="positions">List of trigger prices and their iron condor strike prices.</param>
    /// <returns>List of conflict details.</returns>
    public List<ConflictInfo> DetectConflicts(List<KeyValuePair<double, IronCondorStrikes>> positions)
    {
        var conflicts = new List<ConflictInfo>();

        // Check for conflicts between all pairs
        for (int i = 0; i < positions.Count; i++)
        {
            var outer = positions[i];

            for (int j = i + 1; j < positions.Count; j++)
            {
                var inner = positions[j];

                // Check CALL conflicts (symmetric)
                if (outer.Value.CallSell == inner.Value.CallBuy)
                {
                    conflicts.Add(new ConflictInfo(outer.Key, inner.Key, "Call", outer.Value.CallSell));
                }
                else if (outer.Value.CallBuy == inner.Value.CallSell)
                {
                    conflicts.Add(new ConflictInfo(outer.Key, inner.Key, "Call", outer.Value.CallBuy));
                }

                // Check PUT conflicts (symmetric)
                if (outer.Value.PutSell == inner.Value.PutBuy)
                {
                    conflicts.Add(new ConflictInfo(outer.Key, inner.Key, "Put", outer.Value.PutSell));
                }
                else if (outer.Value.PutBuy == inner.Value.PutSell)
                {
                    conflicts.Add(new ConflictInfo(outer.Key, inner.Key, "Put", outer.Value.PutBuy));
                }
            }
        }

        return conflicts;
    }

    /// <summary>
    /// Finds overlapping strikes between iron condor positions that would create conflicts.
    /// </summary>
    /// <param name="positions">List of trigger prices and their iron condor strike prices.</param>
    /// <returns>Set of trigger prices to remove due to overlapping strikes.</returns>
    public HashSet<double> FindOverlappingStrikes(List<KeyValuePair<double, IronCondorStrikes>> positions)
    {
        var keysToRemove = new HashSet<double>();

        // Check for conflicts between all pairs
        for (int i = 0; i < positions.Count; i++)
        {
            var outer = positions[i];

            for (int j = i + 1; j < positions.Count; j++)  // Only check higher indices
            {
                var inner = positions[j];

                // Check CALL conflicts (symmetric)
                bool hasCallConflict =
                    outer.Value.CallSell == inner.Value.CallBuy ||  // outer SELL vs inner BUY
                    outer.Value.CallBuy == inner.Value.CallSell;    // outer BUY vs inner SELL

                // Check PUT conflicts (symmetric)
                bool hasPutConflict =
                    outer.Value.PutSell == inner.Value.PutBuy ||  // outer SELL vs inner BUY
                    outer.Value.PutBuy == inner.Value.PutSell;    // outer BUY vs inner SELL

                if (hasCallConflict)
                {
                    _logger.LogWarning($"⚠️  Call conflict: Index {i} ({outer.Key}) vs Index {j} ({inner.Key})");
                    _logger.LogWarning($"    → Removing higher index {j} ({inner.Key})");
                    keysToRemove.Add(inner.Key);
                }

                if (hasPutConflict)
                {
                    _logger.LogWarning($"⚠️  Put conflict: Index {i} ({outer.Key}) vs Index {j} ({inner.Key})");
                    _logger.LogWarning($"    → Removing higher index {j} ({inner.Key})");
                    keysToRemove.Add(inner.Key);
                }
            }
        }

        if (keysToRemove.Count > 0)
        {
            _logger.LogInfo($"Detected {keysToRemove.Count} conflicts");
        }
        else
        {
            _logger.LogInfo("No conflicts detected");
        }

        return keysToRemove;
    }

    /// <summary>
    /// Validates if all 4 legs of an iron condor exist in the available strikes.
    /// </summary>
    /// <param name="triggerPrice">The trigger price for the iron condor.</param>
    /// <param name="availableStrikes">Set of available strikes from IBKR.</param>
    /// <returns>Tuple of (allExist, missingStrikes).</returns>
    public (bool AllExist, List<double> MissingStrikes) ValidateLegsExist(double triggerPrice, HashSet<double> availableStrikes)
    {
        var strikes = new IronCondorStrikes(
            callSell: triggerPrice + TradingParameters.Spread,
            callBuy: triggerPrice + (2 * TradingParameters.Spread),
            putSell: triggerPrice - TradingParameters.Spread,
            putBuy: triggerPrice - (2 * TradingParameters.Spread),
            triggerPrice: triggerPrice
        );

        var requiredStrikes = new List<double> { strikes.CallSell, strikes.CallBuy, strikes.PutSell, strikes.PutBuy };
        var missingStrikes = requiredStrikes.Where(s => !availableStrikes.Contains(s)).ToList();

        return (missingStrikes.Count == 0, missingStrikes);
    }

    /// <summary>
    /// Validates a trigger price for both leg existence and conflicts.
    /// </summary>
    /// <param name="triggerPrice">The trigger price to validate.</param>
    /// <param name="availableStrikes">Set of available strikes from IBKR.</param>
    /// <param name="existingTriggers">List of already validated trigger prices.</param>
    /// <returns>Tuple of (isValid, missingStrikes, conflictingTriggers).</returns>
    public (bool IsValid, List<double> MissingStrikes, List<double> ConflictingTriggers) ValidateTriggerPrice(
        double triggerPrice,
        HashSet<double> availableStrikes,
        List<double> existingTriggers)
    {
        // Check legs exist
        var (allExist, missingStrikes) = ValidateLegsExist(triggerPrice, availableStrikes);

        // Check conflicts with existing triggers
        var conflictingTriggers = new List<double>();
        foreach (var existing in existingTriggers)
        {
            if (Math.Abs(triggerPrice - existing) == TradingParameters.Spread)
            {
                conflictingTriggers.Add(existing);
            }
        }

        bool isValid = allExist && conflictingTriggers.Count == 0;
        return (isValid, missingStrikes, conflictingTriggers);
    }

}


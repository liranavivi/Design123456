using IronCondor.Constants;
using IronCondor.Infrastructure;
using IronCondor.Models;

namespace IronCondor.Services;

/// <summary>
/// Service for handling user interaction and selection of iron condor positions.
/// </summary>
public class UserInteractionService
{
    private readonly ILogger _logger;

    public UserInteractionService(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Prompts user to enter the number of interesting points to use.
    /// </summary>
    /// <param name="interestingPoints">List of all found interesting points.</param>
    /// <returns>Number of points selected by user.</returns>
    public int GetNumberOfPointsToUse(List<int> interestingPoints)
    {
        Console.WriteLine();
        _logger.LogInfo($"Found {interestingPoints.Count} optimal trigger percentages:");

        for (int i = 0; i < interestingPoints.Count; i++)
        {
            Console.WriteLine($"  [{i + 1}] {interestingPoints[i]}%");
        }

        Console.WriteLine();
        Console.Write($"  Enter number of points to use (1-{interestingPoints.Count}), or press Enter for all: ");

        while (true)
        {
            var input = Console.ReadLine()?.Trim();

            // Default to all if empty
            if (string.IsNullOrEmpty(input))
            {
                _logger.LogSuccess($"Using all {interestingPoints.Count} points");
                return interestingPoints.Count;
            }

            if (int.TryParse(input, out int count))
            {
                if (count >= 1 && count <= interestingPoints.Count)
                {
                    _logger.LogSuccess($"Using top {count} point(s)");
                    return count;
                }
                _logger.LogError($"Invalid number. Please enter a value between 1 and {interestingPoints.Count}.");
            }
            else
            {
                _logger.LogError("Invalid input. Please enter a number.");
            }

            Console.Write($"  Enter number of points to use (1-{interestingPoints.Count}): ");
        }
    }

    /// <summary>
    /// Displays interesting points with original triggers, conflicts, resolved triggers,
    /// and gets user selection from the resolved triggers only.
    /// </summary>
    /// <param name="originalTriggers">Original trigger prices from interesting points.</param>
    /// <param name="conflicts">List of conflict pairs (trigger prices that differ by Spread).</param>
    /// <param name="adjustments">Dictionary mapping original triggers to adjusted values.</param>
    /// <returns>List of selected trigger prices (from resolved triggers).</returns>
    public List<double> GetUserSelectionFromResolvedTriggers(
        List<double> originalTriggers,
        List<(double Low, double High)> conflicts,
        Dictionary<double, double> adjustments)
    {
        var sortedOriginal = originalTriggers.OrderBy(t => t).ToList();

        // Calculate resolved triggers
        var resolvedTriggers = sortedOriginal
            .Select(t => adjustments.ContainsKey(t) ? adjustments[t] : t)
            .OrderBy(t => t)
            .ToList();

        // Section 1: Display Interesting Points (Original Triggers) - trigger prices only
        Console.WriteLine();
        _logger.LogInfo("Interesting Points (Original Trigger Prices):");
        Console.WriteLine($"      {string.Join(", ", sortedOriginal.Select(t => $"{t:F0}"))}");

        // Section 2: Display Conflicts
        if (conflicts.Count > 0)
        {
            Console.WriteLine();
            _logger.LogWarning($"Conflicts Detected ({conflicts.Count}):");
            foreach (var (low, high) in conflicts)
            {
                Console.WriteLine($"      • {low:F0} ↔ {high:F0}  (difference = {TradingParameters.Spread})");
            }

            // Section 3: Display Adjustments
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
            }
        }
        else
        {
            Console.WriteLine();
            _logger.LogSuccess("No conflicts detected.");
        }

        // Section 4: Display Suggested Resolved Triggers - trigger prices only
        Console.WriteLine();
        _logger.LogInfo("Suggested Trigger Prices (no conflicts):");
        Console.WriteLine($"      {string.Join(", ", resolvedTriggers.Select(t => $"{t:F0}"))}");

        // Get user selection - user enters actual trigger prices
        Console.WriteLine();
        Console.Write("  Enter trigger prices (e.g., 790,783,668) or 'all' for suggested: ");

        while (true)
        {
            var input = Console.ReadLine()?.Trim();

            if (string.IsNullOrEmpty(input))
            {
                _logger.LogWarning("No selection made. Please enter trigger prices.");
                Console.Write("  Enter trigger prices: ");
                continue;
            }

            // Handle 'all' selection - use all resolved triggers
            if (input.ToLower() == "all")
            {
                _logger.LogSuccess($"Selected all suggested triggers: {string.Join(", ", resolvedTriggers.Select(t => $"{t:F0}"))}");
                return resolvedTriggers;
            }

            // Parse comma-separated trigger prices
            var selectedTriggers = new List<double>();
            var invalidInputs = new List<string>();
            var parts = input.Split(',', StringSplitOptions.RemoveEmptyEntries);

            foreach (var part in parts)
            {
                if (double.TryParse(part.Trim(), out double triggerPrice))
                {
                    selectedTriggers.Add(triggerPrice);
                }
                else
                {
                    invalidInputs.Add(part.Trim());
                }
            }

            // Check for invalid inputs (non-numeric)
            if (invalidInputs.Count > 0)
            {
                _logger.LogError($"Invalid input(s): {string.Join(", ", invalidInputs)}. Enter numeric trigger prices.");
                Console.Write("  Enter trigger prices: ");
                continue;
            }

            if (selectedTriggers.Count == 0)
            {
                _logger.LogWarning("No valid trigger prices entered. Please try again.");
                Console.Write("  Enter trigger prices: ");
                continue;
            }

            // Validation: check for duplicates and conflicts, report all issues
            var hasErrors = false;
            var uniqueTriggers = selectedTriggers.Distinct().OrderBy(t => t).ToList();

            // Check for duplicates
            if (uniqueTriggers.Count < selectedTriggers.Count)
            {
                var duplicates = selectedTriggers
                    .GroupBy(t => t)
                    .Where(g => g.Count() > 1)
                    .Select(g => g.Key)
                    .ToList();
                _logger.LogError($"Duplicate trigger prices: {string.Join(", ", duplicates.Select(t => $"{t:F0}"))}");
                hasErrors = true;
            }

            // Check for conflicts
            var userConflicts = DetectConflictsInSelection(uniqueTriggers);
            if (userConflicts.Count > 0)
            {
                _logger.LogError($"Conflicts detected ({userConflicts.Count}):");
                foreach (var (low, high) in userConflicts)
                {
                    Console.WriteLine($"      • {low:F0} ↔ {high:F0}  (difference = {TradingParameters.Spread})");
                }
                hasErrors = true;
            }

            // If any errors, prompt again
            if (hasErrors)
            {
                Console.WriteLine("  Please fix the issues and try again.");
                Console.Write("  Enter trigger prices: ");
                continue;
            }

            // All valid - proceed
            _logger.LogSuccess($"Selected {uniqueTriggers.Count} trigger(s): {string.Join(", ", uniqueTriggers.Select(t => $"{t:F0}"))}");
            return uniqueTriggers;
        }
    }

    /// <summary>
    /// Detects conflicts within a list of trigger prices.
    /// Conflict occurs when |T1 - T2| == Spread.
    /// </summary>
    private static List<(double Low, double High)> DetectConflictsInSelection(List<double> triggerPrices)
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
    /// Calculates iron condor strikes from a trigger price.
    /// </summary>
    private static (double CallSell, double CallBuy, double PutSell, double PutBuy) CalculateStrikes(double triggerPrice)
    {
        return (
            CallSell: triggerPrice + TradingParameters.Spread,
            CallBuy: triggerPrice + (2 * TradingParameters.Spread),
            PutSell: triggerPrice - TradingParameters.Spread,
            PutBuy: triggerPrice - (2 * TradingParameters.Spread)
        );
    }

    /// <summary>
    /// Displays interesting points with their strikes and conflicts, then gets user selection.
    /// </summary>
    /// <param name="positions">All iron condor positions keyed by trigger price.</param>
    /// <param name="conflicts">List of conflict descriptions.</param>
    /// <returns>List of selected trigger prices.</returns>
    [Obsolete("Use GetUserSelectionFromResolvedTriggers instead")]
    public List<double> GetUserSelection(
        Dictionary<double, IronCondorStrikes> positions,
        List<ConflictInfo> conflicts)
    {
        // Display header
        Console.WriteLine();
        _logger.LogInfo("Interesting Points (Trigger Percentages):");
        Console.WriteLine();

        // Create indexed list for display
        var positionList = positions.OrderBy(p => p.Key).ToList();
        var indexToTrigger = new Dictionary<int, double>();

        // Display each position with strikes
        for (int i = 0; i < positionList.Count; i++)
        {
            var pos = positionList[i];
            int displayIndex = i + 1;
            indexToTrigger[displayIndex] = pos.Key;

            Console.WriteLine($"  [{displayIndex}] {pos.Key:F0}%  │  CallSell={pos.Value.CallSell:F0}  CallBuy={pos.Value.CallBuy:F0}  │  PutSell={pos.Value.PutSell:F0}  PutBuy={pos.Value.PutBuy:F0}");
        }

        // Display conflicts if any
        if (conflicts.Count > 0)
        {
            Console.WriteLine();
            _logger.LogWarning($"Conflicts detected ({conflicts.Count}):");
            foreach (var conflict in conflicts)
            {
                int idx1 = positionList.FindIndex(p => p.Key == conflict.TriggerPrice1) + 1;
                int idx2 = positionList.FindIndex(p => p.Key == conflict.TriggerPrice2) + 1;
                Console.WriteLine($"      • [{idx1}] {conflict.TriggerPrice1:F0}% ↔ [{idx2}] {conflict.TriggerPrice2:F0}%  →  {conflict.ConflictType} conflict at strike {conflict.ConflictingStrike:F0}");
            }
        }
        else
        {
            Console.WriteLine();
            _logger.LogSuccess("No conflicts detected between positions.");
        }

        // Get user selection
        Console.WriteLine();
        Console.Write("  Enter your selection (e.g., 1,3,5 or 'all'): ");

        while (true)
        {
            var input = Console.ReadLine()?.Trim().ToLower();

            if (string.IsNullOrEmpty(input))
            {
                _logger.LogWarning("No selection made. Please enter at least one index.");
                Console.Write("  Enter your selection: ");
                continue;
            }

            // Handle 'all' selection
            if (input == "all")
            {
                var allTriggers = positionList.Select(p => p.Key).ToList();
                var allConflicts = GetConflictsForSelection(allTriggers, conflicts);

                if (allConflicts.Count > 0)
                {
                    _logger.LogError($"Selection 'all' has {allConflicts.Count} conflict(s). Please select specific points to avoid conflicts.");
                    Console.Write("  Enter your selection: ");
                    continue;
                }

                return allTriggers;
            }

            // Parse comma-separated indices
            var selectedTriggers = new List<double>();
            var invalidInputs = new List<string>();
            var parts = input.Split(',', StringSplitOptions.RemoveEmptyEntries);

            foreach (var part in parts)
            {
                if (int.TryParse(part.Trim(), out int idx) && indexToTrigger.ContainsKey(idx))
                {
                    selectedTriggers.Add(indexToTrigger[idx]);
                }
                else
                {
                    invalidInputs.Add(part.Trim());
                }
            }

            // Check for invalid inputs
            if (invalidInputs.Count > 0)
            {
                _logger.LogError($"Invalid selection(s): {string.Join(", ", invalidInputs)}. Valid range: 1-{positionList.Count}");
                Console.Write("  Enter your selection: ");
                continue;
            }

            if (selectedTriggers.Count == 0)
            {
                _logger.LogWarning("No valid selections. Please try again.");
                Console.Write("  Enter your selection: ");
                continue;
            }

            // Check for conflicts within selection
            var selectionConflicts = GetConflictsForSelection(selectedTriggers, conflicts);
            if (selectionConflicts.Count > 0)
            {
                _logger.LogError($"Your selection has {selectionConflicts.Count} conflict(s):");
                foreach (var c in selectionConflicts)
                {
                    Console.WriteLine($"      • {c.TriggerPrice1:F0}% ↔ {c.TriggerPrice2:F0}%  →  {c.ConflictType} conflict");
                }
                Console.WriteLine("  Please select points without conflicts.");
                Console.Write("  Enter your selection: ");
                continue;
            }

            _logger.LogSuccess($"Selected {selectedTriggers.Count} position(s): {string.Join(", ", selectedTriggers.Select(t => $"{t:F0}%"))}");
            return selectedTriggers.Distinct().ToList();
        }
    }

    /// <summary>
    /// Gets conflicts that exist within a given selection of trigger prices.
    /// </summary>
    private List<ConflictInfo> GetConflictsForSelection(List<double> selectedTriggers, List<ConflictInfo> allConflicts)
    {
        return allConflicts
            .Where(c => selectedTriggers.Contains(c.TriggerPrice1) && selectedTriggers.Contains(c.TriggerPrice2))
            .ToList();
    }

    /// <summary>
    /// Prompts user to choose a shifted trigger price when legs are missing.
    /// </summary>
    /// <param name="originalTrigger">The original trigger price with missing legs.</param>
    /// <param name="missingStrikes">List of strikes that don't exist.</param>
    /// <param name="shiftUp">Trigger price shifted +1.</param>
    /// <param name="shiftUpValid">Whether shift +1 is valid (legs exist and no conflicts).</param>
    /// <param name="shiftDown">Trigger price shifted -1.</param>
    /// <param name="shiftDownValid">Whether shift -1 is valid (legs exist and no conflicts).</param>
    /// <returns>Selected trigger price, or null to skip this position.</returns>
    public double? GetShiftedTriggerSelection(
        double originalTrigger,
        List<double> missingStrikes,
        double shiftUp,
        bool shiftUpValid,
        double shiftDown,
        bool shiftDownValid)
    {
        Console.WriteLine();
        _logger.LogWarning($"Trigger {originalTrigger}: Missing strikes [{string.Join(", ", missingStrikes)}]");
        Console.WriteLine();
        Console.WriteLine("  Suggested alternatives:");

        int optionNum = 1;
        var options = new Dictionary<int, double?>();

        if (shiftUpValid)
        {
            Console.WriteLine($"    [{optionNum}] Shift UP to {shiftUp} (trigger +1)");
            options[optionNum++] = shiftUp;
        }
        else
        {
            Console.WriteLine($"    [-] Shift UP to {shiftUp} - INVALID (missing legs or conflict)");
        }

        if (shiftDownValid)
        {
            Console.WriteLine($"    [{optionNum}] Shift DOWN to {shiftDown} (trigger -1)");
            options[optionNum++] = shiftDown;
        }
        else
        {
            Console.WriteLine($"    [-] Shift DOWN to {shiftDown} - INVALID (missing legs or conflict)");
        }

        Console.WriteLine($"    [S] Skip this position");
        Console.WriteLine();

        if (options.Count == 0)
        {
            _logger.LogError("No valid alternatives available. Skipping this position.");
            return null;
        }

        Console.Write($"  Select option: ");

        while (true)
        {
            var input = Console.ReadLine()?.Trim().ToUpper();

            if (input == "S")
            {
                _logger.LogInfo($"Skipping trigger {originalTrigger}");
                return null;
            }

            if (int.TryParse(input, out int choice) && options.ContainsKey(choice))
            {
                var selected = options[choice];
                _logger.LogSuccess($"Selected trigger: {selected}");
                return selected;
            }

            _logger.LogError("Invalid selection. Please try again.");
            Console.Write($"  Select option: ");
        }
    }

    /// <summary>
    /// Displays final summary of validated positions and gets user approval.
    /// </summary>
    /// <param name="validatedPositions">Dictionary of validated trigger prices and their strikes.</param>
    /// <returns>True if user approves, false otherwise.</returns>
    public bool GetOrderApproval(Dictionary<double, IronCondorStrikes> validatedPositions)
    {
        Console.WriteLine();
        _logger.LogInfo("=== FINAL POSITIONS SUMMARY ===");
        Console.WriteLine();

        int num = 1;
        foreach (var kvp in validatedPositions.OrderBy(p => p.Key))
        {
            var strikes = kvp.Value;
            Console.WriteLine($"  [{num++}] Trigger: {strikes.TriggerPrice}");
            Console.WriteLine($"       CallBuy: {strikes.CallBuy}, CallSell: {strikes.CallSell}");
            Console.WriteLine($"       PutSell: {strikes.PutSell}, PutBuy: {strikes.PutBuy}");
            Console.WriteLine();
        }

        Console.Write("  Approve and place orders? (Y/N): ");

        while (true)
        {
            var input = Console.ReadLine()?.Trim().ToUpper();

            if (input == "Y")
            {
                _logger.LogSuccess("Orders approved");
                return true;
            }

            if (input == "N")
            {
                _logger.LogWarning("Orders cancelled by user");
                return false;
            }

            _logger.LogError("Please enter Y or N");
            Console.Write("  Approve and place orders? (Y/N): ");
        }
    }
}


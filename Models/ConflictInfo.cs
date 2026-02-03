namespace IronCondor.Models;

/// <summary>
/// Represents a conflict between two iron condor positions.
/// </summary>
public record ConflictInfo
{
    /// <summary>
    /// Trigger price of the first conflicting position.
    /// </summary>
    public double TriggerPrice1 { get; init; }

    /// <summary>
    /// Trigger price of the second conflicting position.
    /// </summary>
    public double TriggerPrice2 { get; init; }

    /// <summary>
    /// Type of conflict (Call or Put).
    /// </summary>
    public string ConflictType { get; init; } = string.Empty;

    /// <summary>
    /// The strike price where the conflict occurs.
    /// </summary>
    public double ConflictingStrike { get; init; }

    public ConflictInfo(double triggerPrice1, double triggerPrice2, string conflictType, double conflictingStrike)
    {
        TriggerPrice1 = triggerPrice1;
        TriggerPrice2 = triggerPrice2;
        ConflictType = conflictType;
        ConflictingStrike = conflictingStrike;
    }
}


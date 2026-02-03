namespace IronCondor.Models;

/// <summary>
/// Constants representing gap types between trading periods.
/// </summary>
public static class GapType
{
    /// <summary>
    /// Gap up - current open is higher than previous close.
    /// </summary>
    public const string Up = "GAP UP";

    /// <summary>
    /// Gap down - current open is lower than previous close.
    /// </summary>
    public const string Down = "GAP DOWN";

    /// <summary>
    /// No gap - current open equals previous close.
    /// </summary>
    public const string No = "NO GAP";
}


namespace IronCondor.Constants;

/// <summary>
/// Trading parameters and constants for the iron condor strategy.
/// </summary>
public static class TradingParameters
{
    /// <summary>
    /// Iron condor spread width (distance between strikes).
    /// </summary>
    public const int Spread = 5;

    /// <summary>
    /// Target probability/chance threshold.
    /// </summary>
    public const double Chance = 0.5;

    /// <summary>
    /// Minimum percentage for backtest range.
    /// </summary>
    public const int MinPercentage = 10;

    /// <summary>
    /// Maximum percentage for backtest range.
    /// </summary>
    public const int MaxPercentage = 90;

    /// <summary>
    /// Threshold for interesting point detection.
    /// </summary>
    public const int InterestingPointThreshold = 5;

    /// <summary>
    /// Window size for local maxima verification.
    /// </summary>
    public const int LocalMaximaWindowSize = 5;

    /// <summary>
    /// Backtest window size (number of weeks to analyze).
    /// </summary>
    public const int BackTestWindowSize = 50;

    /// <summary>
    /// Minimum spread for success calculation.
    /// </summary>
    public const int MinSpreadForSuccess = 2; // 2 * spread
}


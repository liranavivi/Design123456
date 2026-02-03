namespace IronCondor.Constants;

/// <summary>
/// Request ID constants for TWS API calls.
/// </summary>
public static class RequestIds
{
    /// <summary>
    /// Request ID for weekly historical data.
    /// </summary>
    public const int UnderlyingHistoricalDataWeekly = 2000;

    /// <summary>
    /// Request ID for underlying contract details.
    /// </summary>
    public const int UnderlyingContractDetails = 2200;

    /// <summary>
    /// Base request ID for option contract details.
    /// Use strike price as offset from this base.
    /// </summary>
    public const int OptionContractDetailsBase = 3000;

    /// <summary>
    /// Request ID for security definition option parameters.
    /// </summary>
    public const int UnderlyingSecDefOptParams = 2300;

    /// <summary>
    /// Request ID for underlying market data (if needed).
    /// </summary>
    public const int UnderlyingMarketData = 2100;

    /// <summary>
    /// Base request ID for option market data (if needed).
    /// </summary>
    public const int OptionMarketDataBase = 4000;

    /// <summary>
    /// Request ID for PnL (if needed).
    /// </summary>
    public const int PnL = 1000;
}


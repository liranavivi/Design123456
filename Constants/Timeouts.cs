namespace IronCondor.Constants;

/// <summary>
/// Timeout constants for async operations.
/// </summary>
public static class Timeouts
{
    /// <summary>
    /// Timeout for historical data requests.
    /// </summary>
    public static readonly TimeSpan HistoricalData = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Timeout for contract details requests.
    /// </summary>
    public static readonly TimeSpan ContractDetails = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Timeout for security definition option parameters requests.
    /// </summary>
    public static readonly TimeSpan SecDefOptParams = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Timeout for order placement.
    /// </summary>
    public static readonly TimeSpan OrderPlacement = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Default timeout for general requests.
    /// </summary>
    public static readonly TimeSpan DefaultRequest = TimeSpan.FromSeconds(10);
}


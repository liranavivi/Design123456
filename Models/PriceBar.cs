namespace IronCondor.Models;

/// <summary>
/// Represents a historical price bar with OHLCV (Open-High-Low-Close-Volume) data.
/// </summary>
public record PriceBar
{
    /// <summary>
    /// The date and time of this price bar.
    /// </summary>
    public DateTime DateTime { get; init; }

    /// <summary>
    /// Opening price.
    /// </summary>
    public double Open { get; init; }

    /// <summary>
    /// Highest price during the period.
    /// </summary>
    public double High { get; init; }

    /// <summary>
    /// Lowest price during the period.
    /// </summary>
    public double Low { get; init; }

    /// <summary>
    /// Closing price.
    /// </summary>
    public double Close { get; init; }

    /// <summary>
    /// Trading volume.
    /// </summary>
    public double Volume { get; init; }

    /// <summary>
    /// Creates a new PriceBar instance.
    /// </summary>
    public PriceBar()
    {
    }

    /// <summary>
    /// Creates a new PriceBar instance with specified values.
    /// </summary>
    public PriceBar(DateTime dateTime, double open, double high, double low, double close, double volume)
    {
        DateTime = dateTime;
        Open = open;
        High = high;
        Low = low;
        Close = close;
        Volume = volume;
    }
}


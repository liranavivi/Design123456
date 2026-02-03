namespace IronCondor.Infrastructure;

/// <summary>
/// Console-based logger implementation with optional host/price context.
/// </summary>
public class ConsoleLogger : ILogger
{
    private readonly string? _host;
    private readonly Func<double>? _priceProvider;
    private const int HostColumnWidth = 11;
    private const int PriceColumnWidth = 5;

    /// <summary>
    /// Creates a new ConsoleLogger instance.
    /// </summary>
    /// <param name="host">Optional host identifier (e.g., IP address).</param>
    /// <param name="priceProvider">Optional function to get current price for context.</param>
    public ConsoleLogger(string? host = null, Func<double>? priceProvider = null)
    {
        _host = host;
        _priceProvider = priceProvider;
    }

    /// <inheritdoc/>
    public void LogInfo(string message)
    {
        var formattedMessage = FormatMessage("INFO", message);
        Console.WriteLine(formattedMessage);
    }

    /// <inheritdoc/>
    public void LogError(string message)
    {
        var formattedMessage = FormatMessage("ERROR", message);
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(formattedMessage);
        Console.ResetColor();
    }

    /// <inheritdoc/>
    public void LogWarning(string message)
    {
        var formattedMessage = FormatMessage("WARNING", message);
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine(formattedMessage);
        Console.ResetColor();
    }

    /// <inheritdoc/>
    public void LogSuccess(string message)
    {
        var formattedMessage = FormatMessage("SUCCESS", message);
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine(formattedMessage);
        Console.ResetColor();
    }

    /// <inheritdoc/>
    public void LogDebug(string message)
    {
        var formattedMessage = FormatMessage("DEBUG", message);
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine(formattedMessage);
        Console.ResetColor();
    }

    /// <summary>
    /// Formats a log message with optional host/price context.
    /// </summary>
    private string FormatMessage(string level, string message)
    {
        // If no context, use simple format
        if (_host == null && _priceProvider == null)
        {
            return $"[{level}] {message}";
        }

        // Build context prefix
        var hostStr = (_host ?? "").PadRight(HostColumnWidth);
        var priceStr = "";

        if (_priceProvider != null)
        {
            try
            {
                var price = _priceProvider();
                priceStr = price.ToString("F1").PadRight(PriceColumnWidth);
            }
            catch
            {
                priceStr = "".PadRight(PriceColumnWidth);
            }
        }
        else
        {
            priceStr = "".PadRight(PriceColumnWidth);
        }

        return $"{hostStr} {priceStr} [{level}] {message}";
    }
}


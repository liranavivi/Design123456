namespace IronCondor.Infrastructure;

/// <summary>
/// File-based logger implementation that writes to daily log files.
/// </summary>
public class FileLogger : ILogger, IDisposable
{
    private readonly string _logDirectory;
    private readonly string? _host;
    private readonly Func<double>? _priceProvider;
    private StreamWriter? _writer;
    private string? _currentLogFile;
    private readonly object _lock = new object();
    private const int HostColumnWidth = 11;
    private const int PriceColumnWidth = 5;

    /// <summary>
    /// Creates a new FileLogger instance.
    /// </summary>
    /// <param name="logDirectory">Directory where log files will be stored (default: "logs").</param>
    /// <param name="host">Optional host identifier (e.g., IP address).</param>
    /// <param name="priceProvider">Optional function to get current price for context.</param>
    public FileLogger(string logDirectory = "logs", string? host = null, Func<double>? priceProvider = null)
    {
        _logDirectory = logDirectory;
        _host = host;
        _priceProvider = priceProvider;
        EnsureLogDirectoryExists();
    }

    /// <inheritdoc/>
    public void LogInfo(string message)
    {
        WriteLog("INFO", message);
    }

    /// <inheritdoc/>
    public void LogError(string message)
    {
        WriteLog("ERROR", message);
    }

    /// <inheritdoc/>
    public void LogWarning(string message)
    {
        WriteLog("WARNING", message);
    }

    /// <inheritdoc/>
    public void LogSuccess(string message)
    {
        WriteLog("SUCCESS", message);
    }

    /// <inheritdoc/>
    public void LogDebug(string message)
    {
        WriteLog("DEBUG", message);
    }

    /// <summary>
    /// Writes a log message to the daily log file.
    /// </summary>
    private void WriteLog(string level, string message)
    {
        lock (_lock)
        {
            try
            {
                EnsureWriterIsOpen();
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                var formattedMessage = FormatMessage(timestamp, level, message);
                _writer?.WriteLine(formattedMessage);
                _writer?.Flush(); // Ensure immediate write to disk
            }
            catch (Exception ex)
            {
                // Fallback to console if file writing fails
                Console.WriteLine($"[FileLogger Error] Failed to write to log file: {ex.Message}");
                Console.WriteLine($"[{level}] {message}");
            }
        }
    }

    /// <summary>
    /// Formats a log message with timestamp and optional host/price context.
    /// </summary>
    private string FormatMessage(string timestamp, string level, string message)
    {
        // If no context, use simple format
        if (_host == null && _priceProvider == null)
        {
            return $"{timestamp} [{level}] {message}";
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

        return $"{timestamp} {hostStr} {priceStr} [{level}] {message}";
    }

    /// <summary>
    /// Ensures the log directory exists.
    /// </summary>
    private void EnsureLogDirectoryExists()
    {
        if (!Directory.Exists(_logDirectory))
        {
            Directory.CreateDirectory(_logDirectory);
        }
    }

    /// <summary>
    /// Ensures the StreamWriter is open for the current day's log file.
    /// </summary>
    private void EnsureWriterIsOpen()
    {
        var logFileName = $"log_{DateTime.Now:yyyyMMdd}.txt";
        var logFilePath = Path.Combine(_logDirectory, logFileName);

        // If the log file has changed (new day), close the old writer and open a new one
        if (_currentLogFile != logFilePath)
        {
            _writer?.Close();
            _writer?.Dispose();
            _writer = new StreamWriter(logFilePath, append: true) { AutoFlush = true };
            _currentLogFile = logFilePath;
        }
    }

    /// <summary>
    /// Disposes the FileLogger and closes the log file.
    /// </summary>
    public void Dispose()
    {
        lock (_lock)
        {
            _writer?.Close();
            _writer?.Dispose();
            _writer = null;
        }
    }
}



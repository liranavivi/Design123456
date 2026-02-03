namespace IronCondor.Infrastructure;

/// <summary>
/// Logger interface for structured logging.
/// </summary>
public interface ILogger
{
    /// <summary>
    /// Logs an informational message.
    /// </summary>
    void LogInfo(string message);

    /// <summary>
    /// Logs an error message.
    /// </summary>
    void LogError(string message);

    /// <summary>
    /// Logs a warning message.
    /// </summary>
    void LogWarning(string message);

    /// <summary>
    /// Logs a success message.
    /// </summary>
    void LogSuccess(string message);

    /// <summary>
    /// Logs a debug message.
    /// </summary>
    void LogDebug(string message);
}


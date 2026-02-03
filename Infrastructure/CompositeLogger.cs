namespace IronCondor.Infrastructure;

/// <summary>
/// Composite logger that writes to multiple logger implementations.
/// </summary>
public class CompositeLogger : ILogger, IDisposable
{
    private readonly ILogger[] _loggers;

    /// <summary>
    /// Creates a new CompositeLogger instance.
    /// </summary>
    /// <param name="loggers">Array of logger implementations to write to.</param>
    public CompositeLogger(params ILogger[] loggers)
    {
        _loggers = loggers ?? throw new ArgumentNullException(nameof(loggers));
    }

    /// <inheritdoc/>
    public void LogInfo(string message)
    {
        foreach (var logger in _loggers)
        {
            logger.LogInfo(message);
        }
    }

    /// <inheritdoc/>
    public void LogError(string message)
    {
        foreach (var logger in _loggers)
        {
            logger.LogError(message);
        }
    }

    /// <inheritdoc/>
    public void LogWarning(string message)
    {
        foreach (var logger in _loggers)
        {
            logger.LogWarning(message);
        }
    }

    /// <inheritdoc/>
    public void LogSuccess(string message)
    {
        foreach (var logger in _loggers)
        {
            logger.LogSuccess(message);
        }
    }

    /// <inheritdoc/>
    public void LogDebug(string message)
    {
        foreach (var logger in _loggers)
        {
            logger.LogDebug(message);
        }
    }

    /// <summary>
    /// Disposes all disposable loggers.
    /// </summary>
    public void Dispose()
    {
        foreach (var logger in _loggers)
        {
            if (logger is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}



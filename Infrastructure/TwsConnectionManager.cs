/* Copyright (C) 2025 Interactive Brokers LLC. All rights reserved. This code is subject to the terms
 * and conditions of the IB API Non-Commercial License or the IB API Commercial License, as applicable. */

using IBApi;

namespace IronCondor.Infrastructure;

/// <summary>
/// Manages the connection to TWS/Gateway and handles message processing
/// </summary>
public class TwsConnectionManager : IDisposable
{
    private readonly TwsClientWrapper _wrapper;
    private readonly TwsConnectionConfig _config;
    private EReader? _reader;
    private Thread? _messageProcessingThread;
    private bool _disposed;

    /// <summary>
    /// Enable or disable debug logging
    /// </summary>
    public bool EnableDebugLogging { get; set; } = false;

    public TwsConnectionManager(TwsConnectionConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _wrapper = new TwsClientWrapper();
    }

    /// <summary>
    /// Gets the client wrapper for accessing TWS API functionality
    /// </summary>
    public TwsClientWrapper Wrapper => _wrapper;

    /// <summary>
    /// Gets the client socket for making API requests
    /// </summary>
    public EClientSocket ClientSocket => _wrapper.ClientSocket;

    /// <summary>
    /// Indicates whether the client is connected to TWS/Gateway
    /// </summary>
    public bool IsConnected => _wrapper.IsConnected;

    /// <summary>
    /// Connects to TWS/Gateway using the configured parameters
    /// </summary>
    /// <returns>True if connection successful, false otherwise</returns>
    public async Task<bool> ConnectAsync()
    {
        try
        {
            Console.WriteLine($"[INFO] Connecting to TWS/Gateway...");
            Console.WriteLine($"[INFO] {_config}");

            // Set connection options (PACEAPI for better performance)
            try
            {
                if (EnableDebugLogging) Console.WriteLine($"[DEBUG] Setting connection options: +PACEAPI");
                _wrapper.ClientSocket.SetConnectOptions("+PACEAPI");
                if (EnableDebugLogging) Console.WriteLine($"[DEBUG] Connection options set successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARNING] Failed to set PACEAPI option: {ex.Message}");
                Console.WriteLine($"[INFO] Continuing without PACEAPI option...");
            }

            // Establish connection
            if (EnableDebugLogging) Console.WriteLine($"[DEBUG] Calling eConnect({_config.Host}, {_config.Port}, {_config.ClientId})");
            _wrapper.ClientSocket.eConnect(_config.Host, _config.Port, _config.ClientId);
            if (EnableDebugLogging) Console.WriteLine($"[DEBUG] eConnect call completed");

            if (!_wrapper.IsConnected)
            {
                Console.WriteLine("[ERROR] Failed to establish connection");
                return false;
            }

            // Create and start the message reader
            _reader = new EReader(_wrapper.ClientSocket, _wrapper.Signal);
            _reader.Start();

            // Start message processing thread
            _messageProcessingThread = new Thread(() =>
            {
                if (EnableDebugLogging) Console.WriteLine("[DEBUG] Message processing thread started");
                while (_wrapper.IsConnected)
                {
                    _wrapper.Signal.waitForSignal();
                    _reader.processMsgs();
                }
                if (EnableDebugLogging) Console.WriteLine("[DEBUG] Message processing thread stopped");
            })
            {
                IsBackground = true,
                Name = "TWS Message Processor"
            };
            _messageProcessingThread.Start();

            // Wait for NextOrderId to be received (indicates successful connection)
            var timeout = DateTime.UtcNow.AddMilliseconds(_config.ConnectionTimeoutMs);
            while (_wrapper.NextOrderId <= 0 && DateTime.UtcNow < timeout)
            {
                await Task.Delay(100);
            }

            if (_wrapper.NextOrderId <= 0)
            {
                Console.WriteLine("[ERROR] Connection timeout - NextOrderId not received");
                Disconnect();
                return false;
            }

            Console.WriteLine("[SUCCESS] Connected to TWS/Gateway successfully");
            Console.WriteLine($"[INFO] Next Valid Order ID: {_wrapper.NextOrderId}");

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Connection failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Disconnects from TWS/Gateway
    /// </summary>
    public void Disconnect()
    {
        if (_wrapper.IsConnected)
        {
            Console.WriteLine("[INFO] Disconnecting from TWS/Gateway...");
            _wrapper.ClientSocket.eDisconnect();
            
            // Wait for message processing thread to complete
            _messageProcessingThread?.Join(TimeSpan.FromSeconds(2));
            
            Console.WriteLine("[INFO] Disconnected");
        }
    }

    /// <summary>
    /// Requests current server time
    /// </summary>
    public void RequestCurrentTime()
    {
        if (!IsConnected)
        {
            Console.WriteLine("[ERROR] Not connected to TWS/Gateway");
            return;
        }

        Console.WriteLine("[INFO] Requesting current server time...");
        _wrapper.ClientSocket.reqCurrentTime();
    }

    /// <summary>
    /// Waits for the connection to be ready (NextOrderId received)
    /// </summary>
    public async Task WaitForConnectionReadyAsync(int timeoutMs = 5000)
    {
        var timeout = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (_wrapper.NextOrderId <= 0 && DateTime.UtcNow < timeout && IsConnected)
        {
            await Task.Delay(100);
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            Disconnect();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}


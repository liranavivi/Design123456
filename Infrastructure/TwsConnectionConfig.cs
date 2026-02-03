/* Copyright (C) 2025 Interactive Brokers LLC. All rights reserved. This code is subject to the terms
 * and conditions of the IB API Non-Commercial License or the IB API Commercial License, as applicable. */

namespace IronCondor.Infrastructure;

/// <summary>
/// Configuration class for TWS/Gateway connection parameters
/// </summary>
public class TwsConnectionConfig
{
    /// <summary>
    /// Host address for TWS/Gateway connection
    /// Default: 127.0.0.1 (localhost)
    /// </summary>
    public string Host { get; set; } = "127.0.0.1";

    /// <summary>
    /// Port number for TWS/Gateway connection
    /// Default: 7497 (TWS Paper Trading)
    /// Common ports:
    /// - 7496: TWS Live Trading
    /// - 7497: TWS Paper Trading
    /// - 4001: IB Gateway Live Trading
    /// - 4002: IB Gateway Paper Trading
    /// </summary>
    public int Port { get; set; } = 7497;

    /// <summary>
    /// Client ID for the connection
    /// Must be unique if multiple clients connect to the same TWS/Gateway instance
    /// Valid range: 0-32 (max 32 simultaneous connections)
    /// Default: 0
    /// </summary>
    public int ClientId { get; set; } = 0;

    /// <summary>
    /// Connection timeout in milliseconds
    /// Default: 5000ms (5 seconds)
    /// </summary>
    public int ConnectionTimeoutMs { get; set; } = 5000;

    /// <summary>
    /// Creates a default configuration for TWS Paper Trading
    /// </summary>
    public static TwsConnectionConfig PaperTrading(int clientId = 1)
    {
        return new TwsConnectionConfig
        {
            Host = "127.0.0.1",
            Port = 4001,
            ClientId = clientId
        };
    }

    /// <summary>
    /// Creates a configuration for TWS Live Trading
    /// </summary>
    public static TwsConnectionConfig LiveTrading(int clientId = 0)
    {
        return new TwsConnectionConfig
        {
            Host = "127.0.0.1",
            Port = 7496,
            ClientId = clientId
        };
    }

    /// <summary>
    /// Creates a configuration for IB Gateway Paper Trading
    /// </summary>
    public static TwsConnectionConfig GatewayPaperTrading(int clientId = 0)
    {
        return new TwsConnectionConfig
        {
            Host = "127.0.0.1",
            Port = 4001,
            ClientId = clientId
        };
    }

    /// <summary>
    /// Creates a configuration for IB Gateway Live Trading
    /// </summary>
    public static TwsConnectionConfig GatewayLiveTrading(int clientId = 0)
    {
        return new TwsConnectionConfig
        {
            Host = "127.0.0.1",
            Port = 4002,
            ClientId = clientId
        };
    }

    public override string ToString()
    {
        return $"Host: {Host}, Port: {Port}, ClientId: {ClientId}";
    }
}


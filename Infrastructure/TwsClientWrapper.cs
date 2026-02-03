/* Copyright (C) 2025 Interactive Brokers LLC. All rights reserved. This code is subject to the terms
 * and conditions of the IB API Non-Commercial License or the IB API Commercial License, as applicable. */

using IBApi;
using IronCondor.Services;
using System.Net.Sockets;

namespace IronCondor.Infrastructure;

/// <summary>
/// Wrapper implementation for TWS API callbacks
/// Implements the EWrapper interface to handle all incoming messages from TWS/Gateway
/// Inherits from DefaultEWrapper to provide default implementations for all callbacks
/// </summary>
public class TwsClientWrapper : DefaultEWrapper
{
    private readonly EReaderSignal _signal;
    private EClientSocket? _clientSocket;
    private int _nextOrderId;

    public TwsClientWrapper()
    {
        _signal = new EReaderMonitorSignal();
        _clientSocket = new EClientSocket(this, _signal);
    }

    /// <summary>
    /// Gets the client socket for sending requests to TWS/Gateway
    /// </summary>
    public EClientSocket ClientSocket => _clientSocket ?? throw new InvalidOperationException("Client socket not initialized");

    /// <summary>
    /// Gets the signal used for message processing
    /// </summary>
    public EReaderSignal Signal => _signal;

    /// <summary>
    /// Gets the next valid order ID received from TWS/Gateway
    /// </summary>
    public int NextOrderId
    {
        get => _nextOrderId;
        private set => _nextOrderId = value;
    }

    /// <summary>
    /// Indicates whether the client is connected to TWS/Gateway
    /// </summary>
    public bool IsConnected => _clientSocket?.IsConnected() ?? false;

    /// <summary>
    /// Increments the next order ID after placing an order
    /// </summary>
    public void IncrementNextOrderId()
    {
        _nextOrderId++;
    }

    #region Connection Events

    public event EventHandler? Connected;
    public event EventHandler? Disconnected;
    public event EventHandler<ErrorEventArgs>? ErrorOccurred;
    public event EventHandler<string>? MessageReceived;

    #endregion

    #region Strategy Events

    public event EventHandler<HistoricalDataEventArgs>? HistoricalDataReceived;
    public event EventHandler<HistoricalDataEndEventArgs>? HistoricalDataEnd;
    public event EventHandler<ContractDetailsEventArgs>? ContractDetailsReceived;
    public event EventHandler<ContractDetailsEventArgs>? ContractDetailsEnd;
    public event EventHandler<SecurityDefinitionOptionParameterEventArgs>? SecurityDefinitionOptionParameterReceived;
    public event EventHandler<OpenOrderEventArgs>? OpenOrderReceived;
    public event EventHandler<OrderStatusEventArgs>? OrderStatusReceived;

    #endregion

    #region EWrapper Implementation - Connection & Error Handling

    public override void error(Exception e)
    {
        var errorMsg = e switch
        {
            SocketException s => $"Socket Error: {s.Message}",
            _ => $"Exception: {e.Message}"
        };

        Console.WriteLine($"[ERROR] {errorMsg}");
        ErrorOccurred?.Invoke(this, new ErrorEventArgs(e));
    }

    public override void error(string str)
    {
        Console.WriteLine($"[ERROR] {str}");
        ErrorOccurred?.Invoke(this, new ErrorEventArgs(new Exception(str)));
    }

    public override void error(int id, long errorTime, int errorCode, string errorMsg, string advancedOrderRejectJson)
    {
        string errorTimeStr = errorTime > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(errorTime).ToString("yyyy-MM-dd HH:mm:ss") : "";

        // Filter informational messages (these are status notifications, not errors)
        bool isInformational = id == -1 && (errorCode == 2104 || errorCode == 2106 || errorCode == 2158);
        string prefix = isInformational ? "[INFO]" : "[ERROR]";

        var message = string.IsNullOrEmpty(advancedOrderRejectJson)
            ? $"{prefix} Id: {id}, Time: {errorTimeStr}, Code: {errorCode}, Msg: {errorMsg}"
            : $"{prefix} Id: {id}, Time: {errorTimeStr}, Code: {errorCode}, Msg: {errorMsg}, AdvancedOrderRejectJson: {advancedOrderRejectJson}";

        Console.WriteLine(message);

        // Only invoke error event for actual errors, not informational messages
        if (!isInformational)
        {
            ErrorOccurred?.Invoke(this, new ErrorEventArgs(new Exception(message)));
        }
    }

    public override void connectionClosed()
    {
        Console.WriteLine("[INFO] Connection closed");
        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    public override void connectAck()
    {
        Console.WriteLine("[INFO] Connection acknowledged");
        Connected?.Invoke(this, EventArgs.Empty);
    }

    #endregion

    #region EWrapper Implementation - Account & Order Management

    public override void nextValidId(int orderId)
    {
        Console.WriteLine($"[INFO] Next Valid Order ID: {orderId}");
        NextOrderId = orderId;
    }

    public override void managedAccounts(string accountsList)
    {
        Console.WriteLine($"[INFO] Managed Accounts: {accountsList}");
        MessageReceived?.Invoke(this, $"Managed Accounts: {accountsList}");
    }

    public override void currentTime(long time)
    {
        var timeStr = DateTimeOffset.FromUnixTimeSeconds(time).ToString("MMM dd, yyyy HH:mm:ss");
        Console.WriteLine($"[INFO] Current Time: {time} ({timeStr})");
    }

    #endregion

    #region EWrapper Implementation - Market Data (Minimal Implementation)

    public override void tickPrice(int tickerId, int field, double price, TickAttrib attribs)
    {
        Console.WriteLine($"[TICK] Price - TickerId: {tickerId}, Field: {field}, Price: {price}");
    }

    public override void tickSize(int tickerId, int field, decimal size)
    {
        Console.WriteLine($"[TICK] Size - TickerId: {tickerId}, Field: {field}, Size: {size}");
    }

    public override void tickString(int tickerId, int tickType, string value)
    {
        Console.WriteLine($"[TICK] String - TickerId: {tickerId}, Type: {tickType}, Value: {value}");
    }

    public override void tickGeneric(int tickerId, int tickType, double value)
    {
        Console.WriteLine($"[TICK] Generic - TickerId: {tickerId}, Type: {tickType}, Value: {value}");
    }

    #endregion

    #region EWrapper Implementation - Historical Data

    public override void historicalData(int reqId, Bar bar)
    {
        HistoricalDataReceived?.Invoke(this, new HistoricalDataEventArgs(
            reqId,
            bar.Time,
            bar.Open,
            bar.High,
            bar.Low,
            bar.Close,
            (double)bar.Volume));
    }

    public override void historicalDataEnd(int reqId, string startDate, string endDate)
    {
        Console.WriteLine($"[INFO] Historical Data End - ReqId: {reqId}, Start: {startDate}, End: {endDate}");
        HistoricalDataEnd?.Invoke(this, new HistoricalDataEndEventArgs(reqId));
    }

    #endregion

    #region EWrapper Implementation - Contract Details

    public override void contractDetails(int reqId, ContractDetails contractDetails)
    {
        Console.WriteLine($"[INFO] Contract Details - ReqId: {reqId}, Symbol: {contractDetails.Contract.Symbol}");
        ContractDetailsReceived?.Invoke(this, new ContractDetailsEventArgs(reqId, contractDetails));
    }

    public override void contractDetailsEnd(int reqId)
    {
        Console.WriteLine($"[INFO] Contract Details End - ReqId: {reqId}");
        ContractDetailsEnd?.Invoke(this, new ContractDetailsEventArgs(reqId, null!));
    }

    #endregion

    #region EWrapper Implementation - Security Definition Option Parameters

    public override void securityDefinitionOptionParameter(
        int reqId,
        string exchange,
        int underlyingConId,
        string tradingClass,
        string multiplier,
        HashSet<string> expirations,
        HashSet<double> strikes)
    {
        Console.WriteLine($"[INFO] SecDefOptParams - ReqId: {reqId}, Exchange: {exchange}, TradingClass: {tradingClass}, Strikes: {strikes.Count}, Expirations: {expirations.Count}");
        SecurityDefinitionOptionParameterReceived?.Invoke(this, new SecurityDefinitionOptionParameterEventArgs(
            reqId,
            exchange,
            tradingClass,
            strikes,
            expirations));
    }

    #endregion

    #region EWrapper Implementation - Orders

    public override void openOrder(int orderId, Contract contract, Order order, OrderState orderState)
    {
        Console.WriteLine($"[INFO] Open Order - OrderId: {orderId}, Symbol: {contract.Symbol}, Status: {orderState.Status}");
        OpenOrderReceived?.Invoke(this, new OpenOrderEventArgs(orderId, contract, order, orderState));
    }

    public override void orderStatus(
        int orderId,
        string status,
        decimal filled,
        decimal remaining,
        double avgFillPrice,
        long permId,
        int parentId,
        double lastFillPrice,
        int clientId,
        string whyHeld,
        double mktCapPrice)
    {
        Console.WriteLine($"[INFO] Order Status - OrderId: {orderId}, Status: {status}, Filled: {filled}, Remaining: {remaining}");
        OrderStatusReceived?.Invoke(this, new OrderStatusEventArgs(
            orderId,
            status,
            filled,
            remaining,
            avgFillPrice,
            permId,
            parentId,
            lastFillPrice,
            clientId,
            whyHeld,
            mktCapPrice));
    }

    #endregion

    #region Additional EWrapper Methods - Using DefaultEWrapper base implementations

    // All other EWrapper methods are inherited from DefaultEWrapper
    // Override specific methods as needed for your trading strategy

    #endregion
}

/// <summary>
/// Event args for order status received event.
/// </summary>
public class OrderStatusEventArgs : EventArgs
{
    public int OrderId { get; }
    public string Status { get; }
    public decimal Filled { get; }
    public decimal Remaining { get; }
    public double AvgFillPrice { get; }
    public long PermId { get; }
    public int ParentId { get; }
    public double LastFillPrice { get; }
    public int ClientId { get; }
    public string WhyHeld { get; }
    public double MktCapPrice { get; }

    public OrderStatusEventArgs(
        int orderId,
        string status,
        decimal filled,
        decimal remaining,
        double avgFillPrice,
        long permId,
        int parentId,
        double lastFillPrice,
        int clientId,
        string whyHeld,
        double mktCapPrice)
    {
        OrderId = orderId;
        Status = status;
        Filled = filled;
        Remaining = remaining;
        AvgFillPrice = avgFillPrice;
        PermId = permId;
        ParentId = parentId;
        LastFillPrice = lastFillPrice;
        ClientId = clientId;
        WhyHeld = whyHeld;
        MktCapPrice = mktCapPrice;
    }
}



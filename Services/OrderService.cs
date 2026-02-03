using IBApi;
using IronCondor.Constants;
using IronCondor.Infrastructure;
using IronCondor.Models;

namespace IronCondor.Services;

/// <summary>
/// Service for placing iron condor orders.
/// </summary>
public class OrderService
{
    private readonly TwsClientWrapper _client;
    private readonly ILogger _logger;
    private readonly ContractService _contractService;

    public OrderService(TwsClientWrapper client, ILogger logger, ContractService contractService)
    {
        _client = client;
        _logger = logger;
        _contractService = contractService;
    }

    /// <summary>
    /// Places an iron condor order.
    /// </summary>
    /// <param name="underlyingContract">The underlying contract.</param>
    /// <param name="triggerPrice">The trigger price for the iron condor.</param>
    /// <param name="spread">The spread width.</param>
    /// <param name="quantity">The quantity to trade.</param>
    /// <param name="lastUnderlyingPrice">The last underlying price.</param>
    /// <param name="weeklyPriceBars">Historical weekly price bars for range checking.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if order was successfully placed, false otherwise.</returns>
    public async Task<bool> PlaceIronCondorOrderAsync(
        Contract underlyingContract,
        double triggerPrice,
        int spread,
        double quantity,
        double lastUnderlyingPrice,
        List<PriceBar> weeklyPriceBars,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInfo($"Placing iron condor order for trigger price: {triggerPrice}, quantity: {quantity}");

        // Build combo legs
        var comboLegs = BuildComboLegs(triggerPrice, spread);

        // Create BAG contract
        var comboContract = new Contract
        {
            Symbol = underlyingContract.Symbol,
            SecType = "BAG",
            Currency = "USD",
            Exchange = "SMART",
            ComboLegs = comboLegs
        };

        // Determine if trigger is outside current range
        bool isMore = lastUnderlyingPrice < triggerPrice;
        bool isOutsideRange = triggerPrice > weeklyPriceBars.Last().High || triggerPrice < weeklyPriceBars.Last().Low;

        // Create limit order
        var comboOrder = new Order
        {
            OrderId = _client.NextOrderId,
            Action = "BUY",
            OrderType = "LMT",
            LmtPrice = Math.Round(-(double)spread * TradingParameters.Chance, 2),
            UsePriceMgmtAlgo = true,
            TotalQuantity = (decimal)quantity,
            Tif = "GTC",
            OrderRef = isOutsideRange
                ? $"Cond IronCondor - TPrice: {triggerPrice}"
                : $"Non Cond IronCondor - TPrice: {triggerPrice}"
        };

        // Add price condition if trigger is outside current range
        double conditionPrice = 0;
        if (isOutsideRange)
        {
            PriceCondition underlyingPriceCondition = (PriceCondition)OrderCondition.Create(OrderConditionType.Price);
            underlyingPriceCondition.ConId = underlyingContract.ConId;
            underlyingPriceCondition.Exchange = underlyingContract.Exchange;
            underlyingPriceCondition.IsMore = isMore;
            conditionPrice = Math.Round(triggerPrice, 2) + (isMore ? -0.5 : +0.5);
            underlyingPriceCondition.Price = conditionPrice;
            underlyingPriceCondition.TriggerMethod = TriggerMethod.BidAsk;

            comboOrder.Conditions.Add(underlyingPriceCondition);
        }

        // Place order
        try
        {
            var orderPlaced = await PlaceOrderAsync(comboOrder, comboContract, triggerPrice, cancellationToken);

            if (orderPlaced)
            {
                var conditionInfo = isOutsideRange
                    ? $", Condition: {(isMore ? ">" : "<")} {conditionPrice} (grace: {(isMore ? "-" : "+")}0.5)"
                    : "";
                _logger.LogSuccess($"PlaceIronCondorOrder {comboContract.LocalSymbol} order ID: {comboOrder.OrderId} for TPrice: {triggerPrice}, quantity: {quantity}{conditionInfo}");
                _client.IncrementNextOrderId();
                return true;
            }
            else
            {
                _logger.LogWarning($"PlaceIronCondorOrder cancelled {comboContract.LocalSymbol} for TPrice: {triggerPrice}, quantity: {quantity}");
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to place iron condor order: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Builds the combo legs for an iron condor.
    /// </summary>
    private List<ComboLeg> BuildComboLegs(double triggerPrice, int spread)
    {
        var conIds = _contractService.ConIds;

        // Leg 1: Sell Call (strike above trigger)
        var leg1 = new ComboLeg
        {
            ConId = conIds[(triggerPrice + spread, "C")],
            Ratio = 1,
            Action = "SELL",
            Exchange = "SMART"
        };

        // Leg 2: Buy Call (strike above trigger, further OTM)
        var leg2 = new ComboLeg
        {
            ConId = conIds[(triggerPrice + spread * 2, "C")],
            Ratio = 1,
            Action = "BUY",
            Exchange = "SMART"
        };

        // Leg 3: Sell Put (strike below trigger)
        var leg3 = new ComboLeg
        {
            ConId = conIds[(triggerPrice - spread, "P")],
            Ratio = 1,
            Action = "SELL",
            Exchange = "SMART"
        };

        // Leg 4: Buy Put (strike below trigger, further OTM)
        var leg4 = new ComboLeg
        {
            ConId = conIds[(triggerPrice - spread * 2, "P")],
            Ratio = 1,
            Action = "BUY",
            Exchange = "SMART"
        };

        return new List<ComboLeg> { leg1, leg2, leg3, leg4 };
    }

    /// <summary>
    /// Places an order and waits for confirmation.
    /// </summary>
    private async Task<bool> PlaceOrderAsync(
        Order order,
        Contract contract,
        double triggerPrice,
        CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<bool>();

        EventHandler<OpenOrderEventArgs>? handler = null;
        handler = (sender, args) =>
        {
            if (args.OrderId == order.OrderId &&
                (args.OrderState.Status == "PreSubmitted" || args.OrderState.Status == "Submitted"))
            {
                _logger.LogInfo($"OpenOrder {args.Contract.LocalSymbol} {args.OrderState.Status} order ID: {args.OrderId} {args.Order.OrderType} for TPrice: {triggerPrice}");
                tcs.TrySetResult(true);
            }
        };

        try
        {
            _client.OpenOrderReceived += handler;
            _client.ClientSocket.placeOrder(order.OrderId, contract, order);

            _logger.LogInfo($"PlaceIronCondorOrder {contract.LocalSymbol} {order.OrderId} for TPrice: {triggerPrice}");

            // Small delay to allow order to be processed
            await Task.Delay(100, cancellationToken);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(Timeouts.OrderPlacement);

            return await tcs.Task.WaitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            return false; // Timeout or cancellation
        }
        finally
        {
            _client.OpenOrderReceived -= handler;
        }
    }
}

/// <summary>
/// Event args for open order received event.
/// </summary>
public class OpenOrderEventArgs : EventArgs
{
    public int OrderId { get; }
    public Contract Contract { get; }
    public Order Order { get; }
    public OrderState OrderState { get; }

    public OpenOrderEventArgs(int orderId, Contract contract, Order order, OrderState orderState)
    {
        OrderId = orderId;
        Contract = contract;
        Order = order;
        OrderState = orderState;
    }
}

using IBApi;
using IronCondor.Constants;
using IronCondor.Infrastructure;
using IronCondor.Models;

namespace IronCondor.Services;

/// <summary>
/// Orchestrates the complete iron condor strategy workflow.
/// </summary>
public class StrategyOrchestrator
{
    private readonly TwsClientWrapper _client;
    private readonly BackTestService _backTestService;
    private readonly StrikeProbabilityService _strikeProbabilityService;
    private readonly ContractService _contractService;
    private readonly ConflictDetectionService _conflictDetectionService;
    private readonly UserInteractionService _userInteractionService;
    private readonly OrderService _orderService;
    private readonly ChartService _chartService;
    private readonly ILogger _logger;

    private Contract? _underlyingContract;
    private readonly List<PriceBar> _weeklyPriceBars;
    private PriceBar? _thisWeekBar;
    private PriceBar? _lastWeekBar;
    private double _lastUnderlyingPrice;
    private double _quantity;
    private string _symbol = string.Empty;

    public StrategyOrchestrator(
        TwsClientWrapper client,
        BackTestService backTestService,
        StrikeProbabilityService strikeProbabilityService,
        ContractService contractService,
        ConflictDetectionService conflictDetectionService,
        UserInteractionService userInteractionService,
        OrderService orderService,
        ChartService chartService,
        ILogger logger)
    {
        _client = client;
        _backTestService = backTestService;
        _strikeProbabilityService = strikeProbabilityService;
        _contractService = contractService;
        _conflictDetectionService = conflictDetectionService;
        _userInteractionService = userInteractionService;
        _orderService = orderService;
        _chartService = chartService;
        _logger = logger;
        _weeklyPriceBars = new List<PriceBar>();
    }

    /// <summary>
    /// Runs the complete iron condor strategy for a given symbol.
    /// </summary>
    /// <param name="symbol">The underlying symbol (e.g., "SPY", "AAPL").</param>
    /// <param name="placeOrders">Whether to actually place orders (default: false for dry-run).</param>
    /// <param name="quantity">The quantity to trade per order (default: 4.0).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if strategy executed successfully, false otherwise.</returns>
    public async Task<bool> RunStrategyAsync(
        string symbol,
        bool placeOrders = false,
        double quantity = 4.0,
        CancellationToken cancellationToken = default)
    {
        _quantity = quantity;
        _symbol = symbol;

        _logger.LogInfo($"========================================");
        _logger.LogInfo($"Starting strategy for symbol: {symbol}");
        _logger.LogInfo($"========================================");

        try
        {
            // Step 1: Create underlying contract
            _underlyingContract = CreateUnderlyingContract(symbol);

            // Step 2: Request historical data
            _weeklyPriceBars.Clear();
            var historicalDataReceived = await RequestHistoricalDataAsync(cancellationToken);

            if (!historicalDataReceived)
            {
                _logger.LogError("Timeout - Request historical data failed");
                return false;
            }

            // Step 3: Validate weekly points
            if (!ValidateWeeklyPoints())
            {
                return false;
            }

            // Step 4: Request contract details for underlying
            var contractDetailsReceived = await RequestUnderlyingContractDetailsAsync(cancellationToken);

            if (!contractDetailsReceived)
            {
                _logger.LogError("Timeout - Request underlying contract detail failed");
                return false;
            }

            // Step 5: Set last underlying price
            _lastUnderlyingPrice = _weeklyPriceBars.Last().Close;
            _logger.LogInfo($"Last underlying price: {_lastUnderlyingPrice}");

            // Step 6: Request security definition option parameters
            var secDefOptParams = await RequestSecDefOptParamsAsync(cancellationToken);

            if (secDefOptParams == null)
            {
                _logger.LogError("Timeout - Request security options params failed");
                return false;
            }

            // Step 7: Validate strikes and expirations
            if (secDefOptParams.Strikes.Count == 0)
            {
                _logger.LogError("No Data - Security options params failed");
                return false;
            }

            // Step 8: Find valid expiration
            var expiration = FindValidExpiration(secDefOptParams.Expirations);

            if (expiration == null)
            {
                _logger.LogError("No Data - Valid expiration date does not exist");
                return false;
            }

            _logger.LogInfo($"Using expiration: {expiration}");

            // Step 9: Build option contracts
            var contracts = BuildOptionContracts(symbol, secDefOptParams.Strikes, expiration);

            _logger.LogInfo($"Built {contracts.Count} option contracts");

            // Step 10: Run backtest
            double minStrike = contracts.Min(c => c.Strike);
            double maxStrike = contracts.Max(c => c.Strike);
            var backTestResult = _backTestService.FindOptimalTriggerPercentages(_weeklyPriceBars, minStrike, maxStrike);

            if (backTestResult.InterestingPoints.Count == 0)
            {
                _logger.LogWarning("No Data - Percents data failed (no optimal trigger percentages found)");
                return false;
            }

            // Step 10b: Get user input for number of points to use
            int pointsToUse = _userInteractionService.GetNumberOfPointsToUse(backTestResult.InterestingPoints);

            // Trim interesting points to user selection
            backTestResult.InterestingPoints = backTestResult.InterestingPoints.Take(pointsToUse).ToList();

            _logger.LogSuccess($"Using {backTestResult.InterestingPoints.Count} optimal trigger percentages");

            // Step 11: Calculate strike probabilities
            if (_thisWeekBar == null || _lastWeekBar == null)
            {
                _logger.LogError("Weekly price bars not properly initialized");
                return false;
            }

            var (gap, filteredContracts) = _strikeProbabilityService.CalculateStrikeProbabilities(
                _weeklyPriceBars,
                contracts,
                _thisWeekBar,
                _lastWeekBar);

            // Step 12: Calculate trigger prices for each optimal point
            var triggerPrices = CalculateTriggerPricesFromInterestingPoints(
                backTestResult.InterestingPoints,
                _strikeProbabilityService.StrikeProbabilitiesFromWeekLow,
                _strikeProbabilityService.StrikeProbabilitiesFromWeekHigh,
                gap);

            if (triggerPrices.Count == 0)
            {
                _logger.LogWarning("No valid trigger prices could be calculated");
                return false;
            }

            // Step 13: Validate and build iron condor positions with leg validation
            var validatedPositions = ValidateAndBuildPositions(
                triggerPrices,
                secDefOptParams.Strikes);

            if (validatedPositions.Count == 0)
            {
                _logger.LogWarning("No valid iron condors after validation");
                return false;
            }

            // Step 14: Display chart (no wait for user input)
            _chartService.DisplayBackTestChart(backTestResult, _symbol);

            // Step 15: Show summary and get user approval
            if (!_userInteractionService.GetOrderApproval(validatedPositions))
            {
                _logger.LogInfo("Order placement cancelled by user");
                return false;
            }

            // Step 16: Request contract details for validated positions
            var ironCondorPositions = await _contractService.RequestContractDetailsForPositionsAsync(
                filteredContracts,
                validatedPositions,
                cancellationToken);

            if (ironCondorPositions.Count == 0)
            {
                _logger.LogWarning("No valid iron condors after contract details request");
                return false;
            }

            _logger.LogSuccess($"Strategy analysis complete. {ironCondorPositions.Count} valid iron condors identified.");

            // Step 17: Place orders (if enabled)
            if (placeOrders)
            {
                _logger.LogInfo("Placing orders...");
                await PlaceOrdersAsync(ironCondorPositions, cancellationToken);
            }
            else
            {
                _logger.LogInfo("Dry-run mode - orders not placed");
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Strategy execution failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Creates the underlying contract.
    /// </summary>
    private Contract CreateUnderlyingContract(string symbol)
    {
        return new Contract
        {
            Symbol = symbol,
            SecType = "STK",
            Currency = "USD",
            Exchange = "SMART"
        };
    }

    /// <summary>
    /// Requests historical data for the underlying.
    /// </summary>
    private async Task<bool> RequestHistoricalDataAsync(CancellationToken cancellationToken)
    {
        _logger.LogInfo("Requesting historical data (4 years, weekly)...");

        var tcs = new TaskCompletionSource<bool>();

        EventHandler<HistoricalDataEndEventArgs>? handler = null;
        handler = (sender, args) =>
        {
            if (args.RequestId == RequestIds.UnderlyingHistoricalDataWeekly)
            {
                tcs.TrySetResult(true);
            }
        };

        EventHandler<HistoricalDataEventArgs>? dataHandler = null;
        dataHandler = (sender, args) =>
        {
            if (args.RequestId == RequestIds.UnderlyingHistoricalDataWeekly)
            {
                var priceBar = new PriceBar
                {
                    DateTime = DateTime.ParseExact(args.Date, "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture),
                    Open = args.Open,
                    High = args.High,
                    Low = args.Low,
                    Close = args.Close,
                    Volume = args.Volume
                };
                _weeklyPriceBars.Add(priceBar);
            }
        };

        try
        {
            _client.HistoricalDataEnd += handler;
            _client.HistoricalDataReceived += dataHandler;

            _client.ClientSocket.reqHistoricalData(
                RequestIds.UnderlyingHistoricalDataWeekly,
                _underlyingContract,
                string.Empty,
                "4 Y",
                "1 week",
                "TRADES",
                1,
                1,
                false,
                new List<TagValue>());

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(Timeouts.HistoricalData);

            var result = await tcs.Task.WaitAsync(cts.Token);

            _logger.LogSuccess($"Received {_weeklyPriceBars.Count} weekly data points");

            return result;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        finally
        {
            _client.HistoricalDataEnd -= handler;
            _client.HistoricalDataReceived -= dataHandler;
        }
    }

    /// <summary>
    /// Validates weekly price bars data.
    /// </summary>
    private bool ValidateWeeklyPoints()
    {
        if (_weeklyPriceBars.Count <= 1)
        {
            _logger.LogError("No Data - Historical data failed (insufficient price bars)");
            return false;
        }

        // Check if this week's data is valid
        var mondayOfThisWeek = DateTime.Now.AddDays(-(int)DateTime.Now.DayOfWeek);

        if (mondayOfThisWeek >= _weeklyPriceBars[_weeklyPriceBars.Count - 1].DateTime)
        {
            _logger.LogError("Invalid - First open of week underlying price data failed");
            return false;
        }

        _thisWeekBar = _weeklyPriceBars[_weeklyPriceBars.Count - 1];
        _lastWeekBar = _weeklyPriceBars[_weeklyPriceBars.Count - 2];

        _logger.LogInfo($"This week bar: {_thisWeekBar.DateTime:yyyy-MM-dd} Close: {_thisWeekBar.Close}");
        _logger.LogInfo($"Last week bar: {_lastWeekBar.DateTime:yyyy-MM-dd} Close: {_lastWeekBar.Close}");

        return true;
    }

    /// <summary>
    /// Requests contract details for the underlying.
    /// </summary>
    private async Task<bool> RequestUnderlyingContractDetailsAsync(CancellationToken cancellationToken)
    {
        _logger.LogInfo("Requesting underlying contract details...");

        var tcs = new TaskCompletionSource<bool>();

        EventHandler<ContractDetailsEventArgs>? handler = null;
        handler = (sender, args) =>
        {
            if (args.RequestId == RequestIds.UnderlyingContractDetails)
            {
                _underlyingContract = args.ContractDetails.Contract;
                tcs.TrySetResult(true);
            }
        };

        try
        {
            _client.ContractDetailsReceived += handler;
            _client.ClientSocket.reqContractDetails(RequestIds.UnderlyingContractDetails, _underlyingContract);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(Timeouts.ContractDetails);

            var result = await tcs.Task.WaitAsync(cts.Token);

            _logger.LogSuccess($"Received contract details for {_underlyingContract?.Symbol ?? "Unknown"}");

            return result;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        finally
        {
            _client.ContractDetailsReceived -= handler;
        }
    }

    /// <summary>
    /// Requests security definition option parameters.
    /// </summary>
    private async Task<SecurityDefinitionOptionParameterEventArgs?> RequestSecDefOptParamsAsync(CancellationToken cancellationToken)
    {
        _logger.LogInfo("Requesting security definition option parameters...");

        var tcs = new TaskCompletionSource<SecurityDefinitionOptionParameterEventArgs?>();

        EventHandler<SecurityDefinitionOptionParameterEventArgs>? handler = null;
        handler = (sender, args) =>
        {
            if (args.ReqId == RequestIds.UnderlyingSecDefOptParams)
            {
                if (_underlyingContract != null &&
                    args.Exchange == _underlyingContract.Exchange &&
                    args.TradingClass == _underlyingContract.TradingClass)
                {
                    tcs.TrySetResult(args);
                }
            }
        };

        try
        {
            if (_underlyingContract == null)
            {
                _logger.LogError("Underlying contract is null");
                return null;
            }

            _client.SecurityDefinitionOptionParameterReceived += handler;
            _client.ClientSocket.reqSecDefOptParams(
                RequestIds.UnderlyingSecDefOptParams,
                _underlyingContract.Symbol,
                "",
                _underlyingContract.SecType,
                _underlyingContract.ConId);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(Timeouts.SecDefOptParams);

            var result = await tcs.Task.WaitAsync(cts.Token);

            if (result != null)
            {
                _logger.LogSuccess($"Received {result.Strikes.Count} strikes and {result.Expirations.Count} expirations");
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            _client.SecurityDefinitionOptionParameterReceived -= handler;
        }
    }

    /// <summary>
    /// Finds a valid expiration date (before next Saturday).
    /// </summary>
    private string? FindValidExpiration(HashSet<string> expirations)
    {
        var nextSaturday = DateTime.Now.AddDays((int)DayOfWeek.Saturday - (int)DateTime.Now.DayOfWeek);

        var validExpirations = expirations
            .Where(e => DateTime.ParseExact(e, "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture) < nextSaturday)
            .ToList();

        if (validExpirations.Count == 0)
            return null;

        return validExpirations.Last();
    }

    /// <summary>
    /// Calculates trigger prices from interesting points using strike probabilities.
    /// </summary>
    private List<double> CalculateTriggerPricesFromInterestingPoints(
        List<int> interestingPoints,
        StrikeProbabilityMap strikeProbabilitiesFromWeekLow,
        StrikeProbabilityMap strikeProbabilitiesFromWeekHigh,
        string gap)
    {
        var triggerPrices = new List<double>();

        foreach (var interesting in interestingPoints)
        {
            var targetFromMaxPointPair = strikeProbabilitiesFromWeekHigh.FirstOrDefault(p => p.Probability > interesting);
            var targetFromMinPointPair = strikeProbabilitiesFromWeekLow.FirstOrDefault(p => p.Probability > interesting);

            if (targetFromMaxPointPair == null || targetFromMinPointPair == null)
                continue;

            double minTargetForCall = Math.Min(targetFromMaxPointPair.Strike, targetFromMinPointPair.Strike);
            double maxTargetForPut = Math.Max(targetFromMaxPointPair.Strike, targetFromMinPointPair.Strike);

            double triggerPrice = Math.Round((minTargetForCall + maxTargetForPut) / 2);

            _logger.LogInfo($"{gap} MinTargetForCall: {minTargetForCall} MaxTargetForPut: {maxTargetForPut} triggerPrice: {triggerPrice} interesting: {interesting}%");

            if (!triggerPrices.Contains(triggerPrice))
            {
                triggerPrices.Add(triggerPrice);
            }
        }

        return triggerPrices;
    }

    /// <summary>
    /// Validates trigger prices and builds iron condor positions.
    /// Validates legs exist and handles conflicts with user interaction.
    /// </summary>
    private Dictionary<double, IronCondorStrikes> ValidateAndBuildPositions(
        List<double> triggerPrices,
        HashSet<double> availableStrikes)
    {
        var validatedPositions = new Dictionary<double, IronCondorStrikes>();
        var validatedTriggers = new List<double>();

        foreach (var triggerPrice in triggerPrices)
        {
            var currentTrigger = triggerPrice;

            // Validate trigger price (legs exist and no conflicts)
            var (isValid, missingStrikes, conflictingTriggers) = _conflictDetectionService.ValidateTriggerPrice(
                currentTrigger, availableStrikes, validatedTriggers);

            if (!isValid)
            {
                // Check if legs are missing
                if (missingStrikes.Count > 0)
                {
                    // Suggest shifted alternatives
                    double shiftUp = currentTrigger + 1;
                    double shiftDown = currentTrigger - 1;

                    var (shiftUpValid, _, _) = _conflictDetectionService.ValidateTriggerPrice(
                        shiftUp, availableStrikes, validatedTriggers);
                    var (shiftDownValid, _, _) = _conflictDetectionService.ValidateTriggerPrice(
                        shiftDown, availableStrikes, validatedTriggers);

                    var selectedTrigger = _userInteractionService.GetShiftedTriggerSelection(
                        currentTrigger, missingStrikes, shiftUp, shiftUpValid, shiftDown, shiftDownValid);

                    if (selectedTrigger == null)
                        continue; // User chose to skip

                    currentTrigger = selectedTrigger.Value;
                }
                else if (conflictingTriggers.Count > 0)
                {
                    // Handle conflict - suggest shifted alternatives
                    _logger.LogWarning($"Trigger {currentTrigger} conflicts with: {string.Join(", ", conflictingTriggers)}");

                    double shiftUp = currentTrigger + 1;
                    double shiftDown = currentTrigger - 1;

                    var (shiftUpValid, shiftUpMissing, _) = _conflictDetectionService.ValidateTriggerPrice(
                        shiftUp, availableStrikes, validatedTriggers);
                    var (shiftDownValid, shiftDownMissing, _) = _conflictDetectionService.ValidateTriggerPrice(
                        shiftDown, availableStrikes, validatedTriggers);

                    var selectedTrigger = _userInteractionService.GetShiftedTriggerSelection(
                        currentTrigger,
                        new List<double>(), // No missing strikes, it's a conflict
                        shiftUp, shiftUpValid,
                        shiftDown, shiftDownValid);

                    if (selectedTrigger == null)
                        continue; // User chose to skip

                    currentTrigger = selectedTrigger.Value;
                }
            }

            // Build the iron condor strikes
            var strikes = new IronCondorStrikes(
                callSell: currentTrigger + TradingParameters.Spread,
                callBuy: currentTrigger + (2 * TradingParameters.Spread),
                putSell: currentTrigger - TradingParameters.Spread,
                putBuy: currentTrigger - (2 * TradingParameters.Spread),
                triggerPrice: currentTrigger
            );

            validatedPositions[currentTrigger] = strikes;
            validatedTriggers.Add(currentTrigger);
            _logger.LogSuccess($"Validated trigger: {currentTrigger}");
        }

        return validatedPositions;
    }

    /// <summary>
    /// Builds option contracts for all strikes.
    /// </summary>
    private List<Contract> BuildOptionContracts(string symbol, HashSet<double> strikes, string expiration)
    {
        var contracts = new List<Contract>();

        if (_underlyingContract == null)
        {
            _logger.LogError("Underlying contract is null");
            return contracts;
        }

        // Only use integer strikes
        foreach (var strike in strikes.Where(s => s == (int)s))
        {
            var contract = new Contract
            {
                Symbol = symbol,
                Currency = _underlyingContract.Currency,
                Exchange = _underlyingContract.Exchange,
                SecType = "OPT",
                Strike = strike,
                LastTradeDateOrContractMonth = expiration
            };
            contracts.Add(contract);
        }

        return contracts;
    }

    /// <summary>
    /// Places orders for all iron condor positions.
    /// </summary>
    private async Task PlaceOrdersAsync(
        Dictionary<double, IronCondorStrikes> ironCondorPositions,
        CancellationToken cancellationToken)
    {
        if (_underlyingContract == null)
        {
            _logger.LogError("Cannot place orders: underlying contract is null");
            return;
        }

        int successCount = 0;
        int failureCount = 0;

        foreach (var kvp in ironCondorPositions)
        {
            var triggerPrice = kvp.Key;
            var strikes = kvp.Value;

            _logger.LogInfo($"Placing order for trigger price: {triggerPrice}");

            var success = await _orderService.PlaceIronCondorOrderAsync(
                _underlyingContract,
                triggerPrice,
                TradingParameters.Spread,
                _quantity,
                _lastUnderlyingPrice,
                _weeklyPriceBars,
                cancellationToken);

            if (success)
            {
                successCount++;
            }
            else
            {
                failureCount++;
            }
        }

        _logger.LogSuccess($"Orders placed: {successCount} successful, {failureCount} failed");
    }
}

/// <summary>
/// Event args for historical data received event.
/// </summary>
public class HistoricalDataEventArgs : EventArgs
{
    public int RequestId { get; }
    public string Date { get; }
    public double Open { get; }
    public double High { get; }
    public double Low { get; }
    public double Close { get; }
    public double Volume { get; }

    public HistoricalDataEventArgs(int requestId, string date, double open, double high, double low, double close, double volume)
    {
        RequestId = requestId;
        Date = date;
        Open = open;
        High = high;
        Low = low;
        Close = close;
        Volume = volume;
    }
}

/// <summary>
/// Event args for historical data end event.
/// </summary>
public class HistoricalDataEndEventArgs : EventArgs
{
    public int RequestId { get; }

    public HistoricalDataEndEventArgs(int requestId)
    {
        RequestId = requestId;
    }
}

/// <summary>
/// Event args for security definition option parameter event.
/// </summary>
public class SecurityDefinitionOptionParameterEventArgs : EventArgs
{
    public int ReqId { get; }
    public string Exchange { get; }
    public string TradingClass { get; }
    public HashSet<double> Strikes { get; }
    public HashSet<string> Expirations { get; }

    public SecurityDefinitionOptionParameterEventArgs(
        int reqId,
        string exchange,
        string tradingClass,
        HashSet<double> strikes,
        HashSet<string> expirations)
    {
        ReqId = reqId;
        Exchange = exchange;
        TradingClass = tradingClass;
        Strikes = strikes;
        Expirations = expirations;
    }
}

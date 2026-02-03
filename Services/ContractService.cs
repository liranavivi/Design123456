using IBApi;
using IronCondor.Constants;
using IronCondor.Infrastructure;
using IronCondor.Models;
using System.Text.RegularExpressions;

namespace IronCondor.Services;

/// <summary>
/// Service for managing contract details and strike mappings.
/// </summary>
public class ContractService
{
    private readonly TwsClientWrapper _client;
    private readonly ILogger _logger;
    private readonly Dictionary<(double Strike, string Right), int> _conIds;
    private readonly Dictionary<string, Match> _tradingHours;

    public ContractService(TwsClientWrapper client, ILogger logger)
    {
        _client = client;
        _logger = logger;
        _conIds = new Dictionary<(double Strike, string Right), int>();
        _tradingHours = new Dictionary<string, Match>();
    }

    /// <summary>
    /// Gets the contract ID mapping ((strike, right) -> conId).
    /// </summary>
    public IReadOnlyDictionary<(double Strike, string Right), int> ConIds => _conIds;

    /// <summary>
    /// Gets the strike price from a contract ID.
    /// </summary>
    public double GetStrikeFromConId(int conId)
    {
        // Reverse lookup in conIds dictionary
        foreach (var kvp in _conIds)
        {
            if (kvp.Value == conId)
                return kvp.Key.Strike;
        }
        return 0; // Not found
    }

    /// <summary>
    /// Builds iron condor positions from contract details and probability maps.
    /// Detects conflicts, resolves them with minimum adjustments, and lets user select from resolved triggers.
    /// </summary>
    public async Task<Dictionary<double, IronCondorStrikes>> BuildIronCondorPositionsAsync(
        List<Contract> contracts,
        string gap,
        List<int> interestingPoints,
        StrikeProbabilityMap strikeProbabilitiesFromWeekLow,
        StrikeProbabilityMap strikeProbabilitiesFromWeekHigh,
        ConflictDetectionService conflictDetectionService,
        UserInteractionService userInteractionService,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInfo("Building iron condor positions from contract details...");

        // Step 1: Extract trigger prices from interesting points
        var originalTriggers = new List<double>();

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

            if (!originalTriggers.Contains(triggerPrice))
            {
                originalTriggers.Add(triggerPrice);
            }
        }

        if (originalTriggers.Count == 0)
        {
            _logger.LogWarning("No valid trigger prices could be extracted from interesting points.");
            return new Dictionary<double, IronCondorStrikes>();
        }

        // Step 2: Detect conflicts (trigger prices that differ by exactly Spread)
        var conflicts = conflictDetectionService.DetectConflictsByTriggerPrice(originalTriggers);

        // Step 3: Resolve conflicts with minimum adjustments
        var adjustments = conflictDetectionService.ResolveConflicts(originalTriggers);

        // Step 4: Get user selection from resolved triggers
        var selectedTriggers = userInteractionService.GetUserSelectionFromResolvedTriggers(
            originalTriggers, conflicts, adjustments);

        // Step 5: Build IronCondorStrikes for selected triggers
        var selectedPositions = new Dictionary<double, IronCondorStrikes>();
        foreach (var triggerPrice in selectedTriggers)
        {
            var strikes = new IronCondorStrikes(
                callSell: triggerPrice + TradingParameters.Spread,
                callBuy: triggerPrice + (2 * TradingParameters.Spread),
                putSell: triggerPrice - TradingParameters.Spread,
                putBuy: triggerPrice - (2 * TradingParameters.Spread),
                triggerPrice: triggerPrice
            );
            selectedPositions[triggerPrice] = strikes;
        }

        _logger.LogSuccess($"✅ Selected iron condors: {selectedPositions.Count}");

        // Request contract details for each trigger price
        _conIds.Clear();
        _tradingHours.Clear();

        Regex regex = new Regex("(?<OpenDate>\\d{8}:\\d{4})-(?<CloseDate>\\d{8}:\\d{4})");

        // Track trigger prices to remove (can't modify collection while iterating)
        var triggersToRemove = new List<double>();

        foreach (var triggerPrice in selectedPositions.Keys)
        {
            double[] strikes = {
                triggerPrice + TradingParameters.Spread,
                triggerPrice + (2 * TradingParameters.Spread),
                triggerPrice - TradingParameters.Spread,
                triggerPrice - (2 * TradingParameters.Spread)
            };

            double[] callStrikes = strikes.Where(s => s >= triggerPrice).ToArray();
            double[] putStrikes = strikes.Where(s => s < triggerPrice).ToArray();

            List<Contract> callContractChances = contracts.Where(c => callStrikes.Contains(c.Strike)).ToList();
            callContractChances.ForEach(c => c.Right = "C");

            List<Contract> putContractChances = contracts.Where(c => putStrikes.Contains(c.Strike)).ToList();
            putContractChances.ForEach(c => c.Right = "P");

            List<Contract> selectedContractChances = new List<Contract>();
            selectedContractChances.AddRange(callContractChances);
            selectedContractChances.AddRange(putContractChances);

            // Temporary storage for this trigger price's contract details
            var tempConIds = new Dictionary<(double Strike, string Right), int>();
            var tempTradingHours = new Dictionary<string, Match>();
            bool allSucceeded = true;

            // Request contract details for each strike
            for (int i = 0; i < selectedContractChances.Count; i++)
            {
                var contract = selectedContractChances[i];
                var reqId = (int)contract.Strike;

                try
                {
                    var contractDetails = await RequestContractDetailsAsync(reqId, contract, cancellationToken);

                    if (contractDetails != null)
                    {
                        tempConIds[(contract.Strike, contract.Right)] = contractDetails.Contract.ConId;

                        // Parse trading hours
                        foreach (string tradingHour in contractDetails.LiquidHours.Split(';'))
                        {
                            if (regex.Match(tradingHour).Success)
                            {
                                if (!tempTradingHours.ContainsKey(tradingHour))
                                    tempTradingHours.Add(tradingHour, regex.Match(tradingHour));
                            }
                        }

                        _logger.LogSuccess($"Request Contract Details succeeded {contract.Symbol} {contract.Right} {contract.Strike} for TPrice: {triggerPrice}");
                    }
                    else
                    {
                        _logger.LogWarning($"Contract details returned null for {contract.Symbol} {contract.Right} {contract.Strike} TPrice: {triggerPrice}");
                        allSucceeded = false;
                        break;
                    }
                }
                catch (TimeoutException)
                {
                    _logger.LogError($"Timeout - Request contract detail failed for {contract.Symbol} {contract.Right} {contract.Strike} TPrice: {triggerPrice}");
                    allSucceeded = false;
                    break;
                }
            }

            // Only add to _conIds if all contracts for this trigger price succeeded
            if (allSucceeded)
            {
                foreach (var kvp in tempConIds)
                {
                    _conIds[kvp.Key] = kvp.Value;
                }
                foreach (var kvp in tempTradingHours)
                {
                    if (!_tradingHours.ContainsKey(kvp.Key))
                        _tradingHours.Add(kvp.Key, kvp.Value);
                }
            }
            else
            {
                _logger.LogWarning($"Removing trigger price {triggerPrice} due to failed contract details");
                triggersToRemove.Add(triggerPrice);
            }
        }

        // Remove failed trigger prices from selectedPositions
        foreach (var trigger in triggersToRemove)
        {
            selectedPositions.Remove(trigger);
        }

        // Log trading hours
        LogTradingHours();

        return selectedPositions;
    }

    /// <summary>
    /// Requests contract details for a specific contract.
    /// </summary>
    private async Task<ContractDetails?> RequestContractDetailsAsync(
        int reqId,
        Contract contract,
        CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<ContractDetails?>();

        EventHandler<ContractDetailsEventArgs>? handler = null;
        handler = (sender, args) =>
        {
            if (args.RequestId == reqId)
            {
                tcs.TrySetResult(args.ContractDetails);
            }
        };

        try
        {
            _client.ContractDetailsReceived += handler;
            _client.ClientSocket.reqContractDetails(reqId, contract);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(Timeouts.ContractDetails);

            return await tcs.Task.WaitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException($"Contract details request timed out for reqId: {reqId}");
        }
        finally
        {
            _client.ContractDetailsReceived -= handler;
        }
    }

    /// <summary>
    /// Requests contract details for validated iron condor positions.
    /// </summary>
    public async Task<Dictionary<double, IronCondorStrikes>> RequestContractDetailsForPositionsAsync(
        List<Contract> contracts,
        Dictionary<double, IronCondorStrikes> validatedPositions,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInfo("Requesting contract details for validated positions...");

        _conIds.Clear();
        _tradingHours.Clear();

        Regex regex = new Regex("(?<OpenDate>\\d{8}:\\d{4})-(?<CloseDate>\\d{8}:\\d{4})");

        // Track trigger prices to remove (can't modify collection while iterating)
        var triggersToRemove = new List<double>();

        foreach (var kvp in validatedPositions)
        {
            var triggerPrice = kvp.Key;
            var strikes = kvp.Value;

            double[] strikeValues = { strikes.CallSell, strikes.CallBuy, strikes.PutSell, strikes.PutBuy };

            double[] callStrikes = strikeValues.Where(s => s >= triggerPrice).ToArray();
            double[] putStrikes = strikeValues.Where(s => s < triggerPrice).ToArray();

            List<Contract> callContractChances = contracts.Where(c => callStrikes.Contains(c.Strike)).ToList();
            callContractChances.ForEach(c => c.Right = "C");

            List<Contract> putContractChances = contracts.Where(c => putStrikes.Contains(c.Strike)).ToList();
            putContractChances.ForEach(c => c.Right = "P");

            List<Contract> selectedContractChances = new List<Contract>();
            selectedContractChances.AddRange(callContractChances);
            selectedContractChances.AddRange(putContractChances);

            // Temporary storage for this trigger price's contract details
            var tempConIds = new Dictionary<(double Strike, string Right), int>();
            var tempTradingHours = new Dictionary<string, Match>();
            bool allSucceeded = true;

            // Request contract details for each strike
            for (int i = 0; i < selectedContractChances.Count; i++)
            {
                var contract = selectedContractChances[i];
                var reqId = (int)contract.Strike;

                try
                {
                    var contractDetails = await RequestContractDetailsAsync(reqId, contract, cancellationToken);

                    if (contractDetails != null)
                    {
                        tempConIds[(contract.Strike, contract.Right)] = contractDetails.Contract.ConId;

                        // Parse trading hours
                        foreach (string tradingHour in contractDetails.LiquidHours.Split(';'))
                        {
                            if (regex.Match(tradingHour).Success)
                            {
                                if (!tempTradingHours.ContainsKey(tradingHour))
                                    tempTradingHours.Add(tradingHour, regex.Match(tradingHour));
                            }
                        }

                        _logger.LogSuccess($"Request Contract Details succeeded {contract.Symbol} {contract.Right} {contract.Strike} for TPrice: {triggerPrice}");
                    }
                    else
                    {
                        _logger.LogWarning($"Contract details returned null for {contract.Symbol} {contract.Right} {contract.Strike} TPrice: {triggerPrice}");
                        allSucceeded = false;
                        break;
                    }
                }
                catch (TimeoutException)
                {
                    _logger.LogError($"Timeout - Request contract detail failed for {contract.Symbol} {contract.Right} {contract.Strike} TPrice: {triggerPrice}");
                    allSucceeded = false;
                    break;
                }
            }

            // Only add to _conIds if all contracts for this trigger price succeeded
            if (allSucceeded)
            {
                foreach (var conIdKvp in tempConIds)
                {
                    _conIds[conIdKvp.Key] = conIdKvp.Value;
                }
                foreach (var thKvp in tempTradingHours)
                {
                    if (!_tradingHours.ContainsKey(thKvp.Key))
                        _tradingHours.Add(thKvp.Key, thKvp.Value);
                }
            }
            else
            {
                _logger.LogWarning($"Removing trigger price {triggerPrice} due to failed contract details");
                triggersToRemove.Add(triggerPrice);
            }
        }

        // Remove failed trigger prices from validatedPositions
        var result = new Dictionary<double, IronCondorStrikes>();
        foreach (var kvp in validatedPositions)
        {
            if (!triggersToRemove.Contains(kvp.Key))
            {
                result[kvp.Key] = kvp.Value;
            }
        }

        // Log trading hours
        LogTradingHours();

        _logger.LogSuccess($"✅ Contract details received for {result.Count} positions");

        return result;
    }

    /// <summary>
    /// Logs trading hours information.
    /// </summary>
    private void LogTradingHours()
    {
        // Find the last trading day (by close date)
        DateTime? lastCloseDate = null;

        foreach (string tradingHour in _tradingHours.Keys)
        {
            var openDate = DateTime.ParseExact(_tradingHours[tradingHour].Groups["OpenDate"].Value, "yyyyMMdd:HHmm", null);
            var closeDate = DateTime.ParseExact(_tradingHours[tradingHour].Groups["CloseDate"].Value, "yyyyMMdd:HHmm", null);
            TimeSpan diff = closeDate - openDate;

            if (diff.Hours != 6 || diff.Minutes != 45)
            {
                _logger.LogWarning($"WARNING IRREGULAR Trading hours: {diff.Hours}H{diff.Minutes}M {closeDate.DayOfWeek}");
            }
            else
            {
                _logger.LogInfo($"Trading hours: {diff.Hours}H{diff.Minutes}M {closeDate.DayOfWeek}");
            }

            // Track the last close date
            if (lastCloseDate == null || closeDate > lastCloseDate)
            {
                lastCloseDate = closeDate;
            }
        }

        // Check if the last trading day is Friday
        if (lastCloseDate.HasValue && lastCloseDate.Value.DayOfWeek != DayOfWeek.Friday)
        {
            _logger.LogWarning($"WARNING: Last trading day is {lastCloseDate.Value.DayOfWeek}, expected Friday");
        }
    }
}

/// <summary>
/// Event args for contract details received event.
/// </summary>
public class ContractDetailsEventArgs : EventArgs
{
    public int RequestId { get; }
    public ContractDetails ContractDetails { get; }

    public ContractDetailsEventArgs(int requestId, ContractDetails contractDetails)
    {
        RequestId = requestId;
        ContractDetails = contractDetails;
    }
}

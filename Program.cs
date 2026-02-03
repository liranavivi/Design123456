/* Copyright (C) 2025 Interactive Brokers LLC. All rights reserved. This code is subject to the terms
 * and conditions of the IB API Non-Commercial License or the IB API Commercial License, as applicable. */

using IronCondor.Infrastructure;
using IronCondor.Services;

namespace IronCondor;

/// <summary>
/// IronCondor - TWS API .NET 9 Console Application
/// 
/// This application demonstrates how to connect to Interactive Brokers TWS/Gateway
/// using the TWS API with .NET 9.
/// 
/// IMPORTANT: Always use your paper trading account for testing.
/// The code may submit orders as part of demonstrations.
/// IB will not be responsible for accidental executions on your live account.
/// </summary>
class Program
{
    static async Task<int> Main(string[] args)
    {
        Console.WriteLine("=".PadRight(80, '='));
        Console.WriteLine("IronCondor - TWS API .NET 9 Console Application");
        Console.WriteLine("Interactive Brokers TWS API Integration");
        Console.WriteLine("=".PadRight(80, '='));
        Console.WriteLine();

        // If no arguments provided, prompt user interactively
        if (args.Length == 0)
        {
            args = PromptForArguments();
        }

        // Parse command line arguments or use defaults
        var (config, symbol, placeOrders, quantity) = ParseArguments(args);

        // Create connection manager
        using var connectionManager = new TwsConnectionManager(config);

        // Connect to TWS/Gateway
        var connected = await connectionManager.ConnectAsync();

        if (!connected)
        {
            Console.WriteLine("[ERROR] Failed to connect to TWS/Gateway");
            Console.WriteLine("[INFO] Please ensure TWS or IB Gateway is running and API connections are enabled");
            Console.WriteLine("[INFO] TWS: Configure > API > Settings > Enable ActiveX and Socket Clients");
            return 1;
        }

        // Connection successful - run iron condor strategy
        await RunStrategyAsync(connectionManager, symbol, placeOrders, quantity);

        // Wait for user input before disconnecting
        Console.WriteLine();
        Console.WriteLine("Press any key to disconnect and exit...");
        Console.ReadKey();

        // Disconnect (also handled by Dispose)
        connectionManager.Disconnect();

        Console.WriteLine();
        Console.WriteLine("Application terminated successfully");
        return 0;
    }

    /// <summary>
    /// Runs the iron condor strategy
    /// </summary>
    private static async Task RunStrategyAsync(TwsConnectionManager manager, string symbol, bool placeOrders, double quantity)
    {
        Console.WriteLine();
        Console.WriteLine("-".PadRight(80, '-'));
        Console.WriteLine("Iron Condor Strategy Execution");
        Console.WriteLine("-".PadRight(80, '-'));
        Console.WriteLine();

        // Request current server time
        manager.RequestCurrentTime();
        await Task.Delay(1000);

        // Create loggers (console + file)
        using var fileLogger = new FileLogger(); // Logs to "logs/log_yyyyMMdd.txt"
        using var logger = new CompositeLogger(new ConsoleLogger(), fileLogger);

        // Create services
        var backTestService = new BackTestService(logger);
        var strikeProbabilityService = new StrikeProbabilityService(logger);
        var contractService = new ContractService(manager.Wrapper, logger);
        var conflictDetectionService = new ConflictDetectionService(logger);
        var userInteractionService = new UserInteractionService(logger);
        var orderService = new OrderService(manager.Wrapper, logger, contractService);
        var chartService = new ChartService(logger);

        // Create orchestrator
        var orchestrator = new StrategyOrchestrator(
            manager.Wrapper,
            backTestService,
            strikeProbabilityService,
            contractService,
            conflictDetectionService,
            userInteractionService,
            orderService,
            chartService,
            logger);

        Console.WriteLine();
        logger.LogInfo($"Starting strategy for {symbol} (Place Orders: {placeOrders}, Quantity: {quantity})");
        Console.WriteLine();

        // Run strategy
        using var cts = new CancellationTokenSource();

        // Handle Ctrl+C gracefully
        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true;
            logger.LogWarning("Cancellation requested...");
            cts.Cancel();
        };

        try
        {
            var success = await orchestrator.RunStrategyAsync(symbol, placeOrders, quantity, cts.Token);

            Console.WriteLine();
            if (success)
            {
                logger.LogSuccess("✅ Strategy execution completed successfully");
            }
            else
            {
                logger.LogError("❌ Strategy execution failed");
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Strategy execution cancelled by user");
        }
        catch (Exception ex)
        {
            logger.LogError($"Strategy execution error: {ex.Message}");
        }

        Console.WriteLine();
        logger.LogInfo("Strategy execution finished");
    }

    /// <summary>
    /// Prompts user for command line arguments interactively
    /// </summary>
    private static string[] PromptForArguments()
    {
        Console.WriteLine("No command line arguments provided. Enter arguments interactively:");
        Console.WriteLine("(Press Enter to skip and use defaults, or type --help for options)");
        Console.WriteLine();

        var arguments = new List<string>();

        // Connection type
        Console.WriteLine("Connection Type:");
        Console.WriteLine("  1. TWS Paper Trading (default)");
        Console.WriteLine("  2. TWS Live Trading");
        Console.WriteLine("  3. IB Gateway Paper Trading");
        Console.WriteLine("  4. IB Gateway Live Trading");
        Console.WriteLine("  5. Custom (specify host/port)");
        Console.Write("Select [1-5] (default: 1): ");

        // Flush output before reading input
        Console.Out.Flush();
        var connectionChoice = ReadLineWithFlush();

        // Default to option 1 if empty
        if (string.IsNullOrEmpty(connectionChoice))
        {
            connectionChoice = "1";
        }

        switch (connectionChoice)
        {
            case "1":
                // Use default (TWS Paper Trading) - no arguments needed
                break;
            case "2":
                arguments.Add("--live");
                break;
            case "3":
                arguments.Add("--gateway");
                break;
            case "4":
                arguments.Add("--gateway-live");
                break;
            case "5":
                Console.Write("Host (default: 127.0.0.1): ");
                Console.Out.Flush();
                var host = ReadLineWithFlush();
                if (!string.IsNullOrEmpty(host))
                {
                    arguments.Add("--host");
                    arguments.Add(host);
                }

                Console.Write("Port (default: 7497): ");
                Console.Out.Flush();
                var port = ReadLineWithFlush();
                if (!string.IsNullOrEmpty(port))
                {
                    arguments.Add("--port");
                    arguments.Add(port);
                }

                Console.Write("Client ID (default: 0): ");
                Console.Out.Flush();
                var clientId = ReadLineWithFlush();
                if (!string.IsNullOrEmpty(clientId))
                {
                    arguments.Add("--clientid");
                    arguments.Add(clientId);
                }
                break;
            default:
                // Invalid choice - use default (TWS Paper Trading)
                Console.WriteLine($"Invalid choice '{connectionChoice}'. Using default: TWS Paper Trading");
                break;
        }

        Console.WriteLine();

        // Symbol
        Console.Write("Symbol to analyze (default: SPY): ");
        Console.Out.Flush();
        var symbol = ReadLineWithFlush()?.ToUpper();
        if (!string.IsNullOrEmpty(symbol))
        {
            arguments.Add("--symbol");
            arguments.Add(symbol);
        }

        // Quantity
        Console.Write("Quantity per order (default: 4.0): ");
        Console.Out.Flush();
        var quantity = ReadLineWithFlush();
        if (!string.IsNullOrEmpty(quantity))
        {
            arguments.Add("--quantity");
            arguments.Add(quantity);
        }

        // Place orders
        Console.Write("Place orders? (y/n) (default: n): ");
        Console.Out.Flush();
        var placeOrders = ReadLineWithFlush()?.ToLower();
        if (placeOrders == "y" || placeOrders == "yes")
        {
            arguments.Add("--place-orders");
        }

        Console.WriteLine();
        Console.WriteLine("Configuration complete!");
        Console.WriteLine();

        return arguments.ToArray();
    }

    /// <summary>
    /// Reads a line from console with proper input buffer handling.
    /// Clears any buffered keys before reading to avoid multiple Enter presses issue.
    /// </summary>
    private static string? ReadLineWithFlush()
    {
        // Clear any buffered keyboard input
        while (Console.KeyAvailable)
        {
            Console.ReadKey(intercept: true);
        }

        return Console.ReadLine()?.Trim();
    }

    /// <summary>
    /// Parses command line arguments to create connection configuration and strategy parameters
    /// </summary>
    private static (TwsConnectionConfig config, string symbol, bool placeOrders, double quantity) ParseArguments(string[] args)
    {
        var config = TwsConnectionConfig.PaperTrading();
        string symbol = "SPY";
        bool placeOrders = false;
        double quantity = 4.0;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLower())
            {
                case "--host":
                case "-h":
                    if (i + 1 < args.Length)
                        config.Host = args[++i];
                    break;

                case "--port":
                case "-p":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out int port))
                        config.Port = port;
                    break;

                case "--clientid":
                case "-c":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out int clientId))
                        config.ClientId = clientId;
                    break;

                case "--symbol":
                case "-s":
                    if (i + 1 < args.Length)
                        symbol = args[++i];
                    break;

                case "--place-orders":
                case "-o":
                    placeOrders = true;
                    break;

                case "--quantity":
                case "-q":
                    if (i + 1 < args.Length && double.TryParse(args[++i], out double qty))
                        quantity = qty;
                    break;

                case "--live":
                    config = TwsConnectionConfig.LiveTrading();
                    break;

                case "--gateway":
                    config = TwsConnectionConfig.GatewayPaperTrading();
                    break;

                case "--gateway-live":
                    config = TwsConnectionConfig.GatewayLiveTrading();
                    break;

                case "--help":
                    PrintUsage();
                    Environment.Exit(0);
                    break;
            }
        }

        return (config, symbol, placeOrders, quantity);
    }

    /// <summary>
    /// Prints usage information
    /// </summary>
    private static void PrintUsage()
    {
        Console.WriteLine("Usage: IronCondor [options]");
        Console.WriteLine();
        Console.WriteLine("Note: If no options are provided, the application will prompt interactively.");
        Console.WriteLine();
        Console.WriteLine("Connection Options:");
        Console.WriteLine("  -h, --host <host>        TWS/Gateway host address (default: 127.0.0.1)");
        Console.WriteLine("  -p, --port <port>        TWS/Gateway port number (default: 7497)");
        Console.WriteLine("  -c, --clientid <id>      Client ID (default: 0)");
        Console.WriteLine("  --live                   Connect to TWS Live Trading (port 7496)");
        Console.WriteLine("  --gateway                Connect to IB Gateway Paper Trading (port 4002)");
        Console.WriteLine("  --gateway-live           Connect to IB Gateway Live Trading (port 4001)");
        Console.WriteLine();
        Console.WriteLine("Strategy Options:");
        Console.WriteLine("  -s, --symbol <symbol>       Symbol to analyze (default: SPY)");
        Console.WriteLine("  -o, --place-orders          Place orders (default: false, analysis only)");
        Console.WriteLine("  -q, --quantity <qty>        Quantity per order (default: 4.0)");
        Console.WriteLine();
        Console.WriteLine("Other Options:");
        Console.WriteLine("  --help                   Show this help message");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  IronCondor");
        Console.WriteLine("    # Connect to TWS Paper Trading, analyze SPY, no orders");
        Console.WriteLine();
        Console.WriteLine("  IronCondor --gateway -s QQQ");
        Console.WriteLine("    # Connect to IB Gateway Paper Trading, analyze QQQ, no orders");
        Console.WriteLine();
        Console.WriteLine("  IronCondor -h 127.0.0.1 -p 4001 -c 1 -s SPY -o");
        Console.WriteLine("    # Connect to Gateway on port 4001, analyze SPY, place orders");
        Console.WriteLine();
        Console.WriteLine("  IronCondor --gateway -s AAPL -o");
        Console.WriteLine("    # Connect to IB Gateway Paper Trading, analyze AAPL, place orders");
        Console.WriteLine();
        Console.WriteLine("  IronCondor -s SPY -o -q 10");
        Console.WriteLine("    # Analyze SPY, place orders with quantity 10");
    }
}


using System.Net.Http.Headers;
using Newtonsoft.Json;
using SubscriptionLibraries.Core.Providers.GamePass;
using SubscriptionLibraries.Core.Services;

namespace SubscriptionLibraries.CatalogTool;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Any(argument => argument is "--help" or "-h" or "/?"))
        {
            PrintHelp();
            return 0;
        }

        try
        {
            var command = CommandLineOptions.Parse(args);
            using var cancellation = new CancellationTokenSource();
            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cancellation.Cancel();
            };

            using var httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            httpClient.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("SubscriptionLibraries.CatalogTool", "1.0"));
            var logger = new ConsoleSubscriptionLogger();
            var options = new GamePassProviderOptions
            {
                Region = command.Region,
                Language = command.Language,
                SiglId = command.SiglId,
                ConsoleSiglId = command.ConsoleSiglId
            }.NormalizeAndValidate();
            var transport = new HttpClientService(httpClient, logger);
            var provider = new GamePassProvider(
                new GamePassCatalogClient(transport, options),
                new MicrosoftStoreCatalogClient(transport, options, logger),
                new GamePassPlatformClassifier(),
                options,
                logger);

            var result = await provider.GetCatalogAsync(cancellation.Token).ConfigureAwait(false);
            PrintResult(options, result, command.Selection, command.Plan,
                command.ConsoleSelection, command.IncludeRejected);
            if (!string.IsNullOrWhiteSpace(command.JsonOutputPath))
            {
                WriteJson(command.JsonOutputPath!, result);
                Console.WriteLine();
                Console.WriteLine($"Wrote normalized diagnostics to {Path.GetFullPath(command.JsonOutputPath!)}");
            }

            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Catalog request canceled.");
            return 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Catalog request failed: {exception.Message}");
            return 1;
        }
    }

    private static void PrintResult(
        GamePassProviderOptions options,
        GamePassCatalogResult result,
        GamePassCatalogSelection selection,
        GamePassPlanSelection plan,
        GamePassConsoleSelection consoleSelection,
        bool includeRejected)
    {
        var diagnostics = result.Diagnostics;
        var selectedGames = result.Games
            .Where(game => selection.Includes(plan, consoleSelection, game))
            .Select(game => plan.Project(selection, consoleSelection, game))
            .ToList();
        Console.WriteLine();
        Console.WriteLine("Game Pass");
        Console.WriteLine($"Region: {options.Region}");
        Console.WriteLine($"Language: {options.Language}");
        Console.WriteLine($"PC SIGL: {options.SiglId}");
        Console.WriteLine($"Xbox console SIGL: {options.ConsoleSiglId}");
        Console.WriteLine($"Plan: {plan.DisplayName()}");
        Console.WriteLine($"Selection: {selection}");
        Console.WriteLine($"Xbox generation: {consoleSelection}");
        Console.WriteLine();
        Console.WriteLine($"Total catalog IDs: {diagnostics.TotalCatalogIds}");
        Console.WriteLine($"PC catalog IDs: {diagnostics.PcCatalogIds}");
        Console.WriteLine($"Xbox console catalog IDs: {diagnostics.ConsoleCatalogIds}");
        Console.WriteLine($"Products successfully resolved: {diagnostics.ProductsSuccessfullyResolved}");
        Console.WriteLine($"Products identified as PC: {diagnostics.ProductsIdentifiedAsPc}");
        Console.WriteLine($"Products identified as Xbox console: {diagnostics.ProductsIdentifiedAsConsole}");
        Console.WriteLine($"Products in both: {diagnostics.ProductsInBothCatalogs}");
        Console.WriteLine($"Products with PC catalog membership but no Windows evidence: {diagnostics.ProductsRejectedAsNonPc}");
        Console.WriteLine($"Products that could not be classified: {diagnostics.ProductsThatCouldNotBeClassified}");
        Console.WriteLine($"Product IDs without metadata: {diagnostics.ProductIdsWithoutMetadata}");
        Console.WriteLine($"Games in selected view: {selectedGames.Count}");
        Console.WriteLine($"Leaving soon in selected view: {selectedGames.Count(game => game.Availability == SubscriptionLibraries.Core.Models.SubscriptionAvailability.LeavingSoon)}");
        Console.WriteLine($"Confirmed free-to-play in selected view: {selectedGames.Count(game => game.IsConfirmedFreeToPlay)}");
        Console.WriteLine($"Leaving-soon feed verified: {result.LeavingSoonStatusKnown}");
        foreach (var tier in new[]
                 {
                     GamePassPlanSelection.PcGamePass,
                     GamePassPlanSelection.XboxGamePassConsole,
                     GamePassPlanSelection.Essential,
                     GamePassPlanSelection.Premium,
                     GamePassPlanSelection.Ultimate
                 })
        {
            var pcCount = result.Games.Count(game =>
                (tier.EligiblePlatforms(game) & SubscriptionLibraries.Core.Models.SubscriptionPlatforms.WindowsPc) != 0);
            var xboxCount = result.Games.Count(game =>
                (tier.EligiblePlatforms(game) & SubscriptionLibraries.Core.Models.SubscriptionPlatforms.XboxConsole) != 0);
            Console.WriteLine($"{tier.DisplayName()}: {pcCount} PC, {xboxCount} Xbox");
        }
        Console.WriteLine();
        Console.WriteLine("Sample:");

        foreach (var game in selectedGames.OrderBy(game => game.Name).Take(10))
        {
            Console.WriteLine();
            Console.WriteLine(game.Name);
            Console.WriteLine($"Microsoft ID: {game.MicrosoftProductId}");
            Console.WriteLine($"Platform: {game.Platform}");
            Console.WriteLine($"Xbox generation: {game.XboxGenerations}");
            Console.WriteLine($"Availability: {game.Availability}");
            Console.WriteLine($"Subscription: {game.SubscriptionTier}");
        }

        if (includeRejected && result.RejectedProducts.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Rejected/unclassified products:");
            foreach (var product in result.RejectedProducts.OrderBy(product => product.Name))
            {
                Console.WriteLine(
                    $"- {product.Name ?? "<missing title>"} ({product.ProductId ?? "<missing id>"}): " +
                    $"{product.Classification} - {product.Reason}");
            }
        }
    }

    private static void WriteJson(string outputPath, GamePassCatalogResult result)
    {
        var fullPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(
                temporaryPath,
                JsonConvert.SerializeObject(result, Formatting.Indented));
            File.Move(temporaryPath, fullPath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Fetch and diagnose the live Game Pass plan and platform catalogs.");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run --project SubscriptionLibraries.CatalogTool -- [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --region <code>       Market code (default: US)");
        Console.WriteLine("  --language <tag>      Language tag (default: en-US)");
        Console.WriteLine("  --selection <value>   pc, xbox, or both (default: pc)");
        Console.WriteLine("  --xbox-generation <value> both, one, or series (default: both)");
        Console.WriteLine("  --plan <value>        pc, console, essential, premium, ultimate, or all (default: ultimate)");
        Console.WriteLine("  --sigl-id <guid>      Override the PC catalog collection ID");
        Console.WriteLine("  --console-sigl-id <guid> Override the Xbox console collection ID");
        Console.WriteLine("  --include-rejected    Print products with failed PC classification");
        Console.WriteLine("  --json <path>         Write the normalized result as JSON");
        Console.WriteLine("  --help                Show this help");
    }
}

internal sealed class CommandLineOptions
{
    public string Region { get; private set; } = "US";

    public string Language { get; private set; } = "en-US";

    public string SiglId { get; private set; } = GamePassConstants.PcCatalogSiglId;

    public string ConsoleSiglId { get; private set; } = GamePassConstants.ConsoleCatalogSiglId;

    public GamePassCatalogSelection Selection { get; private set; } = GamePassCatalogSelection.PcOnly;

    public GamePassPlanSelection Plan { get; private set; } = GamePassPlanSelection.Ultimate;

    public GamePassConsoleSelection ConsoleSelection { get; private set; } =
        GamePassConsoleSelection.Both;

    public bool IncludeRejected { get; private set; }

    public string? JsonOutputPath { get; private set; }

    public static CommandLineOptions Parse(IReadOnlyList<string> args)
    {
        var result = new CommandLineOptions();
        for (var index = 0; index < args.Count; index++)
        {
            switch (args[index])
            {
                case "--region":
                    result.Region = ReadValue(args, ref index, "--region");
                    break;
                case "--language":
                    result.Language = ReadValue(args, ref index, "--language");
                    break;
                case "--sigl-id":
                    result.SiglId = ReadValue(args, ref index, "--sigl-id");
                    break;
                case "--console-sigl-id":
                    result.ConsoleSiglId = ReadValue(args, ref index, "--console-sigl-id");
                    break;
                case "--selection":
                    var selection = ReadValue(args, ref index, "--selection");
                    result.Selection = selection.ToLowerInvariant() switch
                    {
                        "pc" => GamePassCatalogSelection.PcOnly,
                        "xbox" => GamePassCatalogSelection.XboxOnly,
                        "both" => GamePassCatalogSelection.Both,
                        _ => throw new ArgumentException("--selection must be pc, xbox, or both.")
                    };
                    break;
                case "--xbox-generation":
                    var generation = ReadValue(args, ref index, "--xbox-generation");
                    result.ConsoleSelection = generation.ToLowerInvariant() switch
                    {
                        "both" => GamePassConsoleSelection.Both,
                        "one" => GamePassConsoleSelection.XboxOne,
                        "series" => GamePassConsoleSelection.SeriesXorS,
                        _ => throw new ArgumentException(
                            "--xbox-generation must be both, one, or series.")
                    };
                    break;
                case "--plan":
                    var plan = ReadValue(args, ref index, "--plan");
                    result.Plan = plan.ToLowerInvariant() switch
                    {
                        "pc" => GamePassPlanSelection.PcGamePass,
                        "console" => GamePassPlanSelection.XboxGamePassConsole,
                        "essential" => GamePassPlanSelection.Essential,
                        "premium" => GamePassPlanSelection.Premium,
                        "ultimate" => GamePassPlanSelection.Ultimate,
                        "all" => GamePassPlanSelection.AllCatalogs,
                        _ => throw new ArgumentException(
                            "--plan must be pc, console, essential, premium, ultimate, or all.")
                    };
                    break;
                case "--json":
                    result.JsonOutputPath = ReadValue(args, ref index, "--json");
                    break;
                case "--include-rejected":
                    result.IncludeRejected = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown argument: {args[index]}");
            }
        }

        return result;
    }

    private static string ReadValue(IReadOnlyList<string> args, ref int index, string option)
    {
        index++;
        if (index >= args.Count || string.IsNullOrWhiteSpace(args[index]))
        {
            throw new ArgumentException($"{option} requires a value.");
        }

        return args[index];
    }
}

internal sealed class ConsoleSubscriptionLogger : ISubscriptionLogger
{
    public void Debug(string message) => Write("DEBUG", message);

    public void Info(string message) => Write("INFO", message);

    public void Warn(string message) => Write("WARN", message);

    public void Warn(Exception exception, string message) =>
        Write("WARN", $"{message} ({exception.Message})");

    public void Error(Exception exception, string message) =>
        Write("ERROR", $"{message} ({exception.Message})");

    private static void Write(string level, string message) =>
        Console.Error.WriteLine($"[{DateTimeOffset.Now:HH:mm:ss}] {level} {message}");
}

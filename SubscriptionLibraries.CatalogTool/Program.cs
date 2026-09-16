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
                SiglId = command.SiglId
            }.NormalizeAndValidate();
            var transport = new HttpClientService(httpClient, logger);
            var provider = new GamePassProvider(
                new GamePassCatalogClient(transport, options),
                new MicrosoftStoreCatalogClient(transport, options, logger),
                new GamePassPlatformClassifier(),
                options,
                logger);

            var result = await provider.GetCatalogAsync(cancellation.Token).ConfigureAwait(false);
            PrintResult(options, result, command.IncludeRejected);
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
        bool includeRejected)
    {
        var diagnostics = result.Diagnostics;
        Console.WriteLine();
        Console.WriteLine("PC Game Pass");
        Console.WriteLine($"Region: {options.Region}");
        Console.WriteLine($"Language: {options.Language}");
        Console.WriteLine($"SIGL: {options.SiglId}");
        Console.WriteLine();
        Console.WriteLine($"Total catalog IDs: {diagnostics.TotalCatalogIds}");
        Console.WriteLine($"Products successfully resolved: {diagnostics.ProductsSuccessfullyResolved}");
        Console.WriteLine($"Products identified as PC: {diagnostics.ProductsIdentifiedAsPc}");
        Console.WriteLine($"Products rejected as non-PC: {diagnostics.ProductsRejectedAsNonPc}");
        Console.WriteLine($"Products that could not be classified: {diagnostics.ProductsThatCouldNotBeClassified}");
        Console.WriteLine($"Product IDs without metadata: {diagnostics.ProductIdsWithoutMetadata}");
        Console.WriteLine();
        Console.WriteLine("Sample:");

        foreach (var game in result.Games.OrderBy(game => game.Name).Take(10))
        {
            Console.WriteLine();
            Console.WriteLine(game.Name);
            Console.WriteLine($"Microsoft ID: {game.MicrosoftProductId}");
            Console.WriteLine($"Platform: {game.Platform}");
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
        Console.WriteLine("Fetch and diagnose the live PC Game Pass catalog.");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run --project SubscriptionLibraries.CatalogTool -- [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --region <code>       Market code (default: US)");
        Console.WriteLine("  --language <tag>      Language tag (default: en-US)");
        Console.WriteLine("  --sigl-id <guid>      Override the configured PC catalog collection ID");
        Console.WriteLine("  --include-rejected    Print products rejected by the PC classifier");
        Console.WriteLine("  --json <path>         Write the normalized result as JSON");
        Console.WriteLine("  --help                Show this help");
    }
}

internal sealed class CommandLineOptions
{
    public string Region { get; private set; } = "US";

    public string Language { get; private set; } = "en-US";

    public string SiglId { get; private set; } = GamePassConstants.PcCatalogSiglId;

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

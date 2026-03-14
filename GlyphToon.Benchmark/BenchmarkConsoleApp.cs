// This file owns the benchmark flow, console output, and token/cost calculations.
// @decision The app measures prompt-shaping impact with exact Microsoft.ML.Tokenizers
// counts and focuses cost reporting on input tokens, because JSON vs TOON affects
// prompt size directly while model output volume is independent of the source format.
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using GlyphTone;
using Microsoft.ML.Tokenizers;

namespace GlyphToon.Benchmark;

internal static class BenchmarkConsoleApp
{
    private const double ZeroTolerance = 0.0000001d;
    private const int MaxBenchmarkOutputLength = 4_000_000;
    private const int MaxBenchmarkStringLength = 1_000_000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    private static readonly EncoderOptions BuiltInToonOptions = CreateBuiltInToonOptions();

    private static readonly EncoderOptions ExternalInputToonOptions = CreateExternalInputToonOptions();

    private static readonly IReadOnlyList<CostProfile> DefaultCostProfiles =
    [
        new("GPT-5.4 input", 1.25m),
        new("GPT-5 mini input", 0.25m),
    ];

    public static int Run(string[] args)
        => Run(args, Console.Out, Console.Error);

    internal static int Run(string[] args, TextWriter standardOutput, TextWriter standardError)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(standardError);

        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

        BenchmarkOptions options;

        try
        {
            options = BenchmarkOptions.Parse(args);
        }
        catch (ArgumentException exception)
        {
            standardError.WriteLine(exception.Message);
            standardError.WriteLine();
            WriteUsage(standardOutput);
            return 1;
        }

        if (options.ShowHelp)
        {
            WriteUsage(standardOutput);
            return 0;
        }

        Tokenizer tokenizer;

        try
        {
            tokenizer = TiktokenTokenizer.CreateForModel(options.TokenizerModel);
        }
        catch (Exception exception)
        {
            standardError.WriteLine(
                $"Unable to create a tokenizer for '{options.TokenizerModel}': {exception.Message}");
            standardError.WriteLine("Try a supported tiktoken model such as 'gpt-5' or 'gpt-4o'.");
            return 1;
        }

        BenchmarkScenario[] scenarios = BenchmarkScenarios.Create()
            .ToArray();
        EncoderOptions toonOptions = BuiltInToonOptions;

        if (options.InputPaths.Count > 0)
        {
            try
            {
                scenarios = JsonInputScenarios.Create(options.InputPaths).ToArray();
                toonOptions = ExternalInputToonOptions;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
            {
                standardError.WriteLine(exception.Message);
                return 1;
            }
        }

        scenarios = scenarios
            .Where(scenario => options.ScenarioFilter is null
                || scenario.Name.Contains(options.ScenarioFilter, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (scenarios.Length == 0)
        {
            standardError.WriteLine(
                $"No benchmark scenarios matched '{options.ScenarioFilter}'.");
            return 1;
        }

        WriteHeader(options, scenarios.Length, standardOutput);

        foreach (BenchmarkScenario scenario in scenarios)
        {
            ScenarioResult result;

            try
            {
                result = RunScenario(scenario, tokenizer, options.Iterations, toonOptions);
            }
            catch (ToonEncodingException exception)
            {
                standardError.WriteLine($"Unable to encode scenario '{scenario.Name}': {exception.Message}");
                return 1;
            }

            WriteScenario(result, options.ShowPayloads, standardOutput);
        }

        return 0;
    }

    private static ScenarioResult RunScenario(
        BenchmarkScenario scenario,
        Tokenizer tokenizer,
        int iterations,
        EncoderOptions toonOptions)
    {
        object? payload = scenario.PayloadFactory();
        SerializationResult json = MeasureSerialization(
            static value => JsonSerializer.Serialize(value, JsonOptions),
            payload,
            iterations);
        SerializationResult toon = MeasureSerialization(
            value => GlyphTone.Encoder.Serialize(value, toonOptions),
            payload,
            iterations);

        PayloadStats jsonStats = CreatePayloadStats("JSON", json, tokenizer);
        PayloadStats toonStats = CreatePayloadStats("TOON", toon, tokenizer);
        return new ScenarioResult(scenario, jsonStats, toonStats);
    }

    private static SerializationResult MeasureSerialization(
        Func<object?, string> serialize,
        object? payload,
        int iterations)
    {
        _ = serialize(payload);

        long start = Stopwatch.GetTimestamp();
        string last = string.Empty;

        for (int iteration = 0; iteration < iterations; iteration++)
        {
            last = serialize(payload);
        }

        TimeSpan elapsed = Stopwatch.GetElapsedTime(start);
        return new SerializationResult(last, elapsed, iterations);
    }

    private static PayloadStats CreatePayloadStats(
        string formatName,
        SerializationResult serialization,
        Tokenizer tokenizer)
    {
        string text = serialization.Payload;
        int tokenCount = tokenizer.CountTokens(text);
        int byteCount = Encoding.UTF8.GetByteCount(text);
        double averageMicroseconds = serialization.Elapsed.TotalMilliseconds * 1000d / serialization.Iterations;
        double throughput = serialization.Iterations / serialization.Elapsed.TotalSeconds;

        return new PayloadStats(
            formatName,
            text,
            tokenCount,
            byteCount,
            averageMicroseconds,
            throughput);
    }

    private static void WriteHeader(BenchmarkOptions options, int scenarioCount, TextWriter output)
    {
        output.WriteLine("GlyphToon JSON vs TOON benchmark");
        output.WriteLine(new string('=', 32));
        output.WriteLine($"Tokenizer model: {options.TokenizerModel}");
        output.WriteLine($"Iterations per serializer: {options.Iterations:N0}");
        output.WriteLine($"Scenarios: {scenarioCount}");
        output.WriteLine(
            options.InputPaths.Count > 0
                ? "TOON profile: hardened external-input profile (reflection disabled, bounded output)"
                : "TOON profile: built-in benchmark profile (reflection enabled for synthetic POCO scenarios)");
        if (options.InputPaths.Count > 0)
        {
            output.WriteLine($"Input files: {options.InputPaths.Count}");
        }
        if (options.ShowPayloads)
        {
            output.WriteLine("Payload output: enabled. Do not use --show-payloads with sensitive input files.");
        }
        output.WriteLine("Token counts: exact via Microsoft.ML.Tokenizers + O200kBase data");
        output.WriteLine("Cost presets: OpenAI API input pricing snapshot from 2026-03-14");
        output.WriteLine();
    }

    private static void WriteScenario(ScenarioResult result, bool showPayloads, TextWriter output)
    {
        output.WriteLine($"Scenario: {result.Scenario.Name}");
        output.WriteLine($"Description: {result.Scenario.Description}");
        output.WriteLine($"Rows or items: {result.Scenario.ItemCount:N0}");
        WritePayloadStats(result.Json, output);
        WritePayloadStats(result.Toon, output);
        WriteSavings(result.Json, result.Toon, output);
        WriteCosts(result.Json, result.Toon, output);

        if (showPayloads)
        {
            WritePayload("JSON", result.Json.Text, output);
            WritePayload("TOON", result.Toon.Text, output);
        }

        output.WriteLine();
    }

    private static void WritePayloadStats(PayloadStats stats, TextWriter output)
    {
        output.WriteLine(
            $"{stats.FormatName,4}: {stats.ByteCount,8:N0} bytes | " +
            $"{stats.TokenCount,7:N0} tokens | " +
            $"{stats.AverageMicroseconds,9:N1} us avg | " +
            $"{stats.DocumentsPerSecond,9:N0} docs/s");
    }

    private static void WriteSavings(PayloadStats json, PayloadStats toon, TextWriter output)
    {
        output.WriteLine(
            "Savings vs JSON: " +
            $"{FormatPercentSavings(json.ByteCount, toon.ByteCount)} bytes, " +
            $"{FormatPercentSavings(json.TokenCount, toon.TokenCount)} tokens");
        output.WriteLine(
            "Serialize delta vs JSON: " +
            $"{FormatRelativeSpeed(json.AverageMicroseconds, toon.AverageMicroseconds)}");
    }

    private static void WriteCosts(PayloadStats json, PayloadStats toon, TextWriter output)
    {
        foreach (CostProfile profile in DefaultCostProfiles)
        {
            decimal jsonCost = CalculateInputCost(json.TokenCount, profile.InputCostPerMillionTokens);
            decimal toonCost = CalculateInputCost(toon.TokenCount, profile.InputCostPerMillionTokens);
            decimal savings = jsonCost - toonCost;

            output.WriteLine(
                $"{profile.Name,18}: {FormatUsd(jsonCost)} JSON | " +
                $"{FormatUsd(toonCost)} TOON | " +
                $"{FormatUsd(savings)} saved");
        }
    }

    private static void WritePayload(string label, string payload, TextWriter output)
    {
        output.WriteLine();
        output.WriteLine($"--- {label} payload ---");
        output.WriteLine(payload);
    }

    private static decimal CalculateInputCost(int tokens, decimal inputCostPerMillionTokens)
        => tokens / 1_000_000m * inputCostPerMillionTokens;

    private static EncoderOptions CreateBuiltInToonOptions() => new()
    {
        AllowReflectionObjectSerialization = true,
        PreferTabularArrays = true,
        PropertyNamingPolicy = static propertyName => JsonNamingPolicy.CamelCase.ConvertName(propertyName),
        SortDictionaryKeys = false,
        SortProperties = false,
        StrictMode = true,
    };

    private static EncoderOptions CreateExternalInputToonOptions()
    {
        EncoderOptions options = EncoderOptions.CreateHardenedDefaults();
        options.MaxCollectionItemCount = 100_000;
        options.MaxObjectMemberCount = 4_096;
        options.MaxOutputLength = MaxBenchmarkOutputLength;
        options.MaxStringLength = MaxBenchmarkStringLength;
        options.PreferTabularArrays = true;
        options.PropertyNamingPolicy = static propertyName => JsonNamingPolicy.CamelCase.ConvertName(propertyName);
        options.SortDictionaryKeys = false;
        options.SortProperties = false;
        return options;
    }

    private static string FormatPercentSavings(double baseline, double candidate)
    {
        if (Math.Abs(baseline) < ZeroTolerance)
        {
            return "n/a";
        }

        double savings = (baseline - candidate) / baseline * 100d;
        return savings.ToString("0.0'%'",
            CultureInfo.InvariantCulture);
    }

    private static string FormatPercentSavings(int baseline, int candidate)
        => FormatPercentSavings((double)baseline, candidate);

    private static string FormatRelativeSpeed(double baseline, double candidate)
    {
        if (Math.Abs(baseline) < ZeroTolerance)
        {
            return "n/a";
        }

        double change = (candidate - baseline) / baseline * 100d;
        string direction = change >= 0d ? "slower" : "faster";
        return $"{Math.Abs(change).ToString("0.0", CultureInfo.InvariantCulture)}% {direction}";
    }

    private static string FormatUsd(decimal amount)
        => amount.ToString("'$'0.000000", CultureInfo.InvariantCulture);

    private static void WriteUsage(TextWriter output)
    {
        output.WriteLine("Usage:");
        output.WriteLine("  dotnet run --project GlyphToon.Benchmark/GlyphToon.Benchmark.csproj -- [--iterations 500] [--scenario glyph] [--tokenizer-model gpt-5] [--input file.json] [--show-payloads]");
        output.WriteLine();
        output.WriteLine("Options:");
        output.WriteLine("  --iterations <number>      Number of repeated serializations per format. Default: 500");
        output.WriteLine("  --scenario <filter>        Runs only scenarios whose names contain the filter text.");
        output.WriteLine("  --tokenizer-model <name>   Tiktoken model name for CountTokens. Default: gpt-5");
        output.WriteLine("  --input <file.json>        Benchmarks one JSON file up to 4 MiB. Repeat to benchmark multiple files.");
        output.WriteLine("  --show-payloads            Prints full JSON and TOON payloads. Avoid with sensitive input.");
        output.WriteLine("  --help                     Shows this help text.");
    }

    private readonly record struct BenchmarkOptions(
        int Iterations,
        string TokenizerModel,
        string? ScenarioFilter,
        IReadOnlyList<string> InputPaths,
        bool ShowPayloads,
        bool ShowHelp)
    {
        public static BenchmarkOptions Parse(string[] args)
        {
            int iterations = 500;
            string tokenizerModel = "gpt-5";
            string? scenarioFilter = null;
            List<string> inputPaths = [];
            bool showPayloads = false;
            bool showHelp = false;

            for (int index = 0; index < args.Length; index++)
            {
                string argument = args[index];

                switch (argument)
                {
                    case "--iterations":
                        iterations = ParsePositiveInt(GetRequiredValue(args, ref index, argument), argument);
                        break;
                    case "--scenario":
                        scenarioFilter = GetRequiredValue(args, ref index, argument);
                        break;
                    case "--tokenizer-model":
                        tokenizerModel = GetRequiredValue(args, ref index, argument);
                        break;
                    case "--input":
                        inputPaths.Add(GetRequiredValue(args, ref index, argument));
                        break;
                    case "--show-payloads":
                        showPayloads = true;
                        break;
                    case "--help":
                    case "-h":
                    case "/?":
                        showHelp = true;
                        break;
                    default:
                        throw new ArgumentException($"Unknown argument '{argument}'.");
                }
            }

            return new BenchmarkOptions(
                iterations,
                tokenizerModel,
                scenarioFilter,
                inputPaths,
                showPayloads,
                showHelp);
        }

        private static int ParsePositiveInt(string value, string argumentName)
        {
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) || parsed <= 0)
            {
                throw new ArgumentException($"Argument '{argumentName}' requires a positive integer.");
            }

            return parsed;
        }

        private static string GetRequiredValue(string[] args, ref int index, string argumentName)
        {
            if (index + 1 >= args.Length)
            {
                throw new ArgumentException($"Argument '{argumentName}' requires a value.");
            }

            index++;
            return args[index];
        }
    }

    private readonly record struct CostProfile(string Name, decimal InputCostPerMillionTokens);

    private readonly record struct SerializationResult(
        string Payload,
        TimeSpan Elapsed,
        int Iterations);

    private readonly record struct PayloadStats(
        string FormatName,
        string Text,
        int TokenCount,
        int ByteCount,
        double AverageMicroseconds,
        double DocumentsPerSecond);

    private readonly record struct ScenarioResult(
        BenchmarkScenario Scenario,
        PayloadStats Json,
        PayloadStats Toon);
}

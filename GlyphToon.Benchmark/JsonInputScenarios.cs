// This file loads external JSON payloads and normalizes them into plain CLR objects
// so the benchmark can run the same comparison pipeline against real user data.
// @decision External files are parsed into dictionaries, lists, and primitives instead
// of JsonElement values because GlyphTone.Encoder already handles plain CLR graphs well
// and this keeps JSON and TOON serialization driven by the same normalized structure.
using System.Text.Json;

namespace GlyphToon.Benchmark;

internal static class JsonInputScenarios
{
    private const long MaxInputFileBytes = 4 * 1024 * 1024;
    private const int MaxJsonDepth = 64;

    public static IReadOnlyList<BenchmarkScenario> Create(IEnumerable<string> inputPaths)
    {
        ArgumentNullException.ThrowIfNull(inputPaths);

        List<BenchmarkScenario> scenarios = [];

        foreach (string inputPath in inputPaths)
        {
            string fullPath = Path.GetFullPath(inputPath);
            FileInfo inputFile = new(fullPath);

            if (!inputFile.Exists)
            {
                throw new FileNotFoundException(
                    $"Input JSON file was not found: {Path.GetFileName(fullPath)}",
                    Path.GetFileName(fullPath));
            }

            if (inputFile.Length > MaxInputFileBytes)
            {
                throw new IOException(
                    $"Input JSON file exceeds the {FormatFileSize(MaxInputFileBytes)} limit: {inputFile.Name}");
            }

            using FileStream stream = inputFile.OpenRead();
            using JsonDocument document = JsonDocument.Parse(stream, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = MaxJsonDepth,
            });

            object? payload = ConvertElement(document.RootElement);
            int itemCount = CountRootItems(document.RootElement);
            string fileName = Path.GetFileNameWithoutExtension(fullPath);

            scenarios.Add(new BenchmarkScenario(
                fileName,
                $"Loaded from input file '{inputFile.Name}'",
                itemCount,
                () => payload));
        }

        return scenarios;
    }

    private static string FormatFileSize(long byteCount)
        => $"{byteCount / (1024d * 1024d):0.#} MiB";

    private static int CountRootItems(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Array => element.GetArrayLength(),
        JsonValueKind.Object => element.EnumerateObject().Count(),
        JsonValueKind.Null => 0,
        _ => 1,
    };

    private static object? ConvertElement(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => ConvertObject(element),
        JsonValueKind.Array => ConvertArray(element),
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => ConvertNumber(element),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => throw new NotSupportedException($"Unsupported JSON token kind: {element.ValueKind}"),
    };

    private static Dictionary<string, object?> ConvertObject(JsonElement element)
    {
        Dictionary<string, object?> properties = new(StringComparer.Ordinal);

        foreach (JsonProperty property in element.EnumerateObject())
        {
            properties[property.Name] = ConvertElement(property.Value);
        }

        return properties;
    }

    private static List<object?> ConvertArray(JsonElement element)
    {
        List<object?> items = [];

        foreach (JsonElement item in element.EnumerateArray())
        {
            items.Add(ConvertElement(item));
        }

        return items;
    }

    private static object ConvertNumber(JsonElement element)
    {
        if (element.TryGetInt32(out int int32))
        {
            return int32;
        }

        if (element.TryGetInt64(out long int64))
        {
            return int64;
        }

        if (element.TryGetDecimal(out decimal decimalValue))
        {
            return decimalValue;
        }

        return element.GetDouble();
    }
}

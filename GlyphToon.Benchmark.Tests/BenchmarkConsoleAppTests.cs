// This file validates the benchmark CLI contract without shelling out to a child process.
namespace GlyphToon.Benchmark.Tests;

public sealed class BenchmarkConsoleAppTests
{
    private const int MaxInputFileBytes = 4 * 1024 * 1024;

    [Fact]
    public void RunHelpPrintsUsageAndReturnsZero()
    {
        StringWriter stdout = new();
        StringWriter stderr = new();

        int exitCode = BenchmarkConsoleApp.Run(["--help"], stdout, stderr);

        Assert.Equal(0, exitCode);
        Assert.Contains("Usage:", stdout.ToString(), StringComparison.Ordinal);
        Assert.Contains("--input <file.json>", stdout.ToString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, stderr.ToString());
    }

    [Fact]
    public void RunMissingInputFilePrintsErrorAndReturnsOne()
    {
        StringWriter stdout = new();
        StringWriter stderr = new();
        string missingPath = Path.Combine(AppContext.BaseDirectory, "TestData", "missing.json");

        int exitCode = BenchmarkConsoleApp.Run(["--input", missingPath], stdout, stderr);

        Assert.Equal(1, exitCode);
        Assert.Contains("Input JSON file was not found", stderr.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(missingPath, stderr.ToString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, stdout.ToString());
    }

    [Fact]
    public void RunSampleInputPrintsScenarioSummary()
    {
        StringWriter stdout = new();
        StringWriter stderr = new();
        string samplePath = GetRepoRelativePath("tmp", "sample-input.json");

        int exitCode = BenchmarkConsoleApp.Run(["--iterations", "1", "--input", samplePath], stdout, stderr);

        string output = stdout.ToString();
        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, stderr.ToString());
        Assert.Contains("Scenario: sample-input", output, StringComparison.Ordinal);
        Assert.DoesNotContain(samplePath, output, StringComparison.Ordinal);
        Assert.Contains("Savings vs JSON:", output, StringComparison.Ordinal);
        Assert.Contains("GPT-5.4 input:", output, StringComparison.Ordinal);
    }

    [Fact]
    public void RunRejectsOversizedInputFile()
    {
        StringWriter stdout = new();
        StringWriter stderr = new();
        string tempFile = Path.Combine(Path.GetTempPath(), $"glyphtoon-large-{Guid.NewGuid():N}.json");

        try
        {
            using (FileStream stream = new(tempFile, FileMode.CreateNew, FileAccess.Write))
            {
                stream.SetLength(MaxInputFileBytes + 1L);
            }

            int exitCode = BenchmarkConsoleApp.Run(["--input", tempFile], stdout, stderr);

            Assert.Equal(1, exitCode);
            Assert.Contains("exceeds the 4 MiB limit", stderr.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain(tempFile, stderr.ToString(), StringComparison.Ordinal);
            Assert.Equal(string.Empty, stdout.ToString());
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public void RunShowPayloadsPrintsSensitiveInputWarning()
    {
        StringWriter stdout = new();
        StringWriter stderr = new();
        string samplePath = GetRepoRelativePath("tmp", "sample-input.json");

        int exitCode = BenchmarkConsoleApp.Run(["--iterations", "1", "--input", samplePath, "--show-payloads"], stdout, stderr);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, stderr.ToString());
        Assert.Contains("Do not use --show-payloads with sensitive input files.", stdout.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void JsonInputScenariosCreateConvertsSampleFileIntoScenario()
    {
        string samplePath = GetRepoRelativePath("tmp", "sample-input.json");

        IReadOnlyList<BenchmarkScenario> scenarios = JsonInputScenarios.Create([samplePath]);

        BenchmarkScenario scenario = Assert.Single(scenarios);
        Assert.Equal("sample-input", scenario.Name);
        Assert.Equal(3, scenario.ItemCount);
        Assert.Equal("Loaded from input file 'sample-input.json'", scenario.Description);
        Assert.IsAssignableFrom<Dictionary<string, object?>>(scenario.PayloadFactory());
    }

    private static string GetRepoRelativePath(params string[] segments)
    {
        string path = AppContext.BaseDirectory;

        for (int index = 0; index < 5; index++)
        {
            path = Path.GetDirectoryName(path)
                ?? throw new InvalidOperationException("Unable to locate repository root.");
        }

        return Path.Combine([path, .. segments]);
    }
}

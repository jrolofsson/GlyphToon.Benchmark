# GlyphToon.Benchmark

Console benchmark for comparing prompt size and input-token cost between minified JSON and TOON output produced by the published `GlyphTone.Encoder` package.

Package source:

- GitHub Packages: https://github.com/jrolofsson/GlyphToon/pkgs/nuget/GlyphTone.Encoder

The repo also includes:

- [GlyphToon.Benchmark](/Users/johaolof/git/GlyphToon.Benchmark/GlyphToon.Benchmark) as the console app project
- [GlyphToon.Benchmark.slnx](/Users/johaolof/git/GlyphToon.Benchmark/GlyphToon.Benchmark.slnx) for app + tests
- [GlyphToon.Benchmark.Tests](/Users/johaolof/git/GlyphToon.Benchmark/GlyphToon.Benchmark.Tests) as the xUnit test harness

## What It Measures

- UTF-8 payload bytes for JSON and TOON
- Exact token counts via `Microsoft.ML.Tokenizers`
- Estimated input cost using built-in OpenAI pricing presets
- Average serialization time across repeated iterations

The benchmark uses `TiktokenTokenizer.CreateForModel(...)` and `CountTokens(...)` following Microsoft Learn guidance:

- https://learn.microsoft.com/dotnet/ai/how-to/use-tokenizers
- https://learn.microsoft.com/en-us/dotnet/machine-learning/whats-new/overview

## Run

```bash
dotnet run --project GlyphToon.Benchmark/GlyphToon.Benchmark.csproj -- --iterations 500
```

If restore fails on a fresh machine, make sure your GitHub Packages credentials are configured for:

```text
https://nuget.pkg.github.com/jrolofsson/index.json
```

This repo includes a local [NuGet.Config](/Users/johaolof/git/GlyphToon.Benchmark/NuGet.Config) that expects these environment variables:

```text
GLYPHTOON_GITHUB_USERNAME
GLYPHTOON_GITHUB_PAT
```

`GLYPHTOON_GITHUB_PAT` should be a GitHub personal access token (classic) with at least `read:packages`.

## Test Harness

The repo includes an xUnit test project that validates the benchmark CLI and JSON input pipeline without needing to shell out to a subprocess for every assertion.

It currently covers:

- help/usage output
- missing input-file errors
- file-backed benchmark execution
- JSON input scenario loading

Run tests with:

```bash
dotnet test GlyphToon.Benchmark.slnx --no-restore
```

If you need a fresh restore first:

```bash
dotnet restore GlyphToon.Benchmark.slnx
```

Common examples:

```bash
dotnet run --project GlyphToon.Benchmark/GlyphToon.Benchmark.csproj -- --scenario glyph
dotnet run --project GlyphToon.Benchmark/GlyphToon.Benchmark.csproj -- --tokenizer-model gpt-5
dotnet run --project GlyphToon.Benchmark/GlyphToon.Benchmark.csproj -- --input data/sample.json
dotnet run --project GlyphToon.Benchmark/GlyphToon.Benchmark.csproj -- --input data/a.json --input data/b.json
dotnet run --project GlyphToon.Benchmark/GlyphToon.Benchmark.csproj -- --show-payloads
dotnet run --project GlyphToon.Benchmark/GlyphToon.Benchmark.csproj -- --iterations 1000 --scenario audit
```

When one or more `--input` files are provided, the app benchmarks those JSON files instead of the built-in synthetic scenarios.

## CLI Options

### `--iterations <number>`

Controls how many times each serializer runs per scenario or input file.

- Default: `500`
- Higher values make timing numbers more stable.
- Token counts, bytes, and cost do not change with this option.

Example:

```bash
dotnet run --project GlyphToon.Benchmark/GlyphToon.Benchmark.csproj -- --iterations 2000
```

### `--scenario <filter>`

Filters scenarios by name using a case-insensitive substring match.

- Applies to built-in scenarios by default.
- Also applies to `--input` files, using the file name without extension as the scenario name.
- Useful when you want to benchmark only one scenario.

Built-in scenario names:

- `GlyphCatalog`
- `RenderBatch`
- `QualityAudit`

Examples:

```bash
dotnet run --project GlyphToon.Benchmark/GlyphToon.Benchmark.csproj -- --scenario glyph
dotnet run --project GlyphToon.Benchmark/GlyphToon.Benchmark.csproj -- --scenario render
```

### `--tokenizer-model <name>`

Chooses the tiktoken model used by `Microsoft.ML.Tokenizers` for exact token counting.

- Default: `gpt-5`
- This changes token counts and cost calculations.
- Use a model whose tokenizer is compatible with your target LLM pricing assumptions.

Examples:

```bash
dotnet run --project GlyphToon.Benchmark/GlyphToon.Benchmark.csproj -- --tokenizer-model gpt-5
dotnet run --project GlyphToon.Benchmark/GlyphToon.Benchmark.csproj -- --tokenizer-model gpt-4o
```

### `--input <file.json>`

Benchmarks one JSON file from disk. Repeat the switch to benchmark multiple files.

- When at least one `--input` is present, built-in scenarios are skipped.
- Each file becomes its own scenario.
- The scenario name is the file name without `.json`.
- The app supports JSON objects, arrays, primitives, booleans, numbers, and `null`.

Examples:

```bash
dotnet run --project GlyphToon.Benchmark/GlyphToon.Benchmark.csproj -- --input tmp/sample-input.json
dotnet run --project GlyphToon.Benchmark/GlyphToon.Benchmark.csproj -- --input data/orders.json --input data/catalog.json
```

### `--show-payloads`

Prints the generated minified JSON payload and the TOON payload after each benchmark result.

- Useful for inspecting exactly what was measured.
- Best for one scenario or a small number of input files, since output can get large.

Example:

```bash
dotnet run --project GlyphToon.Benchmark/GlyphToon.Benchmark.csproj -- --scenario glyph --show-payloads
```

### `--help`

Prints the built-in command help from the application.

Example:

```bash
dotnet run --project GlyphToon.Benchmark/GlyphToon.Benchmark.csproj -- --help
```

## Option Combinations

The switches can be combined in a few important ways:

- No `--input`: run the built-in benchmark scenarios.
- One or more `--input`: run only those JSON files.
- `--scenario` + built-in scenarios: filter the synthetic scenarios by name.
- `--scenario` + `--input`: filter the provided files by file name.
- `--show-payloads`: include the exact JSON and TOON strings in the output.
- `--iterations`: only affects timing stability, not bytes, tokens, or cost.

Example combinations:

```bash
dotnet run --project GlyphToon.Benchmark/GlyphToon.Benchmark.csproj -- --input data/prompt.json --iterations 200 --show-payloads
dotnet run --project GlyphToon.Benchmark/GlyphToon.Benchmark.csproj -- --input data/a.json --input data/b.json --scenario catalog
dotnet run --project GlyphToon.Benchmark/GlyphToon.Benchmark.csproj -- --scenario quality --tokenizer-model gpt-4o
```

## Notes

- The JSON baseline is minified camelCase JSON to keep the comparison fair.
- External input files are parsed into plain CLR dictionaries, lists, and primitives before benchmarking so both serializers work from the same normalized object graph.
- The TOON output uses camelCase property names and keeps tabular arrays enabled.
- The default cost presets only apply to input tokens, which is the portion affected by JSON vs TOON prompt formatting.
- Built-in price presets are based on the OpenAI API pricing page snapshot from 2026-03-14 and can be adjusted in code if your target model pricing differs.

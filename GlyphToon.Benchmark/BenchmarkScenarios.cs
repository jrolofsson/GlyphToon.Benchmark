// This file defines deterministic benchmark datasets tailored to structured LLM prompts.
// @decision The sample payloads emphasize repeated object fields and mixed nesting so the
// benchmark reflects the trade-off the TOON format is designed for: lower prompt overhead
// on regular JSON-shaped data without inventing unrealistic micro payloads.
namespace GlyphToon.Benchmark;

internal static class BenchmarkScenarios
{
    public static IReadOnlyList<BenchmarkScenario> Create() =>
    [
        new(
            "GlyphCatalog",
            "Dense glyph metadata with repeated numeric metrics and tags.",
            256,
            CreateGlyphCatalog),
        new(
            "RenderBatch",
            "Prompt-ready render instructions for a batch of image generation jobs.",
            180,
            CreateRenderBatch),
        new(
            "QualityAudit",
            "Evaluation rows that mix scalar scores, labels, and reviewer routing metadata.",
            220,
            CreateQualityAudit),
    ];

    private static GlyphCatalogPayload CreateGlyphCatalog()
    {
        GlyphMetric[] glyphs =
        [
            .. Enumerable.Range(0, 256).Select(static index => new GlyphMetric(
                GlyphId: index + 1,
                Unicode: $"U+{0x4E00 + index:X4}",
                Slug: $"glyph-{index + 1:D4}",
                Width: 480 + index % 19 * 6,
                Height: 640 + index % 13 * 5,
                Advance: 500 + index % 17 * 4,
                LeftBearing: -14 + index % 7,
                TopBearing: 690 - index % 11 * 3,
                Style: index % 3 == 0 ? "display" : "text",
                LanguageHint: index % 2 == 0 ? "latin" : "cjk")),
        ];

        KerningPair[] kerningPairs =
        [
            .. Enumerable.Range(0, 96).Select(static index => new KerningPair(
                Left: $"glyph-{index + 1:D4}",
                Right: $"glyph-{(index + 17) % 256 + 1:D4}",
                Adjustment: -20 + index % 9 * 2,
                Category: index % 2 == 0 ? "tight" : "balanced")),
        ];

        return new GlyphCatalogPayload(
            Project: "GlyphToon benchmark atlas",
            Revision: "2026.03",
            FontFamily: "GlyphToon Sans",
            Glyphs: glyphs,
            KerningPairs: kerningPairs);
    }

    private static RenderBatchPayload CreateRenderBatch()
    {
        RenderJob[] jobs =
        [
            .. Enumerable.Range(0, 180).Select(static index => new RenderJob(
                JobId: $"job-{index + 1:D4}",
                Scene: (index % 4) switch
                {
                    0 => "forest shrine",
                    1 => "subway platform",
                    2 => "floating market",
                    _ => "workshop desk",
                },
                Subject: $"mask-{index % 40 + 1:D2}",
                Palette: index % 3 == 0 ? "copper-cyan" : index % 3 == 1 ? "indigo-sand" : "olive-cream",
                Mood: index % 2 == 0 ? "calm" : "kinetic",
                Width: 1024,
                Height: 1024,
                Variations: 2 + index % 3,
                GuidanceScale: 6 + index % 4,
                Seed: 10_000 + index * 37,
                UseReference: index % 5 == 0,
                OutputFormat: index % 2 == 0 ? "png" : "webp")),
        ];

        return new RenderBatchPayload(
            BatchId: "batch-2026-03-14-a",
            RequestedBy: "benchmark-runner",
            PromptTemplate: "Create a clean stylized product shot with readable silhouettes and soft rim light.",
            Jobs: jobs);
    }

    private static QualityAuditPayload CreateQualityAudit()
    {
        AuditRow[] rows =
        [
            .. Enumerable.Range(0, 220).Select(static index => new AuditRow(
                AssetId: $"asset-{index + 1:D5}",
                Model: index % 2 == 0 ? "gpt-5.4" : "gpt-5-mini",
                Locale: (index % 4) switch
                {
                    0 => "en-US",
                    1 => "sv-SE",
                    2 => "ja-JP",
                    _ => "fr-FR",
                },
                SafetyLabel: index % 6 == 0 ? "review" : "clear",
                FaithfulnessScore: 78 + index % 17,
                LayoutScore: 74 + index % 19,
                TypographyScore: 80 + index % 13,
                RequiresEscalation: index % 17 == 0,
                ReviewerQueue: index % 3 == 0 ? "tier-2" : "tier-1",
                Resolution: index % 5 == 0 ? "retry" : "accept")),
        ];

        return new QualityAuditPayload(
            AuditId: "audit-2026-03-14",
            SourceSystem: "glyphtoon-lab",
            Rows: rows);
    }
}

internal sealed record BenchmarkScenario(
    string Name,
    string Description,
    int ItemCount,
    Func<object?> PayloadFactory);

internal sealed record GlyphCatalogPayload(
    string Project,
    string Revision,
    string FontFamily,
    IReadOnlyList<GlyphMetric> Glyphs,
    IReadOnlyList<KerningPair> KerningPairs);

internal sealed record GlyphMetric(
    int GlyphId,
    string Unicode,
    string Slug,
    int Width,
    int Height,
    int Advance,
    int LeftBearing,
    int TopBearing,
    string Style,
    string LanguageHint);

internal sealed record KerningPair(
    string Left,
    string Right,
    int Adjustment,
    string Category);

internal sealed record RenderBatchPayload(
    string BatchId,
    string RequestedBy,
    string PromptTemplate,
    IReadOnlyList<RenderJob> Jobs);

internal sealed record RenderJob(
    string JobId,
    string Scene,
    string Subject,
    string Palette,
    string Mood,
    int Width,
    int Height,
    int Variations,
    int GuidanceScale,
    int Seed,
    bool UseReference,
    string OutputFormat);

internal sealed record QualityAuditPayload(
    string AuditId,
    string SourceSystem,
    IReadOnlyList<AuditRow> Rows);

internal sealed record AuditRow(
    string AssetId,
    string Model,
    string Locale,
    string SafetyLabel,
    int FaithfulnessScore,
    int LayoutScore,
    int TypographyScore,
    bool RequiresEscalation,
    string ReviewerQueue,
    string Resolution);

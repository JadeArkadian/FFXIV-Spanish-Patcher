using XivSpanish.GameData;
using XivSpanish.Translation;

namespace XivSpanish.Packager;

/// <summary>
/// One manifest entry that failed the SeString gate, with the validator's full diagnostics.
/// </summary>
public sealed record ManifestSeStringViolation(TranslationEntry Entry, SeStringCompatibilityReport Report)
{
    /// <summary>Stable row label for CLI output: sheet/row(.field) from the source key, falling back
    /// to the source text when the key is absent.</summary>
    public string Label
    {
        get
        {
            var key = Entry.SourceKey;
            return key is null
                ? Entry.Source
                : $"{key.Sheet}/{key.RowId}{(string.IsNullOrWhiteSpace(key.Field) ? string.Empty : $".{key.Field}")}";
        }
    }

    /// <summary>Multi-line block describing this violation for stderr.</summary>
    public string Describe()
        => $"SESTRING GATE: {Label} — target is not payload-compatible with source:"
           + Environment.NewLine
           + string.Join(Environment.NewLine, Report.Violations.Select(v => $"    {v}"));
}

/// <summary>
/// SeString gate run at manifest load, before any build work (T-19-03). Every entry that would
/// be packaged and whose source carries payload machinery, or whose target carries an
/// allow-listed standard macro, must pass <see cref="SeStringCompatibilityValidator"/>. ALL
/// offending rows are listed — the check never stops at the first violation — and the pipeline
/// leaves them out of the package. Plain-text rows without a reserved target macro pass
/// trivially. Mirrors the XivSpanish.Jsonl T-19-02 gate.
/// </summary>
public static class ManifestSeStringGate
{
    /// <summary>
    /// Checks every entry and collects ALL violations. Payload-bearing sources and targets carrying
    /// reserved standard-macro delimiters are validated;
    /// entries with a null/empty target are skipped (they are not packageable and are reported as
    /// skips elsewhere).
    /// </summary>
    public static IReadOnlyList<ManifestSeStringViolation> Check(
        IEnumerable<TranslationEntry> entries,
        int? maxDegreeOfParallelism = null)
        // Each entry is validated on its own, so the rows fan out; AsOrdered keeps the report order.
        => entries
            .AsParallel()
            .AsOrdered()
            .WithDegreeOfParallelism(Math.Clamp(maxDegreeOfParallelism ?? Environment.ProcessorCount, 1, 512))
            .Where(entry => !string.IsNullOrEmpty(entry.Target)
                && (SeStringCompatibilityValidator.HasPayloads(entry.Source)
                    || SeStringStandardMacros.HasReservedDelimiter(entry.Target)))
            .Select(entry => new ManifestSeStringViolation(
                entry,
                SeStringCompatibilityValidator.Validate(entry.Source, entry.Target)))
            .Where(violation => !violation.Report.IsCompatible)
            .ToList();
}

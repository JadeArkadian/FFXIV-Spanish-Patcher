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

    /// <summary>Multi-line block describing a forced override for this row.</summary>
    public string DescribeOverride()
        => $"{ManifestSeStringGate.OverrideMarker}: {Label} — packaging despite violations:"
           + Environment.NewLine
           + string.Join(Environment.NewLine, Report.Violations.Select(v => $"    {v}"));
}

/// <summary>
/// Hard SeString gate run at manifest load, before any build work (T-19-03). Every entry that
/// would be packaged and whose source carries payload machinery, or whose target carries an
/// allow-listed standard macro, must pass
/// <see cref="SeStringCompatibilityValidator"/>; otherwise the build fails (exit ≠ 0) listing ALL
/// offending rows — the check never stops at the first violation. Plain-text rows without a
/// reserved target macro pass trivially.
/// Mirrors the XivSpanish.Jsonl T-19-02 gate, including the loudly-logged
/// <c>--force-sestring</c> override.
/// </summary>
public static class ManifestSeStringGate
{
    /// <summary>Marker prefixed to every forced-override line so it stands out in logs/transcripts.</summary>
    public const string OverrideMarker = "!! SESTRING OVERRIDE (--force-sestring)";

    /// <summary>
    /// Checks every entry and collects ALL violations. Payload-bearing sources and targets carrying
    /// reserved standard-macro delimiters are validated;
    /// entries with a null/empty target are skipped (they are not packageable and are reported as
    /// skips elsewhere). The caller decides whether the manifest is rejected or force-overridden.
    /// </summary>
    public static IReadOnlyList<ManifestSeStringViolation> Check(IEnumerable<TranslationEntry> entries)
    {
        var violations = new List<ManifestSeStringViolation>();
        foreach (var entry in entries)
        {
            if (string.IsNullOrEmpty(entry.Target)
                || (!SeStringCompatibilityValidator.HasPayloads(entry.Source)
                    && !SeStringStandardMacros.HasReservedDelimiter(entry.Target)))
            {
                continue;
            }

            var report = SeStringCompatibilityValidator.Validate(entry.Source, entry.Target);
            if (!report.IsCompatible)
            {
                violations.Add(new ManifestSeStringViolation(entry, report));
            }
        }

        return violations;
    }
}

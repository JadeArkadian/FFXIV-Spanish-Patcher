using XivSpanish.GameData;
using XivSpanish.Translation;

namespace XivSpanish.Packager;

public enum BroadcastKind
{
    Plain,
    Payload,
}

public sealed record BroadcastColumn(
    uint RowId,
    string Field,
    string Source,
    bool HasPayload,
    string RawHash);

public sealed record BroadcastDecision(
    string Target,
    string? ReplacementField,
    BroadcastKind Kind);

public readonly record struct PayloadBroadcastSignature(
    string Field,
    string Source,
    string Target,
    string RawHash);

public sealed class BroadcastCatalog
{
    private readonly Dictionary<string, Dictionary<string, Dictionary<string, string?>>> _targets =
        new(StringComparer.OrdinalIgnoreCase);

    public void Add(TranslationEntry entry)
    {
        var key = entry.SourceKey ?? throw new ArgumentException("Entry has no source key.", nameof(entry));
        Add(
            key.Sheet,
            string.IsNullOrWhiteSpace(key.Field) ? string.Empty : key.Field,
            entry.Source,
            entry.Target);
    }

    public void Add(string sheet, string field, string source, string target)
    {
        if (!_targets.TryGetValue(sheet, out var byField))
        {
            byField = new Dictionary<string, Dictionary<string, string?>>(StringComparer.Ordinal);
            _targets[sheet] = byField;
        }

        if (!byField.TryGetValue(field, out var bySource))
        {
            bySource = new Dictionary<string, string?>(StringComparer.Ordinal);
            byField[field] = bySource;
        }

        if (!bySource.TryGetValue(source, out var existingTarget))
        {
            bySource[source] = target;
        }
        else if (existingTarget is not null && existingTarget != target)
        {
            bySource[source] = null;
        }
    }

    public BroadcastTarget? Resolve(string sheet, string field, string source, bool allowAnyField)
    {
        if (!_targets.TryGetValue(sheet, out var byField))
        {
            return null;
        }

        if (TryResolveField(byField, field, source, out var exact))
        {
            return exact;
        }

        if (allowAnyField
            && field.Length > 0
            && TryResolveField(byField, string.Empty, source, out var anyField))
        {
            return anyField;
        }

        return null;
    }

    private static bool TryResolveField(
        Dictionary<string, Dictionary<string, string?>> byField,
        string field,
        string source,
        out BroadcastTarget? target)
    {
        target = null;
        if (!byField.TryGetValue(field, out var bySource)
            || !bySource.TryGetValue(source, out var value))
        {
            return false;
        }

        target = value is null ? null : new BroadcastTarget(value, field);
        return true;
    }
}

public sealed record BroadcastTarget(string Target, string Field);

public static class BroadcastPlanner
{
    public static IReadOnlySet<PayloadBroadcastSignature> BuildPayloadSignatures(
        IEnumerable<BroadcastColumn> columns,
        IReadOnlyDictionary<uint, IReadOnlyList<StringReplacement>> explicitReplacements)
    {
        var byRow = columns
            .Where(column => column.HasPayload && column.Field.Length > 0)
            .GroupBy(column => column.RowId)
            .ToDictionary(group => group.Key, group => group.ToArray());

        var signatures = new HashSet<PayloadBroadcastSignature>();
        foreach (var (rowId, replacements) in explicitReplacements)
        {
            if (!byRow.TryGetValue(rowId, out var rowColumns))
            {
                continue;
            }

            foreach (var replacement in replacements)
            {
                if (string.IsNullOrWhiteSpace(replacement.Field)
                    || !SeStringCompatibilityValidator.Validate(replacement.Source, replacement.Target).IsCompatible)
                {
                    continue;
                }

                foreach (var column in rowColumns)
                {
                    if (column.Field == replacement.Field && column.Source == replacement.Source)
                    {
                        signatures.Add(new PayloadBroadcastSignature(
                            column.Field,
                            replacement.Source,
                            replacement.Target,
                            column.RawHash));
                    }
                }
            }
        }

        return signatures;
    }

    public static BroadcastDecision? Decide(
        BroadcastCatalog catalog,
        string sheet,
        BroadcastColumn column,
        IReadOnlySet<PayloadBroadcastSignature> payloadSignatures)
    {
        var resolved = catalog.Resolve(
            sheet,
            column.Field,
            column.Source,
            allowAnyField: !column.HasPayload);
        if (resolved is null)
        {
            return null;
        }

        if (!column.HasPayload)
        {
            return new BroadcastDecision(
                resolved.Target,
                string.IsNullOrEmpty(resolved.Field) ? null : resolved.Field,
                BroadcastKind.Plain);
        }

        if (column.Field.Length == 0
            || !SeStringCompatibilityValidator.Validate(column.Source, resolved.Target).IsCompatible
            || !payloadSignatures.Contains(new PayloadBroadcastSignature(
                column.Field,
                column.Source,
                resolved.Target,
                column.RawHash)))
        {
            return null;
        }

        return new BroadcastDecision(resolved.Target, column.Field, BroadcastKind.Payload);
    }
}

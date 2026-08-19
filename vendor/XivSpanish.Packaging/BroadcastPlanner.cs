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
            // Same source, different target: ambiguous. Null disables broadcast for this source.
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
    /// <summary>
    /// Broadcast decision for a payload-bearing duplicate row: apply the representative row's
    /// reviewed <see cref="Replacement"/> (its corpus source and target) to <see cref="RowId"/>.
    /// </summary>
    public sealed record PayloadSiblingDecision(uint RowId, StringReplacement Replacement);

    /// <summary>
    /// Plans payload broadcast for byte-identical duplicate rows the manifest does not list.
    /// <para>
    /// Payload-bearing (run/macro) strings cannot be broadcast by matching tokenized source text
    /// the way plain rows are (see <see cref="Decide"/>): the corpus stores a run-aware
    /// tokenization (e.g. <c>Return to &lt;EnNoun&gt;&lt;Run&gt;PlaceName&lt;RunEnd&gt;…</c>) while
    /// the packager re-tokenizes the base EXD with the flat tokenizer (e.g.
    /// <c>Return to &lt;EnNoun&gt;&lt;Raw&gt;&lt;Payload03&gt;…</c>), so the two source strings never
    /// compare equal and the catalog/source-string join silently drops every run-bearing duplicate.
    /// </para>
    /// <para>
    /// The reliable identity here is the raw byte hash, not the tokenized text: the corpus dedup
    /// collapses BYTE-IDENTICAL rows onto the lowest row id, so a duplicate row shares the
    /// representative's exact bytes. This method keys on <c>(Field, RawHash)</c>: for every base row
    /// that carries a payload column and IS explicitly translated, it records the reviewed corpus
    /// replacement; then, for every base row with the same field and identical raw bytes that is NOT
    /// explicitly listed, it emits that same reviewed replacement. Reusing the representative's own
    /// source/target (which already patches the representative's identical bytes) is safe by
    /// construction and never re-derives a target from the divergent flat tokenization.
    /// </para>
    /// <para>
    /// The <c>(Field, RawHash)</c> gate preserves the invariant that payload is broadcast only to
    /// rows whose bytes match an approved row exactly; if two explicitly-translated rows share the
    /// same field and raw bytes but disagree on the target, the signature is disabled (null) so no
    /// ambiguous payload is broadcast.
    /// </para>
    /// </summary>
    public static IReadOnlyList<PayloadSiblingDecision> PlanPayloadSiblings(
        IEnumerable<BroadcastColumn> columns,
        IReadOnlyDictionary<uint, IReadOnlyList<StringReplacement>> explicitReplacements)
    {
        var columnList = columns as IReadOnlyList<BroadcastColumn> ?? columns.ToList();

        // (Field, RawHash) -> reviewed replacement to broadcast; null once the target is ambiguous.
        var bySignature = new Dictionary<(string Field, string RawHash), StringReplacement?>();
        foreach (var column in columnList)
        {
            if (!column.HasPayload
                || column.Field.Length == 0
                || !explicitReplacements.TryGetValue(column.RowId, out var replacements))
            {
                continue;
            }

            foreach (var replacement in replacements)
            {
                if (replacement.Field != column.Field
                    || !SeStringCompatibilityValidator.Validate(replacement.Source, replacement.Target).IsCompatible)
                {
                    continue;
                }

                var key = (column.Field, column.RawHash);
                if (!bySignature.TryGetValue(key, out var existing))
                {
                    bySignature[key] = replacement;
                }
                else if (existing is not null && existing.Target != replacement.Target)
                {
                    bySignature[key] = null;
                }
            }
        }

        if (bySignature.Count == 0)
        {
            return [];
        }

        var decisions = new List<PayloadSiblingDecision>();
        foreach (var column in columnList)
        {
            if (!column.HasPayload
                || column.Field.Length == 0
                || explicitReplacements.ContainsKey(column.RowId))
            {
                continue;
            }

            if (bySignature.TryGetValue((column.Field, column.RawHash), out var replacement)
                && replacement is not null)
            {
                decisions.Add(new PayloadSiblingDecision(column.RowId, replacement));
            }
        }

        return decisions;
    }

    /// <summary>
    /// Plans plain-text broadcast for a base-EXD column. Payload-bearing columns are never
    /// broadcast here — their tokenized source diverges from the corpus, so they are handled by
    /// <see cref="PlanPayloadSiblings"/> (byte-identity broadcast) instead.
    /// </summary>
    public static BroadcastDecision? Decide(
        BroadcastCatalog catalog,
        string sheet,
        BroadcastColumn column)
    {
        if (column.HasPayload)
        {
            return null;
        }

        var resolved = catalog.Resolve(sheet, column.Field, column.Source, allowAnyField: true);
        if (resolved is null)
        {
            return null;
        }

        return new BroadcastDecision(
            resolved.Target,
            string.IsNullOrEmpty(resolved.Field) ? null : resolved.Field,
            BroadcastKind.Plain);
    }
}

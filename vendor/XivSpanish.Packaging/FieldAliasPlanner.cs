using XivSpanish.GameData;

namespace XivSpanish.Packager;

public sealed record FieldAliasDecision(uint RowId, string Source, string Target, string ReplacementField);

public static class FieldAliasPlanner
{
    private const string ItemSheet = "Item";
    private const string ItemSingularField = "Singular";
    private const string ItemNameField = "Name";

    public static IReadOnlyList<FieldAliasDecision> Decide(
        string sheet,
        IEnumerable<BroadcastColumn> columns,
        IReadOnlyDictionary<uint, IReadOnlyList<StringReplacement>> replacements)
    {
        if (!string.Equals(sheet, ItemSheet, StringComparison.Ordinal))
        {
            return [];
        }

        return DecideSameRowAlias(
            columns,
            replacements,
            sourceField: ItemSingularField,
            targetField: ItemNameField);
    }

    private static IReadOnlyList<FieldAliasDecision> DecideSameRowAlias(
        IEnumerable<BroadcastColumn> columns,
        IReadOnlyDictionary<uint, IReadOnlyList<StringReplacement>> replacements,
        string sourceField,
        string targetField)
    {
        var decisions = new List<FieldAliasDecision>();
        foreach (var row in columns.GroupBy(column => column.RowId).OrderBy(group => group.Key))
        {
            var sourceColumn = row.FirstOrDefault(column => column.Field == sourceField);
            var targetColumn = row.FirstOrDefault(column => column.Field == targetField);
            if (sourceColumn is null
                || targetColumn is null
                || sourceColumn.HasPayload
                || targetColumn.HasPayload
                || sourceColumn.Source != targetColumn.Source)
            {
                continue;
            }

            if (!replacements.TryGetValue(row.Key, out var rowReplacements))
            {
                continue;
            }

            if (HasReplacementForField(rowReplacements, targetField, targetColumn.Source))
            {
                continue;
            }

            var sourceReplacement = rowReplacements.FirstOrDefault(replacement =>
                replacement.Field == sourceField
                && replacement.Source == sourceColumn.Source);
            if (sourceReplacement is null)
            {
                continue;
            }

            decisions.Add(new FieldAliasDecision(
                row.Key,
                targetColumn.Source,
                sourceReplacement.Target,
                targetField));
        }

        return decisions;
    }

    private static bool HasReplacementForField(
        IEnumerable<StringReplacement> replacements,
        string field,
        string source)
        => replacements.Any(replacement =>
            replacement.Field == field
            || (replacement.Field is null && replacement.Source == source));
}

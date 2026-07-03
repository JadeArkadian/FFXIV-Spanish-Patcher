using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Lumina.Data.Structs.Excel;
using Lumina.Excel;
using XivSpanish.Translation;
using LuminaGameData = Lumina.GameData;

namespace XivSpanish.GameData;

/// <summary>
/// Resolves the ordered string-column field names of a generated Lumina sheet type, so the
/// packager can target an <see cref="ExdPatcher"/> replacement at a specific column by its
/// source-key <c>Field</c> (e.g. <c>Singular</c> vs <c>Plural</c> on <c>ENpcResident</c>).
/// <para>
/// This mirrors the extractor's <c>MapStringColumnNames</c>: the i-th string column is named
/// after the i-th string-typed (SeString) member of the generated type in declaration order,
/// reusing <see cref="StringColumnFieldNames.Resolve"/> so packaging inverts exactly the same
/// mapping the manifest was written with. It depends only on the Lumina sheet assembly (pure
/// reflection), never on the live client, so it is reproducible from a vanilla snapshot.
/// </para>
/// </summary>
public static class SheetStringFieldResolver
{
    private static readonly Lazy<Type[]> SheetTypes = new(LoadSheetTypes);

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "The app roots Lumina and Lumina.Excel when publishing trimmed; sheet type metadata is intentionally preserved.")]
    private static Type[] LoadSheetTypes()
        => typeof(Lumina.Excel.Sheets.Quest).Assembly
            .GetTypes()
            .Where(type => type.IsValueType && type.Namespace == "Lumina.Excel.Sheets")
            .OrderBy(type => type.Name)
            .ToArray();

    /// <summary>
    /// Returns the field name of every string column of <paramref name="sheet"/> in EXH
    /// string-column order, given the EXH's actual string-column <paramref name="stringColumnCount"/>.
    /// Falls back to stable <c>Column{index}</c> labels when the type is unknown or its string
    /// member count does not match the EXH (matching the extractor's behavior exactly).
    /// </summary>
    public static IReadOnlyList<string> Resolve(string sheet, int stringColumnCount)
    {
        var sheetType = SheetTypes.Value.FirstOrDefault(type =>
            string.Equals(type.Name, sheet, StringComparison.Ordinal));

        // Includes collection members (Collection<ReadOnlySeString>, e.g. GuildleveAssignmentTalk's
        // Talk) so they expand to the Name[index] paths the extractor's typed scan emits; the shared
        // resolver infers the collection size from the EXH string-column count.
        var stringMembers = sheetType is null
            ? null
            : GetReadableMembers(sheetType)
                .Where(member => IsTextLike(member.Type) || StringColumnFieldNames.IsTextLikeCollection(member.Type))
                .Select(member => new StringColumnMember(member.Name, StringColumnFieldNames.IsTextLikeCollection(member.Type)))
                .ToArray();

        return StringColumnFieldNames.Resolve(stringMembers, stringColumnCount);
    }

    /// <summary>
    /// Returns the field name of every string column of <paramref name="sheet"/> in EXH
    /// string-column (physical offset) order, aligning each generated string member to the
    /// physical column it actually reads via Lumina.
    /// <para>
    /// The reflection-only <see cref="Resolve(string,int)"/> assumes the generated type lists its
    /// string members in the same order the EXH defines its String columns. That holds for most
    /// sheets (e.g. <c>ENpcResident</c> = Singular, Plural, Title at ascending offsets) but NOT for
    /// sheets whose generated declaration order is permuted relative to the on-disk column offsets
    /// (e.g. <c>TextCommand</c>, where <c>Description</c> lives at offset 0 but is declared first
    /// while its physical column is the 3rd). For those, the positional zip mislabels columns and
    /// the patcher targets the wrong column, so every field-targeted replacement misses.
    /// </para>
    /// <para>
    /// This overload pins each member to its physical String-column ordinal by reading the sheet
    /// through Lumina (typed values vs raw String columns) and aggregating the match across rows
    /// until each member is uniquely resolved. It needs the live game data, so the packager uses it
    /// only on the client-backed build path; the snapshot build falls back to the reflection-only
    /// overload. Returns null when the alignment cannot be determined unambiguously (the caller then
    /// falls back to <see cref="Resolve(string,int)"/>), so a partial/uncertain map never ships.
    /// </para>
    /// </summary>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2075",
        Justification = "The app roots Lumina and Lumina.Excel for trimmed publish; reflected typed-sheet members remain available.")]
    public static IReadOnlyList<string>? ResolveByOffset(
        LuminaGameData gameData,
        string sheet,
        int stringColumnCount)
    {
        var sheetType = SheetTypes.Value.FirstOrDefault(type =>
            string.Equals(type.Name, sheet, StringComparison.Ordinal));
        if (sheetType is null)
        {
            return null;
        }

        var members = GetReadableMembers(sheetType).ToArray();

        // Collection members (Collection<ReadOnlySeString>) cannot be compared member-by-member
        // against physical columns here: return null so the caller falls back to the reflection-only
        // Resolve, which expands them to Name[index] paths in declaration order.
        if (members.Any(member => StringColumnFieldNames.IsTextLikeCollection(member.Type)))
        {
            return null;
        }

        var stringMembers = members
            .Where(member => IsTextLike(member.Type))
            .Select(member => member.Name)
            .ToArray();

        // The member count must match the EXH string-column count, exactly as the reflection-only
        // resolver requires; otherwise the alignment is undefined and we defer to the fallback.
        if (stringMembers.Length != stringColumnCount || stringColumnCount == 0)
        {
            return null;
        }

        var rawRows = TryGetRawRows(gameData, sheet);
        var typedSheet = TryGetTypedSheet(gameData, sheetType);
        var getRow = typedSheet?.GetType().GetMethod("GetRowOrDefault", [typeof(uint)]);
        if (rawRows is null || typedSheet is null || getRow is null)
        {
            return null;
        }

        var memberProps = stringMembers
            .Select(name => sheetType.GetProperty(name)!)
            .ToArray();

        // candidate[memberIndex] = the set of String-column ordinals still consistent with every
        // row seen so far. Starts as "any ordinal" and narrows to one as rows disambiguate.
        var candidates = new HashSet<int>[stringMembers.Length];
        for (var m = 0; m < candidates.Length; m++)
        {
            candidates[m] = Enumerable.Range(0, stringColumnCount).ToHashSet();
        }

        foreach (RawRow rawRow in rawRows)
        {
            var typedRow = getRow.Invoke(typedSheet, [rawRow.RowId]);
            if (typedRow is null)
            {
                continue;
            }

            var physical = ReadPhysicalStringColumns(rawRow, stringColumnCount);
            for (var m = 0; m < memberProps.Length; m++)
            {
                var typedValue = memberProps[m].GetValue(typedRow)?.ToString() ?? string.Empty;
                var narrowed = new HashSet<int>();
                foreach (var ordinal in candidates[m])
                {
                    if (string.Equals(physical[ordinal], typedValue, StringComparison.Ordinal))
                    {
                        narrowed.Add(ordinal);
                    }
                }

                // Keep the previous set if this row matched nothing (e.g. all-empty row): it must
                // never widen, only narrow, so an inconsistent row cannot reintroduce a rejected
                // ordinal. A genuinely empty member stays ambiguous until a row pins it.
                if (narrowed.Count > 0)
                {
                    candidates[m] = narrowed;
                }

                // Early exit once every member is pinned to exactly one ordinal.
                if (candidates.All(set => set.Count == 1))
                {
                    return BuildOrdered(candidates, stringMembers, stringColumnCount);
                }
            }
        }

        return BuildOrdered(candidates, stringMembers, stringColumnCount);
    }

    // Inverts the member->ordinal candidate sets into the column-ordered name array the patcher
    // needs (names[ordinal] = member). Returns null unless every member is pinned to a distinct
    // ordinal and every column ends up named, so an ambiguous alignment is rejected outright.
    private static string[]? BuildOrdered(
        HashSet<int>[] candidates,
        string[] members,
        int stringColumnCount)
    {
        var names = new string[stringColumnCount];
        var filled = new bool[stringColumnCount];
        for (var m = 0; m < candidates.Length; m++)
        {
            if (candidates[m].Count != 1)
            {
                return null;
            }

            var ordinal = candidates[m].Single();
            if (filled[ordinal])
            {
                return null; // Two members resolved to the same column: ambiguous, reject.
            }

            names[ordinal] = members[m];
            filled[ordinal] = true;
        }

        return filled.All(f => f) ? names : null;
    }

    private static string[] ReadPhysicalStringColumns(RawRow row, int stringColumnCount)
    {
        var values = new string[stringColumnCount];
        var ordinal = 0;
        var columns = row.Columns;
        for (var i = 0; i < columns.Count && ordinal < stringColumnCount; i++)
        {
            if (columns[i].Type != ExcelColumnDataType.String)
            {
                continue;
            }

            values[ordinal] = row.ReadStringColumn(i).ExtractText();
            ordinal++;
        }

        return values;
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2060",
        Justification = "The app roots Lumina and Lumina.Excel for trimmed publish; reflected generic sheet APIs remain available.")]
    private static IEnumerable? TryGetRawRows(LuminaGameData gameData, string sheet)
    {
        try
        {
            var getRawSheet = typeof(ExcelModule)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(method => method.Name == "GetSheet" && method.IsGenericMethodDefinition)
                .First(method => method.GetParameters().Length >= 2)
                .MakeGenericMethod(typeof(RawRow));

            return getRawSheet.Invoke(gameData.Excel, [Lumina.Data.Language.English, sheet]) as IEnumerable;
        }
        catch
        {
            return null;
        }
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2060",
        Justification = "The app roots Lumina and Lumina.Excel for trimmed publish; reflected generated sheet types remain available.")]
    private static object? TryGetTypedSheet(LuminaGameData gameData, Type sheetType)
    {
        try
        {
            var getExcelSheet = typeof(LuminaGameData)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(method => method.Name == "GetExcelSheet" && method.IsGenericMethodDefinition)
                .First(method => method.GetParameters().Length >= 1)
                .MakeGenericMethod(sheetType);

            return getExcelSheet.Invoke(gameData, [Lumina.Data.Language.English, null]);
        }
        catch
        {
            return null;
        }
    }

    // Public instance members in reflection order: properties first, then fields, excluding the
    // Lumina infrastructure members. Matches the extractor's GetReadableMembers ordering so the
    // resolved field names line up with the manifest the extractor produced.
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2070",
        Justification = "The app roots Lumina.Excel for trimmed publish; generated sheet public fields and properties remain available.")]
    private static IEnumerable<(string Name, Type Type)> GetReadableMembers(Type type)
    {
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length == 0 && !IsInfrastructureMember(property.Name))
            {
                yield return (property.Name, property.PropertyType);
            }
        }

        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!IsInfrastructureMember(field.Name))
            {
                yield return (field.Name, field.FieldType);
            }
        }
    }

    private static bool IsInfrastructureMember(string name)
        => name is "ExcelPage" or "Module" or "Columns" or "OffsetLookupTable" or "RawSheet";

    private static bool IsTextLike(Type type)
        => type.Name.Contains("SeString", StringComparison.OrdinalIgnoreCase)
        || type.Name.Contains("ReadOnlySeString", StringComparison.OrdinalIgnoreCase);
}

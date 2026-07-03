namespace XivSpanish.Translation;

/// <summary>
/// Names the string columns of a hash-mismatched sheet read through the RawRow fallback.
/// <para>
/// When a sheet's generated row type fails its column-hash check (e.g. <c>Status</c> after
/// a patch), the extractor re-reads it as a hash-agnostic RawRow sheet. RawRow exposes
/// columns by index, not by name, so this maps the i-th string column to the i-th
/// string-typed member of the generated type (declaration order) — reproducing the field
/// paths a typed scan would emit (<c>Name</c>, <c>Description</c>, ...). When the member
/// list is unavailable or its count does not match the EXH string-column count, it falls
/// back to a stable <c>Column{index}</c> label so output stays deterministic.
/// </para>
/// </summary>
/// <summary>
/// One string-bearing member of a generated sheet type, in declaration order. A scalar member
/// (e.g. <c>ReadOnlySeString Singular</c>) owns exactly one string column; a collection member
/// (e.g. <c>Collection&lt;ReadOnlySeString&gt; Talk</c>) owns a contiguous run of string columns
/// whose cardinality is not present in the type and must be inferred from the EXH column count.
/// </summary>
public readonly record struct StringColumnMember(string Name, bool IsCollection);

public static class StringColumnFieldNames
{
    /// <summary>
    /// True when <paramref name="type"/> is a text-like collection: a generic enumerable whose
    /// single type argument is a SeString type (Lumina's <c>Collection&lt;ReadOnlySeString&gt;</c>).
    /// The extractor emits each element of such a member as <c>Name[index]</c>.
    /// </summary>
    public static bool IsTextLikeCollection(Type type)
        => type.IsGenericType
        && typeof(System.Collections.IEnumerable).IsAssignableFrom(type)
        && type.GetGenericArguments() is [var element]
        && IsTextLike(element);

    /// <summary>True when <paramref name="type"/> is a SeString-like scalar text type.</summary>
    public static bool IsTextLike(Type type)
        => type.Name.Contains("SeString", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Maps <paramref name="stringColumnCount"/> string columns to field names from the generated
    /// type's string-bearing <paramref name="members"/> (declaration order), expanding collection
    /// members to <c>Name[0]..Name[k-1]</c> — the exact paths the extractor's typed scan emits.
    /// <para>
    /// A collection's cardinality is not encoded in the type, so it is inferred: with exactly ONE
    /// collection its size is <c>stringColumnCount - scalarCount</c>. With two or more collections
    /// the split is ambiguous, and with impossible counts the map would be wrong; both cases fall
    /// back to stable <c>Column{index}</c> labels (never a partial/incorrect map).
    /// </para>
    /// </summary>
    public static string[] Resolve(IReadOnlyList<StringColumnMember>? members, int stringColumnCount)
    {
        if (stringColumnCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stringColumnCount));
        }

        if (members is null)
        {
            return Resolve((IReadOnlyList<string>?)null, stringColumnCount);
        }

        var collectionCount = members.Count(member => member.IsCollection);
        if (collectionCount == 0)
        {
            return Resolve(members.Select(member => member.Name).ToArray(), stringColumnCount);
        }

        // Two or more collections: the column split between them is ambiguous. Fall back.
        if (collectionCount > 1)
        {
            return Resolve((IReadOnlyList<string>?)null, stringColumnCount);
        }

        var scalarCount = members.Count - 1;
        var collectionSize = stringColumnCount - scalarCount;
        if (collectionSize < 1)
        {
            return Resolve((IReadOnlyList<string>?)null, stringColumnCount);
        }

        var names = new List<string>(stringColumnCount);
        foreach (var member in members)
        {
            if (member.IsCollection)
            {
                for (var index = 0; index < collectionSize; index++)
                {
                    names.Add($"{member.Name}[{index}]");
                }
            }
            else
            {
                names.Add(member.Name);
            }
        }

        return [.. names];
    }

    /// <summary>
    /// Maps <paramref name="stringColumnCount"/> string columns to field names using
    /// <paramref name="stringMemberNames"/> (the generated type's string members in
    /// declaration order). Returns the member names only when their count matches the
    /// column count; otherwise returns <c>Column0..Column{n-1}</c>.
    /// </summary>
    public static string[] Resolve(IReadOnlyList<string>? stringMemberNames, int stringColumnCount)
    {
        if (stringColumnCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stringColumnCount));
        }

        if (stringMemberNames is not null && stringMemberNames.Count == stringColumnCount)
        {
            return stringMemberNames.ToArray();
        }

        return Enumerable.Range(0, stringColumnCount).Select(index => $"Column{index}").ToArray();
    }
}

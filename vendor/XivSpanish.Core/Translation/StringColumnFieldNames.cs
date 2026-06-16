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
public static class StringColumnFieldNames
{
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

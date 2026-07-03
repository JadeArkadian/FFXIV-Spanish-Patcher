using System.Buffers.Binary;
using System.Text;

namespace XivSpanish.GameData;

/// <summary>
/// A single approved string swap inside one row.
/// <para>
/// <paramref name="Field"/> is the optional source-key field/column name (e.g. <c>Singular</c>,
/// <c>Plural</c>). When supplied AND the patcher is given the sheet's ordered string-column
/// field names, the replacement is targeted at that column's blob offset only, so a source that
/// is a substring of another column's string (the Singular-vs-Plural collision) cannot hijack the
/// wrong offset. When null/unknown the patcher falls back to content-matching across all columns.
/// </para>
/// </summary>
public sealed record StringReplacement(string Source, string Target, string? Field = null);

/// <summary>Outcome of patching one EXD page.</summary>
/// <param name="Bytes">The rewritten EXD bytes.</param>
/// <param name="Applied">Replacements that matched an existing string and were written.</param>
/// <param name="Missed">Replacements whose source string was not found in its row.</param>
public sealed record ExdPatchResult(byte[] Bytes, int Applied, IReadOnlyList<MissedReplacement> Missed);

public sealed record MissedReplacement(uint RowId, string Source, string Reason);

/// <summary>
/// Rewrites the variable-length string area of EXD rows for the <c>Default</c> excel
/// variant. All EXD integers are big-endian. String columns hold a u32 offset into the
/// row's string blob; replacing a string of a different length shifts the blob, so the
/// blob is rebuilt and every string-column offset, the row <c>DataSize</c>, and the row
/// offset table are recomputed.
/// </summary>
public static class ExdPatcher
{
    private const int HeaderSize = ExdPage.HeaderSize;
    private const int RowHeaderSize = ExdPage.RowHeaderSize;
    private const int RowAlignment = ExdPage.RowAlignment;

    /// <param name="original">Raw EXD page bytes.</param>
    /// <param name="fixedDataSize">EXH <c>DataOffset</c>: fixed row size in bytes.</param>
    /// <param name="stringColumnOffsets">Fixed-data offsets of String columns (from the EXH).</param>
    /// <param name="replacements">Replacements keyed by row id.</param>
    /// <param name="stringColumnFieldNames">
    /// Optional ordered string-column field names: the i-th name belongs to
    /// <paramref name="stringColumnOffsets"/>[i]. When supplied, a <see cref="StringReplacement"/>
    /// that carries a matching <see cref="StringReplacement.Field"/> is targeted at that column
    /// only (fixing the substring collision where, e.g., <c>Singular</c> is a prefix of
    /// <c>Plural</c>). When null/empty, every replacement falls back to content matching.
    /// </param>
    public static ExdPatchResult Patch(
        byte[] original,
        int fixedDataSize,
        IReadOnlyList<int> stringColumnOffsets,
        IReadOnlyDictionary<uint, IReadOnlyList<StringReplacement>> replacements,
        IReadOnlyList<string>? stringColumnFieldNames = null)
    {
        if (!ExdPage.HasExdfMagic(original))
        {
            throw new InvalidDataException("Not an EXDF file.");
        }

        var rowCount = ExdPage.ReadRowCount(original);

        var entries = new List<(uint RowId, uint Offset)>(rowCount);
        for (var i = 0; i < rowCount; i++)
        {
            entries.Add(ExdPage.ReadIndexEntry(original, i));
        }

        var missed = new List<MissedReplacement>();
        var applied = 0;
        var newOffsets = new uint[rowCount];

        using var body = new MemoryStream();
        var dataStart = (uint)(HeaderSize + (rowCount * 8));
        var rowHeader = new byte[RowHeaderSize];

        for (var i = 0; i < rowCount; i++)
        {
            var (rowId, offset) = entries[i];

            // A row whose declared offset cannot even hold a row header is unrecoverable: the
            // page cannot be rebuilt safely, so fail loudly instead of shipping a corrupt file.
            if (ExdPage.TryReadRow(original, fixedDataSize, rowId, offset) is not { } row)
            {
                throw new InvalidDataException($"Row {rowId} offset {offset} is out of range.");
            }

            var rowCountField = original.AsSpan((int)offset + 4, 2).ToArray();
            var fixedStart = row.FixedStart;
            var stringStart = row.StringStart;
            var stringLength = row.StringLength;
            var rowBlockEnd = (long)offset + RowHeaderSize + Math.Max(row.DataSize, 0);

            // Subrow variant (RowCount > 1) and malformed rows are NOT supported: copy the
            // original row block verbatim so the page stays valid, and report any pending
            // replacement as missed instead of silently corrupting the row.
            var unsupported = !row.IsSupported;

            byte[] fixedData;
            byte[] stringBlob;

            if (unsupported)
            {
                // Copy the original row block verbatim (the bytes that actually fit) so the
                // page stays structurally valid, and report any pending replacement as missed.
                var safeEnd = (int)Math.Min(rowBlockEnd, original.Length);
                var preservedDataSize = Math.Max(0, safeEnd - fixedStart);
                var preserved = original.AsSpan(fixedStart, preservedDataSize).ToArray();

                if (replacements.TryGetValue(rowId, out var skippedReplacements))
                {
                    foreach (var replacement in skippedReplacements)
                    {
                        missed.Add(new MissedReplacement(rowId, replacement.Source,
                            row.SubRowCount != 1
                                ? "subrow variant row not supported"
                                : "malformed row block, skipped"));
                    }
                }

                newOffsets[i] = dataStart + (uint)body.Length;
                BinaryPrimitives.WriteUInt32BigEndian(rowHeader, (uint)preservedDataSize);
                rowCountField.CopyTo(rowHeader, 4);
                body.Write(rowHeader);
                body.Write(preserved);

                var skipPad = (RowAlignment - ((RowHeaderSize + preservedDataSize) % RowAlignment)) % RowAlignment;
                for (var p = 0; p < skipPad; p++)
                {
                    body.WriteByte(0);
                }

                continue;
            }

            if (replacements.TryGetValue(rowId, out var rowReplacements))
            {
                (fixedData, stringBlob, var rowApplied) = RebuildRow(
                    original,
                    fixedStart,
                    fixedDataSize,
                    stringStart,
                    stringLength,
                    stringColumnOffsets,
                    stringColumnFieldNames,
                    rowId,
                    rowReplacements,
                    missed);
                applied += rowApplied;
            }
            else
            {
                fixedData = original.AsSpan(fixedStart, fixedDataSize).ToArray();
                stringBlob = original.AsSpan(stringStart, Math.Max(0, stringLength)).ToArray();
            }

            newOffsets[i] = dataStart + (uint)body.Length;
            var newDataSize = fixedData.Length + stringBlob.Length;

            BinaryPrimitives.WriteUInt32BigEndian(rowHeader, (uint)newDataSize);
            rowCountField.CopyTo(rowHeader, 4);
            body.Write(rowHeader);
            body.Write(fixedData);
            body.Write(stringBlob);

            var padding = (RowAlignment - ((RowHeaderSize + newDataSize) % RowAlignment)) % RowAlignment;
            for (var p = 0; p < padding; p++)
            {
                body.WriteByte(0);
            }
        }

        var bodyBytes = body.ToArray();
        var output = new byte[dataStart + bodyBytes.Length];
        original.AsSpan(0, HeaderSize).CopyTo(output);

        // Update the data-section size field (u32 big-endian) to reflect the new body length.
        // The original header value is stale after patching; the game allocates a buffer of
        // DataSize bytes and then dereferences row offsets into it — if DataSize is smaller
        // than the actual body the game crashes on any row whose offset lands beyond the limit.
        BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(ExdPage.DataSectionSizeOffset, 4), (uint)bodyBytes.Length);

        for (var i = 0; i < rowCount; i++)
        {
            var entryPos = HeaderSize + (i * 8);
            BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(entryPos, 4), entries[i].RowId);
            BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(entryPos + 4, 4), newOffsets[i]);
        }

        bodyBytes.CopyTo(output, (int)dataStart);
        return new ExdPatchResult(output, applied, missed);
    }

    private static (byte[] Fixed, byte[] Strings, int Applied) RebuildRow(
        byte[] original,
        int fixedStart,
        int fixedDataSize,
        int stringStart,
        int stringLength,
        IReadOnlyList<int> stringColumnOffsets,
        IReadOnlyList<string>? stringColumnFieldNames,
        uint rowId,
        IReadOnlyList<StringReplacement> rowReplacements,
        List<MissedReplacement> missed)
    {
        var fixedData = original.AsSpan(fixedStart, fixedDataSize).ToArray();
        var stringArea = original.AsSpan(stringStart, stringLength);

        // Map a replacement's Field to the string-column ordinal it targets. Only usable when
        // the caller supplied the ordered field names; the i-th name belongs to the i-th
        // string column (stringColumnOffsets[i]). This is the inverse of the extractor's
        // string-column → field-name mapping, so Field round-trips back to the same column.
        var fieldToOrdinal = new Dictionary<string, int>(StringComparer.Ordinal);
        if (stringColumnFieldNames is not null)
        {
            for (var ordinal = 0; ordinal < stringColumnFieldNames.Count && ordinal < stringColumnOffsets.Count; ordinal++)
            {
                // First mapping wins so a duplicated field name cannot silently retarget; the
                // extractor emits distinct names per string column in practice.
                fieldToOrdinal.TryAdd(stringColumnFieldNames[ordinal], ordinal);
            }

            // Positional alias: Column{i} always targets ordinal i. Corpora extracted while the
            // resolver fell back to Column{i} labels (e.g. Fate before collection expansion) must
            // stay field-targeted when the resolver later learns the real member names; Column{i}
            // is positional by definition of the extractor fallback, so the alias is correct on
            // any sheet. TryAdd keeps a real member name that happens to spell Column{i} authoritative.
            for (var ordinal = 0; ordinal < stringColumnOffsets.Count; ordinal++)
            {
                fieldToOrdinal.TryAdd($"Column{ordinal}", ordinal);
            }
        }

        // Partition replacements:
        //  - fieldTargeted[ordinal]: a replacement whose Field resolves to that exact column. It
        //    is applied to ONLY that column (fixing the substring collision: a Singular source
        //    that is a prefix of the Plural string can no longer hijack the Plural column).
        //  - contentMatched: non-empty source, no usable field → legacy content matching across
        //    the remaining (non-field-targeted) columns.
        //  - emptyWrites: empty source, no usable field → legacy write-at-offset.
        var fieldTargeted = new Dictionary<int, StringReplacement>();
        var contentMatched = new List<StringReplacement>();
        var emptyWrites = new Queue<StringReplacement>();
        foreach (var rep in rowReplacements)
        {
            if (rep.Field is not null && fieldToOrdinal.TryGetValue(rep.Field, out var ordinal)
                && stringColumnOffsets[ordinal] + 4 <= fixedData.Length
                && !fieldTargeted.ContainsKey(ordinal))
            {
                fieldTargeted[ordinal] = rep;
            }
            else if (rep.Source.Length == 0)
            {
                emptyWrites.Enqueue(rep);
            }
            else
            {
                contentMatched.Add(rep);
            }
        }

        // Columns whose field-targeted replacement was actually APPLIED are excluded from the
        // legacy content/empty rebuild below (they already got a fresh blob offset in Phase A).
        // A field-targeted column whose replacement MISSES must NOT be excluded: it has to fall
        // through to Phase B so its original string is copied into the rebuilt blob and the column
        // is remapped. Otherwise the column keeps a stale offset into the discarded blob and reads
        // as empty/garbage (observed: rows became blank when a field target missed).
        var fieldTargetedOffsets = new HashSet<int>();

        var matchedSources = new HashSet<string>(StringComparer.Ordinal);
        var rejectedSources = new Dictionary<string, string>(StringComparer.Ordinal);
        using var newBlob = new MemoryStream();
        var appliedFieldTargets = 0;

        // Phase A — field-targeted columns. Each gets its OWN fresh blob offset (sharing is split
        // when targets differ; if a previously-shared vanilla offset feeds two columns with the
        // same target, they each get an independent copy here, which is correct and harmless).
        // CRITICAL: blobs hold SeString payloads (bytes ≥0x80 that are not valid UTF-8); we go
        // through SeStringParser so payload bytes are preserved exactly and only literals change.
        foreach (var (ordinal, rep) in fieldTargeted)
        {
            var columnOffset = stringColumnOffsets[ordinal];
            var oldOffset = BinaryPrimitives.ReadUInt32BigEndian(fixedData.AsSpan(columnOffset, 4));
            var currentRaw = ReadNulTerminatedBytes(stringArea, (int)oldOffset);

            byte[]? newBytes = null;
            if (currentRaw.Length == 0)
            {
                // Empty column: write-at-offset. A non-empty source against an empty column has
                // nothing to match, so only an empty (or matching-empty) source may write here.
                if (rep.Source.Length == 0)
                {
                    newBytes = Encoding.UTF8.GetBytes(rep.Target);
                }
            }
            else if (rep.Source.Length != 0)
            {
                var segments = SeStringParser.Parse(currentRaw);
                if (SeStringParser.TryReplace(segments, rep.Source, rep.Target, out var newSegments, out var reason))
                {
                    newBytes = GuardRunIntegrity(currentRaw, TrimTerminator(SeStringParser.Serialize(newSegments)), rep, rejectedSources);
                }
                else if (SeStringParser.TryReplaceTokenized(segments, rep.Source, rep.Target, out var tokenSegments, out _))
                {
                    newBytes = GuardRunIntegrity(currentRaw, TrimTerminator(SeStringParser.Serialize(tokenSegments)), rep, rejectedSources);
                }
                else if (SeStringTree.TryTranslate(currentRaw, rep.Source, rep.Target, out var runBytes, out _))
                {
                    // Run-aware path: matches a corpus source tokenized with run brackets and
                    // recomputes every 0xFF run length on serialize (translating text inside a run
                    // is structurally safe). Used for the Addon rows that carry length-prefixed runs.
                    newBytes = runBytes;
                }
                else if (reason is "target token references do not match source"
                    or "target control references do not match source")
                {
                    rejectedSources.TryAdd(rep.Source, reason);
                }
            }

            if (newBytes is null)
            {
                // The field column could not receive the target (empty source vs non-empty
                // column, non-empty source absent from the column, or a rejected target). Leave
                // the column pointing where it did and report a precise miss below.
                missed.Add(new MissedReplacement(rowId, rep.Source,
                    rejectedSources.GetValueOrDefault(rep.Source)
                        ?? (currentRaw.Length == 0
                            ? "field column is empty; only an empty-source write-at-offset can fill it"
                            // Same wording as the legacy content-match miss so the packager's
                            // contamination guard counts a genuinely-absent source identically.
                            : "source string not present in row string columns")));
                continue;
            }

            var freshOffset = (uint)newBlob.Length;
            newBlob.Write(newBytes);
            newBlob.WriteByte(0);
            BinaryPrimitives.WriteUInt32BigEndian(fixedData.AsSpan(columnOffset, 4), freshOffset);
            matchedSources.Add(rep.Source);
            appliedFieldTargets++;

            // Only an APPLIED field target owns its column; missed ones fall through to Phase B.
            fieldTargetedOffsets.Add(columnOffset);
        }

        // Distinct blob offsets referenced by the columns NOT owned by a field target, ascending.
        var referenced = new SortedSet<uint>();
        foreach (var columnOffset in stringColumnOffsets)
        {
            if (columnOffset + 4 <= fixedData.Length && !fieldTargetedOffsets.Contains(columnOffset))
            {
                referenced.Add(BinaryPrimitives.ReadUInt32BigEndian(fixedData.AsSpan(columnOffset, 4)));
            }
        }

        // Phase B (legacy) — content-matched replacements over the remaining shared offsets.
        var remap = new Dictionary<uint, uint>();
        foreach (var oldOffset in referenced)
        {
            var currentRaw = ReadNulTerminatedBytes(stringArea, (int)oldOffset);
            byte[] newBytes;

            if (currentRaw.Length > 0)
            {
                var segments = SeStringParser.Parse(currentRaw);
                string? appliedSource = null;
                byte[]? appliedBytes = null;

                foreach (var rep in contentMatched)
                {
                    if (SeStringParser.TryReplace(segments, rep.Source, rep.Target, out var newSegments, out var replaceReason))
                    {
                        segments = newSegments;
                        appliedSource = rep.Source;
                        break; // Only apply the first matching replacement per string.
                    }

                    if (replaceReason is "target token references do not match source"
                        or "target control references do not match source")
                    {
                        rejectedSources.TryAdd(rep.Source, replaceReason);
                    }

                    if (SeStringParser.TryReplaceTokenized(segments, rep.Source, rep.Target, out var tokenSegments, out _))
                    {
                        segments = tokenSegments;
                        appliedSource = rep.Source;
                        break;
                    }

                    if (SeStringTree.TryTranslate(currentRaw, rep.Source, rep.Target, out var runBytes, out _))
                    {
                        appliedBytes = runBytes;
                        appliedSource = rep.Source;
                        break;
                    }
                }

                newBytes = appliedBytes ?? TrimTerminator(SeStringParser.Serialize(segments));

                if (appliedSource is not null)
                {
                    if (RunPatchIsCoherent(currentRaw, newBytes))
                    {
                        matchedSources.Add(appliedSource);
                    }
                    else
                    {
                        // Defense in depth (Duty Finder crash class): the flat-path patch desynced
                        // a 0xFF run; revert this string to its vanilla bytes and report a miss.
                        newBytes = currentRaw;
                        rejectedSources.TryAdd(appliedSource, StaleRunLengthMissReason);
                    }
                }
            }
            else
            {
                newBytes = currentRaw;
            }

            remap[oldOffset] = (uint)newBlob.Length;
            newBlob.Write(newBytes);
            newBlob.WriteByte(0);
        }

        // Apply the Phase B remap to every non-field-targeted column, then write-at-offset for any
        // remaining empty column. Columns are visited in fixed-data offset order (deterministic).
        var appliedEmptyWrites = 0;
        foreach (var columnOffset in stringColumnOffsets)
        {
            if (columnOffset + 4 > fixedData.Length || fieldTargetedOffsets.Contains(columnOffset))
            {
                continue;
            }

            var oldOffset = BinaryPrimitives.ReadUInt32BigEndian(fixedData.AsSpan(columnOffset, 4));
            if (remap.TryGetValue(oldOffset, out var mapped))
            {
                BinaryPrimitives.WriteUInt32BigEndian(fixedData.AsSpan(columnOffset, 4), mapped);
            }

            var currentRaw = ReadNulTerminatedBytes(stringArea, (int)oldOffset);
            if (currentRaw.Length == 0 && emptyWrites.Count > 0)
            {
                var write = emptyWrites.Dequeue();
                var freshOffset = (uint)newBlob.Length;
                newBlob.Write(Encoding.UTF8.GetBytes(write.Target));
                newBlob.WriteByte(0);
                BinaryPrimitives.WriteUInt32BigEndian(fixedData.AsSpan(columnOffset, 4), freshOffset);
                appliedEmptyWrites++;
            }
        }

        var rowApplied = appliedFieldTargets + appliedEmptyWrites;
        foreach (var replacement in contentMatched)
        {
            if (matchedSources.Contains(replacement.Source))
            {
                rowApplied++;
            }
            else
            {
                missed.Add(new MissedReplacement(rowId, replacement.Source,
                    rejectedSources.GetValueOrDefault(replacement.Source)
                        ?? "source string not present in row string columns"));
            }
        }

        // Any empty-source replacements left unconsumed had no empty string column to write
        // into; report them as missed rather than silently dropping them.
        foreach (var leftover in emptyWrites)
        {
            missed.Add(new MissedReplacement(rowId, leftover.Source,
                "no empty string column available for write-at-offset"));
        }

        return (fixedData, newBlob.ToArray(), rowApplied);
    }

    /// <summary>Miss reason reported when the post-patch run-aware verification rejects a flat-path patch.</summary>
    public const string StaleRunLengthMissReason =
        "patched bytes failed run-aware verification (stale 0xFF run-length prefix risk); row reverted to vanilla";

    // Phase A wrapper: returns the patched bytes when the run-aware verification accepts them,
    // null (→ precise miss) otherwise, recording the stale-run-length reason for the miss report.
    private static byte[]? GuardRunIntegrity(
        byte[] vanilla,
        byte[] patched,
        StringReplacement rep,
        Dictionary<string, string> rejectedSources)
    {
        if (RunPatchIsCoherent(vanilla, patched))
        {
            return patched;
        }

        rejectedSources.TryAdd(rep.Source, StaleRunLengthMissReason);
        return null;
    }

    // Defense in depth for the stale run-length crash class (Duty Finder, Addon row 2513): the
    // flat SeStringParser paths replay opaque payload bytes verbatim, INCLUDING any embedded 0xFF
    // run-length prefix, so a literal change can desync the client's length-prefixed reader. After
    // patching a 0xFF-bearing string via a flat path, re-parse the patched bytes with the
    // run-aware tree and require that the result is structurally identical to the vanilla parse
    // (same payload tokens, order, run brackets and control bytes; only literals may differ) —
    // i.e. every run's declared length still delimits exactly its body. Fails safe: any parse
    // error or divergence rejects the patch and the row ships its vanilla bytes as a miss.
    private static bool RunPatchIsCoherent(byte[] vanilla, byte[] patched)
    {
        if (Array.IndexOf(vanilla, (byte)0xFF) < 0 || patched.AsSpan().SequenceEqual(vanilla))
        {
            return true;
        }

        try
        {
            var vanillaTokens = SeStringTreeTokenizer.TokenizeRawText(vanilla);
            var patchedTokens = SeStringTreeTokenizer.TokenizeRawText(patched);
            return SeStringCompatibilityValidator.Validate(vanillaTokens, patchedTokens).IsCompatible;
        }
        catch
        {
            return false;
        }
    }

    // Serialize() appends a NUL terminator; the blob writer adds its own, so strip the trailing
    // NUL here to avoid a double terminator (which would leave a stray empty byte in the blob).
    private static byte[] TrimTerminator(byte[] bytes)
        => bytes.Length > 0 && bytes[^1] == 0 ? bytes[..^1] : bytes;

    // Shared raw NUL-terminated read: see ExdPage.ReadNulTerminatedBytes for the rationale.
    private static byte[] ReadNulTerminatedBytes(ReadOnlySpan<byte> area, int start)
        => ExdPage.ReadNulTerminatedBytes(area, start);
}

using System.Text;

namespace XivSpanish.GameData;

/// <summary>
/// Represents a segment in a SeString: either a literal text or an opaque binary payload.
/// </summary>
public abstract record SeStringSegment
{
    /// <summary>A segment containing literal UTF-8 text (no control bytes).</summary>
    public sealed record Literal(string Text) : SeStringSegment;

    /// <summary>A segment containing opaque binary payload (0x02 ... 0x03).</summary>
    public sealed record Payload(byte[] Bytes) : SeStringSegment;
}

/// <summary>
/// Parses and serializes FFXIV SeString format: sequences of literal text and binary payloads.
/// 
/// SeString structure:
/// - Literal segments: UTF-8 text (0x00-0x7F, multi-byte UTF-8).
/// - Payload segments: 0x02 (START_BYTE) ... 0x03 (END_BYTE) with arbitrary bytes in between.
/// - Terminator: 0x00 (NUL).
/// 
/// Invariant: Payload bytes are opaque and never modified. Only Literal segments are translated.
/// </summary>
public static class SeStringParser
{
    private const byte StartByte = 0x02;
    private const byte EndByte = 0x03;
    private const byte NulTerminator = 0x00;
    private const byte StringRunByte = 0xFF;

    /// <summary>
    /// Parses a NUL-terminated byte sequence into a list of SeString segments.
    /// 
    /// Strategy:
    /// 1. When we see 0x02 (START_BYTE), read until 0x03 (END_BYTE) as a Payload.
    /// 2. Otherwise, collect consecutive bytes that form valid UTF-8 as a Literal.
    /// 3. Any byte that breaks UTF-8 validity is treated as a separate Payload.
    /// </summary>
    /// <param name="bytes">Raw bytes (may contain payloads with non-UTF8 bytes).</param>
    /// <returns>List of Literal and Payload segments.</returns>
    public static List<SeStringSegment> Parse(byte[] bytes) => ParseCore(bytes, opaqueRuns: true);

    /// <summary>
    /// Legacy flat parse used ONLY by run-aware corpus migration: it does NOT treat a 0xFF run as a
    /// single opaque payload, so it reproduces the original tokenization the corpus sources were
    /// generated with (run prefix bytes flow through as literal/raw bytes). Do not use for patching.
    /// </summary>
    public static List<SeStringSegment> ParseLegacy(byte[] bytes) => ParseCore(bytes, opaqueRuns: false);

    private static List<SeStringSegment> ParseCore(byte[] bytes, bool opaqueRuns)
    {
        var segments = new List<SeStringSegment>();
        var i = 0;

        while (i < bytes.Length)
        {
            if (bytes[i] == StartByte)
            {
                // Read payload: 0x02 ... 0x03
                var payloadStart = i;
                i++;

                // Scan for EndByte; payloads can contain arbitrary bytes.
                while (i < bytes.Length && bytes[i] != EndByte)
                {
                    i++;
                }

                if (i < bytes.Length)
                {
                    i++; // Include the EndByte
                }

                var payloadBytes = bytes[payloadStart..i];
                segments.Add(new SeStringSegment.Payload(payloadBytes));
            }
            else if (opaqueRuns && bytes[i] == StringRunByte)
            {
                // Length-prefixed string run: 0xFF <int-length> <length bytes>. The run body carries
                // nested case strings (plural/Switch tables: "one".."ten", "player"/"players", role
                // names) whose lengths live in the prefix. Translating any literal inside a run would
                // change its byte length WITHOUT updating the prefix, desyncing the client SeString
                // reader and crashing the panel that renders it (observed: Duty Finder). The whole
                // run is therefore captured as ONE opaque Payload and never modified, so run-bearing
                // rows fall back to the vanilla (English) text instead of a corrupt translation.
                var runStart = i;
                if (TryReadRunLength(bytes, i + 1, out var runLength, out var bodyStart)
                    && (long)bodyStart + runLength <= bytes.Length)
                {
                    i = bodyStart + runLength;
                }
                else
                {
                    i = runStart + 1;
                }

                segments.Add(new SeStringSegment.Payload(bytes[runStart..i]));
            }
            else if (bytes[i] == NulTerminator)
            {
                // End of string
                break;
            }
            else
            {
                // Try to read valid UTF-8 bytes
                var literalBytes = new List<byte>();

                while (i < bytes.Length && bytes[i] != StartByte && bytes[i] != NulTerminator)
                {
                    var b = bytes[i];

                    // Check if this is a valid UTF-8 start byte or continuation
                    if (b <= 0x7F)
                    {
                        // ASCII: valid
                        literalBytes.Add(b);
                        i++;
                    }
                    else if ((b & 0xC0) == 0x80)
                    {
                        // Continuation byte (0x80-0xBF): invalid as start, break
                        break;
                    }
                    else if ((b & 0xE0) == 0xC0)
                    {
                        // 2-byte sequence (0xC0-0xDF)
                        if (i + 1 < bytes.Length && (bytes[i + 1] & 0xC0) == 0x80)
                        {
                            literalBytes.Add(b);
                            literalBytes.Add(bytes[i + 1]);
                            i += 2;
                        }
                        else
                        {
                            // Invalid continuation: break
                            break;
                        }
                    }
                    else if ((b & 0xF0) == 0xE0)
                    {
                        // 3-byte sequence (0xE0-0xEF)
                        if (i + 2 < bytes.Length
                            && (bytes[i + 1] & 0xC0) == 0x80
                            && (bytes[i + 2] & 0xC0) == 0x80)
                        {
                            literalBytes.Add(b);
                            literalBytes.Add(bytes[i + 1]);
                            literalBytes.Add(bytes[i + 2]);
                            i += 3;
                        }
                        else
                        {
                            // Invalid continuation: break
                            break;
                        }
                    }
                    else if ((b & 0xF8) == 0xF0)
                    {
                        // 4-byte sequence (0xF0-0xF7)
                        if (i + 3 < bytes.Length
                            && (bytes[i + 1] & 0xC0) == 0x80
                            && (bytes[i + 2] & 0xC0) == 0x80
                            && (bytes[i + 3] & 0xC0) == 0x80)
                        {
                            literalBytes.Add(b);
                            literalBytes.Add(bytes[i + 1]);
                            literalBytes.Add(bytes[i + 2]);
                            literalBytes.Add(bytes[i + 3]);
                            i += 4;
                        }
                        else
                        {
                            // Invalid continuation: break
                            break;
                        }
                    }
                    else
                    {
                        // Invalid UTF-8 byte (0xF8-0xFF): break
                        break;
                    }
                }

                // If we collected any literal bytes, add them as a Literal segment
                if (literalBytes.Count > 0)
                {
                    var text = Encoding.UTF8.GetString(literalBytes.ToArray());
                    segments.Add(new SeStringSegment.Literal(text));
                }

                // If we stopped at a non-UTF8 byte (not 0x02 or 0x00), treat it as opaque. In opaque-
                // run mode 0xFF is handled by the run branch above so it is excluded here; in legacy
                // mode 0xFF flows through as a single opaque byte (original tokenization).
                if (i < bytes.Length && bytes[i] != StartByte && bytes[i] != NulTerminator
                    && (!opaqueRuns || bytes[i] != StringRunByte))
                {
                    // Single non-UTF8 byte: treat as opaque payload
                    segments.Add(new SeStringSegment.Payload([bytes[i]]));
                    i++;
                }
            }
        }

        return segments;
    }

    /// <summary>
    /// Decodes the SeString integer that prefixes a 0xFF string run, giving the run body length in
    /// bytes and the index where the body starts. Encoding (matches the client/Lumina):
    /// <list type="bullet">
    ///   <item>A byte <c>0x01..0xEF</c> is an inline value: length = <c>byte - 1</c>.</item>
    ///   <item><c>0xF0..0xFE</c> is a marker whose low nibble (of <c>marker + 1</c>) is a bitmask of
    ///   which of up to four big-endian value bytes follow (bit3→byte&lt;&lt;24 … bit0→byte).</item>
    /// </list>
    /// Returns false on a truncated/!invalid prefix so the caller can fail safe.
    /// </summary>
    internal static bool TryReadRunLength(byte[] bytes, int index, out int length, out int bodyStart)
    {
        length = 0;
        bodyStart = index;
        if (index >= bytes.Length)
        {
            return false;
        }

        var marker = bytes[index];
        if (marker is >= 0x01 and <= 0xEF)
        {
            length = marker - 1;
            bodyStart = index + 1;
            return true;
        }

        if (marker is >= 0xF0 and <= 0xFE)
        {
            var mask = marker + 1;
            var j = index + 1;
            var value = 0;
            if ((mask & 0b1000) != 0)
            {
                if (j >= bytes.Length) { return false; }
                value |= bytes[j++] << 24;
            }

            if ((mask & 0b0100) != 0)
            {
                if (j >= bytes.Length) { return false; }
                value |= bytes[j++] << 16;
            }

            if ((mask & 0b0010) != 0)
            {
                if (j >= bytes.Length) { return false; }
                value |= bytes[j++] << 8;
            }

            if ((mask & 0b0001) != 0)
            {
                if (j >= bytes.Length) { return false; }
                value |= bytes[j++];
            }

            length = value;
            bodyStart = j;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Serializes a list of segments back to a NUL-terminated byte sequence.
    /// </summary>
    /// <param name="segments">List of Literal and Payload segments.</param>
    /// <returns>Byte array with NUL terminator.</returns>
    public static byte[] Serialize(List<SeStringSegment> segments)
    {
        using var stream = new MemoryStream();

        foreach (var segment in segments)
        {
            switch (segment)
            {
                case SeStringSegment.Literal lit:
                    stream.Write(Encoding.UTF8.GetBytes(lit.Text));
                    break;

                case SeStringSegment.Payload payload:
                    stream.Write(payload.Bytes);
                    break;
            }
        }

        stream.WriteByte(NulTerminator);
        return stream.ToArray();
    }

    /// <summary>
    /// Attempts to replace a source string with a target string in the literal segments.
    /// 
    /// Strategy:
    /// - Concatenate all Literal segments to form the full text.
    /// - Search for source in the full text.
    /// - If found and the source is contained within a single Literal segment, replace it.
    /// - Otherwise, report failure (source spans multiple segments or not found).
    /// 
    /// Returns the modified segments if successful, or the original segments if no match.
    /// </summary>
    /// <param name="segments">Original segments.</param>
    /// <param name="source">Source string to find (plain text, no payloads).</param>
    /// <param name="target">Target string to replace with.</param>
    /// <param name="result">Output: modified segments (or original if no match).</param>
    /// <param name="reason">Output: reason for failure (null if successful).</param>
    /// <returns>True if replacement was applied, false otherwise.</returns>
    public static bool TryReplace(
        List<SeStringSegment> segments,
        string source,
        string target,
        out List<SeStringSegment> result,
        out string? reason)
    {
        result = segments;
        reason = null;

        if (string.IsNullOrEmpty(source))
        {
            reason = "source is empty";
            return false;
        }

        // Target-authored standard macros require the run-aware tree so their binary framing and
        // length-prefixed branches are synthesized structurally. Never write their authoring tags
        // as visible literal text through the flat replacement path.
        if (SeStringStandardMacros.HasReservedDelimiter(target))
        {
            reason = "target contains standard macros requiring run-aware translation";
            return false;
        }

        var sourceTokens = SeStringTokenizer.FindTokenReferences(source);
        var targetTokens = SeStringTokenizer.FindTokenReferences(target);
        if (!TokenReferencesEqual(sourceTokens, targetTokens))
        {
            reason = "target token references do not match source";
            return false;
        }

        var sourceControls = SeStringTokenizer.FindUnsafeControlReferences(source);
        var targetControls = SeStringTokenizer.FindUnsafeControlReferences(target);
        if (!ControlReferencesEqual(sourceControls, targetControls))
        {
            reason = "target control references do not match source";
            return false;
        }

        // Find which Literal segment contains the source.
        var matchingSegmentIndex = -1;
        var matchingLiteralIndex = 0;
        var literalCount = 0;

        for (var i = 0; i < segments.Count; i++)
        {
            if (segments[i] is SeStringSegment.Literal lit)
            {
                if (lit.Text.Contains(source, StringComparison.Ordinal))
                {
                    matchingSegmentIndex = i;
                    matchingLiteralIndex = literalCount;
                }

                literalCount++;
            }
        }

        if (matchingSegmentIndex == -1)
        {
            // The source is not contained within any single Literal segment.
            // This can happen when the extractor turned a SeString payload into
            // a newline in the manifest, so the source spans multiple Literal
            // segments separated by Payloads. We do NOT attempt to replace
            // across multiple literals: reassembling the segments while keeping
            // payloads in their original positions is error-prone and can produce
            // malformed SeStrings that crash the client (observed in Gold Saucer
            // UI). Return false so ExdPatcher falls back to copying the raw bytes.
            reason = "source spans multiple literal segments";
            return false;
        }

        // Check if source spans multiple literal segments.
        // Simple heuristic: if the source is found in a single literal, it's safe to replace.
        // If it would require concatenating multiple literals, reject (conservative approach).
        var matchingLit = (SeStringSegment.Literal)segments[matchingSegmentIndex];
        if (!matchingLit.Text.Contains(source, StringComparison.Ordinal))
        {
            reason = "source not found in the matching literal segment (internal error)";
            return false;
        }

        // Replace in the matching segment.
        var newText = matchingLit.Text.Replace(source, target, StringComparison.Ordinal);
        var newSegments = new List<SeStringSegment>(segments);
        newSegments[matchingSegmentIndex] = new SeStringSegment.Literal(newText);

        result = newSegments;
        return true;
    }

    private static bool TokenReferencesEqual(IReadOnlyList<string> sourceTokens, IReadOnlyList<string> targetTokens)
    {
        if (sourceTokens.Count != targetTokens.Count)
        {
            return false;
        }

        return sourceTokens
            .OrderBy(token => token, StringComparer.Ordinal)
            .SequenceEqual(targetTokens.OrderBy(token => token, StringComparer.Ordinal), StringComparer.Ordinal);
    }

    private static bool ControlReferencesEqual(IReadOnlyList<char> sourceControls, IReadOnlyList<char> targetControls)
        => sourceControls.SequenceEqual(targetControls);

    /// <summary>
    /// Token-aware replacement for sources that span payloads (the common "Missed" case).
    ///
    /// The row's current segments are tokenized into a "baliza" text form (payloads → tokens
    /// like <c>$Item</c>, <c>$Num</c>). When the manifest <paramref name="source"/> equals that
    /// tokenized form, the <paramref name="target"/> — which must carry the same tokens — is
    /// detokenized by reinserting the row's ORIGINAL payload bytes in token order, so macro
    /// bytes are preserved byte-for-byte and are never regenerated.
    ///
    /// Fails safe (returns false, leaves <paramref name="result"/> = input) when the tokenized
    /// source does not match, or when the target drops/duplicates/adds tokens ambiguously.
    /// </summary>
    public static bool TryReplaceTokenized(
        List<SeStringSegment> segments,
        string source,
        string target,
        out List<SeStringSegment> result,
        out string? reason)
    {
        result = segments;
        reason = null;

        if (string.IsNullOrEmpty(source))
        {
            reason = "source is empty";
            return false;
        }

        if (SeStringStandardMacros.HasReservedDelimiter(target))
        {
            reason = "target contains standard macros requiring run-aware translation";
            return false;
        }

        var tokenized = SeStringTokenizer.Tokenize(segments);

        // Only the whole-string case is handled here: the source must equal the row's full
        // tokenized text. Partial token-spanning replacement would require re-tokenizing a
        // substring and is ambiguous when the same token name repeats, so we keep it strict.
        if (!string.Equals(tokenized.Text, source, StringComparison.Ordinal))
        {
            reason = "tokenized source does not match the row's tokenized string";
            return false;
        }

        var sourceControls = SeStringTokenizer.FindUnsafeControlReferences(source);
        var targetControls = SeStringTokenizer.FindUnsafeControlReferences(target);
        if (!ControlReferencesEqual(sourceControls, targetControls))
        {
            reason = "target control references do not match source";
            return false;
        }

        if (!SeStringTokenizer.TryDetokenize(target, tokenized.Tokens, out var rebuilt, out var detokenReason))
        {
            reason = detokenReason ?? "could not detokenize target";
            return false;
        }

        result = rebuilt;
        return true;
    }
}

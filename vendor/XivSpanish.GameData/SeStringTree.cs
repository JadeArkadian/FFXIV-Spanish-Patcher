using System.Text;

namespace XivSpanish.GameData;

/// <summary>
/// A node in a <b>run-aware</b> SeString tree. Unlike the flat <see cref="SeStringParser"/> model
/// (which treats a length-prefixed <c>0xFF</c> run as one opaque blob), this tree recurses into run
/// bodies so the literal text inside a run can be translated and the run's length prefix is
/// recomputed on serialize. Macros (<c>0x02 … 0x03</c>) stay opaque.
/// </summary>
public abstract record SeNode
{
    /// <summary>Literal UTF-8 text (may contain control bytes &lt; 0x20 that are not 0x02/0x03/0xFF).</summary>
    public sealed record Literal(string Text) : SeNode;

    /// <summary>
    /// A macro chunk <c>0x02 &lt;code&gt; &lt;packint-length&gt; &lt;body&gt; 0x03</c>. The body is
    /// parsed run-aware (it may embed <c>0xFF</c> runs and nested macros that carry translatable
    /// text — e.g. the <c>&lt;If&gt;</c> branches in Duty Finder rows), and the chunk length prefix is
    /// recomputed from the real serialized body on serialize. <see cref="Code"/> is the macro opcode
    /// byte; <see cref="LengthMarker"/> records the original length-prefix form so an unchanged macro
    /// re-serializes byte-identically.
    /// </summary>
    public sealed record Macro(byte Code, IReadOnlyList<SeNode> Children, byte LengthMarker) : SeNode;

    /// <summary>
    /// A malformed/edge macro start (<c>0x02</c>) whose body could not be delimited by a packint
    /// length prefix terminated by <c>0x03</c>. Captured verbatim (including framing) so the tree
    /// round-trips, but never descended into. Rare; preserves the historical opaque behavior for the
    /// few rows the structured parse cannot model.
    /// </summary>
    public sealed record OpaqueMacro(byte[] Bytes) : SeNode;

    /// <summary>A single opaque byte (e.g. the <c>0xFF</c> edge marker that is not a decodable run prefix).</summary>
    public sealed record RawByte(byte Value) : SeNode;

    /// <summary>
    /// A length-prefixed string run. <see cref="Children"/> is the parsed body; <see cref="MarkerByte"/>
    /// records the original length-prefix encoding form so an unchanged run re-serializes byte-identically.
    /// </summary>
    public sealed record Run(IReadOnlyList<SeNode> Children, byte MarkerByte) : SeNode;
}

/// <summary>
/// Parses and serializes the run-aware SeString tree (<see cref="SeNode"/>). Serialization recomputes
/// every run length prefix from the actual body bytes, so translating literal text inside a run no
/// longer desyncs the client reader (the Duty Finder crash class).
/// </summary>
public static class SeStringTree
{
    private const byte StartByte = 0x02;
    private const byte EndByte = 0x03;
    private const byte NulTerminator = 0x00;
    private const byte StringRunByte = 0xFF;

    /// <summary>Parses NUL-terminated bytes into a run-aware tree (NUL terminator is dropped).</summary>
    public static List<SeNode> Parse(byte[] bytes)
    {
        var end = Array.IndexOf(bytes, NulTerminator);
        if (end < 0)
        {
            end = bytes.Length;
        }

        var pos = 0;
        return ParseRange(bytes, ref pos, end);
    }

    /// <summary>Serializes a tree back to bytes WITH a trailing NUL, recomputing run lengths.</summary>
    public static byte[] Serialize(IReadOnlyList<SeNode> nodes)
    {
        using var stream = new MemoryStream();
        SerializeInto(nodes, stream);
        stream.WriteByte(NulTerminator);
        return stream.ToArray();
    }

    /// <summary>
    /// Run-aware whole-string replacement for the packager. Tokenizes the row's vanilla bytes with
    /// the run-aware tokenizer; if the result equals <paramref name="source"/>, detokenizes
    /// <paramref name="target"/> against the same token map and serializes — recomputing every run
    /// length, so translating text inside a run is structurally safe (no client desync) by
    /// construction. Returns the bytes WITHOUT the trailing NUL (the blob writer adds its own).
    /// Fails safe (false) when the tokenized source does not match or the target cannot be rebuilt.
    /// </summary>
    public static bool TryTranslate(byte[] vanilla, string source, string target, out byte[] result, out string? reason)
    {
        result = vanilla;
        reason = null;

        var tree = Parse(vanilla);
        var tok = SeStringTreeTokenizer.Tokenize(tree);
        if (!string.Equals(tok.Text, source, StringComparison.Ordinal))
        {
            reason = "tokenized source does not match the row's run-aware tokenized string";
            return false;
        }

        if (!SeStringTreeTokenizer.TryDetokenize(target, tok.Tokens, out var rebuilt, out var detokenReason))
        {
            reason = detokenReason ?? "could not detokenize target";
            return false;
        }

        var bytes = Serialize(rebuilt);
        // Drop the trailing NUL that Serialize appends; the EXD blob writer adds its own.
        result = bytes.Length > 0 && bytes[^1] == NulTerminator ? bytes[..^1] : bytes;
        return true;
    }

    /// <summary>Serializes a single node to its bytes (no trailing NUL). Used by the tokenizer to
    /// capture a leaf macro as one opaque token that round-trips byte-identically.</summary>
    public static byte[] SerializeNode(SeNode node)
    {
        using var stream = new MemoryStream();
        SerializeInto([node], stream);
        return stream.ToArray();
    }

    // Parses nodes from bytes[pos..end), advancing pos. Used recursively for run bodies.
    private static List<SeNode> ParseRange(byte[] bytes, ref int pos, int end)
    {
        var nodes = new List<SeNode>();
        while (pos < end)
        {
            var b = bytes[pos];
            if (b == StartByte)
            {
                // Macro chunk: 0x02 <code> <packint-length> <body...> 0x03. Delimit the body by the
                // packint length (NOT by scanning to the first 0x03 — the length byte itself or a body
                // expression byte can legitimately be 0x03), then recurse into the body so embedded
                // runs/nested macros become translatable nodes. The chunk length is recomputed on
                // serialize, so translating text inside a macro body stays self-consistent.
                if (pos + 1 < end
                    && TryReadRunLength(bytes, pos + 2, out var bodyLen, out var bodyStart, out var lenMarker)
                    && (long)bodyStart + bodyLen < end
                    && bytes[bodyStart + bodyLen] == EndByte)
                {
                    var code = bytes[pos + 1];
                    var bodyEnd = bodyStart + bodyLen;
                    var bodyCursor = bodyStart;
                    var children = ParseRange(bytes, ref bodyCursor, bodyEnd);
                    pos = bodyEnd + 1; // skip the trailing 0x03
                    nodes.Add(new SeNode.Macro(code, children, lenMarker));
                }
                else
                {
                    // Not a delimitable macro chunk: this 0x02 is a macro EXPRESSION byte (the packint
                    // integer encoding reuses 0x01..0xEF inline values, so an If/Switch condition can
                    // legitimately contain 0x02 — e.g. `e1 e8 03 02` in Addon row 2513). Emit it as a
                    // single opaque RawByte and keep parsing, so the 0xFF runs that follow are modeled
                    // structurally instead of being swallowed by an opaque scan-to-0x03 (which truncated
                    // the macro and left its stale run-length prefix replayed verbatim — the Duty
                    // Finder crash class).
                    nodes.Add(new SeNode.RawByte(b));
                    pos++;
                }
            }
            else if (b == StringRunByte)
            {
                if (TryReadRunLength(bytes, pos + 1, out var length, out var bodyStart, out var marker)
                    && (long)bodyStart + length <= end)
                {
                    var bodyEnd = bodyStart + length;
                    var bodyCursor = bodyStart;
                    var children = ParseRange(bytes, ref bodyCursor, bodyEnd);
                    // ParseRange consumes exactly [bodyStart, bodyEnd); a misalignment would only
                    // happen on malformed data, in which case the run still spans [bodyStart,bodyEnd).
                    pos = bodyEnd;
                    nodes.Add(new SeNode.Run(children, marker));
                }
                else
                {
                    // Edge: 0xFF that is not a decodable length prefix (nested-marker form, 2 known
                    // Addon rows). Keep it as one opaque byte so the tree still round-trips.
                    nodes.Add(new SeNode.RawByte(b));
                    pos++;
                }
            }
            else
            {
                var literalBytes = ReadLiteral(bytes, ref pos, end);
                if (literalBytes.Count > 0)
                {
                    nodes.Add(new SeNode.Literal(Encoding.UTF8.GetString(literalBytes.ToArray())));
                }
                else if (pos < end)
                {
                    // A lone non-UTF8 byte that is not 0x02/0xFF: keep it opaque so it round-trips.
                    nodes.Add(new SeNode.RawByte(bytes[pos]));
                    pos++;
                }
            }
        }

        return nodes;
    }

    // Reads a maximal UTF-8 literal run from bytes[pos..end), stopping at 0x02/0xFF or an invalid
    // UTF-8 byte. Mirrors SeStringParser's UTF-8 handling so literal segmentation is identical.
    private static List<byte> ReadLiteral(byte[] bytes, ref int pos, int end)
    {
        var literal = new List<byte>();
        while (pos < end)
        {
            var b = bytes[pos];
            if (b == StartByte || b == StringRunByte)
            {
                break;
            }

            if (b <= 0x7F)
            {
                literal.Add(b);
                pos++;
            }
            else if ((b & 0xC0) == 0x80)
            {
                break; // stray continuation byte
            }
            else if ((b & 0xE0) == 0xC0)
            {
                if (pos + 1 < end && (bytes[pos + 1] & 0xC0) == 0x80)
                {
                    literal.Add(b);
                    literal.Add(bytes[pos + 1]);
                    pos += 2;
                }
                else { break; }
            }
            else if ((b & 0xF0) == 0xE0)
            {
                if (pos + 2 < end && (bytes[pos + 1] & 0xC0) == 0x80 && (bytes[pos + 2] & 0xC0) == 0x80)
                {
                    literal.Add(b);
                    literal.Add(bytes[pos + 1]);
                    literal.Add(bytes[pos + 2]);
                    pos += 3;
                }
                else { break; }
            }
            else if ((b & 0xF8) == 0xF0)
            {
                if (pos + 3 < end
                    && (bytes[pos + 1] & 0xC0) == 0x80
                    && (bytes[pos + 2] & 0xC0) == 0x80
                    && (bytes[pos + 3] & 0xC0) == 0x80)
                {
                    literal.Add(b);
                    literal.Add(bytes[pos + 1]);
                    literal.Add(bytes[pos + 2]);
                    literal.Add(bytes[pos + 3]);
                    pos += 4;
                }
                else { break; }
            }
            else
            {
                break; // invalid 0xF8-0xFE byte
            }
        }

        return literal;
    }

    private static void SerializeInto(IReadOnlyList<SeNode> nodes, MemoryStream stream)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case SeNode.Literal lit:
                    stream.Write(Encoding.UTF8.GetBytes(lit.Text));
                    break;
                case SeNode.Macro mac:
                    using (var macBody = new MemoryStream())
                    {
                        SerializeInto(mac.Children, macBody);
                        var bodyBytes = macBody.ToArray();
                        stream.WriteByte(StartByte);
                        stream.WriteByte(mac.Code);
                        // Recompute the chunk length from the actual (possibly translated) body, then
                        // write body + the 0x03 terminator. This is the core crash fix: a translated
                        // body changes its byte length and the prefix follows, so the 0x03 always lands
                        // where the client's reader expects it (no chunk desync).
                        stream.Write(EncodeRunLength(bodyBytes.Length, mac.LengthMarker));
                        stream.Write(bodyBytes);
                        stream.WriteByte(EndByte);
                    }

                    break;
                case SeNode.OpaqueMacro opaque:
                    stream.Write(opaque.Bytes);
                    break;
                case SeNode.RawByte raw:
                    stream.WriteByte(raw.Value);
                    break;
                case SeNode.Run run:
                    using (var body = new MemoryStream())
                    {
                        SerializeInto(run.Children, body);
                        var bodyBytes = body.ToArray();
                        stream.WriteByte(StringRunByte);
                        var prefix = EncodeRunLength(bodyBytes.Length, run.MarkerByte);
                        stream.Write(prefix);
                        stream.Write(bodyBytes);
                    }

                    break;
            }
        }
    }

    /// <summary>
    /// Decodes the run length prefix. <c>0x01..0xEF</c> is inline (value = byte − 1); <c>0xF0..0xFE</c>
    /// is a marker whose low nibble (of marker+1) is a bitmask of big-endian value bytes that follow.
    /// </summary>
    internal static bool TryReadRunLength(byte[] bytes, int index, out int length, out int bodyStart, out byte marker)
    {
        length = 0;
        bodyStart = index;
        marker = 0;
        if (index >= bytes.Length)
        {
            return false;
        }

        marker = bytes[index];
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
            if ((mask & 0b1000) != 0) { if (j >= bytes.Length) { return false; } value |= bytes[j++] << 24; }
            if ((mask & 0b0100) != 0) { if (j >= bytes.Length) { return false; } value |= bytes[j++] << 16; }
            if ((mask & 0b0010) != 0) { if (j >= bytes.Length) { return false; } value |= bytes[j++] << 8; }
            if ((mask & 0b0001) != 0) { if (j >= bytes.Length) { return false; } value |= bytes[j++]; }
            length = value;
            bodyStart = j;
            return true;
        }

        return false;
    }

    // Re-encodes a run length, preserving the original marker form when the value still fits (so an
    // unchanged run round-trips byte-identically), widening to the smallest contiguous-low marker
    // (0xF0/0xF2/0xF6/0xFE) otherwise.
    private static byte[] EncodeRunLength(int value, byte originalMarker)
    {
        if (originalMarker is >= 0x01 and <= 0xEF)
        {
            if (value is >= 0 and <= 0xEE)
            {
                return [(byte)(value + 1)];
            }
        }
        else if (originalMarker is >= 0xF0 and <= 0xFE && FitsMarker(value, originalMarker))
        {
            return EmitMarker(value, originalMarker);
        }

        if (value <= 0xEE) { return [(byte)(value + 1)]; }
        if (value <= 0xFF) { return EmitMarker(value, 0xF0); }
        if (value <= 0xFFFF) { return EmitMarker(value, 0xF2); }
        if (value <= 0xFFFFFF) { return EmitMarker(value, 0xF6); }
        return EmitMarker(value, 0xFE);
    }

    private static bool FitsMarker(int value, byte marker)
    {
        var count = ByteCount(marker);
        return count >= 4 || (uint)value < (1u << (8 * count));
    }

    private static int ByteCount(byte marker)
    {
        var mask = marker + 1;
        return ((mask & 0b1000) >> 3) + ((mask & 0b0100) >> 2) + ((mask & 0b0010) >> 1) + (mask & 0b0001);
    }

    private static byte[] EmitMarker(int value, byte marker)
    {
        var mask = marker + 1;
        var bytes = new List<byte> { marker };
        if ((mask & 0b1000) != 0) { bytes.Add((byte)(value >> 24)); }
        if ((mask & 0b0100) != 0) { bytes.Add((byte)(value >> 16)); }
        if ((mask & 0b0010) != 0) { bytes.Add((byte)(value >> 8)); }
        if ((mask & 0b0001) != 0) { bytes.Add((byte)value); }
        return bytes.ToArray();
    }
}

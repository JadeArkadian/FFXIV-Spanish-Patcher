using System.Text;

namespace XivSpanish.GameData;

/// <summary>
/// Result of tokenizing a SeString: a reversible text form where each binary payload
/// has been replaced by a stable, human-readable token (a "baliza", e.g. <c>$Num</c>),
/// plus the ordered mapping from each token occurrence back to its original payload bytes.
/// </summary>
/// <param name="Text">
/// The tokenized text: literal segments verbatim, payloads rendered as <c>&lt;Name&gt;</c>
/// (or <c>&lt;Name#2&gt;</c>, <c>&lt;Name#3&gt;</c>… when the same token name repeats in this string).
/// </param>
/// <param name="Tokens">
/// The tokens in left-to-right order. <see cref="TokenOccurrence.Token"/> matches the
/// exact string that appears in <see cref="Text"/>; <see cref="TokenOccurrence.Bytes"/>
/// holds the original payload bytes to reinsert.
/// </param>
public sealed record TokenizedSeString(string Text, IReadOnlyList<TokenOccurrence> Tokens);

/// <summary>One payload occurrence: its rendered token and the original bytes it stands for.</summary>
public sealed record TokenOccurrence(string Token, byte[] Bytes);

/// <summary>
/// Converts parsed <see cref="SeStringSegment"/> lists into a reversible tokenized text form
/// and back. Each <see cref="SeStringSegment.Payload"/> becomes a friendly token (baliza) so
/// that manifest <c>source</c>/<c>target</c> strings can carry payloads as plain text, while
/// the original payload bytes are preserved byte-for-byte on reinsertion.
///
/// The mapping is deterministic: the same payload bytes always render to the same token name,
/// and repeated names within one string are disambiguated with a <c>#N</c> suffix in order.
/// </summary>
public static class SeStringTokenizer
{
    // Friendly names per macro kind byte (the byte right after 0x02). Names mirror Lumina's
    // MacroCode enum for the kinds that actually occur in the data; unknown kinds fall back
    // to "<Payload{HEXKIND}>" so every payload is still representable and reversible.
    private static readonly Dictionary<byte, string> KindNames = new()
    {
        [0x10] = "NewLine",
        [0x20] = "Num",
        [0x08] = "If",
        [0x09] = "Switch",
        [0x28] = "Sheet",
        [0x29] = "String",
        [0x31] = "EnNoun",
        [0x32] = "Split",
        [0x2B] = "Head",
        [0x2D] = "HeadAll",
        [0x2F] = "Lower",
        [0x12] = "Icon",
        [0x1E] = "Icon2",
        [0x48] = "ColorType",
        [0x49] = "EdgeColorType",
        [0x13] = "Color",
        [0x14] = "EdgeColor",
        [0x1A] = "Italic",
        [0x1B] = "Edge",
        [0x1F] = "Hyphen",
        [0x1D] = "Nbsp",
        [0x24] = "Sec",
        [0x22] = "Kilo",
        [0x07] = "Time",
        [0x06] = "ResetTime",
        [0x2C] = "Split",
        [0x26] = "Float",
        [0x50] = "Digit",
        [0x51] = "Ordinal",
        [0x60] = "Sound",
        [0x61] = "LevelPos",
        [0x42] = "SwitchPlatform",
        [0x41] = "SheetSub",
        [0x2E] = "Fixed",
    };

    /// <summary>
    /// The set of friendly base token names this tokenizer can emit (the values of the kind map
    /// plus the <c>Raw</c> fallback). Exposed so migration tools can recognize a token name
    /// boundary in the legacy delimiter-less form. Does not include the <c>PayloadXX</c> hex
    /// fallback (those are recognized by pattern, not by membership).
    /// </summary>
    public static IReadOnlyCollection<string> KnownBaseNames { get; } =
        new HashSet<string>(KindNames.Values, StringComparer.Ordinal) { "Raw" };

    /// <summary>The token opening delimiter. Literal occurrences are escaped on output.</summary>
    public const char TokenOpen = '<';

    /// <summary>The token closing delimiter.</summary>
    public const char TokenClose = '>';

    /// <summary>
    /// Returns the stable base token name for a payload (without the surrounding
    /// <c>&lt;&gt;</c> delimiters or <c>#N</c> suffix). Derived solely from the macro kind byte,
    /// so it is identical across the extractor and patcher for the same payload.
    /// </summary>
    internal static string BaseName(byte[] payload)
    {
        // Payload form is 0x02 <kind> <len/args...> 0x03; a lone 0x02/0x03 or non-UTF8 byte
        // captured as a Payload has length 1 and no kind byte.
        if (payload.Length >= 2 && payload[0] == 0x02)
        {
            var kind = payload[1];
            if (KindNames.TryGetValue(kind, out var name))
            {
                return name;
            }

            return $"Payload{kind:X2}";
        }

        return "Raw";
    }

    /// <summary>
    /// Convenience: parses raw SeString bytes and returns their tokenized text. Equivalent to
    /// <c>Tokenize(SeStringParser.Parse(raw)).Text</c>. Used by the extractor to emit a
    /// payload-preserving source instead of the payload-stripped <c>ExtractText()</c> form.
    /// </summary>
    public static string TokenizeRawText(byte[] raw)
        => Tokenize(SeStringParser.Parse(raw)).Text;

    /// <summary>
    /// Tokenizes a parsed segment list into reversible text plus an ordered token map.
    /// Literal text is escaped so a literal '&lt;' cannot be confused with a token boundary
    /// (a literal '&lt;' becomes "&lt;&lt;"). Tokens are emitted as <c>&lt;Name&gt;</c>.
    /// </summary>
    public static TokenizedSeString Tokenize(IReadOnlyList<SeStringSegment> segments)
    {
        var sb = new StringBuilder();
        var tokens = new List<TokenOccurrence>();
        var nameCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var segment in segments)
        {
            switch (segment)
            {
                case SeStringSegment.Literal lit:
                    // Escape literal '<' so it never collides with a token delimiter. '>' needs no
                    // escaping (it is only consumed as part of a matched token).
                    sb.Append(lit.Text.Replace("<", "<<", StringComparison.Ordinal));
                    break;

                case SeStringSegment.Payload payload:
                    var baseName = BaseName(payload.Bytes);
                    var count = nameCounts.GetValueOrDefault(baseName) + 1;
                    nameCounts[baseName] = count;
                    var token = count == 1
                        ? $"{TokenOpen}{baseName}{TokenClose}"
                        : $"{TokenOpen}{baseName}#{count}{TokenClose}";
                    sb.Append(token);
                    tokens.Add(new TokenOccurrence(token, payload.Bytes));
                    break;
            }
        }

        return new TokenizedSeString(sb.ToString(), tokens);
    }

    /// <summary>
    /// Rebuilds a segment list from a tokenized <paramref name="text"/> and a token map.
    /// At each '&lt;' the text is matched against the known token strings (whose keys include the
    /// closing '&gt;', so the match is unambiguous); literal "&lt;&lt;" is un-escaped to "&lt;". Each
    /// token is replaced by the original payload bytes from the matching <see cref="TokenOccurrence"/>.
    ///
    /// Fails (returns false) when a referenced token has no mapping, when a token in the map
    /// is dropped or duplicated by the text, or when a stray '&lt;' remains — never
    /// emits a corrupt or ambiguous result.
    /// </summary>
    /// <param name="text">Tokenized text (e.g. a translated target carrying balizas).</param>
    /// <param name="tokens">The token map produced for the original string.</param>
    /// <param name="segments">Output: rebuilt literal/payload segments.</param>
    /// <param name="reason">Output: failure reason, null on success.</param>
    public static bool TryDetokenize(
        string text,
        IReadOnlyList<TokenOccurrence> tokens,
        out List<SeStringSegment> segments,
        out string? reason)
    {
        segments = new List<SeStringSegment>();
        reason = null;

        // Map token string -> bytes. A token string may legitimately appear once per unique
        // token (names are made unique with #N), so duplicates in the map are a contract break.
        var byToken = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var occurrence in tokens)
        {
            if (!byToken.TryAdd(occurrence.Token, occurrence.Bytes))
            {
                reason = $"duplicate token '{occurrence.Token}' in map";
                return false;
            }
        }

        var used = new HashSet<string>(StringComparer.Ordinal);
        var literal = new StringBuilder();
        var i = 0;

        while (i < text.Length)
        {
            var c = text[i];
            if (c != TokenOpen)
            {
                literal.Append(c);
                i++;
                continue;
            }

            // c == '<'. Either an escaped literal "<<" or the start of a token.
            if (i + 1 < text.Length && text[i + 1] == TokenOpen)
            {
                literal.Append(TokenOpen);
                i += 2;
                continue;
            }

            // Try to match the longest known token at this position.
            var matched = MatchTokenAt(text, i, byToken.Keys);
            if (matched is null)
            {
                reason = $"unmapped or stray '<' at index {i} in target";
                return false;
            }

            // Flush any pending literal run (un-escaping already handled above).
            if (literal.Length > 0)
            {
                segments.Add(new SeStringSegment.Literal(literal.ToString()));
                literal.Clear();
            }

            if (!used.Add(matched))
            {
                reason = $"token '{matched}' used more than once in target";
                return false;
            }

            segments.Add(new SeStringSegment.Payload(byToken[matched]));
            i += matched.Length;
        }

        if (literal.Length > 0)
        {
            segments.Add(new SeStringSegment.Literal(literal.ToString()));
        }

        // Every payload from the source must be accounted for; dropping one would silently
        // lose a macro (e.g. a player-name placeholder) and corrupt the displayed string.
        if (used.Count != byToken.Count)
        {
            reason = $"target uses {used.Count} of {byToken.Count} source tokens";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Finds known baliza references in tokenized text, ignoring escaped literal "&lt;&lt;".
    /// </summary>
    public static IReadOnlyList<string> FindTokenReferences(string text)
    {
        var references = new List<string>();

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != TokenOpen)
            {
                continue;
            }

            if (i + 1 < text.Length && text[i + 1] == TokenOpen)
            {
                i++;
                continue;
            }

            var close = text.IndexOf(TokenClose, i + 1);
            if (close < 0)
            {
                continue;
            }

            var body = text[(i + 1)..close];
            if (IsKnownTokenBody(body))
            {
                references.Add(text[i..(close + 1)]);
                i = close;
            }
        }

        return references;
    }

    /// <summary>
    /// Finds non-whitespace C0 control bytes that leaked into tokenized text and must be
    /// preserved with the surrounding macro bytes.
    /// </summary>
    public static IReadOnlyList<char> FindUnsafeControlReferences(string text)
    {
        var references = new List<char>();
        foreach (var c in text)
        {
            if ((c <= '\x08' || c is '\x0B' or '\x0C' || (c >= '\x0E' && c <= '\x1F'))
                && c != '\0')
            {
                references.Add(c);
            }
        }

        return references;
    }

    private static bool IsKnownTokenBody(string body)
    {
        var hash = body.IndexOf('#', StringComparison.Ordinal);
        var baseName = hash < 0 ? body : body[..hash];
        if (hash >= 0
            && (hash == body.Length - 1 || !body[(hash + 1)..].All(char.IsAsciiDigit)))
        {
            return false;
        }

        return KnownBaseNames.Contains(baseName)
            || (baseName.Length == 9
                && baseName.StartsWith("Payload", StringComparison.Ordinal)
                && baseName[7..].All(char.IsAsciiHexDigit));
    }

    // Returns the longest token in <paramref name="candidates"/> that matches at position
    // <paramref name="start"/>, or null if none match. Since token keys include the closing
    // '>', "<Num>" and "<Num#2>" are distinct strings; longest-first is still harmless.
    internal static string? MatchTokenAt(string text, int start, IEnumerable<string> candidates)
    {
        string? best = null;
        foreach (var candidate in candidates)
        {
            if (start + candidate.Length <= text.Length
                && string.CompareOrdinal(text, start, candidate, 0, candidate.Length) == 0
                && (best is null || candidate.Length > best.Length))
            {
                best = candidate;
            }
        }

        return best;
    }
}

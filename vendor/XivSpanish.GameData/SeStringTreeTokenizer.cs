using System.Text;

namespace XivSpanish.GameData;

/// <summary>
/// Run-aware tokenizer over <see cref="SeNode"/> trees. Unlike <see cref="SeStringTokenizer"/>
/// (which keeps a 0xFF run opaque), this descends into run bodies so the literal text inside a run
/// becomes translatable baliza text, and reconstructs the run on detokenize so its length prefix is
/// recomputed by <see cref="SeStringTree.Serialize"/>.
///
/// <para>Runs are bracketed by structural tokens <c>&lt;Run&gt;</c> … <c>&lt;RunEnd&gt;</c> (made
/// unique with <c>#N</c> like every other token). The open token carries the original length-prefix
/// marker byte so an untouched run re-encodes identically.</para>
/// </summary>
public static class SeStringTreeTokenizer
{
    private const char TokenOpen = SeStringTokenizer.TokenOpen;
    private const char TokenClose = SeStringTokenizer.TokenClose;
    private const string RunName = "Run";
    private const string RunEndName = "RunEnd";
    private const string MacroEndName = "MacroEnd";

    // Token-map byte sentinel marking a macro-open occurrence (vs. a leaf/opaque macro whose token
    // bytes begin with 0x02). Bytes are [sentinel, code, lengthMarker]. 0x00 cannot begin any real
    // node's captured bytes (macros start 0x02, raw/run-marker are 1 byte), so it is unambiguous.
    private const byte MacroOpenSentinel = 0x00;

    /// <summary>
    /// Convenience: parses raw SeString bytes run-aware and returns their tokenized text. Equivalent
    /// to <c>Tokenize(SeStringTree.Parse(raw)).Text</c>. This is the exact form the packager's
    /// run-aware translate path matches against, so the extractor emits it directly and no
    /// legacy→run-aware migration step is needed.
    /// </summary>
    public static string TokenizeRawText(byte[] raw)
        => Tokenize(SeStringTree.Parse(raw)).Text;

    /// <summary>Tokenizes a run-aware tree into reversible baliza text plus an ordered token map.</summary>
    public static TokenizedSeString Tokenize(IReadOnlyList<SeNode> nodes)
    {
        var sb = new StringBuilder();
        var tokens = new List<TokenOccurrence>();
        var nameCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        string Emit(string baseName, byte[] bytes)
        {
            var count = nameCounts.GetValueOrDefault(baseName) + 1;
            nameCounts[baseName] = count;
            var token = count == 1
                ? $"{TokenOpen}{baseName}{TokenClose}"
                : $"{TokenOpen}{baseName}#{count}{TokenClose}";
            sb.Append(token);
            tokens.Add(new TokenOccurrence(token, bytes));
            return token;
        }

        void Walk(IReadOnlyList<SeNode> ns)
        {
            foreach (var node in ns)
            {
                switch (node)
                {
                    case SeNode.Literal lit:
                        sb.Append(lit.Text.Replace("<", "<<", StringComparison.Ordinal));
                        break;
                    case SeNode.Macro mac:
                        if (MacroHasTranslatableText(mac))
                        {
                            // Bracket the macro so its embedded translatable text (in runs / nested
                            // macros, e.g. <If> branches) is exposed between the open/close tokens. The
                            // open token carries [code, lengthMarker] so the macro re-serializes with
                            // the correct opcode and a recomputed chunk length.
                            Emit(SeStringTokenizer.BaseName([0x02, mac.Code]), [MacroOpenSentinel, mac.Code, mac.LengthMarker]);
                            Walk(mac.Children);
                            Emit(MacroEndName, []);
                        }
                        else
                        {
                            // Leaf macro with no translatable text: emit one opaque self-closing token
                            // carrying the full serialized macro bytes, so it round-trips byte-identically
                            // and detokenize can rebuild it without descending.
                            Emit(SeStringTokenizer.BaseName([0x02, mac.Code]), SeStringTree.SerializeNode(mac));
                        }

                        break;
                    case SeNode.OpaqueMacro opaque:
                        Emit(SeStringTokenizer.BaseName(opaque.Bytes), opaque.Bytes);
                        break;
                    case SeNode.RawByte raw:
                        Emit(SeStringTokenizer.BaseName([raw.Value]), [raw.Value]);
                        break;
                    case SeNode.Run run:
                        Emit(RunName, [run.MarkerByte]);
                        Walk(run.Children);
                        Emit(RunEndName, []);
                        break;
                }
            }
        }

        Walk(nodes);
        return new TokenizedSeString(sb.ToString(), tokens);
    }

    /// <summary>
    /// Rebuilds a run-aware tree from tokenized <paramref name="text"/> and the token map. Run
    /// brackets are matched by nesting (a stack), so a translated target only needs to preserve the
    /// token sequence. Fails (returns false) on unmapped/stray tokens, duplicate use, or unbalanced
    /// run brackets — never emits an ambiguous tree.
    /// </summary>
    public static bool TryDetokenize(
        string text,
        IReadOnlyList<TokenOccurrence> tokens,
        out List<SeNode> tree,
        out string? reason)
    {
        tree = new List<SeNode>();
        reason = null;

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
        var stack = new Stack<Frame>();
        var current = tree;
        var literal = new StringBuilder();
        var i = 0;

        void FlushLiteral()
        {
            if (literal.Length > 0)
            {
                current.Add(new SeNode.Literal(literal.ToString()));
                literal.Clear();
            }
        }

        while (i < text.Length)
        {
            var c = text[i];
            if (c != TokenOpen)
            {
                literal.Append(c);
                i++;
                continue;
            }

            if (i + 1 < text.Length && text[i + 1] == TokenOpen)
            {
                literal.Append(TokenOpen);
                i += 2;
                continue;
            }

            var matched = SeStringTokenizer.MatchTokenAt(text, i, byToken.Keys);
            if (matched is null)
            {
                reason = $"unmapped or stray '<' at index {i} in target";
                return false;
            }

            if (!used.Add(matched))
            {
                reason = $"token '{matched}' used more than once in target";
                return false;
            }

            FlushLiteral();
            var bytes = byToken[matched];
            var baseName = BaseNameOf(matched);

            if (baseName == RunName)
            {
                stack.Push(new Frame(current, FrameKind.Run, bytes.Length > 0 ? bytes[0] : (byte)0, 0));
                current = new List<SeNode>();
            }
            else if (baseName == RunEndName)
            {
                if (stack.Count == 0 || stack.Peek().Kind != FrameKind.Run)
                {
                    reason = "unbalanced <RunEnd> without matching <Run>";
                    return false;
                }

                var children = current;
                var frame = stack.Pop();
                current = frame.Parent;
                current.Add(new SeNode.Run(children, frame.Marker));
            }
            else if (baseName == MacroEndName)
            {
                if (stack.Count == 0 || stack.Peek().Kind != FrameKind.Macro)
                {
                    reason = "unbalanced <MacroEnd> without matching macro open";
                    return false;
                }

                var children = current;
                var frame = stack.Pop();
                current = frame.Parent;
                current.Add(new SeNode.Macro(frame.Code, children, frame.Marker));
            }
            else if (bytes.Length == 3 && bytes[0] == MacroOpenSentinel)
            {
                // Macro-open sentinel: [0x00, code, lengthMarker]. Children follow until <MacroEnd>.
                stack.Push(new Frame(current, FrameKind.Macro, bytes[2], bytes[1]));
                current = new List<SeNode>();
            }
            else if (bytes.Length >= 2 && bytes[0] == 0x02)
            {
                // Leaf/opaque macro captured as full serialized bytes: re-parse to a node so the tree
                // is uniform (a single Macro/OpaqueMacro), preserving byte-identity on serialize.
                var parsed = SeStringTree.Parse(bytes.Append((byte)0x00).ToArray());
                if (parsed.Count == 1)
                {
                    current.Add(parsed[0]);
                }
                else
                {
                    current.Add(new SeNode.OpaqueMacro(bytes));
                }
            }
            else if (bytes.Length == 1)
            {
                current.Add(new SeNode.RawByte(bytes[0]));
            }
            else
            {
                reason = $"token '{matched}' has no reconstructible bytes";
                return false;
            }

            i += matched.Length;
        }

        FlushLiteral();

        if (stack.Count != 0)
        {
            reason = stack.Peek().Kind == FrameKind.Run
                ? "unbalanced <Run> without matching <RunEnd>"
                : "unbalanced macro open without matching <MacroEnd>";
            return false;
        }

        return true;
    }

    private enum FrameKind { Run, Macro }

    // A bracketed scope being rebuilt: where to attach the finished node (Parent), its kind, the run
    // marker (Run) or macro length marker (Macro), and the macro opcode (Code, unused for Run).
    private readonly record struct Frame(List<SeNode> Parent, FrameKind Kind, byte Marker, byte Code);

    // A macro is descended into (bracketed) only when its body, recursively, carries text a
    // translator would change: a Literal with non-empty text, or a 0xFF Run (whose body holds the
    // case-string text). Pure structural/expression macros (e.g. <NewLine>, <ColorType>) have no such
    // content and stay a single opaque self-closing token so the corpus is not needlessly verbose.
    private static bool MacroHasTranslatableText(SeNode.Macro macro) => HasTranslatableText(macro.Children);

    private static bool HasTranslatableText(IReadOnlyList<SeNode> nodes)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case SeNode.Literal lit when lit.Text.Any(c => c >= ' '):
                    // Only printable text is translatable. A literal that is purely C0 control bytes
                    // (e.g. the 0x01 expression byte inside a <ColorType> body) is machinery, not text,
                    // so a macro carrying only those stays a single opaque token (no needless bracketing).
                    return true;
                case SeNode.Run:
                    return true;
                case SeNode.Macro mac when HasTranslatableText(mac.Children):
                    return true;
            }
        }

        return false;
    }

    // "<Run#3>" -> "Run"; "<Payload03>" -> "Payload03".
    private static string BaseNameOf(string token)
    {
        var inner = token.Trim(TokenOpen, TokenClose);
        var hash = inner.IndexOf('#');
        return hash >= 0 ? inner[..hash] : inner;
    }
}

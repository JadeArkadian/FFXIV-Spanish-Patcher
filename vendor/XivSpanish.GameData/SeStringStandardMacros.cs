using System.Text;

namespace XivSpanish.GameData;

/// <summary>
/// Parses the small, explicitly allow-listed set of SeString macros that a translation target may
/// add even when the source language is plain text. This is deliberately separate from the normal
/// token map: arbitrary payload invention remains impossible.
/// </summary>
public static class SeStringStandardMacros
{
    public const string GenderOpen = "<Gender>";
    public const string GenderElse = "<GenderElse>";
    public const string GenderEnd = "<GenderEnd>";

    private const byte IfMacroCode = 0x08;
    private const byte GlobalNumberExpression = 0xE9;
    private const char PlayerGenderParameter = '\x05'; // packed integer 4: player gender

    // Private markers used to carry a gender conditional through the payload detokenizer. Chosen
    // from the "interlinear annotation" block: never present in game text nor in a target.
    private const char MarkerOpen = '\uFFF9';
    private const char MarkerClose = '\uFFFA';

    /// <summary>True when text contains any reserved standard-macro delimiter.</summary>
    public static bool HasReservedDelimiter(string text)
        => FindUnescaped(text, GenderOpen, 0) >= 0
            || FindUnescaped(text, GenderElse, 0) >= 0
            || FindUnescaped(text, GenderEnd, 0) >= 0;

    /// <summary>
    /// Parses target authoring syntax into a run-aware SeString tree. The supported construct is
    /// <c>&lt;Gender&gt;female&lt;GenderElse&gt;male&lt;GenderEnd&gt;</c>. Its binary form matches the
    /// official French localization: <c>If(GlobalNumber(4), female, male)</c> with both branches
    /// encoded as length-prefixed runs.
    /// </summary>
    public static bool TryParse(string text, out List<SeNode> nodes, out string? reason)
    {
        ArgumentNullException.ThrowIfNull(text);
        nodes = [];
        reason = null;

        var literal = new StringBuilder();
        var position = 0;
        while (position < text.Length)
        {
            if (text[position] != SeStringTokenizer.TokenOpen)
            {
                literal.Append(text[position++]);
                continue;
            }

            if (position + 1 < text.Length && text[position + 1] == SeStringTokenizer.TokenOpen)
            {
                literal.Append(SeStringTokenizer.TokenOpen);
                position += 2;
                continue;
            }

            if (!text.AsSpan(position).StartsWith(GenderOpen, StringComparison.Ordinal))
            {
                reason = $"unsupported or stray '<' at index {position} in standard-macro target";
                nodes = [];
                return false;
            }

            FlushLiteral(nodes, literal);
            var femaleStart = position + GenderOpen.Length;
            var elsePosition = FindUnescaped(text, GenderElse, femaleStart);
            var nestedPosition = FindUnescaped(text, GenderOpen, femaleStart);
            if (elsePosition < 0 || (nestedPosition >= 0 && nestedPosition < elsePosition))
            {
                reason = $"{GenderOpen} at index {position} has no valid {GenderElse}";
                nodes = [];
                return false;
            }

            var maleStart = elsePosition + GenderElse.Length;
            var endPosition = FindUnescaped(text, GenderEnd, maleStart);
            nestedPosition = FindUnescaped(text, GenderOpen, maleStart);
            if (endPosition < 0 || (nestedPosition >= 0 && nestedPosition < endPosition))
            {
                reason = $"{GenderOpen} at index {position} has no valid {GenderEnd}";
                nodes = [];
                return false;
            }

            var femaleRaw = text[femaleStart..elsePosition];
            var maleRaw = text[maleStart..endPosition];
            if (!TryDecodeLiteralBranch(femaleRaw, out var female, out reason)
                || !TryDecodeLiteralBranch(maleRaw, out var male, out reason))
            {
                nodes = [];
                return false;
            }

            if (female.Length == 0 || male.Length == 0)
            {
                reason = "gender conditional requires non-empty female and male branches";
                nodes = [];
                return false;
            }

            nodes.Add(BuildGenderMacro(female, male));

            position = endPosition + GenderEnd.Length;
        }

        FlushLiteral(nodes, literal);
        return true;
    }

    /// <summary>
    /// Splits the gender constructs out of <paramref name="text"/>, replacing each with a private
    /// marker, and returns the synthesized <c>If</c> nodes in the order they appeared. Lets a
    /// target carry BOTH the source's real payloads and a target-authored gender conditional: the
    /// caller detokenizes the marked text against the source token map and then splices the nodes
    /// back in with <see cref="SpliceMarkers"/>. Branches must still be literal text.
    /// </summary>
    public static bool TrySplitGenderMacros(
        string text,
        out string markedText,
        out List<SeNode> macros,
        out string? reason)
    {
        ArgumentNullException.ThrowIfNull(text);
        markedText = text;
        macros = [];
        reason = null;

        if (text.Contains(MarkerOpen) || text.Contains(MarkerClose))
        {
            reason = "target contains a reserved gender-macro marker character";
            return false;
        }

        var builder = new StringBuilder();
        var position = 0;
        while (position < text.Length)
        {
            var open = FindUnescaped(text, GenderOpen, position);
            if (open < 0)
            {
                builder.Append(text, position, text.Length - position);
                break;
            }

            builder.Append(text, position, open - position);

            var femaleStart = open + GenderOpen.Length;
            var elsePosition = FindUnescaped(text, GenderElse, femaleStart);
            var nestedPosition = FindUnescaped(text, GenderOpen, femaleStart);
            if (elsePosition < 0 || (nestedPosition >= 0 && nestedPosition < elsePosition))
            {
                reason = $"{GenderOpen} at index {open} has no valid {GenderElse}";
                macros = [];
                return false;
            }

            var maleStart = elsePosition + GenderElse.Length;
            var endPosition = FindUnescaped(text, GenderEnd, maleStart);
            nestedPosition = FindUnescaped(text, GenderOpen, maleStart);
            if (endPosition < 0 || (nestedPosition >= 0 && nestedPosition < endPosition))
            {
                reason = $"{GenderOpen} at index {open} has no valid {GenderEnd}";
                macros = [];
                return false;
            }

            if (!TryDecodeLiteralBranch(text[femaleStart..elsePosition], out var female, out reason)
                || !TryDecodeLiteralBranch(text[maleStart..endPosition], out var male, out reason))
            {
                macros = [];
                return false;
            }

            if (female.Length == 0 || male.Length == 0)
            {
                reason = "gender conditional requires non-empty female and male branches";
                macros = [];
                return false;
            }

            builder.Append(MarkerOpen).Append(macros.Count).Append(MarkerClose);
            macros.Add(BuildGenderMacro(female, male));
            position = endPosition + GenderEnd.Length;
        }

        markedText = builder.ToString();
        return true;
    }

    /// <summary>
    /// Replaces the markers left by <see cref="TrySplitGenderMacros"/> with their macro nodes,
    /// walking runs and macros so a conditional may sit at any depth of the rebuilt tree.
    /// </summary>
    public static bool SpliceMarkers(List<SeNode> nodes, IReadOnlyList<SeNode> macros, out string? reason)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(macros);
        reason = null;

        var spliced = 0;
        if (!SpliceInto(nodes, macros, ref spliced, out reason))
        {
            return false;
        }

        if (spliced != macros.Count)
        {
            reason = "gender conditional markers were lost while rebuilding the target";
            return false;
        }

        return true;
    }

    private static bool SpliceInto(List<SeNode> nodes, IReadOnlyList<SeNode> macros, ref int spliced, out string? reason)
    {
        reason = null;
        for (var i = 0; i < nodes.Count; i++)
        {
            switch (nodes[i])
            {
                case SeNode.Literal literal when literal.Text.Contains(MarkerOpen):
                {
                    if (!TryExpandLiteral(literal.Text, macros, ref spliced, out var expanded, out reason))
                    {
                        return false;
                    }

                    nodes.RemoveAt(i);
                    nodes.InsertRange(i, expanded);
                    i += expanded.Count - 1;
                    break;
                }

                case SeNode.Run run:
                {
                    var children = new List<SeNode>(run.Children);
                    if (!SpliceInto(children, macros, ref spliced, out reason))
                    {
                        return false;
                    }

                    nodes[i] = new SeNode.Run(children, run.MarkerByte);
                    break;
                }

                case SeNode.Macro macro:
                {
                    var children = new List<SeNode>(macro.Children);
                    if (!SpliceInto(children, macros, ref spliced, out reason))
                    {
                        return false;
                    }

                    nodes[i] = new SeNode.Macro(macro.Code, children, macro.LengthMarker);
                    break;
                }
            }
        }

        return true;
    }

    private static bool TryExpandLiteral(
        string text,
        IReadOnlyList<SeNode> macros,
        ref int spliced,
        out List<SeNode> expanded,
        out string? reason)
    {
        expanded = [];
        reason = null;

        var literal = new StringBuilder();
        var position = 0;
        while (position < text.Length)
        {
            if (text[position] != MarkerOpen)
            {
                literal.Append(text[position++]);
                continue;
            }

            var close = text.IndexOf(MarkerClose, position);
            if (close < 0 || !int.TryParse(text.AsSpan(position + 1, close - position - 1), out var index)
                || index < 0 || index >= macros.Count)
            {
                reason = "malformed gender conditional marker in the rebuilt target";
                expanded = [];
                return false;
            }

            if (literal.Length > 0)
            {
                expanded.Add(new SeNode.Literal(literal.ToString()));
                literal.Clear();
            }

            expanded.Add(macros[index]);
            spliced++;
            position = close + 1;
        }

        if (literal.Length > 0)
        {
            expanded.Add(new SeNode.Literal(literal.ToString()));
        }

        return true;
    }

    private static SeNode BuildGenderMacro(string female, string male)
        => new SeNode.Macro(
            IfMacroCode,
            [
                new SeNode.RawByte(GlobalNumberExpression),
                new SeNode.Literal(PlayerGenderParameter.ToString()),
                new SeNode.Run([new SeNode.Literal(female)], 0x01),
                new SeNode.Run([new SeNode.Literal(male)], 0x01),
            ],
            0x01);

    private static void FlushLiteral(List<SeNode> nodes, StringBuilder literal)
    {
        if (literal.Length == 0)
        {
            return;
        }

        nodes.Add(new SeNode.Literal(literal.ToString()));
        literal.Clear();
    }

    private static int FindUnescaped(string text, string delimiter, int start)
    {
        var position = start;
        while (position < text.Length)
        {
            var found = text.IndexOf(delimiter, position, StringComparison.Ordinal);
            if (found < 0)
            {
                return -1;
            }

            var precedingOpenCount = 0;
            for (var i = found - 1; i >= 0 && text[i] == SeStringTokenizer.TokenOpen; i--)
            {
                precedingOpenCount++;
            }

            if (precedingOpenCount % 2 == 0)
            {
                return found;
            }

            position = found + delimiter.Length;
        }

        return -1;
    }

    private static bool TryDecodeLiteralBranch(string text, out string decoded, out string? reason)
    {
        var literal = new StringBuilder();
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != SeStringTokenizer.TokenOpen)
            {
                literal.Append(text[i]);
                continue;
            }

            if (i + 1 < text.Length && text[i + 1] == SeStringTokenizer.TokenOpen)
            {
                literal.Append(SeStringTokenizer.TokenOpen);
                i++;
                continue;
            }

            decoded = string.Empty;
            reason = "gender conditional branches must be literal text; nested payloads are not allowed";
            return false;
        }

        decoded = literal.ToString();
        reason = null;
        return true;
    }
}

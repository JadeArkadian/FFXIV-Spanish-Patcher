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

            nodes.Add(new SeNode.Macro(
                IfMacroCode,
                [
                    new SeNode.RawByte(GlobalNumberExpression),
                    new SeNode.Literal(PlayerGenderParameter.ToString()),
                    new SeNode.Run([new SeNode.Literal(female)], 0x01),
                    new SeNode.Run([new SeNode.Literal(male)], 0x01),
                ],
                0x01));

            position = endPosition + GenderEnd.Length;
        }

        FlushLiteral(nodes, literal);
        return true;
    }

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

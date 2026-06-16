using System.Text.RegularExpressions;

namespace XivSpanish.GameData;

/// <summary>The kind of incompatibility found between a source and a target tokenized SeString.</summary>
public enum SeStringViolationKind
{
    /// <summary>A payload token present in the source is missing from the target (the client would lose a macro).</summary>
    MissingPayload,

    /// <summary>The target references a payload token that does not exist in the source (nothing to detokenize it to).</summary>
    ExtraPayload,

    /// <summary>Both strings carry the same payload multiset but in a different structural order.</summary>
    ReorderedPayloads,

    /// <summary>A '&lt;' in the target is neither an escaped literal ("&lt;&lt;") nor part of a payload token.</summary>
    StrayDelimiter,

    /// <summary>
    /// A length-prefixed run bracket (<c>&lt;Run&gt;</c>/<c>&lt;RunEnd&gt;</c>) is dropped, invented or
    /// mis-nested: the run's recomputed length prefix would no longer delimit the content the client
    /// reader expects (the historical Duty Finder crash class).
    /// </summary>
    UnbalancedRun,

    /// <summary>A macro bracket (<c>&lt;MacroEnd&gt;</c> or its opening token) is dropped or invented: the macro chunk length would desync.</summary>
    UnbalancedMacro,

    /// <summary>The raw C0 control characters (macro expression bytes) differ between source and target.</summary>
    ControlCharMismatch,

    /// <summary>
    /// The source carries a legacy opaque <c>0xFF</c> run token (<c>&lt;PayloadFF...&gt;</c>) whose
    /// mapped bytes embed the run-length prefix VERBATIM, and the target changes literal text. The
    /// patcher would replay the stale prefix over a body of a different length — the exact desync
    /// that crashed the Duty Finder (Addon row 2513). Such rows must be re-tokenized run-aware
    /// before any literal may change; identical literals (status-only edits) remain safe.
    /// </summary>
    StaleRunLength,
}

/// <summary>
/// One concrete incompatibility. <see cref="Token"/> is the offending payload token (or the
/// offending character rendered as hex for control/delimiter issues); <see cref="Position"/> is the
/// character index in the target (or in the source for a missing payload), -1 when not applicable.
/// </summary>
public sealed record SeStringViolation(SeStringViolationKind Kind, string? Token, int Position, string Message)
{
    /// <summary>Single-line, human-readable form suitable for CLI output.</summary>
    public override string ToString()
        => Position >= 0 ? $"[{Kind}] {Message} (at index {Position})" : $"[{Kind}] {Message}";
}

/// <summary>
/// The full diagnostic result of a source/target compatibility check. Compatible means every check
/// passed and the target is structurally safe to detokenize and serialize (payloads intact,
/// structural order preserved, run/macro brackets balanced so every length prefix recomputes to a
/// consistent value).
/// </summary>
public sealed record SeStringCompatibilityReport(IReadOnlyList<SeStringViolation> Violations)
{
    /// <summary>True when no violation was found.</summary>
    public bool IsCompatible => Violations.Count == 0;

    /// <summary>Multi-line, human-readable summary of every violation (empty string when compatible).</summary>
    public string Describe() => string.Join(Environment.NewLine, Violations);
}

/// <summary>
/// Shared validator for corpus <c>source</c>/<c>target</c> pairs in the tokenized (baliza) SeString
/// representation produced by <see cref="SeStringTokenizer"/> / <see cref="SeStringTreeTokenizer"/>.
/// Implements the "atom-spec" guard proven in the Addon iteration-2 batch:
///
/// <list type="number">
/// <item><b>Payload multiset</b> — every payload token in the source appears in the target exactly
/// once each (token names are made unique with <c>#N</c>), and the target invents none.</item>
/// <item><b>Structural order</b> — the token sequence of the target equals that of the source.
/// Detokenization pairs brackets by nesting, so a reordered sequence would rebuild a different tree
/// than the one the row's vanilla bytes encode.</item>
/// <item><b>Length-prefixed run integrity</b> — <c>&lt;Run&gt;</c>/<c>&lt;RunEnd&gt;</c> brackets must
/// nest correctly. Run (and macro chunk) length prefixes are recomputed from bracket content on
/// serialize, so a dropped, invented or inverted bracket is exactly what desyncs the client's
/// length-prefixed reader (the crash class fixed by the run-aware tree).</item>
/// <item><b>Control characters</b> — raw C0 bytes embedded in the tokenized text are macro
/// expression machinery, never translatable: their sequence must be preserved verbatim.</item>
/// </list>
///
/// Plain text without payloads passes trivially. The check is purely textual (no vanilla bytes
/// required), which makes it usable as a hard gate in XivSpanish.Jsonl edits and as a pre-flight in
/// XivSpanish.Packager before the patcher cascade runs.
/// </summary>
public static partial class SeStringCompatibilityValidator
{
    private const string RunBase = "Run";
    private const string RunEndBase = "RunEnd";
    private const string MacroEndBase = "MacroEnd";

    [GeneratedRegex(@"<[A-Za-z][A-Za-z0-9]*(?:#[0-9]+)?>")]
    private static partial Regex TokenPattern();

    /// <summary>
    /// True when <paramref name="text"/> carries SeString machinery (payload tokens or raw macro
    /// control bytes). Used by callers that gate only payload-bearing rows and leave plain text alone.
    /// </summary>
    public static bool HasPayloads(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return ExtractAtoms(text).Count > 0 || ControlChars(text).Count > 0;
    }

    /// <summary>
    /// Validates that <paramref name="target"/> is payload-compatible with <paramref name="source"/>.
    /// Returns a detailed report; <see cref="SeStringCompatibilityReport.IsCompatible"/> is the gate.
    /// </summary>
    public static SeStringCompatibilityReport Validate(string source, string target)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        var violations = new List<SeStringViolation>();

        var sourceAtoms = ExtractAtoms(source);
        var targetAtoms = ExtractAtoms(target);

        CheckStrayDelimiters(target, targetAtoms, violations);
        CheckMultiset(sourceAtoms, targetAtoms, violations);
        CheckRunNesting(targetAtoms, violations);
        CheckOrder(sourceAtoms, targetAtoms, violations);
        CheckControlChars(source, target, violations);
        CheckStaleRunLength(source, target, sourceAtoms, targetAtoms, violations);

        return new SeStringCompatibilityReport(violations);
    }

    /// <summary>One payload token occurrence: its exact text (e.g. "&lt;Num#2&gt;") and char index.</summary>
    private readonly record struct Atom(string Token, int Position)
    {
        public string BaseName
        {
            get
            {
                var inner = Token[1..^1];
                var hash = inner.IndexOf('#');
                return hash < 0 ? inner : inner[..hash];
            }
        }
    }

    // Extracts payload tokens left to right. "<<" is the escape for a literal '<' and never starts
    // a token (mirrors the tokenizers); any regex-shaped <Name> / <Name#N> counts as a token, which
    // matches the proven atom-spec guard (an unknown name simply fails the multiset check).
    private static List<Atom> ExtractAtoms(string text)
    {
        var atoms = new List<Atom>();
        var i = 0;
        while (i < text.Length)
        {
            if (text[i] != SeStringTokenizer.TokenOpen)
            {
                i++;
                continue;
            }

            if (i + 1 < text.Length && text[i + 1] == SeStringTokenizer.TokenOpen)
            {
                i += 2; // escaped literal '<'
                continue;
            }

            var match = TokenPattern().Match(text, i);
            if (match.Success && match.Index == i)
            {
                atoms.Add(new Atom(match.Value, i));
                i += match.Length;
            }
            else
            {
                i++;
            }
        }

        return atoms;
    }

    // A '<' in the target that is neither "<<" nor a token is a corruption the detokenizer would
    // reject; report it with its exact position so the editor can fix the right spot.
    private static void CheckStrayDelimiters(string target, List<Atom> targetAtoms, List<SeStringViolation> violations)
    {
        var tokenStarts = new HashSet<int>(targetAtoms.Select(a => a.Position));
        var i = 0;
        while (i < target.Length)
        {
            if (target[i] != SeStringTokenizer.TokenOpen)
            {
                i++;
                continue;
            }

            if (i + 1 < target.Length && target[i + 1] == SeStringTokenizer.TokenOpen)
            {
                i += 2;
                continue;
            }

            if (tokenStarts.Contains(i))
            {
                var atom = targetAtoms.First(a => a.Position == i);
                i += atom.Token.Length;
                continue;
            }

            violations.Add(new SeStringViolation(
                SeStringViolationKind.StrayDelimiter,
                "<",
                i,
                "target has a stray '<' that is neither an escaped literal (\"<<\") nor a payload token"));
            i++;
        }
    }

    // Multiset equality. Token names are unique per string (#N suffixes), so each count is normally
    // 0/1, but the comparison stays count-based to also catch duplicated tokens in a target.
    private static void CheckMultiset(List<Atom> sourceAtoms, List<Atom> targetAtoms, List<SeStringViolation> violations)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var atom in sourceAtoms)
        {
            counts[atom.Token] = counts.GetValueOrDefault(atom.Token) + 1;
        }

        foreach (var atom in targetAtoms)
        {
            counts[atom.Token] = counts.GetValueOrDefault(atom.Token) - 1;
        }

        foreach (var (token, delta) in counts)
        {
            if (delta == 0)
            {
                continue;
            }

            var baseName = BaseNameOf(token);
            if (delta > 0)
            {
                var position = sourceAtoms.First(a => a.Token == token).Position;
                violations.Add(new SeStringViolation(
                    ClassifyMissingOrExtra(baseName, missing: true),
                    token,
                    position,
                    $"payload {token} appears {delta} more time(s) in source than in target"
                        + StructuralHint(baseName)));
            }
            else
            {
                var position = targetAtoms.First(a => a.Token == token).Position;
                violations.Add(new SeStringViolation(
                    ClassifyMissingOrExtra(baseName, missing: false),
                    token,
                    position,
                    $"payload {token} appears {-delta} more time(s) in target than in source"
                        + StructuralHint(baseName)));
            }
        }
    }

    private static SeStringViolationKind ClassifyMissingOrExtra(string baseName, bool missing) => baseName switch
    {
        RunBase or RunEndBase => SeStringViolationKind.UnbalancedRun,
        MacroEndBase => SeStringViolationKind.UnbalancedMacro,
        _ => missing ? SeStringViolationKind.MissingPayload : SeStringViolationKind.ExtraPayload,
    };

    private static string StructuralHint(string baseName) => baseName switch
    {
        RunBase or RunEndBase =>
            "; this is a length-prefixed run bracket — its recomputed length prefix would desync the client reader",
        MacroEndBase =>
            "; this is a macro bracket — the macro chunk length would desync",
        _ => string.Empty,
    };

    // <Run>/<RunEnd> must nest like a balanced bracket sequence in the target. This fires even when
    // the multiset matches (e.g. a <RunEnd> moved before its <Run>): the rebuilt run would prefix a
    // length over the wrong content.
    private static void CheckRunNesting(List<Atom> targetAtoms, List<SeStringViolation> violations)
    {
        var depth = 0;
        Atom? lastUnmatchedOpen = null;
        foreach (var atom in targetAtoms)
        {
            switch (atom.BaseName)
            {
                case RunBase:
                    depth++;
                    lastUnmatchedOpen = atom;
                    break;
                case RunEndBase:
                    depth--;
                    if (depth < 0)
                    {
                        violations.Add(new SeStringViolation(
                            SeStringViolationKind.UnbalancedRun,
                            atom.Token,
                            atom.Position,
                            $"{atom.Token} in target has no preceding open <Run...> bracket"));
                        depth = 0;
                    }

                    break;
            }
        }

        if (depth > 0 && lastUnmatchedOpen is { } open)
        {
            violations.Add(new SeStringViolation(
                SeStringViolationKind.UnbalancedRun,
                open.Token,
                open.Position,
                $"{open.Token} in target is never closed by a <RunEnd...> bracket"));
        }
    }

    // Exact token sequence equality. Only reported when the multiset already matches, so each
    // problem surfaces as a single, precise violation.
    private static void CheckOrder(List<Atom> sourceAtoms, List<Atom> targetAtoms, List<SeStringViolation> violations)
    {
        if (violations.Any(v => v.Kind is SeStringViolationKind.MissingPayload
            or SeStringViolationKind.ExtraPayload
            or SeStringViolationKind.UnbalancedRun
            or SeStringViolationKind.UnbalancedMacro))
        {
            return;
        }

        for (var i = 0; i < sourceAtoms.Count && i < targetAtoms.Count; i++)
        {
            if (!string.Equals(sourceAtoms[i].Token, targetAtoms[i].Token, StringComparison.Ordinal))
            {
                violations.Add(new SeStringViolation(
                    SeStringViolationKind.ReorderedPayloads,
                    targetAtoms[i].Token,
                    targetAtoms[i].Position,
                    $"payload order diverges at token #{i + 1}: source has {sourceAtoms[i].Token}, target has {targetAtoms[i].Token}"));
                return;
            }
        }
    }

    // Non-whitespace C0 control characters in the tokenized text are raw macro expression bytes
    // (e.g. the \x03 branch separators inside an <If> body). They are machinery, not text: the
    // target must carry exactly the same sequence. Whitespace controls (\t \n \r) are ordinary
    // text a translator may legitimately reflow, so they are excluded — the same definition as
    // SeStringTokenizer.FindUnsafeControlReferences.
    private static void CheckControlChars(string source, string target, List<SeStringViolation> violations)
    {
        var sourceControls = ControlChars(source);
        var targetControls = ControlChars(target);
        if (sourceControls.SequenceEqual(targetControls))
        {
            return;
        }

        violations.Add(new SeStringViolation(
            SeStringViolationKind.ControlCharMismatch,
            null,
            -1,
            $"raw control bytes differ: source has [{FormatControls(sourceControls)}], target has [{FormatControls(targetControls)}]"));
    }

    /// <summary>Base-name prefix of legacy opaque 0xFF run tokens whose mapped bytes embed the run-length prefix verbatim.</summary>
    public const string LegacyRunTokenPrefix = "PayloadFF";

    // Legacy 0xFF tokens (<PayloadFF>, <PayloadFF#N>) are opaque: their mapped bytes carry the
    // run-length prefix verbatim, computed against the VANILLA literals. If the target changes any
    // literal text, the replayed prefix no longer covers the run body and the client's
    // length-prefixed reader desyncs (the Duty Finder crash, Addon row 2513). Hard-reject any
    // literal change on such rows; identical literals (status-only edits, untranslated targets)
    // still pass so already-shipped untouched rows are not blocked.
    private static void CheckStaleRunLength(
        string source,
        string target,
        List<Atom> sourceAtoms,
        List<Atom> targetAtoms,
        List<SeStringViolation> violations)
    {
        string? legacyToken = null;
        foreach (var atom in sourceAtoms)
        {
            if (atom.BaseName.StartsWith(LegacyRunTokenPrefix, StringComparison.Ordinal))
            {
                legacyToken = atom.Token;
                break;
            }
        }

        if (legacyToken is null
            || string.Equals(LiteralsOf(source, sourceAtoms), LiteralsOf(target, targetAtoms), StringComparison.Ordinal))
        {
            return;
        }

        violations.Add(new SeStringViolation(
            SeStringViolationKind.StaleRunLength,
            legacyToken,
            -1,
            $"source carries legacy opaque 0xFF run token {legacyToken} whose mapped bytes embed the "
            + "run-length prefix verbatim; the target changes literal text, so the replayed prefix would "
            + "no longer match the run body length (stale run-length — the Duty Finder crash class). "
            + "Re-tokenize the row with the run-aware tree before translating it"));
    }

    // The literal text of a tokenized string: everything outside <...> payload tokens, concatenated.
    private static string LiteralsOf(string text, List<Atom> atoms)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        var i = 0;
        foreach (var atom in atoms)
        {
            sb.Append(text, i, atom.Position - i);
            i = atom.Position + atom.Token.Length;
        }

        sb.Append(text, i, text.Length - i);
        return sb.ToString();
    }

    private static List<char> ControlChars(string text)
    {
        var controls = new List<char>();
        foreach (var c in text)
        {
            if (c < '\x20' && c is not ('\t' or '\n' or '\r' or '\0'))
            {
                controls.Add(c);
            }
        }

        return controls;
    }

    private static string FormatControls(List<char> controls)
        => string.Join(" ", controls.Select(c => $"0x{(int)c:X2}"));

    private static string BaseNameOf(string token)
    {
        var inner = token[1..^1];
        var hash = inner.IndexOf('#');
        return hash < 0 ? inner : inner[..hash];
    }
}

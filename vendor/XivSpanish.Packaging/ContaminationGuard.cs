namespace XivSpanish.Packager;

/// <summary>Outcome of the contamination guard check.</summary>
/// <param name="Contaminated">True if the base EXD looks contaminated/already translated.</param>
/// <param name="MatchRate">applied / (applied + missedDueToAbsentSource).</param>
/// <param name="EvaluatedEntries">applied + missedDueToAbsentSource (the denominator).</param>
public readonly record struct GuardResult(bool Contaminated, double MatchRate, int EvaluatedEntries);

/// <summary>
/// Detects a base EXD that is contaminated by an installed mod (e.g. already translated by
/// TexTools/Penumbra). The packager matches replacements by English source content; if the base
/// is no longer vanilla the sources stop matching and <c>applied</c> collapses while "source not
/// present" misses spike, silently producing a near-empty <c>.pmp</c>. This guard turns that
/// silent failure into a loud abort.
/// </summary>
public static class ContaminationGuard
{
    /// <summary>
    /// Reason string an <see cref="ExdPatcher"/> miss uses when the English source was not found
    /// in the row. Only these misses count against the match rate; subrow/malformed/write-at-offset
    /// misses are legitimate and excluded from the metric.
    /// </summary>
    public const string AbsentSourceReason = "source string not present in row string columns";

    /// <summary>
    /// Computes the content-match rate over entries where a match was even possible and decides
    /// whether the base is contaminated.
    /// </summary>
    /// <param name="applied">Replacements that matched and were written.</param>
    /// <param name="missedAbsentSource">Misses whose English source was absent from the row.</param>
    /// <param name="minMatchRate">Abort threshold, e.g. 0.5.</param>
    /// <param name="minVolume">Minimum evaluated entries before the guard can fire (avoids flagging
    /// tiny packages where a low rate is not statistically meaningful).</param>
    public static GuardResult Evaluate(int applied, int missedAbsentSource, double minMatchRate, int minVolume)
    {
        var evaluated = applied + missedAbsentSource;
        if (evaluated == 0)
        {
            return new GuardResult(false, 1.0, 0);
        }

        var rate = (double)applied / evaluated;
        var contaminated = evaluated >= minVolume && rate < minMatchRate;
        return new GuardResult(contaminated, rate, evaluated);
    }
}

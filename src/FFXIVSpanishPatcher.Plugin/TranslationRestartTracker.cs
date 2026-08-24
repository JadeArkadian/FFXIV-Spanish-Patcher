namespace FFXIVSpanishPatcher.Plugin;

/// <summary>
/// Compares the effective mod state with the first valid Penumbra snapshot observed this session.
/// Reverting activation or categories to that baseline clears the restart requirement; rebuilding
/// installed files remains irreversible until the game actually restarts.
/// </summary>
public sealed class TranslationRestartTracker
{
    private RuntimeState? _initial;
    private RuntimeState? _current;
    private bool _filesRebuilt;

    public bool IsRestartRequired
        => _filesRebuilt
            || (_initial is not null && _current is not null && !_initial.Equals(_current));

    public void Observe(CategorySyncSnapshot snapshot)
    {
        if (!snapshot.CanEdit || snapshot.ModEnabled is not { } enabled)
        {
            return;
        }

        var state = new RuntimeState(
            enabled,
            snapshot.EnabledDomains.Order(StringComparer.OrdinalIgnoreCase).ToArray());
        _initial ??= state;
        _current = state;
    }

    public void MarkFilesRebuilt() => _filesRebuilt = true;

    private sealed class RuntimeState(bool enabled, IReadOnlyList<string> domains) : IEquatable<RuntimeState>
    {
        public bool Enabled { get; } = enabled;

        public IReadOnlyList<string> Domains { get; } = domains;

        public bool Equals(RuntimeState? other)
            => other is not null
                && Enabled == other.Enabled
                && Domains.SequenceEqual(other.Domains, StringComparer.OrdinalIgnoreCase);

        public override bool Equals(object? obj) => obj is RuntimeState other && Equals(other);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(Enabled);
            foreach (var domain in Domains)
            {
                hash.Add(domain, StringComparer.OrdinalIgnoreCase);
            }

            return hash.ToHashCode();
        }
    }
}

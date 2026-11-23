namespace FFXIVSpanishPatcher.Plugin;

/// <summary>
/// Compares the effective mod state with the first valid Penumbra snapshot observed this session.
/// Reverting activation or categories to that baseline clears the restart requirement; rebuilding
/// installed files remains irreversible until the game actually restarts.
/// </summary>
/// <remarks>
/// "This session" means the game process, not this plugin instance. With a session memory the
/// baseline and the rebuild flag survive a plugin reload: a reload neither invents a restart
/// requirement nor forgets a real one.
/// </remarks>
public sealed class TranslationRestartTracker
{
    private const string BaselineKey = "restart.baseline";
    private const string FilesRebuiltKey = "restart.filesRebuilt";

    private readonly IGameSessionMemory? _session;
    private RuntimeState? _initial;
    private RuntimeState? _current;
    private bool _filesRebuilt;

    public TranslationRestartTracker(IGameSessionMemory? session = null)
    {
        _session = session;
        _initial = RuntimeState.Parse(session?.Get(BaselineKey));
        _filesRebuilt = session?.Get(FilesRebuiltKey) is not null;
    }

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
        if (_initial is null)
        {
            _initial = state;
            _session?.Set(BaselineKey, state.Serialize());
        }

        _current = state;
    }

    public void MarkFilesRebuilt()
    {
        _filesRebuilt = true;
        _session?.Set(FilesRebuiltKey, "1");
    }

    private sealed class RuntimeState(bool enabled, IReadOnlyList<string> domains) : IEquatable<RuntimeState>
    {
        public bool Enabled { get; } = enabled;

        public IReadOnlyList<string> Domains { get; } = domains;

        // Domains are catalog identifiers, so a comma can never be part of one.
        public string Serialize() => (Enabled ? "1" : "0") + "|" + string.Join(',', Domains);

        public static RuntimeState? Parse(string? value)
        {
            if (value is null || value.Length < 2 || value[1] != '|' || value[0] is not ('0' or '1'))
            {
                return null;
            }

            return new RuntimeState(
                value[0] == '1',
                value[2..].Split(',', StringSplitOptions.RemoveEmptyEntries));
        }

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

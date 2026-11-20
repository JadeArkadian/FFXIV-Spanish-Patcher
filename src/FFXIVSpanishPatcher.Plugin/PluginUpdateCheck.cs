using System.Text.Json;

namespace FFXIVSpanishPatcher.Plugin;

/// <summary>
/// Tells the user when a newer plugin version has been published. Dalamud normally updates
/// plugins, but a user can have that switched off or simply ignore it, and an outdated plugin
/// keeps installing outdated translations. Dalamud does not tell a plugin whether it has an
/// update pending, so this reads the same repository file Dalamud installs the plugin from.
/// </summary>
public static class PluginUpdateCheck
{
    /// <summary>The custom Dalamud repository written by the release workflow.</summary>
    public const string RepositoryUrl =
        "https://raw.githubusercontent.com/JadeArkadian/FFXIV-Spanish-Patcher/main/dalamud/pluginmaster.json";

    private const string InternalName = "FFXIVSpanishTranslationManager";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// A development build has no published counterpart: it is loaded from a folder, or was built
    /// without a release version (0.0.0, as CI artifacts are). Comparing it would always "find"
    /// an update.
    /// </summary>
    public static bool AppliesTo(bool isDevPlugin, Version? installed)
        => !isDevPlugin
            && installed is not null
            && (installed.Major, installed.Minor, installed.Build) != (0, 0, 0);

    /// <summary>The version the repository file offers, or null when it does not say.</summary>
    public static Version? ReadPublishedVersion(string repositoryJson)
    {
        try
        {
            using var document = JsonDocument.Parse(repositoryJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var plugin in document.RootElement.EnumerateArray())
            {
                if (plugin.ValueKind == JsonValueKind.Object
                    && plugin.TryGetProperty("InternalName", out var name)
                    && name.ValueKind == JsonValueKind.String
                    && StringComparer.Ordinal.Equals(name.GetString(), InternalName)
                    && plugin.TryGetProperty("AssemblyVersion", out var version)
                    && version.ValueKind == JsonValueKind.String
                    && Version.TryParse(version.GetString(), out var published))
                {
                    return published;
                }
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    /// <summary>
    /// The published version when it is newer than <paramref name="installed"/>; null when the
    /// plugin is current or the repository cannot be read. Being offline is not an error.
    /// </summary>
    public static async Task<Version?> FindNewerVersionAsync(
        HttpClient client,
        Version installed,
        CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(Timeout);
            var json = await client.GetStringAsync(RepositoryUrl, timeout.Token).ConfigureAwait(false);
            return ReadPublishedVersion(json) is { } published && published > installed
                ? published
                : null;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>Release versions have three parts; the fourth is always zero.</summary>
    public static string Display(Version version) => version.ToString(3);
}

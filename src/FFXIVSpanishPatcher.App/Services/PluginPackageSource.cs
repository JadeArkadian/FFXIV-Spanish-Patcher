using System.Text.Json;

namespace FFXIVSpanishPatcher.App.Services;

public interface IPluginPackageSource
{
    /// <summary>Downloads the plugin package the custom repository offers; null when it cannot.</summary>
    Task<byte[]?> DownloadAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads the custom Dalamud repository and downloads the plugin ZIP it points to. Only a link into
/// this repository's own GitHub releases is followed.
/// </summary>
public sealed class GitHubPluginPackageSource : IPluginPackageSource
{
    private const int MaxRepositoryBytes = 1024 * 1024;
    private const int MaxPackageBytes = 256 * 1024 * 1024;
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(10);

    private readonly AppBuildInfo _buildInfo;
    private readonly HttpClient _client;

    public GitHubPluginPackageSource(AppBuildInfo buildInfo, HttpMessageHandler? handler = null)
    {
        _buildInfo = buildInfo;
        _client = handler is null ? new HttpClient() : new HttpClient(handler);
        _client.Timeout = System.Threading.Timeout.InfiniteTimeSpan;
        _client.DefaultRequestHeaders.UserAgent.ParseAdd($"FFXIVSpanishPatcher/{buildInfo.PackageVersion}");
    }

    public async Task<byte[]?> DownloadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(Timeout);

            var repository = await GetAsync(_buildInfo.PluginRepositoryUrl, MaxRepositoryBytes, timeout.Token)
                .ConfigureAwait(false);
            if (repository is null || PackageLink(repository) is not { } link)
            {
                return null;
            }

            return await GetAsync(link, MaxPackageBytes, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception
                                              is HttpRequestException
                                              or OperationCanceledException
                                              or JsonException
                                              or IOException)
        {
            return null;
        }
    }

    private string? PackageLink(byte[] repository)
    {
        using var document = JsonDocument.Parse(repository);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var plugin in document.RootElement.EnumerateArray())
        {
            if (plugin.ValueKind == JsonValueKind.Object
                && plugin.TryGetProperty("InternalName", out var name)
                && name.ValueKind == JsonValueKind.String
                && name.GetString() == DalamudPenumbraService.PluginInternalName
                && plugin.TryGetProperty("DownloadLinkInstall", out var link)
                && link.ValueKind == JsonValueKind.String
                // Compared after normalization: raw text would let "../" walk out of the prefix.
                && Uri.TryCreate(link.GetString(), UriKind.Absolute, out var url)
                && url.AbsoluteUri.StartsWith(_buildInfo.PluginDownloadPrefix, StringComparison.Ordinal))
            {
                return url.AbsoluteUri;
            }
        }

        return null;
    }

    private async Task<byte[]?> GetAsync(string url, int maxBytes, CancellationToken cancellationToken)
    {
        using var response = await _client
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > maxBytes)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }
}

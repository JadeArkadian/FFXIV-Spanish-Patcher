using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FFXIVSpanishPatcher.App.Services;

public interface IUpdateCheckService
{
    Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default);
}

public enum UpdateCheckStatus
{
    Disabled,
    UpToDate,
    UpdateAvailable,
    CurrentVersionUnknown,
    Unavailable,
}

public sealed record UpdateCheckResult(
    UpdateCheckStatus Status,
    string CurrentVersion,
    string? LatestVersion = null,
    string? ReleaseUrl = null,
    string? Detail = null)
{
    public static UpdateCheckResult Disabled(string currentVersion)
        => new(UpdateCheckStatus.Disabled, currentVersion);

    public static UpdateCheckResult UpToDate(string currentVersion, string latestVersion, string releaseUrl)
        => new(UpdateCheckStatus.UpToDate, currentVersion, latestVersion, releaseUrl);

    public static UpdateCheckResult UpdateAvailable(string currentVersion, string latestVersion, string releaseUrl)
        => new(UpdateCheckStatus.UpdateAvailable, currentVersion, latestVersion, releaseUrl);

    public static UpdateCheckResult CurrentVersionUnknown(string currentVersion, string latestVersion, string releaseUrl)
        => new(UpdateCheckStatus.CurrentVersionUnknown, currentVersion, latestVersion, releaseUrl);

    public static UpdateCheckResult Unavailable(string currentVersion, string detail)
        => new(UpdateCheckStatus.Unavailable, currentVersion, Detail: detail);
}

public sealed class NullUpdateCheckService : IUpdateCheckService
{
    public static NullUpdateCheckService Instance { get; } = new();

    private NullUpdateCheckService()
    {
    }

    public Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(UpdateCheckResult.Disabled("test"));
}

public sealed class GitHubReleaseUpdateCheckService : IUpdateCheckService
{
    private static readonly HttpClient Client = new();
    private readonly AppBuildInfo _buildInfo;
    private readonly TimeSpan _timeout;

    public GitHubReleaseUpdateCheckService(AppBuildInfo buildInfo, TimeSpan? timeout = null)
    {
        _buildInfo = buildInfo;
        _timeout = timeout ?? TimeSpan.FromSeconds(3);
    }

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(_buildInfo.LatestReleaseApiUrl, UriKind.Absolute, out var endpoint))
        {
            return UpdateCheckResult.Unavailable(_buildInfo.DisplayVersion, "URL de releases invalida.");
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_timeout);

            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.UserAgent.ParseAdd($"FFXIVSpanishPatcher/{_buildInfo.PackageVersion}");
            request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");

            using var response = await Client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return UpdateCheckResult.Unavailable(
                    _buildInfo.DisplayVersion,
                    $"GitHub respondio {(int)response.StatusCode}.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            var payload = await JsonSerializer
                .DeserializeAsync<GitHubLatestRelease>(stream, cancellationToken: timeout.Token)
                .ConfigureAwait(false);

            var latestTag = payload?.TagName?.Trim();
            if (!AppReleaseVersion.TryParse(latestTag, out var latestVersion))
            {
                return UpdateCheckResult.Unavailable(
                    _buildInfo.DisplayVersion,
                    "La release latest no contiene un tag versionable.");
            }

            var releaseUrl = string.IsNullOrWhiteSpace(payload?.HtmlUrl)
                ? _buildInfo.LatestReleasePageUrl
                : payload.HtmlUrl.Trim();

            if (_buildInfo.ComparableVersion is not { } currentVersion)
            {
                return UpdateCheckResult.CurrentVersionUnknown(_buildInfo.DisplayVersion, $"v{latestVersion}", releaseUrl);
            }

            return latestVersion.CompareTo(currentVersion) > 0
                ? UpdateCheckResult.UpdateAvailable(_buildInfo.DisplayVersion, $"v{latestVersion}", releaseUrl)
                : UpdateCheckResult.UpToDate(_buildInfo.DisplayVersion, $"v{latestVersion}", releaseUrl);
        }
        catch (OperationCanceledException)
        {
            return UpdateCheckResult.Unavailable(_buildInfo.DisplayVersion, "Timeout comprobando releases.");
        }
        catch (HttpRequestException exception)
        {
            return UpdateCheckResult.Unavailable(_buildInfo.DisplayVersion, exception.Message);
        }
        catch (JsonException exception)
        {
            return UpdateCheckResult.Unavailable(_buildInfo.DisplayVersion, exception.Message);
        }
    }

    private sealed class GitHubLatestRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; init; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; init; }
    }
}

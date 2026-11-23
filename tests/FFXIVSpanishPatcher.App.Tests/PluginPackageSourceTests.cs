using System.Net;
using FFXIVSpanishPatcher.App.Services;
using Xunit;

namespace FFXIVSpanishPatcher.App.Tests;

public sealed class PluginPackageSourceTests
{
    private const string Releases = "https://github.com/owner/repo/releases/download/";
    private static readonly AppBuildInfo BuildInfo = new("v0.6.0", new AppReleaseVersion(0, 6, 0), "0.6.0", "owner/repo", "", "");

    [Fact]
    public async Task Download_FollowsTheRepositoryToItsOwnRelease()
    {
        var handler = new Handler(Repository(Releases + "v0.6.0/plugin.zip"), [7, 7, 7]);

        var package = await new GitHubPluginPackageSource(BuildInfo, handler).DownloadAsync(TestContext.Current.CancellationToken);

        Assert.Equal([7, 7, 7], package);
        Assert.Equal([BuildInfo.PluginRepositoryUrl, Releases + "v0.6.0/plugin.zip"], handler.Requested);
    }

    [Theory]
    [InlineData("https://example.invalid/plugin.zip")]
    [InlineData("https://github.com/someone/else/releases/download/v1/plugin.zip")]
    [InlineData("http://github.com/owner/repo/releases/download/v0.6.0/plugin.zip")]
    [InlineData("https://github.com/owner/repo/releases/download/../../../../someone/else/releases/download/v1/plugin.zip")]
    [InlineData("https://github.com/owner/repo/releases/download/%2e%2e/%2e%2e/%2e%2e/%2e%2e/someone/else/plugin.zip")]
    [InlineData("releases/download/v0.6.0/plugin.zip")]
    public async Task Download_NeverFetchesAPackageFromOutsideTheProjectReleases(string link)
    {
        var handler = new Handler(Repository(link), [7, 7, 7]);

        Assert.Null(await new GitHubPluginPackageSource(BuildInfo, handler).DownloadAsync(TestContext.Current.CancellationToken));

        Assert.Equal([BuildInfo.PluginRepositoryUrl], handler.Requested);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""[{"InternalName":"Penumbra","DownloadLinkInstall":"https://github.com/owner/repo/releases/download/v1/x.zip"}]""")]
    public async Task Download_WithoutAUsableRepositoryEntryReturnsNothing(string repository)
    {
        var handler = new Handler(repository, [7, 7, 7]);

        Assert.Null(await new GitHubPluginPackageSource(BuildInfo, handler).DownloadAsync(TestContext.Current.CancellationToken));

        Assert.Single(handler.Requested);
    }

    [Fact]
    public async Task Download_WhenTheRepositoryIsNotPublishedYetReturnsNothing()
    {
        var handler = new Handler(repository: null, [7, 7, 7]);

        Assert.Null(await new GitHubPluginPackageSource(BuildInfo, handler).DownloadAsync(TestContext.Current.CancellationToken));
    }

    private static string Repository(string link)
        => $$"""[{"InternalName":"{{DalamudPenumbraService.PluginInternalName}}","DownloadLinkInstall":"{{link}}"}]""";

    private sealed class Handler(string? repository, byte[] package) : HttpMessageHandler
    {
        public List<string> Requested { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            Requested.Add(url);
            if (url != BuildInfo.PluginRepositoryUrl)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(package) });
            }

            return Task.FromResult(repository is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(repository) });
        }
    }
}

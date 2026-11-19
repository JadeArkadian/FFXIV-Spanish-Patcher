using System.Net;
using FFXIVSpanishPatcher.Plugin;
using Xunit;

namespace FFXIVSpanishPatcher.Plugin.Tests;

public sealed class PluginUpdateCheckTests
{
    private const string Repository = """
        [
          { "InternalName": "OtroPlugin", "AssemblyVersion": "9.0.0.0" },
          { "InternalName": "FFXIVSpanishTranslationManager", "AssemblyVersion": "0.6.1.0" }
        ]
        """;

    [Theory]
    [InlineData(false, "0.6.0.0", true)]
    [InlineData(true, "0.6.0.0", false)]
    [InlineData(false, "0.0.0.0", false)]
    [InlineData(false, null, false)]
    public void AppliesTo_SkipsDevelopmentBuilds(bool isDevPlugin, string? installed, bool expected)
    {
        var version = installed is null ? null : Version.Parse(installed);

        Assert.Equal(expected, PluginUpdateCheck.AppliesTo(isDevPlugin, version));
    }

    [Fact]
    public void ReadPublishedVersion_FindsThisPluginAmongOthers()
        => Assert.Equal(new Version(0, 6, 1, 0), PluginUpdateCheck.ReadPublishedVersion(Repository));

    [Theory]
    [InlineData("")]
    [InlineData("404: Not Found")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("""[ "texto", 3, null ]""")]
    [InlineData("""[ { "InternalName": "FFXIVSpanishTranslationManager" } ]""")]
    [InlineData("""[ { "InternalName": "FFXIVSpanishTranslationManager", "AssemblyVersion": 6 } ]""")]
    [InlineData("""[ { "InternalName": "FFXIVSpanishTranslationManager", "AssemblyVersion": "nueva" } ]""")]
    public void ReadPublishedVersion_IgnoresAFileItCannotUse(string json)
        => Assert.Null(PluginUpdateCheck.ReadPublishedVersion(json));

    [Theory]
    [InlineData("0.6.0.0", "0.6.1.0")]
    [InlineData("0.5.9.0", "0.6.1.0")]
    [InlineData("0.6.1.0", null)]
    // A build newer than the repository (a rolled-back release) is not offered a downgrade.
    [InlineData("0.6.2.0", null)]
    public async Task FindNewerVersion_ReportsOnlyAStrictlyNewerRelease(string installed, string? expected)
    {
        using var client = new HttpClient(new StubHandler(HttpStatusCode.OK, Repository));

        var newer = await PluginUpdateCheck.FindNewerVersionAsync(
            client,
            Version.Parse(installed),
            TestContext.Current.CancellationToken);

        Assert.Equal(expected is null ? null : Version.Parse(expected), newer);
    }

    [Fact]
    public async Task FindNewerVersion_AsksTheRepositoryDalamudInstallsFrom()
    {
        var handler = new StubHandler(HttpStatusCode.OK, Repository);
        using var client = new HttpClient(handler);

        await PluginUpdateCheck.FindNewerVersionAsync(
            client,
            new Version(0, 6, 0, 0),
            TestContext.Current.CancellationToken);

        Assert.Equal(PluginUpdateCheck.RepositoryUrl, handler.RequestedUrl);
        Assert.EndsWith("/main/dalamud/pluginmaster.json", handler.RequestedUrl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindNewerVersion_StaysQuietWhenTheRepositoryIsUnreachable()
    {
        using var missing = new HttpClient(new StubHandler(HttpStatusCode.NotFound, "404: Not Found"));
        using var offline = new HttpClient(new StubHandler(new HttpRequestException("sin red")));
        var installed = new Version(0, 6, 0, 0);

        Assert.Null(await PluginUpdateCheck.FindNewerVersionAsync(missing, installed, TestContext.Current.CancellationToken));
        Assert.Null(await PluginUpdateCheck.FindNewerVersionAsync(offline, installed, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task FindNewerVersion_StopsWhenThePluginUnloads()
    {
        using var client = new HttpClient(new StubHandler(HttpStatusCode.OK, Repository));
        using var unloaded = new CancellationTokenSource();
        await unloaded.CancelAsync();

        Assert.Null(await PluginUpdateCheck.FindNewerVersionAsync(client, new Version(0, 6, 0, 0), unloaded.Token));
    }

    [Fact]
    public void Display_ShowsTheThreePartReleaseNumber()
        => Assert.Equal("0.6.1", PluginUpdateCheck.Display(new Version(0, 6, 1, 0)));

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body = string.Empty;
        private readonly Exception? _failure;

        public StubHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        public StubHandler(Exception failure) => _failure = failure;

        public string? RequestedUrl { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestedUrl = request.RequestUri?.ToString();
            return _failure is not null
                ? Task.FromException<HttpResponseMessage>(_failure)
                : Task.FromResult(new HttpResponseMessage(_status) { Content = new StringContent(_body) });
        }
    }
}

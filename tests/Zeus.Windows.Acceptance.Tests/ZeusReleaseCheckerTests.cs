using System.Net;
using System.Net.Http;
using System.Text;
using Zeus.Desktop;

namespace Zeus.Windows.Acceptance.Tests;

public sealed class ZeusReleaseCheckerTests
{
    [Fact]
    public void AutomaticCheckIsOptInAndThrottledForTwentyFourHours()
    {
        var now = new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

        Assert.False(MainWindow.ShouldRunAutomaticZeusUpdateCheck(false, null, now));
        Assert.True(MainWindow.ShouldRunAutomaticZeusUpdateCheck(true, null, now));
        Assert.False(MainWindow.ShouldRunAutomaticZeusUpdateCheck(true, now.AddHours(-23), now));
        Assert.True(MainWindow.ShouldRunAutomaticZeusUpdateCheck(true, now.AddHours(-24), now));
        Assert.False(MainWindow.ShouldRunAutomaticZeusUpdateCheck(true, now.AddHours(1), now));
    }

    [Fact]
    public async Task ReportsNewerStableVersionAndOnlyOfficialReleasePage()
    {
        var checker = new ZeusReleaseChecker(new StubHandler(HttpStatusCode.OK,
            """{"tag_name":"v2.0.0","html_url":"https://github.com/ggabedesing/zeus/releases/tag/v2.0.0","prerelease":false}"""));

        var result = await checker.CheckAsync("1.2.0+abc123", CancellationToken.None);

        Assert.Contains("Há uma versão mais recente: v2.0.0", result.Summary);
        Assert.Equal("github.com", result.ReleasePage?.Host);
    }

    [Fact]
    public async Task DoesNotReportCurrentWhenNoStableReleaseExists()
    {
        var checker = new ZeusReleaseChecker(new StubHandler(HttpStatusCode.NotFound, "{}"));

        var result = await checker.CheckAsync("1.2.0", CancellationToken.None);

        Assert.Contains("Ainda não há uma versão estável publicada", result.Summary);
        Assert.Equal("https://github.com/ggabedesing/zeus/releases", result.ReleasePage?.AbsoluteUri);
    }

    [Fact]
    public async Task RejectsNonOfficialReleaseLink()
    {
        var checker = new ZeusReleaseChecker(new StubHandler(HttpStatusCode.OK,
            """{"tag_name":"v2.0.0","html_url":"https://example.org/download","prerelease":false}"""));

        var result = await checker.CheckAsync("1.2.0", CancellationToken.None);

        Assert.Equal("github.com", result.ReleasePage?.Host);
        Assert.StartsWith("https://github.com/ggabedesing/zeus/releases", result.ReleasePage?.AbsoluteUri);
    }

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("https://api.github.com/repos/ggabedesing/zeus/releases/latest", request.RequestUri?.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}

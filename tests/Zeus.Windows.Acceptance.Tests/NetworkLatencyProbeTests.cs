using System.Net.NetworkInformation;
using Zeus.Windows;

namespace Zeus.Windows.Acceptance.Tests;

public sealed class NetworkLatencyProbeTests
{
    [Theory]
    [InlineData("  127.0.0.1  ", "127.0.0.1")]
    [InlineData("zeus.local", "zeus.local")]
    [InlineData("2001:db8::1", "2001:db8::1")]
    public void ValidateTarget_accepts_ip_or_host_and_trims(string value, string expected)
    {
        Assert.Equal(expected, NetworkLatencyProbe.ValidateTarget(value));
    }

    [Theory]
    [InlineData("")]
    [InlineData("https://example.com")]
    [InlineData("example.com/path")]
    [InlineData("bad host")]
    public void ValidateTarget_rejects_url_path_and_invalid_input(string value)
    {
        Assert.Throws<ArgumentException>(() => NetworkLatencyProbe.ValidateTarget(value));
    }

    [Fact]
    public void Result_aggregates_only_successful_icmp_replies()
    {
        var result = new NetworkLatencyResult("router.local", "192.0.2.1", DateTimeOffset.UtcNow,
        [
            new(1, IPStatus.Success.ToString(), 12),
            new(2, IPStatus.TimedOut.ToString(), null),
            new(3, IPStatus.Success.ToString(), 18),
            new(4, IPStatus.DestinationHostUnreachable.ToString(), null)
        ]);

        Assert.Equal(2, result.Replies);
        Assert.Equal(1, result.NoReplies);
        Assert.Equal(12, result.MinimumMilliseconds);
        Assert.Equal(15, result.AverageMilliseconds);
        Assert.Equal(18, result.MaximumMilliseconds);
    }

    [Fact]
    public void Result_keeps_latency_unavailable_when_there_are_no_successful_replies()
    {
        var result = new NetworkLatencyResult("router.local", "192.0.2.1", DateTimeOffset.UtcNow,
        [new(1, IPStatus.TimedOut.ToString(), null)]);

        Assert.Null(result.MinimumMilliseconds);
        Assert.Null(result.AverageMilliseconds);
        Assert.Null(result.MaximumMilliseconds);
    }
}

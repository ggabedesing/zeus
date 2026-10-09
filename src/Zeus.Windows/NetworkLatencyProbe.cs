using System.Net;
using System.Net.NetworkInformation;

namespace Zeus.Windows;

public sealed record NetworkPingSample(int Attempt, string Status, long? RoundtripMilliseconds);

public sealed record NetworkLatencyResult(
    string Target,
    string Address,
    DateTimeOffset CheckedAt,
    IReadOnlyList<NetworkPingSample> Samples)
{
    public int AttemptCount => Samples.Count;
    public int Replies => Samples.Count(sample => sample.Status == IPStatus.Success.ToString());
    public int NoReplyCount => AttemptCount - Replies;
    public int NoReplies => Samples.Count(sample => sample.Status == IPStatus.TimedOut.ToString());
    public double? NoReplyPercent => AttemptCount == 0 ? null : NoReplyCount / (double)AttemptCount * 100;
    public double? TimeoutPercent => AttemptCount == 0 ? null : NoReplies / (double)AttemptCount * 100;
    public long? MinimumMilliseconds => SuccessfulTimes() is { Length: > 0 } times ? times.Min() : null;
    public double? AverageMilliseconds => SuccessfulTimes() is { Length: > 0 } times ? times.Average() : null;
    public long? MaximumMilliseconds => SuccessfulTimes() is { Length: > 0 } times ? times.Max() : null;

    private long[] SuccessfulTimes() => Samples
        .Where(sample => sample.Status == IPStatus.Success.ToString() && sample.RoundtripMilliseconds.HasValue)
        .Select(sample => sample.RoundtripMilliseconds!.Value)
        .ToArray();
}

public sealed class NetworkLatencyProbe
{
    private static readonly TimeSpan DnsTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PingTimeout = TimeSpan.FromSeconds(1);
    private const int Attempts = 5;

    public async Task<NetworkLatencyResult> MeasureAsync(string target, CancellationToken cancellationToken = default)
    {
        target = ValidateTarget(target);
        var address = await ResolveAsync(target, cancellationToken).ConfigureAwait(false);
        var samples = new List<NetworkPingSample>(Attempts);

        using var ping = new Ping();
        for (var attempt = 1; attempt <= Attempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var reply = await ping.SendPingAsync(address, PingTimeout, new byte[32], new PingOptions(64, true), cancellationToken)
                    .ConfigureAwait(false);
                samples.Add(new(attempt, reply.Status.ToString(), reply.Status == IPStatus.Success ? reply.RoundtripTime : null));
            }
            catch (PingException error)
            {
                samples.Add(new(attempt, $"Erro ICMP: {error.InnerException?.GetType().Name ?? error.GetType().Name}", null));
            }
        }

        return new(target, address.ToString(), DateTimeOffset.UtcNow, samples);
    }

    public static string ValidateTarget(string target)
    {
        target = target?.Trim() ?? string.Empty;
        if (target.Length is 0 or > 253 || target.Any(char.IsWhiteSpace) || target.Any(char.IsControl) ||
            target.Contains('/') || target.Contains('\\') || target.Contains("://", StringComparison.Ordinal))
            throw new ArgumentException("Informe um endereço IP ou nome de host válido, sem URL ou caminho.", nameof(target));

        if (!IPAddress.TryParse(target, out _) && Uri.CheckHostName(target) != UriHostNameType.Dns)
            throw new ArgumentException("Informe um endereço IP ou nome de host válido.", nameof(target));

        return target;
    }

    private static async Task<IPAddress> ResolveAsync(string target, CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(target, out var literal)) return literal;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(DnsTimeout);
        IPAddress[] addresses;
        try { addresses = await Dns.GetHostAddressesAsync(target, timeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("A resolução do nome demorou mais de cinco segundos.");
        }

        return addresses.FirstOrDefault(address => address.AddressFamily is System.Net.Sockets.AddressFamily.InterNetwork or System.Net.Sockets.AddressFamily.InterNetworkV6)
            ?? throw new InvalidOperationException("O nome não resolveu para um endereço IP.");
    }
}

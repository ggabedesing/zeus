using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Zeus.Windows;

namespace Zeus.Hardware.Tests;

public sealed class ObsReadOnlyClientTests
{
    [Fact]
    public async Task AuthenticatedFragmentedConnectionIsReadOnlyAndPersistent()
    {
        await using var server = new ObsFixture { Password = "fixture-only-password", Fragment = true };
        await using var client = new ObsReadOnlyClient();
        var first = await client.CaptureAsync(server.Port, server.Password);
        var second = await client.CaptureAsync(server.Port, server.Password);
        Assert.Equal(ObsObservationState.Complete, first.State);
        Assert.True(first.Streaming); Assert.False(first.Recording);
        Assert.False(first.RecordingPaused);
        Assert.Equal(first.ConnectionId, second.ConnectionId);
        Assert.Null(first.RenderSkippedPercent);
        Assert.Equal(1d, second.RenderSkippedPercent);
        Assert.Equal(2d, second.OutputSkippedPercent);
        Assert.Equal(3d, second.StreamSkippedPercent);
        Assert.Equal(first.StatsReadAt, second.PreviousStatsReadAt);
        Assert.Equal(first.StreamReadAt, second.PreviousStreamReadAt);
        Assert.True(second.StatsReadAt > second.PreviousStatsReadAt);
        Assert.True(second.StreamReadAt > second.PreviousStreamReadAt);
        Assert.NotNull(second.RecordReadAt);
        Assert.Equal(1, server.Connections);
        Assert.Equal(0, server.EventSubscriptions);
        Assert.Equal(new[] { "GetVersion", "GetStats", "GetStreamStatus", "GetRecordStatus", "GetStats", "GetStreamStatus", "GetRecordStatus" }, server.Requests);
        var exported = JsonSerializer.Serialize(second);
        Assert.DoesNotContain(server.Password, exported);
        Assert.DoesNotContain("fixture-sensitive-recording-path", exported);
    }

    [Fact]
    public async Task UnsupportedRequestsRemainUnknownAndAreNotSent()
    {
        await using var server = new ObsFixture { Supported = ["GetStats"] };
        await using var client = new ObsReadOnlyClient();
        var result = await client.CaptureAsync(server.Port, null);
        Assert.Equal(ObsObservationState.Partial, result.State);
        Assert.NotNull(result.ActiveFps); Assert.Null(result.Streaming); Assert.Null(result.Recording);
        Assert.Equal(new[] { "GetVersion", "GetStats" }, server.Requests);
    }

    [Fact]
    public async Task MissingNumericFieldsNeverBecomeZero()
    {
        await using var server = new ObsFixture { Stats = _ => new { activeFps = 60 } };
        await using var client = new ObsReadOnlyClient();
        var result = await client.CaptureAsync(server.Port, null);
        Assert.Equal(ObsObservationState.Partial, result.State);
        Assert.Null(result.CpuUsage); Assert.Null(result.RenderTotalFrames);
    }

    [Fact]
    public async Task RecordingPauseIsActualEvidenceAndMissingPauseRemainsUnknown()
    {
        await using var server = new ObsFixture { Record = n => n == 1
            ? new { outputActive = true, outputPaused = true }
            : (object)new { outputActive = true } };
        await using var client = new ObsReadOnlyClient();
        var first = await client.CaptureAsync(server.Port, null);
        Assert.Equal(ObsObservationState.Complete, first.State);
        Assert.True(first.Recording); Assert.True(first.RecordingPaused);
        var second = await client.CaptureAsync(server.Port, null);
        Assert.Equal(ObsObservationState.Partial, second.State);
        Assert.True(second.Recording); Assert.Null(second.RecordingPaused);
        Assert.Null(second.OutputSkippedPercent);
    }

    [Fact]
    public async Task ObservedRecordingPauseTransitionResetsOutputDeltaOnly()
    {
        await using var server = new ObsFixture { Record = n => new { outputActive = true, outputPaused = n > 1 } };
        await using var client = new ObsReadOnlyClient();
        await client.CaptureAsync(server.Port, null);
        var second = await client.CaptureAsync(server.Port, null);
        Assert.Equal(ObsObservationState.Complete, second.State);
        Assert.True(second.RecordingPaused);
        Assert.Equal(1d, second.RenderSkippedPercent); Assert.Equal(3d, second.StreamSkippedPercent);
        Assert.Null(second.OutputSkippedPercent);
    }

    [Fact]
    public async Task RequestFailureIsSanitizedAndOtherEvidenceSurvives()
    {
        await using var server = new ObsFixture { FailedRequest = "GetStreamStatus" };
        await using var client = new ObsReadOnlyClient();
        var result = await client.CaptureAsync(server.Port, null);
        Assert.Equal(ObsObservationState.Partial, result.State);
        Assert.NotNull(result.ActiveFps); Assert.Null(result.Streaming); Assert.False(result.Recording);
        Assert.DoesNotContain("server-comment-secret", JsonSerializer.Serialize(result));
    }

    [Theory]
    [InlineData("requestId")]
    [InlineData("requestType")]
    public async Task MismatchedCorrelationIsRejected(string field)
    {
        await using var server = new ObsFixture { Mismatch = field };
        await using var client = new ObsReadOnlyClient();
        var result = await client.CaptureAsync(server.Port, null);
        Assert.Equal(ObsObservationState.Failed, result.State);
        Assert.Null(result.Streaming);
    }

    [Fact]
    public async Task TimeoutPreservesPriorValidatedRequestAndReconnects()
    {
        await using var server = new ObsFixture { StallRequest = "GetStreamStatus" };
        await using var client = new ObsReadOnlyClient(TimeSpan.FromMilliseconds(500));
        var first = await client.CaptureAsync(server.Port, null);
        Assert.Equal(ObsObservationState.TimedOut, first.State);
        Assert.NotNull(first.ActiveFps); Assert.Null(first.Streaming);
        server.StallRequest = null;
        var second = await client.CaptureAsync(server.Port, null);
        Assert.Equal(ObsObservationState.Complete, second.State);
        Assert.NotEqual(first.ConnectionId, second.ConnectionId);
        Assert.Null(second.RenderSkippedPercent);
    }

    [Fact]
    public async Task UserCancellationPropagatesAndDropsConnection()
    {
        await using var server = new ObsFixture { StallRequest = "GetStreamStatus" };
        await using var client = new ObsReadOnlyClient();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.CaptureAsync(server.Port, null, cancellation.Token));
        server.StallRequest = null;
        var next = await client.CaptureAsync(server.Port, null);
        Assert.Equal(ObsObservationState.Complete, next.State); Assert.Null(next.RenderSkippedPercent);
    }

    [Fact]
    public async Task CounterResetAndStreamRestartDoNotProducePercentages()
    {
        await using var server = new ObsFixture {
            Stats = n => Stats(n == 1 ? 100 : 50),
            Stream = n => Stream(n == 1 ? 100 : 50, n == 1 ? 1000 : 500) };
        await using var client = new ObsReadOnlyClient();
        await client.CaptureAsync(server.Port, null);
        var second = await client.CaptureAsync(server.Port, null);
        Assert.Null(second.RenderSkippedPercent); Assert.Null(second.OutputSkippedPercent); Assert.Null(second.StreamSkippedPercent);
    }

    [Fact]
    public async Task InactiveStreamDoesNotGenerateNetworkIntervalPercentage()
    {
        await using var server = new ObsFixture { Stream = n => new { outputActive = false, outputReconnecting = false,
            outputSkippedFrames = n, outputTotalFrames = 100 * n, outputDuration = 1000 * n } };
        await using var client = new ObsReadOnlyClient();
        await client.CaptureAsync(server.Port, null);
        var second = await client.CaptureAsync(server.Port, null);
        Assert.Null(second.StreamSkippedPercent);
    }

    [Fact]
    public async Task ObservedOutputToggleResetsOutputDeltaButRetainsRenderEvidence()
    {
        await using var server = new ObsFixture { Stream = n => new { outputActive = n == 1, outputReconnecting = false,
            outputSkippedFrames = 3 * n, outputTotalFrames = 100 * n, outputDuration = n == 1 ? 1000 : 0 } };
        await using var client = new ObsReadOnlyClient();
        var first = await client.CaptureAsync(server.Port, null);
        var second = await client.CaptureAsync(server.Port, null);
        Assert.Equal(1d, second.RenderSkippedPercent);
        Assert.Equal(first.StatsReadAt, second.PreviousStatsReadAt);
        Assert.Null(second.OutputSkippedPercent); Assert.Null(second.StreamSkippedPercent);
        Assert.Null(second.PreviousStreamReadAt);
    }

    [Fact]
    public async Task DisconnectStartsNewConnectionAndDeltaBaseline()
    {
        await using var server = new ObsFixture();
        await using var client = new ObsReadOnlyClient();
        var first = await client.CaptureAsync(server.Port, null);
        await client.DisconnectAsync();
        var next = await client.CaptureAsync(server.Port, null);
        Assert.NotEqual(first.ConnectionId, next.ConnectionId); Assert.Null(next.RenderSkippedPercent);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("oversize")]
    [InlineData("negative")]
    [InlineData("binary")]
    [InlineData("counterPair")]
    [InlineData("falseSuccessCode")]
    public async Task InvalidFramesAreRejectedWithinBoundary(string mode)
    {
        await using var server = new ObsFixture { Invalid = mode };
        await using var client = new ObsReadOnlyClient();
        var result = await client.CaptureAsync(server.Port, null);
        Assert.Equal(ObsObservationState.Failed, result.State);
        Assert.Null(result.ActiveFps);
    }

    [Theory]
    [InlineData("missingAuthenticationChallenge")]
    [InlineData("unexpectedEvent")]
    public async Task InvalidHandshakeIsSanitized(string mode)
    {
        await using var server = new ObsFixture { Invalid = mode };
        await using var client = new ObsReadOnlyClient();
        var result = await client.CaptureAsync(server.Port, null);
        Assert.Equal(ObsObservationState.Failed, result.State); Assert.Null(result.Streaming);
        Assert.Empty(server.Requests);
    }

    [Fact]
    public async Task InvalidUtf16CredentialIsRejectedWithoutConnecting()
    {
        await using var server = new ObsFixture();
        await using var client = new ObsReadOnlyClient();
        var result = await client.CaptureAsync(server.Port, "\ud800");
        Assert.Equal(ObsObservationState.Failed, result.State);
        Assert.Equal(0, server.Connections);
    }

    [Fact]
    public async Task ConcurrentCapturesAreSerialized()
    {
        await using var server = new ObsFixture();
        await using var client = new ObsReadOnlyClient();
        var observations = await Task.WhenAll(client.CaptureAsync(server.Port, null), client.CaptureAsync(server.Port, null));
        Assert.All(observations, o => Assert.Equal(ObsObservationState.Complete, o.State));
        Assert.Equal(observations[0].ConnectionId, observations[1].ConnectionId);
        Assert.Equal(1, server.Connections);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public async Task InvalidPortDoesNotConnect(int port)
    {
        await using var client = new ObsReadOnlyClient();
        var result = await client.CaptureAsync(port, null);
        Assert.Equal(ObsObservationState.Unavailable, result.State); Assert.Null(result.ConnectionId);
    }

    private static object Stats(int total) => new { activeFps = 60, cpuUsage = 2.5, memoryUsage = 400,
        averageFrameRenderTime = 1.2, renderSkippedFrames = total / 100, renderTotalFrames = total,
        outputSkippedFrames = 2 * total / 100, outputTotalFrames = total };
    private static object Stream(int total, int duration) => new { outputActive = true, outputReconnecting = false,
        outputSkippedFrames = 3 * total / 100, outputTotalFrames = total, outputDuration = duration };

    private sealed class ObsFixture : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _run;
        private readonly List<Task> _connections = [];
        private readonly List<string> _requests = [];
        private int _captures;
        public string? Password { get; init; }
        public bool Fragment { get; init; }
        public string[] Supported { get; init; } = ["GetStats", "GetStreamStatus", "GetRecordStatus"];
        public string? FailedRequest { get; init; }
        public string? Mismatch { get; init; }
        public string? StallRequest { get; set; }
        public string? Invalid { get; init; }
        public Func<int, object> Stats { get; init; } = n => ObsReadOnlyClientTests.Stats(n * 100);
        public Func<int, object> Stream { get; init; } = n => ObsReadOnlyClientTests.Stream(n * 100, n * 1000);
        public Func<int, object> Record { get; init; } = _ => new { outputActive = false, outputPaused = false, outputPath = "fixture-sensitive-recording-path" };
        public int Connections { get; private set; }
        public int EventSubscriptions { get; private set; } = -1;
        public string[] Requests { get { lock (_requests) return _requests.ToArray(); } }
        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
        public ObsFixture() { _listener.Start(); _run = AcceptAsync(); }

        private async Task AcceptAsync()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    var tcp = await _listener.AcceptTcpClientAsync(_stop.Token);
                    Connections++;
                    _connections.Add(ServeAsync(tcp));
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        }
        private async Task ServeAsync(TcpClient tcp)
        {
            using (tcp)
            try
            {
                var stream = tcp.GetStream();
                var header = new StringBuilder();
                var one = new byte[1];
                while (!header.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
                {
                    if (await stream.ReadAsync(one, _stop.Token) == 0 || header.Length > 8192) throw new IOException();
                    header.Append((char)one[0]);
                }
                var key = header.ToString().Split("\r\n").Single(l => l.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase)).Split(':', 2)[1].Trim();
                var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
                await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: {accept}\r\n\r\n"), _stop.Token);
                using var socket = WebSocket.CreateFromStream(stream, true, null, Timeout.InfiniteTimeSpan);
                object hello = Password is null ? new { op = 0, d = (object)new { rpcVersion = 1 } }
                    : new { op = 0, d = (object)new { rpcVersion = 1, authentication = new { salt = "fixture-salt", challenge = "fixture-challenge" } } };
                await SendAsync(socket, Invalid == "missingAuthenticationChallenge"
                    ? "{\"op\":0,\"d\":{\"rpcVersion\":1,\"authentication\":{\"salt\":\"s\"}}}"
                    : Invalid == "unexpectedEvent" ? "{\"op\":5,\"d\":{}}" : JsonSerializer.Serialize(hello));
                using var identify = await ReceiveAsync(socket);
                var identifyData = identify.RootElement.GetProperty("d");
                Assert.Equal(1, identify.RootElement.GetProperty("op").GetInt32());
                Assert.Equal(1, identifyData.GetProperty("rpcVersion").GetInt32());
                EventSubscriptions = identifyData.GetProperty("eventSubscriptions").GetInt32();
                if (Password is not null)
                {
                    var secret = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(Password + "fixture-salt")));
                    var auth = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(secret + "fixture-challenge")));
                    Assert.Equal(auth, identifyData.GetProperty("authentication").GetString());
                }
                await SendAsync(socket, "{\"op\":2,\"d\":{\"negotiatedRpcVersion\":1}}");
                while (!_stop.IsCancellationRequested)
                {
                    using var request = await ReceiveAsync(socket);
                    Assert.Equal(6, request.RootElement.GetProperty("op").GetInt32());
                    var d = request.RootElement.GetProperty("d");
                    var name = d.GetProperty("requestType").GetString()!;
                    Assert.False(d.TryGetProperty("requestData", out _));
                    Assert.Contains(name, new[] { "GetVersion", "GetStats", "GetStreamStatus", "GetRecordStatus" });
                    lock (_requests) _requests.Add(name);
                    if (name == StallRequest) await Task.Delay(Timeout.Infinite, _stop.Token);
                    if (name == "GetStats") Interlocked.Increment(ref _captures);
                    object payload = name switch {
                        "GetVersion" => new { availableRequests = Supported }, "GetStats" => Stats(_captures),
                        "GetStreamStatus" => Stream(_captures), _ => Record(_captures) };
                    var success = name != FailedRequest;
                    var response = JsonSerializer.Serialize(new { op = 7, d = new {
                        requestType = Mismatch == "requestType" ? "StartStream" : name,
                        requestId = Mismatch == "requestId" ? "wrong-id" : d.GetProperty("requestId").GetString(),
                        requestStatus = new { result = success, code = success ? 100 : 600, comment = "server-comment-secret" }, responseData = payload } });
                    if (name == "GetStats") response = Invalid switch {
                        "duplicate" => response.Replace("\"activeFps\":60", "\"activeFps\":60,\"activeFps\":0"),
                        "oversize" => new string('x', 65537),
                        "negative" => response.Replace("\"activeFps\":60", "\"activeFps\":-1"), _ => response };
                    if (name == "GetStats" && Invalid == "counterPair") response = response.Replace("\"renderSkippedFrames\":1", "\"renderSkippedFrames\":101");
                    if (name == "GetStats" && Invalid == "falseSuccessCode") response = response.Replace("\"result\":true", "\"result\":false");
                    await SendAsync(socket, response, name == "GetStats" && Invalid == "binary");
                }
            }
            catch (Exception e) when (e is WebSocketException or IOException or OperationCanceledException) { }
        }
        private async Task SendAsync(WebSocket socket, string text, bool binary = false)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            var type = binary ? WebSocketMessageType.Binary : WebSocketMessageType.Text;
            if (Fragment && bytes.Length > 1)
            {
                await socket.SendAsync(bytes.AsMemory(0, bytes.Length / 2), type, false, _stop.Token);
                await socket.SendAsync(bytes.AsMemory(bytes.Length / 2), type, true, _stop.Token);
            }
            else await socket.SendAsync(bytes.AsMemory(), type, true, _stop.Token);
        }
        private async Task<JsonDocument> ReceiveAsync(WebSocket socket)
        {
            using var data = new MemoryStream();
            var bytes = new byte[4096];
            WebSocketReceiveResult message;
            do
            {
                message = await socket.ReceiveAsync(bytes, _stop.Token);
                if (message.MessageType == WebSocketMessageType.Close) throw new IOException();
                data.Write(bytes, 0, message.Count);
            } while (!message.EndOfMessage);
            return JsonDocument.Parse(data.ToArray());
        }
        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            await _run;
            await Task.WhenAll(_connections);
            _listener.Stop(); _stop.Dispose();
        }
    }
}

using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Zeus.Windows;

/// <summary>Optional OBS protocol 5 reader. Its closed request allowlist cannot change OBS outputs or configuration.</summary>
public sealed class ObsReadOnlyClient : IAsyncDisposable
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
        { "GetVersion", "GetStats", "GetStreamStatus", "GetRecordStatus" };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeSpan _timeout;
    private ClientWebSocket? _socket;
    private Guid? _connectionId;
    private int _port;
    private byte[]? _passwordFingerprint;
    private HashSet<string>? _available;
    private ObsObservation? _previous;
    private bool _disposed;

    public ObsReadOnlyClient() : this(TimeSpan.FromSeconds(6)) { }
    internal ObsReadOnlyClient(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromSeconds(6)) throw new ArgumentOutOfRangeException(nameof(timeout));
        _timeout = timeout;
    }

    public async Task<ObsObservation> CaptureAsync(int port, string? password, CancellationToken cancellationToken = default)
    {
        var started = DateTimeOffset.UtcNow;
        var result = new ObsObservation(started, started, null, ObsObservationState.Unavailable,
            "OBS local indisponível. Verifique a conexão autorizada nas configurações do OBS.");
        cancellationToken.ThrowIfCancellationRequested();
        if (port is < 1 or > 65535 || (password?.Length ?? 0) > 1024)
            return result with { FinishedAt = DateTimeOffset.UtcNow, Summary = "Porta ou credencial local do OBS inválida." };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_timeout);
        var ownsGate = false;
        try
        {
            await _gate.WaitAsync(deadline.Token).ConfigureAwait(false);
            ownsGate = true;
            ObjectDisposedException.ThrowIf(_disposed, this);
            var passwordBytes = StrictUtf8.GetBytes(password ?? "");
            byte[] fingerprint;
            try { fingerprint = SHA256.HashData(passwordBytes); }
            finally { CryptographicOperations.ZeroMemory(passwordBytes); }
            try
            {
                if (_socket?.State != WebSocketState.Open || _port != port || _passwordFingerprint is null ||
                    !CryptographicOperations.FixedTimeEquals(fingerprint, _passwordFingerprint)) Reset();
                if (_socket is null)
                {
                    _socket = new ClientWebSocket();
                    _socket.Options.Proxy = null;
                    _socket.Options.KeepAliveInterval = TimeSpan.Zero;
                    await _socket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), deadline.Token).ConfigureAwait(false);
                    await IdentifyAsync(password, deadline.Token).ConfigureAwait(false);
                    _connectionId = Guid.NewGuid(); _port = port;
                    _passwordFingerprint = fingerprint.ToArray();
                    var version = await RequestAsync("GetVersion", deadline.Token).ConfigureAwait(false);
                    if (version is null || !version.Value.TryGetProperty("availableRequests", out var requests) ||
                        requests.ValueKind != JsonValueKind.Array || requests.GetArrayLength() > 1024) throw new InvalidDataException();
                    _available = new(StringComparer.Ordinal);
                    foreach (var item in requests.EnumerateArray())
                    {
                        var name = Text(item, 128);
                        if (Allowed.Contains(name)) _available.Add(name);
                    }
                }
            }
            finally { CryptographicOperations.ZeroMemory(fingerprint); }
            result = result with { ConnectionId = _connectionId };
            var stats = await SupportedRequestAsync("GetStats", deadline.Token).ConfigureAwait(false);
            if (stats is { } s)
            {
                var candidate = result with {
                    ActiveFps = Number(s, "activeFps"), CpuUsage = Number(s, "cpuUsage", 100),
                    MemoryMegabytes = Number(s, "memoryUsage"), AverageFrameRenderMilliseconds = Number(s, "averageFrameRenderTime"),
                    RenderSkippedFrames = Counter(s, "renderSkippedFrames"), RenderTotalFrames = Counter(s, "renderTotalFrames"),
                    OutputSkippedFrames = Counter(s, "outputSkippedFrames"), OutputTotalFrames = Counter(s, "outputTotalFrames") };
                ValidateCounterPair(candidate.RenderSkippedFrames, candidate.RenderTotalFrames);
                ValidateCounterPair(candidate.OutputSkippedFrames, candidate.OutputTotalFrames);
                result = candidate with { StatsReadAt = DateTimeOffset.UtcNow };
            }
            var stream = await SupportedRequestAsync("GetStreamStatus", deadline.Token).ConfigureAwait(false);
            if (stream is { } t)
            {
                var candidate = result with { Streaming = Boolean(t, "outputActive"), Reconnecting = Boolean(t, "outputReconnecting"),
                    StreamSkippedFrames = Counter(t, "outputSkippedFrames"), StreamTotalFrames = Counter(t, "outputTotalFrames"),
                    StreamDurationMilliseconds = Counter(t, "outputDuration") };
                ValidateCounterPair(candidate.StreamSkippedFrames, candidate.StreamTotalFrames);
                result = candidate with { StreamReadAt = DateTimeOffset.UtcNow };
            }
            var record = await SupportedRequestAsync("GetRecordStatus", deadline.Token).ConfigureAwait(false);
            if (record is { } r) result = result with { Recording = Boolean(r, "outputActive"),
                RecordingPaused = Boolean(r, "outputPaused"), RecordReadAt = DateTimeOffset.UtcNow };
            var complete = result.ActiveFps.HasValue && result.CpuUsage.HasValue && result.MemoryMegabytes.HasValue &&
                result.AverageFrameRenderMilliseconds.HasValue && result.RenderSkippedFrames.HasValue && result.RenderTotalFrames.HasValue &&
                result.OutputSkippedFrames.HasValue && result.OutputTotalFrames.HasValue && result.Streaming.HasValue &&
                result.Reconnecting.HasValue && result.StreamSkippedFrames.HasValue && result.StreamTotalFrames.HasValue &&
                result.StreamDurationMilliseconds.HasValue && result.Recording.HasValue && result.RecordingPaused.HasValue;
            result = result with { State = complete ? ObsObservationState.Complete : ObsObservationState.Partial,
                Summary = complete ? "Dados informados pelo OBS local. Codificador indisponível nesta leitura. Percentuais exigem duas amostras compatíveis."
                    : "Leitura parcial do OBS local; campos ausentes permanecem desconhecidos. Codificador indisponível." };
            result = WithDeltas(result, _previous);
            result = result with { FinishedAt = DateTimeOffset.UtcNow };
            _previous = result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (ownsGate) Reset();
            throw;
        }
        catch (OperationCanceledException)
        {
            if (ownsGate) Reset();
            result = result with { State = ObsObservationState.TimedOut, Summary = "A leitura do OBS excedeu o limite de tempo. Campos não lidos permanecem desconhecidos." };
        }
        catch (Exception e) when (e is WebSocketException or IOException or JsonException or InvalidOperationException or ObjectDisposedException or EncoderFallbackException)
        {
            if (ownsGate) Reset();
            result = result with { State = e is WebSocketException ? ObsObservationState.Unavailable : ObsObservationState.Failed,
                Summary = "Não foi possível validar a leitura local do OBS. Verifique porta, autenticação e compatibilidade; dados ausentes permanecem desconhecidos." };
        }
        catch (InvalidDataException)
        {
            if (ownsGate) Reset();
            result = result with { State = ObsObservationState.Failed, Summary = "Resposta do OBS incompatível ou inválida; dados ausentes permanecem desconhecidos." };
        }
        finally { if (ownsGate) _gate.Release(); }
        return result with { FinishedAt = DateTimeOffset.UtcNow };
    }

    private async Task IdentifyAsync(string? password, CancellationToken token)
    {
        using var hello = await ReceiveAsync(token).ConfigureAwait(false);
        var data = Payload(hello.RootElement, 0);
        if (!data.TryGetProperty("rpcVersion", out var rpc) || !rpc.TryGetInt32(out var version) || version < 1) throw new InvalidDataException();
        string? auth = null;
        if (data.TryGetProperty("authentication", out var authentication))
        {
            if (authentication.ValueKind != JsonValueKind.Object) throw new InvalidDataException();
            if (!authentication.TryGetProperty("salt", out var saltValue) || !authentication.TryGetProperty("challenge", out var challengeValue))
                throw new InvalidDataException();
            var salt = Text(saltValue, 1024);
            var challenge = Text(challengeValue, 1024);
            var secretInput = StrictUtf8.GetBytes((password ?? "") + salt);
            byte[] secretBytes;
            try { secretBytes = SHA256.HashData(secretInput); }
            finally { CryptographicOperations.ZeroMemory(secretInput); }
            try
            {
                var authInput = StrictUtf8.GetBytes(Convert.ToBase64String(secretBytes) + challenge);
                byte[] authBytes;
                try { authBytes = SHA256.HashData(authInput); }
                finally { CryptographicOperations.ZeroMemory(authInput); }
                try { auth = Convert.ToBase64String(authBytes); }
                finally { CryptographicOperations.ZeroMemory(authBytes); }
            }
            finally { CryptographicOperations.ZeroMemory(secretBytes); }
        }
        var identify = auth is null
            ? JsonSerializer.SerializeToUtf8Bytes(new { op = 1, d = new { rpcVersion = 1, eventSubscriptions = 0 } })
            : JsonSerializer.SerializeToUtf8Bytes(new { op = 1, d = new { rpcVersion = 1, eventSubscriptions = 0, authentication = auth } });
        try { await _socket!.SendAsync(identify, WebSocketMessageType.Text, true, token).ConfigureAwait(false); }
        finally { CryptographicOperations.ZeroMemory(identify); }
        using var identified = await ReceiveAsync(token).ConfigureAwait(false);
        var accepted = Payload(identified.RootElement, 2);
        if (!accepted.TryGetProperty("negotiatedRpcVersion", out var negotiated) || !negotiated.TryGetInt32(out var selected) || selected != 1)
            throw new InvalidDataException();
    }

    private Task<JsonElement?> SupportedRequestAsync(string name, CancellationToken token) =>
        _available?.Contains(name) == true ? RequestAsync(name, token) : Task.FromResult<JsonElement?>(null);

    private async Task<JsonElement?> RequestAsync(string name, CancellationToken token)
    {
        if (!Allowed.Contains(name)) throw new InvalidOperationException();
        var id = Guid.NewGuid().ToString("N");
        await _socket!.SendAsync(JsonSerializer.SerializeToUtf8Bytes(new { op = 6, d = new { requestType = name, requestId = id } }),
            WebSocketMessageType.Text, true, token).ConfigureAwait(false);
        using var response = await ReceiveAsync(token).ConfigureAwait(false);
        var data = Payload(response.RootElement, 7);
        if (!data.TryGetProperty("requestId", out var responseId) || Text(responseId, 64) != id ||
            !data.TryGetProperty("requestType", out var type) || Text(type, 128) != name ||
            !data.TryGetProperty("requestStatus", out var status) || status.ValueKind != JsonValueKind.Object ||
            !status.TryGetProperty("result", out var success) || success.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
            !status.TryGetProperty("code", out var code) || !code.TryGetInt32(out var statusCode) || statusCode < 0)
            throw new InvalidDataException();
        if (!success.GetBoolean())
        {
            if (statusCode == 100) throw new InvalidDataException();
            return null; // Never propagate the server's comment: it may contain sensitive content.
        }
        if (statusCode != 100 || !data.TryGetProperty("responseData", out var payload) || payload.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException();
        return payload.Clone();
    }

    private async Task<JsonDocument> ReceiveAsync(CancellationToken token)
    {
        using var data = new MemoryStream();
        var buffer = new byte[4096];
        WebSocketReceiveResult received;
        do
        {
            received = await _socket!.ReceiveAsync(buffer, token).ConfigureAwait(false);
            if (received.MessageType != WebSocketMessageType.Text || data.Length + received.Count > 65536) throw new InvalidDataException();
            data.Write(buffer, 0, received.Count);
        } while (!received.EndOfMessage);
        var document = JsonDocument.Parse(data.ToArray(), new JsonDocumentOptions { MaxDepth = 16 });
        try { ValidateUnique(document.RootElement); return document; }
        catch { document.Dispose(); throw; }
    }

    private static void ValidateUnique(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException();
                ValidateUnique(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) ValidateUnique(item);
    }
    private static JsonElement Payload(JsonElement root, int op)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("op", out var code) || !code.TryGetInt32(out var actual) || actual != op ||
            !root.TryGetProperty("d", out var data) || data.ValueKind != JsonValueKind.Object) throw new InvalidDataException();
        return data;
    }
    private static string Text(JsonElement value, int maxLength)
    {
        if (value.ValueKind != JsonValueKind.String || value.GetString() is not { } text || text.Length > maxLength) throw new InvalidDataException();
        return text;
    }
    private static bool? Boolean(JsonElement value, string name)
    {
        if (!value.TryGetProperty(name, out var field) || field.ValueKind == JsonValueKind.Null) return null;
        return field.ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => throw new InvalidDataException() };
    }
    private static double? Number(JsonElement value, string name, double maximum = double.MaxValue)
    {
        if (!value.TryGetProperty(name, out var field) || field.ValueKind == JsonValueKind.Null) return null;
        if (!field.TryGetDouble(out var number) || !double.IsFinite(number) || number < 0 || number > maximum) throw new InvalidDataException();
        return number;
    }
    private static long? Counter(JsonElement value, string name)
    {
        if (!value.TryGetProperty(name, out var field) || field.ValueKind == JsonValueKind.Null) return null;
        if (!field.TryGetInt64(out var number) || number < 0) throw new InvalidDataException();
        return number;
    }
    private static void ValidateCounterPair(long? skipped, long? total)
    {
        if (skipped.HasValue && total.HasValue && skipped > total) throw new InvalidDataException();
    }
    private static ObsObservation WithDeltas(ObsObservation current, ObsObservation? previous)
    {
        if (previous is null || current.ConnectionId is null || current.ConnectionId != previous.ConnectionId) return current;
        var streamCompatible = current.Streaming == true && previous.Streaming == true &&
            current.StreamDurationMilliseconds > previous.StreamDurationMilliseconds;
        var statsInterval = current.StatsReadAt.HasValue && previous.StatsReadAt.HasValue && current.StatsReadAt > previous.StatsReadAt;
        var streamInterval = current.StreamReadAt.HasValue && previous.StreamReadAt.HasValue && current.StreamReadAt > previous.StreamReadAt;
        // GetStats describes the aggregate output thread. Reject observed output transitions; an unobserved
        // stop/restart between requests cannot be conclusively excluded without subscribing to output events.
        var outputCompatible = current.Streaming.HasValue && current.Recording.HasValue && current.RecordingPaused.HasValue &&
            current.Streaming == previous.Streaming && current.Recording == previous.Recording &&
            current.RecordingPaused == previous.RecordingPaused &&
            (current.Streaming == true || current.Recording == true) && (current.Streaming != true || streamCompatible);
        var renderDelta = statsInterval ? Delta(current.RenderSkippedFrames, current.RenderTotalFrames, previous.RenderSkippedFrames, previous.RenderTotalFrames) : null;
        var outputDelta = statsInterval && outputCompatible ? Delta(current.OutputSkippedFrames, current.OutputTotalFrames, previous.OutputSkippedFrames, previous.OutputTotalFrames) : null;
        var streamDelta = streamInterval && streamCompatible ? Delta(current.StreamSkippedFrames, current.StreamTotalFrames, previous.StreamSkippedFrames, previous.StreamTotalFrames) : null;
        return current with {
            RenderSkippedPercent = renderDelta, OutputSkippedPercent = outputDelta, StreamSkippedPercent = streamDelta,
            PreviousStatsReadAt = renderDelta.HasValue || outputDelta.HasValue ? previous.StatsReadAt : null,
            PreviousStreamReadAt = streamDelta.HasValue ? previous.StreamReadAt : null };
    }
    private static double? Delta(long? skipped, long? total, long? oldSkipped, long? oldTotal)
    {
        if (skipped is null || total is null || oldSkipped is null || oldTotal is null ||
            skipped > total || oldSkipped > oldTotal || skipped < oldSkipped || total <= oldTotal) return null;
        var count = skipped.Value - oldSkipped.Value;
        var frames = total.Value - oldTotal.Value;
        return count <= frames ? 100d * count / frames : null;
    }
    private void Reset()
    {
        _socket?.Abort(); _socket?.Dispose(); _socket = null;
        _connectionId = null; _available = null; _previous = null;
        if (_passwordFingerprint is not null) CryptographicOperations.ZeroMemory(_passwordFingerprint);
        _passwordFingerprint = null;
    }
    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { Reset(); } finally { _gate.Release(); }
    }
    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try { _disposed = true; Reset(); } finally { _gate.Release(); }
    }
}

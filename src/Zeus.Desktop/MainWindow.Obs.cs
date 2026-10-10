using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using Zeus.Windows;

namespace Zeus.Desktop;

public partial class MainWindow
{
    private readonly ObsReadOnlyClient _obsClient = new();
    private readonly ObsCredentialStore _obsCredentials;
    private bool _observeObs, _observeObsDraft;
    private int _obsPort = 4455;
    private string _obsPortDraft = "4455";
    private string _obsConnectionSummary = "Conexão opcional desativada. Processo ou engine GPU não confirmam transmissão.";

    public bool ObserveObs { get => _observeObsDraft; set { _observeObsDraft = value; Notify(); } }
    public string ObsPortDraft { get => _obsPortDraft; set { _obsPortDraft = value; Notify(); } }
    public string ObsConnectionSummary { get => _obsConnectionSummary; private set { _obsConnectionSummary = value; Notify(); } }
    public ObservableCollection<DeviceRow> ObsRows { get; } = [];

    private async Task<PerformanceObservation> SamplePerformanceWithObsAsync(TimeSpan duration, CancellationToken token)
    {
        var observation = await _performanceProbe.SampleAsync(duration, token);
        return observation with { Obs = await ReadObsAsync(token) };
    }

    private async Task<ObsObservation> ReadObsAsync(CancellationToken token)
    {
        if (!_observeObs) return ObsObservation.Disabled();
        string? password;
        try { password = _obsCredentials.Load(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            var now = DateTimeOffset.UtcNow;
            return new(now, now, null, ObsObservationState.Unavailable, Summary: "A senha local do OBS não pôde ser lida. Informe e salve novamente; dados indisponíveis.");
        }
        return await _obsClient.CaptureAsync(_obsPort, password, token);
    }

    private async void SaveObsConnection_Click(object sender, RoutedEventArgs e)
    {
        if (!CanChooseActions) return;
        if (!int.TryParse(ObsPortDraft, NumberStyles.None, CultureInfo.InvariantCulture, out var port) || port is < 1 or > 65535)
        {
            ObsConnectionSummary = "Informe uma porta inteira entre 1 e 65535. O endereço será sempre 127.0.0.1.";
            return;
        }
        await RunOperationAsync("Salvando conexão local do OBS", "Guardando somente as preferências desta conexão.", async _ =>
        {
            try
            {
                if (ObsPasswordBox.Password.Length > 0) _obsCredentials.Save(ObsPasswordBox.Password);
                await _preferenceLock.WaitAsync();
                try
                {
                    await _storage.SavePreferencesAsync(CurrentPreferences() with { ObserveObs = ObserveObs, ObsPort = port });
                    _observeObs = ObserveObs;
                    _obsPort = port;
                }
                finally { _preferenceLock.Release(); }
                await _obsClient.DisconnectAsync();
                ObsRows.Clear();
                ObsConnectionSummary = _observeObs
                    ? "Conexão local ativada. Consulte agora ou inicie uma medição. Senha vazia mantém a senha já salva; use Esquecer senha para removê-la."
                    : "Conexão desativada. Nenhum dado será consultado no OBS.";
                QueueActivity(new(DateTimeOffset.UtcNow, "obs-connection", "preferences-saved", "info", _observeObs ? "Consulta local OBS ativada." : "Consulta local OBS desativada."));
                StatusTitle = "Conexão OBS salva";
                StatusDetail = ObsConnectionSummary;
            }
            catch (Exception error) when (IsStorageError(error) || error is System.Security.Cryptography.CryptographicException or ArgumentException)
            {
                ObsConnectionSummary = "Não foi possível salvar toda a configuração. A senha pode ter sido guardada separadamente; confira e tente novamente.";
                StatusTitle = "Conexão OBS não foi salva";
                StatusDetail = ObsConnectionSummary;
            }
            finally { ObsPasswordBox.Clear(); }
        });
    }

    private async void ForgetObsPassword_Click(object sender, RoutedEventArgs e)
    {
        if (!CanChooseActions) return;
        await RunOperationAsync("Removendo senha local do OBS", "Removendo somente a cópia protegida pelo ZEUS.", async _ =>
        {
            try
            {
                _obsCredentials.Clear();
                ObsPasswordBox.Clear();
                await _obsClient.DisconnectAsync();
                ObsRows.Clear();
                ObsConnectionSummary = "Senha local removida. Se o servidor OBS exige senha, a próxima consulta ficará indisponível até você informar e salvar outra.";
                StatusTitle = "Senha OBS removida";
                StatusDetail = ObsConnectionSummary;
            }
            catch (Exception error) when (IsStorageError(error) || error is System.Security.Cryptography.CryptographicException)
            { ObsConnectionSummary = "Não foi possível remover a senha protegida. Nenhuma credencial foi exibida."; StatusTitle = "Senha OBS não foi removida"; StatusDetail = ObsConnectionSummary; }
        });
    }

    private async void ReadObs_Click(object sender, RoutedEventArgs e) =>
        await RunOperationAsync("Consultando OBS local", "Somente leitura, com prazo de seis segundos. A consulta avulsa não entra nas sessões de desempenho.", async token =>
        {
            DisplayObsObservation(await ReadObsAsync(token));
            StatusTitle = "Consulta OBS finalizada";
            StatusDetail = ObsConnectionSummary;
        }, cancellable: true);

    internal static string FormatObsActivity(bool? active) => active switch
    {
        true => "Ativa, informada pelo OBS",
        false => "Inativa, informada pelo OBS",
        null => "Indisponível"
    };

    internal static string FormatObsRecording(bool? active, bool? paused) => active switch
    {
        false => "Inativa, informada pelo OBS",
        true when paused == true => "Ativa e pausada, informada pelo OBS",
        true when paused == false => "Ativa e não pausada, informada pelo OBS",
        true => "Ativa, informada pelo OBS; estado de pausa indisponível",
        null => "Indisponível"
    };

    private void DisplayObsObservation(ObsObservation? obs)
    {
        ObsRows.Clear();
        if (obs is null)
        {
            ObsConnectionSummary = "Estado OBS indisponível nesta amostra ou relatório antigo.";
            return;
        }
        ObsConnectionSummary = $"{obs.Summary} Consulta entre {obs.StartedAt.ToLocalTime():HH:mm:ss} e {obs.FinishedAt.ToLocalTime():HH:mm:ss}.";
        ObsRows.Add(new("OBS · transmissão", FormatObsActivity(obs.Streaming)));
        ObsRows.Add(new("OBS · gravação", FormatObsRecording(obs.Recording, obs.RecordingPaused)));
        if (obs.State == ObsObservationState.Disabled) return;
        static string Number(double? value, string unit) => value is { } n ? $"{n:0.##} {unit}" : "indisponível";
        static string Interval(DateTimeOffset? previous, DateTimeOffset? current) => previous is { } before && current is { } after
            ? $"{before.ToLocalTime():HH:mm:ss.fff} a {after.ToLocalTime():HH:mm:ss.fff} ({(after - before).TotalSeconds:0.###} s)"
            : "indisponível";
        ObsRows.Add(new("OBS · reconectando transmissão", obs.Reconnecting switch { true => "Sim, informado pelo OBS", false => "Não, informado pelo OBS", null => "Indisponível" }));
        ObsRows.Add(new("OBS · renderização", $"FPS: {Number(obs.ActiveFps, "FPS")} · tempo médio de frame: {Number(obs.AverageFrameRenderMilliseconds, "ms")} · CPU informada: {Number(obs.CpuUsage, "%")} · memória informada: {Number(obs.MemoryMegabytes, "MB")}."));
        ObsRows.Add(new("OBS · frames pulados no intervalo", $"Renderização: {Number(obs.RenderSkippedPercent, "%")} · thread de saída: {Number(obs.OutputSkippedPercent, "%")} · saída de transmissão: {Number(obs.StreamSkippedPercent, "%")}. Deltas exigem leituras compatíveis na mesma conexão; não equivalem à perda de pacotes de toda a rede."));
        ObsRows.Add(new("OBS · janela dos deltas", $"Estatísticas: {Interval(obs.PreviousStatsReadAt, obs.StatsReadAt)} · transmissão: {Interval(obs.PreviousStreamReadAt, obs.StreamReadAt)}. Horários locais de recebimento das respostas."));
        ObsRows.Add(new("OBS · limites", "Codec e causa dos frames pulados indisponíveis nesta integração. As leituras são sequenciais e não representam um único instante. Um reinício ocorrido entre duas leituras pode passar despercebido; deltas não comprovam continuidade da saída nem causalidade. Nenhuma configuração do OBS foi alterada."));
    }
}

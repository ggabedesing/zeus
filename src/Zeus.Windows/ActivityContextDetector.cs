namespace Zeus.Windows;

public enum DetectionConfidence { Low, Medium, High }

public sealed record ActivityContextInfo(
    bool ObsProcessDetected,
    bool KnownGameProcessDetected,
    string? MatchedGameProcess,
    DetectionConfidence Confidence,
    string Summary,
    bool? ObsVideoEncodeEngineActive = null,
    double? ObsVideoEncodeEnginePercent = null);

/// <summary>Process-name heuristics identify software presence, not a live game or stream.</summary>
public static class ActivityContextDetector
{
    private static readonly HashSet<string> ObsNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "obs64", "obs", "obs-studio"
    };

    private static readonly HashSet<string> GameNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "FortniteClient-Win64-Shipping", "VALORANT-Win64-Shipping", "cs2", "csgo",
        "Overwatch", "r5apex", "GTA5", "RocketLeague",
        "RobloxPlayerBeta", "Dota2", "League of Legends", "eldenring", "ForzaHorizon5"
    };

    public static ActivityContextInfo Detect(IEnumerable<ProcessObservation> processes, IEnumerable<GpuEngineObservation>? gpuEngines = null)
    {
        ArgumentNullException.ThrowIfNull(processes);
        var observed = processes.Where(process => !string.IsNullOrWhiteSpace(process.Name)).ToArray();
        var names = observed.Select(process => process.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var obs = ObsNames.Any(names.Contains);
        var game = GameNames.FirstOrDefault(names.Contains);
        var obsIds = observed.Where(process => ObsNames.Contains(process.Name)).Select(process => process.Id).ToHashSet();
        var encodeEngines = obsIds.Count == 0 ? [] : (gpuEngines ?? [])
            .Where(engine => engine.ProcessId is { } id && obsIds.Contains(id) &&
                string.Equals(engine.EngineType, "VideoEncode", StringComparison.OrdinalIgnoreCase) &&
                double.IsFinite(engine.UtilizationPercent) && engine.UtilizationPercent >= 0)
            .Select(engine => engine.UtilizationPercent).ToArray();
        bool? encoderActive = encodeEngines.Length == 0 ? null : encodeEngines.Any(value => value > 0);
        double? encoderPercent = encodeEngines.Length == 0 ? null : encodeEngines.Max();
        var encoderDetail = encoderActive switch
        {
            true => $"maior utilização de engine VideoEncode associada ao OBS: {encoderPercent:0.#}%; isso não confirma transmissão ao vivo.",
            false => "engine VideoEncode associada ao OBS está em 0% nesta amostra; isso não confirma que a transmissão esteja parada.",
            null => "atividade de encoder GPU do OBS indisponível nesta amostra; isso não confirma transmissão ao vivo."
        };
        if (obs && game is not null)
            return new(true, true, game, DetectionConfidence.Medium,
                $"OBS e processo de jogo conhecido detectados ({game}); {encoderDetail}", encoderActive, encoderPercent);
        if (obs)
            return new(true, false, null, DetectionConfidence.High,
                $"Processo do OBS detectado; {encoderDetail}", encoderActive, encoderPercent);
        if (game is not null)
            return new(false, true, game, DetectionConfidence.Medium,
                $"Processo de jogo conhecido detectado ({game}); não confirma uma partida em andamento.", encoderActive, encoderPercent);
        return new(false, false, null, DetectionConfidence.Low,
            $"Nenhum processo conhecido foi identificado entre os processos acessíveis observados; isso não confirma que jogos ou OBS estejam fechados. {encoderDetail}", encoderActive, encoderPercent);
    }
}

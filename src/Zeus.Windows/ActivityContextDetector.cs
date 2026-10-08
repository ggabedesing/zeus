namespace Zeus.Windows;

public enum DetectionConfidence { Low, Medium, High }

public sealed record ActivityContextInfo(
    bool ObsProcessDetected,
    bool KnownGameProcessDetected,
    string? MatchedGameProcess,
    DetectionConfidence Confidence,
    string Summary);

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

    public static ActivityContextInfo Detect(IEnumerable<ProcessObservation> processes)
    {
        ArgumentNullException.ThrowIfNull(processes);
        var names = processes.Where(process => !string.IsNullOrWhiteSpace(process.Name))
            .Select(process => process.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var obs = ObsNames.Any(names.Contains);
        var game = GameNames.FirstOrDefault(names.Contains);
        if (obs && game is not null)
            return new(true, true, game, DetectionConfidence.Medium,
                $"OBS e processo de jogo conhecido detectados ({game}); estado da partida e transmissão não confirmados.");
        if (obs)
            return new(true, false, null, DetectionConfidence.High,
                "Processo do OBS detectado; não confirma que uma transmissão esteja ao vivo.");
        if (game is not null)
            return new(false, true, game, DetectionConfidence.Medium,
                $"Processo de jogo conhecido detectado ({game}); não confirma uma partida em andamento.");
        return new(false, false, null, DetectionConfidence.Low,
            "Nenhum processo conhecido foi identificado entre os processos observados; isso não confirma que jogos ou OBS estejam fechados.");
    }
}

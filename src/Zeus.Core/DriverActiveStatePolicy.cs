using System.Text.Json;

namespace Zeus.Core;

public sealed record ActiveDriverDevice(string DeviceInstanceId, string? InfName, string? Version, string? Provider, bool? IsPresent = null);
public sealed record ActiveDriverSnapshot(DateTimeOffset CheckedAt, bool IsComplete,
    IReadOnlyList<ActiveDriverDevice> Devices, IReadOnlyList<string> Warnings);
public sealed record DriverActiveEvidence(string HardwareId, ActiveDriverSnapshot Before,
    ActiveDriverSnapshot? After = null, ActiveDriverSnapshot? Latest = null);

/// <summary>Observes device bindings independently of WUA package registration.</summary>
public static class DriverActiveStatePolicy
{
    public const int MaximumPersistedBytes = 256 * 1024;

    public static DriverActiveEvidence BoundForPersistence(DriverActiveEvidence evidence)
    {
        Validate(evidence);
        static ActiveDriverSnapshot Omitted(ActiveDriverSnapshot snapshot) =>
            new(snapshot.CheckedAt, false, [], ["Captura omitida do resumo por limite de tamanho; associação e estado desconhecidos. Consulte o arquivo protegido da sessão para capturas iniciais."]);
        static bool Fits(DriverActiveEvidence value) => JsonSerializer.SerializeToUtf8Bytes(value).Length <= MaximumPersistedBytes;
        if (Fits(evidence)) return evidence;
        var bounded = evidence with { Latest = evidence.Latest is null ? null : Omitted(evidence.Latest) };
        if (Fits(bounded)) return bounded;
        bounded = bounded with { After = bounded.After is null ? null : Omitted(bounded.After) };
        if (Fits(bounded)) return bounded;
        return bounded with { Before = Omitted(bounded.Before) };
    }

    public static void Validate(DriverActiveEvidence evidence)
    {
        if (string.IsNullOrWhiteSpace(evidence.HardwareId) || evidence.HardwareId.Length > 1024 || evidence.HardwareId.Any(char.IsControl) || evidence.Before is null)
            throw new ArgumentException("Identidade de hardware ou captura anterior inválida.");
        foreach (var snapshot in new[] { evidence.Before, evidence.After, evidence.Latest }.OfType<ActiveDriverSnapshot>())
        {
            if (snapshot.CheckedAt == default || snapshot.Devices is null || snapshot.Warnings is null || snapshot.Devices.Count > 64 || snapshot.Warnings.Count > 16 ||
                snapshot.Devices.Any(device => device is null || string.IsNullOrWhiteSpace(device.DeviceInstanceId) ||
                    device.DeviceInstanceId.Length > 1024 || device.DeviceInstanceId.Any(char.IsControl) ||
                    new[] { device.InfName, device.Version, device.Provider }.Any(value => value?.Length > 1024 || value?.Any(char.IsControl) == true)) ||
                snapshot.Warnings.Any(warning => warning is null || warning.Length > 1024 || warning.Any(char.IsControl)) ||
                snapshot.Devices.Select(device => device.DeviceInstanceId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != snapshot.Devices.Count)
                throw new ArgumentException("A captura de driver contém identidade repetida ou dados inválidos.");
        }
        if (evidence.After is { } after && after.CheckedAt < evidence.Before.CheckedAt ||
            evidence.Latest is { } latest && latest.CheckedAt < (evidence.After?.CheckedAt ?? evidence.Before.CheckedAt))
            throw new ArgumentException("As capturas de driver não seguem a ordem temporal.");
    }

    public static string Describe(DriverActiveEvidence? evidence)
    {
        if (evidence is null) return "Driver ativo: captura vinculada indisponível nesta sessão; registro do Windows Update não confirma o driver em uso.";
        try { Validate(evidence); }
        catch (ArgumentException) { return "Driver ativo: evidência inválida; associação não confirmada."; }
        var after = evidence.Latest ?? evidence.After;
        if (after is null) return "Driver ativo: somente captura anterior disponível; estado posterior desconhecido.";
        if (!evidence.Before.IsComplete || !after.IsComplete)
            return "Driver ativo: captura anterior ou posterior incompleta; comparação não confirmada. Consulte os avisos. Registro WUA é evidência separada.";
        var details = evidence.Before.Devices.Select(before =>
        {
            var current = after.Devices.SingleOrDefault(item => string.Equals(item.DeviceInstanceId, before.DeviceInstanceId, StringComparison.OrdinalIgnoreCase));
            if (current is null) return $"{before.DeviceInstanceId}: não encontrado na captura posterior; estado desconhecido.";
            if (before.IsPresent != true || current.IsPresent != true)
                return $"{before.DeviceInstanceId}: presença do dispositivo não confirmada nos dois períodos; driver ativo desconhecido.";
            if (new[] { before.InfName, before.Version, current.InfName, current.Version }.Any(string.IsNullOrWhiteSpace))
                return $"{before.DeviceInstanceId}: INF/versão indisponível; mudança não confirmada.";
            var changed = !string.Equals(before.InfName, current.InfName, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(before.Version, current.Version, StringComparison.OrdinalIgnoreCase);
            return $"{before.DeviceInstanceId}: INF {before.InfName} → {current.InfName}; versão {before.Version} → {current.Version}; " +
                $"fornecedor {before.Provider ?? "indisponível"} → {current.Provider ?? "indisponível"}; " +
                (changed ? "vínculo observado mudou." : "INF/versão observados não mudaram.");
        }).ToArray();
        if (details.Length == 0) return "Driver ativo: nenhum dispositivo associado na captura anterior; não há identidade para comparação.";
        var added = after.Devices.Count(device => !evidence.Before.Devices.Any(before => string.Equals(before.DeviceInstanceId, device.DeviceInstanceId, StringComparison.OrdinalIgnoreCase)));
        return string.Join(" ", details) + $" Captura posterior: {after.CheckedAt:O}; dispositivos novos sem referência anterior: {added}. " +
            "Associação por ID de hardware/compatível pode incluir vários dispositivos. Mudança observada não prova que o pacote WUA exato está ativo, reinício realizado, assinatura válida, correção ou melhoria.";
    }
}

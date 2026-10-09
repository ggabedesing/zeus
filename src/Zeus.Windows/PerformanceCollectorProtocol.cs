using System.Text.Json;

namespace Zeus.Windows;

public enum PerformanceCollectorCategory { CpuProcesses, GpuEngines, GpuAdapterMemory, GpuProcessMemory, MemoryPaging, Disks, Networks }
public enum PerformanceCollectorState { Complete, Partial, Unavailable, TimedOut, Failed }
public sealed record PerformanceCollectorStatus(PerformanceCollectorCategory Category, DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt, TimeSpan Duration, PerformanceCollectorState State, IReadOnlyList<string> Warnings);
internal sealed record PerformanceCollectorEnvelope(PerformanceCollectorCategory Category, DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt, PerformanceCollectorState State, IReadOnlyList<string> Warnings,
    double? CpuPercent = null, TimeSpan SamplingDuration = default, IReadOnlyList<ProcessObservation>? Processes = null,
    IReadOnlyList<GpuEngineObservation>? GpuEngines = null, IReadOnlyList<GpuMemoryObservation>? GpuMemory = null,
    IReadOnlyList<GpuProcessMemoryObservation>? GpuProcessMemory = null, MemoryPagingObservation? MemoryPaging = null,
    ulong? TotalMemoryBytes = null, ulong? AvailableMemoryBytes = null,
    IReadOnlyList<DiskPerformanceObservation>? Disks = null, IReadOnlyList<NetworkPerformanceObservation>? Networks = null);
internal sealed record PerformanceCollectorPacket(int Sequence, bool Completed, PerformanceCollectorEnvelope Data)
{
    [System.Text.Json.Serialization.JsonRequired] public int Version { get; init; } = 1;
}

internal static class PerformanceCollectorProtocol
{
    internal const int MaximumBytes = 2 * 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new() { RespectRequiredConstructorParameters = true, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };
    internal static string Serialize(PerformanceCollectorPacket packet) => JsonSerializer.Serialize(packet,Options);
    internal static PerformanceCollectorPacket Parse(string json, PerformanceCollectorCategory category)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaximumBytes) throw new InvalidDataException("Saída do coletor excedeu o limite.");
        using var document=JsonDocument.Parse(json);
        ValidateUnique(document.RootElement);
        var p = JsonSerializer.Deserialize<PerformanceCollectorPacket>(json,Options) ?? throw new InvalidDataException("Saída vazia.");
        var d = p.Data;
        if (p.Version != 1 || p.Sequence < 1 || d is null || d.Category != category || !Enum.IsDefined(d.State) || d.StartedAt == default || d.FinishedAt < d.StartedAt ||
            d.Warnings is null || d.Warnings.Count > 128 || d.Warnings.Any(w => !Text(w)) || d.SamplingDuration < TimeSpan.Zero || d.SamplingDuration > TimeSpan.FromMinutes(2))
            throw new InvalidDataException("Contrato de coletor inválido.");
        bool Good(double? v, double maximum = double.MaxValue) => v is null || double.IsFinite(v.Value) && v >= 0 && v <= maximum;
        bool List<T>(IReadOnlyList<T>? rows, int limit, Func<T, bool> valid) => rows is null || rows.Count <= limit && rows.All(r => r is not null && valid(r));
        if (!Good(d.CpuPercent, 100) ||
            !List(d.Processes, 4096, r => r.Id >= 0 && !string.IsNullOrWhiteSpace(r.Name) && Text(r.Name) && r.StartTimeUtcTicks is null or > 0 && Good(r.CpuPercent,100) && Good(r.CpuCoresUsed) && Good(r.IoReadBytesPerSecond) && Good(r.IoWriteBytesPerSecond) && Good(r.IoOtherBytesPerSecond) && Good(r.IoSamplingDurationSeconds)) ||
            !List(d.GpuEngines, 200, r => !string.IsNullOrWhiteSpace(r.InstanceName) && Text(r.InstanceName) && !string.IsNullOrWhiteSpace(r.EngineType) && Text(r.EngineType) && Good(r.UtilizationPercent) && r.ProcessId is null or >= 0) ||
            !List(d.GpuMemory, 128, r => !string.IsNullOrWhiteSpace(r.AdapterInstance) && Text(r.AdapterInstance)) ||
            !List(d.GpuProcessMemory, 512, r => !string.IsNullOrWhiteSpace(r.InstanceName) && Text(r.InstanceName) && !string.IsNullOrWhiteSpace(r.AdapterInstance) && Text(r.AdapterInstance) && r.ProcessId >= 0) ||
            !List(d.Disks, 128, r => !string.IsNullOrWhiteSpace(r.InstanceName) && Text(r.InstanceName) && Good(r.ActivePercent) && Good(r.AverageReadLatencyMilliseconds)) ||
            !List(d.Networks, 128, r => !string.IsNullOrWhiteSpace(r.Adapter) && Text(r.Adapter)) ||
            d.MemoryPaging is { } m && (!Good(m.PageReadsPerSecond) || !Good(m.PagesInputPerSecond)) ||
            d.TotalMemoryBytes is { } total && d.AvailableMemoryBytes > total)
            throw new InvalidDataException("Dados do coletor inválidos.");
        if (d.Processes is { } processes && processes.Select(r => r.Id).Distinct().Count() != processes.Count) throw new InvalidDataException("Identidade de processo duplicada.");
        if (category != PerformanceCollectorCategory.CpuProcesses && (d.Processes is not null || d.CpuPercent is not null || d.SamplingDuration != default) ||
            category != PerformanceCollectorCategory.GpuEngines && d.GpuEngines is not null ||
            category != PerformanceCollectorCategory.GpuAdapterMemory && d.GpuMemory is not null ||
            category != PerformanceCollectorCategory.GpuProcessMemory && d.GpuProcessMemory is not null ||
            category != PerformanceCollectorCategory.MemoryPaging && (d.MemoryPaging is not null || d.TotalMemoryBytes is not null || d.AvailableMemoryBytes is not null) ||
            category != PerformanceCollectorCategory.Disks && d.Disks is not null || category != PerformanceCollectorCategory.Networks && d.Networks is not null)
            throw new InvalidDataException("Categoria de dados inesperada.");
        return p;
    }
    private static void ValidateUnique(JsonElement element)
    {
        if(element.ValueKind==JsonValueKind.Object)
        {
            var names=new HashSet<string>(StringComparer.Ordinal);
            foreach(var property in element.EnumerateObject()) { if(!names.Add(property.Name)) throw new InvalidDataException("Propriedade JSON duplicada."); ValidateUnique(property.Value); }
        }
        else if(element.ValueKind==JsonValueKind.Array) foreach(var item in element.EnumerateArray()) ValidateUnique(item);
    }
    private static bool Text(string? value) => value is not null && value.Length <= 2048 && !value.Any(c => char.IsControl(c) && c != '\n' && c != '\r' && c != '\t');
    internal static PerformanceCollectorEnvelope Append(PerformanceCollectorEnvelope? prior, PerformanceCollectorEnvelope next) => prior is null ? next : next with
    {
        CpuPercent = next.CpuPercent ?? prior.CpuPercent, SamplingDuration = next.SamplingDuration == default ? prior.SamplingDuration : next.SamplingDuration,
        MemoryPaging = next.MemoryPaging ?? prior.MemoryPaging,
        TotalMemoryBytes = next.TotalMemoryBytes ?? prior.TotalMemoryBytes, AvailableMemoryBytes = next.AvailableMemoryBytes ?? prior.AvailableMemoryBytes,
        Warnings = prior.Warnings.Concat(next.Warnings).Distinct().Take(128).ToArray(),
        Processes = Join(prior.Processes,next.Processes), GpuEngines = Join(prior.GpuEngines,next.GpuEngines),
        GpuMemory = Join(prior.GpuMemory,next.GpuMemory), GpuProcessMemory = Join(prior.GpuProcessMemory,next.GpuProcessMemory),
        Disks = Join(prior.Disks,next.Disks), Networks = Join(prior.Networks,next.Networks)
    };
    private static IReadOnlyList<T>? Join<T>(IReadOnlyList<T>? a,IReadOnlyList<T>? b) => a is null ? b : b is null ? a : a.Concat(b).ToArray();
}

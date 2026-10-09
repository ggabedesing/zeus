namespace Zeus.Windows;

/// <summary>
/// Relates a process allocation to the adapter's reported dedicated capacity.
/// This ratio is not a process budget, residency guarantee, or pressure diagnosis.
/// </summary>
public static class GpuProcessMemoryShare
{
    public static double? GetDedicatedCapacityPercent(
        GpuProcessMemoryObservation process,
        IEnumerable<GpuMemoryObservation> adapterReadings)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(adapterReadings);
        var matches = adapterReadings.Where(adapter => string.Equals(
                adapter.AdapterInstance, process.AdapterInstance, StringComparison.OrdinalIgnoreCase))
            .Take(2).ToArray();
        if (matches.Length != 1 || process.DedicatedUsageBytes is not { } usage)
            return null;
        var capacity = matches[0].DedicatedCapacityBytes;
        if (capacity is null or 0) return null;

        var percent = usage / (double)capacity.Value * 100;
        return double.IsFinite(percent) ? percent : null;
    }
}

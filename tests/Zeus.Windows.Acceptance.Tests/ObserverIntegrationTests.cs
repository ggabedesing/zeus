using System.Diagnostics;
using System.Text.Json;
using Zeus.Desktop;
using Zeus.Windows;

namespace Zeus.Windows.Acceptance.Tests;

public sealed class ObserverIntegrationTests
{
    [Fact]
    public async Task PackagedHostRejectsUnknownArgumentsWithoutCollecting()
    {
        var executable = Path.Combine(AppContext.BaseDirectory, "Zeus.Observer.exe");
        Assert.True(File.Exists(executable));
        var info = new ProcessStartInfo(executable)
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add("--collector"); info.ArgumentList.Add("unknown-command");
        info.ArgumentList.Add("--duration"); info.ArgumentList.Add("2");
        using var process = Process.Start(info)!;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            await process.WaitForExitAsync(deadline.Token);
            Assert.Equal(2, process.ExitCode);
            Assert.Empty(await process.StandardOutput.ReadToEndAsync());
        }
        finally { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); } }
    }

    [Fact]
    public async Task CollectorStatesAndTimesSurviveSqliteAndReportExport()
    {
        var root = Path.Combine(Path.GetTempPath(), "Zeus.ObserverStorage." + Guid.NewGuid().ToString("N"));
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var collector = new PerformanceCollectorStatus(PerformanceCollectorCategory.Disks, now.AddSeconds(-10), now,
            TimeSpan.FromSeconds(10), PerformanceCollectorState.TimedOut, ["Leitura parcial: prazo excedido."]);
        var observation = new PerformanceObservation(now, TimeSpan.FromSeconds(2), null, 0, 0, [], [],
            Disks: [new("fixture", 1024, null, null)], Collectors: [collector]);
        try
        {
            var storage = new DesktopStorage(root);
            await storage.StartPerformanceSessionAsync(id, "fixture", now);
            await storage.AppendPerformanceObservationAsync(id, 0, observation);
            var saved = Assert.Single(Assert.Single(await storage.ReadPerformanceSessionsAsync()).Samples);
            var restored = JsonSerializer.Deserialize<PerformanceObservation>(saved.DetailsJson, DesktopStorage.JsonOptions)!;
            var status = Assert.Single(restored.Collectors!);
            Assert.Equal(collector.Category, status.Category);
            Assert.Equal(collector.State, status.State);
            Assert.Equal(collector.StartedAt, status.StartedAt);
            Assert.Equal(collector.Duration, status.Duration);
            Assert.Null(restored.CpuPercent);
            Assert.Equal(1024UL, Assert.Single(restored.Disks!).BytesPerSecond);
            var report = Path.Combine(root, "report.json");
            await DesktopStorage.ExportAsync(report, new ExportDocument(13, now, null, [], Performance: restored));
            using var exported = JsonDocument.Parse(await File.ReadAllTextAsync(report));
            Assert.Equal("TimedOut", exported.RootElement.GetProperty("Performance").GetProperty("Collectors")[0].GetProperty("State").GetString());
            var legacyJson = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(observation, DesktopStorage.JsonOptions))!;
            legacyJson.AsObject().Remove("Collectors");
            var legacy = JsonSerializer.Deserialize<PerformanceObservation>(legacyJson.ToJsonString(), DesktopStorage.JsonOptions)!;
            Assert.Null(legacy.Collectors);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}

using System.Text.Json;
using System.Text.Json.Nodes;
using Zeus.Desktop;
using Zeus.Windows;

namespace Zeus.Windows.Acceptance.Tests;

public sealed class ObsIntegrationTests
{
    [Fact]
    public void UnknownActivityAndLegacyPreferencesStayExplicit()
    {
        Assert.Equal("Indisponível", MainWindow.FormatObsActivity(null));
        Assert.Contains("Inativa", MainWindow.FormatObsActivity(false));
        Assert.Contains("Ativa", MainWindow.FormatObsActivity(true));
        Assert.Contains("e pausada", MainWindow.FormatObsRecording(true, true));
        Assert.Contains("e não pausada", MainWindow.FormatObsRecording(true, false));
        Assert.Contains("pausa indisponível", MainWindow.FormatObsRecording(true, null));
        Assert.Equal("Indisponível", MainWindow.FormatObsRecording(null, null));
        var old = JsonSerializer.Deserialize<DesktopPreferences>("{\"IsMinimal\":false}", DesktopStorage.JsonOptions)!;
        Assert.False(old.ObserveObs);
        Assert.Equal(4455, old.ObsPort);
        var node = JsonNode.Parse(JsonSerializer.Serialize(new PerformanceObservation(DateTimeOffset.UtcNow, TimeSpan.Zero, null, 0, 0, [], []), DesktopStorage.JsonOptions))!;
        node.AsObject().Remove("Obs");
        Assert.Null(JsonSerializer.Deserialize<PerformanceObservation>(node.ToJsonString(), DesktopStorage.JsonOptions)!.Obs);
    }

    [ObsCredentialWindowsFact]
    public async Task ObsEvidenceSurvivesHistoryExportAndDatabaseBackupWithoutCredential()
    {
        var root = Path.Combine(Path.GetTempPath(), "Zeus.ObsIntegration." + Guid.NewGuid().ToString("N"));
        try
        {
            var now = DateTimeOffset.UtcNow;
            var obs = new ObsObservation(now.AddSeconds(-1), now, Guid.NewGuid(), ObsObservationState.Partial,
                "Leitura parcial; codificador indisponível.", Streaming: true, Recording: true, RecordingPaused: true,
                ActiveFps: 59.94, RenderSkippedFrames: 10, RenderTotalFrames: 1000, RenderSkippedPercent: 1.2);
            var observation = new PerformanceObservation(now, TimeSpan.FromSeconds(2), 12, 1000, 800, [], [], Obs: obs);
            var storage = new DesktopStorage(root);
            var credential = new ObsCredentialStore(Path.Combine(root, "ObsConnection"));
            const string secret = "senhafixture-OBS-never-export-ç";
            credential.Save(secret);
            await storage.SavePreferencesAsync(new(false, ObserveObs: true, ObsPort: 4455));
            var id = Guid.NewGuid();
            await storage.StartPerformanceSessionAsync(id, "OBS fixture", now);
            await storage.AppendPerformanceObservationAsync(id, 0, observation);
            var sample = Assert.Single(Assert.Single(await storage.ReadPerformanceSessionsAsync()).Samples);
            var restored = JsonSerializer.Deserialize<PerformanceObservation>(sample.DetailsJson, DesktopStorage.JsonOptions)!;
            Assert.Equal(obs, restored.Obs);
            Assert.DoesNotContain(secret, sample.DetailsJson);
            var export = Path.Combine(root, "report.json");
            await DesktopStorage.ExportAsync(export, new ExportDocument(14, now, null, [], Performance: restored));
            var reportText = await File.ReadAllTextAsync(export);
            Assert.DoesNotContain(secret, reportText);
            Assert.DoesNotContain("password.dpapi", reportText);
            using var report = JsonDocument.Parse(reportText);
            var evidence = report.RootElement.GetProperty("Performance").GetProperty("Obs");
            Assert.True(evidence.GetProperty("Streaming").GetBoolean());
            Assert.True(evidence.GetProperty("Recording").GetBoolean());
            Assert.True(evidence.GetProperty("RecordingPaused").GetBoolean());
            Assert.Equal(JsonValueKind.Null, evidence.GetProperty("Reconnecting").ValueKind);
            Assert.Equal("Partial", evidence.GetProperty("State").GetString());
            var backup = Path.Combine(root, "backup.db");
            await storage.BackupDatabaseAsync(backup);
            Assert.DoesNotContain(secret, System.Text.Encoding.UTF8.GetString(await File.ReadAllBytesAsync(backup)));
            Assert.Equal(secret, credential.Load());
            Assert.True((await storage.ReadPreferencesAsync()).ObserveObs);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}

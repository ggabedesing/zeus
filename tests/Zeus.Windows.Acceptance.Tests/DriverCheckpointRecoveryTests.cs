using System.Text;
using System.Text.Json;
using Zeus.Core;
using Zeus.Windows;

namespace Zeus.Windows.Acceptance.Tests;

public sealed class DriverCheckpointRecoveryTests
{
    private static readonly Guid UpdateId = Guid.Parse("93416193-0643-4a17-a59a-c2ecfe501ebc");
    private static DriverActiveEvidence Evidence() => new("PCI\\VEN_1234",
        new(DateTimeOffset.UtcNow.AddMinutes(-2), true, [new("PCI\\VEN_1234\\DEVICE", "oem1.inf", "1.0", "Vendor", true)], []));
    private static MaintenanceRequest Request() => new(MaintenanceActionId.InstallDriverUpdate, $"{UpdateId:D}:7", UpdateServerSelection: 2);

    [Fact]
    public void NameIsFixedAndCultureIndependent()
    {
        Assert.Equal("driver-9341619306434a17a59ac2ecfe501ebc-7-active.json", SessionStore.GetDriverActiveCheckpointFileName(UpdateId, 7));
        Assert.Throws<ArgumentException>(() => SessionStore.GetDriverActiveCheckpointFileName(Guid.Empty, 7));
        Assert.Throws<ArgumentException>(() => SessionStore.GetDriverActiveCheckpointFileName(UpdateId, 0));
        Assert.Throws<ArgumentException>(() => SessionStore.GetDriverActiveCheckpointFileName(UpdateId, -1));
    }

    [Fact]
    public void ParserPreservesCompleteOriginalEvidence()
    {
        var original = Evidence();
        var parsed = SessionStore.ParseDriverActiveCheckpoint(JsonSerializer.SerializeToUtf8Bytes(original));
        Assert.Equal(original.HardwareId, parsed.HardwareId);
        Assert.Equal(original.Before.CheckedAt, parsed.Before.CheckedAt);
        Assert.Equal(original.Before.Devices[0], parsed.Before.Devices[0]);
        Assert.Null(parsed.After);
        Assert.Null(parsed.Latest);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"HardwareId\":\"A\",\"Before\":null}")]
    [InlineData("{\"HardwareId\":\"A\",\"HardwareId\":\"B\",\"Before\":null}")]
    public void ParserRejectsMissingInvalidAndDuplicateProperties(string json) =>
        Assert.ThrowsAny<Exception>(() => SessionStore.ParseDriverActiveCheckpoint(Encoding.UTF8.GetBytes(json)));

    [Fact]
    public void ParserRejectsUnknownNestedMembersAndOversize()
    {
        var json = JsonSerializer.Serialize(Evidence()).Replace("\"IsComplete\":true", "\"IsComplete\":true,\"Foreign\":true");
        Assert.Throws<JsonException>(() => SessionStore.ParseDriverActiveCheckpoint(Encoding.UTF8.GetBytes(json)));
        Assert.Throws<InvalidDataException>(() => SessionStore.ParseDriverActiveCheckpoint(new byte[DriverActiveStatePolicy.MaximumPersistedBytes + 1]));
    }

    [Fact]
    public async Task MergeOnlyAddsObservationWithoutConfirmingWuaOrCompletion()
    {
        var request = Request();
        var now = DateTimeOffset.UtcNow;
        var step = new MaintenanceStepResult(request.Action, StepOutcome.Skipped, "Unknown", TargetId: request.TargetId,
            Verification: MaintenanceVerificationStatus.ManualReviewRequired, UpdateServerSelection: 2);
        var report = new MaintenanceReport(Guid.NewGuid(), now, now, false, [step], "interrupted", IsComplete: false);
        var evidence = Evidence();
        var calls = 0;
        var result = await PendingMaintenanceSessions.RecoverDriverObservationsAsync(report, [request], (session, update, revision, ct) =>
        {
            calls++;
            Assert.Equal(report.SessionId, session); Assert.Equal(UpdateId, update); Assert.Equal(7, revision);
            return Task.FromResult(evidence);
        }, CancellationToken.None);
        Assert.Equal(1, calls);
        Assert.False(result.IsComplete);
        Assert.Equal(report.Error, result.Error);
        Assert.Equal(step, result.Steps[0] with { ActiveDriver = null });
        Assert.Same(evidence, result.Steps[0].ActiveDriver);
        Assert.Single(MaintenancePolicy.FindUnresolvedAttempts([request], [result]));
    }

    [Fact]
    public async Task RawUnicodeCheckpointIsPreservedButRecoveredSummaryFitsPersistenceBudget()
    {
        var request = Request(); var now = DateTimeOffset.UtcNow;
        var metadata = new string('ç', 256);
        var devices = Enumerable.Range(0, 64).Select(index => new ActiveDriverDevice(
            $"PCI\\VEN_1234\\DEVICE{index}", metadata, metadata, metadata, true)).ToArray();
        var original = new DriverActiveEvidence("PCI\\VEN_1234", new(now, true, devices, []));
        var raw = JsonSerializer.SerializeToUtf8Bytes(original, new JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
        Assert.True(raw.Length < DriverActiveStatePolicy.MaximumPersistedBytes);
        Assert.True(JsonSerializer.SerializeToUtf8Bytes(original).Length > DriverActiveStatePolicy.MaximumPersistedBytes);
        var parsed = SessionStore.ParseDriverActiveCheckpoint(raw);
        Assert.True(parsed.Before.IsComplete);
        Assert.Equal(64, parsed.Before.Devices.Count);
        Assert.Equal(devices[63], parsed.Before.Devices[63]);
        var report = new MaintenanceReport(Guid.NewGuid(), now, now, false,
            [new(request.Action, StepOutcome.Skipped, "pending", TargetId: request.TargetId, Verification: MaintenanceVerificationStatus.Pending)],
            IsComplete: false);
        var recovered = await PendingMaintenanceSessions.RecoverDriverObservationsAsync(report, [request],
            (_, _, _, _) => Task.FromResult(parsed), CancellationToken.None);
        var bounded = Assert.IsType<DriverActiveEvidence>(recovered.Steps[0].ActiveDriver);
        Assert.True(JsonSerializer.SerializeToUtf8Bytes(bounded).Length <= DriverActiveStatePolicy.MaximumPersistedBytes);
        Assert.False(bounded.Before.IsComplete);
        Assert.Empty(bounded.Before.Devices);
        Assert.NotEmpty(bounded.Before.Warnings);
        Assert.True(parsed.Before.IsComplete);
        Assert.Equal(64, parsed.Before.Devices.Count);
        Assert.Equal(MaintenanceVerificationStatus.Pending, recovered.Steps[0].Verification);
        Assert.False(recovered.IsComplete);
    }

    [Fact]
    public async Task LegacyPendingSourceIsRestoredFromMatchingReceiptWithoutPromotingOutcome()
    {
        var request = Request(); var now = DateTimeOffset.UtcNow;
        var step = new MaintenanceStepResult(request.Action, StepOutcome.Skipped, "pending", TargetId: request.TargetId,
            Verification: MaintenanceVerificationStatus.Pending);
        var report = new MaintenanceReport(Guid.NewGuid(), now, now, false, [step], IsComplete: false);
        var result = await PendingMaintenanceSessions.RecoverDriverObservationsAsync(report, [request],
            (_, _, _, _) => Task.FromResult(Evidence()), CancellationToken.None);
        Assert.Equal(2, result.Steps[0].UpdateServerSelection);
        Assert.Equal(StepOutcome.Skipped, result.Steps[0].Outcome);
        Assert.Equal(MaintenanceVerificationStatus.Pending, result.Steps[0].Verification);
        Assert.False(result.IsComplete);
    }

    [Fact]
    public async Task ExistingEvidenceWrongTargetAndWrongSourceAreNeverReplaced()
    {
        var request = Request();
        var now = DateTimeOffset.UtcNow;
        var evidence = Evidence();
        var step = new MaintenanceStepResult(request.Action, StepOutcome.Failed, "failure", TargetId: request.TargetId, UpdateServerSelection: 2);
        foreach (var candidate in new[] { step with { ActiveDriver = evidence }, step with { TargetId = $"{UpdateId:D}:8" }, step with { UpdateServerSelection = 3 } })
        {
            var report = new MaintenanceReport(Guid.NewGuid(), now, now, false, [candidate], IsComplete: false);
            var result = await PendingMaintenanceSessions.RecoverDriverObservationsAsync(report, [request], (_, _, _, _) =>
                throw new InvalidOperationException("Checkpoint must not be read"), CancellationToken.None);
            Assert.Equal(candidate, result.Steps[0]);
        }
    }

    [Fact]
    public async Task MissingInvalidCheckpointAndEmptyReceiptKeepUnknownState()
    {
        var request = Request(); var now = DateTimeOffset.UtcNow;
        var step = new MaintenanceStepResult(request.Action, StepOutcome.Skipped, "unknown", TargetId: request.TargetId);
        var report = new MaintenanceReport(Guid.NewGuid(), now, now, false, [step], IsComplete: false);
        var unavailable = await PendingMaintenanceSessions.RecoverDriverObservationsAsync(report, [request], (_, _, _, _) =>
            throw new InvalidDataException("Corrupt"), CancellationToken.None);
        Assert.Equal(step, unavailable.Steps[0]);
        var empty = await PendingMaintenanceSessions.RecoverDriverObservationsAsync(report, [], (_, _, _, _) =>
            throw new InvalidOperationException("Must not read"), CancellationToken.None);
        Assert.Same(report, empty);
    }

    [Fact]
    public async Task RecoveryHonorsCancellation()
    {
        var request = Request(); var now = DateTimeOffset.UtcNow;
        var report = new MaintenanceReport(Guid.NewGuid(), now, now, false,
            [new(request.Action, StepOutcome.Skipped, "unknown", TargetId: request.TargetId)], IsComplete: false);
        using var source = new CancellationTokenSource(); source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PendingMaintenanceSessions.RecoverDriverObservationsAsync(report, [request],
            (_, _, _, _) => Task.FromResult(Evidence()), source.Token));
    }
}

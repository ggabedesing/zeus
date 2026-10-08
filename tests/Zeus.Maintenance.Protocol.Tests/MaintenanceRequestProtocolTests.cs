using System.Text;
using Zeus.Core;

namespace Zeus.Maintenance.Protocol.Tests;

public sealed class MaintenanceRequestProtocolTests
{
    private const string SessionId = "12345678-1234-1234-1234-123456789abc";
    private const string DriverId = SessionId + ":1";

    [Fact]
    public void ExistingActionIdsRetainTheirSerializedNumbers()
    {
        Assert.Equal(0, (int)MaintenanceActionId.DefenderQuickScan);
        Assert.Equal(1, (int)MaintenanceActionId.ScanWindowsImage);
        Assert.Equal(2, (int)MaintenanceActionId.RepairWindowsImage);
        Assert.Equal(3, (int)MaintenanceActionId.VerifySystemFiles);
        Assert.Equal(4, (int)MaintenanceActionId.RepairSystemFiles);
        Assert.Equal(5, (int)MaintenanceActionId.AnalyzeSystemDrive);
        Assert.Equal(10, (int)MaintenanceActionId.InstallDriverUpdate);
        Assert.Equal(11, (int)MaintenanceActionId.RollbackDriver);
    }

    [Fact]
    public void TypedPlanRoundTripsIdentityAndConsentAndOrdersOfflineLast()
    {
        MaintenanceRequest[] selected = [
            new(MaintenanceActionId.DefenderOfflineScan),
            new(MaintenanceActionId.UpdateDefenderSignatures)];
        var encoded = MaintenanceRequestProtocol.Encode(selected);

        Assert.True(MaintenanceRequestProtocol.TryReadArguments(["--session", SessionId, "--requests", encoded],
            out var session, out var requests));
        Assert.Equal(Guid.Parse(SessionId), session);
        Assert.Equal(MaintenanceActionId.UpdateDefenderSignatures, requests[0].Action);
        Assert.Equal(MaintenanceActionId.DefenderOfflineScan, requests[^1].Action);
    }

    [Fact]
    public void DriverInstallationIsExclusiveAndCannotContainMultipleCandidates()
    {
        MaintenanceRequest[] selected = [
            new(MaintenanceActionId.InstallDriverUpdate, DriverId, true, 2),
            new(MaintenanceActionId.InstallDriverUpdate, SessionId + ":2", false, 2)];
        Assert.Throws<ArgumentException>(() => MaintenancePolicy.ValidateRequests(selected));
        Assert.Throws<ArgumentException>(() => MaintenancePolicy.ValidateRequests([
            selected[0], new(MaintenanceActionId.UpdateDefenderSignatures)]));
    }

    [Fact]
    public void DriverInstallRequestPreservesExactWindowsUpdateServerSelection()
    {
        var serviceId = "12345678-1234-1234-1234-123456789abc";
        var selected = new[] { new MaintenanceRequest(MaintenanceActionId.InstallDriverUpdate, DriverId, false, 3, serviceId) };
        var encoded = MaintenanceRequestProtocol.Encode(selected);

        Assert.True(MaintenanceRequestProtocol.TryReadArguments(["--session", SessionId, "--requests", encoded], out _, out var requests));
        Assert.Equal(selected, requests);
    }

    [Theory]
    [InlineData("[{\"Action\":\"DefenderQuickScan\",\"Command\":\"cmd.exe\"}]")]
    [InlineData("[{\"Action\":0}]")]
    [InlineData("[{\"Action\":\"0\"}]")]
    [InlineData("[{\"Action\":\"defenderQuickScan\"}]")]
    [InlineData("[{\"Action\":\"Bogus\"}]")]
    [InlineData("[{\"Action\":\"DefenderQuickScan\",\"Action\":\"RepairSystemFiles\"}]")]
    [InlineData("[{\"action\":\"DefenderQuickScan\"}]")]
    [InlineData("[{\"Action\":\"DefenderQuickScan\",\"TargetId\":\"C:\\\\x.exe\"}]")]
    [InlineData("[{\"Action\":\"DefenderQuickScan\",\"EulaAccepted\":true}]")]
    [InlineData("[{\"Action\":\"DefenderQuickScan\",\"EulaAccepted\":\"true\"}]")]
    [InlineData("[{\"Action\":\"InstallDriverUpdate\"}]")]
    [InlineData("[{\"Action\":\"DefenderQuickScan\"},{\"Action\":\"DefenderQuickScan\"}]")]
    [InlineData("{\"Action\":\"DefenderQuickScan\"}")]
    [InlineData("[null]")]
    [InlineData("[]")]
    [InlineData("[{\"Action\":\"DefenderQuickScan\",}]")]
    public void RejectsMalformedOrUnsupportedTypedPayloadsBeforeExecution(string payload)
    {
        Assert.False(Read(payload, out _));
    }

    [Theory]
    [InlineData("[{\"Action\":\"\\uD800\"}]")]
    [InlineData("[{\"Action\":\"\\uDC00\"}]")]
    [InlineData("[{\"\\uD800\":\"DefenderQuickScan\"}]")]
    [InlineData("[{\"\\uDC00\":\"DefenderQuickScan\"}]")]
    [InlineData("[{\"Action\":\"InstallDriverUpdate\",\"TargetId\":\"\\uD800\"}]")]
    [InlineData("[{\"Action\":\"InstallDriverUpdate\",\"TargetId\":\"\\uDC00\"}]")]
    public void InvalidUnicodeEscapesAreRejectedWithoutThrowing(string payload)
    {
        Assert.False(Read(payload, out var requests));
        Assert.Empty(requests);
    }

    [Theory]
    [InlineData("12345678-1234-1234-1234-123456789abc:1", true)]
    [InlineData("12345678-1234-1234-1234-123456789abc:2147483647", true)]
    [InlineData("12345678-1234-1234-1234-123456789abc:0", false)]
    [InlineData("12345678-1234-1234-1234-123456789abc:01", false)]
    [InlineData("12345678-1234-1234-1234-123456789abc:-1", false)]
    [InlineData("12345678-1234-1234-1234-123456789abc:2147483648", false)]
    [InlineData("12345678-1234-1234-1234-123456789abc:1;calc", false)]
    [InlineData("{12345678-1234-1234-1234-123456789abc}:1", false)]
    [InlineData("00000000-0000-0000-0000-000000000000:1", false)]
    [InlineData("C:\\driver.inf", false)]
    [InlineData(null, false)]
    public void DriverIdentityOnlyAllowsNonemptyGuidAndCanonicalPositiveRevision(string? target, bool allowed)
    {
        Assert.Equal(allowed, MaintenanceRequestProtocol.TryParseDriverIdentity(target, out _, out _));
    }

    [Theory]
    [InlineData("PCI\\VEN_10DE&DEV_1C82\\4&2A1B2C3D&0&0008", true)]
    [InlineData("USB\\VID_046D&PID_C52B\\ABC123", true)]
    [InlineData("\\PCI\\DEVICE", false)]
    [InlineData("PCI", false)]
    [InlineData("PCI\\", false)]
    [InlineData("PCI\\DEVICE\r\ncalc", false)]
    [InlineData(" PCI\\DEVICE", false)]
    [InlineData("PCI\\DEVICE;calc", true)]
    [InlineData(null, false)]
    public void PnpInstanceIdsAreBoundedOpaqueIdentifiers(string? target, bool allowed)
    {
        Assert.Equal(allowed, MaintenanceRequestProtocol.TryParsePnpInstanceId(target));
    }

    [Fact]
    public void DriverRollbackRequiresOneExplicitDeviceAndCannotBeMixedWithOtherActions()
    {
        const string device = "PCI\\VEN_10DE&DEV_1C82\\4&2A1B2C3D&0&0008";
        var request = new MaintenanceRequest(MaintenanceActionId.RollbackDriver, device);
        Assert.Equal(request, Assert.Single(MaintenancePolicy.ValidateRequests([request])));
        Assert.False(MaintenanceCatalog.Get(MaintenanceActionId.RollbackDriver).RequiresRestorePoint);
        Assert.Throws<ArgumentException>(() => MaintenancePolicy.ValidateRequests([
            request, new(MaintenanceActionId.DefenderQuickScan)]));
        Assert.Throws<ArgumentException>(() => MaintenancePolicy.ValidateRequests([
            request with { EulaAccepted = true }]));
        Assert.Throws<ArgumentException>(() => MaintenancePolicy.ValidateRequests([
            request, request with { TargetId = device.ToLowerInvariant() }]));
    }

    [Theory]
    [InlineData("DefenderQuickScan", true)]
    [InlineData("ScanWindowsImage,RepairWindowsImage", false)]
    [InlineData("0", false)]
    [InlineData("DefenderQuickScan,DefenderQuickScan", false)]
    [InlineData("defenderquickscan", false)]
    [InlineData("DefenderQuickScan;calc", false)]
    [InlineData("InstallDriverUpdate", false)]
    [InlineData("RollbackDriver", false)]
    [InlineData("", false)]
    public void LegacyArgumentsRemainStrictAndCannotInstallUnidentifiedDriver(string actions, bool allowed)
    {
        Assert.Equal(allowed, MaintenanceRequestProtocol.TryReadArguments(
            ["--session", SessionId, "--actions", actions], out _, out _));
    }

    [Fact]
    public void InvalidSessionsAndExtraFlagsCannotReachExecution()
    {
        var payload = MaintenanceRequestProtocol.Encode([new(MaintenanceActionId.DefenderQuickScan)]);
        Assert.False(MaintenanceRequestProtocol.TryReadArguments(["--session", "{" + SessionId + "}", "--requests", payload], out _, out _));
        Assert.False(MaintenanceRequestProtocol.TryReadArguments(["--session", " " + SessionId + " ", "--requests", payload], out _, out _));
        Assert.False(MaintenanceRequestProtocol.TryReadArguments(["--session", Guid.Empty.ToString("D"), "--requests", payload], out _, out _));
        Assert.False(MaintenanceRequestProtocol.TryReadArguments(["--session", SessionId, "--requests", payload, "--command", "calc"], out _, out _));
    }

    [Fact]
    public void OversizedMalformedBase64AndInvalidUtf8AreRejected()
    {
        Assert.False(Read("[{\"Action\":\"DefenderQuickScan\"}]" + new string(' ', 8192), out _));
        Assert.False(MaintenanceRequestProtocol.TryReadArguments(["--session", SessionId, "--requests", "$$$"], out _, out _));
        Assert.False(MaintenanceRequestProtocol.TryReadArguments(
            ["--session", SessionId, "--requests", Convert.ToBase64String([0xff])], out _, out _));
    }

    [Fact]
    public void DriverNeedsRecoveryAndWindowsVolumeOptimizationDoesNot()
    {
        Assert.True(MaintenanceCatalog.Get(MaintenanceActionId.InstallDriverUpdate).RequiresRestorePoint);
        Assert.False(MaintenanceCatalog.Get(MaintenanceActionId.OptimizeSystemDrive).RequiresRestorePoint);
        Assert.Throws<InvalidOperationException>(() => MaintenancePolicy.EnsureRestorePoint([MaintenanceActionId.InstallDriverUpdate], false));
        MaintenancePolicy.EnsureRestorePoint([MaintenanceActionId.OptimizeSystemDrive], false);
    }

    [Theory]
    [InlineData(MaintenanceActionId.ScanWindowsImage, MaintenanceActionId.RepairWindowsImage)]
    [InlineData(MaintenanceActionId.VerifySystemFiles, MaintenanceActionId.RepairSystemFiles)]
    public void HelperRejectsCombinedScanAndRepairRequestsBeforeExecution(MaintenanceActionId scan, MaintenanceActionId repair)
    {
        var requests = new[] { new MaintenanceRequest(scan), new MaintenanceRequest(repair) };
        Assert.Throws<ArgumentException>(() => MaintenancePolicy.ValidateRequests(requests));
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(requests)));
        Assert.False(MaintenanceRequestProtocol.TryReadArguments(
            ["--session", SessionId, "--requests", payload], out _, out _));
    }

    [Fact]
    public void EveryActionIsCataloguedAndCompatibleRequestsCannotBeMutated()
    {
        Assert.All(Enum.GetValues<MaintenanceActionId>(), action => Assert.Equal(action, MaintenanceCatalog.Get(action).Id));
        var requests = Enum.GetValues<MaintenanceActionId>()
            .Where(action => action is not MaintenanceActionId.ScanWindowsImage and not MaintenanceActionId.VerifySystemFiles and not MaintenanceActionId.RollbackDriver and not MaintenanceActionId.InstallDriverUpdate)
            .Select(action => new MaintenanceRequest(action)).ToArray();
        var ordered = MaintenancePolicy.ValidateRequests(requests);
        Assert.Equal(requests.Length, ordered.Count);
        Assert.Equal(MaintenanceActionId.DefenderOfflineScan, ordered[^1].Action);
        Assert.Throws<NotSupportedException>(() => ((IList<MaintenanceRequest>)ordered).Clear());
        var rollback = MaintenancePolicy.ValidateRequests([new MaintenanceRequest(MaintenanceActionId.RollbackDriver, "USB\\VID_1234&PID_5678\\A1")]);
        Assert.Equal(MaintenanceActionId.RollbackDriver, Assert.Single(rollback).Action);
    }

    [Fact]
    public void OldReportsDefaultToCompleteAndProgressReportsAreExplicitlyIncomplete()
    {
        var historical = new MaintenanceReport(Guid.Parse(SessionId), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, false, []);
        Assert.True(historical.IsComplete);
        Assert.False((historical with { IsComplete = false }).IsComplete);
        var oldJson = "{\"SessionId\":\"" + SessionId + "\",\"StartedAt\":\"2026-01-01T00:00:00Z\",\"FinishedAt\":\"2026-01-01T00:00:00Z\",\"RestorePointConfirmed\":false,\"Steps\":[]}";
        Assert.True(System.Text.Json.JsonSerializer.Deserialize<MaintenanceReport>(oldJson)!.IsComplete);
    }

    [Fact]
    public void HistoricalStepsWithoutVerificationRemainExplicitlyUnrecorded()
    {
        var oldJson = "{\"SessionId\":\"" + SessionId + "\",\"StartedAt\":\"2026-01-01T00:00:00Z\",\"FinishedAt\":\"2026-01-01T00:01:00Z\",\"RestorePointConfirmed\":false,\"Steps\":[{\"Action\":3,\"Outcome\":0,\"Message\":\"Concluída\"}]}";

        var report = System.Text.Json.JsonSerializer.Deserialize<MaintenanceReport>(oldJson)!;

        Assert.Equal(MaintenanceVerificationStatus.NotRecorded, Assert.Single(report.Steps).Verification);
    }

    private static bool Read(string payload, out IReadOnlyList<MaintenanceRequest> requests) =>
        MaintenanceRequestProtocol.TryReadArguments(["--session", SessionId, "--requests", Convert.ToBase64String(Encoding.UTF8.GetBytes(payload))],
            out _, out requests);
}

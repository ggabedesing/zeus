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
    }

    [Fact]
    public void TypedPlanRoundTripsIdentityAndConsentAndOrdersOfflineLast()
    {
        MaintenanceRequest[] selected = [
            new(MaintenanceActionId.DefenderOfflineScan),
            new(MaintenanceActionId.InstallDriverUpdate, DriverId, true),
            new(MaintenanceActionId.UpdateDefenderSignatures)];
        var encoded = MaintenanceRequestProtocol.Encode(selected);

        Assert.True(MaintenanceRequestProtocol.TryReadArguments(["--session", SessionId, "--requests", encoded],
            out var session, out var requests));
        Assert.Equal(Guid.Parse(SessionId), session);
        Assert.Equal(new MaintenanceRequest(MaintenanceActionId.InstallDriverUpdate, DriverId, true), requests[0]);
        Assert.Equal(MaintenanceActionId.UpdateDefenderSignatures, requests[1].Action);
        Assert.Equal(MaintenanceActionId.DefenderOfflineScan, requests[^1].Action);
    }

    [Fact]
    public void MultipleDifferentDriverIdentitiesShareAPlanAndRetainSelectionOrder()
    {
        MaintenanceRequest[] selected = [
            new(MaintenanceActionId.InstallDriverUpdate, DriverId, true),
            new(MaintenanceActionId.InstallDriverUpdate, SessionId + ":2", false)];
        var encoded = MaintenanceRequestProtocol.Encode(selected);
        Assert.True(MaintenanceRequestProtocol.TryReadArguments(["--session", SessionId, "--requests", encoded],
            out _, out var requests));
        Assert.Equal(selected, requests);
        Assert.Throws<ArgumentException>(() => MaintenancePolicy.ValidateRequests([
            selected[0], new(MaintenanceActionId.InstallDriverUpdate, DriverId.ToUpperInvariant(), false)]));
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
    [InlineData("DefenderQuickScan", true)]
    [InlineData("ScanWindowsImage,RepairWindowsImage", true)]
    [InlineData("0", false)]
    [InlineData("DefenderQuickScan,DefenderQuickScan", false)]
    [InlineData("defenderquickscan", false)]
    [InlineData("DefenderQuickScan;calc", false)]
    [InlineData("InstallDriverUpdate", false)]
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

    [Fact]
    public void EveryNewActionIsOrderedAndCataloguedAndRequestsCannotBeMutated()
    {
        var requests = Enum.GetValues<MaintenanceActionId>().Select(action =>
            new MaintenanceRequest(action, action == MaintenanceActionId.InstallDriverUpdate ? DriverId : null)).ToArray();
        var ordered = MaintenancePolicy.ValidateRequests(requests);
        Assert.Equal(requests.Length, ordered.Count);
        Assert.Equal(MaintenanceActionId.DefenderOfflineScan, ordered[^1].Action);
        Assert.Throws<NotSupportedException>(() => ((IList<MaintenanceRequest>)ordered).Clear());
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

    private static bool Read(string payload, out IReadOnlyList<MaintenanceRequest> requests) =>
        MaintenanceRequestProtocol.TryReadArguments(["--session", SessionId, "--requests", Convert.ToBase64String(Encoding.UTF8.GetBytes(payload))],
            out _, out requests);
}

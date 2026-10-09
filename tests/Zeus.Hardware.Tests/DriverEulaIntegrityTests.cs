using System.Text.Json;
using Zeus.Core;
using Zeus.Windows;

namespace Zeus.Hardware.Tests;

public sealed class DriverEulaIntegrityTests
{
    [Fact]
    public void SearchPreservesExactLicenseTextIncludingBoundaryWhitespaceForConsentHash()
    {
        const string license = "\r\n  Licença de teste ç 漢字  \r\n";
        var payload = JsonSerializer.Serialize(new { IsComplete = true, ServerSelection = 2, ServiceId = (string?)null,
            Updates = new[] { new { Id = "9d1fa4a8-a21a-4cc9-84a1-42d7428a46d8:2", Title = "Fixture", Manufacturer = "Vendor", DeviceName = "FixtureDevice", RequiresEula = true, EulaText = license, DriverDate = "2025-11-04" } }, Warnings = Array.Empty<string>() });
        var candidate = Assert.Single(WindowsUpdateService.ParseDriverUpdatesPayload(payload).Updates);
        Assert.Equal(license, candidate.EulaText);
        Assert.Equal(MaintenanceRequestProtocol.ComputeTextSha256(license), MaintenanceRequestProtocol.ComputeTextSha256(candidate.EulaText!));
        Assert.NotEqual(MaintenanceRequestProtocol.ComputeTextSha256(license.Trim()), MaintenanceRequestProtocol.ComputeTextSha256(candidate.EulaText!));
    }
}

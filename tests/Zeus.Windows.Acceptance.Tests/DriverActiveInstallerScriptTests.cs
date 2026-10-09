using System.Diagnostics;
using System.Reflection;

namespace Zeus.Windows.Acceptance.Tests;

public sealed class DriverActiveInstallerScriptTests
{
    [Fact]
    public async Task ActualCheckpointWriterPreservesBeforeAndProducesReadableEvidenceWithoutInstalling()
    {
        var assembly = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "MaintenanceContract", "Zeus.Maintenance.dll"));
        var script = (string)assembly.GetType("Zeus.Maintenance.CommandRunner", true)!.GetField("DriverInstallScript", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var begin = script.IndexOf("function Save-ZeusDriverObservation", StringComparison.Ordinal);
        var end = script.IndexOf("$session =", begin, StringComparison.Ordinal);
        var root = Path.Combine(Path.GetTempPath(), "Zeus.CheckpointWriter." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, Zeus.Windows.SessionStore.GetDriverActiveCheckpointFileName(Guid.NewGuid(), 1));
        try
        {
            File.WriteAllBytes(path, []);
            var fixture = new Zeus.Core.DriverActiveEvidence("PCI\\FIXTURE", new(DateTimeOffset.UtcNow.AddSeconds(-1), true,
                [new("PCI\\FIXTURE\\1", "oem1.inf", "1.0", "Fornecedor ç", true)], []));
            var payload = Convert.ToBase64String(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(fixture));
            var encodedPath = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(path));
            var command = "$ErrorActionPreference='Stop'; $activeObservationPath=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('" + encodedPath + "')); " +
                script[begin..end] + " $fixture=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('" + payload + "')) | ConvertFrom-Json; " +
                "Save-ZeusDriverObservation $fixture.HardwareId $fixture.Before $null; " +
                "$after=$fixture.Before.PSObject.Copy(); $after.CheckedAt=[DateTimeOffset]::UtcNow.ToString('o'); " +
                "Save-ZeusDriverObservation $fixture.HardwareId $fixture.Before $after";
            var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"))
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-NonInteractive"); start.ArgumentList.Add("-Command");
            start.ArgumentList.Add("Invoke-Expression ([Console]::In.ReadToEnd())");
            using var process = Process.Start(start)!;
            await process.StandardInput.WriteAsync(command); process.StandardInput.Close();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
                Assert.True(process.ExitCode == 0, await process.StandardError.ReadToEndAsync());
            }
            finally { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); } }
            var parsed = (Zeus.Core.DriverActiveEvidence)typeof(Zeus.Windows.SessionStore)
                .GetMethod("ParseDriverActiveCheckpoint", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [await File.ReadAllBytesAsync(path)])!;
            Assert.Equal(fixture.HardwareId, parsed.HardwareId);
            Assert.Equal(fixture.Before.CheckedAt, parsed.Before.CheckedAt);
            Assert.Equal("Fornecedor ç", Assert.Single(parsed.Before.Devices).Provider);
            Assert.NotNull(parsed.After);
            Assert.Null(parsed.Latest);
            Assert.False(File.Exists(path + ".pending"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task ActualInstallScriptParsesAndRequiresCheckpointBeforeDownloadWithoutRunningInstaller()
    {
        var assembly = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "MaintenanceContract", "Zeus.Maintenance.dll"));
        var type = assembly.GetType("Zeus.Maintenance.CommandRunner", throwOnError: true)!;
        var script = Assert.IsType<string>(type.GetField("DriverInstallScript", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null));
        var before = script.IndexOf("Save-ZeusDriverObservation $activeHardwareId $activeBefore $null", StringComparison.Ordinal);
        var download = script.IndexOf("$downloader.Download()", StringComparison.Ordinal);
        var install = script.IndexOf("$installer.Install()", StringComparison.Ordinal);
        var after = script.IndexOf("Get-ZeusActiveDriverSnapshot $activeHardwareId $frozenDeviceIds", StringComparison.Ordinal);
        Assert.True(before >= 0 && before < download && download < install && install < after);
        Assert.True(script.IndexOf("ZEUS_DRIVER_REBOOT_REQUIRED", StringComparison.Ordinal) < after);
        Assert.Contains("ZEUS_DRIVER_ACTIVE_CAPTURE_INCOMPLETE", script);
        Assert.Contains("$stream.Flush($true)", script);
        Assert.Contains("[System.IO.File]::Replace", script);
        Assert.Contains("$activeBefore.IsComplete", script);
        Assert.True(script.IndexOf("ZEUS_DRIVER_BASELINE_BLOCKED", StringComparison.Ordinal) < download);
        Assert.Contains("$update.DriverHardwareID", script);
        Assert.DoesNotContain("::HashData", script);
        Assert.DoesNotContain("::ToHexString", script);
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-NonInteractive"); start.ArgumentList.Add("-Command");
        start.ArgumentList.Add("$source=[Console]::In.ReadToEnd(); $tokens=$null; $errors=$null; [void][System.Management.Automation.Language.Parser]::ParseInput($source,[ref]$tokens,[ref]$errors); if($errors.Count -gt 0){[Console]::Error.WriteLine(($errors | ForEach-Object Message) -join '; '); exit 1}; exit 0");
        using var process = Process.Start(start)!;
        await process.StandardInput.WriteAsync(script); process.StandardInput.Close();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            var errors = await process.StandardError.ReadToEndAsync();
            Assert.True(process.ExitCode == 0, "O parser oficial do PowerShell deve aceitar o script sem executar instalação: " + errors);
        }
        finally { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); } }
    }

    [Fact]
    public async Task ActualEulaHashBlockRunsInTrustedWindowsPowerShellAndMatchesUtf8Sha256()
    {
        var assembly = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "MaintenanceContract", "Zeus.Maintenance.dll"));
        var script = (string)assembly.GetType("Zeus.Maintenance.CommandRunner", true)!.GetField("DriverInstallScript", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var hashStart = script.IndexOf("$eulaBytes =", StringComparison.Ordinal);
        var hashEnd = script.IndexOf("if (-not [string]::Equals($actualEulaTextSha256", hashStart, StringComparison.Ordinal);
        var hashBlock = script[hashStart..hashEnd];
        const string text = "ZEUS licença de teste ç 漢字";
        var encodedText = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(text));
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-NonInteractive"); start.ArgumentList.Add("-Command");
        start.ArgumentList.Add("$ErrorActionPreference='Stop'; $update=[pscustomobject]@{EulaText=[System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('" + encodedText + "'))}; " + hashBlock + " [Console]::WriteLine($actualEulaTextSha256)");
        using var process = Process.Start(start)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            Assert.Equal(0, process.ExitCode);
            Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text))), (await process.StandardOutput.ReadToEndAsync()).Trim());
        }
        finally { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); } }
    }
}

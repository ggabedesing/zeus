using System.Diagnostics;
using System.Text;

namespace Zeus.Windows.Acceptance.Tests;

public sealed class WorkerBoundaryTests
{
    public static IEnumerable<object[]> InvalidArguments()
    {
        yield return [Array.Empty<string>()];
        yield return [new[] { "--session", "not-a-guid", "--actions", "VerifySystemFiles" }];
        yield return [new[] { "--session", Guid.Empty.ToString("D"), "--actions", "VerifySystemFiles" }];
        yield return [new[] { "--session", Guid.NewGuid().ToString("D"), "--actions", "UnknownAction" }];
        yield return [new[] { "--session", Guid.NewGuid().ToString("D"), "--actions", "2" }];
        yield return [new[] { "--session", Guid.NewGuid().ToString("D"), "--actions", "verifySystemFiles" }];
        yield return [new[] { "--session", Guid.NewGuid().ToString("D"), "--actions", "VerifySystemFiles,VerifySystemFiles" }];
        yield return [new[] { "--session", Guid.NewGuid().ToString("D"), "--actions", "VerifySystemFiles;whoami" }];
        yield return [new[] { "--session", Guid.NewGuid().ToString("D"), "--command", "cmd.exe" }];
        yield return [new[] { "--session", Guid.NewGuid().ToString("D"), "--actions", "" }];
        yield return [new[] { "--session", Guid.NewGuid().ToString("D"), "--requests", "%%%invalid-base64%%%" }];
        foreach (var payload in new[]
        {
            "{}",
            "[]",
            "[{\"Action\":2}]",
            "[{\"Action\":\"UnknownAction\"}]",
            "[{\"Action\":\"InstallDriverUpdate\"}]",
            "[{\"Action\":\"VerifySystemFiles\",\"Command\":\"cmd.exe\"}]",
            "[{\"Action\":\"VerifySystemFiles\",\"Action\":\"RepairSystemFiles\"}]",
            "[{\"Action\":\"VerifySystemFiles\",\"TargetId\":\"C:\\\\Windows\\\\System32\\\\cmd.exe\"}]",
            "[{\"Action\":\"VerifySystemFiles\",\"EulaAccepted\":true}]",
            "[{\"Action\":\"InstallDriverUpdate\",\"TargetId\":\"12345678-1234-1234-1234-123456789abc:1;whoami\"}]",
            "[{\"Action\":\"InstallDriverUpdate\",\"TargetId\":\"12345678-1234-1234-1234-123456789abc:01\"}]",
            "[{\"Action\":\"InstallDriverUpdate\",\"TargetId\":\"00000000-0000-0000-0000-000000000000:1\"}]"
        })
            yield return [new[] { "--session", Guid.NewGuid().ToString("D"), "--requests", Convert.ToBase64String(Encoding.UTF8.GetBytes(payload)) }];
    }

    [Theory]
    [MemberData(nameof(InvalidArguments))]
    public async Task MalformedWorkerRequestsAreRejectedBeforeSessionCreation(string[] arguments)
    {
        Assert.True(OperatingSystem.IsWindows(), "Run worker boundary tests on Windows.");
        var validSession = arguments.Length >= 2 && Guid.TryParseExact(arguments[1], "D", out var candidate)
            ? candidate : Guid.Empty;
        var sessionDirectory = validSession != Guid.Empty
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Zeus", "Sessions", validSession.ToString("D"))
            : null;
        if (sessionDirectory is not null) Assert.False(Directory.Exists(sessionDirectory));
        var helper = Path.Combine(AppContext.BaseDirectory, "MaintenanceContract", "Zeus.Maintenance.dll");
        Assert.True(File.Exists(helper), "The real maintenance worker must be built before this test.");
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = AppContext.BaseDirectory
        };
        start.ArgumentList.Add(helper);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the actual worker.");
        // Invoke the .dll through dotnet so a requireAdministrator apphost cannot
        // trigger UAC. Every case is malformed: no repair or scan is requested.
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20)); }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }
        await Task.WhenAll(output, error);
        Assert.Equal(2, process.ExitCode);
        if (sessionDirectory is not null) Assert.False(Directory.Exists(sessionDirectory), "An invalid request must not create a privileged session.");
    }
}

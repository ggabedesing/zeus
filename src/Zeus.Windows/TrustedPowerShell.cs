using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

[assembly: InternalsVisibleTo("Zeus.Maintenance")]

namespace Zeus.Windows;

internal enum WindowsPowerShellModule { Management, Utility, Defender, Storage }

/// <summary>Loads only explicit Windows module manifests; no user-controlled module discovery.</summary>
internal static class TrustedPowerShell
{
    internal static ProcessStartInfo Create(string script, params WindowsPowerShellModule[] modules)
    {
        var home = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0");
        var moduleRoot = Path.Combine(home, "Modules");
        var imports = new StringBuilder();
        foreach (var module in modules.Distinct())
        {
            var name = module switch
            {
                WindowsPowerShellModule.Management => "Microsoft.PowerShell.Management",
                WindowsPowerShellModule.Utility => "Microsoft.PowerShell.Utility",
                WindowsPowerShellModule.Defender => "Defender",
                WindowsPowerShellModule.Storage => "Storage",
                _ => throw new ArgumentOutOfRangeException(nameof(modules))
            };
            imports.Append("Microsoft.PowerShell.Core\\Import-Module -Name ([System.IO.Path]::Combine($PSHOME, 'Modules', '")
                .Append(name).Append("', '").Append(name).Append(".psd1')) -Force -ErrorAction Stop; ");
        }

        var trustedScript = "[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false); " +
            "$env:PSModulePath = [System.IO.Path]::Combine($PSHOME, 'Modules'); " +
            "$ErrorActionPreference = 'Stop'; try { " + imports + script +
            " } catch { [Console]::Error.WriteLine($_.ToString()); exit 1 }";
        var start = new ProcessStartInfo(Path.Combine(home, "powershell.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Environment.SystemDirectory
        };
        start.Environment["PSModulePath"] = moduleRoot;
        foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-Command", trustedScript })
            start.ArgumentList.Add(argument);
        return start;
    }
}

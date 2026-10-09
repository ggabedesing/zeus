namespace Zeus.Cleanup.Tests;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class SymbolicLinkFactAttribute : FactAttribute
{
    private static readonly Lazy<bool> CanCreateSymbolicLinks = new(ProbeSymbolicLinkPrivilege);

    public SymbolicLinkFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "Este teste valida o comportamento de links simbólicos no Windows.";
            return;
        }

        if (!CanCreateSymbolicLinks.Value)
            Skip = "O Windows não concedeu o privilégio para criar links simbólicos; a aceitação Windows elevada cobre este cenário.";
    }

    private static bool ProbeSymbolicLinkPrivilege()
    {
        var directory = Path.Combine(Path.GetTempPath(), "zeus-symlink-probe-" + Guid.NewGuid().ToString("N"));
        var target = Path.Combine(directory, "target.txt");
        var link = Path.Combine(directory, "link.txt");
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(target, "probe");
            File.CreateSymbolicLink(link, target);
            return true;
        }
        catch (IOException error) when (unchecked((uint)error.HResult) == 0x80070522U)
        {
            return false;
        }
        finally
        {
            if (File.Exists(link)) File.Delete(link);
            if (File.Exists(target)) File.Delete(target);
            Directory.Delete(directory);
        }
    }
}

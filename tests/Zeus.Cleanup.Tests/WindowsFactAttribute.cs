namespace Zeus.Cleanup.Tests;

public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "Verifica primitivas e caminhos do Windows no job Windows.";
    }
}

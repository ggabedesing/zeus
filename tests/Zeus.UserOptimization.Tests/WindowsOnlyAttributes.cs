namespace Zeus.UserOptimization.Tests;

public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "Este teste requer Windows.";
    }
}

public sealed class WindowsTheoryAttribute : TheoryAttribute
{
    public WindowsTheoryAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "Este teste requer Windows.";
    }
}

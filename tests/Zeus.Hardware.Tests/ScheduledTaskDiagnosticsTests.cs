using System.Text.Json;
using Zeus.Core;
using Zeus.Windows;

namespace Zeus.Hardware.Tests;

public sealed class ScheduledTaskDiagnosticsTests
{
    [Fact]
    public void LegacyInventoryDoesNotInventExecutionData()
    {
        var task = JsonSerializer.Deserialize<ScheduledTaskInfo>("""{"Name":"test","Path":"root","State":"Ready"}""")!;
        Assert.Null(task.LastTaskResult);
        Assert.Null(task.RuntimeInfoAvailable);
        Assert.Contains("indisponíveis", ScheduledTaskDiagnostics.Describe(task));
    }

    [Theory]
    [InlineData(0x00041301u, "em execução")]
    [InlineData(0x00041303u, "ainda não executou")]
    [InlineData(0x00041308u, "gatilho por evento")]
    public void SchedulerStatusIsDescribedWithoutTreatingItAsFailure(uint result, string expected)
    {
        var task = new ScheduledTaskInfo("test", "root", "Ready", result, RuntimeInfoAvailable: true);
        Assert.Contains(expected, ScheduledTaskDiagnostics.Describe(task));
        Assert.DoesNotContain("falha", ScheduledTaskDiagnostics.Describe(task));
    }

    [Theory]
    [InlineData(1u)]
    [InlineData(uint.MaxValue)]
    public void ApplicationExitCodeRetainsRawValueWithoutInventingCause(uint result)
    {
        var task = new ScheduledTaskInfo("test", "root", "Ready", result, RuntimeInfoAvailable: true);
        var description = ScheduledTaskDiagnostics.Describe(task);
        Assert.Contains($"0x{result:X8}", description);
        Assert.Contains("sem interpretação automática", description);
    }

    [Fact]
    public void ZeroWithoutRunTimeDoesNotProveTaskRan()
    {
        var task = new ScheduledTaskInfo("test", "root", "Ready", 0, RuntimeInfoAvailable: true);
        Assert.Contains("não comprova que a tarefa executou", ScheduledTaskDiagnostics.Describe(task));
    }

    [Fact]
    public void RuntimeAssociationUsesFolderAndNameAndRejectsAmbiguousResults()
    {
        var tasks = new[] { new ScheduledTaskInfo("same", "a", "Ready"), new ScheduledTaskInfo("same", "b", "Running") };
        var runtime = new[] { new ScheduledTaskInfo("same", "A", "Disabled", 0, MissedRuns: 2, RuntimeInfoAvailable: true) };
        var merged = WindowsHardwareDiagnostics.MergeScheduledTaskRuntime(tasks, runtime);
        Assert.Equal(2u, merged[0].MissedRuns);
        Assert.Equal("Ready", merged[0].State);
        Assert.Null(merged[1].RuntimeInfoAvailable);
        var ambiguous = WindowsHardwareDiagnostics.MergeScheduledTaskRuntime(tasks, [runtime[0], runtime[0]]);
        Assert.Null(ambiguous[0].RuntimeInfoAvailable);
    }
}

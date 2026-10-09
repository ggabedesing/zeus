using System.Diagnostics;
using System.Text;
using Zeus.Windows;

namespace Zeus.Hardware.Tests;

public sealed class WindowsCollectorFactAttribute : FactAttribute
{
    public WindowsCollectorFactAttribute()
    {
        if(!OperatingSystem.IsWindows()) Skip="This fixture requires Windows native process containment and Windows PowerShell.";
    }
}

public sealed class PerformanceCollectorRunnerTests
{
    private static PerformanceCollectorRunner Fixture(string script) => new((_,_) =>
    {
        var info=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"System32","WindowsPowerShell","v1.0","powershell.exe"));
        info.ArgumentList.Add("-NoProfile"); info.ArgumentList.Add("-NonInteractive"); info.ArgumentList.Add("-EncodedCommand");
        info.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes("[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false);"+script)));
        return info;
    });
    private static string WritePartial()
    {
        var now=DateTimeOffset.UtcNow;
        var packet=new PerformanceCollectorPacket(1,false,new(PerformanceCollectorCategory.Disks,now,now,PerformanceCollectorState.Partial,[],Disks:[new("fixture",123,null,null)]));
        var data=Convert.ToBase64String(Encoding.UTF8.GetBytes(PerformanceCollectorProtocol.Serialize(packet)));
        return "[Console]::WriteLine([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('"+data+"')));[Console]::Out.Flush();";
    }
    [WindowsCollectorFact]
    public async Task TimeoutKeepsValidatedRows()
    {
        var result=await Fixture(WritePartial()+"Start-Sleep -Seconds 30").RunAsync(PerformanceCollectorCategory.Disks,TimeSpan.FromSeconds(2),default,TimeSpan.FromSeconds(2));
        Assert.Equal(PerformanceCollectorState.TimedOut,result.State); Assert.Equal(123UL,Assert.Single(result.Disks!).BytesPerSecond);
    }
    [WindowsCollectorFact]
    public async Task EofWithoutCompletedKeepsPartialAndDoesNotClaimSuccess()
    {
        var result=await Fixture(WritePartial()).RunAsync(PerformanceCollectorCategory.Disks,TimeSpan.FromSeconds(2),default);
        Assert.Equal(PerformanceCollectorState.Failed,result.State); Assert.Single(result.Disks!);
    }
    [WindowsCollectorFact]
    public async Task InvalidPacketKeepsPreviousValidatedRows()
    {
        var result=await Fixture(WritePartial()+"[Console]::WriteLine('{}');Start-Sleep -Seconds 30").RunAsync(PerformanceCollectorCategory.Disks,TimeSpan.FromSeconds(2),default);
        Assert.NotEqual(PerformanceCollectorState.Complete,result.State); Assert.Single(result.Disks!);
    }
    [WindowsCollectorFact]
    public async Task StdoutFloodStopsWithinBoundedTime()
    {
        var watch=Stopwatch.StartNew();
        var result=await Fixture("[Console]::Write(('x'*2200000));[Console]::Out.Flush();Start-Sleep -Seconds 30").RunAsync(PerformanceCollectorCategory.Disks,TimeSpan.FromSeconds(2),default);
        Assert.NotEqual(PerformanceCollectorState.Complete,result.State); Assert.True(watch.Elapsed<TimeSpan.FromSeconds(8));
    }
    [WindowsCollectorFact]
    public async Task CancellationKillsOwnedProcessAndPropagates()
    {
        var path=Path.Combine(Path.GetTempPath(),"zeus-observer-fixture-"+Guid.NewGuid().ToString("N")+".txt");
        try
        {
            using var cancellation=new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var task=Fixture("[IO.File]::WriteAllText('"+path.Replace("'","''")+"',[string]$PID);Start-Sleep -Seconds 30")
                .RunAsync(PerformanceCollectorCategory.Disks,TimeSpan.FromSeconds(2),cancellation.Token);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>task);
            if(File.Exists(path)) AssertGone(int.Parse(File.ReadAllText(path)));
        }
        finally { File.Delete(path); }
    }
    [WindowsCollectorFact]
    public async Task ExitedHostCannotLeaveDescendantHoldingPipes()
    {
        var path=Path.Combine(Path.GetTempPath(),"zeus-observer-child-"+Guid.NewGuid().ToString("N")+".txt");
        try
        {
            var script="$p=Start-Process -FilePath $env:ComSpec -ArgumentList '/c ping -n 30 127.0.0.1' -NoNewWindow -PassThru;[IO.File]::WriteAllText('"+path.Replace("'","''")+"',[string]$p.Id);";
            var result=await Fixture(script).RunAsync(PerformanceCollectorCategory.Disks,TimeSpan.FromSeconds(2),default,TimeSpan.FromSeconds(2));
            Assert.NotEqual(PerformanceCollectorState.Complete,result.State);
            Assert.True(File.Exists(path)); AssertGone(int.Parse(File.ReadAllText(path)));
        }
        finally { File.Delete(path); }
    }
    private static void AssertGone(int pid)
    {
        try { using var process=Process.GetProcessById(pid); Assert.True(process.HasExited); }
        catch(ArgumentException) { }
    }
}

using Zeus.Windows;

namespace Zeus.Hardware.Tests;

public sealed class PerformanceCollectorProtocolTests
{
    private static PerformanceCollectorEnvelope Envelope(PerformanceCollectorCategory category) =>
        new(category,DateTimeOffset.UtcNow.AddSeconds(-2),DateTimeOffset.UtcNow,PerformanceCollectorState.Complete,[]);
    [Fact]
    public void RoundTripKeepsCollectorTimesAndNullableFields()
    {
        var d=Envelope(PerformanceCollectorCategory.CpuProcesses) with { Processes=[new(12,"obs64",null,200,123)] };
        var result=PerformanceCollectorProtocol.Parse(PerformanceCollectorProtocol.Serialize(new(1,true,d)),d.Category);
        Assert.Null(result.Data.CpuPercent); Assert.Equal(d.StartedAt,result.Data.StartedAt);
        Assert.Equal(123,result.Data.Processes![0].StartTimeUtcTicks);
    }
    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("not-json")]
    public void RejectsMalformedEnvelope(string json) => Assert.ThrowsAny<Exception>(()=>PerformanceCollectorProtocol.Parse(json,PerformanceCollectorCategory.CpuProcesses));
    [Fact]
    public void RejectsMismatchedCategoryAndCrossCategoryPayload()
    {
        var d=Envelope(PerformanceCollectorCategory.GpuEngines) with { Processes=[] };
        Assert.Throws<InvalidDataException>(()=>PerformanceCollectorProtocol.Parse(PerformanceCollectorProtocol.Serialize(new(1,true,d)),d.Category));
        Assert.Throws<InvalidDataException>(()=>PerformanceCollectorProtocol.Parse(PerformanceCollectorProtocol.Serialize(new(1,true,Envelope(PerformanceCollectorCategory.Disks))),d.Category));
    }
    [Fact]
    public void RejectsOutOfBoundsAndDuplicatePayloadTimes()
    {
        var d=Envelope(PerformanceCollectorCategory.CpuProcesses) with { CpuPercent=101 };
        Assert.Throws<InvalidDataException>(()=>PerformanceCollectorProtocol.Parse(PerformanceCollectorProtocol.Serialize(new(1,true,d)),d.Category));
        d=d with { CpuPercent=null, FinishedAt=d.StartedAt.AddSeconds(-1) };
        Assert.Throws<InvalidDataException>(()=>PerformanceCollectorProtocol.Parse(PerformanceCollectorProtocol.Serialize(new(1,true,d)),d.Category));
        d=Envelope(PerformanceCollectorCategory.GpuEngines) with { GpuEngines=Enumerable.Range(0,201).Select(i=>new GpuEngineObservation(i.ToString(),null,"3D",0)).ToArray() };
        Assert.Throws<InvalidDataException>(()=>PerformanceCollectorProtocol.Parse(PerformanceCollectorProtocol.Serialize(new(1,true,d)),d.Category));
    }
    [Fact]
    public void ProgressiveRowsAppendAndKeepCpuSummary()
    {
        var d=Envelope(PerformanceCollectorCategory.CpuProcesses) with { CpuPercent=23,SamplingDuration=TimeSpan.FromSeconds(2) };
        var row=Envelope(d.Category) with { Processes=[new(1,"one",null,1)] };
        var combined=PerformanceCollectorProtocol.Append(d,row);
        Assert.Equal(23,combined.CpuPercent); Assert.Equal(d.SamplingDuration,combined.SamplingDuration);
        Assert.Single(combined.Processes!);
    }
    [Fact]
    public void MergeUsesAllProcessesForGpuMappingAndKeepsIndependentIoTop()
    {
        var processes=Enumerable.Range(1,60).Select(i=>new ProcessObservation(i,i==60?"obs64":"p"+i, i==60?0:80,100,
            1000+i,IoReadBytesPerSecond:i==60?9000:0,IoSamplingDurationSeconds:2)).ToArray();
        var cpu=Envelope(PerformanceCollectorCategory.CpuProcesses) with { Processes=processes, CpuPercent=80,SamplingDuration=TimeSpan.FromSeconds(2) };
        var gpu=Envelope(PerformanceCollectorCategory.GpuEngines) with { GpuEngines=[new("pid_60_engtype_VideoEncode",60,"VideoEncode",15)] };
        var memory=Envelope(PerformanceCollectorCategory.GpuProcessMemory) with { GpuProcessMemory=[new("pid_60_luid_0","luid_0",60,null,1060,1,2,null,null,3)] };
        var result=WindowsPerformanceProbe.MergeCollectors([cpu,gpu,memory]);
        Assert.Equal(50,result.Processes.Count); Assert.DoesNotContain(result.Processes,p=>p.Id==60);
        Assert.Equal(60,result.IoProcesses![0].Id); Assert.Equal("obs64",result.GpuEngines![0].ProcessName);
        Assert.Equal(1060,result.GpuProcessMemory![0].ProcessStartTimeUtcTicks);
        Assert.Equal(3,result.Collectors!.Count); Assert.Equal(cpu.SamplingDuration,result.SamplingDuration);
    }
    [Fact]
    public void MergeDoesNotAssociateReusedGpuPidWithCpuEpoch()
    {
        var cpu=Envelope(PerformanceCollectorCategory.CpuProcesses) with { Processes=[new(60,"obs64",1,100,1000)] };
        var gpu=Envelope(PerformanceCollectorCategory.GpuProcessMemory) with { GpuProcessMemory=[new("fixture","luid_0",60,null,2000,1,2,null,null,3)] };
        var merged=WindowsPerformanceProbe.MergeCollectors([cpu,gpu]);
        Assert.Null(merged.GpuProcessMemory![0].ProcessName); Assert.Equal(2000,merged.GpuProcessMemory[0].ProcessStartTimeUtcTicks);
    }
    [Fact]
    public void FailedCollectorsNeverProduceFakeCpuZero()
    {
        var result=WindowsPerformanceProbe.MergeCollectors([Envelope(PerformanceCollectorCategory.CpuProcesses) with { State=PerformanceCollectorState.TimedOut }]);
        Assert.Null(result.CpuPercent); Assert.Empty(result.Processes);
        Assert.Equal(PerformanceCollectorState.TimedOut,Assert.Single(result.Collectors!).State);
    }
    [Fact]
    public void StartInfoUsesFixedHostAndStrictArguments()
    {
        var info=PerformanceCollectorRunner.StartInfo(PerformanceCollectorCategory.GpuEngines,TimeSpan.FromSeconds(2));
        Assert.Equal(Path.Combine(AppContext.BaseDirectory,"Zeus.Observer.exe"),info.FileName);
        Assert.Equal(new[]{"--collector","GpuEngines","--duration","2"},info.ArgumentList.ToArray());
    }
    [Fact]
    public void MissingMandatoryOrDuplicateFieldsCannotInventDefaults()
    {
        var packet=new PerformanceCollectorPacket(1,true,Envelope(PerformanceCollectorCategory.GpuEngines) with { GpuEngines=[new("fixture",null,"3D",0)] });
        var json=PerformanceCollectorProtocol.Serialize(packet);
        foreach(var field in new[]{ "Version", "Category", "State", "UtilizationPercent" })
        {
            var node=System.Text.Json.Nodes.JsonNode.Parse(json)!;
            if(field=="Version") node.AsObject().Remove(field);
            else if(field=="UtilizationPercent") node["Data"]!["GpuEngines"]![0]!.AsObject().Remove(field);
            else node["Data"]!.AsObject().Remove(field);
            Assert.ThrowsAny<Exception>(()=>PerformanceCollectorProtocol.Parse(node.ToJsonString(),packet.Data.Category));
        }
        Assert.Throws<InvalidDataException>(()=>PerformanceCollectorProtocol.Parse(json.Replace("\"Version\":1","\"Version\":1,\"Version\":1"),packet.Data.Category));
        Assert.ThrowsAny<Exception>(()=>PerformanceCollectorProtocol.Parse(json.Replace("\"Version\":1","\"Version\":1,\"Unexpected\":false"),packet.Data.Category));
    }
    [Theory]
    [InlineData("cpu")]
    [InlineData("0")]
    [InlineData("GpuEngines;whoami")]
    public async Task HostRejectsUnrecognizedNamesWithoutCollection(string category) => Assert.Equal(2,await PerformanceCollectorHost.RunAsync(["--collector",category,"--duration","2"]));
}

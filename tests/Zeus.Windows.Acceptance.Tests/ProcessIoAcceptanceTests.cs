using System.Diagnostics;
using Zeus.Desktop;
using Zeus.Windows;

namespace Zeus.Windows.Acceptance.Tests;

public sealed class ProcessIoAcceptanceTests
{
    [Fact]
    public void NativeIoCountersObserveOwnedTemporaryFileReadAndWrite()
    {
        Assert.True(OperatingSystem.IsWindows());
        var path = Path.Combine(Path.GetTempPath(), $"zeus-io-acceptance-{Guid.NewGuid():N}.tmp");
        var before = ProcessIoReader.Read(Environment.ProcessId);
        Assert.True(before.HasValue, "Current-process I/O counters must be readable on the Windows test host.");
        using var process = Process.GetCurrentProcess();
        Assert.Equal(process.StartTime.ToUniversalTime().Ticks, before.Value.StartTimeUtcTicks);
        const int payloadLength = 1024 * 1024;
        try
        {
            using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                file.Write(new byte[payloadLength]);
                file.Flush(flushToDisk: true);
            }
            Assert.Equal(payloadLength, File.ReadAllBytes(path).Length);
            var after = ProcessIoReader.Read(Environment.ProcessId);
            Assert.True(after.HasValue);
            Assert.Equal(before.Value.StartTimeUtcTicks, after.Value.StartTimeUtcTicks);
            Assert.True(after.Value.ReadBytes >= before.Value.ReadBytes + payloadLength);
            Assert.True(after.Value.WrittenBytes >= before.Value.WrittenBytes + payloadLength);
            var rates = ProcessIoReader.CalculateRates(before, after);
            Assert.True(rates.ReadBytesPerSecond > 0);
            Assert.True(rates.WriteBytesPerSecond > 0);
            Assert.True(rates.SamplingDurationSeconds > 0);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void PresentationDistinguishesMissingIoFromObservedZeroAndShowsCoverage()
    {
        var legacy = new ProcessObservation(7, "fixture", 1, 1024, 1234);
        Assert.Contains("leitura indisponível", MainWindow.FormatProcessIo(legacy));
        var observed = legacy with { IoReadBytesPerSecond = 0, IoWriteBytesPerSecond = 1024, IoSamplingDurationSeconds = 2 };
        Assert.Contains("leitura 0 B/s", MainWindow.FormatProcessIo(observed));
        Assert.Contains("intervalo do processo: 2", MainWindow.FormatProcessIo(observed));
        var reference = Sample(observed);
        var later = Sample(legacy);
        var description = MainWindow.FormatProcessIoComparison(PerformanceComparisonBuilder.Compare([reference], [later]));
        Assert.Contains("inclui rede/dispositivos", description);
        Assert.Contains("(1/1)", description);
        Assert.Contains("(0/1)", description);
        Assert.Contains("indisponível", description);
    }

    private static PerformanceObservation Sample(ProcessObservation process) =>
        new(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(2), 1, 1024, 512, [], [], IoProcesses: [process]);
}

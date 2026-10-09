using System.Diagnostics;
using System.Text;

namespace Zeus.Windows;

/// <summary>Fixed host contract; process factory is internal solely for fixture tests.</summary>
internal sealed class PerformanceCollectorRunner
{
    private readonly Func<PerformanceCollectorCategory, TimeSpan, ProcessStartInfo> factory;
    internal PerformanceCollectorRunner(Func<PerformanceCollectorCategory, TimeSpan, ProcessStartInfo>? factory = null) => this.factory = factory ?? StartInfo;
    internal static ProcessStartInfo StartInfo(PerformanceCollectorCategory category, TimeSpan duration)
    {
        var info = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "Zeus.Observer.exe"));
        info.ArgumentList.Add("--collector"); info.ArgumentList.Add(category.ToString());
        info.ArgumentList.Add("--duration"); info.ArgumentList.Add(((int)Math.Ceiling(duration.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture));
        return info;
    }
    internal async Task<PerformanceCollectorEnvelope> RunAsync(PerformanceCollectorCategory category, TimeSpan duration, CancellationToken token, TimeSpan? testTimeout = null)
    {
        token.ThrowIfCancellationRequested();
        var started = DateTimeOffset.UtcNow;
        PerformanceCollectorEnvelope? partial = null;
        var sequence = 0;
        var completed = false;
        var partialGate = new object();
        var info = factory(category, duration);
        info.UseShellExecute = false; info.CreateNoWindow = true;
        info.RedirectStandardOutput = true; info.RedirectStandardError = true;
        info.StandardOutputEncoding = new UTF8Encoding(false, true);
        PerformanceCollectorProcess owned;
        try { owned=PerformanceCollectorProcess.Start(info); }
        catch(Exception error) { token.ThrowIfCancellationRequested(); return Failure(PerformanceCollectorState.Unavailable,$"Falha ao iniciar ou conter o coletor ({error.GetType().Name})."); }
        using var ownedProcess=owned;
        var process=owned.Process;
        var ownedJob=owned.Job;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(testTimeout ?? (category == PerformanceCollectorCategory.CpuProcesses ? duration + TimeSpan.FromSeconds(10) : TimeSpan.FromSeconds(10)));
        var output = ReadPacketsAsync();
        var errors = ReadErrorAsync();
        var exit = process.WaitForExitAsync(deadline.Token);
        _ = output.ContinueWith(_ => deadline.Cancel(), CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        _ = errors.ContinueWith(_ => deadline.Cancel(), CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        try
        {
            // A stream limit/protocol failure must interrupt the process immediately, rather than wait for its deadline.
            var first = await Task.WhenAny(Task.WhenAll(output, errors, exit), output, errors);
            if (first.IsFaulted) await first;
            await Task.WhenAll(output, errors, exit);
            token.ThrowIfCancellationRequested();
            if (process.ExitCode != 0 || !completed) return Failure(PerformanceCollectorState.Failed,"Coletor terminou sem confirmação completa; leituras já recebidas foram preservadas.");
            lock (partialGate) return partial!;
        }
        catch (OperationCanceledException)
        {
            await StopAsync();
            token.ThrowIfCancellationRequested();
            return Failure(output.IsFaulted || errors.IsFaulted ? PerformanceCollectorState.Failed : PerformanceCollectorState.TimedOut,
                output.IsFaulted || errors.IsFaulted ? "A saída do coletor foi rejeitada; linhas validadas foram preservadas." : "Prazo do coletor excedido; processo encerrado e leituras parciais preservadas.");
        }
        catch (Exception error)
        {
            await StopAsync();
            token.ThrowIfCancellationRequested();
            return Failure(PerformanceCollectorState.Failed,$"Saída do coletor interrompida ({error.GetType().Name}); somente leituras validadas foram preservadas.");
        }
        async Task StopAsync()
        {
            ownedJob.Dispose();
            try { if (!process.HasExited) process.Kill(entireProcessTree:true); }
            catch (InvalidOperationException) { }
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { await process.WaitForExitAsync(cleanup.Token); }
            catch (OperationCanceledException) { throw new IOException("Não foi possível confirmar o encerramento do coletor."); }
            deadline.Cancel();
            try { await Task.WhenAll(output,errors).WaitAsync(cleanup.Token); } catch (Exception) { /* Stopped process may close a partial JSON line. */ }
        }
        PerformanceCollectorEnvelope Failure(PerformanceCollectorState state,string warning)
        {
            lock(partialGate)
                return (partial ?? new(category,started,DateTimeOffset.UtcNow,state,[])) with
                { FinishedAt = DateTimeOffset.UtcNow, State = state, Warnings = (partial?.Warnings ?? []).Append(warning).Take(128).ToArray() };
        }
        async Task ReadPacketsAsync()
        {
            var buffer = new char[4096]; var line = new StringBuilder(); var bytes = 0;
            while (true)
            {
                var count = await owned.Output.ReadAsync(buffer.AsMemory(),deadline.Token);
                if (count == 0) break;
                bytes += Encoding.UTF8.GetByteCount(buffer,0,count);
                if (bytes > PerformanceCollectorProtocol.MaximumBytes) throw new InvalidDataException("Limite stdout excedido.");
                for(var i=0;i<count;i++)
                {
                    if(buffer[i] == '\n') { Consume(line.ToString().TrimEnd('\r')); line.Clear(); }
                    else line.Append(buffer[i]);
                }
            }
            if(line.Length > 0) Consume(line.ToString());
        }
        void Consume(string line)
        {
            var packet = PerformanceCollectorProtocol.Parse(line, category);
            if(completed || packet.Sequence != sequence+1) throw new InvalidDataException("Sequência do coletor inválida.");
            sequence = packet.Sequence;
            lock(partialGate)
            {
                var next = packet.Completed ? packet.Data : PerformanceCollectorProtocol.Append(partial,packet.Data);
                // Revalidate accumulated lists: individual rows cannot bypass the category bound.
                PerformanceCollectorProtocol.Parse(PerformanceCollectorProtocol.Serialize(new(sequence,packet.Completed,next)),category);
                partial = next;
                completed = packet.Completed;
            }
        }
        async Task ReadErrorAsync()
        {
            var buffer=new char[1024]; var count=0;
            while(true) { var read=await owned.Error.ReadAsync(buffer.AsMemory(),deadline.Token); if(read==0) return; count+=Encoding.UTF8.GetByteCount(buffer,0,read); if(count>16384) throw new InvalidDataException("Limite stderr excedido."); }
        }
    }
}

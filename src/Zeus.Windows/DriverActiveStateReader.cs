using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Zeus.Core;

namespace Zeus.Windows;

/// <summary>Reads the driver actually associated with exact PnP hardware/compatible IDs.</summary>
public static class DriverActiveStateReader
{
    // Shared with the elevated installer so before/after uses the same association algorithm.
    internal const string CaptureFunctionScript = """
function Get-ZeusActiveDriverSnapshot($hardwareId,$deviceInstanceIds=$null) {
    $devices = [System.Collections.Generic.List[object]]::new()
    $warnings = [System.Collections.Generic.List[string]]::new()
    $complete = $true
    try {
        if ([string]::IsNullOrWhiteSpace($hardwareId) -or $hardwareId.Length -gt 1024 -or $hardwareId -match '[\x00-\x1F\x7F]') { throw 'Identificador de hardware invalido.' }
        [void][System.Reflection.Assembly]::LoadWithPartialName('System.Management')
        $options = [System.Management.EnumerationOptions]::new()
        $options.Timeout = [TimeSpan]::FromSeconds(8)
        $options.ReturnImmediately = $false
        $matched = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
        $presence = [System.Collections.Generic.Dictionary[string,object]]::new([System.StringComparer]::OrdinalIgnoreCase)
        $bound = $null -ne $deviceInstanceIds
        if ($bound) {
            if (@($deviceInstanceIds).Count -eq 0 -or @($deviceInstanceIds).Count -gt 64) { throw 'Lista de dispositivos invalida.' }
            foreach ($id in $deviceInstanceIds) {
                if ([string]::IsNullOrWhiteSpace($id) -or $id.Length -gt 1024 -or $id -match '[\x00-\x1F\x7F]' -or -not $matched.Add($id)) { throw 'Identificador de dispositivo invalido ou repetido.' }
            }
        }
        $pnpComplete = $true
        $searcher = [System.Management.ManagementObjectSearcher]::new([System.Management.ManagementScope]::new('root\cimv2'), [System.Management.ObjectQuery]::new('SELECT DeviceID,HardwareID,CompatibleID,Present FROM Win32_PnPEntity'), $options)
        try {
            $results = $searcher.Get()
            try {
                $count = 0
                foreach ($item in $results) {
                    try {
                        $count++
                        if ($count -gt 5000) { $complete = $false; $pnpComplete = $false; $warnings.Add('Inventario PnP excedeu o limite de 5000 dispositivos.'); break }
                        $deviceId = [string]$item['DeviceID']
                        $match = $bound -and $matched.Contains($deviceId)
                        if (-not $bound) {
                            foreach ($id in @($item['HardwareID']) + @($item['CompatibleID'])) {
                                if ([string]::Equals([string]$id, $hardwareId, [System.StringComparison]::OrdinalIgnoreCase)) { $match = $true; break }
                            }
                        }
                        if ($match) {
                            $deviceId = [string]$item['DeviceID']
                            if ([string]::IsNullOrWhiteSpace($deviceId) -or $deviceId.Length -gt 1024 -or $deviceId -match '[\x00-\x1F\x7F]') { $complete = $false; $warnings.Add('Identificador PnP indisponivel ou invalido.'); continue }
                            if ($matched.Count -ge 64 -and -not $matched.Contains($deviceId)) { $complete = $false; $warnings.Add('Correspondencias excederam o limite de 64 dispositivos.'); break }
                            [void]$matched.Add($deviceId)
                            $present = $item['Present']
                            $presence[$deviceId] = $(if($null -eq $present){$null}else{[bool]$present})
                            if ($null -eq $present) { $complete = $false; $warnings.Add('Presenca atual do dispositivo indisponivel.') }
                        }
                    } finally { $item.Dispose() }
                }
            } finally { $results.Dispose() }
        } finally { $searcher.Dispose() }
        if ($matched.Count -eq 0) { $complete = $false; $warnings.Add('Nenhum dispositivo corresponde exatamente ao hardware ID ou compatible ID informado.') }
        else {
            $found = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
            $searcher = [System.Management.ManagementObjectSearcher]::new([System.Management.ManagementScope]::new('root\cimv2'), [System.Management.ObjectQuery]::new('SELECT DeviceID,InfName,DriverVersion,DriverProviderName FROM Win32_PnPSignedDriver'), $options)
            try {
                $results = $searcher.Get()
                try {
                    $count = 0
                    foreach ($item in $results) {
                        try {
                            $count++
                            if ($count -gt 5000) { $complete = $false; $warnings.Add('Inventario de drivers excedeu o limite de 5000 registros.'); break }
                            $deviceId = [string]$item['DeviceID']
                            if (-not $matched.Contains($deviceId)) { continue }
                            if (-not $found.Add($deviceId)) { $complete = $false; $warnings.Add('Mais de um registro de driver foi encontrado para o mesmo dispositivo.'); continue }
                            $inf = [string]$item['InfName']; $version = [string]$item['DriverVersion']; $provider = [string]$item['DriverProviderName']
                            foreach ($value in @($inf,$version,$provider)) {
                                if ($value.Length -gt 1024 -or $value -match '[\x00-\x1F\x7F]') { throw 'Metadados de driver excedem os limites permitidos.' }
                            }
                            if ([string]::IsNullOrWhiteSpace($inf) -or [string]::IsNullOrWhiteSpace($version)) { $complete = $false; $warnings.Add('INF ou versao do driver ativo indisponivel.') }
                            $present = $(if($presence.ContainsKey($deviceId)){$presence[$deviceId]}elseif($pnpComplete){$false}else{$null})
                            $devices.Add([pscustomobject]@{ DeviceInstanceId=$deviceId; InfName=$(if($inf){$inf}else{$null}); Version=$(if($version){$version}else{$null}); Provider=$(if($provider){$provider}else{$null}); IsPresent=$present })
                        } finally { $item.Dispose() }
                    }
                } finally { $results.Dispose() }
            } finally { $searcher.Dispose() }
            if ($found.Count -ne $matched.Count) { $complete = $false; $warnings.Add('Nem todos os dispositivos correspondentes possuem associacao de driver disponivel.') }
            foreach ($deviceId in $matched) {
                if (-not $found.Contains($deviceId)) {
                    $present = $(if($presence.ContainsKey($deviceId)){$presence[$deviceId]}elseif($pnpComplete){$false}else{$null})
                    $devices.Add([pscustomobject]@{ DeviceInstanceId=$deviceId; InfName=$null; Version=$null; Provider=$null; IsPresent=$present })
                }
            }
        }
    } catch { $complete = $false; $warnings.Add('Falha ou tempo limite na consulta da associacao PnP/driver ativo.') }
    return [pscustomobject]@{ CheckedAt=[DateTimeOffset]::UtcNow.ToString('o'); IsComplete=$complete; Devices=@($devices.ToArray()); Warnings=@($warnings.ToArray() | Microsoft.PowerShell.Utility\Select-Object -First 15 -Unique) }
}
""";

    public static async Task<ActiveDriverSnapshot> CaptureAsync(string hardwareId, CancellationToken cancellationToken = default, IReadOnlyList<string>? deviceInstanceIds = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows() || !ValidText(hardwareId, false) || deviceInstanceIds is not null &&
            (deviceInstanceIds.Count is < 1 or > 64 || deviceInstanceIds.Any(id => !ValidText(id, false)) || deviceInstanceIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() != deviceInstanceIds.Count))
            return Unknown("Captura de driver ativo indisponivel: sistema ou identificador invalido.");
        // Input is JSON on stdin rather than command text: 64 long IDs can exceed Windows command-line limits.
        var script = CaptureFunctionScript + "\n[Console]::InputEncoding=[System.Text.UTF8Encoding]::new($false); $captureInput=Microsoft.PowerShell.Utility\\ConvertFrom-Json -InputObject ([Console]::In.ReadToEnd()); Get-ZeusActiveDriverSnapshot $captureInput.HardwareId $captureInput.DeviceInstanceIds | Microsoft.PowerShell.Utility\\ConvertTo-Json -Depth 6 -Compress";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var process = new Process { StartInfo = TrustedPowerShell.Create(script, WindowsPowerShellModule.Utility) };
        process.StartInfo.RedirectStandardInput = true;
        process.StartInfo.StandardInputEncoding = new UTF8Encoding(false);
        try
        {
            if (!process.Start()) return Unknown("Nao foi possivel iniciar a consulta de driver ativo.");
            using var registration = timeout.Token.Register(() => { try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { } });
            var output = ReadBoundedAsync(process.StandardOutput.BaseStream, timeout.Token);
            var error = ReadBoundedAsync(process.StandardError.BaseStream, timeout.Token);
            await process.StandardInput.WriteAsync(JsonSerializer.Serialize(new { HardwareId = hardwareId, DeviceInstanceIds = deviceInstanceIds }).AsMemory(), timeout.Token);
            process.StandardInput.Close();
            await Task.WhenAll(output, error, process.WaitForExitAsync(timeout.Token));
            return process.ExitCode == 0 ? Parse(await output) : Unknown("Consulta de driver ativo falhou.");
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException or InvalidOperationException or System.ComponentModel.Win32Exception or DecoderFallbackException)
        {
            await StopAsync(process);
            cancellationToken.ThrowIfCancellationRequested();
            return Unknown("Driver ativo indisponivel: consulta cancelada, limitada ou com falha.");
        }
    }

    internal static ActiveDriverSnapshot Parse(string payload)
    {
        try
        {
            if (Encoding.UTF8.GetByteCount(payload) > 128 * 1024) return Unknown("Resposta de driver ativo excedeu o limite.");
            var value = JsonSerializer.Deserialize<ActiveDriverSnapshot>(payload, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, MaxDepth = 12 });
            if (value is null || value.CheckedAt == default || value.Devices is null || value.Warnings is null || value.Devices.Count > 64 || value.Warnings.Count > 16 ||
                value.Devices.Any(d => d is null || !ValidText(d.DeviceInstanceId, false) || !ValidText(d.InfName, true) || !ValidText(d.Version, true) || !ValidText(d.Provider, true)) ||
                value.Warnings.Any(w => !ValidText(w, false)) || value.Devices.Select(d => d.DeviceInstanceId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != value.Devices.Count)
                return Unknown("Resposta de driver ativo invalida.");
            // A missing mapping/INF/version must never be reported as complete, even by malformed producers.
            if (value.IsComplete && (value.Devices.Count == 0 || value.Devices.Any(d => d.IsPresent is null || string.IsNullOrWhiteSpace(d.InfName) || string.IsNullOrWhiteSpace(d.Version))))
                return value with { IsComplete = false, Warnings = value.Warnings.Take(15).Concat(["Associacao ou versao do driver ativo indisponivel."]).ToArray() };
            return value;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or ArgumentException)
        { return Unknown("Resposta de driver ativo nao pode ser interpretada."); }
    }

    private static bool ValidText(string? value, bool optional) => value is null ? optional : value.Length <= 1024 && !value.Any(char.IsControl) && (optional || !string.IsNullOrWhiteSpace(value));
    private static ActiveDriverSnapshot Unknown(string warning) => new(DateTimeOffset.UtcNow, false, [], [warning]);

    private static async Task StopAsync(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await process.WaitForExitAsync(cleanupTimeout.Token);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or OperationCanceledException) { }
    }

    private static async Task<string> ReadBoundedAsync(Stream stream, CancellationToken token)
    {
        using var output = new MemoryStream();
        var buffer = new byte[4096];
        int count;
        while ((count = await stream.ReadAsync(buffer, token)) > 0)
        {
            if (output.Length + count > 128 * 1024) throw new IOException("Output limit exceeded.");
            output.Write(buffer, 0, count);
        }
        return new UTF8Encoding(false, true).GetString(output.ToArray());
    }
}

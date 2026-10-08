using System.Diagnostics;
using System.Text.Json;
using Zeus.Core;

namespace Zeus.Windows;

public sealed record DriverUpdateCandidate(string Id, string Title, string? Manufacturer,
    string? DeviceName, string? DriverVersion, bool RequiresEula, string? EulaText = null);

public sealed record DriverUpdateSearch(DateTimeOffset CheckedAt,
    IReadOnlyList<DriverUpdateCandidate> Updates, IReadOnlyList<string> Warnings);

/// <summary>Read-only discovery through the native Windows Update Agent and its configured trusted sources.</summary>
public sealed class WindowsUpdateService
{
    private const string SearchScript = """
        $session = [System.Activator]::CreateInstance([System.Type]::GetTypeFromProgID('Microsoft.Update.Session'));
        $session.ClientApplicationID = 'ZEUS';
        $warnings = [System.Collections.Generic.List[string]]::new();
        if ($session.CreateUpdateInstaller().IsBusy) {
            $warnings.Add('O Windows Update está ocupado com outra instalação. A busca é somente leitura; aguarde antes de instalar um driver.');
        };
        $search = $session.CreateUpdateSearcher().Search("IsInstalled=0 and Type='Driver' and IsHidden=0");
        if ([int]$search.ResultCode -ne 2) {
            $warnings.Add('O Windows Update retornou resultado parcial ou incompleto na busca (código ' + [int]$search.ResultCode + ').');
        };
        $drivers = [System.Collections.Generic.List[object]]::new();
        for ($i = 0; $i -lt $search.Updates.Count; $i++) {
            $update = $search.Updates.Item($i);
            $class = [string]$update.DriverClass;
            $title = [string]$update.Title;
            if ($class -match '(?i)firmware|bios|uefi' -or $title -match '(?i)firmware|\bbios\b|\buefi\b') { continue };
            $identity = [guid]$update.Identity.UpdateID;
            $revision = [int]$update.Identity.RevisionNumber;
            if ($identity -eq [guid]::Empty -or $revision -lt 1) { continue };
            $requiresEula = -not [bool]$update.EulaAccepted;
            $eula = $null;
            if ($requiresEula) { $eula = [string]$update.EulaText };
            $drivers.Add([pscustomobject]@{
                Id = $identity.ToString('D') + ':' + $revision;
                Title = $title;
                Manufacturer = [string]$update.DriverManufacturer;
                DeviceName = [string]$update.DriverModel;
                DriverVersion = $null;
                RequiresEula = $requiresEula;
                EulaText = $eula
            });
        };
        [pscustomobject]@{ Updates = @($drivers.ToArray()); Warnings = @($warnings.ToArray()) } |
            Microsoft.PowerShell.Utility\ConvertTo-Json -Depth 5 -Compress;
        """;

    public async Task<DriverUpdateSearch> SearchDriverUpdatesAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("A busca de drivers requer Windows.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        using var process = new Process
        {
            StartInfo = TrustedPowerShell.Create(SearchScript, WindowsPowerShellModule.Utility)
        };
        try
        {
            if (!process.Start()) throw new InvalidOperationException("A consulta ao Windows Update não iniciou.");
            var output = ReadBoundedAsync(process.StandardOutput, 2 * 1024 * 1024, timeout.Token);
            var errors = ReadBoundedAsync(process.StandardError, 16 * 1024, timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var payload = await output;
            var diagnostic = await errors;
            if (process.ExitCode != 0)
                return Failed("A consulta ao Windows Update falhou. " + diagnostic.Trim());
            var data = JsonSerializer.Deserialize<SearchPayload>(payload)
                ?? throw new InvalidDataException("Resposta vazia do Windows Update.");
            if (data.Updates is null || data.Warnings is null)
                throw new InvalidDataException("Resposta incompleta do Windows Update.");
            var warnings = data.Warnings.ToList();
            var drivers = new List<DriverUpdateCandidate>();
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var driver in data.Updates)
            {
                if (driver is null || !MaintenanceRequestProtocol.TryParseDriverIdentity(driver.Id, out _, out _) ||
                    string.IsNullOrWhiteSpace(driver.Title) || !ids.Add(driver.Id))
                {
                    warnings.Add("Uma oferta de driver foi ignorada porque sua identidade era inválida ou repetida.");
                    continue;
                }
                drivers.Add(driver);
            }
            warnings.Add("As ofertas seguem as fontes configuradas no Windows Update. Em notebooks, confira a recomendação do fabricante antes de instalar.");
            return new DriverUpdateSearch(DateTimeOffset.UtcNow, drivers.AsReadOnly(), warnings.AsReadOnly());
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failed("A busca excedeu três minutos. Confira conexão, políticas e o estado do Windows Update antes de tentar novamente.");
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return Failed("Não foi possível consultar drivers pelo Windows Update: " + exception.Message);
        }
        finally
        {
            // Only this read-only search process can be terminated; installation uses a separate untimed helper.
            try { if (process.Id > 0 && !process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
        }
    }

    private static DriverUpdateSearch Failed(string message) =>
        new(DateTimeOffset.UtcNow, [], [message]);

    private static async Task<string> ReadBoundedAsync(StreamReader reader, int maximumCharacters, CancellationToken cancellationToken)
    {
        var output = new System.Text.StringBuilder();
        var buffer = new char[4096];
        while (await reader.ReadAsync(buffer.AsMemory(), cancellationToken) is var count && count > 0)
        {
            if (output.Length + count > maximumCharacters)
                throw new InvalidDataException("A resposta do Windows Update excede o tamanho permitido.");
            output.Append(buffer, 0, count);
        }
        return output.ToString();
    }

    private sealed record SearchPayload(IReadOnlyList<DriverUpdateCandidate> Updates, IReadOnlyList<string> Warnings);
}

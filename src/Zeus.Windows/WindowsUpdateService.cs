using System.Diagnostics;
using System.Text.Json;
using Zeus.Core;

namespace Zeus.Windows;

public sealed record DriverUpdateCandidate(string Id, string Title, string? Manufacturer,
    string? DeviceName, string? DriverVersion, bool RequiresEula, string? EulaText = null,
    string? DriverProvider = null, string? DriverClass = null, DateOnly? DriverDate = null,
    int? UpdateServerSelection = null, string? UpdateServiceId = null);

public sealed record DriverUpdateSearch(DateTimeOffset CheckedAt,
    IReadOnlyList<DriverUpdateCandidate> Updates, IReadOnlyList<string> Warnings);

public sealed record PendingWindowsUpdate(string Title, IReadOnlyList<string> KnowledgeBaseIds, bool Downloaded, string UpdateId);
public sealed record PendingWindowsUpdateSearch(DateTimeOffset CheckedAt, bool IsComplete,
    IReadOnlyList<PendingWindowsUpdate> Updates, IReadOnlyList<string> Warnings);
public sealed record WindowsUpdateHistoryEntry(DateTimeOffset Date, string Title, string Operation, string Result, string? HResult);
public sealed record WindowsUpdateHistorySearch(DateTimeOffset CheckedAt, bool IsComplete,
    IReadOnlyList<WindowsUpdateHistoryEntry> Entries, IReadOnlyList<string> Warnings);
public sealed record InstalledDriverUpdateVerification(DateTimeOffset CheckedAt, bool IsComplete, bool? IsInstalled,
    bool? SourceMatches, IReadOnlyList<string> Warnings);

/// <summary>Read-only discovery through the native Windows Update Agent and its configured trusted sources.</summary>
public sealed class WindowsUpdateService
{
    public const int WindowsUpdateHistoryLimit = 50;

    private const string SearchHistoryScript = """
        $session = [System.Activator]::CreateInstance([System.Type]::GetTypeFromProgID('Microsoft.Update.Session'));
        $session.ClientApplicationID = 'ZEUS';
        $searcher = $session.CreateUpdateSearcher();
        $history = $searcher.QueryHistory(0, 50);
        $entries = [System.Collections.Generic.List[object]]::new();
        for ($i = 0; $i -lt $history.Count; $i++) {
            $entry = $history.Item($i);
            $operation = switch ([int]$entry.Operation) { 1 { 'Instalação' } 2 { 'Desinstalação' } default { 'Operação desconhecida (' + [int]$entry.Operation + ')' } };
            $result = switch ([int]$entry.ResultCode) { 0 { 'Não iniciado' } 1 { 'Em andamento' } 2 { 'Concluído' } 3 { 'Concluído com avisos' } 4 { 'Falhou' } 5 { 'Cancelado' } default { 'Resultado desconhecido (' + [int]$entry.ResultCode + ')' } };
            $hresult = if ($null -ne $entry.HResult) { '0x' + ('{0:X8}' -f [int]$entry.HResult) } else { $null };
            $entries.Add([pscustomobject]@{ DateUtc=([datetime]$entry.Date).ToUniversalTime().ToString('O'); Title=[string]$entry.Title; Operation=$operation; Result=$result; HResult=$hresult });
        };
        [pscustomobject]@{ IsComplete=$true; Entries=@($entries.ToArray()); Warnings=@() } |
            Microsoft.PowerShell.Utility\ConvertTo-Json -Depth 4 -Compress;
        """;

    private const string SearchPendingScript = """
        $session = [System.Activator]::CreateInstance([System.Type]::GetTypeFromProgID('Microsoft.Update.Session'));
        $session.ClientApplicationID = 'ZEUS';
        $searcher = $session.CreateUpdateSearcher();
        $searcher.Online = $true;
        $searcher.CanAutomaticallyUpgradeService = $false;
        $search = $searcher.Search("IsInstalled=0 and IsHidden=0 and Type='Software'");
        $warnings = [System.Collections.Generic.List[string]]::new();
        if ([int]$search.ResultCode -ne 2) { $warnings.Add('O Windows Update retornou resultado parcial ou incompleto (código ' + [int]$search.ResultCode + ').'); };
        $updates = [System.Collections.Generic.List[object]]::new();
        for ($i = 0; $i -lt $search.Updates.Count; $i++) {
            $update = $search.Updates.Item($i);
            $updates.Add([pscustomobject]@{ Title=[string]$update.Title; KnowledgeBaseIds=@($update.KBArticleIDs | ForEach-Object { [string]$_ }); Downloaded=[bool]$update.IsDownloaded; UpdateId=[string]$update.Identity.UpdateID });
        };
        [pscustomobject]@{ IsComplete=([int]$search.ResultCode -eq 2); Updates=@($updates.ToArray()); Warnings=@($warnings.ToArray()) } |
            Microsoft.PowerShell.Utility\ConvertTo-Json -Depth 5 -Compress;
        """;

    private const string VerifyInstalledDriverScript = """
        $session = [System.Activator]::CreateInstance([System.Type]::GetTypeFromProgID('Microsoft.Update.Session'));
        $session.ClientApplicationID = 'ZEUS';
        $searcher = $session.CreateUpdateSearcher();
        $searcher.Online = $true;
        $searcher.CanAutomaticallyUpgradeService = $false;
        $actualServerSelection = [int]$searcher.ServerSelection;
        $actualServiceId = if ($actualServerSelection -eq 3) { [string]$searcher.ServiceID } else { '' };
        $sourceMatches = $actualServerSelection -eq $expectedServerSelection -and
            ($expectedServerSelection -ne 3 -or [guid]$actualServiceId -eq [guid]$expectedServiceId);
        $isComplete = $false;
        $isInstalled = $null;
        $warnings = [System.Collections.Generic.List[string]]::new();
        if ($sourceMatches) {
            $query = "IsInstalled=1 and Type='Driver' and UpdateID='" + $expectedId + "' and RevisionNumber=" + $expectedRevision;
            $search = $searcher.Search($query);
            $isComplete = [int]$search.ResultCode -eq 2;
            if ($isComplete) {
                $isInstalled = $false;
                if ($search.Updates.Count -eq 1) {
                    $update = $search.Updates.Item(0);
                    $isInstalled = [bool]$update.IsInstalled -and
                        [guid]$update.Identity.UpdateID -eq [guid]$expectedId -and
                        [int]$update.Identity.RevisionNumber -eq $expectedRevision;
                } elseif ($search.Updates.Count -gt 1) {
                    $warnings.Add('O Windows Update retornou mais de um registro para a identidade exata; a verificação foi considerada incompleta.');
                    $isComplete = $false;
                    $isInstalled = $null;
                }
            } else {
                $warnings.Add('A consulta ao Windows Update retornou resultado parcial ou incompleto (código ' + [int]$search.ResultCode + ').');
            }
        } else {
            $warnings.Add('A seleção lógica de origem atualmente configurada no Windows Update difere da seleção registrada na instalação. Nenhuma consulta de pacote foi feita.');
        }
        [pscustomobject]@{ IsComplete=$isComplete; IsInstalled=$isInstalled; SourceMatches=$sourceMatches; Warnings=@($warnings.ToArray()) } |
            Microsoft.PowerShell.Utility\ConvertTo-Json -Depth 4 -Compress;
        """;

    private const string SearchScript = """
        $session = [System.Activator]::CreateInstance([System.Type]::GetTypeFromProgID('Microsoft.Update.Session'));
        $session.ClientApplicationID = 'ZEUS';
        $warnings = [System.Collections.Generic.List[string]]::new();
        if ($session.CreateUpdateInstaller().IsBusy) {
            $warnings.Add('O Windows Update está ocupado com outra instalação. A busca é somente leitura; aguarde antes de instalar um driver.');
        };
        $searcher = $session.CreateUpdateSearcher();
        $serverSelection = [int]$searcher.ServerSelection;
        $serviceId = if ($serverSelection -eq 3) { [string]$searcher.ServiceID } else { $null };
        $search = $searcher.Search("IsInstalled=0 and Type='Driver' and IsHidden=0");
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
            $driverDate = $null;
            try { $driverDate = ([datetime]$update.DriverVerDate).ToString('yyyy-MM-dd', [System.Globalization.CultureInfo]::InvariantCulture) } catch { };
            $drivers.Add([pscustomobject]@{
                Id = $identity.ToString('D') + ':' + $revision;
                Title = $title;
                Manufacturer = [string]$update.DriverManufacturer;
                DeviceName = [string]$update.DriverModel;
                DriverVersion = $null;
                RequiresEula = $requiresEula;
                EulaText = $eula;
                DriverProvider = [string]$update.DriverProvider;
                DriverClass = $class;
                DriverDate = $driverDate
            });
        };
        [pscustomobject]@{ Updates = @($drivers.ToArray()); Warnings = @($warnings.ToArray()); ServerSelection=$serverSelection; ServiceId=$serviceId } |
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
            return ParseDriverUpdatesPayload(payload);
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

    /// <summary>Explicit read-only recheck of one exact installed driver update in its recorded WUA source selection.</summary>
    public async Task<InstalledDriverUpdateVerification> VerifyInstalledDriverUpdateAsync(
        string updateIdentity, int? serverSelection, string? serviceId, DateTimeOffset installationFinishedAt,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows()) return FailedDriverVerification("A verificação de drivers requer Windows.");
        if (!MaintenanceRequestProtocol.TryParseDriverIdentity(updateIdentity, out var updateId, out var revision) ||
            !WindowsUpdateSourcePolicy.IsAllowed(serverSelection, serviceId))
            return FailedDriverVerification("A identidade ou a origem registrada do driver é inválida.");

        var estimatedBootTime = DateTimeOffset.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64);
        if (estimatedBootTime <= installationFinishedAt)
            return new(DateTimeOffset.UtcNow, false, null, null,
                ["O Windows ainda não registra uma reinicialização posterior à instalação. Nenhuma consulta foi feita; reinicie quando solicitado e tente novamente."]);

        var expectedServiceId = serviceId is null ? Guid.Empty : Guid.Parse(serviceId);
        var script = $"$expectedId = '{updateId:D}'; $expectedRevision = {revision}; $expectedServerSelection = {serverSelection!.Value}; $expectedServiceId = '{expectedServiceId:D}'; " + VerifyInstalledDriverScript;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        using var process = new Process { StartInfo = TrustedPowerShell.Create(script, WindowsPowerShellModule.Utility) };
        try
        {
            if (!process.Start()) return FailedDriverVerification("A consulta ao Windows Update não iniciou.");
            var outputTask = ReadBoundedAsync(process.StandardOutput, 64 * 1024, timeout.Token);
            var errorTask = ReadBoundedAsync(process.StandardError, 16 * 1024, timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var output = await outputTask;
            var diagnostic = await errorTask;
            if (process.ExitCode != 0) return FailedDriverVerification("A consulta ao Windows Update falhou. " + diagnostic.Trim());
            return ParseInstalledDriverVerificationPayload(output);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return FailedDriverVerification("A consulta excedeu três minutos. Estado desconhecido; não repita a instalação com base nesta falha.");
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return FailedDriverVerification("Não foi possível verificar o driver pelo Windows Update: " + exception.Message);
        }
        finally
        {
            try { if (process.Id > 0 && !process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
        }
    }

    internal static InstalledDriverUpdateVerification ParseInstalledDriverVerificationPayload(string output)
    {
        var result = JsonSerializer.Deserialize<InstalledDriverVerificationPayload>(output)
            ?? throw new InvalidDataException("Resposta vazia do Windows Update.");
        if (result.Warnings is null) throw new InvalidDataException("Resposta incompleta do Windows Update.");
        if (result.IsComplete && (!result.SourceMatches || result.IsInstalled is null))
            throw new InvalidDataException("A resposta do Windows Update contradiz o estado de verificação.");
        if (!result.IsComplete && result.IsInstalled is not null)
            throw new InvalidDataException("Uma consulta incompleta não pode confirmar o estado instalado.");
        return new(DateTimeOffset.UtcNow, result.IsComplete, result.IsInstalled, result.SourceMatches,
            Array.AsReadOnly(result.Warnings.ToArray()));
    }

    /// <summary>Performs an explicit online, read-only search of configured Windows Update sources; no update is downloaded or installed.</summary>
    public async Task<PendingWindowsUpdateSearch> SearchPendingSoftwareUpdatesAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows()) return FailedPending("A busca de atualizações do Windows requer Windows.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        using var process = new Process { StartInfo = TrustedPowerShell.Create(SearchPendingScript, WindowsPowerShellModule.Utility) };
        try
        {
            if (!process.Start()) return FailedPending("A consulta do Windows Update não iniciou.");
            var outputTask = ReadBoundedAsync(process.StandardOutput, 2 * 1024 * 1024, timeout.Token);
            var errorTask = ReadBoundedAsync(process.StandardError, 16 * 1024, timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var output = await outputTask;
            var diagnostic = await errorTask;
            if (process.ExitCode != 0) return FailedPending("A consulta ao Windows Update falhou. " + diagnostic.Trim());
            var parsed = ParsePendingSoftwareUpdatesPayload(output);
            var warnings = parsed.Warnings.ToList();
            warnings.Add("Busca online somente leitura pela fonte configurada no Windows Update; nenhum download, instalação ou reinicialização foi solicitado.");
            if (parsed.Updates.Count == 0 && parsed.IsComplete) warnings.Add("Nenhuma atualização de software pendente foi encontrada nesta busca. Isso não avalia drivers nem atualizações ocultas.");
            return parsed with { CheckedAt = DateTimeOffset.UtcNow, Warnings = warnings.AsReadOnly() };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return FailedPending("A busca excedeu três minutos. Estado desconhecido; confira conexão, políticas e o Windows Update.");
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return FailedPending("Não foi possível concluir a busca do Windows Update: " + exception.Message);
        }
        finally
        {
            try { if (process.Id > 0 && !process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
        }
    }

    /// <summary>Reads only the newest local Windows Update Agent history entries; it does not contact an update source or change system state.</summary>
    public async Task<WindowsUpdateHistorySearch> ReadHistoryAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows()) return FailedHistory("O histórico do Windows Update requer Windows.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        using var process = new Process { StartInfo = TrustedPowerShell.Create(SearchHistoryScript, WindowsPowerShellModule.Utility) };
        try
        {
            if (!process.Start()) return FailedHistory("A leitura do histórico do Windows Update não iniciou.");
            var outputTask = ReadBoundedAsync(process.StandardOutput, 1024 * 1024, timeout.Token);
            var errorTask = ReadBoundedAsync(process.StandardError, 16 * 1024, timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var output = await outputTask;
            var diagnostic = await errorTask;
            if (process.ExitCode != 0) return FailedHistory("Não foi possível ler o histórico local do Windows Update. " + diagnostic.Trim());
            var parsed = ParseHistoryPayload(output);
            var warnings = parsed.Warnings.ToList();
            warnings.Add("Histórico local somente leitura, limitado aos 50 registros mais recentes; não houve contato com servidores, download, instalação ou alteração do Windows.");
            return parsed with { CheckedAt = DateTimeOffset.UtcNow, Warnings = warnings.AsReadOnly() };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return FailedHistory("A leitura excedeu 45 segundos. O estado do histórico permanece desconhecido.");
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return FailedHistory("Não foi possível ler o histórico local do Windows Update: " + exception.Message);
        }
        finally
        {
            try { if (process.Id > 0 && !process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
        }
    }

    public static WindowsUpdateHistorySearch ParseHistoryPayload(string output)
    {
        var result = JsonSerializer.Deserialize<HistorySearchPayload>(output)
            ?? throw new InvalidDataException("Resposta vazia do histórico do Windows Update.");
        if (result.Entries is null || result.Warnings is null)
            throw new InvalidDataException("Resposta incompleta do histórico do Windows Update.");
        var warnings = result.Warnings.ToList();
        var entries = new List<WindowsUpdateHistoryEntry>();
        var malformed = result.Entries.Count > WindowsUpdateHistoryLimit;
        foreach (var entry in result.Entries.Take(WindowsUpdateHistoryLimit))
        {
            if (entry is null || string.IsNullOrWhiteSpace(entry.Title) || entry.Title.Length > 500 ||
                !DateTimeOffset.TryParse(entry.DateUtc, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var date) ||
                string.IsNullOrWhiteSpace(entry.Operation) || entry.Operation.Length > 80 ||
                string.IsNullOrWhiteSpace(entry.Result) || entry.Result.Length > 100 ||
                (entry.HResult is not null && (entry.HResult.Length != 10 || !entry.HResult.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ||
                    !uint.TryParse(entry.HResult.AsSpan(2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out _))))
            {
                malformed = true;
                warnings.Add("Um registro foi omitido porque o Windows retornou data, título ou resultado inválido.");
                continue;
            }
            entries.Add(new(date, entry.Title, entry.Operation, entry.Result, entry.HResult?.ToUpperInvariant()));
        }
        if (result.Entries.Count > WindowsUpdateHistoryLimit)
            warnings.Add("A resposta excedeu o limite de registros e foi cortada; a leitura é considerada parcial.");
        return new(DateTimeOffset.UtcNow, result.IsComplete && !malformed, entries.AsReadOnly(), warnings.AsReadOnly());
    }

    public static PendingWindowsUpdateSearch ParsePendingSoftwareUpdatesPayload(string output)
    {
        var result = JsonSerializer.Deserialize<PendingSearchPayload>(output)
            ?? throw new InvalidDataException("Resposta vazia do Windows Update.");
        if (result.Updates is null || result.Warnings is null) throw new InvalidDataException("Resposta incompleta do Windows Update.");
        var warnings = result.Warnings.ToList();
        var updates = new List<PendingWindowsUpdate>();
        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var malformed = false;
        foreach (var update in result.Updates)
        {
            if (update is null || string.IsNullOrWhiteSpace(update.Title) || !Guid.TryParse(update.UpdateId, out var id) || id == Guid.Empty || !identities.Add(id.ToString("D")))
            {
                malformed = true;
                warnings.Add("Uma atualização foi omitida porque o título ou a identidade retornada era inválida ou repetida.");
                continue;
            }
            updates.Add(update with { UpdateId = id.ToString("D"), KnowledgeBaseIds = update.KnowledgeBaseIds ?? [] });
        }
        return new(DateTimeOffset.UtcNow, result.IsComplete && !malformed, updates.AsReadOnly(), warnings.AsReadOnly());
    }

    private static DriverUpdateSearch Failed(string message) =>
        new(DateTimeOffset.UtcNow, [], [message]);

    private static PendingWindowsUpdateSearch FailedPending(string message) =>
        new(DateTimeOffset.UtcNow, false, [], [message]);

    private static WindowsUpdateHistorySearch FailedHistory(string message) =>
        new(DateTimeOffset.UtcNow, false, [], [message]);

    private static InstalledDriverUpdateVerification FailedDriverVerification(string message) =>
        new(DateTimeOffset.UtcNow, false, null, null, [message]);

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

    private sealed record SearchPayload(IReadOnlyList<DriverSearchCandidate>? Updates, IReadOnlyList<string>? Warnings,
        int? ServerSelection, string? ServiceId);
    private sealed record DriverSearchCandidate(string? Id, string? Title, string? Manufacturer,
        string? DeviceName, string? DriverVersion, bool RequiresEula, string? EulaText,
        string? DriverProvider, string? DriverClass, string? DriverDate);
    private sealed record PendingSearchPayload(bool IsComplete, IReadOnlyList<PendingWindowsUpdate>? Updates, IReadOnlyList<string>? Warnings);
    private sealed record HistorySearchPayload(bool IsComplete, IReadOnlyList<HistoryEntryPayload?>? Entries, IReadOnlyList<string>? Warnings);
    private sealed record HistoryEntryPayload(string? DateUtc, string? Title, string? Operation, string? Result, string? HResult);
    private sealed record InstalledDriverVerificationPayload(bool IsComplete, bool? IsInstalled, bool SourceMatches, IReadOnlyList<string>? Warnings);

    internal static DriverUpdateSearch ParseDriverUpdatesPayload(string output)
    {
        var result = JsonSerializer.Deserialize<SearchPayload>(output)
            ?? throw new InvalidDataException("Resposta vazia do Windows Update.");
        if (result.Updates is null || result.Warnings is null)
            throw new InvalidDataException("Resposta incompleta do Windows Update.");
        var warnings = result.Warnings.ToList();
        var sourceValid = WindowsUpdateSourcePolicy.IsAllowed(result.ServerSelection, result.ServiceId);
        if (!sourceValid)
            warnings.Add("A seleção de servidor do Windows Update Agent não é reconhecida como Windows Update ou Microsoft Update oficial; os candidatos ficam visíveis, mas bloqueados para instalação.");
        var drivers = new List<DriverUpdateCandidate>();
        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var driver in result.Updates)
        {
            if (driver is null || string.IsNullOrWhiteSpace(driver.Id) || !MaintenanceRequestProtocol.TryParseDriverIdentity(driver.Id, out _, out _) ||
                string.IsNullOrWhiteSpace(driver.Title) || !identities.Add(driver.Id))
            {
                warnings.Add("Uma oferta de driver foi ignorada porque sua identidade era inválida ou repetida.");
                continue;
            }
            DateOnly? driverDate = null;
            if (!string.IsNullOrWhiteSpace(driver.DriverDate))
            {
                if (DateOnly.TryParseExact(driver.DriverDate, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out var parsedDate) && parsedDate.Year >= 1980)
                {
                    driverDate = parsedDate;
                    if (parsedDate > DateOnly.FromDateTime(DateTime.Today))
                        warnings.Add($"A oferta '{driver.Title.Trim()}' informa uma data futura; a instalação pelo ZEUS ficará bloqueada.");
                }
                else
                {
                    warnings.Add($"A data do driver da oferta '{driver.Title.Trim()}' é inválida ou sentinela e foi mantida como indisponível.");
                }
            }
            else
            {
                warnings.Add($"A oferta '{driver.Title.Trim()}' não informou a data do driver; a instalação pelo ZEUS ficará bloqueada.");
            }
            if (string.IsNullOrWhiteSpace(driver.Manufacturer) || string.IsNullOrWhiteSpace(driver.DeviceName))
                warnings.Add($"A oferta '{driver.Title.Trim()}' não identifica fabricante e modelo do dispositivo; ela não poderá ser instalada pelo ZEUS.");

            drivers.Add(new DriverUpdateCandidate(driver.Id!, driver.Title.Trim(), Optional(driver.Manufacturer),
                Optional(driver.DeviceName), Optional(driver.DriverVersion), driver.RequiresEula, Optional(driver.EulaText),
                Optional(driver.DriverProvider), Optional(driver.DriverClass), driverDate, result.ServerSelection, result.ServiceId));
        }
        warnings.Add("As ofertas seguem as fontes configuradas no Windows Update. Em notebooks, confira a recomendação do fabricante antes de instalar.");
        warnings.Add("O Windows Update informa fornecedor, classe e data do driver, mas não uma versão numérica nem hash/assinatura do arquivo nesta busca; esses itens permanecem indisponíveis e não são inferidos do título.");
        return new(DateTimeOffset.UtcNow, drivers.AsReadOnly(), warnings.AsReadOnly());
    }

    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

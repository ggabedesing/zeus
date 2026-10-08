using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Zeus.Windows;

public sealed record WingetUpdateCandidate(string Name, string PackageId, string InstalledVersion,
    string AvailableVersion, string Source)
{
    public bool CanInstall => WingetUpdateService.CanInstall(this, out _);
    public string InstallabilityReason => WingetUpdateService.CanInstall(this, out var reason) ?
        "Atualização exata disponível; a instalação pedirá consentimento no WinGet." : reason;
}

public sealed record WingetUpdateSearch(DateTimeOffset CheckedAt, bool IsComplete,
    IReadOnlyList<WingetUpdateCandidate> Updates, IReadOnlyList<string> Warnings);

public sealed record WingetInstalledVersionResult(bool IsComplete, string? InstalledVersion, string? Warning);

public sealed record WingetUpdateTransactionResult(bool Started, bool ProcessCompleted, int? ExitCode,
    bool InstalledVersionVerified, string? ObservedInstalledVersion, string Message)
{
    public bool Succeeded => ProcessCompleted && ExitCode == 0 && InstalledVersionVerified;
    public const string RecoveryClassification = "ManualReviewRequired";
}

/// <summary>Reads WinGet's locally installed package inventory; it never installs or accepts package licenses.</summary>
public sealed class WingetUpdateService
{
    private static readonly Regex SafePackageId = new("^[A-Za-z0-9][A-Za-z0-9.+_-]{0,127}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex SafeVersion = new("^[A-Za-z0-9][A-Za-z0-9.+_-]{0,127}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool CanInstall(WingetUpdateCandidate candidate, out string reason)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (!candidate.Source.Equals("winget", StringComparison.OrdinalIgnoreCase)) reason = "Fonte não reconhecida como winget.";
        else if (!SafePackageId.IsMatch(candidate.PackageId)) reason = "Identidade de pacote ausente ou fora do formato permitido.";
        else if (!SafeVersion.IsMatch(candidate.InstalledVersion) || !SafeVersion.IsMatch(candidate.AvailableVersion)) reason = "Uma das versões não é exata; atualize apenas após nova consulta reconhecida.";
        else if (candidate.InstalledVersion.Equals(candidate.AvailableVersion, StringComparison.OrdinalIgnoreCase)) reason = "A versão instalada já coincide com a versão anunciada.";
        else { reason = string.Empty; return true; }
        return false;
    }

    public async Task<WingetUpdateSearch> SearchAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
            return Failed("A consulta do WinGet requer Windows.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        var wingetPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "WindowsApps", "winget.exe");
        if (!File.Exists(wingetPath))
            return Failed("O atalho oficial do WinGet deste usuário não está disponível. Instale ou repare o App Installer pela Microsoft Store.");
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = wingetPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                ArgumentList = { "list", "--upgrade-available", "--disable-interactivity", "--source", "winget" }
            }
        };

        try
        {
            if (!process.Start()) return Failed("O WinGet não iniciou.");
            var stdout = ReadBoundedAsync(process.StandardOutput, 1024 * 1024, timeout.Token);
            var stderr = ReadBoundedAsync(process.StandardError, 16 * 1024, timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var output = await stdout;
            var error = await stderr;
            if (process.ExitCode != 0)
                return Failed("O WinGet não concluiu a consulta. Nenhum termo ou licença foi aceito automaticamente. " + TrimDiagnostic(error));

            var parsed = ParseOutput(output);
            return parsed with { CheckedAt = DateTimeOffset.UtcNow };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failed("A consulta do WinGet excedeu dois minutos. O resultado é desconhecido; tente novamente mais tarde.");
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException or IOException or InvalidDataException)
        {
            return Failed("Não foi possível consultar o WinGet. O resultado é desconhecido. " + exception.Message);
        }
        finally
        {
            try { if (process.Id > 0 && !process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
        }
    }

    public async Task<WingetUpdateTransactionResult> UpgradeAsync(WingetUpdateCandidate candidate,
        CancellationToken preflightCancellation = default)
    {
        if (!CanInstall(candidate, out var reason))
            return NotStarted("Instalação bloqueada: " + reason);
        if (!OperatingSystem.IsWindows()) return NotStarted("A instalação WinGet requer Windows.");

        var preflight = await SearchAsync(preflightCancellation);
        if (!preflight.IsComplete)
            return NotStarted("Instalação bloqueada porque não foi possível confirmar novamente a lista. " + string.Join(" ", preflight.Warnings));
        var current = preflight.Updates.SingleOrDefault(update => update.PackageId.Equals(candidate.PackageId, StringComparison.OrdinalIgnoreCase));
        if (current is null || !current.InstalledVersion.Equals(candidate.InstalledVersion, StringComparison.Ordinal) ||
            !current.AvailableVersion.Equals(candidate.AvailableVersion, StringComparison.Ordinal) || !CanInstall(current, out _))
            return NotStarted("A oferta ou a versão instalada mudou desde a prévia. Atualize a lista e revise de novo; nenhum instalador foi iniciado.");

        var path = GetWingetPath();
        if (path is null) return NotStarted("O atalho oficial do WinGet deste usuário não está disponível.");
        using var process = new Process { StartInfo = CreateInteractiveUpgradeStartInfo(path, current) };
        try
        {
            if (!process.Start()) return NotStarted("A janela interativa do WinGet não iniciou.");
            // Do not kill or time out an installer after it starts. The user can cancel in the WinGet/installer window.
            await process.WaitForExitAsync();
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return new(true, false, null, false, null,
                "O resultado da atualização não pôde ser confirmado após a tentativa. Confira o aplicativo instalado antes de tentar novamente. " + error.Message);
        }

        var installed = await ReadInstalledVersionAsync(current.PackageId, CancellationToken.None);
        var verified = installed.IsComplete && installed.InstalledVersion is not null &&
                       installed.InstalledVersion.Equals(current.AvailableVersion, StringComparison.OrdinalIgnoreCase);
        var message = verified && process.ExitCode == 0
            ? $"O WinGet terminou e o ZEUS confirmou a versão instalada {installed.InstalledVersion}. Reversão automática não garantida; consulte o aplicativo e mantenha o instalador/recuperação do fornecedor."
            : $"O resultado não foi confirmado como concluído. Código WinGet: {process.ExitCode}; versão lida: {installed.InstalledVersion ?? "indisponível"}. Não repita antes de conferir o aplicativo e os logs. {installed.Warning}";
        return new(true, true, process.ExitCode, verified, installed.InstalledVersion, message);
    }

    public static IReadOnlyList<string> CreateInteractiveUpgradeArguments(WingetUpdateCandidate candidate)
    {
        if (!CanInstall(candidate, out var reason)) throw new ArgumentException("Candidato inválido: " + reason, nameof(candidate));
        return Array.AsReadOnly(new[] { "upgrade", "--id", candidate.PackageId, "--exact", "--source", "winget",
            "--version", candidate.AvailableVersion, "--interactive" });
    }

    public static WingetInstalledVersionResult ParseInstalledVersion(string output, string expectedPackageId)
    {
        if (string.IsNullOrWhiteSpace(expectedPackageId) || !SafePackageId.IsMatch(expectedPackageId))
            return new(false, null, "Identidade WinGet inválida.");
        var lines = output.Replace("\r", "").Split('\n');
        var headerIndex = Array.FindIndex(lines, line =>
        {
            var columns = GetColumns(line);
            return columns.Count is >= 3 and <= 5 && columns[1].Text.Equals("ID", StringComparison.OrdinalIgnoreCase) &&
                   (columns[2].Text.Equals("Versão", StringComparison.OrdinalIgnoreCase) || columns[2].Text.Equals("Version", StringComparison.OrdinalIgnoreCase));
        });
        if (headerIndex < 0 || headerIndex + 1 >= lines.Length || !IsSeparator(lines[headerIndex + 1]))
            return new(false, null, "Formato do inventário instalado não reconhecido.");
        var headers = GetColumns(lines[headerIndex]);
        var matches = new List<string>();
        for (var i = headerIndex + 2; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line) || line.Length <= headers[1].Start) continue;
            var id = line[headers[1].Start..Math.Min(headers[2].Start, line.Length)].Trim();
            if (!id.Equals(expectedPackageId, StringComparison.OrdinalIgnoreCase)) continue;
            var versionEnd = headers.Count > 3 ? headers[3].Start : line.Length;
            var version = line.Length <= headers[2].Start ? string.Empty : line[headers[2].Start..Math.Min(versionEnd, line.Length)].Trim();
            if (version.Length > 0 && SafeVersion.IsMatch(version)) matches.Add(version);
        }
        return matches.Count == 1
            ? new(true, matches[0], null)
            : new(false, null, matches.Count == 0 ? "O pacote não retornou uma versão instalada exata." : "O inventário retornou mais de uma correspondência para o ID.");
    }

    private async Task<WingetInstalledVersionResult> ReadInstalledVersionAsync(string packageId, CancellationToken cancellationToken)
    {
        var path = GetWingetPath();
        if (path is null) return new(false, null, "Atalho do WinGet indisponível para verificar o resultado.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        using var process = new Process
        {
            StartInfo = CreateReadStartInfo(path, "list", "--id", packageId, "--exact", "--source", "winget", "--disable-interactivity")
        };
        try
        {
            if (!process.Start()) return new(false, null, "A consulta pós-instalação não iniciou.");
            var outputTask = ReadBoundedAsync(process.StandardOutput, 128 * 1024, timeout.Token);
            var errorTask = ReadBoundedAsync(process.StandardError, 16 * 1024, timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var output = await outputTask;
            var error = await errorTask;
            return process.ExitCode == 0 ? ParseInstalledVersion(output, packageId) : new(false, null, TrimDiagnostic(error));
        }
        catch (Exception error) when (error is OperationCanceledException or System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return new(false, null, "A consulta pós-instalação não confirmou o estado. " + error.Message);
        }
        finally
        {
            try { if (process.Id > 0 && !process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
        }
    }

    private static ProcessStartInfo CreateInteractiveUpgradeStartInfo(string wingetPath, WingetUpdateCandidate candidate)
    {
        var start = new ProcessStartInfo
        {
            FileName = wingetPath,
            UseShellExecute = false,
            CreateNoWindow = false,
            WorkingDirectory = Environment.SystemDirectory,
            WindowStyle = ProcessWindowStyle.Normal
        };
        foreach (var argument in CreateInteractiveUpgradeArguments(candidate)) start.ArgumentList.Add(argument);
        return start;
    }

    private static ProcessStartInfo CreateReadStartInfo(string wingetPath, params string[] arguments)
    {
        var start = new ProcessStartInfo
        {
            FileName = wingetPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Environment.SystemDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        return start;
    }

    private static string? GetWingetPath()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "winget.exe");
        return File.Exists(path) ? path : null;
    }

    private static WingetUpdateTransactionResult NotStarted(string message) => new(false, false, null, false, null, message);

    public static WingetUpdateSearch ParseOutput(string output)
    {
        var lines = output.Replace("\r", "").Split('\n');
        var headerIndex = Array.FindIndex(lines, line =>
        {
            var columns = GetColumns(line);
            return columns.Count is 4 or 5 && columns[1].Text.Equals("ID", StringComparison.OrdinalIgnoreCase);
        });
        if (headerIndex < 0) return Failed("O WinGet respondeu em formato ou idioma não reconhecido. A lista não foi interpretada.");

        var headers = GetColumns(lines[headerIndex]);
        if (headerIndex + 1 >= lines.Length || !IsSeparator(lines[headerIndex + 1]))
            return Failed("O WinGet retornou cabeçalho incompleto. A lista não foi interpretada.");

        var rows = new List<WingetUpdateCandidate>();
        var warnings = new List<string>();
        var malformed = false;
        for (var i = headerIndex + 2; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (line.Contains("atualizaç", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("upgrade", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("pacote", StringComparison.OrdinalIgnoreCase)) continue;
            if (line.Length <= headers[1].Start) { malformed = true; continue; }

            var values = new string[headers.Count];
            for (var column = 0; column < headers.Count; column++)
            {
                var start = headers[column].Start;
                var end = column + 1 < headers.Count ? headers[column + 1].Start : line.Length;
                values[column] = line.Length <= start ? string.Empty : line[start..Math.Min(end, line.Length)].Trim();
            }
            if (values.Any(string.IsNullOrWhiteSpace) || values[1].Any(char.IsWhiteSpace)) { malformed = true; continue; }
            rows.Add(new(values[0], values[1], values[2], values[3], values.Length == 5 ? values[4] : "winget"));
        }

        if (malformed) warnings.Add("Uma ou mais linhas estavam truncadas ou fora do formato esperado e foram ignoradas.");
        if (rows.Count == 0 && malformed) return Failed("O WinGet retornou dados que não puderam ser interpretados com segurança.");
        if (rows.Select(row => row.PackageId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != rows.Count)
            return Failed("O WinGet retornou identidades repetidas. A lista foi ocultada para evitar seleção ambígua.");
        if (rows.Count == 0) warnings.Add("Nenhuma atualização foi encontrada na fonte winget consultada. Outras fontes, inclusive Microsoft Store, não foram verificadas.");
        else warnings.Add("Consulta somente leitura da fonte winget. Microsoft Store e programas não correspondidos pela fonte não estão incluídos.");
        return new(DateTimeOffset.UtcNow, true, rows.AsReadOnly(), warnings.AsReadOnly());
    }

    private static List<(string Text, int Start)> GetColumns(string line)
    {
        return Regex.Matches(line, @"\S(?:.*?\S)?(?=\s{2,}|$)")
            .Select(match => (match.Value.Trim(), match.Index)).ToList();
    }

    private static bool IsSeparator(string line) => line.Trim().Length >= 8 && line.Trim().All(c => c is '-' or '─');
    private static WingetUpdateSearch Failed(string message) => new(DateTimeOffset.UtcNow, false, [], [message]);
    private static string TrimDiagnostic(string error) => error.Length > 1000 ? error[..1000] : error.Trim();

    private static async Task<string> ReadBoundedAsync(StreamReader reader, int limit, CancellationToken token)
    {
        var builder = new StringBuilder();
        var buffer = new char[2048];
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), token)) > 0)
        {
            if (builder.Length + count > limit) throw new InvalidDataException("A resposta do WinGet excede o limite permitido.");
            builder.Append(buffer, 0, count);
        }
        return builder.ToString();
    }
}

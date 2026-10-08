using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Zeus.Windows;

public sealed record WingetUpdateCandidate(string Name, string PackageId, string InstalledVersion,
    string AvailableVersion, string Source);

public sealed record WingetUpdateSearch(DateTimeOffset CheckedAt, bool IsComplete,
    IReadOnlyList<WingetUpdateCandidate> Updates, IReadOnlyList<string> Warnings);

/// <summary>Reads WinGet's locally installed package inventory; it never installs or accepts package licenses.</summary>
public sealed class WingetUpdateService
{
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

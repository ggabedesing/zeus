using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Zeus.Windows;

/// <summary>
/// Reversible, current-user changes. This service never writes HKLM, disables services,
/// installs drivers or executes a startup command. The JSON history is local to the user.
/// </summary>
public sealed class UserOptimizationService
{
    private const string StartupScope = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string TransparencyScope = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string TransparencyName = "EnableTransparency";
    private const uint SpiGetClientAreaAnimation = 0x1042;
    private const uint SpiSetClientAreaAnimation = 0x1043;
    private const uint UpdateIniAndBroadcast = 0x01 | 0x02;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly Regex GuidPattern = new(@"\b[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}\b", RegexOptions.CultureInvariant);
    private readonly string storageRoot;
    private readonly SemaphoreSlim gate = new(1, 1);

    public UserOptimizationService(string? storageRoot = null)
    {
        this.storageRoot = Path.GetFullPath(storageRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Zeus", "UserChanges"));
        AssertNoRedirectedAncestors(this.storageRoot);
    }

    public async Task<IReadOnlyList<StartupEntry>> ReadStartupAsync(CancellationToken cancellationToken = default)
    {
        RequireWindows();
        cancellationToken.ThrowIfCancellationRequested();
        var entries = new List<StartupEntry>();
        using (var key = Registry.CurrentUser.OpenSubKey(StartupScope, writable: false))
        {
            if (key is not null)
                foreach (var name in key.GetValueNames())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var snapshot = ReadValue(key, name);
                    if (snapshot is not null) entries.Add(ToStartupEntry(snapshot, true));
                }
        }
        // The disabled item remains visible, with restoration available in history.
        foreach (var document in await ReadDocumentsAsync(cancellationToken))
        {
            if (document.Kind != "startup" || document.Restored || document.Startup is null) continue;
            if (entries.Any(entry => string.Equals(entry.Name, document.Startup.Name, StringComparison.OrdinalIgnoreCase))) continue;
            var item = ToStartupEntry(document.Startup, false);
            if (!entries.Any(entry => entry.Id == item.Id)) entries.Add(item);
        }
        return entries.OrderBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public Task<UserChangeResult> DisableStartupAsync(string entryId, CancellationToken cancellationToken = default) =>
        WithChangeLockAsync(async () =>
        {
            RequireWindows();
            if (!IsFingerprint(entryId)) return Failure("Identificador de inicialização inválido.");
            using var key = Registry.CurrentUser.OpenSubKey(StartupScope, writable: true);
            if (key is null) return Failure("A entrada já não existe.");
            var snapshot = key.GetValueNames().Select(name => ReadValue(key, name))
                .FirstOrDefault(value => value is not null && Fingerprint(value) == entryId);
            if (snapshot is null) return Failure("A entrada mudou desde o diagnóstico. Atualize a lista antes de continuar.");
            var entry = ToStartupEntry(snapshot, true);
            if (entry.IsProtected) return Failure(entry.ProtectionReason ?? "A entrada está protegida.");
            var document = NewDocument("startup", $"Inicialização: {snapshot.Name[..Math.Min(snapshot.Name.Length, 480)]}");
            document.Startup = snapshot;
            await SaveDocumentAsync(document, cancellationToken); // durable backup precedes the registry mutation
            cancellationToken.ThrowIfCancellationRequested();
            var current = ReadValue(key, snapshot.Name);
            if (current is null || Fingerprint(current) != entryId)
                return Failure("A entrada mudou durante a operação; nenhuma entrada foi excluída.", document.Id);
            document.Status = UserChangeStatus.Applying;
            await SaveDocumentAsync(document, cancellationToken);
            key.DeleteValue(snapshot.Name, throwOnMissingValue: true);
            key.Flush();
            if (ReadValue(key, snapshot.Name) is not null)
            {
                await SetChangeStatusAsync(document, UserChangeStatus.NeedsReview);
                return Failure("A entrada voltou a existir durante a operação. Atualize o diagnóstico antes de tentar novamente.", document.Id);
            }
            await SetChangeStatusAsync(document, UserChangeStatus.Applied);
            return Success(document.Id, "Inicialização desativada para este usuário. O histórico permite restaurá-la.");
        }, cancellationToken);

    public async Task<IReadOnlyList<UserChangeSession>> ListChangesAsync(CancellationToken cancellationToken = default) =>
        (await ReadDocumentsAsync(cancellationToken)).Select(document => new UserChangeSession(
            document.Id, document.CreatedAt, document.Description, document.Restored,
            document.Restored ? UserChangeStatus.Restored : document.Status)).ToArray();

    public Task<UserChangeResult> ApplyPreferencesAsync(UserOptimizationPreferences preferences, CancellationToken cancellationToken = default) =>
        WithChangeLockAsync(async () =>
        {
            RequireWindows();
            ArgumentNullException.ThrowIfNull(preferences);
            if (!Enum.IsDefined(preferences.Profile)) return Failure("Perfil desconhecido.");
            var policy = CheckVisualPolicy();
            if (policy is not null) return Failure(policy);
            var animation = ReadAnimation();
            using var key = Registry.CurrentUser.OpenSubKey(TransparencyScope, writable: false);
            var transparency = key is null ? null : ReadValue(key, TransparencyName);
            if (transparency is not null && transparency.Kind != RegistryValueKind.DWord)
                return Failure("A configuração de transparência usa um tipo inesperado; nenhuma alteração foi feita.");
            var document = NewDocument("preferences", $"Preferências visuais: {preferences.Profile}");
            document.Preferences = preferences;
            document.PreviousAnimation = animation;
            document.PreviousTransparency = transparency;
            await SaveDocumentAsync(document, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            using (var recheck = Registry.CurrentUser.OpenSubKey(TransparencyScope, writable: false))
                if (ReadAnimation() != animation || !SameValue(recheck is null ? null : ReadValue(recheck, TransparencyName), transparency))
                    return Failure("As preferências mudaram durante a operação; nenhuma alteração foi aplicada.", document.Id);
            document.Status = UserChangeStatus.Applying;
            await SaveDocumentAsync(document, cancellationToken);
            WriteAnimation(!preferences.ReduceAnimations);
            using var writable = Registry.CurrentUser.CreateSubKey(TransparencyScope, writable: true);
            writable.SetValue(TransparencyName, preferences.ReduceTransparency ? 0 : 1, RegistryValueKind.DWord);
            writable.Flush();
            BroadcastVisualChange();
            if (ReadAnimation() != !preferences.ReduceAnimations || ReadTransparency() != (preferences.ReduceTransparency ? 0 : 1))
            {
                await SetChangeStatusAsync(document, UserChangeStatus.NeedsReview);
                return Failure("O Windows não confirmou todas as preferências. Use o histórico para restaurar e verifique políticas do dispositivo.", document.Id);
            }
            await SetChangeStatusAsync(document, UserChangeStatus.Applied);
            return Success(document.Id, "Preferências visuais aplicadas. O perfil não altera automaticamente o plano de energia.");
        }, cancellationToken);

    public async Task<IReadOnlyList<PowerPlanInfo>> ReadPowerPlansAsync(CancellationToken cancellationToken = default)
    {
        RequireWindows();
        var active = await GetActivePowerPlanAsync(cancellationToken);
        var output = await RunPowerCfgAsync(["/list"], cancellationToken);
        var plans = new List<PowerPlanInfo>();
        foreach (var line in output.Split('\n'))
        {
            var match = GuidPattern.Match(line);
            if (!match.Success || !Guid.TryParse(match.Value, out var id)) continue;
            var rest = line[(match.Index + match.Length)..].Trim();
            var open = rest.IndexOf('(');
            var close = rest.LastIndexOf(')');
            var name = open >= 0 && close > open ? rest[(open + 1)..close].Trim() : id.ToString("D");
            if (plans.All(plan => plan.Id != id)) plans.Add(new(id, name, id == active));
        }
        return plans;
    }

    public Task<UserChangeResult> SetPowerPlanAsync(Guid id, CancellationToken cancellationToken = default) =>
        WithChangeLockAsync(async () =>
        {
            RequireWindows();
            if (id == Guid.Empty || !(await ReadPowerPlansAsync(cancellationToken)).Any(plan => plan.Id == id))
                return Failure("O plano precisa existir neste computador. Atualize os planos disponíveis.");
            var current = await GetActivePowerPlanAsync(cancellationToken);
            if (current == id) return Success(Guid.Empty, "Esse plano de energia já está ativo.");
            var document = NewDocument("power", "Plano de energia escolhido pelo usuário");
            document.PreviousPowerPlan = current;
            document.NewPowerPlan = id;
            await SaveDocumentAsync(document, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (await GetActivePowerPlanAsync(cancellationToken) != current)
                return Failure("O plano ativo mudou durante a operação; nenhuma troca foi feita.", document.Id);
            document.Status = UserChangeStatus.Applying;
            await SaveDocumentAsync(document, cancellationToken);
            await RunPowerCfgAsync(["/setactive", id.ToString("D")], cancellationToken);
            if (await GetActivePowerPlanAsync(cancellationToken) != id)
            {
                await SetChangeStatusAsync(document, UserChangeStatus.NeedsReview);
                return Failure("O Windows não confirmou a troca do plano de energia.", document.Id);
            }
            await SetChangeStatusAsync(document, UserChangeStatus.Applied);
            return Success(document.Id, "Plano de energia aplicado. Em notebooks, confira autonomia e temperatura.");
        }, cancellationToken);

    public Task<UserChangeResult> RestoreAsync(Guid changeId, CancellationToken cancellationToken = default) =>
        WithChangeLockAsync(async () =>
        {
            RequireWindows();
            if (changeId == Guid.Empty) return Failure("Sessão inválida.");
            var document = await ReadDocumentAsync(changeId, cancellationToken);
            if (document.Restored) return Success(document.Id, "Esta alteração já foi restaurada.");
            document.Status = UserChangeStatus.Restoring;
            await SaveDocumentAsync(document, cancellationToken);
            switch (document.Kind)
            {
                case "startup":
                    using (var key = Registry.CurrentUser.CreateSubKey(StartupScope, writable: true))
                    {
                        var saved = document.Startup!;
                        var current = ReadValue(key, saved.Name);
                        if (current is not null && Fingerprint(current) != Fingerprint(saved))
                        {
                            await SetChangeStatusAsync(document, UserChangeStatus.RestoreBlocked);
                            return Failure("Existe uma entrada diferente com o mesmo nome. A restauração não a sobrescreveu.", document.Id);
                        }
                        if (current is null) key.SetValue(saved.Name, saved.StringValue!, saved.Kind);
                        key.Flush();
                        if (!SameValue(ReadValue(key, saved.Name), saved))
                        {
                            await SetChangeStatusAsync(document, UserChangeStatus.NeedsReview);
                            return Failure("O Windows não confirmou a restauração da entrada de inicialização.", document.Id);
                        }
                    }
                    break;
                case "preferences":
                    var policy = CheckVisualPolicy();
                    if (policy is not null)
                    {
                        await SetChangeStatusAsync(document, UserChangeStatus.RestoreBlocked);
                        return Failure(policy, document.Id);
                    }
                    var preferences = document.Preferences!;
                    var currentAnimation = ReadAnimation();
                    using (var key = Registry.CurrentUser.CreateSubKey(TransparencyScope, writable: true))
                    {
                        var currentTransparency = ReadValue(key, TransparencyName);
                        var expectedTransparency = new RegistrySnapshot(TransparencyName, RegistryValueKind.DWord, null, preferences.ReduceTransparency ? 0 : 1);
                        if (currentAnimation != !preferences.ReduceAnimations && currentAnimation != document.PreviousAnimation ||
                            !SameValue(currentTransparency, expectedTransparency) && !SameValue(currentTransparency, document.PreviousTransparency))
                        {
                            await SetChangeStatusAsync(document, UserChangeStatus.RestoreBlocked);
                            return Failure("As preferências foram modificadas fora desta sessão. A restauração preservou as configurações atuais.", document.Id);
                        }
                        WriteAnimation(document.PreviousAnimation!.Value);
                        if (document.PreviousTransparency is null) key.DeleteValue(TransparencyName, throwOnMissingValue: false);
                        else key.SetValue(TransparencyName, document.PreviousTransparency.DWordValue!.Value, RegistryValueKind.DWord);
                        key.Flush();
                    }
                    BroadcastVisualChange();
                    using (var verification = Registry.CurrentUser.OpenSubKey(TransparencyScope, writable: false))
                        if (ReadAnimation() != document.PreviousAnimation ||
                            !SameValue(verification is null ? null : ReadValue(verification, TransparencyName), document.PreviousTransparency))
                        {
                            await SetChangeStatusAsync(document, UserChangeStatus.NeedsReview);
                            return Failure("O Windows não confirmou a restauração das preferências visuais.", document.Id);
                        }
                    break;
                case "power":
                    var currentPowerPlan = await GetActivePowerPlanAsync(cancellationToken);
                    if (currentPowerPlan != document.NewPowerPlan && currentPowerPlan != document.PreviousPowerPlan)
                    {
                        await SetChangeStatusAsync(document, UserChangeStatus.RestoreBlocked);
                        return Failure("Outro plano de energia foi selecionado depois desta sessão. A restauração preservou essa escolha.", document.Id);
                    }
                    if (!(await ReadPowerPlansAsync(cancellationToken)).Any(plan => plan.Id == document.PreviousPowerPlan))
                    {
                        await SetChangeStatusAsync(document, UserChangeStatus.RestoreBlocked);
                        return Failure("O plano anterior já não existe neste computador.", document.Id);
                    }
                    await RunPowerCfgAsync(["/setactive", document.PreviousPowerPlan!.Value.ToString("D")], cancellationToken);
                    if (await GetActivePowerPlanAsync(cancellationToken) != document.PreviousPowerPlan)
                    {
                        await SetChangeStatusAsync(document, UserChangeStatus.NeedsReview);
                        return Failure("O Windows não confirmou a restauração do plano de energia.", document.Id);
                    }
                    break;
                default:
                    throw new InvalidDataException("Tipo de alteração desconhecido.");
            }
            document.Restored = true;
            document.Status = UserChangeStatus.Restored;
            await SaveDocumentAsync(document, CancellationToken.None);
            return Success(document.Id, "Configurações anteriores restauradas.");
        }, cancellationToken);

    private async Task<UserChangeResult> WithChangeLockAsync(Func<Task<UserChangeResult>> action, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            EnsureStorage();
            var lockPath = Path.Combine(storageRoot, "changes.lock");
            AssertRegularFileOrMissing(lockPath);
            await using var fileLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return await action();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Win32Exception or InvalidDataException)
        {
            return Failure($"A operação não foi concluída: {exception.Message} Se houve mudança parcial, consulte o histórico para restaurar.");
        }
        finally { gate.Release(); }
    }

    private async Task<List<ChangeDocument>> ReadDocumentsAsync(CancellationToken cancellationToken)
    {
        AssertNoRedirectedAncestors(storageRoot);
        if (!Directory.Exists(storageRoot)) return [];
        var documents = new List<ChangeDocument>();
        foreach (var file in Directory.EnumerateFiles(storageRoot, "*.json").Take(10001))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (documents.Count >= 10000) throw new InvalidDataException("O histórico excedeu o limite de leitura.");
            if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(file), "D", out var id)) continue;
            // Invalid history is never used as instructions for a registry write.
            documents.Add(await ReadDocumentAsync(id, cancellationToken));
        }
        return documents.OrderByDescending(document => document.CreatedAt).ToList();
    }

    private async Task<ChangeDocument> ReadDocumentAsync(Guid id, CancellationToken cancellationToken)
    {
        var path = DocumentPath(id);
        AssertRegularFileOrMissing(path);
        var info = new FileInfo(path);
        if (!info.Exists || info.Length > 128 * 1024) throw new InvalidDataException("Histórico ausente ou com tamanho inválido.");
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var document = await JsonSerializer.DeserializeAsync<ChangeDocument>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("Histórico vazio.");
        ValidateDocument(document, id);
        return document;
    }

    private async Task SaveDocumentAsync(ChangeDocument document, CancellationToken cancellationToken)
    {
        ValidateDocument(document, document.Id);
        EnsureStorage();
        var path = DocumentPath(document.Id);
        AssertRegularFileOrMissing(path);
        var temporary = Path.Combine(storageRoot, $"{document.Id:D}.{Guid.NewGuid():N}.pending");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, document, JsonOptions, cancellationToken);
                if (stream.Length > 128 * 1024) throw new InvalidDataException("O backup excede o limite seguro de leitura; nenhuma alteração foi aplicada.");
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private Task SetChangeStatusAsync(ChangeDocument document, UserChangeStatus status)
    {
        document.Status = status;
        return SaveDocumentAsync(document, CancellationToken.None);
    }

    private string DocumentPath(Guid id)
    {
        if (id == Guid.Empty) throw new InvalidDataException("Identificador vazio no histórico.");
        AssertNoRedirectedAncestors(storageRoot);
        return Path.Combine(storageRoot, $"{id:D}.json");
    }

    private void EnsureStorage()
    {
        AssertNoRedirectedAncestors(storageRoot);
        Directory.CreateDirectory(storageRoot);
        AssertNoRedirectedAncestors(storageRoot);
    }

    private static void AssertNoRedirectedAncestors(string path)
    {
        var current = new DirectoryInfo(Path.GetFullPath(path));
        while (current is not null)
        {
            if (File.Exists(current.FullName)) throw new IOException("Um arquivo ocupa o caminho do histórico.");
            if ((Directory.Exists(current.FullName) || current.LinkTarget is not null) && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("O histórico não aceita caminhos redirecionados.");
            current = current.Parent;
        }
    }

    private static void AssertRegularFileOrMissing(string path)
    {
        var info = new FileInfo(path);
        if (Directory.Exists(path) || info.LinkTarget is not null || info.Exists && (info.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException("O histórico não aceita arquivos redirecionados.");
    }

    private static void ValidateDocument(ChangeDocument document, Guid expectedId)
    {
        if (document.Version != 1 || document.Id != expectedId || document.Id == Guid.Empty || !Enum.IsDefined(document.Status) ||
            document.Scope != "CurrentUser" || string.IsNullOrWhiteSpace(document.Description) || document.Description.Length > 512 ||
            document.CreatedAt == default || document.CreatedAt > DateTimeOffset.UtcNow.AddMinutes(5))
            throw new InvalidDataException("Metadados inválidos no histórico.");
        switch (document.Kind)
        {
            case "startup" when document.Startup is { } value:
                if (value.Name.Length == 0 || value.Name.Length > 16383 || value.Name.Contains('\0') ||
                    value.Kind is not RegistryValueKind.String and not RegistryValueKind.ExpandString ||
                    value.StringValue is null || value.StringValue.Length > 32768 || value.StringValue.Contains('\0') || value.DWordValue is not null ||
                    document.RegistryPath != StartupScope)
                    throw new InvalidDataException("Backup de inicialização inválido.");
                break;
            case "preferences" when document.Preferences is not null && document.PreviousAnimation is not null:
                if (!Enum.IsDefined(document.Preferences.Profile) || document.RegistryPath != TransparencyScope ||
                    document.PreviousTransparency is { } transparency &&
                    (transparency.Name != TransparencyName || transparency.Kind != RegistryValueKind.DWord || transparency.DWordValue is null || transparency.StringValue is not null))
                    throw new InvalidDataException("Backup de preferências inválido.");
                break;
            case "power" when document.PreviousPowerPlan is { } previous && previous != Guid.Empty &&
                                    document.NewPowerPlan is { } next && next != Guid.Empty && document.RegistryPath is null:
                break;
            default:
                throw new InvalidDataException("O histórico contém uma ação não reconhecida.");
        }
    }

    private static ChangeDocument NewDocument(string kind, string description) => new()
    {
        Id = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow, Description = description, Kind = kind, Status = UserChangeStatus.Prepared,
        RegistryPath = kind == "startup" ? StartupScope : kind == "preferences" ? TransparencyScope : null
    };

    private static RegistrySnapshot? ReadValue(RegistryKey key, string name)
    {
        if (!key.GetValueNames().Contains(name, StringComparer.OrdinalIgnoreCase)) return null;
        var kind = key.GetValueKind(name);
        var data = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        return data switch
        {
            string text => new(name, kind, text, null),
            int number when kind == RegistryValueKind.DWord => new(name, kind, null, number),
            _ => new(name, kind, "[Tipo não suportado pelo ZEUS]", null)
        };
    }

    private static StartupEntry ToStartupEntry(RegistrySnapshot value, bool enabled)
    {
        var text = $"{value.Name} {value.StringValue}".ToLowerInvariant();
        string? reason = null;
        if (value.Name.Length == 0)
            reason = "A entrada padrão do registro não é uma inicialização nomeada suportada; o ZEUS a preserva.";
        else if (value.Kind is not RegistryValueKind.String and not RegistryValueKind.ExpandString)
            reason = "Tipo de registro não suportado; o ZEUS preserva esta entrada.";
        else if (new[] { "defender", "msmpeng", "securityhealth", "smartscreen", "antivirus", "avast", "kaspersky", "eset", "bitdefender", "malwarebytes", "mcafee", "norton", "sophos", "avira", "trendmicro", "fortinet", "crowdstrike", "carbonblack", "avgui" }.Any(text.Contains))
            reason = "Nome ou comando sugere software de segurança. Proteção heurística; não é uma certificação do arquivo.";
        else if (new[] { "onedrive", "dropbox", "googledrive", "google drive", "backup", "syncthing", "acronis", "veeam", "backblaze" }.Any(text.Contains))
            reason = "Nome ou comando sugere sincronização ou backup. Revise a inicialização no aplicativo correspondente.";
        return new(Fingerprint(value), value.Name, value.StringValue ?? "[Tipo não suportado]", enabled, reason is not null, reason);
    }

    private static string Fingerprint(RegistrySnapshot value) => Convert.ToHexString(SHA256.HashData(
        JsonSerializer.SerializeToUtf8Bytes(new { Name = value.Name.ToUpperInvariant(), value.Kind, value.StringValue, value.DWordValue })));
    private static bool IsFingerprint(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigit);
    private static bool SameValue(RegistrySnapshot? first, RegistrySnapshot? second) => first is null ? second is null :
        second is not null && Fingerprint(first) == Fingerprint(second);

    private static bool ReadAnimation()
    {
        if (!GetSystemParametersInfo(SpiGetClientAreaAnimation, 0, out var animation, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Não foi possível consultar animações do usuário.");
        return animation != 0;
    }

    private static void WriteAnimation(bool enabled)
    {
        // SPI_SETCLIENTAREAANIMATION expects the BOOL value in pvParam, not a pointer to a BOOL.
        if (!SetSystemParametersInfo(SpiSetClientAreaAnimation, 0, enabled, UpdateIniAndBroadcast))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Não foi possível configurar animações do usuário.");
    }

    private static int? ReadTransparency()
    {
        using var key = Registry.CurrentUser.OpenSubKey(TransparencyScope, writable: false);
        return key?.GetValue(TransparencyName) is int value ? value : null;
    }

    private static string? CheckVisualPolicy()
    {
        // Managed settings take precedence. Refuse writes rather than try to bypass a policy.
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
            foreach (var path in new[] { @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", @"Software\Policies\Microsoft\Windows\Personalization", @"Software\Policies\Microsoft\Windows\DWM" })
            {
                using var key = hive.OpenSubKey(path, writable: false);
                if (key is null) continue;
                foreach (var name in new[] { "NoChangeAnimation", "NoChangingVisualStyle", "NoChangingColor", "NoChangingTheme", "DisallowAnimations" })
                    if (key.GetValue(name) is int value && value != 0)
                        return "Uma política do dispositivo controla efeitos visuais; o ZEUS preservou essa política.";
            }
        return null;
    }

    private static void BroadcastVisualChange() => SendMessageTimeout(new IntPtr(0xffff), 0x001A, IntPtr.Zero,
        "ImmersiveColorSet", 0x0002, 1000, out _);

    private static async Task<Guid> GetActivePowerPlanAsync(CancellationToken cancellationToken)
    {
        var output = await RunPowerCfgAsync(["/getactivescheme"], cancellationToken);
        var match = GuidPattern.Match(output);
        if (!match.Success || !Guid.TryParse(match.Value, out var id) || id == Guid.Empty)
            throw new InvalidDataException("O Windows não informou o plano de energia ativo.");
        return id;
    }

    private static async Task<string> RunPowerCfgAsync(string[] arguments, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "powercfg.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("Não foi possível iniciar powercfg.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var output = process.StandardOutput.ReadToEndAsync(linked.Token);
        var error = process.StandardError.ReadToEndAsync(linked.Token);
        try { await process.WaitForExitAsync(linked.Token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            if (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                throw new IOException("powercfg excedeu o tempo limite.");
            throw;
        }
        var stdout = await output;
        var stderr = await error;
        if (process.ExitCode != 0) throw new IOException($"powercfg recusou a operação (código {process.ExitCode}): {stderr.Trim()}");
        return stdout;
    }

    private static void RequireWindows()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Esta operação requer Windows.");
    }

    private static UserChangeResult Failure(string message, Guid id = default) => new(id, false, message);
    private static UserChangeResult Success(Guid id, string message) => new(id, true, message);

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemParametersInfo(uint action, uint parameter, out int value, uint flags);
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetSystemParametersInfo(uint action, uint parameter, [MarshalAs(UnmanagedType.Bool)] bool value, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr parameter, string text, uint flags, uint timeout, out IntPtr result);

    // Public construction is required by System.Text.Json; fixed paths and kinds are validated before use.
    private sealed class ChangeDocument
    {
        public int Version { get; set; } = 1;
        public string Scope { get; set; } = "CurrentUser";
        public Guid Id { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public string Description { get; set; } = "";
        public string Kind { get; set; } = "";
        public string? RegistryPath { get; set; }
        public bool Restored { get; set; }
        public UserChangeStatus Status { get; set; }
        public RegistrySnapshot? Startup { get; set; }
        public UserOptimizationPreferences? Preferences { get; set; }
        public bool? PreviousAnimation { get; set; }
        public RegistrySnapshot? PreviousTransparency { get; set; }
        public Guid? PreviousPowerPlan { get; set; }
        public Guid? NewPowerPlan { get; set; }
    }

    private sealed record RegistrySnapshot(string Name, RegistryValueKind Kind, string? StringValue, int? DWordValue);
}

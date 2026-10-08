using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Zeus.Core;

namespace Zeus.Windows;

/// <summary>Shared protocol: the elevated helper writes, the desktop can only read.</summary>
public static class SessionStore
{
    private static readonly SecurityIdentifier Administrators = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    private static readonly SecurityIdentifier SystemAccount = new(WellKnownSidType.LocalSystemSid, null);
    private static readonly SecurityIdentifier Users = new(WellKnownSidType.BuiltinUsersSid, null);
    private const FileSystemRights WriteRights = FileSystemRights.Write | FileSystemRights.Delete |
        FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;

    public static string CreateSession(Guid sessionId)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        if (sessionId == Guid.Empty) throw new ArgumentException("A sessão precisa de um GUID válido.", nameof(sessionId));
        var commonData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        AssertNoReparseAncestors(commonData);
        var root = Path.Combine(commonData, "Zeus");
        var sessions = Path.Combine(root, "Sessions");
        CreateOrValidate(root);
        CreateOrValidate(sessions);
        var session = Path.Combine(sessions, sessionId.ToString("D"));
        if (Directory.Exists(session) || File.Exists(session))
            throw new IOException("A sessão já existe; o auxiliar não reutiliza diretórios.");
        new DirectoryInfo(session).Create(NewDirectorySecurity());
        ValidateDirectory(session);
        return session;
    }

    public static string GetSessionDirectory(Guid sessionId)
    {
        if (sessionId == Guid.Empty) throw new ArgumentException("Sessão inválida.", nameof(sessionId));
        var commonData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        AssertNoReparseAncestors(commonData);
        var root = Path.Combine(commonData, "Zeus");
        var sessions = Path.Combine(root, "Sessions");
        var directory = Path.Combine(sessions, sessionId.ToString("D"));
        ValidateDirectory(root);
        ValidateDirectory(sessions);
        ValidateDirectory(directory);
        return directory;
    }

    public static async Task<MaintenanceReport> ReadReportAsync(Guid sessionId)
    {
        var reportPath = Path.Combine(GetSessionDirectory(sessionId), "report.json");
        ValidateFile(reportPath);
        await using var stream = new FileStream(reportPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var report = await JsonSerializer.DeserializeAsync<MaintenanceReport>(stream)
            ?? throw new InvalidDataException("O relatório está vazio.");
        if (report.SessionId != sessionId) throw new InvalidDataException("O relatório não pertence a esta sessão.");
        return report;
    }

    public static FileStream AcquireMaintenanceLock(Guid sessionId)
    {
        var session = GetSessionDirectory(sessionId);
        var root = Directory.GetParent(Directory.GetParent(session)!.FullName)!.FullName;
        var path = Path.Combine(root, "maintenance.lock");
        if (!File.Exists(path)) return CreateProtectedFile(path, FileShare.None);
        ValidateFile(path);
        // Exclusive kernel file sharing serializes all helpers, including separate desktop instances.
        return new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    public static async Task WriteReportAsync(Guid sessionId, MaintenanceReport report)
    {
        if (report.SessionId != sessionId) throw new ArgumentException("Sessão e relatório diferentes.", nameof(report));
        var directory = GetSessionDirectory(sessionId);
        var temporary = Path.Combine(directory, "report.pending");
        await using (var stream = CreateProtectedFile(temporary))
        {
            await JsonSerializer.SerializeAsync(stream, report, new JsonSerializerOptions { WriteIndented = true });
            await stream.FlushAsync();
        }
        File.Move(temporary, Path.Combine(directory, "report.json"), overwrite: false);
    }

    /// <summary>Only constant log names from the helper are accepted.</summary>
    public static FileStream CreateLog(Guid sessionId, string fileName)
    {
        if (fileName.Length > 64 || fileName.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '.') ||
            !fileName.EndsWith(".log", StringComparison.Ordinal) || fileName.StartsWith('.'))
            throw new ArgumentException("Nome de log inválido.", nameof(fileName));
        return CreateProtectedFile(Path.Combine(GetSessionDirectory(sessionId), fileName));
    }

    private static FileStream CreateProtectedFile(string path, FileShare share = FileShare.Read)
    {
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(Administrators);
        security.AddAccessRule(new FileSystemAccessRule(SystemAccount, FileSystemRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(Administrators, FileSystemRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(Users, FileSystemRights.Read, AccessControlType.Allow));
        // Apply the descriptor atomically at CreateFile, also when the lock uses FileShare.None.
        return new FileInfo(path).Create(FileMode.CreateNew, FileSystemRights.Write, share,
            bufferSize: 4096, FileOptions.None, security);
    }

    private static void CreateOrValidate(string directory)
    {
        if (File.Exists(directory)) throw new IOException("Um arquivo ocupa o diretório de sessões.");
        if (!Directory.Exists(directory)) new DirectoryInfo(directory).Create(NewDirectorySecurity());
        ValidateDirectory(directory);
    }

    private static DirectorySecurity NewDirectorySecurity()
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(Administrators);
        foreach (var sid in new[] { SystemAccount, Administrators, Users })
        {
            var rights = sid.Equals(Users) ? FileSystemRights.ReadAndExecute : FileSystemRights.FullControl;
            security.AddAccessRule(new FileSystemAccessRule(sid, rights,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None, AccessControlType.Allow));
        }
        return security;
    }

    private static void ValidateDirectory(string path)
    {
        var info = new DirectoryInfo(path);
        if (!info.Exists || (info.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Diretório de sessões ausente ou com redirecionamento não permitido.");
        ValidateSecurity(info.GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner));
    }

    private static void ValidateFile(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || (info.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Relatório ausente ou com redirecionamento não permitido.");
        ValidateSecurity(info.GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner));
        if (info.Length > 4 * 1024 * 1024) throw new InvalidDataException("Relatório excede o tamanho permitido.");
    }

    private static void ValidateSecurity(FileSystemSecurity security)
    {
        var owner = security.GetOwner(typeof(SecurityIdentifier));
        if ((!Administrators.Equals(owner) && !SystemAccount.Equals(owner)) || !security.AreAccessRulesProtected)
            throw new UnauthorizedAccessException("O armazenamento não pertence a Administrators/SYSTEM com ACL protegida.");
        foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            if (rule.AccessControlType == AccessControlType.Allow && (rule.FileSystemRights & WriteRights) != 0 &&
                !Administrators.Equals(rule.IdentityReference) && !SystemAccount.Equals(rule.IdentityReference))
                throw new UnauthorizedAccessException("O armazenamento permite escrita por uma identidade não administrativa.");
        }
    }

    private static void AssertNoReparseAncestors(string directory)
    {
        var current = new DirectoryInfo(Path.GetFullPath(directory));
        while (current is not null)
        {
            if (!current.Exists || (current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("O caminho de dados contém um redirecionamento não permitido.");
            current = current.Parent;
        }
    }
}

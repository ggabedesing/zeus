using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Zeus.Windows;

/// <summary>Separate, per-Windows-user DPAPI storage. Never include this file in application exports.</summary>
public sealed class ObsCredentialStore
{
    private const int MaximumPasswordCharacters = 1024;
    private const int MaximumPlaintextBytes = 4096;
    private const int MaximumCiphertextBytes = 16384;
    private const string Failure = "Não foi possível acessar a senha local do OBS com segurança.";
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly string _directory;
    private readonly string _path;
    private readonly object _gate = new();

    public ObsCredentialStore(string? directory = null)
    {
        _directory = Path.GetFullPath(directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Zeus", "obs-connection"));
        _path = Path.Combine(_directory, "password.dpapi");
    }

    public void Save(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        if (password.Length > MaximumPasswordCharacters)
            throw new ArgumentException("A senha do OBS excede o limite permitido.", nameof(password));
        byte[] plaintext;
        try { plaintext = Utf8.GetBytes(password); }
        catch (EncoderFallbackException) { throw new ArgumentException("A senha do OBS contém texto inválido.", nameof(password)); }
        try
        {
            if (plaintext.Length > MaximumPlaintextBytes)
                throw new ArgumentException("A senha do OBS excede o limite permitido.", nameof(password));
            lock (_gate)
            {
                string? temporary = null;
                byte[]? encrypted = null;
                DirectoryLocks? ancestors = null;
                try
                {
                    EnsureWindows();
                    ancestors = LockAncestors(createMissing: true);
                    if (!ancestors.Complete) throw new IOException(Failure);
                    CheckTarget();
                    encrypted = Transform(plaintext, protect: true);
                    temporary = Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".tmp");
                    using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        file.Write(encrypted);
                        file.Flush(flushToDisk: true);
                    }
                    CheckTarget();
                    File.Move(temporary, _path, overwrite: true);
                    temporary = null;
                }
                catch (Exception exception) when (IsStorageFailure(exception)) { throw new IOException(Failure); }
                finally
                {
                    if (encrypted is not null) CryptographicOperations.ZeroMemory(encrypted);
                    if (temporary is not null)
                        try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                    ancestors?.Dispose();
                }
            }
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    public string? Load()
    {
        lock (_gate)
        {
            byte[]? encrypted = null;
            byte[]? plaintext = null;
            try
            {
                EnsureWindows();
                using var ancestors = LockAncestors();
                if (!ancestors.Complete) return null;
                CheckTarget();
                // OPEN_REPARSE_POINT and the handle attribute check prevent following a substituted file link.
                using var handle = CreateFileW(_path, 0x80000000, 1, IntPtr.Zero, 3, 0x00200000, IntPtr.Zero);
                if (handle.IsInvalid)
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error is 2 or 3) return null;
                    throw new IOException(Failure);
                }
                if (!GetFileInformationByHandle(handle, out var information)
                    || (information.Attributes & (uint)(FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
                    throw new IOException(Failure);
                using var file = new FileStream(handle, FileAccess.Read);
                if (file.Length is <= 0 or > MaximumCiphertextBytes) throw new IOException(Failure);
                encrypted = new byte[(int)file.Length];
                file.ReadExactly(encrypted);
                if (file.ReadByte() != -1) throw new IOException(Failure);
                plaintext = Transform(encrypted, protect: false);
                if (plaintext.Length > MaximumPlaintextBytes) throw new IOException(Failure);
                var password = Utf8.GetString(plaintext);
                if (password.Length > MaximumPasswordCharacters) throw new IOException(Failure);
                return password;
            }
            catch (Exception exception) when (IsStorageFailure(exception)) { throw new IOException(Failure); }
            finally
            {
                if (encrypted is not null) CryptographicOperations.ZeroMemory(encrypted);
                if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext);
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            try
            {
                EnsureWindows();
                using var ancestors = LockAncestors();
                if (!ancestors.Complete) return;
                CheckTarget();
                // A missing parent is also an absent credential. Ancestor and target link
                // checks still run first, so absence never authorizes following a junction.
                try { File.Delete(_path); }
                catch (DirectoryNotFoundException) { }
            }
            catch (Exception exception) when (IsStorageFailure(exception)) { throw new IOException(Failure); }
        }
    }

    // Hold every existing ancestor without delete sharing for the entire operation.
    // LIST_DIRECTORY activates sharing checks; this blocks rename/substitution. Write
    // sharing remains necessary for atomic file replacement. This is not a boundary
    // against malicious processes running as the same Windows user.
    private DirectoryLocks LockAncestors(bool createMissing = false)
    {
        var paths = new Stack<string>();
        for (DirectoryInfo? current = new(_directory); current is not null; current = current.Parent)
            paths.Push(current.FullName);
        var locks = new DirectoryLocks();
        try
        {
            while (paths.TryPop(out var path))
            {
                var handle = CreateFileW(path, 0x81, 3, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
                if (handle.IsInvalid)
                {
                    var error = Marshal.GetLastWin32Error();
                    handle.Dispose();
                    if (error is not (2 or 3)) throw new IOException(Failure);
                    if (!createMissing) return locks;
                    // Create only this component: its parent was already validated and
                    // remains locked. Never recursively create through an unchecked path.
                    if (!CreateDirectoryW(path, IntPtr.Zero) && Marshal.GetLastWin32Error() != 183)
                        throw new IOException(Failure);
                    handle = CreateFileW(path, 0x81, 3, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
                    if (handle.IsInvalid) { handle.Dispose(); throw new IOException(Failure); }
                }
                locks.Handles.Add(handle);
                if (!GetFileInformationByHandle(handle, out var information)
                    || (information.Attributes & (uint)FileAttributes.ReparsePoint) != 0
                    || (information.Attributes & (uint)FileAttributes.Directory) == 0)
                    throw new IOException(Failure);
            }
            locks.Complete = true;
            return locks;
        }
        catch { locks.Dispose(); throw; }
    }

    private sealed class DirectoryLocks : IDisposable
    {
        public List<SafeFileHandle> Handles { get; } = [];
        public bool Complete { get; set; }
        public void Dispose() { foreach (var handle in Handles) handle.Dispose(); }
    }

    private void CheckTarget()
    {
        var file = new FileInfo(_path);
        if (file.LinkTarget is not null || file.Exists && (file.Attributes & FileAttributes.ReparsePoint) != 0
            || Directory.Exists(_path)) throw new IOException(Failure);
    }

    private static bool IsStorageFailure(Exception exception) => exception is IOException
        or UnauthorizedAccessException or CryptographicException or DecoderFallbackException;

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("A senha local do OBS requer Windows.");
    }

    private static byte[] Transform(byte[] bytes, bool protect)
    {
        var input = new DataBlob { Length = bytes.Length, Data = Marshal.AllocHGlobal(Math.Max(1, bytes.Length)) };
        DataBlob output = default;
        try
        {
            Marshal.Copy(bytes, 0, input.Data, bytes.Length);
            var succeeded = protect
                ? CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!succeeded || output.Length < 0 || output.Length > MaximumCiphertextBytes
                || output.Data == IntPtr.Zero) throw new CryptographicException(Failure);
            var result = new byte[output.Length];
            Marshal.Copy(output.Data, result, 0, result.Length);
            return result;
        }
        finally
        {
            Wipe(input.Data, input.Length);
            Marshal.FreeHGlobal(input.Data);
            if (output.Data != IntPtr.Zero) { Wipe(output.Data, output.Length); LocalFree(output.Data); }
        }
    }

    private static void Wipe(IntPtr pointer, int length)
    {
        for (var index = 0; index < length; index++) Marshal.WriteByte(pointer, index, 0);
    }

    [StructLayout(LayoutKind.Sequential)] private struct DataBlob { public int Length; public IntPtr Data; }
    [StructLayout(LayoutKind.Sequential)] private struct FileInformation
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME Creation, Access, Write;
        public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref DataBlob input, string? description, IntPtr entropy,
        IntPtr reserved, IntPtr prompt, uint flags, out DataBlob output);
    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref DataBlob input, IntPtr description, IntPtr entropy,
        IntPtr reserved, IntPtr prompt, uint flags, out DataBlob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateDirectoryW(string path, IntPtr security);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security,
        uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle file, out FileInformation information);
}

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Zeus.Cleanup;

/// <summary>
/// Holds the validated file open until its move/delete completes. Windows uses operations
/// on the exclusive handle, rather than reopening a pathname after checksum validation.
/// </summary>
internal sealed class CleanupFileLease : IDisposable
{
    private const uint GenericRead = 0x80000000;
    private const uint DeleteAccess = 0x00010000;
    private const uint ReadAttributes = 0x80;
    private const uint OpenExisting = 3;
    private const uint OpenReparsePoint = 0x00200000;
    private const uint BackupSemantics = 0x02000000;
    private const uint Overlapped = 0x40000000;
    private readonly List<SafeFileHandle> _parents = [];
    private readonly string _path;
    private readonly FileStream _stream;
    private bool _disposed;

    private CleanupFileLease(string path)
    {
        _path = path;
        try
        {
            if (OperatingSystem.IsWindows())
            {
                PinParents(path, _parents);
                var handle = OpenWindows(path, GenericRead | DeleteAccess, 0, OpenReparsePoint | Overlapped);
                try
                {
                    RequireNormalHandle(handle, directory: false);
                    _stream = new FileStream(handle, FileAccess.Read, 81920, isAsync: true);
                }
                catch { handle.Dispose(); throw; }
            }
            else
            {
                _stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None, 81920,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
            }
        }
        catch
        {
            foreach (var parent in _parents) parent.Dispose();
            throw;
        }
    }

    public long Length => _stream.Length;
    public static CleanupFileLease Open(string path) => new(path);

    public async Task<string> HashAsync(CancellationToken cancellationToken)
    {
        _stream.Position = 0;
        return Convert.ToHexString(await SHA256.HashDataAsync(_stream, cancellationToken).ConfigureAwait(false));
    }

    public void MoveTo(string destination)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!OperatingSystem.IsWindows())
        {
            File.Move(_path, destination, overwrite: false);
            return;
        }
        var destinationParents = new List<SafeFileHandle>();
        try
        {
            PinParents(destination, destinationParents);
            var name = Encoding.Unicode.GetBytes(destination);
            var rootOffset = IntPtr.Size;
            var lengthOffset = IntPtr.Size * 2;
            var nameOffset = lengthOffset + sizeof(uint);
            var buffer = Marshal.AllocHGlobal(nameOffset + name.Length);
            try
            {
                // FILE_RENAME_INFO: ReplaceIfExists=false; RootDirectory=NULL; exact UTF-16 name.
                for (var offset = 0; offset < nameOffset; offset++) Marshal.WriteByte(buffer, offset, 0);
                Marshal.WriteIntPtr(buffer, rootOffset, IntPtr.Zero);
                Marshal.WriteInt32(buffer, lengthOffset, name.Length);
                Marshal.Copy(name, 0, buffer + nameOffset, name.Length);
                if (!SetFileInformationByHandle(_stream.SafeFileHandle, 3, buffer, (uint)(nameOffset + name.Length)))
                    ThrowWindowsError("Não foi possível mover o arquivo validado.");
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        finally { foreach (var parent in destinationParents) parent.Dispose(); }
    }

    public void Delete()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!OperatingSystem.IsWindows())
        {
            File.Delete(_path);
            return;
        }
        var buffer = Marshal.AllocHGlobal(1);
        try
        {
            Marshal.WriteByte(buffer, 1);
            if (!SetFileInformationByHandle(_stream.SafeFileHandle, 4, buffer, 1))
                ThrowWindowsError("Não foi possível excluir o arquivo validado.");
        }
        finally { Marshal.FreeHGlobal(buffer); }
        // Windows finishes a disposition deletion on final handle close.
        Dispose();
    }

    public static string CanonicalizeRoot(string root)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (!OperatingSystem.IsWindows()) return full;
        full = NormalizeWindowsPath(full);
        var missing = new Stack<string>();
        var existing = full;
        while (!Directory.Exists(existing))
        {
            var parent = Path.GetDirectoryName(existing);
            if (parent is null) return full;
            missing.Push(Path.GetFileName(existing));
            existing = parent;
        }
        using var handle = OpenWindows(existing, ReadAttributes, 7, BackupSemantics | OpenReparsePoint);
        RequireNormalHandle(handle, directory: true);
        var capacity = 512;
        string canonical;
        while (true)
        {
            var buffer = new StringBuilder(capacity);
            var length = GetFinalPathNameByHandleW(handle, buffer, (uint)capacity, 0);
            if (length == 0) ThrowWindowsError("Não foi possível confirmar o diretório de limpeza.");
            if (length < capacity) { canonical = NormalizeWindowsPath(buffer.ToString()); break; }
            capacity = checked((int)length + 1);
        }
        while (missing.Count > 0) canonical = Path.Combine(canonical, missing.Pop());
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(canonical));
    }

    private static string NormalizeWindowsPath(string path)
    {
        if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) return @"\\" + path[8..];
        if (path.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase))
        {
            var ordinary = path[4..];
            if (ordinary.Length < 3 || !char.IsAsciiLetter(ordinary[0]) || ordinary[1] != ':' || ordinary[2] != '\\')
                throw new ArgumentException("Caminhos de dispositivo não são aceitos para limpeza.");
            return Path.GetFullPath(ordinary);
        }
        if (path.StartsWith(@"\\.\", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Caminhos de dispositivo não são aceitos para limpeza.");
        return path;
    }

    private static void PinParents(string filePath, List<SafeFileHandle> handles)
    {
        var pending = new Stack<string>();
        for (var directory = Path.GetDirectoryName(Path.GetFullPath(filePath)); directory is not null;
             directory = Path.GetDirectoryName(directory))
            pending.Push(directory);
        while (pending.Count > 0)
        {
            // Excluding FILE_SHARE_DELETE pins each directory against replacement during the operation.
            var handle = OpenWindows(pending.Pop(), ReadAttributes, 3, BackupSemantics | OpenReparsePoint);
            try { RequireNormalHandle(handle, directory: true); handles.Add(handle); }
            catch { handle.Dispose(); throw; }
        }
    }

    private static SafeFileHandle OpenWindows(string path, uint access, uint sharing, uint flags)
    {
        var handle = CreateFileW(path, access, sharing, IntPtr.Zero, OpenExisting, flags, IntPtr.Zero);
        if (!handle.IsInvalid) return handle;
        var error = Marshal.GetLastPInvokeError();
        handle.Dispose();
        throw new IOException("Não foi possível acessar exclusivamente o arquivo ou diretório.", new Win32Exception(error));
    }

    private static void RequireNormalHandle(SafeFileHandle handle, bool directory)
    {
        if (!GetFileInformationByHandleEx(handle, 9, out AttributeTagInfo info, (uint)Marshal.SizeOf<AttributeTagInfo>()))
            ThrowWindowsError("Não foi possível verificar o tipo de arquivo.");
        if ((info.Attributes & (uint)FileAttributes.ReparsePoint) != 0 ||
            ((info.Attributes & (uint)FileAttributes.Directory) != 0) != directory)
            throw new InvalidDataException("O objeto é um link, redirecionamento ou tipo de arquivo incompatível.");
    }

    private static void ThrowWindowsError(string message) =>
        throw new IOException(message, new Win32Exception(Marshal.GetLastPInvokeError()));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stream.Dispose();
        foreach (var parent in _parents) parent.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AttributeTagInfo { public uint Attributes; public uint ReparseTag; }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string fileName, uint desiredAccess, uint shareMode,
        IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int infoClass,
        out AttributeTagInfo info, uint bufferSize);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int infoClass, IntPtr info, uint bufferSize);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle handle, StringBuilder path, uint size, uint flags);
}

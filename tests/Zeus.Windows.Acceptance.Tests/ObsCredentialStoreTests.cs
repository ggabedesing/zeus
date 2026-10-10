using System.Text;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Zeus.Windows;

namespace Zeus.Windows.Acceptance.Tests;

public sealed class ObsCredentialStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Zeus.ObsCredential." + Guid.NewGuid().ToString("N"));
    private string SecretPath => Path.Combine(_root, "password.dpapi");

    [ObsCredentialWindowsFact]
    public void MissingCredentialIsAbsentAndClearIsIdempotent()
    {
        var store = new ObsCredentialStore(_root);
        Assert.Null(store.Load());
        store.Clear(); store.Clear();
        Assert.False(Directory.Exists(_root));
    }

    [ObsCredentialWindowsFact]
    public void MultipleMissingAncestorsAreCreatedOnlyForSave()
    {
        var leaf = Path.Combine(_root, "first", "second", "credentials");
        var store = new ObsCredentialStore(leaf);
        Assert.Null(store.Load());
        store.Clear();
        Assert.False(Directory.Exists(_root));
        store.Save("nested credential");
        Assert.Equal("nested credential", store.Load());
        Assert.Single(Directory.GetFiles(leaf));
        store.Clear();
        Assert.Null(store.Load());
        Assert.Empty(Directory.GetFiles(leaf));
    }

    [ObsCredentialWindowsFact]
    public void AncestorLockBlocksDirectoryDeleteOpenUntilReleased()
    {
        Directory.CreateDirectory(_root);
        var store = new ObsCredentialStore(_root);
        var method = typeof(ObsCredentialStore).GetMethod("LockAncestors", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var held = (IDisposable)method.Invoke(store, [false])!;
        try
        {
            Assert.True((bool)held.GetType().GetProperty("Complete")!.GetValue(held)!);
            using var blocked = OpenDirectoryHandle(_root, 0x00010000, 7, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
            var error = Marshal.GetLastWin32Error();
            Assert.True(blocked.IsInvalid);
            Assert.Equal(32, error); // ERROR_SHARING_VIOLATION; no directory mutation is attempted.
            // Normal writes remain shared for the atomic rename operation. Do not claim
            // this protects against every metadata change by the same Windows user.
            using var writable = OpenDirectoryHandle(_root, 0x40000000, 7, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
            Assert.False(writable.IsInvalid);
        }
        finally { held.Dispose(); }
        using var allowed = OpenDirectoryHandle(_root, 0x00010000, 7, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
        Assert.False(allowed.IsInvalid);
    }

    [ObsCredentialWindowsFact]
    public void RoundTripUsesCiphertextAndCanBeReplacedAndCleared()
    {
        var store = new ObsCredentialStore(_root);
        const string password = "senha OBS çΩ 🎮 nunca exportada";
        store.Save(password);
        Assert.Equal(password, new ObsCredentialStore(_root).Load());
        var file = File.ReadAllBytes(SecretPath);
        Assert.False(file.AsSpan().IndexOf(Encoding.UTF8.GetBytes(password)) >= 0);
        Assert.Single(Directory.GetFiles(_root));
        store.Save("substituída");
        Assert.Equal("substituída", store.Load());
        store.Clear();
        Assert.Null(store.Load());
        Assert.Empty(Directory.GetFiles(_root));
    }

    [ObsCredentialWindowsFact]
    public void EmptyPasswordRoundTrips()
    {
        var store = new ObsCredentialStore(_root);
        store.Save(string.Empty);
        Assert.Equal(string.Empty, store.Load());
    }

    [ObsCredentialWindowsFact]
    public void RejectedPasswordPreservesPreviousCredentialAndDoesNotExposeInput()
    {
        var store = new ObsCredentialStore(_root);
        store.Save("anterior");
        var oversized = new string('Q', 1025);
        var error = Assert.Throws<ArgumentException>(() => store.Save(oversized));
        Assert.DoesNotContain(oversized, error.ToString());
        Assert.Throws<ArgumentException>(() => store.Save("\ud800"));
        Assert.Equal("anterior", store.Load());
        Assert.Single(Directory.GetFiles(_root));
    }

    [ObsCredentialWindowsFact]
    public void CorruptedAndOversizedCiphertextAreRejectedWithoutRawDetails()
    {
        var store = new ObsCredentialStore(_root);
        Directory.CreateDirectory(_root);
        const string marker = "sensitive corrupt payload";
        File.WriteAllText(SecretPath, marker);
        var error = Assert.Throws<IOException>(() => store.Load());
        Assert.DoesNotContain(marker, error.ToString());
        Assert.Null(error.InnerException);
        File.WriteAllBytes(SecretPath, new byte[16385]);
        Assert.Throws<IOException>(() => store.Load());
        File.WriteAllBytes(SecretPath, []);
        Assert.Throws<IOException>(() => store.Load());
    }

    [ObsCredentialWindowsFact]
    public void DirectoryAtCredentialPathCannotBeOverwrittenOrDeleted()
    {
        Directory.CreateDirectory(SecretPath);
        var store = new ObsCredentialStore(_root);
        Assert.Throws<IOException>(() => store.Save("secret"));
        Assert.Throws<IOException>(() => store.Load());
        Assert.Throws<IOException>(() => store.Clear());
        Assert.True(Directory.Exists(SecretPath));
    }

    [ObsCredentialWindowsFact]
    public void JunctionDirectoryIsRejectedWithoutTouchingTarget()
    {
        Directory.CreateDirectory(_root);
        var target = Path.Combine(_root, "target");
        var junction = Path.Combine(_root, "junction");
        Directory.CreateDirectory(target);
        var original = new ObsCredentialStore(target);
        original.Save("protected target");
        var before = File.ReadAllBytes(Path.Combine(target, "password.dpapi"));
        var start = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add("/c"); start.ArgumentList.Add("mklink"); start.ArgumentList.Add("/J");
        start.ArgumentList.Add(junction); start.ArgumentList.Add(target);
        using var process = Process.Start(start)!;
        Assert.True(process.WaitForExit(10000));
        Assert.Equal(0, process.ExitCode);
        try
        {
            var blocked = new ObsCredentialStore(junction);
            Assert.Throws<IOException>(() => blocked.Load());
            Assert.Throws<IOException>(() => blocked.Save("must not replace"));
            Assert.Throws<IOException>(() => blocked.Clear());
            Assert.Equal(before, File.ReadAllBytes(Path.Combine(target, "password.dpapi")));
        }
        finally { Directory.Delete(junction); }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle OpenDirectoryHandle(string path, uint access, uint share,
        IntPtr security, uint creation, uint flags, IntPtr template);
}

public sealed class ObsCredentialWindowsFactAttribute : FactAttribute
{
    public ObsCredentialWindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "DPAPI requer Windows.";
    }
}

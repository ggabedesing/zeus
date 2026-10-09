using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Zeus.Windows;

/// <summary>Starts suspended inside a private job before any collector code can run. Only its three standard handles are inherited.</summary>
internal sealed class PerformanceCollectorProcess : IDisposable
{
    internal Process Process { get; }
    internal StreamReader Output { get; }
    internal StreamReader Error { get; }
    internal PerformanceCollectorJob Job { get; }
    private PerformanceCollectorProcess(Process process,PerformanceCollectorJob job,SafeFileHandle output,SafeFileHandle error)
    {
        Process=process; Job=job;
        Output=new(new FileStream(output,FileAccess.Read),new UTF8Encoding(false,true),false);
        Error=new(new FileStream(error,FileAccess.Read),new UTF8Encoding(false,true),false);
    }
    internal static PerformanceCollectorProcess Start(ProcessStartInfo info)
    {
        var security=new SecurityAttributes { Length=Marshal.SizeOf<SecurityAttributes>(), Inherit=true };
        if(!CreatePipe(out var outRead,out var outWrite,ref security,0)) throw ErrorCode();
        using var outputWrite=outWrite;
        using var outputRead=outRead;
        if(!CreatePipe(out var errRead,out var errWrite,ref security,0)) throw ErrorCode();
        using var errorWrite=errWrite;
        using var errorRead=errRead;
        using var input=CreateFile("NUL",0x80000000,3,ref security,3,0,IntPtr.Zero);
        if(input.IsInvalid || !SetHandleInformation(outRead,1,0) || !SetHandleInformation(errRead,1,0)) throw ErrorCode();
        nuint attributeBytes=0;
        InitializeProcThreadAttributeList(IntPtr.Zero,1,0,ref attributeBytes);
        var attributes=Marshal.AllocHGlobal(checked((int)attributeBytes));
        var handles=Marshal.AllocHGlobal(IntPtr.Size*3);
        var initialized=false;
        ProcessInformation pi=default;
        Process? process=null;
        PerformanceCollectorJob? job=null;
        try
        {
            if(!InitializeProcThreadAttributeList(attributes,1,0,ref attributeBytes)) throw ErrorCode();
            initialized=true;
            Marshal.WriteIntPtr(handles,0,outWrite.DangerousGetHandle());
            Marshal.WriteIntPtr(handles,IntPtr.Size,errWrite.DangerousGetHandle());
            Marshal.WriteIntPtr(handles,IntPtr.Size*2,input.DangerousGetHandle());
            if(!UpdateProcThreadAttribute(attributes,0,(IntPtr)0x20002,handles,(nuint)(IntPtr.Size*3),IntPtr.Zero,IntPtr.Zero)) throw ErrorCode();
            var startup=new StartupInfoEx { Startup=new StartupInfo { Size=Marshal.SizeOf<StartupInfoEx>(), Flags=0x100,
                StandardInput=input.DangerousGetHandle(),StandardOutput=outWrite.DangerousGetHandle(),StandardError=errWrite.DangerousGetHandle() }, Attributes=attributes };
            var command=new StringBuilder(Quote(info.FileName));
            foreach(var argument in info.ArgumentList) command.Append(' ').Append(Quote(argument));
            if(!CreateProcess(info.FileName,command,IntPtr.Zero,IntPtr.Zero,true,0x80000|0x4|0x08000000,IntPtr.Zero,
                string.IsNullOrWhiteSpace(info.WorkingDirectory)?null:info.WorkingDirectory,ref startup,out pi)) throw ErrorCode();
            process=Process.GetProcessById((int)pi.ProcessId);
            job=PerformanceCollectorJob.Attach(process);
            // Duplicate parent read handles; local using statements retain exception-safe ownership.
            if(!DuplicateHandle(GetCurrentProcess(),outRead,GetCurrentProcess(),out var ownedOut,0,false,2)) throw ErrorCode();
            if(!DuplicateHandle(GetCurrentProcess(),errRead,GetCurrentProcess(),out var ownedErr,0,false,2)) { ownedOut.Dispose(); throw ErrorCode(); }
            var owned=new PerformanceCollectorProcess(process,job,ownedOut,ownedErr);
            if(ResumeThread(pi.Thread)==uint.MaxValue) { owned.Dispose(); throw ErrorCode(); }
            process=null; job=null;
            return owned;
        }
        catch
        {
            if(pi.Process!=IntPtr.Zero) { TerminateProcess(pi.Process,1); if(WaitForSingleObject(pi.Process,5000)!=0) throw new IOException("O encerramento do coletor não foi confirmado."); }
            job?.Dispose(); process?.Dispose(); throw;
        }
        finally
        {
            if(pi.Thread!=IntPtr.Zero) CloseHandle(pi.Thread);
            if(pi.Process!=IntPtr.Zero) CloseHandle(pi.Process);
            if(initialized) DeleteProcThreadAttributeList(attributes);
            Marshal.FreeHGlobal(handles); Marshal.FreeHGlobal(attributes);
        }
    }
    internal static string Quote(string value)
    {
        var result=new StringBuilder("\""); var slashes=0;
        foreach(var c in value)
        {
            if(c=='\\') { slashes++; continue; }
            if(c=='"') { result.Append('\\',slashes*2+1).Append(c); slashes=0; continue; }
            result.Append('\\',slashes).Append(c); slashes=0;
        }
        return result.Append('\\',slashes*2).Append('"').ToString();
    }
    public void Dispose() { Job.Dispose(); Output.Dispose(); Error.Dispose(); Process.Dispose(); }
    private static Win32Exception ErrorCode()=>new(Marshal.GetLastWin32Error());
    [StructLayout(LayoutKind.Sequential)] private struct SecurityAttributes { public int Length; public IntPtr Descriptor; [MarshalAs(UnmanagedType.Bool)] public bool Inherit; }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] private struct StartupInfo
    { public int Size; public string? Reserved,Desktop,Title; public int X,Y,XSize,YSize,XCountChars,YCountChars,FillAttribute,Flags; public short ShowWindow,ReservedSize; public IntPtr ReservedPointer,StandardInput,StandardOutput,StandardError; }
    [StructLayout(LayoutKind.Sequential)] private struct StartupInfoEx { public StartupInfo Startup; public IntPtr Attributes; }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInformation { public IntPtr Process,Thread; public uint ProcessId,ThreadId; }
    [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool CreatePipe(out SafeFileHandle read,out SafeFileHandle write,ref SecurityAttributes security,uint size);
    [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool SetHandleInformation(SafeFileHandle handle,uint mask,uint flags);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern SafeFileHandle CreateFile(string name,uint access,uint share,ref SecurityAttributes security,uint creation,uint flags,IntPtr template);
    [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool InitializeProcThreadAttributeList(IntPtr list,int count,uint flags,ref nuint size);
    [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool UpdateProcThreadAttribute(IntPtr list,uint flags,IntPtr attribute,IntPtr value,nuint size,IntPtr previous,IntPtr returnSize);
    [DllImport("kernel32.dll")] private static extern void DeleteProcThreadAttributeList(IntPtr list);
    [DllImport("kernel32.dll",EntryPoint="CreateProcessW",CharSet=CharSet.Unicode,SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool CreateProcess(string application,StringBuilder command,IntPtr processAttributes,IntPtr threadAttributes,[MarshalAs(UnmanagedType.Bool)] bool inherit,uint flags,IntPtr environment,string? directory,ref StartupInfoEx startup,out ProcessInformation information);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32.dll")] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool TerminateProcess(IntPtr process,uint code);
    [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(IntPtr handle,uint milliseconds);
    [DllImport("kernel32.dll")] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool DuplicateHandle(IntPtr sourceProcess,SafeFileHandle source,IntPtr targetProcess,out SafeFileHandle target,uint access,[MarshalAs(UnmanagedType.Bool)] bool inherit,uint options);
}

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Zeus.Windows;

/// <summary>Owns every descendant even if the initial host exits before its redirected pipes close.</summary>
internal sealed class PerformanceCollectorJob : SafeHandleZeroOrMinusOneIsInvalid
{
    private PerformanceCollectorJob() : base(true) { }
    internal static PerformanceCollectorJob Attach(Process process)
    {
        var job = CreateJobObject(IntPtr.Zero,null);
        if(job.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var limits = new ExtendedLimit { Basic = new BasicLimit { LimitFlags = 0x2000 } }; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
            if(!SetInformationJobObject(job,9,ref limits,(uint)Marshal.SizeOf<ExtendedLimit>()) || !AssignProcessToJobObject(job,process.Handle))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            return job;
        }
        catch { job.Dispose(); throw; }
    }
    protected override bool ReleaseHandle() => CloseHandle(handle);
    [StructLayout(LayoutKind.Sequential)] private struct BasicLimit
    { public long PerProcessUserTimeLimit,PerJobUserTimeLimit; public uint LimitFlags; public UIntPtr MinimumWorkingSetSize,MaximumWorkingSetSize; public uint ActiveProcessLimit; public UIntPtr Affinity; public uint PriorityClass,SchedulingClass; }
    [StructLayout(LayoutKind.Sequential)] private struct IoCounters
    { public ulong ReadOperationCount,WriteOperationCount,OtherOperationCount,ReadTransferCount,WriteTransferCount,OtherTransferCount; }
    [StructLayout(LayoutKind.Sequential)] private struct ExtendedLimit
    { public BasicLimit Basic; public IoCounters Io; public UIntPtr ProcessMemoryLimit,JobMemoryLimit,PeakProcessMemoryUsed,PeakJobMemoryUsed; }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern PerformanceCollectorJob CreateJobObject(IntPtr attributes,string? name);
    [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool SetInformationJobObject(PerformanceCollectorJob job,int informationClass,ref ExtendedLimit info,uint length);
    [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool AssignProcessToJobObject(PerformanceCollectorJob job,IntPtr process);
    [DllImport("kernel32.dll")] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(IntPtr handle);
}

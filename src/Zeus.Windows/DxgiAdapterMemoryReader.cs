using System.Runtime.InteropServices;

namespace Zeus.Windows;

internal sealed record DxgiAdapterMemoryInfo(string AdapterInstance, ulong DedicatedCapacityBytes);

/// <summary>Reads adapter identity and dedicated-memory capacity from DXGI without changing adapter state.</summary>
internal static class DxgiAdapterMemoryReader
{
    private static readonly Guid Factory1Iid = new("770aae78-f26f-4dba-a829-253c83d1b387");
    private static readonly Guid Adapter1Iid = new("29038f61-3839-4626-91fd-086879011a05");
    private const int DxgiErrorNotFound = unchecked((int)0x887A0002);

    public static IReadOnlyList<DxgiAdapterMemoryInfo> ReadAdapters()
    {
        var factoryIid = Factory1Iid;
        Marshal.ThrowExceptionForHR(CreateDXGIFactory1(ref factoryIid, out var factory));
        try
        {
            var adapters = new List<DxgiAdapterMemoryInfo>();
            var adapterIid = Adapter1Iid;
            for (uint index = 0; index < 128; index++)
            {
                var result = factory.EnumAdapters1(index, out var adapter);
                if (result == DxgiErrorNotFound) break;
                Marshal.ThrowExceptionForHR(result);
                try
                {
                    Marshal.ThrowExceptionForHR(adapter.GetDesc1(out var description));
                    adapters.Add(new(FormatInstance(description.AdapterLuid), description.DedicatedVideoMemory.ToUInt64()));
                }
                finally { Release(adapter); }
            }

            if (adapters.Count == 128) throw new InvalidDataException("DXGI retornou 128 adaptadores; a enumeração foi limitada.");
            return adapters;
        }
        finally { Release(factory); }
    }

    internal static string FormatInstance(DxgiLuid luid) =>
        $"luid_0x{luid.HighPart:X8}_0x{luid.LowPart:X8}_phys_0";

    private static void Release(object value)
    {
        if (Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
    }

    [DllImport("dxgi.dll", ExactSpelling = true)]
    private static extern int CreateDXGIFactory1(ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IDXGIFactory1 factory);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DxgiAdapterDescription1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public UIntPtr DedicatedVideoMemory;
        public UIntPtr DedicatedSystemMemory;
        public UIntPtr SharedSystemMemory;
        public DxgiLuid AdapterLuid;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DxgiLuid
    {
        public uint LowPart;
        public int HighPart;
    }

    // Methods are declared in inherited vtable order; unused inherited calls keep their native slots intact.
    [ComImport, Guid("770aae78-f26f-4dba-a829-253c83d1b387"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXGIFactory1
    {
        [PreserveSig] int SetPrivateData(ref Guid name, uint dataSize, IntPtr data);
        [PreserveSig] int SetPrivateDataInterface(ref Guid name, IntPtr unknown);
        [PreserveSig] int GetPrivateData(ref Guid name, ref uint dataSize, IntPtr data);
        [PreserveSig] int GetParent(ref Guid riid, out IntPtr parent);
        [PreserveSig] int EnumAdapters(uint index, out IntPtr adapter);
        [PreserveSig] int MakeWindowAssociation(IntPtr window, uint flags);
        [PreserveSig] int GetWindowAssociation(out IntPtr window);
        [PreserveSig] int CreateSwapChain(IntPtr device, IntPtr description, out IntPtr swapChain);
        [PreserveSig] int CreateSoftwareAdapter(IntPtr module, out IntPtr adapter);
        [PreserveSig] int EnumAdapters1(uint index, [MarshalAs(UnmanagedType.Interface)] out IDXGIAdapter1 adapter);
        [return: MarshalAs(UnmanagedType.I1)] bool IsCurrent();
    }

    [ComImport, Guid("29038f61-3839-4626-91fd-086879011a05"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXGIAdapter1
    {
        [PreserveSig] int SetPrivateData(ref Guid name, uint dataSize, IntPtr data);
        [PreserveSig] int SetPrivateDataInterface(ref Guid name, IntPtr unknown);
        [PreserveSig] int GetPrivateData(ref Guid name, ref uint dataSize, IntPtr data);
        [PreserveSig] int GetParent(ref Guid riid, out IntPtr parent);
        [PreserveSig] int EnumOutputs(uint index, out IntPtr output);
        [PreserveSig] int GetDesc(IntPtr description);
        [PreserveSig] int CheckInterfaceSupport(ref Guid interfaceName, out long userModeDriverVersion);
        [PreserveSig] int GetDesc1(out DxgiAdapterDescription1 description);
    }
}

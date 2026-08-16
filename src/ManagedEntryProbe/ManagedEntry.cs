using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace GuideXOS.Net10.ManagedEntryProbe;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct GuideXBootInfo
{
    public const uint ExpectedMagic = 0x534F5847; // "GXOS" in little-endian order.
    public const ushort CurrentVersion = 1;
    public const uint ArchitectureX64 = 0x8664;
    public const ushort MinimumSize = 24;

    public uint Magic;
    public ushort Version;
    public ushort Size;
    public uint Architecture;
    public uint Flags;
    public ulong SerialWrite;
}

public static unsafe class ManagedEntry
{
    private const int MarkerLength = 29;
    private static int s_managedCallbackCount;

    [UnmanagedCallersOnly(EntryPoint = "ManagedCallback")]
    public static int ManagedCallback(int value)
    {
        if ((uint)value > 0xFFFEU)
        {
            return -1;
        }

        s_managedCallbackCount++;
        return (s_managedCallbackCount << 16) | (value + 1);
    }

#if GXOS_ALLOCATION_PROBE
    private const ulong AllocationValue = 0x1122334455667788UL;
    private const int FirstAllocationMarkerLength = 32;

    internal sealed class AllocationProbeObject
    {
        public nuint Value;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static AllocationProbeObject AllocateOne(nuint value)
    {
        AllocationProbeObject instance = new AllocationProbeObject();
        instance.Value = value;
        return instance;
    }
#endif

    [UnmanagedCallersOnly(EntryPoint = "ManagedMain")]
    public static int ManagedMain(nint bootInfoAddress)
    {
        if (bootInfoAddress == 0)
        {
            return -1;
        }

        GuideXBootInfo* bootInfo = (GuideXBootInfo*)bootInfoAddress;
        if (bootInfo->Magic != GuideXBootInfo.ExpectedMagic ||
            bootInfo->Version != GuideXBootInfo.CurrentVersion ||
            bootInfo->Size < GuideXBootInfo.MinimumSize ||
            bootInfo->Architecture != GuideXBootInfo.ArchitectureX64 ||
            bootInfo->SerialWrite == 0)
        {
            return -2;
        }

        delegate* unmanaged<byte*, nuint, void> serialWrite =
            (delegate* unmanaged<byte*, nuint, void>)(nuint)bootInfo->SerialWrite;

#if GXOS_ALLOCATION_PROBE
        AllocationProbeObject instance = AllocateOne((nuint)AllocationValue);
        if (instance is null || instance.Value != (nuint)AllocationValue)
        {
            return -10;
        }

        byte* markerAddress = stackalloc byte[FirstAllocationMarkerLength];
        markerAddress[0] = (byte)'G';
        markerAddress[1] = (byte)'X';
        markerAddress[2] = (byte)'O';
        markerAddress[3] = (byte)'S';
        markerAddress[4] = (byte)'_';
        markerAddress[5] = (byte)'N';
        markerAddress[6] = (byte)'E';
        markerAddress[7] = (byte)'T';
        markerAddress[8] = (byte)'1';
        markerAddress[9] = (byte)'0';
        markerAddress[10] = (byte)':';
        markerAddress[11] = (byte)'F';
        markerAddress[12] = (byte)'I';
        markerAddress[13] = (byte)'R';
        markerAddress[14] = (byte)'S';
        markerAddress[15] = (byte)'T';
        markerAddress[16] = (byte)'_';
        markerAddress[17] = (byte)'A';
        markerAddress[18] = (byte)'L';
        markerAddress[19] = (byte)'L';
        markerAddress[20] = (byte)'O';
        markerAddress[21] = (byte)'C';
        markerAddress[22] = (byte)'A';
        markerAddress[23] = (byte)'T';
        markerAddress[24] = (byte)'I';
        markerAddress[25] = (byte)'O';
        markerAddress[26] = (byte)'N';
        markerAddress[27] = (byte)'_';
        markerAddress[28] = (byte)'O';
        markerAddress[29] = (byte)'K';
        markerAddress[30] = (byte)'\r';
        markerAddress[31] = (byte)'\n';
        serialWrite(markerAddress, (nuint)FirstAllocationMarkerLength);
        return 0;
#else
#if GXOS_NEGATIVE_RETURN
        return 7;
#endif

        byte* markerAddress = stackalloc byte[MarkerLength];
        markerAddress[0] = (byte)'G';
        markerAddress[1] = (byte)'X';
        markerAddress[2] = (byte)'O';
        markerAddress[3] = (byte)'S';
        markerAddress[4] = (byte)'_';
        markerAddress[5] = (byte)'N';
        markerAddress[6] = (byte)'E';
        markerAddress[7] = (byte)'T';
        markerAddress[8] = (byte)'1';
        markerAddress[9] = (byte)'0';
        markerAddress[10] = (byte)':';
        markerAddress[11] = (byte)'M';
        markerAddress[12] = (byte)'A';
        markerAddress[13] = (byte)'N';
        markerAddress[14] = (byte)'A';
        markerAddress[15] = (byte)'G';
        markerAddress[16] = (byte)'E';
        markerAddress[17] = (byte)'D';
        markerAddress[18] = (byte)'_';
        markerAddress[19] = (byte)'E';
        markerAddress[20] = (byte)'N';
        markerAddress[21] = (byte)'T';
        markerAddress[22] = (byte)'R';
        markerAddress[23] = (byte)'Y';
        markerAddress[24] = (byte)'_';
        markerAddress[25] = (byte)'O';
#if GXOS_NEGATIVE_MARKER
        markerAddress[26] = (byte)'X';
#else
        markerAddress[26] = (byte)'K';
#endif
        markerAddress[27] = (byte)'\r';
        markerAddress[28] = (byte)'\n';
        serialWrite(markerAddress, (nuint)MarkerLength);

        return 0;
#endif
    }

    // Keeps this NativeAOT executable independently runnable without using a host API.
    // The freestanding proof calls ManagedMain through the exported symbol instead.
    public static int Main() => 0;
}

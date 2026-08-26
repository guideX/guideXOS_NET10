using System;

namespace GuideXOS.Net10.ManagedKernel;

internal readonly struct ManagedVirtioPciCapabilities
{
    internal readonly byte CommonBar;
    internal readonly uint CommonOffset;
    internal readonly uint CommonLength;
    internal readonly byte NotifyBar;
    internal readonly uint NotifyOffset;
    internal readonly uint NotifyLength;
    internal readonly uint NotifyMultiplier;

    internal ManagedVirtioPciCapabilities(
        byte commonBar, uint commonOffset, uint commonLength,
        byte notifyBar, uint notifyOffset, uint notifyLength,
        uint notifyMultiplier)
    {
        CommonBar = commonBar;
        CommonOffset = commonOffset;
        CommonLength = commonLength;
        NotifyBar = notifyBar;
        NotifyOffset = notifyOffset;
        NotifyLength = notifyLength;
        NotifyMultiplier = notifyMultiplier;
    }
}

internal static class ManagedVirtioRngProtocol
{
    internal const ushort VirtioVendorId = 0x1AF4;
    internal const ushort ModernRngDeviceId = 0x1044;
    internal const ushort TransitionalRngDeviceId = 0x1004;
    internal const ushort QemuVirtioSubsystemVendorId = 0x1AF4;
    internal const ushort QemuVirtioSubsystemDeviceId = 0x1100;
    internal const uint PciOwnerId = ((uint)VirtioVendorId << 16) | ModernRngDeviceId;
    internal const uint DriverId = 0xD026;

    internal const byte PciCapabilityVendorSpecific = 0x09;
    internal const byte VirtioPciCapCommonConfig = 1;
    internal const byte VirtioPciCapNotifyConfig = 2;
    internal const byte VirtioPciCapIsrConfig = 3;
    internal const byte VirtioPciCapDeviceConfig = 4;
    internal const byte VirtioPciCapPciConfig = 5;

    internal const byte StatusAcknowledge = 1;
    internal const byte StatusDriver = 2;
    internal const byte StatusDriverOk = 4;
    internal const byte StatusFeaturesOk = 8;
    internal const byte StatusFailed = 128;

    internal const ulong VirtqueueDescriptorWrite = 2;
    internal const uint QueueSize = 8;
    internal const uint QueuePageBytes = 8192;
    internal const uint EntropyBufferBytes = 4096;
    internal const uint MaximumRequestBytes = 1024;
    internal const uint PollLimit = 2_000_000;

    internal const ulong CommonDeviceFeatureSelect = 0x00;
    internal const ulong CommonDeviceFeature = 0x04;
    internal const ulong CommonDriverFeatureSelect = 0x08;
    internal const ulong CommonDriverFeature = 0x0C;
    internal const ulong CommonNumQueues = 0x12;
    internal const ulong CommonDeviceStatus = 0x14;
    internal const ulong CommonQueueSelect = 0x16;
    internal const ulong CommonQueueSize = 0x18;
    internal const ulong CommonQueueEnable = 0x1C;
    internal const ulong CommonQueueNotifyOffset = 0x1E;
    internal const ulong CommonQueueDescriptor = 0x20;
    internal const ulong CommonQueueAvailable = 0x28;
    internal const ulong CommonQueueUsed = 0x30;

    internal const uint DescriptorTableOffset = 0;
    internal const uint AvailableRingOffset = 128;
    internal const uint UsedRingOffset = 4096;

    internal static bool TryParseCapabilities(
        ReadOnlySpan<byte> configuration, out ManagedVirtioPciCapabilities result)
    {
        result = default;
        if (configuration.Length < 0x40) return false;
        byte pointer = configuration[0x34];
        ulong visited = 0;
        bool commonFound = false;
        bool notifyFound = false;
        byte commonBar = 0;
        byte notifyBar = 0;
        uint commonOffset = 0;
        uint commonLength = 0;
        uint notifyOffset = 0;
        uint notifyLength = 0;
        uint notifyMultiplier = 0;
        for (uint count = 0; count != 48 && pointer != 0; ++count)
        {
            if (pointer < 0x40 || pointer > 0xFC || (pointer & 3) != 0 ||
                pointer + 4 > configuration.Length) return false;
            ulong bit = 1UL << (pointer >> 2);
            if ((visited & bit) != 0) return false;
            visited |= bit;
            byte capabilityId = configuration[pointer];
            byte next = configuration[pointer + 1];
            byte capabilityLength = configuration[pointer + 2];
            if (next != 0 && (next < 0x40 || next > 0xFC || (next & 3) != 0))
                return false;
            if (capabilityId == PciCapabilityVendorSpecific)
            {
                if (capabilityLength < 16 || pointer + capabilityLength > configuration.Length)
                    return false;
                byte type = configuration[pointer + 3];
                if (type == VirtioPciCapCommonConfig)
                {
                    byte bar = configuration[pointer + 4];
                    uint offset = Read32(configuration, pointer + 8);
                    uint length = Read32(configuration, pointer + 12);
                    if (bar >= 6 || length == 0 || offset > uint.MaxValue - length)
                        return false;
                    if (commonFound) return false;
                    commonFound = true;
                    commonBar = bar;
                    commonOffset = offset;
                    commonLength = length;
                }
                else if (type == VirtioPciCapNotifyConfig)
                {
                    byte bar = configuration[pointer + 4];
                    uint offset = Read32(configuration, pointer + 8);
                    uint length = Read32(configuration, pointer + 12);
                    if (bar >= 6 || length == 0 || offset > uint.MaxValue - length)
                        return false;
                    if (notifyFound || capabilityLength < 20) return false;
                    notifyMultiplier = Read32(configuration, pointer + 16);
                    if (notifyMultiplier == 0) return false;
                    notifyFound = true;
                    notifyBar = bar;
                    notifyOffset = offset;
                    notifyLength = length;
                }
                else if (type != VirtioPciCapIsrConfig &&
                         type != VirtioPciCapDeviceConfig &&
                         type != VirtioPciCapPciConfig)
                {
                    return false;
                }
            }
            pointer = next;
        }
        if (pointer != 0 || !commonFound || !notifyFound ||
            commonLength < 0x3A || notifyLength < 4)
            return false;
        result = new ManagedVirtioPciCapabilities(
            commonBar, commonOffset, commonLength, notifyBar, notifyOffset,
            notifyLength, notifyMultiplier);
        return true;
    }

    private static uint Read32(ReadOnlySpan<byte> bytes, int offset)
    {
        return (uint)(bytes[offset] | (bytes[offset + 1] << 8) |
                      (bytes[offset + 2] << 16) | (bytes[offset + 3] << 24));
    }
}

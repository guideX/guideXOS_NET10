using System;

namespace GuideXOS.Net10.ManagedKernel;

/* Pure e1000e policy and layout helpers.  These types contain no pointers and
   are also used by the host-side Phase 14 safety tests. */
internal static class ManagedE1000Protocol
{
    internal const ushort VendorId = 0x8086;
    internal const ushort DeviceId = 0x10D3;
    internal const uint PciOwnerId = 0x808610D3;
    internal const uint ExpectedClass = 0x020000;
    internal const uint PciCommandMemorySpace = 1U << 1;
    internal const uint PciCommandBusMaster = 1U << 2;
    internal const uint PciCommandRequired =
        PciCommandMemorySpace | PciCommandBusMaster;

    internal const ulong BarLength = 0x20000;
    internal const uint DescriptorSize = 16;
    internal const uint RingCount = 16;
    internal const uint PacketBufferSize = 2048;
    internal const uint MinimumEthernetFrameLength = 60;
    internal const ushort ProofEtherType = 0x88B5;
    internal const uint TxCommandEopIfcsRs = 0x0B;
    internal const byte TxStatusDone = 0x01;
    internal const byte RxStatusDone = 0x01;
    internal const byte RxStatusEop = 0x02;
    internal const byte RxErrorMask = 0xFF;
    internal const uint RxFrameLength = MinimumEthernetFrameLength;
    internal const uint RxTestSequence = 0x15000001;
    internal const uint Phase17RxTestSequence = 0x17000001;
    internal const uint Phase18RxTestSequence = 0x18000001;
    internal const uint StatusLinkUp = 1U << 1;

    internal const ulong RegCtrl = 0x0000;
    internal const ulong RegStatus = 0x0008;
    internal const ulong RegRctl = 0x0100;
    internal const ulong RegTctl = 0x0400;
    internal const ulong RegRal = 0x5400;
    internal const ulong RegRah = 0x5404;
    internal const ulong RegRxDbaLow = 0x2800;
    internal const ulong RegRxDbaHigh = 0x2804;
    internal const ulong RegRxDescLength = 0x2808;
    internal const ulong RegRxDescHead = 0x2810;
    internal const ulong RegRxDescTail = 0x2818;
    internal const ulong RegTxDbaLow = 0x3800;
    internal const ulong RegTxDbaHigh = 0x3804;
    internal const ulong RegTxDescLength = 0x3808;
    internal const ulong RegTxDescHead = 0x3810;
    internal const ulong RegTxDescTail = 0x3818;
    internal const ulong RegTipg = 0x0410;

    internal const uint ReceiveEnable = 1U << 1;
    internal const uint ReceiveBroadcast = 1U << 15;
    internal const uint ReceiveBuffer2048 = 0U << 16;
    internal const uint ReceiveStripCrc = 1U << 26;
    internal const uint TransmitEnable = 1U << 1;
    internal const uint TransmitPadShort = 1U << 3;
    internal const uint TransmitCollisionThreshold = 0x10U << 4;
    internal const uint TransmitCollisionDistance = 0x40U << 12;

    internal static ReadOnlySpan<byte> ProofSignature =>
        "guideXOS ManagedKernel Phase14 TX"u8;

    internal static ReadOnlySpan<byte> RxPayloadSignature =>
        "guideXOS ManagedKernel Phase15 RX"u8;

    private static readonly byte[] s_rxSourceMac =
        { 0x02, 0x15, 0x00, 0x00, 0x00, 0x01 };

    internal static ReadOnlySpan<byte> RxSourceMac => s_rxSourceMac;

    internal static bool IsTarget(ushort segment, byte bus, byte device,
                                  byte function, ushort vendorId, ushort deviceId,
                                  byte classCode, byte subclass,
                                  byte programmingInterface)
    {
        return segment == 0 && bus == 0 && device == 2 && function == 0 &&
               vendorId == VendorId && deviceId == DeviceId &&
               classCode == 0x02 && subclass == 0x00 &&
               programmingInterface == 0x00;
    }

    internal static bool TryPlanPciCommand(ushort original, ushort requested,
                                            out ushort resulting)
    {
        resulting = original;
        if ((requested & ~PciCommandRequired) != 0) return false;
        resulting = (ushort)(original | requested);
        return true;
    }

    internal static bool TryAdvanceRing(uint index, uint count, out uint next)
    {
        next = 0;
        if (count == 0 || index >= count) return false;
        next = index + 1 == count ? 0U : index + 1;
        return true;
    }

    internal static bool TryAcceptRxDescriptorIndex(uint expectedIndex,
                                                      uint observedIndex,
                                                      uint ringCount,
                                                      out uint nextIndex)
    {
        nextIndex = 0;
        return expectedIndex == observedIndex &&
               TryAdvanceRing(expectedIndex, ringCount, out nextIndex);
    }

    internal static bool TryValidateRxReadyState(uint status, uint rctl,
                                                  uint rdh, uint rdt,
                                                  uint ringCount)
    {
        return ringCount != 0 &&
               (status & StatusLinkUp) != 0 &&
               (rctl & ReceiveEnable) != 0 &&
               rdh < ringCount && rdt < ringCount && rdh != rdt;
    }

    internal static bool TryValidateMmioWrite(ulong offset, uint width,
                                               ulong mappingLength, bool writable)
    {
        return writable && width == 4 && (offset & 3) == 0 &&
               offset <= ulong.MaxValue - width && offset + width <= mappingLength;
    }

    internal static bool TryValidateDmaRequest(ulong bytes, ulong alignment,
                                                ulong maximumBytes)
    {
        if (bytes == 0 || bytes > maximumBytes || alignment == 0 ||
            (alignment & (alignment - 1)) != 0) return false;
        return bytes <= ulong.MaxValue - (alignment - 1);
    }

    internal static bool TryValidateBusAddress(ulong address, ulong bytes,
                                                 ulong maximumAddress)
    {
        return address != 0 && bytes != 0 && address <= maximumAddress &&
               bytes - 1 <= maximumAddress - address;
    }

    internal static bool TryBuildTxDescriptor(Span<byte> descriptor,
                                               ulong busAddress, ushort length)
    {
        if (descriptor.Length != DescriptorSize || busAddress == 0 || length == 0 ||
            length > PacketBufferSize) return false;
        descriptor.Clear();
        for (int index = 0; index != 8; ++index)
            descriptor[index] = (byte)(busAddress >> (index * 8));
        descriptor[8] = (byte)length;
        descriptor[9] = (byte)(length >> 8);
        descriptor[11] = (byte)TxCommandEopIfcsRs;
        return true;
    }

    internal static bool TryPrepareRxDescriptor(Span<byte> descriptor,
                                                 ulong busAddress)
    {
        if (descriptor.Length != DescriptorSize || busAddress == 0) return false;
        descriptor.Clear();
        for (int index = 0; index != 8; ++index)
            descriptor[index] = (byte)(busAddress >> (index * 8));
        return true;
    }

    internal static bool TryBuildRxDescriptor(Span<byte> descriptor,
                                               ulong busAddress, ushort length,
                                               byte status, byte errors)
    {
        if (!TryPrepareRxDescriptor(descriptor, busAddress) || length == 0 ||
            length > PacketBufferSize) return false;
        descriptor[8] = (byte)length;
        descriptor[9] = (byte)(length >> 8);
        descriptor[12] = status;
        descriptor[13] = errors;
        return true;
    }

    internal static bool TryReadRxDescriptor(ReadOnlySpan<byte> descriptor,
                                              uint descriptorIndex,
                                              uint ringCount,
                                              uint bufferCapacity,
                                              out ushort length,
                                              out byte status,
                                              out byte errors)
    {
        length = 0;
        status = 0;
        errors = 0;
        if (descriptor.Length != DescriptorSize || ringCount == 0 ||
            descriptorIndex >= ringCount || bufferCapacity == 0 ||
            bufferCapacity > ushort.MaxValue) return false;
        length = (ushort)(descriptor[8] | (descriptor[9] << 8));
        status = descriptor[12];
        errors = descriptor[13];
        return (status & (RxStatusDone | RxStatusEop)) ==
                   (RxStatusDone | RxStatusEop) &&
               (errors & RxErrorMask) == 0 && length != 0 &&
               length <= bufferCapacity;
    }

    internal static bool IsDescriptorComplete(ReadOnlySpan<byte> descriptor)
    {
        return descriptor.Length == DescriptorSize &&
               (descriptor[12] & TxStatusDone) != 0;
    }

    internal static bool TryBuildProofFrame(byte[] frame, ReadOnlySpan<byte> source)
    {
        if (frame == null || frame.Length < MinimumEthernetFrameLength ||
            source.Length != 6 || IsInvalidMac(source)) return false;
        Array.Clear(frame, 0, frame.Length);
        for (int index = 0; index != 6; ++index)
        {
            frame[index] = 0xFF;
            frame[index + 6] = source[index];
        }
        frame[12] = (byte)(ProofEtherType >> 8);
        frame[13] = (byte)(ProofEtherType & 0xFF);
        ProofSignature.CopyTo(frame.AsSpan(14));
        return true;
    }

    internal static bool TryBuildRxTestFrame(Span<byte> frame,
                                              ReadOnlySpan<byte> destination)
    {
        if (frame.Length != RxFrameLength || destination.Length != 6 ||
            IsInvalidMac(destination)) return false;
        frame.Clear();
        destination.CopyTo(frame.Slice(0, 6));
        RxSourceMac.CopyTo(frame.Slice(6, 6));
        frame[12] = (byte)(ProofEtherType >> 8);
        frame[13] = (byte)(ProofEtherType & 0xFF);
        RxPayloadSignature.CopyTo(frame.Slice(14));
        int sequenceOffset = 14 + RxPayloadSignature.Length;
        frame[sequenceOffset] = unchecked((byte)(RxTestSequence >> 24));
        frame[sequenceOffset + 1] = unchecked((byte)(RxTestSequence >> 16));
        frame[sequenceOffset + 2] = unchecked((byte)(RxTestSequence >> 8));
        frame[sequenceOffset + 3] = unchecked((byte)RxTestSequence);
        return true;
    }

    internal static bool TryValidateRxTestFrame(ReadOnlySpan<byte> frame,
                                                 ReadOnlySpan<byte> destination)
    {
        if (frame.Length != RxFrameLength || destination.Length != 6 ||
            IsInvalidMac(destination) ||
            !frame.Slice(0, 6).SequenceEqual(destination) ||
            !frame.Slice(6, 6).SequenceEqual(RxSourceMac) ||
            frame[12] != (byte)(ProofEtherType >> 8) ||
            frame[13] != (byte)(ProofEtherType & 0xFF) ||
            !frame.Slice(14, RxPayloadSignature.Length)
                .SequenceEqual(RxPayloadSignature)) return false;
        int sequenceOffset = 14 + RxPayloadSignature.Length;
        uint sequence = ((uint)frame[sequenceOffset] << 24) |
                        ((uint)frame[sequenceOffset + 1] << 16) |
                        ((uint)frame[sequenceOffset + 2] << 8) |
                        frame[sequenceOffset + 3];
        if (sequence != RxTestSequence && sequence != Phase17RxTestSequence &&
            sequence != Phase18RxTestSequence)
            return false;
        for (int index = sequenceOffset + 4; index != frame.Length; ++index)
            if (frame[index] != 0) return false;
        return true;
    }

    internal static bool IsPhase17RxTestFrame(ReadOnlySpan<byte> frame)
    {
        if (frame.Length != RxFrameLength ||
            !frame.Slice(14, RxPayloadSignature.Length)
                .SequenceEqual(RxPayloadSignature)) return false;
        int sequenceOffset = 14 + RxPayloadSignature.Length;
        uint sequence = ((uint)frame[sequenceOffset] << 24) |
                        ((uint)frame[sequenceOffset + 1] << 16) |
                        ((uint)frame[sequenceOffset + 2] << 8) |
                        frame[sequenceOffset + 3];
        return sequence == Phase17RxTestSequence;
    }

    internal static bool IsPhase18RxTestFrame(ReadOnlySpan<byte> frame)
    {
        if (frame.Length != RxFrameLength ||
            !frame.Slice(14, RxPayloadSignature.Length)
                .SequenceEqual(RxPayloadSignature)) return false;
        int sequenceOffset = 14 + RxPayloadSignature.Length;
        uint sequence = ((uint)frame[sequenceOffset] << 24) |
                        ((uint)frame[sequenceOffset + 1] << 16) |
                        ((uint)frame[sequenceOffset + 2] << 8) |
                        frame[sequenceOffset + 3];
        return sequence == Phase18RxTestSequence;
    }

    internal static bool TryValidateFrame(ReadOnlySpan<byte> frame,
                                           ReadOnlySpan<byte> expectedSource)
    {
        if (frame.Length < MinimumEthernetFrameLength || expectedSource.Length != 6 ||
            IsInvalidMac(expectedSource) || frame[12] != (byte)(ProofEtherType >> 8) ||
            frame[13] != (byte)(ProofEtherType & 0xFF)) return false;
        for (int index = 0; index != 6; ++index)
        {
            if (frame[index] != 0xFF || frame[index + 6] != expectedSource[index])
                return false;
        }
        return frame.Slice(14, ProofSignature.Length).SequenceEqual(ProofSignature);
    }

    internal static bool IsInvalidMac(ReadOnlySpan<byte> mac)
    {
        if (mac.Length != 6) return true;
        bool allZero = true;
        bool allOnes = true;
        for (int index = 0; index != 6; ++index)
        {
            allZero &= mac[index] == 0;
            allOnes &= mac[index] == 0xFF;
        }
        return allZero || allOnes;
    }
}

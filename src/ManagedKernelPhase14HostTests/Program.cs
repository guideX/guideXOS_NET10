using System;
using GuideXOS.Net10.ManagedKernel;

internal static class Program
{
    private static int Main()
    {
        Check(ManagedE1000Protocol.IsTarget(0, 0, 2, 0, 0x8086, 0x10D3,
            0x02, 0x00, 0x00), "target-device-validation");
        Check(!ManagedE1000Protocol.IsTarget(0, 0, 2, 0, 0x1234, 0x10D3,
            0x02, 0x00, 0x00), "wrong-vendor-rejected");

        Check(ManagedE1000Protocol.TryPlanPciCommand(0xA0, 0x06, out ushort command) &&
              command == 0xA6, "pci-command-read-modify-write");
        Check(!ManagedE1000Protocol.TryPlanPciCommand(0xA0, 0x08, out _),
            "pci-command-unsupported-bit-rejected");

        Check(ManagedE1000Protocol.TryAdvanceRing(7, 8, out uint wrapped) && wrapped == 0,
            "ring-wraparound");
        Check(!ManagedE1000Protocol.TryAdvanceRing(8, 8, out _),
            "ring-index-out-of-range-rejected");
        Check(ManagedE1000Protocol.TryBuildAsyncTxRequestId(3, 7, 16,
                  out ulong requestId) &&
              ManagedE1000Protocol.TryDecodeAsyncTxRequestId(requestId, 16,
                  out uint requestIndex, out uint requestGeneration) &&
              requestIndex == 3 && requestGeneration == 7,
            "async-request-index-and-generation-round-trip");
        Check(ManagedE1000Protocol.TryBuildAsyncTxRequestId(3, 8, 16,
                  out ulong reusedRequestId) && requestId != reusedRequestId &&
              ManagedE1000Protocol.TryDecodeAsyncTxRequestId(reusedRequestId, 16,
                  out requestIndex, out requestGeneration) &&
              requestIndex == 3 && requestGeneration == 8,
            "async-request-same-slot-reuse-gets-new-generation");
        Check(!ManagedE1000Protocol.TryBuildAsyncTxRequestId(16, 1, 16, out _) &&
              !ManagedE1000Protocol.TryBuildAsyncTxRequestId(0, 0, 16, out _) &&
              !ManagedE1000Protocol.TryDecodeAsyncTxRequestId(0, 16, out _, out _),
            "async-request-invalid-index-and-generation-rejected");

        Check(ManagedE1000Protocol.TryValidateMmioWrite(0x1C, 4, 0x20, true),
            "mmio-write-in-bounds");
        Check(!ManagedE1000Protocol.TryValidateMmioWrite(0x1E, 4, 0x20, true),
            "mmio-misalignment-rejected");
        Check(!ManagedE1000Protocol.TryValidateMmioWrite(0x20, 4, 0x20, true),
            "mmio-end-crossing-rejected");
        Check(!ManagedE1000Protocol.TryValidateMmioWrite(0, 4, 0x20, false),
            "mmio-readonly-write-rejected");

        Check(ManagedE1000Protocol.TryValidateDmaRequest(4096, 4096, 131072),
            "dma-request-valid");
        Check(!ManagedE1000Protocol.TryValidateDmaRequest(0, 4096, 131072),
            "dma-zero-length-rejected");
        Check(!ManagedE1000Protocol.TryValidateDmaRequest(4096, 3000, 131072),
            "dma-invalid-alignment-rejected");
        Check(!ManagedE1000Protocol.TryValidateDmaRequest(132000, 4096, 131072),
            "dma-oversized-request-rejected");
        Check(ManagedE1000Protocol.TryValidateBusAddress(0x1000, 0x1000,
                                                         0xFFFFFFFF),
            "dma-bus-address-valid");
        Check(!ManagedE1000Protocol.TryValidateBusAddress(0xFFFFFFF0, 0x20,
                                                          0xFFFFFFFF),
            "dma-bus-address-overflow-rejected");

        byte[] mac = { 0x52, 0x54, 0x00, 0x12, 0x34, 0x56 };
        byte[] frame = new byte[60];
        Check(!ManagedE1000Protocol.IsInvalidMac(mac), "mac-valid");
        Check(ManagedE1000Protocol.IsInvalidMac(new byte[6]), "zero-mac-rejected");
        Check(ManagedE1000Protocol.IsInvalidMac(new byte[]
            { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF }), "broadcast-mac-rejected");
        Check(ManagedE1000Protocol.TryBuildProofFrame(frame, mac) &&
              ManagedE1000Protocol.TryValidateFrame(frame, mac),
            "proof-frame-construction-and-validation");
        frame[14] ^= 1;
        Check(!ManagedE1000Protocol.TryValidateFrame(frame, mac),
            "proof-payload-mismatch-rejected");

        byte[] descriptor = new byte[(int)ManagedE1000Protocol.DescriptorSize];
        Check(ManagedE1000Protocol.TryBuildTxDescriptor(descriptor, 0x12345000, 60) &&
              descriptor[8] == 60 && descriptor[11] == 0x0B &&
              !ManagedE1000Protocol.IsDescriptorComplete(descriptor),
            "tx-descriptor-owned-by-device");
        descriptor[12] = ManagedE1000Protocol.TxStatusDone;
        Check(ManagedE1000Protocol.IsDescriptorComplete(descriptor),
            "tx-descriptor-completion");
        Check(!ManagedE1000Protocol.TryBuildTxDescriptor(descriptor, 0, 60),
            "tx-descriptor-zero-bus-rejected");

        Check(!ManagedE1000Protocol.TryClassifyInterruptCause(0, out uint noCause) &&
              noCause == 0, "e1000-txdw-cause-required");
        Check(ManagedE1000Protocol.TryClassifyInterruptCause(
                  ManagedE1000Protocol.InterruptCauseTxDescriptorWriteback,
                  out uint noUnrelated) && noUnrelated == 0,
            "e1000-txdw-cause-classified");
        Check(ManagedE1000Protocol.TryClassifyInterruptCause(
                  ManagedE1000Protocol.InterruptCauseTxDescriptorWriteback | 0x20,
                  out uint unrelated) && unrelated == 0x20,
            "e1000-unrelated-cause-preserved");
        Check(ManagedE1000Protocol.TryClassifyInterruptCause(
                  ManagedE1000Protocol.InterruptCauseRxTimer,
                  out uint rxUnrelated) && rxUnrelated == 0,
            "e1000-rxt0-cause-classified");
        Check(ManagedE1000Protocol.TryClassifyInterruptCause(
                  ManagedE1000Protocol.InterruptCauseTxDescriptorWriteback |
                  ManagedE1000Protocol.InterruptCauseRxTimer | 0x20,
                  out uint combinedUnrelated) && combinedUnrelated == 0x20,
            "e1000-simultaneous-txdw-rxt0-causes-preserved");

        GxManagedKernelInterruptEventV1 e1000Event = new()
        {
            Size = GxManagedKernelInterruptEventV1.ExpectedSize,
            AbiVersion = GxManagedKernelInterruptEventV1.AbiVersionCurrent,
            EventType = ManagedE1000Protocol.InterruptEventType,
            DeviceKind = ManagedE1000Protocol.PciDeviceKind,
            DeviceId = ManagedE1000Protocol.PciOwnerId,
            Sequence = 7,
            Flags = GxManagedKernelInterruptEventV1.EventFlagHardwareCapture,
            PayloadByte = 1,
            PayloadLength = 1,
            Status = ManagedE1000Protocol.InterruptCauseTxDescriptorWriteback
        };
        Check(ManagedE1000Protocol.TryValidateInterruptEvent(
                  in e1000Event, 6, out _), "e1000-bound-event-accepted");
        Check(!ManagedE1000Protocol.TryValidateInterruptEvent(
                  in e1000Event, 7, out _), "e1000-stale-sequence-rejected");
        e1000Event.DeviceId++;
        Check(!ManagedE1000Protocol.TryValidateInterruptEvent(
                  in e1000Event, 6, out _), "e1000-wrong-device-rejected");
        e1000Event.DeviceId = ManagedE1000Protocol.PciOwnerId;
        e1000Event.Status = 0;
        Check(!ManagedE1000Protocol.TryValidateInterruptEvent(
                  in e1000Event, 6, out _), "e1000-unbound-or-empty-cause-rejected");
        e1000Event.Status = ManagedE1000Protocol.InterruptCauseRxTimer | 0x20;
        e1000Event.PayloadByte = 0xA0;
        Check(ManagedE1000Protocol.TryValidateInterruptEvent(
                  in e1000Event, 6, out uint rxEventUnrelated) &&
              rxEventUnrelated == 0x20,
            "e1000-rxt0-typed-event-preserves-unrelated-cause");
        e1000Event.Status = ManagedE1000Protocol.InterruptCauseRxTimer |
                            ManagedE1000Protocol.InterruptCauseTxDescriptorWriteback;
        e1000Event.PayloadByte = 0x81;
        Check(ManagedE1000Protocol.TryValidateInterruptEvent(
                  in e1000Event, 6, out _),
            "e1000-typed-event-retains-simultaneous-rx-and-tx-cause-bits");
        e1000Event.Status = ManagedE1000Protocol.InterruptCauseRxTimer;
        e1000Event.PayloadByte = 0x80;
        e1000Event.Sequence = 8;
        Check(!ManagedE1000Protocol.TryValidateInterruptEvent(
                  in e1000Event, 8, out _),
            "e1000-duplicate-or-stale-rx-sequence-rejected");

        byte[] rxDescriptor = new byte[(int)ManagedE1000Protocol.DescriptorSize];
        Check(ManagedE1000Protocol.TryBuildRxDescriptor(
                  rxDescriptor, 0x12346000, 60,
                  ManagedE1000Protocol.RxStatusDone |
                  ManagedE1000Protocol.RxStatusEop, 0) &&
              ManagedE1000Protocol.TryReadRxDescriptor(
                  rxDescriptor, 0, ManagedE1000Protocol.RingCount,
                  ManagedE1000Protocol.PacketBufferSize, out ushort rxLength,
                  out byte rxStatus, out byte rxErrors) &&
              rxLength == 60 && rxStatus == 0x03 && rxErrors == 0,
            "e1000-rx-dd-eop-valid-length-and-errors-accepted");
        rxDescriptor[12] = ManagedE1000Protocol.RxStatusEop;
        Check(!ManagedE1000Protocol.TryReadRxDescriptor(
                  rxDescriptor, 0, ManagedE1000Protocol.RingCount,
                  ManagedE1000Protocol.PacketBufferSize, out _, out _, out _),
            "e1000-rx-empty-descriptor-does-not-deliver");
        rxDescriptor[12] = ManagedE1000Protocol.RxStatusDone;
        Check(!ManagedE1000Protocol.TryReadRxDescriptor(
                  rxDescriptor, 0, ManagedE1000Protocol.RingCount,
                  ManagedE1000Protocol.PacketBufferSize, out _, out _, out _),
            "e1000-rx-dd-without-eop-rejected");
        rxDescriptor[12] = ManagedE1000Protocol.RxStatusDone |
                           ManagedE1000Protocol.RxStatusEop;
        rxDescriptor[13] = 1;
        Check(!ManagedE1000Protocol.TryReadRxDescriptor(
                  rxDescriptor, 0, ManagedE1000Protocol.RingCount,
                  ManagedE1000Protocol.PacketBufferSize, out _, out _, out _),
            "e1000-rx-fatal-error-bits-rejected");
        rxDescriptor[13] = 0;
        rxDescriptor[8] = 13;
        rxDescriptor[9] = 0;
        Check(!ManagedE1000Protocol.TryReadRxDescriptor(
                  rxDescriptor, 0, ManagedE1000Protocol.RingCount,
                  ManagedE1000Protocol.PacketBufferSize, out _, out _, out _),
            "e1000-rx-short-reported-length-rejected");
        rxDescriptor[8] = (byte)ManagedE1000Protocol.MinimumRxEthernetHeaderLength;
        Check(ManagedE1000Protocol.TryReadRxDescriptor(
                  rxDescriptor, 0, ManagedE1000Protocol.RingCount,
                  ManagedE1000Protocol.PacketBufferSize,
                  out ushort headerOnlyLength, out _, out _) &&
              headerOnlyLength == ManagedE1000Protocol.MinimumRxEthernetHeaderLength,
            "e1000-rx-exact-ethernet-header-length-bounded");
        rxDescriptor[8] = 0;
        rxDescriptor[9] = (byte)(ManagedE1000Protocol.PacketBufferSize >> 8);
        Check(ManagedE1000Protocol.TryReadRxDescriptor(
                  rxDescriptor, 0, ManagedE1000Protocol.RingCount,
                  ManagedE1000Protocol.PacketBufferSize,
                  out ushort maximumRxLength, out _, out _) &&
              maximumRxLength == ManagedE1000Protocol.PacketBufferSize,
            "e1000-rx-exact-owned-buffer-capacity-accepted");
        rxDescriptor[8] = 0x01;
        rxDescriptor[9] = 0x08;
        Check(!ManagedE1000Protocol.TryReadRxDescriptor(
                  rxDescriptor, 0, ManagedE1000Protocol.RingCount,
                  ManagedE1000Protocol.PacketBufferSize, out _, out _, out _),
            "e1000-rx-length-over-buffer-capacity-rejected");

        byte[] phase83Frame = new byte[60];
        Check(ManagedE1000Protocol.TryBuildPhase83RxTestFrame(
                  phase83Frame, mac, 1) &&
              ManagedE1000Protocol.TryValidatePhase83RxTestFrame(
                  phase83Frame, mac, 1),
            "phase83-rx-frame-has-bounded-expected-fields");
        Check(!ManagedE1000Protocol.TryValidatePhase83RxTestFrame(
                  phase83Frame, mac, 2),
            "phase83-rx-frame-sequence-sentinel-validated");
        phase83Frame[0] ^= 1;
        Check(!ManagedE1000Protocol.TryValidatePhase83RxTestFrame(
                  phase83Frame, mac, 1),
            "phase83-wrong-destination-mac-rejected");
        phase83Frame[0] ^= 1;
        phase83Frame[6] ^= 1;
        Check(!ManagedE1000Protocol.TryValidatePhase83RxTestFrame(
                  phase83Frame, mac, 1),
            "phase83-wrong-source-mac-rejected");
        phase83Frame[6] ^= 1;
        phase83Frame[13] ^= 1;
        Check(!ManagedE1000Protocol.TryValidatePhase83RxTestFrame(
                  phase83Frame, mac, 1),
            "phase83-wrong-ethertype-rejected");
        phase83Frame[13] ^= 1;
        phase83Frame[14] ^= 1;
        Check(!ManagedE1000Protocol.TryValidatePhase83RxTestFrame(
                  phase83Frame, mac, 1),
            "phase83-payload-sentinel-rejected");

        Check(ManagedE1000Protocol.TryPrepareRxDescriptor(
                  rxDescriptor, 0x12346000) &&
              (rxDescriptor[12] & ManagedE1000Protocol.RxStatusDone) == 0 &&
              rxDescriptor[8] == 0 && rxDescriptor[13] == 0 &&
              !ManagedE1000Protocol.TryReadRxDescriptor(
                  rxDescriptor, 0, ManagedE1000Protocol.RingCount,
                  ManagedE1000Protocol.PacketBufferSize, out _, out _, out _),
            "e1000-rx-recycle-clears-completion-and-prevents-duplicate-delivery");
        Check(ManagedE1000Protocol.TryAcceptRxDescriptorIndex(
                  15, 15, ManagedE1000Protocol.RingCount, out uint wrappedRx) &&
              wrappedRx == 0 &&
              ManagedE1000Protocol.TryAcceptRxDescriptorIndex(
                  0, 0, ManagedE1000Protocol.RingCount, out uint nextRx) &&
              nextRx == 1,
            "e1000-rx-ring-wrap-and-recycle-index-logic");

        byte[] phase80Frame = new byte[60];
        byte[] phase80Repeat = new byte[60];
        Check(ManagedE1000Protocol.TryBuildPhase80Frame(
                  phase80Frame, mac, 1) &&
              ManagedE1000Protocol.TryBuildPhase80Frame(
                  phase80Repeat, mac, 1) &&
              phase80Frame.AsSpan().SequenceEqual(phase80Repeat) &&
              phase80Frame[0] == 0xFF && phase80Frame[6] == mac[0] &&
              phase80Frame[12] == 0x88 && phase80Frame[13] == 0xB5,
            "phase80-frame-is-bounded-local-and-deterministic");
        Check(!ManagedE1000Protocol.TryBuildPhase80Frame(
                  phase80Frame, mac, 0), "phase80-zero-sequence-rejected");

        byte[] phase90Frame = new byte[60];
        Check(ManagedE1000Protocol.TryBuildPhase90Frame(
                  phase90Frame, mac, 0x12345678) &&
              phase90Frame[0] == 0xFF && phase90Frame[6] == mac[0] &&
              phase90Frame[12] == 0x88 && phase90Frame[13] == 0xB5 &&
              phase90Frame[30] == 0x12 && phase90Frame[31] == 0x34 &&
              phase90Frame[32] == 0x56 && phase90Frame[33] == 0x78,
            "phase90-transmit-frame-contains-mac-ethertype-and-sequence");
        Check(!ManagedE1000Protocol.TryBuildPhase90Frame(
                  phase90Frame, mac, 0),
            "phase90-zero-sequence-rejected");

        Console.WriteLine("MANAGED_KERNEL_PHASE14_HOST_TESTS_PASS");
        return 0;
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("FAILED: " + name);
        Console.WriteLine("PASS: " + name);
    }
}

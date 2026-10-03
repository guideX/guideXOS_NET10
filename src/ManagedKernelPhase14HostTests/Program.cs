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

        Console.WriteLine("MANAGED_KERNEL_PHASE14_HOST_TESTS_PASS");
        return 0;
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("FAILED: " + name);
        Console.WriteLine("PASS: " + name);
    }
}

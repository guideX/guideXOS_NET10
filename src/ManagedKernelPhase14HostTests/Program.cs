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

        Console.WriteLine("MANAGED_KERNEL_PHASE14_HOST_TESTS_PASS");
        return 0;
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("FAILED: " + name);
        Console.WriteLine("PASS: " + name);
    }
}

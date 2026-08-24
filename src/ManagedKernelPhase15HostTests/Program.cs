using System;
using GuideXOS.Net10.ManagedKernel;

internal static class Program
{
    private static int s_cases;

    private static int Main()
    {
        byte[] destination = { 0x52, 0x54, 0x00, 0x12, 0x34, 0x56 };
        byte[] frame = new byte[(int)ManagedE1000Protocol.RxFrameLength];
        Check(ManagedE1000Protocol.TryBuildRxTestFrame(frame, destination) &&
              ManagedE1000Protocol.TryValidateRxTestFrame(frame, destination),
            "valid-phase15-frame");

        byte[] descriptor = new byte[(int)ManagedE1000Protocol.DescriptorSize];
        Check(ManagedE1000Protocol.TryBuildRxDescriptor(
                  descriptor, 0x12345000, (ushort)ManagedE1000Protocol.RxFrameLength,
                  ManagedE1000Protocol.RxStatusDone | ManagedE1000Protocol.RxStatusEop,
                  0) &&
              ManagedE1000Protocol.TryReadRxDescriptor(
                  descriptor, 0, ManagedE1000Protocol.RingCount,
                  ManagedE1000Protocol.PacketBufferSize, out ushort length,
                  out byte status, out byte errors) &&
              length == ManagedE1000Protocol.RxFrameLength &&
              status == (ManagedE1000Protocol.RxStatusDone |
                         ManagedE1000Protocol.RxStatusEop) && errors == 0,
            "completed-rx-descriptor-valid");
        descriptor[12] = ManagedE1000Protocol.RxStatusEop;
        Check(!ManagedE1000Protocol.TryReadRxDescriptor(
                  descriptor, 0, ManagedE1000Protocol.RingCount,
                  ManagedE1000Protocol.PacketBufferSize, out _, out _, out _),
            "rx-descriptor-without-dd-rejected");
        descriptor[12] = ManagedE1000Protocol.RxStatusDone;
        Check(!ManagedE1000Protocol.TryReadRxDescriptor(
                  descriptor, 0, ManagedE1000Protocol.RingCount,
                  ManagedE1000Protocol.PacketBufferSize, out _, out _, out _),
            "rx-descriptor-without-eop-rejected");
        descriptor[12] = ManagedE1000Protocol.RxStatusDone |
                         ManagedE1000Protocol.RxStatusEop;
        descriptor[8] = 0;
        descriptor[9] = 0;
        Check(!ManagedE1000Protocol.TryReadRxDescriptor(
                  descriptor, 0, ManagedE1000Protocol.RingCount,
                  ManagedE1000Protocol.PacketBufferSize, out _, out _, out _),
            "rx-descriptor-zero-length-rejected");
        descriptor[8] = 0x01;
        descriptor[9] = 0x08;
        Check(!ManagedE1000Protocol.TryReadRxDescriptor(
                  descriptor, 0, ManagedE1000Protocol.RingCount,
                  ManagedE1000Protocol.PacketBufferSize, out _, out _, out _),
            "rx-descriptor-over-capacity-rejected");
        descriptor[8] = (byte)ManagedE1000Protocol.RxFrameLength;
        descriptor[9] = 0;
        descriptor[13] = 1;
        Check(!ManagedE1000Protocol.TryReadRxDescriptor(
                  descriptor, 0, ManagedE1000Protocol.RingCount,
                  ManagedE1000Protocol.PacketBufferSize, out _, out _, out _),
            "rx-descriptor-error-bit-rejected");
        Check(!ManagedE1000Protocol.TryReadRxDescriptor(
                  descriptor, ManagedE1000Protocol.RingCount,
                  ManagedE1000Protocol.RingCount, ManagedE1000Protocol.PacketBufferSize,
                  out _, out _, out _), "rx-descriptor-index-bounds-rejected");

        Check(ManagedE1000Protocol.TryAcceptRxDescriptorIndex(
                  7, 7, ManagedE1000Protocol.RingCount, out uint wrapped) && wrapped == 0,
            "rx-ring-wraparound");
        Check(!ManagedE1000Protocol.TryAcceptRxDescriptorIndex(
                  2, 3, ManagedE1000Protocol.RingCount, out _),
            "duplicate-or-out-of-order-rx-completion-rejected");
        Check(ManagedE1000Protocol.TryPrepareRxDescriptor(
                  descriptor, 0x12345000) && descriptor[12] == 0 && descriptor[13] == 0 &&
              descriptor[8] == 0 && descriptor[9] == 0,
            "rx-descriptor-recycling-clears-hardware-status");
        Check(!ManagedE1000Protocol.TryPrepareRxDescriptor(descriptor, 0),
            "rx-descriptor-zero-bus-rejected");

        byte[] wrongDestination = (byte[])frame.Clone();
        wrongDestination[0] ^= 1;
        Check(!ManagedE1000Protocol.TryValidateRxTestFrame(wrongDestination, destination),
            "wrong-destination-rejected");
        byte[] wrongSource = (byte[])frame.Clone();
        wrongSource[6] ^= 1;
        Check(!ManagedE1000Protocol.TryValidateRxTestFrame(wrongSource, destination),
            "wrong-source-rejected");
        byte[] wrongType = (byte[])frame.Clone();
        wrongType[13] ^= 1;
        Check(!ManagedE1000Protocol.TryValidateRxTestFrame(wrongType, destination),
            "wrong-ethertype-rejected");
        byte[] wrongPayload = (byte[])frame.Clone();
        wrongPayload[14] ^= 1;
        Check(!ManagedE1000Protocol.TryValidateRxTestFrame(wrongPayload, destination),
            "corrupt-payload-rejected");
        Check(!ManagedE1000Protocol.TryValidateRxTestFrame(
                  frame.AsSpan(0, frame.Length - 1), destination),
            "unexpected-frame-length-rejected");

        Check(ManagedE1000Protocol.TryValidateDmaRequest(4096, 4096, 131072),
            "dma-capability-request-valid");
        Check(!ManagedE1000Protocol.TryValidateDmaRequest(0, 4096, 131072),
            "dma-capability-zero-length-rejected");
        Check(!ManagedE1000Protocol.TryValidateBusAddress(0xFFFFFFF0, 0x20,
                                                           0xFFFFFFFF),
            "dma-capability-overflow-rejected");
        Check(ManagedE1000Protocol.TryValidateBusAddress(0x1000, 0x1000,
                                                          0xFFFFFFFF),
            "dma-capability-bus-range-valid");

        Console.WriteLine($"MANAGED_KERNEL_PHASE15_HOST_TESTS_PASS cases={s_cases}");
        return 0;
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("FAILED: " + name);
        ++s_cases;
        Console.WriteLine("PASS: " + name);
    }
}

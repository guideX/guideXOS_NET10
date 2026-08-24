using System;
using GuideXOS.Net10.ManagedKernel;

internal static class Program
{
    private static readonly byte[] s_localIp = { 10, 15, 0, 1 };
    private static readonly byte[] s_peerIp = { 10, 15, 0, 2 };
    private static int s_cases;

    private static int Main()
    {
        UdpConstructionTests();
        UdpParsingTests();
        UdpChecksumTests();
        EndpointTests();
        Console.WriteLine($"MANAGED_KERNEL_PHASE18_HOST_TESTS_PASS cases={s_cases}");
        return 0;
    }

    private static void UdpConstructionTests()
    {
        byte[] even = new byte[ManagedUdpProtocol.MaximumDatagramLength];
        Check(ManagedUdpProtocol.TryBuild(
                  even, 0x1234, 0x5678, s_localIp, s_peerIp,
                  new byte[] { 1, 2, 3, 4 }, out ushort evenLength) &&
              evenLength == 12,
              "udp-even-construction-length");
        Check(even.AsSpan(0, evenLength).SequenceEqual(new byte[]
        {
            0x12, 0x34, 0x56, 0x78, 0x00, 0x0C, 0x7F, 0x03,
            0x01, 0x02, 0x03, 0x04
        }), "udp-even-exact-wire-bytes");

        byte[] odd = new byte[ManagedUdpProtocol.MaximumDatagramLength];
        Check(ManagedUdpProtocol.TryBuild(
                  odd, 0x1234, 0x5678, s_localIp, s_peerIp,
                  new byte[] { 1, 2, 3 }, out ushort oddLength) &&
              oddLength == 11 && odd[6] == 0x7F && odd[7] == 0x09,
              "udp-odd-construction-and-checksum");

        byte[] zero = new byte[ManagedUdpProtocol.MaximumDatagramLength];
        Check(ManagedUdpProtocol.TryBuild(
                  zero, 0x1234, 0x5678, s_localIp, s_peerIp,
                  Array.Empty<byte>(), out ushort zeroLength) &&
              zeroLength == ManagedUdpProtocol.HeaderLength &&
              zero[6] == 0x83 && zero[7] == 0x11,
              "udp-zero-payload-construction");
        Check(even[12] == 0 && odd[12] == 0 && zero[8] == 0,
              "udp-construction-does-not-overrun");

        Check(!ManagedUdpProtocol.TryBuild(
                  even, 0, 1, s_localIp, s_peerIp, Array.Empty<byte>(), out _),
              "udp-source-port-zero-rejected");
        Check(!ManagedUdpProtocol.TryBuild(
                  even, 1, 0, s_localIp, s_peerIp, Array.Empty<byte>(), out _),
              "udp-destination-port-zero-rejected");
        Check(!ManagedUdpProtocol.TryBuild(
                  new byte[ManagedUdpProtocol.MaximumDatagramLength - 1],
                  1, 2, s_localIp, s_peerIp, new byte[512], out _),
              "udp-short-output-buffer-rejected");
        Check(!ManagedUdpProtocol.TryBuild(
                  even, 1, 2, s_localIp, s_peerIp, new byte[513], out _),
              "udp-payload-above-maximum-rejected");
    }

    private static void UdpParsingTests()
    {
        byte[] packet = BuildUdp(0x1111, 0x2222, new byte[] { 9, 8, 7 });
        Check(ManagedUdpProtocol.TryParse(packet, s_localIp, s_peerIp,
                  out ManagedUdpDatagram parsed) &&
              parsed.SourcePort == 0x1111 && parsed.DestinationPort == 0x2222 &&
              parsed.Length == 11 && parsed.Checksum != 0 &&
              parsed.Payload.SequenceEqual(new byte[] { 9, 8, 7 }),
              "udp-valid-odd-packet");

        byte[] headerOnly = BuildUdp(1, 2, Array.Empty<byte>());
        Check(ManagedUdpProtocol.TryParse(headerOnly, s_localIp, s_peerIp,
                  out ManagedUdpDatagram headerView) &&
              headerView.Length == 8 && headerView.Payload.Length == 0,
              "udp-exactly-eight-byte-datagram");
        Check(!ManagedUdpProtocol.TryParse(packet.AsSpan(0, 7), s_localIp,
                                            s_peerIp, out _),
              "udp-truncated-header-rejected");

        byte[] lengthZero = (byte[])headerOnly.Clone();
        lengthZero[4] = 0;
        lengthZero[5] = 0;
        Check(!ManagedUdpProtocol.TryParse(lengthZero, s_localIp, s_peerIp, out _),
              "udp-length-zero-rejected");
        byte[] lengthShort = (byte[])headerOnly.Clone();
        lengthShort[4] = 0;
        lengthShort[5] = 7;
        Check(!ManagedUdpProtocol.TryParse(lengthShort, s_localIp, s_peerIp, out _),
              "udp-length-less-than-header-rejected");
        byte[] lengthLong = (byte[])packet.Clone();
        lengthLong[4] = 0;
        lengthLong[5] = 40;
        Check(!ManagedUdpProtocol.TryParse(lengthLong, s_localIp, s_peerIp, out _),
              "udp-length-beyond-ipv4-payload-rejected");

        byte[] trailing = new byte[packet.Length + 4];
        packet.CopyTo(trailing, 0);
        trailing[^1] = 0xEE;
        Check(ManagedUdpProtocol.TryParse(trailing, s_localIp, s_peerIp,
                  out ManagedUdpDatagram trailingView) &&
              trailingView.Length == packet.Length &&
              trailingView.Payload.SequenceEqual(new byte[] { 9, 8, 7 }),
              "udp-trailing-ipv4-payload-ignored");

        byte[] maximum = BuildUdp(1, 2, new byte[ManagedUdpProtocol.MaximumPayloadLength]);
        Check(ManagedUdpProtocol.TryParse(maximum, s_localIp, s_peerIp,
                  out ManagedUdpDatagram maximumView) &&
              maximumView.Payload.Length == ManagedUdpProtocol.MaximumPayloadLength,
              "udp-maximum-payload-accepted");
        byte[] aboveMaximum = BuildUdpUnchecked(1, 2,
            new byte[ManagedUdpProtocol.MaximumPayloadLength + 1]);
        Check(!ManagedUdpProtocol.TryParse(aboveMaximum, s_localIp, s_peerIp,
                                            out _),
              "udp-one-byte-above-maximum-rejected");

        byte[] destinationZero = (byte[])headerOnly.Clone();
        destinationZero[2] = 0;
        destinationZero[3] = 0;
        Check(!ManagedUdpProtocol.TryParse(destinationZero, s_localIp, s_peerIp,
                                            out _),
              "udp-destination-port-zero-rejected-on-receive");
        byte[] sourceZero = (byte[])headerOnly.Clone();
        sourceZero[0] = 0;
        sourceZero[1] = 0;
        Check(!ManagedUdpProtocol.TryParse(sourceZero, s_localIp, s_peerIp, out _),
              "udp-source-port-zero-rejected-on-receive");
    }

    private static void UdpChecksumTests()
    {
        byte[] even = BuildUdp(0x1234, 0x5678, new byte[] { 1, 2, 3, 4 });
        Check(ManagedUdpProtocol.ComputeChecksum(s_localIp, s_peerIp, even) == 0,
              "udp-valid-even-checksum-verifies");
        byte[] odd = BuildUdp(0x1234, 0x5678, new byte[] { 1, 2, 3 });
        Check(ManagedUdpProtocol.ComputeChecksum(s_localIp, s_peerIp, odd) == 0,
              "udp-valid-odd-checksum-verifies");
        byte[] zero = BuildUdp(0x1234, 0x5678, Array.Empty<byte>());
        Check(ManagedUdpProtocol.ComputeChecksum(s_localIp, s_peerIp, zero) == 0,
              "udp-valid-zero-payload-checksum-verifies");

        Check(IndependentChecksum(s_localIp, s_peerIp, 0x1234, 0x5678,
                                  new byte[] { 1, 2, 3, 4 }) == 0x7F03,
              "udp-independent-even-known-vector");
        Check(IndependentChecksum(s_localIp, s_peerIp, 0x1234, 0x5678,
                                  new byte[] { 1, 2, 3 }) == 0x7F09,
              "udp-independent-odd-known-vector");
        Check(IndependentChecksum(s_localIp, s_peerIp, 0x1234, 0x5678,
                                  Array.Empty<byte>()) == 0x8311,
              "udp-independent-zero-known-vector");

        byte[] sourceMutation = (byte[])even.Clone();
        sourceMutation[6] ^= 1;
        Check(ManagedUdpProtocol.ComputeChecksum(s_localIp, s_peerIp,
                                                  sourceMutation) != 0,
              "udp-checksum-field-mutation-fails");
        byte[] portMutation = (byte[])even.Clone();
        portMutation[0] ^= 1;
        Check(!ManagedUdpProtocol.TryParse(portMutation, s_localIp, s_peerIp, out _),
              "udp-source-port-mutation-fails");
        portMutation = (byte[])even.Clone();
        portMutation[2] ^= 1;
        Check(!ManagedUdpProtocol.TryParse(portMutation, s_localIp, s_peerIp, out _),
              "udp-destination-port-mutation-fails");
        byte[] payloadMutation = (byte[])even.Clone();
        payloadMutation[8] ^= 1;
        Check(!ManagedUdpProtocol.TryParse(payloadMutation, s_localIp, s_peerIp, out _),
              "udp-payload-mutation-fails");
        byte[] lengthMutation = (byte[])even.Clone();
        lengthMutation[5] ^= 1;
        Check(!ManagedUdpProtocol.TryParse(lengthMutation, s_localIp, s_peerIp, out _),
              "udp-length-mutation-fails");
        Check(!ManagedUdpProtocol.TryParse(even, new byte[] { 10, 15, 0, 9 },
                                           s_peerIp, out _),
              "udp-source-pseudo-header-mutation-fails");
        Check(!ManagedUdpProtocol.TryParse(even, s_localIp,
                                           new byte[] { 10, 15, 0, 9 }, out _),
              "udp-destination-pseudo-header-mutation-fails");

        byte[] zeroChecksum = BuildUdp(3, 4, new byte[] { 5 });
        zeroChecksum[6] = 0;
        zeroChecksum[7] = 0;
        Check(ManagedUdpProtocol.TryParse(zeroChecksum, s_localIp, s_peerIp,
                  out ManagedUdpDatagram zeroView) && zeroView.Checksum == 0,
              "udp-zero-checksum-receive-accepted");

        bool foundComputedZero = false;
        for (int value = 0; value != 65536 && !foundComputedZero; ++value)
        {
            byte[] candidatePayload =
            {
                (byte)(value >> 8), (byte)value
            };
            if (IndependentChecksum(s_localIp, s_peerIp, 0x1111, 0x2222,
                                    candidatePayload) != 0) continue;
            byte[] candidate = new byte[ManagedUdpProtocol.MaximumDatagramLength];
            Check(ManagedUdpProtocol.TryBuild(candidate, 0x1111, 0x2222,
                                              s_localIp, s_peerIp,
                                              candidatePayload,
                                              out ushort candidateLength),
                  "udp-computed-zero-builder-accepted");
            Check(candidateLength == 10 && candidate[6] == 0xFF &&
                  candidate[7] == 0xFF,
                  "udp-computed-zero-encoded-as-ffff");
            foundComputedZero = true;
        }
        Check(foundComputedZero, "udp-computed-zero-vector-found-independently");
    }

    private static void EndpointTests()
    {
        ManagedUdpEndpointTable table = new();
        Check(table.Count == 0 && table.TryRegister(15180,
                  ManagedUdpEndpointHandler.Phase18Echo),
              "udp-endpoint-register");
        Check(table.Count == 1 && table.TryLookup(15180,
                  out ManagedUdpEndpointHandler handler) &&
              handler == ManagedUdpEndpointHandler.Phase18Echo,
              "udp-endpoint-lookup");
        Check(!table.TryRegister(15180, ManagedUdpEndpointHandler.Phase18Echo),
              "udp-endpoint-duplicate-rejected");
        Check(!table.TryRegister(0, ManagedUdpEndpointHandler.Phase18Echo),
              "udp-endpoint-zero-port-rejected");
        Check(!table.TryLookup(15181, out _), "udp-endpoint-unknown-port");

        Check(table.TryRegister(15181, ManagedUdpEndpointHandler.Phase18Echo) &&
              table.TryRegister(15182, ManagedUdpEndpointHandler.Phase18Echo) &&
              table.TryRegister(15183, ManagedUdpEndpointHandler.Phase18Echo),
              "udp-endpoint-table-filled");
        Check(table.Count == ManagedUdpEndpointTable.Capacity &&
              !table.TryRegister(15184, ManagedUdpEndpointHandler.Phase18Echo),
              "udp-endpoint-table-full-rejected");
        Check(table.TryUnregister(15182) && table.Count == 3 &&
              !table.TryLookup(15182, out _),
              "udp-endpoint-unregister");
        Check(table.TryRegister(15184, ManagedUdpEndpointHandler.Phase18Echo) &&
              table.Count == 4, "udp-endpoint-reregister-after-unregister");
        table.Clear();
        Check(table.Count == 0 && !table.TryLookup(15180, out _),
              "udp-endpoint-reset-clears-table");
        Check(table.TryRegister(15180, ManagedUdpEndpointHandler.Phase18Echo),
              "udp-endpoint-reregister-after-reset");
    }

    private static byte[] BuildUdp(ushort sourcePort, ushort destinationPort,
                                   byte[] payload)
    {
        byte[] datagram = new byte[ManagedUdpProtocol.MaximumDatagramLength];
        Check(ManagedUdpProtocol.TryBuild(datagram, sourcePort, destinationPort,
                                          s_localIp, s_peerIp, payload,
                                          out ushort length),
              "udp-build-helper");
        return datagram.AsSpan(0, length).ToArray();
    }

    private static byte[] BuildUdpUnchecked(ushort sourcePort,
                                            ushort destinationPort,
                                            byte[] payload)
    {
        byte[] datagram = new byte[8 + payload.Length];
        WriteU16(datagram, 0, sourcePort);
        WriteU16(datagram, 2, destinationPort);
        WriteU16(datagram, 4, datagram.Length);
        payload.CopyTo(datagram, 8);
        return datagram;
    }

    private static ushort IndependentChecksum(byte[] source, byte[] destination,
                                              ushort sourcePort,
                                              ushort destinationPort,
                                              byte[] payload)
    {
        byte[] datagram = new byte[8 + payload.Length];
        WriteU16(datagram, 0, sourcePort);
        WriteU16(datagram, 2, destinationPort);
        WriteU16(datagram, 4, datagram.Length);
        payload.CopyTo(datagram, 8);
        uint sum = 0;
        sum = Fold(sum + ReadU16(source, 0));
        sum = Fold(sum + ReadU16(source, 2));
        sum = Fold(sum + ReadU16(destination, 0));
        sum = Fold(sum + ReadU16(destination, 2));
        sum = Fold(sum + 17);
        sum = Fold(sum + (uint)datagram.Length);
        for (int index = 0; index + 1 < datagram.Length; index += 2)
            sum = Fold(sum + ReadU16(datagram, index));
        if ((datagram.Length & 1) != 0)
            sum = Fold(sum + ((uint)datagram[^1] << 8));
        sum = Fold(sum);
        return (ushort)~sum;
    }

    private static uint Fold(uint value)
    {
        value = (value & 0xFFFFU) + (value >> 16);
        return (value & 0xFFFFU) + (value >> 16);
    }

    private static ushort ReadU16(byte[] bytes, int offset)
    {
        return (ushort)((bytes[offset] << 8) | bytes[offset + 1]);
    }

    private static void WriteU16(byte[] bytes, int offset, int value)
    {
        bytes[offset] = (byte)(value >> 8);
        bytes[offset + 1] = (byte)value;
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("FAILED: " + name);
        s_cases++;
        Console.WriteLine("PASS: " + name);
    }
}

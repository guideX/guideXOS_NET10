using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using GuideXOS.Net10.ManagedKernel;

namespace GuideXOS.Net10.ManagedKernelPhase31HostTests;

internal static class Program
{
    private static int s_cases;
    private static readonly ManagedX509UtcTime TestTime =
        new(2028, 1, 1, 0, 0, 0);

    private static int Main()
    {
        try
        {
            RunPrfTests();
            RunRecordTests();
            RunFullHandshake();
            RunOrderingAndNegotiationTests();
            Console.WriteLine($"MANAGED_KERNEL_PHASE31_HOST_TESTS_PASS cases={s_cases}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"MANAGED_KERNEL_PHASE31_HOST_TESTS_FAIL cases={s_cases} {exception}");
            return 1;
        }
    }

    private static void RunPrfTests()
    {
        byte[] secret = Bytes(0x01, 0x02, 0x03, 0x04, 0x05);
        byte[] seed = Bytes(0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0xA6);
        foreach (int length in new[] { 12, 32, 40, 48, 65 })
        {
            byte[] actual = new byte[length];
            byte[] expected = ReferencePrf(secret, "phase31 kat", seed, length);
            Case($"prf-{length}",
                ManagedTls12Prf.TryCompute(secret, "phase31 kat"u8, seed, actual) &&
                Equal(actual, expected));
        }

        byte[] changed = new byte[48];
        byte[] original = new byte[48];
        Case("ems-session-hash-kat",
            ManagedTls12Prf.TryCompute(
                ManagedTls12Phase31Fixtures.PremasterSecret,
                "extended master secret"u8,
                ManagedTls12Phase31Fixtures.SessionHash, original) &&
            Equal(original, ManagedTls12Phase31Fixtures.MasterSecret));
        ManagedTls12Phase31Fixtures.SessionHash.CopyTo(changed, 0);
        changed[0] ^= 1;
        byte[] changedMaster = new byte[48];
        Case("ems-transcript-change", ManagedTls12Prf.TryCompute(
            ManagedTls12Phase31Fixtures.PremasterSecret,
            "extended master secret"u8, changed, changedMaster) &&
            !Equal(changedMaster, ManagedTls12Phase31Fixtures.MasterSecret));
        Array.Clear(changed);
        Array.Clear(original);
        Array.Clear(changedMaster);
    }

    private static void RunRecordTests()
    {
        byte[] key = ManagedTls12Phase31Fixtures.KeyBlock[0..16];
        byte[] iv = ManagedTls12Phase31Fixtures.KeyBlock[32..36];
        byte[] plaintext = "PING"u8.ToArray();
        byte[] record = new byte[ManagedTls12RecordProtection.MaximumRecordSize];
        Case("record-encrypt", ManagedTls12RecordProtection.TryEncrypt(
            0, key, iv, ManagedTls12RecordProtection.ApplicationData,
            plaintext, record, out int length));
        Case("record-length", length == 5 + 8 + plaintext.Length + 16);
        byte[] recovered = new byte[plaintext.Length];
        Case("record-decrypt", ManagedTls12RecordProtection.TryDecrypt(
            0, key, iv, ManagedTls12RecordProtection.ApplicationData,
            record.AsSpan(0, length), recovered, out int recoveredLength) &&
            recoveredLength == plaintext.Length && Equal(recovered, plaintext));

        byte[] corrupt = record[..length];
        corrupt[^1] ^= 1;
        recovered.AsSpan().Fill(0xA5);
        Case("record-corrupt-tag-fails-closed",
            !ManagedTls12RecordProtection.TryDecrypt(
                0, key, iv, ManagedTls12RecordProtection.ApplicationData,
                corrupt, recovered, out _) && All(recovered, 0xA5));
        Case("record-wrong-sequence-fails",
            !ManagedTls12RecordProtection.TryDecrypt(
                1, key, iv, ManagedTls12RecordProtection.ApplicationData,
                record.AsSpan(0, length), recovered, out _));
        Case("record-wrong-type-fails",
            !ManagedTls12RecordProtection.TryDecrypt(
                0, key, iv, ManagedTls12RecordProtection.Handshake,
                record.AsSpan(0, length), recovered, out _));
        Case("record-wrong-version-fails", WrongVersion(record, length, key, iv));
        Case("record-explicit-nonce-is-not-sequence",
            ManagedTls12Phase31Fixtures.ServerFinishedRecord[12] == 0x2A &&
            ManagedTls12RecordProtection.TryDecrypt(
                0, ManagedTls12Phase31Fixtures.KeyBlock[16..32],
                ManagedTls12Phase31Fixtures.KeyBlock[36..40],
                ManagedTls12RecordProtection.Handshake,
                ManagedTls12Phase31Fixtures.ServerFinishedRecord,
                new byte[16], out _));
        Case("record-oversized-rejected", !ManagedTls12RecordProtection.TryEncrypt(
            0, key, iv, ManagedTls12RecordProtection.ApplicationData,
            new byte[ManagedTls12RecordProtection.MaximumPlaintextFragment + 1],
            record, out _));
    }

    private static void RunFullHandshake()
    {
        byte[] scalar = new byte[32];
        scalar[^1] = 1;
        byte[] entropyBytes = Concat(ManagedTls12Phase31Fixtures.ClientRandom,
                                     scalar);
        ManagedSecureRandom random = new(new FixedEntropy(entropyBytes));
        Require(ManagedTls12Client.TryCreate(
            "www.example.com"u8, ManagedTls12Phase31Fixtures.Root, in TestTime,
            random, new byte[ManagedTls12Client.CertificateStorageBytes],
            out ManagedTls12Client? client) && client != null, "client create");
        byte[] helloRecord = new byte[512];
        Require(client!.TryStart(helloRecord, out int helloLength), "client hello");
        Case("clienthello-record-header", helloLength == 5 +
            ManagedTls12Phase31Fixtures.ClientHello.Length &&
            helloRecord[0] == 22 && helloRecord[1] == 3 && helloRecord[2] == 3);
        Case("clienthello-exact", Equal(
            helloRecord.AsSpan(5, ManagedTls12Phase31Fixtures.ClientHello.Length),
            ManagedTls12Phase31Fixtures.ClientHello));

        for (int recordIndex = 0;
             recordIndex != ManagedTls12Phase31Fixtures.ServerRecordCount;
             ++recordIndex)
        {
            byte[] record = ManagedTls12Phase31Fixtures.GetServerRecord(recordIndex);
            for (int index = 0; index != record.Length; ++index)
            Require(client!.TryConsume(record.AsSpan(index, 1)),
                        "fragmented server record " + recordIndex);
            if (recordIndex == ManagedTls12Phase31Fixtures.ServerRecordCount - 1)
            {
                if (client!.State != ManagedTls12ClientState.OutputReady)
                throw new InvalidOperationException(
                        $"server-hellodone index={recordIndex} type={record[0]} length={record.Length} state={client.State} last={client.LastHandshake} certs={client.PeerCertificateCount} transcript={client.TranscriptLength} hs={client.HandshakeBytesPending}/{client.ExpectedHandshakeLength}");
                Case("server-hellodone-output-ready", true);
                byte[] output = new byte[512];
                Require(client!.TryTakeOutput(output, out int outputLength),
                        "client flight");
                byte[] expected = Concat(
                    PlainRecord(22, ManagedTls12Phase31Fixtures.ClientKeyExchange),
                    ManagedTls12Phase31Fixtures.ChangeCipherSpec,
                    ManagedTls12Phase31Fixtures.ClientFinishedRecord);
                if (!Equal(output.AsSpan(0, outputLength), expected))
                {
                    int mismatch = 0;
                    while (mismatch < outputLength && mismatch < expected.Length &&
                           output[mismatch] == expected[mismatch]) mismatch++;
                    throw new InvalidOperationException(
                        $"client flight mismatch actual={outputLength} expected={expected.Length} offset={mismatch} actualByte={(mismatch < outputLength ? output[mismatch] : 0):X2} expectedByte={(mismatch < expected.Length ? expected[mismatch] : 0):X2}");
                }
                Case("client-flight-exact", true);
                Case("ems-negotiated", client!.EmsNegotiated);
                Case("client-flight-state", client!.State == ManagedTls12ClientState.NeedInput);
            }
        }

        byte[] ccs = ManagedTls12Phase31Fixtures.ChangeCipherSpec;
        for (int index = 0; index != ccs.Length; ++index)
            Require(client!.TryConsume(ccs.AsSpan(index, 1)), "server ccs");
        byte[] serverFinished = ManagedTls12Phase31Fixtures.ServerFinishedRecord;
        for (int index = 0; index != serverFinished.Length; ++index)
            Require(client!.TryConsume(serverFinished.AsSpan(index, 1)),
                    "server finished");
        Case("established", client!.State == ManagedTls12ClientState.Established);
        Case("server-finished-exact", Equal(
            ManagedTls12Phase31Fixtures.ServerVerifyData,
            ManagedTls12Phase31Fixtures.ServerFinished[4..]));

        Case("server-application-data", client!.TryReadApplicationData(
            ManagedTls12Phase31Fixtures.ServerApplicationRecord) &&
            Equal(client.ApplicationPlaintext, "PONG"u8));
        byte[] appRecord = new byte[ManagedTls12RecordProtection.MaximumRecordSize];
        Require(client!.TryEncryptApplicationData("PING"u8, appRecord,
                                                out int appLength), "client app");
        byte[] expectedApp = ProtectedRecord(
            ManagedTls12Phase31Fixtures.KeyBlock[0..16],
            ManagedTls12Phase31Fixtures.KeyBlock[32..36], 1, 23,
            "PING"u8.ToArray(), appRecord.AsSpan(5, 8).ToArray());
        Case("client-application-data", Equal(
            appRecord.AsSpan(0, appLength), expectedApp));
        byte[] closeNotify = ProtectedRecord(
            ManagedTls12Phase31Fixtures.KeyBlock[16..32],
            ManagedTls12Phase31Fixtures.KeyBlock[36..40], 2, 21,
            new byte[] { 1, 0 }, new byte[] { 0, 0, 0, 0, 0, 0, 0, 2 });
        Case("encrypted-close-notify", client!.TryConsume(closeNotify) &&
            client.State == ManagedTls12ClientState.Closed);
        client!.Teardown();
        Case("teardown", client!.State == ManagedTls12ClientState.Closed);
    }

    private static void RunOrderingAndNegotiationTests()
    {
        byte[] scalar = new byte[32]; scalar[^1] = 1;
        Case("certificate-before-serverhello", FailsAfterStart(
            Concat(ManagedTls12Phase31Fixtures.CertificateRecord0,
                   ManagedTls12Phase31Fixtures.CertificateRecord1,
                   ManagedTls12Phase31Fixtures.CertificateRecord2,
                   ManagedTls12Phase31Fixtures.CertificateRecord3,
                   ManagedTls12Phase31Fixtures.CertificateRecord4,
                   ManagedTls12Phase31Fixtures.CertificateRecord5,
                   ManagedTls12Phase31Fixtures.CertificateRecord6,
                   ManagedTls12Phase31Fixtures.CertificateRecord7,
                   ManagedTls12Phase31Fixtures.CertificateRecord8,
                   ManagedTls12Phase31Fixtures.CertificateRecord9), scalar));
        byte[] noEms = (byte[])ManagedTls12Phase31Fixtures.ServerHelloRecord.Clone();
        noEms[^2] = 0;
        noEms[^1] = 24;
        Case("missing-ems-rejected", FailsAfterStart(noEms, scalar));
        byte[] badFinished = (byte[])ManagedTls12Phase31Fixtures.ServerFinishedRecord.Clone();
        badFinished[^1] ^= 1;
        Case("bad-finished-rejected", BadFinishedFails(scalar, badFinished));
        Case("production-random-unavailable-fails-closed",
            ProductionRandomFailsClosed());
        Case("failed-session-recovery", RecoveryAfterFailure(scalar));
    }

    private static bool FailsAfterStart(byte[] record, byte[] scalar)
    {
        ManagedSecureRandom random = new(new FixedEntropy(
            Concat(ManagedTls12Phase31Fixtures.ClientRandom, scalar)));
        if (!ManagedTls12Client.TryCreate("www.example.com"u8,
                ManagedTls12Phase31Fixtures.Root, in TestTime, random,
                new byte[ManagedTls12Client.CertificateStorageBytes],
                out ManagedTls12Client? client) || client == null)
            return false;
        Span<byte> hello = stackalloc byte[512];
        return client.TryStart(hello, out _) && !client.TryConsume(record) &&
               client.State == ManagedTls12ClientState.Failed;
    }

    private static bool BadFinishedFails(byte[] scalar, byte[] badFinished)
    {
        ManagedSecureRandom random = new(new FixedEntropy(
            Concat(ManagedTls12Phase31Fixtures.ClientRandom, scalar)));
        if (!ManagedTls12Client.TryCreate("www.example.com"u8,
                ManagedTls12Phase31Fixtures.Root, in TestTime, random,
                new byte[ManagedTls12Client.CertificateStorageBytes],
                out ManagedTls12Client? client) || client == null)
            return false;
        Span<byte> hello = stackalloc byte[512];
        if (!client.TryStart(hello, out _)) return false;
        for (int recordIndex = 0;
             recordIndex != ManagedTls12Phase31Fixtures.ServerRecordCount;
             ++recordIndex)
        {
            byte[] record = ManagedTls12Phase31Fixtures.GetServerRecord(recordIndex);
            if (!client.TryConsume(record)) return false;
            if (client.State == ManagedTls12ClientState.OutputReady)
            {
                byte[] output = new byte[512];
                if (!client.TryTakeOutput(output, out _)) return false;
            }
        }
        if (!client.TryConsume(ManagedTls12Phase31Fixtures.ChangeCipherSpec)) return false;
        return !client.TryConsume(badFinished) &&
               client.State == ManagedTls12ClientState.Failed;
    }

    private static bool ProductionRandomFailsClosed()
    {
        ManagedSecureRandom random = new(new FixedEntropy(Array.Empty<byte>()));
        return ManagedTls12Client.TryCreate("www.example.com"u8,
            ManagedTls12Phase31Fixtures.Root, in TestTime, random,
            new byte[ManagedTls12Client.CertificateStorageBytes],
            out ManagedTls12Client? client) && client != null &&
            !client.TryStart(stackalloc byte[512], out _) &&
            client.State == ManagedTls12ClientState.Failed;
    }

    private static bool RecoveryAfterFailure(byte[] scalar)
    {
        bool failed = FailsAfterStart(Concat(
            ManagedTls12Phase31Fixtures.CertificateRecord0,
            ManagedTls12Phase31Fixtures.CertificateRecord1,
            ManagedTls12Phase31Fixtures.CertificateRecord2,
            ManagedTls12Phase31Fixtures.CertificateRecord3,
            ManagedTls12Phase31Fixtures.CertificateRecord4,
            ManagedTls12Phase31Fixtures.CertificateRecord5,
            ManagedTls12Phase31Fixtures.CertificateRecord6,
            ManagedTls12Phase31Fixtures.CertificateRecord7,
            ManagedTls12Phase31Fixtures.CertificateRecord8,
            ManagedTls12Phase31Fixtures.CertificateRecord9), scalar);
        if (!failed) return false;
        ManagedSecureRandom random = new(new FixedEntropy(
            Concat(ManagedTls12Phase31Fixtures.ClientRandom, scalar)));
        if (!ManagedTls12Client.TryCreate("www.example.com"u8,
            ManagedTls12Phase31Fixtures.Root, in TestTime, random,
            new byte[ManagedTls12Client.CertificateStorageBytes],
            out ManagedTls12Client? client) || client == null)
            return false;
        bool started = client.TryStart(stackalloc byte[512], out _);
        if (!started)
            throw new InvalidOperationException($"recovery start state={client.State}");
        return true;
    }

    private static bool WrongVersion(byte[] record, int length, byte[] key, byte[] iv)
    {
        byte[] copy = record[..length];
        copy[2] = 2;
        return !ManagedTls12RecordProtection.TryDecrypt(
            0, key, iv, ManagedTls12RecordProtection.ApplicationData,
            copy, new byte[4], out _);
    }

    private static byte[] ReferencePrf(byte[] secret, string label,
                                       byte[] seed, int length)
    {
        byte[] labelSeed = Concat(System.Text.Encoding.ASCII.GetBytes(label), seed);
        byte[] a = labelSeed;
        byte[] output = new byte[length];
        int offset = 0;
        while (offset < length)
        {
            a = ReferenceHmac(secret, a);
            byte[] block = ReferenceHmac(secret, Concat(a, labelSeed));
            int count = Math.Min(block.Length, length - offset);
            Array.Copy(block, 0, output, offset, count);
            offset += count;
        }
        return output;
    }

    private static byte[] ProtectedRecord(byte[] key, byte[] iv, ulong sequence,
                                           byte type, byte[] plaintext,
                                           byte[] explicitNonce)
    {
        byte[] result = new byte[ManagedTls12RecordProtection.MaximumRecordSize];
        Require(ManagedTls12RecordProtection.TryEncrypt(sequence, key, iv, type,
            plaintext, result, out int length), "reference record");
        explicitNonce.CopyTo(result, 5);
        return result[..length];
    }

    private static byte[] PlainRecord(byte type, byte[] fragment)
    {
        byte[] result = new byte[5 + fragment.Length];
        result[0] = type; result[1] = 3; result[2] = 3;
        result[3] = (byte)(fragment.Length >> 8);
        result[4] = (byte)fragment.Length;
        fragment.CopyTo(result, 5);
        return result;
    }

    private static byte[] ReferenceHmac(byte[] key, byte[] data)
    {
        using HMACSHA256 hmac = new(key);
        return hmac.ComputeHash(data);
    }

    private static byte[] Concat(params byte[][] values)
    {
        int length = 0;
        foreach (byte[] value in values) length += value.Length;
        byte[] result = new byte[length];
        int offset = 0;
        foreach (byte[] value in values)
        {
            value.CopyTo(result, offset);
            offset += value.Length;
        }
        return result;
    }

    private static byte[] Bytes(params int[] values)
    {
        byte[] result = new byte[values.Length];
        for (int index = 0; index != values.Length; ++index)
            result[index] = (byte)values[index];
        return result;
    }

    private static bool Equal(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right) =>
        ManagedCryptoComparison.FixedTimeEquals(left, right);

    private static bool All(ReadOnlySpan<byte> value, byte expected)
    {
        foreach (byte current in value) if (current != expected) return false;
        return true;
    }

    private static void Case(string name, bool passed)
    {
        ++s_cases;
        if (!passed) throw new InvalidOperationException("failed: " + name);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class FixedEntropy : IManagedEntropyProvider
    {
        private readonly byte[] _bytes;
        private int _offset;
        internal FixedEntropy(byte[] bytes) => _bytes = bytes;
        public bool IsAvailable => _offset <= _bytes.Length;
        public bool TryFill(Span<byte> destination)
        {
            if (destination.Length > _bytes.Length - _offset) return false;
            _bytes.AsSpan(_offset, destination.Length).CopyTo(destination);
            _offset += destination.Length;
            return true;
        }
    }
}

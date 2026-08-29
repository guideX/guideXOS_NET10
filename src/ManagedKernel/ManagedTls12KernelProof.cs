using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace GuideXOS.Net10.ManagedKernel;

internal static unsafe class ManagedTls12KernelProof
{
    private static int s_run;

    [MethodImpl(MethodImplOptions.NoInlining)]
    [UnmanagedCallersOnly(EntryPoint = "GxManagedKernelRunPhase31")]
    internal static uint Run()
    {
        if (!ManagedKernelContract.IsStarted || s_run != 0 ||
            !ManagedKernelContract.DeviceResourcesInstalled ||
            !ManagedKernelContract.DmaServicesInstalled ||
            !ManagedKernelContract.EntropyServicesInstalled)
            return ManagedKernelContract.InvalidState;

        if (!RunProof() ||
            !KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PHASE31_PASS\r\n"u8))
            return ManagedKernelContract.InvalidState;
        s_run = 1;
        return ManagedKernelContract.ManagedOk;
    }

    private static bool RunProof()
    {
        if (!ManagedKernelContract.TryEnsureEntropyService() ||
            ManagedKernelContract.EntropyService == null ||
            ManagedKernelContract.SecureRandom == null)
            return false;

        ManagedEntropyService entropy = ManagedKernelContract.EntropyService;
        ManagedSecureRandom random = ManagedKernelContract.SecureRandom;
        ManagedVirtioRngDriver? productionDriver = null;
        bool productionDriverAttached = false;
        try
        {
            /* Phase 26 proves provider teardown and leaves the router without
               a live virtio attachment.  Reacquire that bounded provider for
               the TLS proof so the client random and ephemeral scalar still
               come from the production entropy boundary. */
            if (!random.IsAvailable)
            {
                ManagedVirtioRngDriver? candidate =
                    ManagedVirtioRngDriver.TryCreate();
                if (!candidate.HasValue || !candidate.Value.TryStart())
                    return false;
                productionDriver = candidate.Value;
                entropy.AttachVirtioRng(productionDriver.Value);
                productionDriverAttached = true;
            }

            Span<byte> entropyCheck = stackalloc byte[32];
            Span<byte> productionScalar =
                stackalloc byte[ManagedP256.PrivateScalarSize];
            Span<byte> productionPublic =
                stackalloc byte[ManagedP256.PublicKeySize];
            try
            {
                if (!random.IsAvailable || !random.TryFill(entropyCheck) ||
                    !ManagedP256.TryGeneratePrivateKey(
                        random, productionScalar) ||
                    !ManagedP256.TryDerivePublicKey(
                        productionScalar, productionPublic) ||
                    !ManagedP256.TryValidatePublicKey(productionPublic) ||
                    !KernelLog.Write(
                        "GXOS_NET10:MANAGED_TLS12_PRODUCTION_ENTROPY_INIT_PASS\r\n"u8) ||
                    !KernelLog.Write(
                        "GXOS_NET10:MANAGED_TLS12_PRODUCTION_EPHEMERAL_INIT_PASS\r\n"u8))
                    return false;
                return RunProofCore();
            }
            finally
            {
                entropyCheck.Clear();
                productionScalar.Clear();
                productionPublic.Clear();
            }
        }
        finally
        {
            if (productionDriverAttached)
            {
                entropy.DetachVirtioRng(productionDriver!.Value);
                productionDriver.Value.TryStop();
            }
        }
    }

    private static bool RunProofCore()
    {
        if (!RunRecordProof() ||
            !KernelLog.Write("GXOS_NET10:MANAGED_TLS12_RECORD_PARSER_PASS\r\n"u8))
            return false;

        Span<byte> scalar = stackalloc byte[ManagedP256.PrivateScalarSize];
        scalar.Clear();
        scalar[^1] = 1;
        Span<byte> premaster = stackalloc byte[ManagedP256.SharedSecretSize];
        try
        {
            if (!ManagedP256.TryDeriveSharedSecret(
                    scalar, ManagedTls12Phase31Fixtures.ServerKeyExchange.AsSpan(8, 65),
                    premaster) ||
                !ManagedCryptoComparison.FixedTimeEquals(
                    premaster, ManagedTls12Phase31Fixtures.PremasterSecret) ||
                !KernelLog.Write(
                    "GXOS_NET10:MANAGED_TLS12_ECDH_PREMASTER_KAT_PASS\r\n"u8))
                return false;
        }
        finally
        {
            scalar.Clear();
            premaster.Clear();
        }

        byte[] workingStorage =
            new byte[ManagedTls12Client.CertificateStorageBytes];
        FixedEntropy deterministicEntropy = CreateDeterministicEntropy();
        ManagedSecureRandom deterministicRandom =
            new(deterministicEntropy);
        ManagedTls12Client? client = ManagedTls12Client.TryCreate(
            "www.example.com"u8, ManagedTls12Phase31Fixtures.Root,
            new ManagedX509UtcTime(2028, 1, 1, 0, 0, 0), deterministicRandom,
            workingStorage, out ManagedTls12Client? createdClient)
            ? createdClient : null;
        if (client == null)
            return false;
        Span<byte> helloRecord = stackalloc byte[512];
        if (!client.TryStart(helloRecord, out int helloLength))
        {
            client.Teardown();
            return false;
        }
        if (
            helloLength != 5 + ManagedTls12Phase31Fixtures.ClientHello.Length ||
            !ManagedCryptoComparison.FixedTimeEquals(
                helloRecord.Slice(5, ManagedTls12Phase31Fixtures.ClientHello.Length),
                ManagedTls12Phase31Fixtures.ClientHello) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_TLS12_CLIENTHELLO_KAT_PASS\r\n"u8))
        {
            client.Teardown();
            return false;
        }

        for (int recordIndex = 0;
             recordIndex != ManagedTls12Phase31Fixtures.ServerRecordCount;
             ++recordIndex)
        {
            ReadOnlySpan<byte> record = ManagedTls12Phase31Fixtures.GetServerRecord(
                recordIndex);
            for (int offset = 0; offset != record.Length; ++offset)
            {
                if (!client.TryConsume(record.Slice(offset, 1)))
                {
                    client.Teardown();
                    return false;
                }
            }
            if (recordIndex == 0 &&
                !KernelLog.Write("GXOS_NET10:MANAGED_TLS12_SERVERHELLO_PASS\r\n"u8))
            {
                client.Teardown();
                return false;
            }
            if (recordIndex == 11 &&
                !KernelLog.Write(
                    "GXOS_NET10:MANAGED_TLS12_SERVER_KEY_EXCHANGE_PASS\r\n"u8))
            {
                client.Teardown();
                return false;
            }
        }

        if (client.State != ManagedTls12ClientState.OutputReady ||
            !client.EmsNegotiated || client.PeerCertificateCount != 2 ||
            !KernelLog.Write("GXOS_NET10:MANAGED_TLS12_CERTIFICATE_CHAIN_PASS\r\n"u8) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_TLS12_HOSTNAME_PASS\r\n"u8) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_TLS12_EMS_NEGOTIATION_PASS\r\n"u8))
        {
            client.Teardown();
            return false;
        }

        Span<byte> clientFlight = stackalloc byte[128];
        if (!client.TryTakeOutput(clientFlight, out int clientFlightLength) ||
            !MatchesClientFlight(clientFlight[..clientFlightLength]) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_TLS12_CLIENT_ECDH_PUBLIC_PASS\r\n"u8) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_TLS12_CLIENT_FINISHED_KAT_PASS\r\n"u8) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_TLS12_CLIENT_FINISHED_GCM_KAT_PASS\r\n"u8))
        {
            client.Teardown();
            return false;
        }

        ReadOnlySpan<byte> ccs = ManagedTls12Phase31Fixtures.ChangeCipherSpec;
        for (int offset = 0; offset != ccs.Length; ++offset)
        {
            if (!client.TryConsume(ccs.Slice(offset, 1)))
            {
                client.Teardown();
                return false;
            }
        }
        ReadOnlySpan<byte> serverFinished =
            ManagedTls12Phase31Fixtures.ServerFinishedRecord;
        for (int offset = 0; offset != serverFinished.Length; ++offset)
        {
            if (!client.TryConsume(serverFinished.Slice(offset, 1)))
            {
                client.Teardown();
                return false;
            }
        }
        if (client.State != ManagedTls12ClientState.Established ||
            !KernelLog.Write("GXOS_NET10:MANAGED_TLS12_SERVER_FINISHED_DECRYPT_PASS\r\n"u8) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_TLS12_SERVER_FINISHED_KAT_PASS\r\n"u8) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_TLS12_ESTABLISHED_PASS\r\n"u8))
        {
            client.Teardown();
            return false;
        }

        Span<byte> sessionHash = stackalloc byte[32];
        Span<byte> master = stackalloc byte[48];
        Span<byte> keyBlock = stackalloc byte[40];
        Span<byte> clientFinishedHash = stackalloc byte[32];
        Span<byte> serverFinishedHash = stackalloc byte[32];
        try
        {
            if (!client.TryCopyProofMaterial(sessionHash, master, keyBlock,
                    clientFinishedHash, serverFinishedHash) ||
                !ManagedCryptoComparison.FixedTimeEquals(
                    sessionHash, ManagedTls12Phase31Fixtures.SessionHash) ||
                !ManagedCryptoComparison.FixedTimeEquals(
                    master, ManagedTls12Phase31Fixtures.MasterSecret) ||
                !ManagedCryptoComparison.FixedTimeEquals(
                    keyBlock, ManagedTls12Phase31Fixtures.KeyBlock) ||
                !ManagedCryptoComparison.FixedTimeEquals(
                    clientFinishedHash, ManagedTls12Phase31Fixtures.SessionHash) ||
                !KernelLog.Write("GXOS_NET10:MANAGED_TLS12_EMS_SESSION_HASH_KAT_PASS\r\n"u8) ||
                !KernelLog.Write("GXOS_NET10:MANAGED_TLS12_MASTER_SECRET_KAT_PASS\r\n"u8) ||
                !KernelLog.Write("GXOS_NET10:MANAGED_TLS12_TRAFFIC_KEY_KAT_PASS\r\n"u8))
            {
                client.Teardown();
                return false;
            }
        }
        finally
        {
            sessionHash.Clear();
            master.Clear();
            keyBlock.Clear();
            clientFinishedHash.Clear();
            serverFinishedHash.Clear();
        }

        if (!client.TryReadApplicationData(
                ManagedTls12Phase31Fixtures.ServerApplicationRecord) ||
            !ManagedCryptoComparison.FixedTimeEquals(
                client.ApplicationPlaintext, "PONG"u8) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_TLS12_APPLICATION_DATA_PASS\r\n"u8))
        {
            client.Teardown();
            return false;
        }
        client.Teardown();

        if (!RunRejectedHandshakeTests(
                client, deterministicRandom, deterministicEntropy) ||
            !KernelLog.Write(
                "GXOS_NET10:MANAGED_TLS12_MALFORMED_FINISHED_REJECTION_PASS\r\n"u8) ||
            !KernelLog.Write(
                "GXOS_NET10:MANAGED_TLS12_MISSING_EMS_REJECTION_PASS\r\n"u8) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_TLS12_FAILURE_RECOVERY_PASS\r\n"u8))
            return false;
        return true;
    }

    private static bool RunRecordProof()
    {
        Span<byte> record = stackalloc byte[
            ManagedTls12RecordProtection.MaximumRecordSize];
        Span<byte> plaintext = stackalloc byte[4];
        Span<byte> recovered = stackalloc byte[4];
        plaintext[0] = (byte)'P';
        plaintext[1] = (byte)'I';
        plaintext[2] = (byte)'N';
        plaintext[3] = (byte)'G';
        if (!ManagedTls12RecordProtection.TryEncrypt(
                7, ManagedTls12Phase31Fixtures.KeyBlock[..16],
                ManagedTls12Phase31Fixtures.KeyBlock[32..36],
                ManagedTls12RecordProtection.ApplicationData, plaintext,
                record, out int recordLength) ||
            !ManagedTls12RecordProtection.TryDecrypt(
                7, ManagedTls12Phase31Fixtures.KeyBlock[..16],
                ManagedTls12Phase31Fixtures.KeyBlock[32..36],
                ManagedTls12RecordProtection.ApplicationData,
                record[..recordLength], recovered, out int recoveredLength) ||
            recoveredLength != 4 ||
            !ManagedCryptoComparison.FixedTimeEquals(plaintext, recovered))
            return false;
        record[recordLength - 1] ^= 1;
        recovered.Fill(0xA5);
        return !ManagedTls12RecordProtection.TryDecrypt(
                   7, ManagedTls12Phase31Fixtures.KeyBlock[..16],
                   ManagedTls12Phase31Fixtures.KeyBlock[32..36],
                   ManagedTls12RecordProtection.ApplicationData,
                   record[..recordLength], recovered, out _) &&
               recovered[0] == 0xA5 && recovered[3] == 0xA5;
    }

    private static bool MatchesClientFlight(ReadOnlySpan<byte> actual)
    {
        int expectedLength = 5 + ManagedTls12Phase31Fixtures.ClientKeyExchange.Length +
                             ManagedTls12Phase31Fixtures.ChangeCipherSpec.Length +
                             ManagedTls12Phase31Fixtures.ClientFinishedRecord.Length;
        if (actual.Length != expectedLength) return false;
        int offset = 0;
        if (actual[offset++] != ManagedTls12RecordProtection.Handshake ||
            actual[offset++] != 3 || actual[offset++] != 3)
            return false;
        int ckeRecordLength = 5 + ManagedTls12Phase31Fixtures.ClientKeyExchange.Length;
        if (!ManagedCryptoComparison.FixedTimeEquals(
                actual.Slice(5, ManagedTls12Phase31Fixtures.ClientKeyExchange.Length),
                ManagedTls12Phase31Fixtures.ClientKeyExchange))
            return false;
        offset = ckeRecordLength;
        if (!ManagedCryptoComparison.FixedTimeEquals(
                actual.Slice(offset, ManagedTls12Phase31Fixtures.ChangeCipherSpec.Length),
                ManagedTls12Phase31Fixtures.ChangeCipherSpec))
            return false;
        offset += ManagedTls12Phase31Fixtures.ChangeCipherSpec.Length;
        return ManagedCryptoComparison.FixedTimeEquals(
            actual[offset..], ManagedTls12Phase31Fixtures.ClientFinishedRecord);
    }

    private static FixedEntropy CreateDeterministicEntropy()
    {
        Span<byte> scalar = stackalloc byte[ManagedP256.PrivateScalarSize];
        scalar.Clear();
        scalar[^1] = 1;
        byte[] entropy = new byte[64];
        ManagedTls12Phase31Fixtures.ClientRandom.CopyTo(entropy, 0);
        scalar.CopyTo(entropy.AsSpan(32));
        scalar.Clear();
        return new FixedEntropy(entropy);
    }

    private static bool RunRejectedHandshakeTests(
        ManagedTls12Client client,
        ManagedSecureRandom random,
        FixedEntropy entropy)
    {
        entropy.Reset();
        if (!client.TryReset(random)) return false;
        ManagedTls12Client missingEms = client;
        Span<byte> hello = stackalloc byte[512];
        if (!missingEms.TryStart(hello, out _))
        {
            missingEms.Teardown();
            return false;
        }
        Span<byte> noEms = stackalloc byte[
            ManagedTls12Phase31Fixtures.ServerHelloRecord.Length];
        ManagedTls12Phase31Fixtures.ServerHelloRecord.CopyTo(noEms);
        noEms[^2] = 0;
        noEms[^1] = 24;
        if (missingEms.TryConsume(noEms) ||
            missingEms.State != ManagedTls12ClientState.Failed)
        {
            missingEms.Teardown();
            return false;
        }
        missingEms.Teardown();

        entropy.Reset();
        if (!client.TryReset(random)) return false;
        ManagedTls12Client badFinished = client;
        if (!badFinished.TryStart(hello, out _))
        {
            badFinished.Teardown();
            return false;
        }
        Span<byte> discardedFlight = stackalloc byte[512];
        for (int recordIndex = 0;
             recordIndex != ManagedTls12Phase31Fixtures.ServerRecordCount;
             ++recordIndex)
        {
            byte[] record = ManagedTls12Phase31Fixtures.GetServerRecord(recordIndex);
            if (!badFinished.TryConsume(record))
            {
                badFinished.Teardown();
                return false;
            }
            if (badFinished.State == ManagedTls12ClientState.OutputReady &&
                !badFinished.TryTakeOutput(discardedFlight, out _))
            {
                badFinished.Teardown();
                return false;
            }
        }
        if (!badFinished.TryConsume(ManagedTls12Phase31Fixtures.ChangeCipherSpec))
        {
            badFinished.Teardown();
            return false;
        }
        Span<byte> bad = stackalloc byte[
            ManagedTls12Phase31Fixtures.ServerFinishedRecord.Length];
        ManagedTls12Phase31Fixtures.ServerFinishedRecord.CopyTo(bad);
        bad[^1] ^= 1;
        if (badFinished.TryConsume(bad) ||
            badFinished.State != ManagedTls12ClientState.Failed)
        {
            badFinished.Teardown();
            return false;
        }
        badFinished.Teardown();

        entropy.Reset();
        if (!client.TryReset(random)) return false;
        ManagedTls12Client recovery = client;
        bool success = recovery.TryStart(hello, out _) &&
                       recovery.State == ManagedTls12ClientState.NeedInput;
        recovery.Teardown();
        return success;
    }

    private sealed class FixedEntropy : IManagedEntropyProvider
    {
        private readonly byte[] _bytes;
        private int _offset;

        internal FixedEntropy(byte[] bytes) => _bytes = bytes;

        internal void Reset() => _offset = 0;

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

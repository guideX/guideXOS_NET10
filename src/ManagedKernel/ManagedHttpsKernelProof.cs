using System;

namespace GuideXOS.Net10.ManagedKernel;

/* Bare-metal proof consumer. It knows only the managed service and the
   intentionally deterministic Phase 31 fixture. The fixture provider makes
   the endpoint replayable; the production public ManagedHttpsClient path
   obtains entropy from ManagedKernelContract instead. */
internal sealed class ManagedPhase32TestConsumer
{
    private static ReadOnlySpan<byte> Hostname => "www.example.com"u8;
    private static ReadOnlySpan<byte> Path => "/phase32"u8;
    private static ReadOnlySpan<byte> ExpectedBody => "phase32-http-pass"u8;

    private readonly ManagedNetworkService _service;
    private readonly ManagedHttpsClient _client;
    private readonly byte[] _body =
        new byte[ManagedHttpLimits.MaximumBodyCapacity];

    internal ManagedPhase32TestConsumer(ManagedNetworkService service)
    {
        _service = service;
        ManagedSecureRandom random = new(new FixedEntropy(
            CreateDeterministicEntropy()));
        _client = new ManagedHttpsClient(
            service, ManagedTls12Phase31Fixtures.Root,
            new ManagedX509UtcTime(2028, 1, 1, 0, 0, 0), random);
    }

    internal bool TryRun()
    {
        NetworkStatus status = _service.GetStatus();
        if (!status.DhcpBound || !status.Configured ||
            !KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE32_NETWORK_READY\r\n"u8) ||
            _client.BeginGet(Hostname, Path) != NetworkOperationResult.Started ||
            !KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE32_REQUEST_STARTED\r\n"u8))
            return false;

        bool dnsLogged = false;
        bool tcpLogged = false;
        bool tlsLogged = false;
        bool requestLogged = false;
        bool applicationLogged = false;
        bool statusLogged = false;
        bool gcLogged = false;
        bool bodyLogged = false;
        for (int count = 0; count != 512; ++count)
        {
            NetworkOperationResult result = _client.Poll();
            if (result == NetworkOperationResult.Failed ||
                _client.State == ManagedHttpsClientState.Failed)
                return false;

            if (!dnsLogged && _client.ResolvedAddress.IsUsable)
            {
                if (_client.ResolvedAddress.Value != 0x0A0F0002U ||
                    !KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE32_DNS_SUCCESS\r\n"u8) ||
                    !KernelLog.WriteHexLine(
                        "GXOS_NET10:MANAGED_HTTPS_PHASE32_RESOLVED_IPV4=0x"u8,
                        _client.ResolvedAddress.Value))
                    return false;
                dnsLogged = true;
            }
            if (!tcpLogged && _client.State >= ManagedHttpsClientState.Handshaking)
            {
                if (!KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE32_TCP_CONNECTED\r\n"u8))
                    return false;
                tcpLogged = true;
            }
            if (!tlsLogged && _client.TlsAuthenticated)
            {
                if (!KernelLog.Write(
                        "GXOS_NET10:MANAGED_HTTPS_PHASE32_TLS_HANDSHAKE_AUTHENTICATED\r\n"u8))
                    return false;
                tlsLogged = true;
            }
            if (!requestLogged && _client.RequestSent)
            {
                if (!KernelLog.Write(
                        "GXOS_NET10:MANAGED_HTTPS_PHASE32_HTTP_REQUEST_ENCRYPTED_SENT\r\n"u8))
                    return false;
                requestLogged = true;
            }
            if (!applicationLogged && _client.ApplicationDataReceived)
            {
                if (!KernelLog.Write(
                        "GXOS_NET10:MANAGED_HTTPS_PHASE32_TLS_APPLICATION_DATA_AUTHENTICATED_DECRYPTED\r\n"u8))
                    return false;
                applicationLogged = true;
            }
            if (!statusLogged && _client.StatusParsed)
            {
                if (_client.StatusCode != 200 ||
                    !KernelLog.Write(
                        "GXOS_NET10:MANAGED_HTTPS_PHASE32_HTTP_STATUS_PARSED=200\r\n"u8))
                    return false;
                statusLogged = true;
            }
            if (statusLogged && !gcLogged)
            {
                GC.Collect();
                if (!_client.StatusParsed || _client.StatusCode != 200 ||
                    !KernelLog.Write(
                        "GXOS_NET10:MANAGED_HTTPS_PHASE32_GC_SURVIVAL_PASSED\r\n"u8))
                    return false;
                gcLogged = true;
            }
            if (!bodyLogged && _client.ResponseBodyComplete)
            {
                if (_client.ResponseBodyLength != ExpectedBody.Length ||
                    _client.ContentLength != ExpectedBody.Length ||
                    !KernelLog.Write(
                        "GXOS_NET10:MANAGED_HTTPS_PHASE32_BODY_RECEIVED\r\n"u8))
                    return false;
                bodyLogged = true;
            }
            if (_client.State != ManagedHttpsClientState.Succeeded) continue;
            if (!dnsLogged || !tcpLogged || !tlsLogged || !requestLogged ||
                !applicationLogged || !statusLogged || !gcLogged || !bodyLogged ||
                !_client.TryCopyResponseBody(_body, out int bodyLength) ||
                bodyLength != ExpectedBody.Length ||
                !_body.AsSpan(0, bodyLength).SequenceEqual(ExpectedBody) ||
                !KernelLog.Write(
                    "GXOS_NET10:MANAGED_HTTPS_PHASE32_BODY_VERIFIED\r\n"u8) ||
                !KernelLog.Write(
                    "GXOS_NET10:MANAGED_HTTPS_PHASE32_TEARDOWN_COMPLETE\r\n"u8) ||
                !KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE32_PASS\r\n"u8))
                return false;
            return true;
        }
        return false;
    }

    private static byte[] CreateDeterministicEntropy()
    {
        byte[] entropy = new byte[64];
        ManagedTls12Phase31Fixtures.ClientRandom.CopyTo(entropy, 0);
        entropy[63] = 1;
        return entropy;
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

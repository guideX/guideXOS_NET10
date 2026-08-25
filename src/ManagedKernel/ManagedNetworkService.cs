using System;

namespace GuideXOS.Net10.ManagedKernel;

public enum NetworkOperationResult : byte
{
    Started = 0,
    Success = 1,
    Busy = 2,
    InvalidArgument = 3,
    NotConfigured = 4,
    NoResource = 5,
    Failed = 6,
    Unavailable = 7,
    Rejected = 8
}

public enum NetworkResolutionState : byte
{
    Idle = 0,
    Pending = 1,
    Success = 2,
    NxDomain = 3,
    Failed = 4
}

public enum NetworkPingState : byte
{
    Idle = 0,
    Pending = 1,
    Success = 2,
    Failed = 3
}

public enum NetworkServiceEvent : byte
{
    None = 0,
    DnsResolved = 1,
    DnsNxDomain = 2,
    PingReply = 3,
    UdpReceived = 4,
    UdpReceiveOverflow = 5
}

/* Network-order IPv4 value type.  It deliberately has no System.Net
   dependency and contains no reference fields. */
public readonly struct Ipv4Address : IEquatable<Ipv4Address>
{
    public Ipv4Address(uint networkOrderValue)
    {
        Value = networkOrderValue;
    }

    public uint Value { get; }
    public bool IsUsable => Value != 0 && Value != 0xFFFFFFFFU;
    public byte A => (byte)(Value >> 24);
    public byte B => (byte)(Value >> 16);
    public byte C => (byte)(Value >> 8);
    public byte D => (byte)Value;

    public static bool TryCreate(ReadOnlySpan<byte> bytes,
                                 out Ipv4Address address)
    {
        address = default;
        if (bytes.Length != 4) return false;
        address = new Ipv4Address(((uint)bytes[0] << 24) |
                                   ((uint)bytes[1] << 16) |
                                   ((uint)bytes[2] << 8) | bytes[3]);
        return true;
    }

    public void CopyTo(Span<byte> bytes)
    {
        if (bytes.Length < 4) return;
        bytes[0] = A;
        bytes[1] = B;
        bytes[2] = C;
        bytes[3] = D;
    }

    public bool Equals(Ipv4Address other) => Value == other.Value;
    public override bool Equals(object? obj) =>
        obj is Ipv4Address other && Equals(other);
    public override int GetHashCode() => (int)Value;
    public static bool operator ==(Ipv4Address left, Ipv4Address right) =>
        left.Value == right.Value;
    public static bool operator !=(Ipv4Address left, Ipv4Address right) =>
        left.Value != right.Value;
}

/* A copied snapshot.  MacAddress is the six-byte MAC encoded in the low 48
   bits, in wire order. */
public readonly struct NetworkStatus
{
    public NetworkStatus(bool linkReady, bool driverReady, bool configured,
                         bool dhcpBound, ulong macAddress,
                         Ipv4Address ipv4Address, Ipv4Address subnetMask,
                         Ipv4Address dnsServer)
    {
        LinkReady = linkReady;
        DriverReady = driverReady;
        Configured = configured;
        DhcpBound = dhcpBound;
        MacAddress = macAddress;
        Ipv4Address = ipv4Address;
        SubnetMask = subnetMask;
        DnsServer = dnsServer;
    }

    public bool LinkReady { get; }
    public bool DriverReady { get; }
    public bool Configured { get; }
    public bool DhcpBound { get; }
    public ulong MacAddress { get; }
    public Ipv4Address Ipv4Address { get; }
    public Ipv4Address SubnetMask { get; }
    public Ipv4Address DnsServer { get; }
}

internal enum ManagedNetworkServiceBackendResult : byte
{
    Started = 0,
    Success = 1,
    Busy = 2,
    InvalidArgument = 3,
    NotConfigured = 4,
    NoResource = 5,
    Failed = 6,
    Rejected = 7
}

internal enum ManagedNetworkServiceBackendEvent : byte
{
    None = 0,
    DnsResolved = 1,
    DnsNxDomain = 2,
    PingReply = 3,
    UdpReceived = 4,
    UdpReceiveOverflow = 5
}

/* The adapter is the only bridge from the public service to protocol
   implementation objects.  Host tests use a deterministic fake adapter;
   ordinary managed consumers never see this interface. */
internal interface IManagedNetworkServiceBackend
{
    bool IsAvailable { get; }
    NetworkStatus GetStatus();
    void SetRuntimeStatus(NetworkStatus status);
    ManagedNetworkServiceBackendResult BeginResolve(ReadOnlySpan<byte> name);
    bool TryGetResolved(out Ipv4Address address);
    bool Poll(out ManagedNetworkServiceBackendEvent serviceEvent);
    ManagedNetworkServiceBackendResult BeginPing(Ipv4Address destination);
    ManagedNetworkServiceBackendResult BindUdp(ushort port);
    ManagedNetworkServiceBackendResult UnregisterUdp(ushort port);
    ManagedNetworkServiceBackendResult SendUdp(Ipv4Address destination,
                                               ushort destinationPort,
                                               ushort sourcePort,
                                               ReadOnlySpan<byte> payload);
    bool Teardown();
}

public sealed class ManagedNetworkService
{
    public const int MaximumHostnameLength = 253;
    public const int MaximumUdpPayloadLength = 512;
    /* Must remain equal to ManagedUdpEndpointTable.Capacity (Phase 18 = 4).
       Keeping the public contract independent lets the service host suite test
       the API without linking protocol parser implementation files. */
    public const int UdpEndpointCapacity = 4;
    public const int ReceiveMessageCapacity = 1;

    private readonly IManagedNetworkServiceBackend _backend;
    private readonly byte[] _receiveSlot = new byte[MaximumUdpPayloadLength];
    private readonly ushort[] _boundPorts = new ushort[UdpEndpointCapacity];
    private int _boundPortCount;
    private bool _tornDown;
    private bool _runtimeStatusValid;
    private NetworkStatus _runtimeStatus;
    private NetworkResolutionState _resolutionState;
    private Ipv4Address _resolvedAddress;
    private NetworkPingState _pingState;
    private bool _receiveReady;
    private Ipv4Address _receiveSource;
    private Ipv4Address _receiveDestination;
    private ushort _receiveSourcePort;
    private ushort _receiveDestinationPort;
    private ushort _receiveLength;
    private uint _receiveOverflowCount;

    internal ManagedNetworkService(IManagedNetworkServiceBackend backend)
    {
        _backend = backend;
    }

    internal static ManagedNetworkService CreateForTests(
        IManagedNetworkServiceBackend backend)
    {
        return new ManagedNetworkService(backend);
    }

    public NetworkStatus GetStatus()
    {
        return _tornDown ? default :
            (_runtimeStatusValid ? _runtimeStatus : _backend.GetStatus());
    }

    public NetworkResolutionState ResolutionState => _resolutionState;
    public NetworkPingState PingState => _pingState;
    public bool HasReceivedUdp => _receiveReady;
    public uint ReceiveOverflowCount => _receiveOverflowCount;
    public int BoundEndpointCount => _boundPortCount;
    public NetworkOperationResult BeginResolveIpv4(ReadOnlySpan<byte> hostname)
    {
        if (_tornDown || !IsAvailable) return NetworkOperationResult.Unavailable;
        if (!TryValidateHostname(hostname)) return NetworkOperationResult.InvalidArgument;
        if (_resolutionState == NetworkResolutionState.Pending)
            return NetworkOperationResult.Busy;
        if (!GetStatus().Configured) return NetworkOperationResult.NotConfigured;

        ManagedNetworkServiceBackendResult result = _backend.BeginResolve(hostname);
        if (result == ManagedNetworkServiceBackendResult.Started)
        {
            _resolutionState = NetworkResolutionState.Pending;
            _resolvedAddress = default;
        }
        return Map(result);
    }

    public NetworkOperationResult Poll()
    {
        if (_tornDown || !IsAvailable) return NetworkOperationResult.Unavailable;
        if (!_backend.Poll(out ManagedNetworkServiceBackendEvent serviceEvent))
        {
            if (_resolutionState == NetworkResolutionState.Pending)
                _resolutionState = NetworkResolutionState.Failed;
            if (_pingState == NetworkPingState.Pending)
                _pingState = NetworkPingState.Failed;
            return NetworkOperationResult.Failed;
        }

        switch (serviceEvent)
        {
            case ManagedNetworkServiceBackendEvent.DnsResolved:
                if (_resolutionState == NetworkResolutionState.Pending)
                {
                    if (!_backend.TryGetResolved(out _resolvedAddress))
                    {
                        _resolutionState = NetworkResolutionState.Failed;
                        return NetworkOperationResult.Failed;
                    }
                    _resolutionState = NetworkResolutionState.Success;
                }
                break;
            case ManagedNetworkServiceBackendEvent.DnsNxDomain:
                if (_resolutionState == NetworkResolutionState.Pending)
                {
                    _resolvedAddress = default;
                    _resolutionState = NetworkResolutionState.NxDomain;
                }
                break;
            case ManagedNetworkServiceBackendEvent.PingReply:
                if (_pingState == NetworkPingState.Pending)
                    _pingState = NetworkPingState.Success;
                break;
            case ManagedNetworkServiceBackendEvent.UdpReceiveOverflow:
                _receiveOverflowCount++;
                return NetworkOperationResult.NoResource;
            case ManagedNetworkServiceBackendEvent.UdpReceived:
            case ManagedNetworkServiceBackendEvent.None:
                break;
        }

        return NetworkOperationResult.Success;
    }

    public bool TryGetResolvedIpv4(out Ipv4Address address)
    {
        address = _resolvedAddress;
        return _resolutionState == NetworkResolutionState.Success;
    }

    public NetworkOperationResult BeginPingIpv4(Ipv4Address destination)
    {
        if (_tornDown || !IsAvailable) return NetworkOperationResult.Unavailable;
        if (!destination.IsUsable) return NetworkOperationResult.InvalidArgument;
        if (_pingState == NetworkPingState.Pending) return NetworkOperationResult.Busy;
        if (!GetStatus().Configured) return NetworkOperationResult.NotConfigured;

        ManagedNetworkServiceBackendResult result = _backend.BeginPing(destination);
        if (result == ManagedNetworkServiceBackendResult.Started)
            _pingState = NetworkPingState.Pending;
        return Map(result);
    }

    public NetworkOperationResult BindUdpEndpoint(ushort port)
    {
        if (_tornDown || !IsAvailable) return NetworkOperationResult.Unavailable;
        if (port == 0) return NetworkOperationResult.InvalidArgument;
        if (_boundPortCount == UdpEndpointCapacity) return NetworkOperationResult.NoResource;
        for (int index = 0; index != _boundPortCount; ++index)
            if (_boundPorts[index] == port) return NetworkOperationResult.Busy;
        if (!GetStatus().Configured) return NetworkOperationResult.NotConfigured;

        ManagedNetworkServiceBackendResult result = _backend.BindUdp(port);
        if (result == ManagedNetworkServiceBackendResult.Started ||
            result == ManagedNetworkServiceBackendResult.Success)
            _boundPorts[_boundPortCount++] = port;
        return Map(result);
    }

    public NetworkOperationResult UnregisterUdpEndpoint(ushort port)
    {
        if (_tornDown || !IsAvailable) return NetworkOperationResult.Unavailable;
        int found = -1;
        for (int index = 0; index != _boundPortCount; ++index)
            if (_boundPorts[index] == port) { found = index; break; }
        if (found < 0) return NetworkOperationResult.Rejected;
        ManagedNetworkServiceBackendResult result = _backend.UnregisterUdp(port);
        if (result == ManagedNetworkServiceBackendResult.Success ||
            result == ManagedNetworkServiceBackendResult.Started)
        {
            for (int index = found; index + 1 != _boundPortCount; ++index)
                _boundPorts[index] = _boundPorts[index + 1];
            _boundPorts[--_boundPortCount] = 0;
        }
        return Map(result);
    }

    public NetworkOperationResult SendUdp(Ipv4Address destination,
                                          ushort destinationPort,
                                          ushort sourcePort,
                                          ReadOnlySpan<byte> payload)
    {
        if (_tornDown || !IsAvailable) return NetworkOperationResult.Unavailable;
        if (!destination.IsUsable || destinationPort == 0 || sourcePort == 0)
            return NetworkOperationResult.InvalidArgument;
        if (payload.Length > MaximumUdpPayloadLength)
            return NetworkOperationResult.InvalidArgument;
        if (!IsBound(sourcePort)) return NetworkOperationResult.Rejected;
        if (!GetStatus().Configured) return NetworkOperationResult.NotConfigured;
        return Map(_backend.SendUdp(destination, destinationPort, sourcePort, payload));
    }

    public bool TryReceiveUdp(Span<byte> destination, out Ipv4Address source,
                              out ushort sourcePort, out ushort destinationPort,
                              out int length)
    {
        source = _receiveSource;
        sourcePort = _receiveSourcePort;
        destinationPort = _receiveDestinationPort;
        length = _receiveLength;
        if (!_receiveReady || destination.Length < _receiveLength) return false;
        _receiveSlot.AsSpan(0, _receiveLength).CopyTo(destination);
        _receiveReady = false;
        _receiveLength = 0;
        return true;
    }

    public NetworkOperationResult Teardown()
    {
        if (_tornDown) return NetworkOperationResult.Success;
        bool result = _backend.Teardown();
        ClearForTeardown();
        _tornDown = true;
        return result ? NetworkOperationResult.Success : NetworkOperationResult.Failed;
    }

    internal bool TryCaptureReceivedUdp(Ipv4Address source,
                                        Ipv4Address destination,
                                        ushort sourcePort,
                                        ushort destinationPort,
                                        ReadOnlySpan<byte> payload)
    {
        if (_tornDown || !IsBound(destinationPort) || _receiveReady) return false;
        if (payload.Length > MaximumUdpPayloadLength) return false;
        payload.CopyTo(_receiveSlot);
        _receiveSource = source;
        _receiveDestination = destination;
        _receiveSourcePort = sourcePort;
        _receiveDestinationPort = destinationPort;
        _receiveLength = (ushort)payload.Length;
        _receiveReady = true;
        return true;
    }

    internal void BeginBoot()
    {
        _tornDown = false;
        _runtimeStatusValid = false;
        _runtimeStatus = default;
        _backend.SetRuntimeStatus(default);
        _resolutionState = NetworkResolutionState.Idle;
        _pingState = NetworkPingState.Idle;
        _resolvedAddress = default;
        _receiveReady = false;
        _receiveLength = 0;
        _boundPortCount = 0;
        _receiveOverflowCount = 0;
    }

    internal void OnProtocolTeardown()
    {
        ClearForTeardown();
        _tornDown = true;
    }

    internal void SetRuntimeStatus(NetworkStatus status)
    {
        _runtimeStatus = status;
        _runtimeStatusValid = true;
        _backend.SetRuntimeStatus(status);
    }

    internal IManagedNetworkServiceBackend Backend => _backend;

    private void ClearForTeardown()
    {
        _runtimeStatusValid = false;
        _runtimeStatus = default;
        _backend.SetRuntimeStatus(default);
        _resolutionState = NetworkResolutionState.Idle;
        _pingState = NetworkPingState.Idle;
        _resolvedAddress = default;
        _receiveReady = false;
        _receiveLength = 0;
        _receiveSource = default;
        _receiveDestination = default;
        _receiveSourcePort = 0;
        _receiveDestinationPort = 0;
        for (int index = 0; index != _boundPorts.Length; ++index) _boundPorts[index] = 0;
        _boundPortCount = 0;
    }

    private bool IsBound(ushort port)
    {
        for (int index = 0; index != _boundPortCount; ++index)
            if (_boundPorts[index] == port) return true;
        return false;
    }

    private bool IsAvailable => _runtimeStatusValid
        ? _runtimeStatus.LinkReady : _backend.IsAvailable;

    private static NetworkOperationResult Map(ManagedNetworkServiceBackendResult result)
    {
        return result switch
        {
            ManagedNetworkServiceBackendResult.Started => NetworkOperationResult.Started,
            ManagedNetworkServiceBackendResult.Success => NetworkOperationResult.Success,
            ManagedNetworkServiceBackendResult.Busy => NetworkOperationResult.Busy,
            ManagedNetworkServiceBackendResult.InvalidArgument => NetworkOperationResult.InvalidArgument,
            ManagedNetworkServiceBackendResult.NotConfigured => NetworkOperationResult.NotConfigured,
            ManagedNetworkServiceBackendResult.NoResource => NetworkOperationResult.NoResource,
            ManagedNetworkServiceBackendResult.Rejected => NetworkOperationResult.Rejected,
            _ => NetworkOperationResult.Failed
        };
    }

    private static bool TryValidateHostname(ReadOnlySpan<byte> hostname)
    {
        if (hostname.Length == 0 || hostname.Length > MaximumHostnameLength)
            return false;
        int labelLength = 0;
        for (int index = 0; index <= hostname.Length; ++index)
        {
            if (index != hostname.Length && hostname[index] != (byte)'.')
            {
                byte value = hostname[index];
                if (value < 0x21 || value > 0x7E) return false;
                labelLength++;
                if (labelLength > 63) return false;
                continue;
            }
            if (labelLength == 0) return false;
            labelLength = 0;
        }
        return true;
    }
}

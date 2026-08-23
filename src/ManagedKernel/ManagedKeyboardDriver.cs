using System;
using System.Runtime.InteropServices;

namespace GuideXOS.Net10.ManagedKernel;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct GxManagedKernelInputServicesV1
{
    internal const uint ExpectedSize = 88;
    internal const uint AbiVersionCurrent = 1;
    internal const uint ServiceVersionCurrent = 1;
    internal const uint ArchitectureX64 = 0x8664;
    internal const uint QueueCapacity = 8;
    internal const uint MaxDrain = 4;
    internal const ulong CapabilitySubscribe = 1UL << 0;
    internal const ulong CapabilityUnsubscribe = 1UL << 1;
    internal const ulong CapabilityDrain = 1UL << 2;
    internal const ulong CapabilityQueryStats = 1UL << 3;

    internal uint Size;
    internal uint AbiVersion;
    internal uint ServiceVersion;
    internal uint Architecture;
    internal ulong Capabilities;
    internal uint EventRecordSize;
    internal uint QueueCapacityValue;
    internal uint MaxDrainValue;
    internal uint Reserved0;
    internal ulong SubscribeAddress;
    internal ulong UnsubscribeAddress;
    internal ulong DrainAddress;
    internal ulong QueryStatsAddress;
    internal ulong Reserved1;
    internal ulong Reserved2;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct GxManagedKernelKeyboardPlatformDeviceV1
{
    internal const uint ExpectedSize = 32;
    internal const uint DeviceKindPlatformKeyboard = 3;
    internal const uint DeviceIdI8042 = 1;
    internal const uint Irq1 = 1;
    internal const uint ScancodeSet1 = 1;
    internal const ulong CapabilityRawScancode = 1UL << 0;
    internal const ulong CapabilityMakeBreak = 1UL << 1;
    internal const ulong CapabilityQueryStatus = 1UL << 2;

    internal uint Size;
    internal uint AbiVersion;
    internal uint DeviceKind;
    internal uint DeviceId;
    internal ulong Capabilities;
    internal uint Irq;
    internal uint ScancodeSet;
}

internal enum ManagedKeyboardDriverState : uint
{
    Uninitialized = 0,
    Initialized = 1,
    Started = 2,
    Stopped = 3,
    Disposed = 4
}

internal enum ManagedKeyboardSubscriptionState : uint
{
    NotSubscribed = 0,
    Subscribed = 1,
    Stopped = 2
}

internal unsafe sealed class ManagedKeyboardDriver
{
    internal const uint DriverId = 0x8202;
    internal const uint HistoryCapacity = 8;
    internal const uint StateBytes = 64;

    private readonly KernelArena _arena;
    private readonly KernelArenaAllocation _stateAllocation;
    private readonly GxManagedKernelKeyboardPlatformDeviceV1 _device;
    private readonly bool _ownsArena;
    private ManagedKeyboardDriverState _state;
    private ManagedKeyboardSubscriptionState _subscriptionState;
    private ulong _subscriptionId;
    private ulong _lastSequence;
    private uint _eventCount;
    private uint _makeCount;
    private uint _historyCount;
    private uint _historyWrite;
    private byte _lastScancode;
    private byte _lastMake;
    private byte _lastMakeScancode;

    private ManagedKeyboardDriver(
        KernelArena arena, in KernelArenaAllocation stateAllocation,
        in GxManagedKernelKeyboardPlatformDeviceV1 device, bool ownsArena)
    {
        _arena = arena;
        _stateAllocation = stateAllocation;
        _device = device;
        _ownsArena = ownsArena;
        _state = ManagedKeyboardDriverState.Uninitialized;
        _subscriptionState = ManagedKeyboardSubscriptionState.NotSubscribed;
    }

    internal ManagedKeyboardDriverState State => _state;
    internal ManagedKeyboardSubscriptionState SubscriptionState => _subscriptionState;
    internal uint DeviceKind => _device.DeviceKind;
    internal uint DeviceId => _device.DeviceId;
    internal uint Irq => _device.Irq;
    internal uint ScancodeSet => _device.ScancodeSet;
    internal ulong SubscriptionId => _subscriptionId;
    internal ulong LastSequence => _lastSequence;
    internal uint EventCount => _eventCount;
    internal uint MakeCount => _makeCount;
    internal byte LastScancode => _lastScancode;
    internal bool LastWasMake => _lastMake != 0;
    internal byte LastMakeScancode => _lastMakeScancode;
    internal KernelArenaMetrics Metrics => _arena.IsDestroyed
        ? default : _arena.GetMetrics();
    internal bool IsDestroyed => _state == ManagedKeyboardDriverState.Disposed;

    internal static ManagedKeyboardDriver? TryCreate(
        IKernelMemoryProvider provider,
        in GxManagedKernelKeyboardPlatformDeviceV1 device,
        KernelArena? sharedArena = null)
    {
        KernelArena? arena = null;
        KernelArenaAllocation stateAllocation = default;
        bool ownsArena = false;
        const ulong knownCapabilities =
            GxManagedKernelKeyboardPlatformDeviceV1.CapabilityRawScancode |
            GxManagedKernelKeyboardPlatformDeviceV1.CapabilityMakeBreak;
        if (provider == null || !provider.IsAvailable ||
            device.Size != GxManagedKernelKeyboardPlatformDeviceV1.ExpectedSize ||
            device.AbiVersion != GxManagedKernelInputServicesV1.AbiVersionCurrent ||
            device.DeviceKind != GxManagedKernelKeyboardPlatformDeviceV1.DeviceKindPlatformKeyboard ||
            device.DeviceId != GxManagedKernelKeyboardPlatformDeviceV1.DeviceIdI8042 ||
            device.Capabilities != knownCapabilities || device.Irq !=
                GxManagedKernelKeyboardPlatformDeviceV1.Irq1 ||
            device.ScancodeSet != GxManagedKernelKeyboardPlatformDeviceV1.ScancodeSet1)
        {
            return null;
        }
        if (sharedArena == null)
        {
            if (KernelArena.TryCreate(provider, 2, 2, 1, 2, 4, 64,
                                      out arena) != KernelArenaStatus.Ok ||
                arena == null)
            {
                return null;
            }
            ownsArena = true;
        }
        else
        {
            arena = sharedArena;
        }
        if (arena.TryAllocate(StateBytes, 8, out stateAllocation) !=
            KernelArenaStatus.Ok)
        {
            if (ownsArena) arena.Destroy();
            return null;
        }
        return new ManagedKeyboardDriver(arena, in stateAllocation, in device,
                                         ownsArena);
    }

    internal bool TryInitialize()
    {
        if (_state != ManagedKeyboardDriverState.Uninitialized ||
            _device.Capabilities !=
                (GxManagedKernelKeyboardPlatformDeviceV1.CapabilityRawScancode |
                 GxManagedKernelKeyboardPlatformDeviceV1.CapabilityMakeBreak))
        {
            return false;
        }
        _state = ManagedKeyboardDriverState.Initialized;
        _subscriptionState = ManagedKeyboardSubscriptionState.NotSubscribed;
        return true;
    }

    internal bool TryStart()
    {
        if (_state != ManagedKeyboardDriverState.Initialized) return false;
        _state = ManagedKeyboardDriverState.Started;
        _subscriptionState = ManagedKeyboardSubscriptionState.NotSubscribed;
        return true;
    }

    internal bool TrySubscribe(ManagedInterruptDispatcher dispatcher)
    {
        ulong token;
        if (_state != ManagedKeyboardDriverState.Started ||
            _subscriptionState != ManagedKeyboardSubscriptionState.NotSubscribed ||
            dispatcher == null ||
            !dispatcher.TrySubscribeInput(
                GxManagedKernelInterruptEventV1.EventTypeKeyboardScancode,
                _device.DeviceKind, _device.DeviceId, out token))
        {
            return false;
        }
        _subscriptionId = token;
        _subscriptionState = ManagedKeyboardSubscriptionState.Subscribed;
        return true;
    }

    internal bool TryUnsubscribe(ManagedInterruptDispatcher dispatcher)
    {
        if (_subscriptionState != ManagedKeyboardSubscriptionState.Subscribed ||
            dispatcher == null || !dispatcher.TryUnsubscribeInput()) return false;
        _subscriptionId = 0;
        _subscriptionState = ManagedKeyboardSubscriptionState.NotSubscribed;
        return true;
    }

    internal bool TryHandleScancode(in GxManagedKernelInterruptEventV1 value)
    {
        if (_state != ManagedKeyboardDriverState.Started ||
            _subscriptionState != ManagedKeyboardSubscriptionState.Subscribed ||
            value.EventType != GxManagedKernelInterruptEventV1.EventTypeKeyboardScancode ||
            value.DeviceKind != _device.DeviceKind || value.DeviceId != _device.DeviceId ||
            value.PayloadLength != 1 || value.Sequence == 0 ||
            (_eventCount != 0 && value.Sequence <= _lastSequence) ||
            (value.Status & (1U << 0)) == 0 ||
            (value.Status & (1U << 1)) == 0)
        {
            return false;
        }
        byte scancode = value.PayloadByte;
        byte make = (byte)((scancode & 0x80U) == 0 ? 1U : 0U);
        byte* history = (byte*)(nuint)_stateAllocation.VirtualAddress;
        history[_historyWrite] = scancode;
        history[HistoryCapacity + _historyWrite] = make;
        _historyWrite = (_historyWrite + 1U) % HistoryCapacity;
        if (_historyCount != HistoryCapacity) _historyCount++;
        _lastSequence = value.Sequence;
        _lastScancode = (byte)(scancode & 0x7FU);
        _lastMake = make;
        if (make != 0)
        {
            _makeCount++;
            _lastMakeScancode = _lastScancode;
        }
        _eventCount++;
        return true;
    }

    internal bool TryRunRuntimeArenaProof()
    {
        KernelArenaAllocation allocation;
        if (_state != ManagedKeyboardDriverState.Started ||
            _subscriptionState != ManagedKeyboardSubscriptionState.Subscribed ||
            _arena.TryAllocate(16, 8, out allocation) != KernelArenaStatus.Ok)
        {
            return false;
        }
        Span<byte> bytes = new Span<byte>(
            (void*)(nuint)allocation.VirtualAddress, 16);
        bytes.Fill(0xB6);
        bool valid = bytes[0] == 0xB6 && bytes[15] == 0xB6;
        valid = _arena.Free(in allocation) == KernelArenaStatus.Ok && valid;
        return valid;
    }

    internal bool TryStop()
    {
        if (_state != ManagedKeyboardDriverState.Started ||
            _subscriptionState == ManagedKeyboardSubscriptionState.Subscribed)
        {
            return false;
        }
        _state = ManagedKeyboardDriverState.Stopped;
        _subscriptionState = ManagedKeyboardSubscriptionState.Stopped;
        return true;
    }

    internal bool Destroy()
    {
        if (_state != ManagedKeyboardDriverState.Stopped ||
            _state == ManagedKeyboardDriverState.Disposed ||
            _arena.Free(in _stateAllocation) != KernelArenaStatus.Ok ||
            (_ownsArena && _arena.Destroy() != KernelArenaStatus.Ok))
        {
            return false;
        }
        _state = ManagedKeyboardDriverState.Disposed;
        _subscriptionState = ManagedKeyboardSubscriptionState.Stopped;
        return true;
    }
}

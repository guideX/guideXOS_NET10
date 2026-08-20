# ManagedKernel Phase 8: First Managed Hardware Driver

Phase 8 is the first guideXOS hardware driver whose driver policy and
operational logic execute in managed C#. The selected target is transmit-only
COM1, represented as a small platform device rather than as a PCI device.

## Candidate audit and selection

| Candidate | Discovery/access | DMA | Interrupts | MMIO/port I/O | Existing safe implementation | Observable effect | Rank |
|---|---|---:|---:|---|---|---|---:|
| COM1 serial transmit | Native COM1 initialization; fixed platform identity | No | No; bounded polling | Native port I/O only | Yes: `serial_init`, `serial_out8`, and the serial logger | QEMU `-serial file:` captures bytes | 1 |
| PCI configuration | Six native snapshot devices; CF8/CFC reads | No | No | Native port I/O only, read-only | Yes, but read-only and not a useful device operation | Configuration values only | 2 |
| Display | PCI inventory only; no safe BAR ownership | Unknown | Unknown | Would require MMIO/BAR work | No bounded write path | Not safely provable in Phase 8 | 3 |
| Storage/network | PCI inventory only | Likely | Likely or device-specific | BAR/MMIO and device initialization | No narrow operational driver | Not safely bounded | 4 |

COM1 is the least complex existing target: native guideXOS owns the UART and
its initialization, while the managed driver receives only transmit and
normalized readiness capabilities. Managed code never supplies `0x3F8` or
receives arbitrary port-I/O/MMIO access. The UART is already initialized by
native guideXOS; Phase 8 does not change baud, parity, stop bits, FIFO, modem
control, DMA, or interrupt state. Receive is intentionally omitted.

The physical UART mechanism is shared synchronously with the existing serial
logger. The public managed path is separate from `KernelLog`; only the trusted
native byte-transmit primitive is shared.

## Serial Services ABI v1

`GX_MANAGED_KERNEL_SERIAL_SERVICES_V1` is a separate, packed, x64,
Microsoft-x64-call-convention service table. Reserved fields must be zero.

| Structure | Size | Critical offsets |
|---|---:|---|
| `GX_MANAGED_KERNEL_SERIAL_PLATFORM_DEVICE_V1` | 32 | `Capabilities=16`, `ComIndex=24`, `Reserved=28` |
| `GX_MANAGED_KERNEL_SERIAL_SERVICES_V1` | 72 | `Capabilities=16`, `DeviceKind=24`, `DeviceId=28`, `ComIndex=32`, `MaxTransmitBytes=36`, `TransmitAddress=40`, `QueryStatusAddress=48`, `Reserved0=56`, `Reserved1=64` |
| `GX_MANAGED_KERNEL_SERIAL_STATUS_V1` | 32 | `Status=8`, `Capabilities=16` |

Capabilities are `0x1` bounded transmit and `0x2` normalized status query.
The native-authoritative identity is `DeviceKind=2` (platform serial),
`DeviceId=1` (COM1), and `ComIndex=1`. The service reports
`Architecture=0x8664`, `MaxTransmitBytes=1024`, and nonzero callback
addresses. No raw hardware address crosses the ABI.

The callbacks are:

```c
uint32_t GX_MANAGED_KERNEL_SERIAL_TRANSMIT_ENTRY(
    uint32_t device_id, uintptr_t buffer_address,
    uint32_t byte_length, uint32_t flags);

uint32_t GX_MANAGED_KERNEL_SERIAL_QUERY_STATUS_ENTRY(
    uint32_t requested_abi_version, uint32_t device_id,
    uintptr_t result_address, uintptr_t result_capacity);
```

Transmit accepts `flags=0`, an explicit nonzero byte length up to 1024, the
known COM1 device ID, and a range-validated buffer. Each byte has at most 4096
ready polls. An unavailable transmitter returns `GX_MANAGED_TIMEOUT` (11),
increments the native timeout counter, and never spins indefinitely. A
successful complete message increments the native service success counter.
The status callback reports device-present and transmitter-ready bits and uses
the same bounded wait.

## Managed driver and binding

`ManagedSerialDriver` has driver ID `0x8201` and explicit states:

```text
Uninitialized -> Initialized -> Started -> Stopped -> Disposed
```

Invalid transitions are rejected: initialization and start are one-shot,
writes require `Started`, stop requires `Started`, and destruction requires
`Stopped`. A small platform descriptor is validated against the native service
table before installation. The subsystem performs one service install, rejects
second installation, binds one operational serial driver by exact identity,
and leaves the PCI inventory/driver registry unchanged as additive Phase 7
state.

The driver creates a `KernelArena` with two initial pages, a two-page maximum
backing policy, one backing chunk, four live allocations, and 64-byte
alignment. It allocates a 64-byte persistent state record and a 1024-byte
unmanaged staging buffer. `TryWrite(ReadOnlySpan<byte>)` copies into that
arena-backed staging buffer and invokes the synchronous native callback before
returning. No native callback retains a managed pointer or runs asynchronously.

The controlled accounting instance initializes, starts, stops, destroys, and
restores the exact ManagedKernel-owned arena/page/VM ledger baseline. A second
instance remains operational for the rest of the boot.

## Independent proof path

The proof marker is raw serial data, not a `KernelLog` message:

```text
ManagedSerialDriver -> Serial Services v1 -> native COM1 transmit -> QEMU serial file
MANAGED_SERIAL_DRIVER_TX_FROM_CSHARP
MANAGED_SERIAL_DRIVER_TX_AFTER_RUNTIME
```

The first marker is emitted by the managed driver only after initialization
and start. The driver then exercises monotonic time, Phase 4 page memory,
managed allocation/GC, the Phase 6 inventory, Phase 7 binding, and a read-only
PCI configuration query. It transmits the second raw marker through the same
driver instance. QEMU acceptance requires each marker exactly once, the native
service success count exactly twice, and `MANAGED_KERNEL_PHASE8_PASS` exactly
once. The native loader does not emit either raw driver marker.

## Negative paths and limitations

Native and managed host tests cover null and undersized tables, unsupported
ABI, wrong architecture/identity, missing capabilities, null callbacks,
reserved-field mutations, duplicate installation, invalid state transitions,
null/oversized/overflowing buffers, wrong device ID, unsupported flags, and
bounded transmitter/status timeouts. The QEMU path also proves the existing
Phase 2–7 markers, six-device PCI inventory, read-only PCI comparison, runtime
survival, and no task-owned QEMU remains after cleanup.

Phase 8 is deliberately synchronous and transmit-only. It does not add
arbitrary port I/O, generic MMIO, BAR mapping, DMA, bus mastering, interrupts,
network/storage/display ownership, dynamic loading, hotplug, or a general HAL.
Native guideXOS continues to own raw hardware access; managed drivers receive
only capability-specific native mechanisms.

ManagedSerialDriver is not implemented by forwarding to KernelLog. It owns a
separate Serial Services path that shares only the underlying trusted native
UART mechanism.

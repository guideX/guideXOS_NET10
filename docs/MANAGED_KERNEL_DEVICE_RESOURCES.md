# ManagedKernel Phase 12: Device Resources and Ownership

Phase 12 completed bounded resource discovery for the resources that the
guideXOS bootstrap could identify authoritatively without changing hardware
state. The native side publishes an immutable snapshot; ManagedKernel provides
bounded queries and ownership claims over that snapshot. The accepted Phase 12
classification was:

```text
PHASE 12 RESOURCE DISCOVERY COMPLETE — MMIO ACCESS DEFERRED
```

This remains the historical Phase 12 boundary. Phase 13 adds a separate
bounded, uncacheable, read-only MMIO capability for an already-published and
claimed resource; it does not add arbitrary physical mapping. See
`MANAGED_KERNEL_MMIO_MAPPING.md` for the current mapping architecture.

## Substrate audit

The current loader and QEMU profile provide the following authoritative facts:

- PCI configuration access is the legacy CF8/CFC mechanism, read-only, on
  segment 0; the scanner covers buses 0-255, devices 0-31, and functions 0-7
  when the multifunction bit is set.
- The Q35, 128 MiB, single-threaded-TCG profile exposes six present PCI
  functions to the Phase 6 identity scan. The first selected descriptor is
  BDF `0000:00:00.0`, vendor `0x8086`, device `0x29C0`, class
  `0x060000`; the complete identity/class snapshot remains native-owned and
  is queried through the Phase 6 ABI.
- The existing PCI path does not retain a firmware-assigned PCI resource
  descriptor source. BAR sizing by writing all ones and restoring the BAR is
  outside the read-only discovery boundary and is not performed.
- At the Phase 12 boundary, the VM substrate supplied identity/page-table
  mappings and allocation ledgers, but no generic physical-to-virtual MMIO
  mapping API and no proven PAT/MTRR or equivalent cache-type policy service.
  Phase 13 closes this specific gap with a dedicated native-owned virtual
  window and a fail-closed UC policy.
- Existing COM1 and i8042 drivers retain native-authoritative port-I/O access.
  Managed code receives resource facts and claims; it does not receive raw
  PCI configuration-write authority, arbitrary physical mapping authority, or
  a generic MMIO read/write primitive.

The final Phase 12 audit logged the six normalized PCI functions below. The
`BAR_COUNT` column is the Phase 6 published resource count and is zero for all
six; it is not a claim that the underlying hardware has no BAR registers.

| BDF | Vendor | Device | Class/subclass/prog-if | Header | Published BAR/resource count |
|---|---:|---:|---|---:|---:|
| `0000:00:00.0` | `0x8086` | `0x29C0` | `06/00/00` | `0x00` | 0 |
| `0000:00:01.0` | `0x1234` | `0x1111` | `03/00/00` | `0x00` | 0 |
| `0000:00:02.0` | `0x8086` | `0x10D3` | `02/00/00` | `0x00` | 0 |
| `0000:00:1F.0` | `0x8086` | `0x2918` | `06/01/00` | `0x80` | 0 |
| `0000:00:1F.2` | `0x8086` | `0x2922` | `01/06/01` | `0x80` | 0 |
| `0000:00:1F.3` | `0x8086` | `0x2930` | `0C/05/00` | `0x80` | 0 |

Raw BAR values, BAR widths, and prefetchability are intentionally not
reported by this identity-only scan. The Phase 12 decoder is host-tested on
synthetic raw values, but no active QEMU device is probed or reprogrammed.

## Native publication ABI v1

The declarations are in
`src/Gate4Harness/managed_kernel_abi.h`; the native construction and validation
are in `src/Gate4Harness/managed_kernel_device_resources.c` and `.h`.
Native static assertions and managed layout checks cover these fixed records:

| Record | Size | Purpose |
|---|---:|---|
| `GX_MANAGED_KERNEL_DEVICE_RESOURCE_SUMMARY_V1` | 40 bytes | Version, architecture, count, claim bound, capabilities. |
| `GX_MANAGED_KERNEL_DEVICE_RESOURCE_V1` | 80 bytes | Immutable resource identity, owner, type, flags, range, and alignment. |
| `GX_MANAGED_KERNEL_DEVICE_RESOURCE_PUBLICATION_V1` | 48 bytes | Native addresses and exact byte lengths for the summary and descriptor snapshot. |

The service is ABI v1, service v1, x64 (`0x8664`), bounded to 64 descriptors
and 16 simultaneous claims. Capabilities explicitly advertise summary,
descriptor, immutable-publication, and claim-policy support. Every descriptor
has a nonzero stable resource ID, an owner kind/id, a resource index, a
nonzero power-of-two alignment, a nonzero bounded range, and zero reserved
fields. Duplicate IDs and overlapping ranges of the same resource type are
rejected.

## Published platform resources

The Phase 12 native snapshot contains three platform I/O-port descriptors:

| Resource ID | Owner | Index | Base | Length | Type | Access |
|---|---|---:|---:|---:|---|---|
| `0x47584F5301000001` | COM1, device 1 | 0 | `0x3F8` | 8 | I/O port | readable |
| `0x47584F5301000002` | i8042, device 1 | 0 | `0x60` | 1 | I/O port | readable |
| `0x47584F5301000003` | i8042, device 1 | 1 | `0x64` | 1 | I/O port | readable |

These are platform-authoritative records, not guessed PCI BARs. Their native
IDs and complete 80-byte descriptors are compared byte-for-byte by the UEFI
harness after managed installation and indexed queries.

## PCI BAR decoder boundary

The native host-tested decoder is side-effect-free. It understands I/O BARs,
32-bit memory BARs, 64-bit memory BARs, and the prefetchable bit. It reports
unimplemented zero/all-ones masks, rejects reserved memory types, malformed or
non-power-of-two sizes, and catches base-plus-length overflow. It is a decoder
for already available raw values; it does not read or write PCI configuration
space and does not publish a BAR unless an authoritative assigned value is
available through a future safe source.

Therefore, at the Phase 12 boundary, Phase 6 PCI descriptors continued to report
`ResourceCount == 0` in the Phase 12 identity snapshot, while Phase 12
published the three independently authoritative COM1/i8042 platform
resources. Phase 13 separately publishes the one-page, firmware-authorized
MMIO representation for `0000:00:02.0` after its side-effect-free BAR decode.

## Managed catalog and ownership

The boot-time catalog is implemented by
`src/ManagedKernel/ManagedDeviceResourceRuntimeCatalog.cs`. Native descriptor
bytes remain authoritative and immutable. The runtime catalog retains only a
bounded descriptor address/count, a catalog identity, and 64 fixed claim slots
in static storage; it does not allocate a managed catalog object and does not
copy or map arbitrary physical memory. Resource handles are value types carrying
the descriptor value and catalog identity, so stale or foreign handles cannot
claim a resource.

The managed proof covers:

- query-before-install, null, bad-size, unsupported-ABI, out-of-range, and
  sentinel-preservation negatives;
- native-to-managed byte-for-byte descriptor equality;
- serial-driver claim and release;
- rejection of a serial-driver claim against the keyboard owner;
- keyboard-driver claim and release;
- duplicate/active-claim and stale-handle behavior in the managed host vector;
- invariant validation across runtime activity and an explicit `GC.Collect()`;
- release, claim accounting restoration, and one final Phase 12 pass marker.

The existing instance catalog in
`src/ManagedKernel/ManagedDeviceResourceCatalog.cs` is retained for managed
host vectors that exercise construction, malformed inputs, stale handles, and
teardown independently of the NativeAOT boot path.

## Access classification

| Level | Result | Evidence |
|---|---|---|
| A — authoritative discovery | Complete | Native platform snapshot, fixed ABI, validation, six-device PCI identity audit, and three fresh QEMU boots. |
| B — managed ownership/capability | Complete | Bounded queries, owner-checked claims, wrong-owner rejection, GC survival, release, and accounting markers. |
| C — safe generic MMIO execution | Deferred in Phase 12 | Phase 13 now provides only the bounded UC mapping proof described in `MANAGED_KERNEL_MMIO_MAPPING.md`; arbitrary mapping and managed PCI configuration writes remain unsupported. |

The deliberate next step for Level C in the Phase 12 record was to add a native
cache-policy and mapping substrate first, then publish only assigned, validated
ranges with an explicit access capability. Phase 13 supplies that bounded
substrate; the remaining unsupported operations are listed in
`MANAGED_KERNEL_MMIO_MAPPING.md`.

## Verification record

Host vectors:

```powershell
.\tools\Run-ManagedKernelDeviceResourcesHostTests.ps1
.\tools\Run-ManagedKernelDeviceInventoryManagedHostTests.ps1
```

Both passed in the Phase 12 implementation. The final ManagedKernel payload
is 967,168 bytes with SHA-256
`933FA2781EFFFD574D9A286CA9FE0401A96EDF40A23C5E6065CCA243BD49190C`.
The staged Phase 12 UEFI harness is
`artifacts/phase12-final-gate4-v3/ESP/EFI/BOOT/BOOTX64.EFI` with SHA-256
`396A52D7A97E8F786846345FE0137F7B106BE53DF2189AABED29184C18B89524`.

Three fresh Phase 12 boots passed with
`tools/Run-ManagedKernelPhase12FreshBoots.ps1`; evidence is under
`artifacts/phase12-final-evidence-v4`. The run serial SHA-256 values are:

| Run | Serial SHA-256 |
|---:|---|
| 1 | `574713824D2E4175B63A9A6130D331814941A9D5B0B07C94DCD067213321C053` |
| 2 | `077E3AC4288314073257CB8A7430C9109E94BB67D1EF50E8AE6AC7523BFA65CE` |
| 3 | `C68462EA8694E291167AE60DE3EA7E927C250A3A48EF78038D88E7455561492E` |

Three separate ManagedEntryProbe control boots also passed with the final
shared loader guard: control payload SHA-256
`2F66A6E85B61C48E87238EC972C9681B15084340C6F3C86F2FCA5EDC7FC3F837`, loader
SHA-256 `7CB08172685E22ABC1DED29AD3FAEF24A31A54C9916D4FC539DE6FD2ACBA522A`,
and QEMU 11.0.0. No commit, push, merge, rebase, amend, branch switch, or
stash operation was performed.

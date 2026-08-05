# Resumable x64 exception-context foundation

This document describes the bounded trap/context substrate. The ordered
vectored-handler registry and exact NativeAOT registration route are
documented separately in
[`VECTORED_EXCEPTION_HANDLER_REGISTRY.md`](VECTORED_EXCEPTION_HANDLER_REGISTRY.md).
This substrate still does not implement exception raising, unwinding, XState,
or generalized managed dispatch.

## Existing entry audit

The original `gate4_loader.c` used one naked common entry and two stub macros:

* `GX_FAULT_NO_ERROR(n)` pushed a synthetic zero error code, then pushed the
  vector number.
* `GX_FAULT_WITH_ERROR(n)` relied on the CPU-pushed error code, then pushed the
  vector number.

The baseline macro assignment marked vectors 8, 10, 11, 12, 13, 14, 20, and
21 as CPU-error vectors. Vectors 0 through 7, 9, 15 through 19, and 22
through 31 used synthetic zero. The new stubs correct that classification for
the x64 architecture: CPU-pushed error codes are used by 8, 10, 11, 12, 13,
14, 17, 21, 29, and 30; all other vectors use the synthetic zero.

On entry to the original `fault_common`, with `R0` denoting the current stack
pointer, the exact normalized prefix was:

| Offset | Contents |
|---:|---|
| `R0+0x00` | vector pushed by the stub |
| `R0+0x08` | CPU error code or synthetic zero |
| `R0+0x10` | interrupted RIP |
| `R0+0x18` | interrupted CS |
| `R0+0x20` | interrupted RFLAGS |
| `R0+0x28` | old RSP when the physical stack pair is present; otherwise post-`iretq` stack position |
| `R0+0x30` | old SS when the physical stack pair is present; otherwise ordinary interrupted-stack contents |

For a privilege transition, the CPU additionally pushes interrupted RSP at
`R0+0x28` and SS at `R0+0x30`; the active CPL0 firmware/QEMU entry showed the
same physical pair. The existing code did not distinguish those layouts. It
passed the raw pointer in RCX, aligned RSP down to a 16-byte
boundary, reserved 32 bytes of Microsoft shadow space, and called
`fault_handler`; all volatile registers were otherwise available only as
whatever values happened to survive the entry. It never returned through
`iretq`; it disabled interrupts and halted.

The current UEFI application executes in CPL0 with a flat x64 code/data model.
The installed IDT gates use selector `CS`, interrupt-gate attributes `0x8E`,
IST zero, and no user-mode transition. The loader copies the firmware IDT,
replaces vectors 0 through 31, and restores the saved IDT after the ordinary
managed call. Firmware timer/IRQ entries outside that range remain copied.
The previous path had no nested-dispatch guard; a fault during serial logging
could fault again while the same terminal path was active.

## New raw assembly frame

`exception_entry.S` retains the normalized `[vector, error, CPU frame]` prefix.
It then saves all GPRs before C:

```text
lower addresses
  frame + 0x000 .. frame + 0x0DF   GXOS_X64_TRAP_FRAME
  frame + 0x0E0 .. frame + 0x157   saved RAX, RBX, RCX, RDX, RSI, RDI, RBP,
                                   R8, R9, R10, R11, R12, R13, R14, R15
  frame + 0x158                     vector
  frame + 0x160                     error code
  frame + 0x168                     interrupted RIP
  frame + 0x170                     interrupted CS
  frame + 0x178                     interrupted RFLAGS
  frame + 0x180 / +0x188           interrupted RSP/SS when the physical pair is present
high addresses
```

The saved-register block is 15 qwords (120 bytes); the normalized C frame is
224 bytes. Architecturally a same-CPL exception need not push RSP/SS, but the
active firmware/QEMU entry was measured to contain an old RSP/SS pair even
though the captured CS was `0x38` (RPL0): saved RSP was at `raw+0x28` and SS at
`raw+0x30`. The assembly detects that physical pair (canonical RSP and the
current SS selector) and marks it `GXOS_TRAP_ENTRY_CPU_STACK_FRAME`. If that
pair is absent, it derives same-CPL interrupted RSP as the post-`iretq` stack
position (`raw + 0x28`) and marks it `GXOS_TRAP_ENTRY_DERIVED_RSP`. A privilege
transition also uses CPU-pushed RSP/SS.

Error source is separately marked as CPU-pushed or synthetic. C calls use a
16-byte-aligned call-site RSP, 32-byte Microsoft shadow space, and an extra
private save slot. `cld` is issued before every C dispatch call. Interrupts
remain disabled inside the interrupt gate and the restored RFLAGS controls
the interrupted code's post-`iretq` state.

The continuation path writes the approved RIP/RFLAGS fields back to the CPU
frame, writes RSP/SS when a privilege-transition frame exists, restores all
GPRs, removes the vector/error prefix, and executes `iretq`. For the current
CPL0 proof the original stack pointer is unchanged, so same-CPL `iretq` leaves
the interrupted stack exactly where the CPU expects it.

## Normalized C trap frame

`GXOS_X64_TRAP_FRAME` is 0xE0 bytes. Its GPR/control offsets are:

| Offset | Field |
|---:|---|
| `0x000` | vector |
| `0x008` | error code |
| `0x010` | entry flags |
| `0x020`..`0x090` | RAX, RBX, RCX, RDX, RSI, RDI, RBP, R8..R15 |
| `0x098` | interrupted RIP |
| `0x0A0` | CS |
| `0x0A8` | RFLAGS |
| `0x0B0` | interrupted RSP, derived or CPU-pushed |
| `0x0B8` | SS |
| `0x0C0` | CR2 |
| `0x0C8` | raw normalized frame pointer |

The header contains compile-time `sizeof` and `offsetof` assertions for all
assembly-consumed offsets and key fields.

## Bounded compatibility view

`GXOS_EXCEPTION_POINTERS_COMPAT` is 0x10 bytes: exception-record pointer at
0x0 and context pointer at 0x8. The exception record is 0x98 bytes and retains
the code, flags, nested record, address, parameter count, and 15 information
slots. The bounded context is 0x100 bytes and stops at RIP; it places RCX at
0x80, RDX at 0x88, RSP at 0x98, and RIP at 0xF8. It also represents the other
GPRs and EFLAGS consistently. Its flags are `0x00100003` (AMD64 control plus
integer state only); no XState data is fabricated.

## Breakpoint proof

The translator recognizes only vector 3 and maps it to `0x80000003`. It
examines the captured RIP and the controlled probe bytes: if RIP is the byte
after `INT3` and the preceding byte is `0xCC`, the exception address is RIP-1;
if RIP is the `INT3` byte itself, the exception address is RIP. The QEMU proof
therefore validates actual CPU behavior instead of assuming it.

The internal callback is Microsoft x64 ABI and is called only by the harness.
It validates the exception pointers, breakpoint code/address, and incoming
RCX/RDX/RSP/RIP, changes RCX/RDX/RIP to approved values, and returns
`EXCEPTION_CONTINUE_EXECUTION` (`0xFFFFFFFF`). Context changes are checked for
canonical addresses, approved executable range, proven stack bounds, and a
strict RFLAGS mask. Any other result, unsupported vector, malformed frame, or
unsafe change goes through the existing visible fatal path. A dispatch already
in progress is terminal and never recursively invokes the synthetic callback.

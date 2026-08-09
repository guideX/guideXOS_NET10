/* Give the unresolved-import C handler a real Microsoft x64 call frame. */
.text
.p2align 4
.globl gxos_import_failfast_entry
gxos_import_failfast_entry:
    /*
     * At entry RSP points at the payload return address.  Keep the captured
     * register block at entry-RSP-0x20, below the handler's return address and
     * 32-byte shadow space.  Its [4], [9], and [10] entries therefore remain
     * the payload return address and bounded stack arguments.
     */
    subq    $0x60, %rsp
    movq    %r9, 0x40(%rsp)
    movq    %r8, 0x48(%rsp)
    movq    %rdx, 0x50(%rsp)
    movq    %rcx, 0x58(%rsp)
    leaq    0x40(%rsp), %rdx
    movq    %r10, %rcx
    call    import_failfast
    ud2

/* Capture the true Microsoft x64 import-call stack before entering C. */
.text
.p2align 4
.globl gxos_create_thread_entry
gxos_create_thread_entry:
    /* Entry RSP points at the import call's return address. */
    movq    %rsp, %r10
    /* Keep the C callee's 32-byte shadow space and three stack arguments. */
    subq    $56, %rsp
    /* Original [RSP+0x28] and [RSP+0x30] are CreateThread arguments 5/6. */
    movq    96(%rsp), %rax
    movq    %rax, 32(%rsp)
    movq    104(%rsp), %rax
    movq    %rax, 40(%rsp)
    /* Seventh C argument: the original import-call entry RSP. */
    movq    %r10, 48(%rsp)
    call    gxos_create_thread_platform_impl
    addq    $56, %rsp
    ret

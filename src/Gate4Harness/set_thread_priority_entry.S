/* Capture the true Microsoft x64 import-call ABI before entering C. */
.text
.p2align 4
.globl gxos_set_thread_priority_entry
gxos_set_thread_priority_entry:
    /* Entry RSP points at the import call's return address. */
    movq    %rsp, %r10
    /* Preserve original R8/R9/RDX in the caller-owned shadow space. */
    movq    %r8, 8(%rsp)
    movq    %r9, 16(%rsp)
    movq    %rdx, %r11
    /* Keep the C callee's 32-byte shadow space and two stack arguments. */
    subq    $56, %rsp
    /* C arguments 3/4 are the original entry RSP and original R8. */
    movq    %r10, %r8
    movq    64(%rsp), %r9
    /* C arguments 5/6 are original R9 and original RDX. */
    movq    72(%rsp), %rax
    movq    %rax, 32(%rsp)
    movq    %r11, 40(%rsp)
    call    gxos_set_thread_priority_platform_impl
    addq    $56, %rsp
    ret

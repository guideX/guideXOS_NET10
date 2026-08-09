/* Capture the true Microsoft x64 import-call ABI for ResumeThread. */
.text
.p2align 4
.globl gxos_resume_thread_entry
gxos_resume_thread_entry:
    /* Entry RSP points at the import call's return address. */
    movq    %rsp, %r10
    /* Preserve incidental register values without treating them as API args. */
    movq    %rdx, 8(%rsp)
    movq    %r8, 16(%rsp)
    movq    %r9, 24(%rsp)
    /* Keep the C callee's 32-byte shadow space and one extra argument. */
    subq    $56, %rsp
    /* C arguments 2/3/4 are the original entry RSP and incidental RDX/R8. */
    movq    %r10, %rdx
    movq    64(%rsp), %r8
    movq    72(%rsp), %r9
    /* C argument 5 is the incidental original R9. */
    movq    80(%rsp), %rax
    movq    %rax, 32(%rsp)
    call    gxos_resume_thread_platform_impl
    addq    $56, %rsp
    ret

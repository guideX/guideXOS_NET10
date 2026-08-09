/* Capture the exact three-register import ABI plus incidental R9. */
.text
.p2align 4
.globl gxos_is_process_in_job_entry
gxos_is_process_in_job_entry:
    /* Entry RSP points at the import call's return address. */
    movq    %rsp, %r10
    movq    (%r10), %rax
    /* Keep the C callee's 32-byte shadow space and two stack arguments. */
    subq    $48, %rsp
    /* C argument 4 is the original R9; argument 5 is the import return. */
    /* R9 remains live as argument 4; argument 5 is at the first stack slot. */
    movq    %rax, 32(%rsp)
    call    platform_is_process_in_job
    addq    $48, %rsp
    ret

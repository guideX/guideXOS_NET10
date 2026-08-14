/* Capture the Microsoft x64 WriteFile arguments, including stack arg 5. */
.text
.p2align 4
.globl gxos_write_file_entry
gxos_write_file_entry:
    /* At entry RSP points at the payload return address. */
    subq    $0x60, %rsp
    movq    %rcx, 0x00(%rsp)      /* hFile */
    movq    %rdx, 0x08(%rsp)      /* lpBuffer */
    movq    %r8,  0x10(%rsp)      /* nNumberOfBytesToWrite */
    movq    %r9,  0x18(%rsp)      /* lpNumberOfBytesWritten */
    movq    0x88(%rsp), %rax      /* lpOverlapped: entry RSP + 0x28 */
    movq    %rax, 0x20(%rsp)
    movq    0x60(%rsp), %rax      /* original payload return address */
    movq    %rax, 0x28(%rsp)
    movq    %rsp, %rcx
    call    gxos_write_file_import
    addq    $0x60, %rsp
    ret

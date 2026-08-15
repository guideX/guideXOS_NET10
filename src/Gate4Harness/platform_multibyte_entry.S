/* Capture the complete Microsoft x64 MultiByteToWideChar import frame. */
.text
.p2align 4
.globl gxos_multibyte_to_wide_char_entry
gxos_multibyte_to_wide_char_entry:
    /* Entry RSP points at the payload return address. */
    subq    $0x90, %rsp
    movl    %ecx, 0x30(%rsp)       /* CodePage */
    movl    %edx, 0x34(%rsp)       /* dwFlags */
    movq    %r8,  0x38(%rsp)       /* lpMultiByteStr */
    movq    %r9,  0x40(%rsp)       /* raw cbMultiByte */
    movq    0xb8(%rsp), %rax       /* entry RSP + 0x28: lpWideCharStr */
    movq    %rax, 0x48(%rsp)
    movq    0xc0(%rsp), %rax       /* entry RSP + 0x30: raw cchWideChar */
    movq    %rax, 0x50(%rsp)
    movq    0x90(%rsp), %rax       /* original payload return address */
    movq    %rax, 0x58(%rsp)
    leaq    0x30(%rsp), %rcx
    call    gxos_multibyte_import
    addq    $0x90, %rsp
    ret

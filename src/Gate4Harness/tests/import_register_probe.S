.text
.p2align 4
.globl gxos_import_register_probe_entry
gxos_import_register_probe_entry:
    movq    gxos_import_register_probe_destination(%rip), %rax
    movq    %rcx, 0(%rax)
    movq    %rdx, 8(%rax)
    movq    %r8, 16(%rax)
    movq    %r9, 24(%rax)
    xorl    %eax, %eax
    ret

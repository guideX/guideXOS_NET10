.text
.globl gxos_platform_virtual_query_capture
.extern platform_virtual_query
.extern gxos_virtual_query_entry_rcx
.extern gxos_virtual_query_entry_rdx
.extern gxos_virtual_query_entry_r8
.extern gxos_virtual_query_entry_r9
.extern gxos_virtual_query_entry_rsp
.extern gxos_virtual_query_entry_stack_arg4
.extern gxos_virtual_query_entry_stack_arg5

gxos_platform_virtual_query_capture:
    movq %rcx, gxos_virtual_query_entry_rcx(%rip)
    movq %rdx, gxos_virtual_query_entry_rdx(%rip)
    movq %r8, gxos_virtual_query_entry_r8(%rip)
    movq %r9, gxos_virtual_query_entry_r9(%rip)
    movq %rsp, gxos_virtual_query_entry_rsp(%rip)
    movq 0x20(%rsp), %rax
    movq %rax, gxos_virtual_query_entry_stack_arg4(%rip)
    movq 0x28(%rsp), %rax
    movq %rax, gxos_virtual_query_entry_stack_arg5(%rip)
    jmp platform_virtual_query

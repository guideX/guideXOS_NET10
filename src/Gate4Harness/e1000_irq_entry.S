.text

.extern gxos_managed_kernel_e1000_irq_capture

.globl gxos_managed_kernel_e1000_irq_entry
gxos_managed_kernel_e1000_irq_entry:
    /* MSI enters the fixed native vector. Capture only the device cause and
       queue a deferred record; managed code never runs on this stack. */
    pushq %r15
    pushq %r14
    pushq %r13
    pushq %r12
    pushq %r11
    pushq %r10
    pushq %r9
    pushq %r8
    pushq %rbp
    pushq %rdi
    pushq %rsi
    pushq %rdx
    pushq %rcx
    pushq %rbx
    pushq %rax
    movq %rsp, %r12
    andq $-16, %rsp
    subq $32, %rsp
    subq $512, %rsp
    fxsave64 (%rsp)
    call gxos_managed_kernel_e1000_irq_capture
    fxrstor64 (%rsp)
    addq $512, %rsp
    movq %r12, %rsp
    popq %rax
    popq %rbx
    popq %rcx
    popq %rdx
    popq %rsi
    popq %rdi
    popq %rbp
    popq %r8
    popq %r9
    popq %r10
    popq %r11
    popq %r12
    popq %r13
    popq %r14
    popq %r15
    iretq

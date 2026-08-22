.text

.extern gxos_managed_kernel_serial_irq_capture

.globl gxos_managed_kernel_serial_irq_entry
gxos_managed_kernel_serial_irq_entry:
    /* IRQ4 enters with IF cleared by the interrupt gate. No managed call is
       made here; the C routine only captures bounded UART state. */
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
    movq %rsp, %r11
    andq $-16, %rsp
    subq $32, %rsp
    call gxos_managed_kernel_serial_irq_capture
    movq %r11, %rsp
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

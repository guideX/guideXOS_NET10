.text

.equ GXOS_TRAP_FRAME_SIZE, 224
.equ GXOS_TRAP_VECTOR, 0
.equ GXOS_TRAP_ERROR, 8
.equ GXOS_TRAP_FLAGS, 16
.equ GXOS_TRAP_RAX, 32
.equ GXOS_TRAP_RBX, 40
.equ GXOS_TRAP_RCX, 48
.equ GXOS_TRAP_RDX, 56
.equ GXOS_TRAP_RSI, 64
.equ GXOS_TRAP_RDI, 72
.equ GXOS_TRAP_RBP, 80
.equ GXOS_TRAP_R8, 88
.equ GXOS_TRAP_R9, 96
.equ GXOS_TRAP_R10, 104
.equ GXOS_TRAP_R11, 112
.equ GXOS_TRAP_R12, 120
.equ GXOS_TRAP_R13, 128
.equ GXOS_TRAP_R14, 136
.equ GXOS_TRAP_R15, 144
.equ GXOS_TRAP_RIP, 152
.equ GXOS_TRAP_CS, 160
.equ GXOS_TRAP_RFLAGS, 168
.equ GXOS_TRAP_RSP, 176
.equ GXOS_TRAP_SS, 184
.equ GXOS_TRAP_CR2, 192
.equ GXOS_TRAP_RAW, 200
.equ GXOS_TRAP_ENTRY_CPU_ERROR, 1
.equ GXOS_TRAP_ENTRY_SYNTHETIC_ERROR, 2
.equ GXOS_TRAP_ENTRY_CPU_STACK, 4
.equ GXOS_TRAP_ENTRY_DERIVED_RSP, 8

.extern gxos_exception_dispatch_active
.extern gxos_exception_dispatch
.extern gxos_exception_fatal_dispatch
.extern gxos_exception_nested_terminal
.extern gxos_probe_expected_rip
.extern gxos_probe_expected_rsp
.extern gxos_probe_landing_rcx
.extern gxos_probe_landing_rdx
.extern gxos_probe_landing_rsp
.extern gxos_probe_landing_reached
.extern gxos_probe_expected_rcx
.extern gxos_probe_expected_rdx
.extern gxos_probe_sentinel_rcx
.extern gxos_probe_sentinel_rdx
.extern gxos_exception_probe_landing_report

.globl gxos_fault_common
gxos_fault_common:
    /* Entry is [vector, error, CPU frame]. Save every GPR before C. */
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
    subq $GXOS_TRAP_FRAME_SIZE, %rsp

    /* r11 points at saved RAX..R15; the normalized raw frame follows it. */
    leaq GXOS_TRAP_FRAME_SIZE(%rsp), %r10
    leaq 120(%r10), %r11
    movq 0(%r11), %rax
    movq %rax, GXOS_TRAP_VECTOR(%rsp)
    movq 8(%r11), %rax
    movq %rax, GXOS_TRAP_ERROR(%rsp)
    movq %r11, GXOS_TRAP_RAW(%rsp)

    movq 0(%r10), %rax
    movq %rax, GXOS_TRAP_RAX(%rsp)
    movq 8(%r10), %rax
    movq %rax, GXOS_TRAP_RBX(%rsp)
    movq 16(%r10), %rax
    movq %rax, GXOS_TRAP_RCX(%rsp)
    movq 24(%r10), %rax
    movq %rax, GXOS_TRAP_RDX(%rsp)
    movq 32(%r10), %rax
    movq %rax, GXOS_TRAP_RSI(%rsp)
    movq 40(%r10), %rax
    movq %rax, GXOS_TRAP_RDI(%rsp)
    movq 48(%r10), %rax
    movq %rax, GXOS_TRAP_RBP(%rsp)
    movq 56(%r10), %rax
    movq %rax, GXOS_TRAP_R8(%rsp)
    movq 64(%r10), %rax
    movq %rax, GXOS_TRAP_R9(%rsp)
    movq 72(%r10), %rax
    movq %rax, GXOS_TRAP_R10(%rsp)
    movq 80(%r10), %rax
    movq %rax, GXOS_TRAP_R11(%rsp)
    movq 88(%r10), %rax
    movq %rax, GXOS_TRAP_R12(%rsp)
    movq 96(%r10), %rax
    movq %rax, GXOS_TRAP_R13(%rsp)
    movq 104(%r10), %rax
    movq %rax, GXOS_TRAP_R14(%rsp)
    movq 112(%r10), %rax
    movq %rax, GXOS_TRAP_R15(%rsp)

    movq 16(%r11), %rax
    movq %rax, GXOS_TRAP_RIP(%rsp)
    movq 24(%r11), %rax
    movq %rax, GXOS_TRAP_CS(%rsp)
    movq 32(%r11), %rax
    movq %rax, GXOS_TRAP_RFLAGS(%rsp)
    movq %cr2, %rax
    movq %rax, GXOS_TRAP_CR2(%rsp)

    /* Vector-specific stubs identify whether error-code storage is synthetic. */
    movq GXOS_TRAP_VECTOR(%rsp), %rax
    cmpq $8, %rax
    je .Lcpu_error_code
    cmpq $10, %rax
    je .Lcpu_error_code
    cmpq $11, %rax
    je .Lcpu_error_code
    cmpq $12, %rax
    je .Lcpu_error_code
    cmpq $13, %rax
    je .Lcpu_error_code
    cmpq $14, %rax
    je .Lcpu_error_code
    cmpq $17, %rax
    je .Lcpu_error_code
    cmpq $21, %rax
    je .Lcpu_error_code
    cmpq $29, %rax
    je .Lcpu_error_code
    cmpq $30, %rax
    je .Lcpu_error_code
    movq $GXOS_TRAP_ENTRY_SYNTHETIC_ERROR, GXOS_TRAP_FLAGS(%rsp)
    jmp .Lerror_source_ready
.Lcpu_error_code:
    movq $GXOS_TRAP_ENTRY_CPU_ERROR, GXOS_TRAP_FLAGS(%rsp)
.Lerror_source_ready:

    /* Determine whether this active firmware entry supplied RSP/SS. */
    xorq %rax, %rax
    movw %ss, %ax
    movq %rax, GXOS_TRAP_SS(%rsp)
    movq GXOS_TRAP_CS(%rsp), %rax
    testb $3, %al
    jnz .Lcpu_stack_frame
    /*
     * The active firmware/QEMU entry also supplies an old RSP/SS pair.
     * Detect that physical pair rather than assuming CS.RPL alone. The SS
     * word must match the current flat selector and the RSP word canonical.
     */
    movq 40(%r11), %rax
    movq 48(%r11), %rcx
    xorq %rdx, %rdx
    movw %ss, %dx
    cmpw %dx, %cx
    jne .Lderived_stack_frame
    movq %rax, %rcx
    shrq $48, %rcx
    cmpq $0, %rcx
    je .Lcpu_stack_frame
    cmpq $0xFFFF, %rcx
    jne .Lderived_stack_frame
    jmp .Lcpu_stack_frame
.Lderived_stack_frame:
    /* Plain same-CPL S is the post-iretq stack position raw+0x28. */
    leaq 40(%r11), %rax
    movq %rax, GXOS_TRAP_RSP(%rsp)
    movq $GXOS_TRAP_ENTRY_DERIVED_RSP, GXOS_TRAP_FLAGS(%rsp)
    jmp .Lframe_ready
.Lcpu_stack_frame:
    movq 40(%r11), %rax
    movq %rax, GXOS_TRAP_RSP(%rsp)
    movq 48(%r11), %rax
    movq %rax, GXOS_TRAP_SS(%rsp)
    orq $GXOS_TRAP_ENTRY_CPU_STACK, GXOS_TRAP_FLAGS(%rsp)
.Lframe_ready:
    /* A nested fault is terminal and never re-enters the synthetic handler. */
    cmpl $0, gxos_exception_dispatch_active(%rip)
    jne .Lnested
    movl $1, gxos_exception_dispatch_active(%rip)
    cld
    movq %rsp, %r10
    andq $-16, %rsp
    subq $64, %rsp
    movq %r10, 48(%rsp)
    movq %r10, %rcx
    call gxos_exception_dispatch
    movq 48(%rsp), %r10
    cmpl $-1, %eax
    jne .Lfatal

    /* Materialize the approved fields into the real iret frame. */
    movq GXOS_TRAP_RAW(%r10), %rax
    movq GXOS_TRAP_RIP(%r10), %r11
    movq %r11, 16(%rax)
    movq GXOS_TRAP_RFLAGS(%r10), %r11
    movq %r11, 32(%rax)
    movq GXOS_TRAP_FLAGS(%r10), %r11
    testq $GXOS_TRAP_ENTRY_CPU_STACK, %r11
    jz .Lno_cpu_rsp
    movq GXOS_TRAP_RSP(%r10), %r11
    movq %r11, 40(%rax)
    movq GXOS_TRAP_SS(%r10), %r11
    movq %r11, 48(%rax)
.Lno_cpu_rsp:
    movq %rax, 48(%rsp)

    /* Restore the complete saved register set; R10 is restored last. */
    movq GXOS_TRAP_RBX(%r10), %rbx
    movq GXOS_TRAP_RCX(%r10), %rcx
    movq GXOS_TRAP_RDX(%r10), %rdx
    movq GXOS_TRAP_RSI(%r10), %rsi
    movq GXOS_TRAP_RDI(%r10), %rdi
    movq GXOS_TRAP_RBP(%r10), %rbp
    movq GXOS_TRAP_R8(%r10), %r8
    movq GXOS_TRAP_R9(%r10), %r9
    movq GXOS_TRAP_R12(%r10), %r12
    movq GXOS_TRAP_R13(%r10), %r13
    movq GXOS_TRAP_R14(%r10), %r14
    movq GXOS_TRAP_R15(%r10), %r15
    movq GXOS_TRAP_RAX(%r10), %rax
    movq GXOS_TRAP_R11(%r10), %r11
    movq GXOS_TRAP_R10(%r10), %r10
    movq 48(%rsp), %rsp
    addq $16, %rsp
    iretq

.Lfatal:
    movq %r10, %rcx
    call gxos_exception_fatal_dispatch
    ud2

.Lnested:
    cld
    movq %rsp, %r10
    andq $-16, %rsp
    subq $64, %rsp
    movq %r10, %rcx
    call gxos_exception_nested_terminal
    ud2

.macro GXOS_FAULT_NO_ERROR number
.globl gxos_fault_no_error_\number
gxos_fault_no_error_\number:
    pushq $0
    pushq $\number
    jmp gxos_fault_common
.endm

.macro GXOS_FAULT_WITH_ERROR number
.globl gxos_fault_with_error_\number
gxos_fault_with_error_\number:
    pushq $\number
    jmp gxos_fault_common
.endm

GXOS_FAULT_NO_ERROR 0
GXOS_FAULT_NO_ERROR 1
GXOS_FAULT_NO_ERROR 2
GXOS_FAULT_NO_ERROR 3
GXOS_FAULT_NO_ERROR 4
GXOS_FAULT_NO_ERROR 5
GXOS_FAULT_NO_ERROR 6
GXOS_FAULT_NO_ERROR 7
GXOS_FAULT_WITH_ERROR 8
GXOS_FAULT_NO_ERROR 9
GXOS_FAULT_WITH_ERROR 10
GXOS_FAULT_WITH_ERROR 11
GXOS_FAULT_WITH_ERROR 12
GXOS_FAULT_WITH_ERROR 13
GXOS_FAULT_WITH_ERROR 14
GXOS_FAULT_NO_ERROR 15
GXOS_FAULT_NO_ERROR 16
GXOS_FAULT_WITH_ERROR 17
GXOS_FAULT_NO_ERROR 18
GXOS_FAULT_NO_ERROR 19
GXOS_FAULT_NO_ERROR 20
GXOS_FAULT_WITH_ERROR 21
GXOS_FAULT_NO_ERROR 22
GXOS_FAULT_NO_ERROR 23
GXOS_FAULT_NO_ERROR 24
GXOS_FAULT_NO_ERROR 25
GXOS_FAULT_NO_ERROR 26
GXOS_FAULT_NO_ERROR 27
GXOS_FAULT_NO_ERROR 28
GXOS_FAULT_WITH_ERROR 29
GXOS_FAULT_WITH_ERROR 30
GXOS_FAULT_NO_ERROR 31

/* Controlled proof: the CPU must capture RIP immediately after INT3. */
.section .text
.globl gxos_exception_probe
gxos_exception_probe:
    movq $0x0102030405060708, %rcx
    movq $0x1020304050607080, %rdx
    movq $0x1111111111111111, %rax
    movq $0x2222222222222222, %rbx
    movq $0x3333333333333333, %rsi
    movq $0x4444444444444444, %rdi
    movq $0x5555555555555555, %rbp
    movq $0x6666666666666666, %r8
    movq $0x7777777777777777, %r9
    movq $0x8888888888888888, %r10
    movq $0x9999999999999999, %r11
    movq $0xAAAAAAAAAAAAAAAA, %r12
    movq $0xBBBBBBBBBBBBBBBB, %r13
    movq $0xCCCCCCCCCCCCCCCC, %r14
    movq $0xDDDDDDDDDDDDDDDD, %r15
    movq %rcx, gxos_probe_expected_rcx(%rip)
    movq %rdx, gxos_probe_expected_rdx(%rip)
    lea gxos_exception_probe_after_int3(%rip), %rax
    movq %rax, gxos_probe_expected_rip(%rip)
    movq %rsp, %rax
    movq %rax, gxos_probe_expected_rsp(%rip)
    movq $0x1122334455667788, %rax
.globl gxos_exception_probe_int3
gxos_exception_probe_int3:
    .byte 0xCC
gxos_exception_probe_after_int3:
    .byte 0x0F, 0x0B

.globl gxos_exception_probe_landing
gxos_exception_probe_landing:
    movq %rcx, gxos_probe_landing_rcx(%rip)
    movq %rdx, gxos_probe_landing_rdx(%rip)
    movq %rsp, gxos_probe_landing_rsp(%rip)
    movl $1, gxos_probe_landing_reached(%rip)
    subq $40, %rsp
    call gxos_exception_probe_landing_report
    addq $40, %rsp
    xorl %eax, %eax
    ret

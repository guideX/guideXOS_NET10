/* AMD64 cooperative context switch for the bounded Gate4 scheduler. */
.text
.p2align 4
.globl gxos_scheduler_context_switch
gxos_scheduler_context_switch:
    subq    $320, %rsp
    movq    %rcx, 0(%rsp)
    movq    %rdx, 8(%rsp)
    leaq    16(%rsp), %rax
    movq    %rbx, 0(%rax)
    movq    %rbp, 8(%rax)
    movq    %rsi, 16(%rax)
    movq    %rdi, 24(%rax)
    movq    %r12, 32(%rax)
    movq    %r13, 40(%rax)
    movq    %r14, 48(%rax)
    movq    %r15, 56(%rax)
    leaq    328(%rsp), %r10
    movq    %r10, 64(%rax)
    movq    320(%rsp), %r10
    movq    %r10, 72(%rax)
    pushfq
    popq    %r10
    movabsq $0xFFFFFFFFFFFFF9FF, %r11
    andq    %r11, %r10
    movq    %r10, 80(%rax)
    stmxcsr 88(%rax)
    fnstcw  92(%rax)
    movl    $0xC0000101, %ecx
    rdmsr
    shlq    $32, %rdx
    orq     %rdx, %rax
    leaq    16(%rsp), %r11
    movq    %rax, 96(%r11)
    movdqu  %xmm6, 104(%r11)
    movdqu  %xmm7, 120(%r11)
    movdqu  %xmm8, 136(%r11)
    movdqu  %xmm9, 152(%r11)
    movdqu  %xmm10, 168(%r11)
    movdqu  %xmm11, 184(%r11)
    movdqu  %xmm12, 200(%r11)
    movdqu  %xmm13, 216(%r11)
    movdqu  %xmm14, 232(%r11)
    movdqu  %xmm15, 248(%r11)
    movq    0(%rsp), %r10
    movq    %r11, (%r10)
    movq    8(%rsp), %r11
    movq    72(%r11), %r10
    movq    %r10, 288(%rsp)
    movq    64(%r11), %r10
    movq    %r10, 296(%rsp)
    movq    80(%r11), %r10
    movq    %r10, 304(%rsp)
    movq    0(%r11), %rbx
    movq    8(%r11), %rbp
    movq    16(%r11), %rsi
    movq    24(%r11), %rdi
    movq    32(%r11), %r12
    movq    40(%r11), %r13
    movq    48(%r11), %r14
    movq    56(%r11), %r15
    movdqu  104(%r11), %xmm6
    movdqu  120(%r11), %xmm7
    movdqu  136(%r11), %xmm8
    movdqu  152(%r11), %xmm9
    movdqu  168(%r11), %xmm10
    movdqu  184(%r11), %xmm11
    movdqu  200(%r11), %xmm12
    movdqu  216(%r11), %xmm13
    movdqu  232(%r11), %xmm14
    movdqu  248(%r11), %xmm15
    ldmxcsr 88(%r11)
    fldcw   92(%r11)
    movq    96(%r11), %r10
    movl    %r10d, %eax
    shrq    $32, %r10
    movl    %r10d, %edx
    movl    $0xC0000101, %ecx
    wrmsr
    movq    304(%rsp), %rax
    pushq   %rax
    popfq
    cld
    movq    288(%rsp), %rax
    movq    296(%rsp), %r10
    movq    %r10, %rsp
    jmp     *%rax

.p2align 4
.globl gxos_scheduler_capture_registers
gxos_scheduler_capture_registers:
    movq    %rcx, %r11
    movq    %rbx, 0(%r11)
    movq    %rbp, 8(%r11)
    movq    %rsi, 16(%r11)
    movq    %rdi, 24(%r11)
    movq    %r12, 32(%r11)
    movq    %r13, 40(%r11)
    movq    %r14, 48(%r11)
    movq    %r15, 56(%r11)
    movdqu  %xmm6, 64(%r11)
    movdqu  %xmm7, 80(%r11)
    movdqu  %xmm8, 96(%r11)
    movdqu  %xmm9, 112(%r11)
    movdqu  %xmm10, 128(%r11)
    movdqu  %xmm11, 144(%r11)
    movdqu  %xmm12, 160(%r11)
    movdqu  %xmm13, 176(%r11)
    movdqu  %xmm14, 192(%r11)
    movdqu  %xmm15, 208(%r11)
    stmxcsr 224(%r11)
    fnstcw  228(%r11)
    movq    %rsp, 232(%r11)
    pushfq
    popq    %r10
    movq    %r10, 240(%r11)
    movl    $0xC0000101, %ecx
    rdmsr
    shlq    $32, %rdx
    orq     %rdx, %rax
    movq    %rax, 248(%r11)
    ret

.p2align 4
.globl gxos_scheduler_set_main_sentinels
gxos_scheduler_set_main_sentinels:
    movabsq $0x4D4D000000000001, %rbx
    movabsq $0x4D4D000000000002, %rbp
    movabsq $0x4D4D000000000003, %rsi
    movabsq $0x4D4D000000000004, %rdi
    movabsq $0x4D4D000000000005, %r12
    movabsq $0x4D4D000000000006, %r13
    movabsq $0x4D4D000000000007, %r14
    movabsq $0x4D4D000000000008, %r15
    movdqu  gxos_main_xmm6(%rip), %xmm6
    movdqu  gxos_main_xmm7(%rip), %xmm7
    movdqu  gxos_main_xmm8(%rip), %xmm8
    movdqu  gxos_main_xmm9(%rip), %xmm9
    movdqu  gxos_main_xmm10(%rip), %xmm10
    movdqu  gxos_main_xmm11(%rip), %xmm11
    movdqu  gxos_main_xmm12(%rip), %xmm12
    movdqu  gxos_main_xmm13(%rip), %xmm13
    movdqu  gxos_main_xmm14(%rip), %xmm14
    movdqu  gxos_main_xmm15(%rip), %xmm15
    ldmxcsr gxos_main_mxcsr(%rip)
    fldcw   gxos_main_x87(%rip)
    ret

.p2align 4
.globl gxos_scheduler_set_worker_sentinels
gxos_scheduler_set_worker_sentinels:
    movabsq $0x5757000000000001, %rbx
    movabsq $0x5757000000000002, %rbp
    movabsq $0x5757000000000003, %rsi
    movabsq $0x5757000000000004, %rdi
    movabsq $0x5757000000000005, %r12
    movabsq $0x5757000000000006, %r13
    movabsq $0x5757000000000007, %r14
    movabsq $0x5757000000000008, %r15
    movdqu  gxos_worker_xmm6(%rip), %xmm6
    movdqu  gxos_worker_xmm7(%rip), %xmm7
    movdqu  gxos_worker_xmm8(%rip), %xmm8
    movdqu  gxos_worker_xmm9(%rip), %xmm9
    movdqu  gxos_worker_xmm10(%rip), %xmm10
    movdqu  gxos_worker_xmm11(%rip), %xmm11
    movdqu  gxos_worker_xmm12(%rip), %xmm12
    movdqu  gxos_worker_xmm13(%rip), %xmm13
    movdqu  gxos_worker_xmm14(%rip), %xmm14
    movdqu  gxos_worker_xmm15(%rip), %xmm15
    ldmxcsr gxos_worker_mxcsr(%rip)
    fldcw   gxos_worker_x87(%rip)
    ret

.p2align 4
.globl gxos_scheduler_main_block
gxos_scheduler_main_block:
    subq    $456, %rsp
    movq    %rcx, 40(%rsp)
    movq    %rdx, 48(%rsp)
    movq    %r8, 56(%rsp)
    movq    %rbx, 120(%rsp)
    movq    %rbp, 128(%rsp)
    movq    %rsi, 136(%rsp)
    movq    %rdi, 144(%rsp)
    movq    %r12, 152(%rsp)
    movq    %r13, 160(%rsp)
    movq    %r14, 168(%rsp)
    movq    %r15, 176(%rsp)
    pushfq
    popq    %r10
    movabsq $0xFFFFFFFFFFFFF9FF, %r11
    andq    %r11, %r10
    movq    %r10, 184(%rsp)
    stmxcsr 192(%rsp)
    fnstcw  196(%rsp)
    movdqu  %xmm6, 200(%rsp)
    movdqu  %xmm7, 216(%rsp)
    movdqu  %xmm8, 232(%rsp)
    movdqu  %xmm9, 248(%rsp)
    movdqu  %xmm10, 264(%rsp)
    movdqu  %xmm11, 280(%rsp)
    movdqu  %xmm12, 296(%rsp)
    movdqu  %xmm13, 312(%rsp)
    movdqu  %xmm14, 328(%rsp)
    movdqu  %xmm15, 344(%rsp)
    call    gxos_scheduler_set_main_sentinels
    movq    40(%rsp), %rcx
    leaq    64(%rsp), %rdx
    call    gxos_scheduler_prepare_wait
    cmpl    $0, %eax
    jl      gxos_scheduler_switch_failure
    jne     gxos_scheduler_main_block_after_switch
    movq    64(%rsp), %rcx
    movq    72(%rsp), %rdx
    call    gxos_scheduler_context_switch
gxos_scheduler_main_block_after_switch:
    movq    40(%rsp), %rcx
    call    gxos_scheduler_finish_wait
    movl    %eax, %r10d
    movq    56(%rsp), %rcx
    movl    %r10d, (%rcx)
    movq    48(%rsp), %rcx
    call    gxos_scheduler_capture_registers
    movq    120(%rsp), %rbx
    movq    128(%rsp), %rbp
    movq    136(%rsp), %rsi
    movq    144(%rsp), %rdi
    movq    152(%rsp), %r12
    movq    160(%rsp), %r13
    movq    168(%rsp), %r14
    movq    176(%rsp), %r15
    ldmxcsr 192(%rsp)
    fldcw   196(%rsp)
    movq    184(%rsp), %rax
    pushq   %rax
    popfq
    cld
    addq    $456, %rsp
    ret

.p2align 4
.globl gxos_scheduler_worker_wait
gxos_scheduler_worker_wait:
    subq    $456, %rsp
    movq    %rcx, 40(%rsp)
    movq    %rdx, 48(%rsp)
    movq    %r8, 56(%rsp)
    movq    %rbx, 120(%rsp)
    movq    %rbp, 128(%rsp)
    movq    %rsi, 136(%rsp)
    movq    %rdi, 144(%rsp)
    movq    %r12, 152(%rsp)
    movq    %r13, 160(%rsp)
    movq    %r14, 168(%rsp)
    movq    %r15, 176(%rsp)
    pushfq
    popq    %r10
    movabsq $0xFFFFFFFFFFFFF9FF, %r11
    andq    %r11, %r10
    movq    %r10, 184(%rsp)
    stmxcsr 192(%rsp)
    fnstcw  196(%rsp)
    movdqu  %xmm6, 200(%rsp)
    movdqu  %xmm7, 216(%rsp)
    movdqu  %xmm8, 232(%rsp)
    movdqu  %xmm9, 248(%rsp)
    movdqu  %xmm10, 264(%rsp)
    movdqu  %xmm11, 280(%rsp)
    movdqu  %xmm12, 296(%rsp)
    movdqu  %xmm13, 312(%rsp)
    movdqu  %xmm14, 328(%rsp)
    movdqu  %xmm15, 344(%rsp)
    movq    40(%rsp), %rcx
    leaq    64(%rsp), %rdx
    call    gxos_scheduler_prepare_wait
    cmpl    $0, %eax
    jl      gxos_scheduler_switch_failure
    jne     gxos_scheduler_worker_wait_finish_immediate
    call    gxos_scheduler_set_worker_sentinels
    movq    64(%rsp), %rcx
    movq    72(%rsp), %rdx
    call    gxos_scheduler_context_switch
gxos_scheduler_worker_wait_after_switch:
    movq    48(%rsp), %rcx
    call    gxos_scheduler_capture_registers
    movq    120(%rsp), %rbx
    movq    128(%rsp), %rbp
    movq    136(%rsp), %rsi
    movq    144(%rsp), %rdi
    movq    152(%rsp), %r12
    movq    160(%rsp), %r13
    movq    168(%rsp), %r14
    movq    176(%rsp), %r15
    ldmxcsr 192(%rsp)
    fldcw   196(%rsp)
    movq    184(%rsp), %rax
    pushq   %rax
    popfq
    cld
gxos_scheduler_worker_wait_finish_immediate:
    movq    40(%rsp), %rcx
    call    gxos_scheduler_finish_wait
    movl    %eax, %r10d
    movq    56(%rsp), %rcx
    movl    %r10d, (%rcx)
    addq    $456, %rsp
    ret

.p2align 4
.globl gxos_scheduler_capture_worker_sentinels
gxos_scheduler_capture_worker_sentinels:
    subq    $328, %rsp
    movq    %rcx, 32(%rsp)
    movq    %rbx, 40(%rsp)
    movq    %rbp, 48(%rsp)
    movq    %rsi, 56(%rsp)
    movq    %rdi, 64(%rsp)
    movq    %r12, 72(%rsp)
    movq    %r13, 80(%rsp)
    movq    %r14, 88(%rsp)
    movq    %r15, 96(%rsp)
    stmxcsr 112(%rsp)
    fnstcw  116(%rsp)
    movdqu  %xmm6, 120(%rsp)
    movdqu  %xmm7, 136(%rsp)
    movdqu  %xmm8, 152(%rsp)
    movdqu  %xmm9, 168(%rsp)
    movdqu  %xmm10, 184(%rsp)
    movdqu  %xmm11, 200(%rsp)
    movdqu  %xmm12, 216(%rsp)
    movdqu  %xmm13, 232(%rsp)
    movdqu  %xmm14, 248(%rsp)
    movdqu  %xmm15, 264(%rsp)
    call    gxos_scheduler_set_worker_sentinels
    movq    32(%rsp), %rcx
    call    gxos_scheduler_capture_registers
    movq    40(%rsp), %rbx
    movq    48(%rsp), %rbp
    movq    56(%rsp), %rsi
    movq    64(%rsp), %rdi
    movq    72(%rsp), %r12
    movq    80(%rsp), %r13
    movq    88(%rsp), %r14
    movq    96(%rsp), %r15
    ldmxcsr 112(%rsp)
    fldcw   116(%rsp)
    addq    $328, %rsp
    ret

.p2align 4
.globl gxos_scheduler_main_dispatch
gxos_scheduler_main_dispatch:
    subq    $456, %rsp
    movq    %rcx, 40(%rsp)
    movq    %rbx, 120(%rsp)
    movq    %rbp, 128(%rsp)
    movq    %rsi, 136(%rsp)
    movq    %rdi, 144(%rsp)
    movq    %r12, 152(%rsp)
    movq    %r13, 160(%rsp)
    movq    %r14, 168(%rsp)
    movq    %r15, 176(%rsp)
    pushfq
    popq    %r10
    movabsq $0xFFFFFFFFFFFFF9FF, %r11
    andq    %r11, %r10
    movq    %r10, 184(%rsp)
    stmxcsr 192(%rsp)
    fnstcw  196(%rsp)
    movdqu  %xmm6, 200(%rsp)
    movdqu  %xmm7, 216(%rsp)
    movdqu  %xmm8, 232(%rsp)
    movdqu  %xmm9, 248(%rsp)
    movdqu  %xmm10, 264(%rsp)
    movdqu  %xmm11, 280(%rsp)
    movdqu  %xmm12, 296(%rsp)
    movdqu  %xmm13, 312(%rsp)
    movdqu  %xmm14, 328(%rsp)
    movdqu  %xmm15, 344(%rsp)
    call    gxos_scheduler_set_main_sentinels
    leaq    64(%rsp), %rdx
    call    gxos_scheduler_prepare_yield
    testl   %eax, %eax
    jz      gxos_scheduler_main_dispatch_capture
    call    gxos_scheduler_pending_plan
    movq    (%rax), %rcx
    movq    8(%rax), %rdx
    call    gxos_scheduler_context_switch
gxos_scheduler_main_dispatch_capture:
    movq    40(%rsp), %rcx
    call    gxos_scheduler_capture_registers
    movq    120(%rsp), %rbx
    movq    128(%rsp), %rbp
    movq    136(%rsp), %rsi
    movq    144(%rsp), %rdi
    movq    152(%rsp), %r12
    movq    160(%rsp), %r13
    movq    168(%rsp), %r14
    movq    176(%rsp), %r15
    ldmxcsr 192(%rsp)
    fldcw   196(%rsp)
    movq    184(%rsp), %rax
    pushq   %rax
    popfq
    cld
    addq    $456, %rsp
    ret

.p2align 4
.globl gxos_scheduler_start_worker
gxos_scheduler_start_worker:
    movw    $0x03F8, %dx
    movb    $0x53, %al
    outb    %al, (%dx)
    call    gxos_scheduler_note_worker_started
    movq    %r12, %r10
    movq    %r13, %r11
    subq    $8, %rsp
    call    gxos_scheduler_set_worker_sentinels
    addq    $8, %rsp
    movq    %r11, %rcx
    subq    $40, %rsp
    call    *%r10
    addq    $40, %rsp
    subq    $72, %rsp
    movq    %rax, 40(%rsp)
    leaq    48(%rsp), %rdx
    movq    40(%rsp), %rcx
    call    gxos_scheduler_prepare_terminate
    testl   %eax, %eax
    jz      gxos_scheduler_switch_failure
    movq    48(%rsp), %rcx
    movq    56(%rsp), %rdx
    call    gxos_scheduler_context_switch
    ud2

.p2align 4
.globl gxos_scheduler_invalid_thread_return
gxos_scheduler_invalid_thread_return:
    ud2

gxos_scheduler_switch_failure:
    ud2

.section .rdata,"dr"
.p2align 4
gxos_main_mxcsr:
    .long   0x00001F80
gxos_main_x87:
    .word   0x037F
gxos_worker_mxcsr:
    .long   0x00003F80
gxos_worker_x87:
    .word   0x077F

gxos_main_xmm6:  .quad 0x4D4D4D4D00000006, 0x0606060606060606
gxos_main_xmm7:  .quad 0x4D4D4D4D00000007, 0x0707070707070707
gxos_main_xmm8:  .quad 0x4D4D4D4D00000008, 0x0808080808080808
gxos_main_xmm9:  .quad 0x4D4D4D4D00000009, 0x0909090909090909
gxos_main_xmm10: .quad 0x4D4D4D4D0000000A, 0x0A0A0A0A0A0A0A0A
gxos_main_xmm11: .quad 0x4D4D4D4D0000000B, 0x0B0B0B0B0B0B0B0B
gxos_main_xmm12: .quad 0x4D4D4D4D0000000C, 0x0C0C0C0C0C0C0C0C
gxos_main_xmm13: .quad 0x4D4D4D4D0000000D, 0x0D0D0D0D0D0D0D0D
gxos_main_xmm14: .quad 0x4D4D4D4D0000000E, 0x0E0E0E0E0E0E0E0E
gxos_main_xmm15: .quad 0x4D4D4D4D0000000F, 0x0F0F0F0F0F0F0F0F
gxos_worker_xmm6:  .quad 0x5757575700000006, 0x1616161616161616
gxos_worker_xmm7:  .quad 0x5757575700000007, 0x1717171717171717
gxos_worker_xmm8:  .quad 0x5757575700000008, 0x1818181818181818
gxos_worker_xmm9:  .quad 0x5757575700000009, 0x1919191919191919
gxos_worker_xmm10: .quad 0x575757570000000A, 0x1A1A1A1A1A1A1A1A
gxos_worker_xmm11: .quad 0x575757570000000B, 0x1B1B1B1B1B1B1B1B
gxos_worker_xmm12: .quad 0x575757570000000C, 0x1C1C1C1C1C1C1C1C
gxos_worker_xmm13: .quad 0x575757570000000D, 0x1D1D1D1D1D1D1D1D
gxos_worker_xmm14: .quad 0x575757570000000E, 0x1E1E1E1E1E1E1E1E
gxos_worker_xmm15: .quad 0x575757570000000F, 0x1F1F1F1F1F1F1F1F

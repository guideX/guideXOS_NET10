#ifndef GXOS_EXCEPTION_CONTEXT_H
#define GXOS_EXCEPTION_CONTEXT_H

#include <stddef.h>
#include <stdint.h>

/*
 * This header deliberately describes a bounded view, not the complete
 * Windows CONTEXT contract.  It contains only integer/control state and no
 * XState, floating-point, SIMD, unwind, or debug-register continuation.
 */
#if defined(__x86_64__)
#define GXOS_EXCEPTION_MS_ABI __attribute__((ms_abi))
#else
#define GXOS_EXCEPTION_MS_ABI
#endif

#define GXOS_EXCEPTION_CONTINUE_SEARCH ((int32_t)0)
#define GXOS_EXCEPTION_CONTINUE_EXECUTION ((int32_t)-1)
#define GXOS_EXCEPTION_CONTEXT_FLAGS_BOUNDED ((uint32_t)0x00100003U)
#define GXOS_EXCEPTION_ALLOWED_RFLAGS_MASK ((uint64_t)0x0000000000000DD5ULL)
#define GXOS_EXCEPTION_BP_RIP_AT_INT3 ((uint32_t)1U)
#define GXOS_EXCEPTION_BP_RIP_AFTER_INT3 ((uint32_t)2U)

enum {
    GXOS_TRAP_ENTRY_CPU_ERROR = 0x00000001U,
    GXOS_TRAP_ENTRY_SYNTHETIC_ERROR = 0x00000002U,
    GXOS_TRAP_ENTRY_CPU_STACK_FRAME = 0x00000004U,
    GXOS_TRAP_ENTRY_DERIVED_RSP = 0x00000008U
};

/*
 * Canonical internal x64 trap frame.  The assembly entry code writes this
 * structure before calling C.  The raw_frame member points at the normalized
 * [vector, error-code, CPU-frame] stack sequence and is never exposed to the
 * synthetic handler.
 */
typedef struct GXOS_X64_TRAP_FRAME {
    uint64_t vector;              /* 0x000 */
    uint64_t error_code;          /* 0x008 */
    uint64_t entry_flags;         /* 0x010 */
    uint64_t reserved0;           /* 0x018 */
    uint64_t rax;                 /* 0x020 */
    uint64_t rbx;                 /* 0x028 */
    uint64_t rcx;                 /* 0x030 */
    uint64_t rdx;                 /* 0x038 */
    uint64_t rsi;                 /* 0x040 */
    uint64_t rdi;                 /* 0x048 */
    uint64_t rbp;                 /* 0x050 */
    uint64_t r8;                  /* 0x058 */
    uint64_t r9;                  /* 0x060 */
    uint64_t r10;                 /* 0x068 */
    uint64_t r11;                 /* 0x070 */
    uint64_t r12;                 /* 0x078 */
    uint64_t r13;                 /* 0x080 */
    uint64_t r14;                 /* 0x088 */
    uint64_t r15;                 /* 0x090 */
    uint64_t rip;                 /* 0x098 */
    uint64_t cs;                  /* 0x0A0 */
    uint64_t rflags;              /* 0x0A8 */
    uint64_t rsp;                 /* 0x0B0 */
    uint64_t ss;                  /* 0x0B8 */
    uint64_t cr2;                 /* 0x0C0 */
    uint64_t raw_frame;           /* 0x0C8 */
    uint64_t reserved1;           /* 0x0D0 */
    uint64_t reserved2;           /* 0x0D8 */
} GXOS_X64_TRAP_FRAME;

/* Bounded Windows-x64-compatible EXCEPTION_POINTERS view. */
typedef struct GXOS_EXCEPTION_POINTERS_COMPAT {
    void *exception_record;       /* 0x00 */
    void *context_record;         /* 0x08 */
} GXOS_EXCEPTION_POINTERS_COMPAT;

/* Bounded Windows-x64-compatible EXCEPTION_RECORD view. */
typedef struct GXOS_EXCEPTION_RECORD_COMPAT {
    uint32_t exception_code;      /* 0x00 */
    uint32_t exception_flags;     /* 0x04 */
    uint64_t nested_record;       /* 0x08 */
    uint64_t exception_address;   /* 0x10 */
    uint32_t number_parameters;   /* 0x18 */
    uint32_t reserved;            /* 0x1C */
    uint64_t exception_information[15]; /* 0x20 */
} GXOS_EXCEPTION_RECORD_COMPAT;

/*
 * The prefix through RIP intentionally matches the Windows AMD64 CONTEXT
 * integer/control offsets used by the future payload integration.  The
 * structure ends at 0x100; no XState data follows it.
 */
typedef struct GXOS_CONTEXT_COMPAT {
    uint64_t p1_home;             /* 0x000 */
    uint64_t p2_home;             /* 0x008 */
    uint64_t p3_home;             /* 0x010 */
    uint64_t p4_home;             /* 0x018 */
    uint64_t p5_home;             /* 0x020 */
    uint64_t p6_home;             /* 0x028 */
    uint32_t context_flags;       /* 0x030 */
    uint32_t mxcsr;               /* 0x034, not represented by the flags */
    uint16_t seg_cs;              /* 0x038 */
    uint16_t seg_ds;              /* 0x03A */
    uint16_t seg_es;              /* 0x03C */
    uint16_t seg_fs;              /* 0x03E */
    uint16_t seg_gs;              /* 0x040 */
    uint16_t seg_ss;              /* 0x042 */
    uint32_t eflags;              /* 0x044 */
    uint64_t dr0;                 /* 0x048 */
    uint64_t dr1;                 /* 0x050 */
    uint64_t dr2;                 /* 0x058 */
    uint64_t dr3;                 /* 0x060 */
    uint64_t dr6;                 /* 0x068 */
    uint64_t dr7;                 /* 0x070 */
    uint64_t rax;                 /* 0x078 */
    uint64_t rcx;                 /* 0x080 */
    uint64_t rdx;                 /* 0x088 */
    uint64_t rbx;                 /* 0x090 */
    uint64_t rsp;                 /* 0x098 */
    uint64_t rbp;                 /* 0x0A0 */
    uint64_t rsi;                 /* 0x0A8 */
    uint64_t rdi;                 /* 0x0B0 */
    uint64_t r8;                  /* 0x0B8 */
    uint64_t r9;                  /* 0x0C0 */
    uint64_t r10;                 /* 0x0C8 */
    uint64_t r11;                 /* 0x0D0 */
    uint64_t r12;                 /* 0x0D8 */
    uint64_t r13;                 /* 0x0E0 */
    uint64_t r14;                 /* 0x0E8 */
    uint64_t r15;                 /* 0x0F0 */
    uint64_t rip;                 /* 0x0F8 */
} GXOS_CONTEXT_COMPAT;

typedef struct GXOS_EXCEPTION_VALIDATION_BOUNDS {
    uintptr_t stack_lower;
    uintptr_t stack_upper;
    uintptr_t executable_lower;
    uintptr_t executable_upper;
} GXOS_EXCEPTION_VALIDATION_BOUNDS;

enum {
    GXOS_EXCEPTION_VALIDATION_OK = 0,
    GXOS_EXCEPTION_VALIDATION_NULL_TRAP = 1,
    GXOS_EXCEPTION_VALIDATION_NULL_CONTEXT = 2,
    GXOS_EXCEPTION_VALIDATION_NONCANONICAL_RIP = 3,
    GXOS_EXCEPTION_VALIDATION_UNAPPROVED_RIP = 4,
    GXOS_EXCEPTION_VALIDATION_NONCANONICAL_RSP = 5,
    GXOS_EXCEPTION_VALIDATION_UNSAFE_RSP = 6,
    GXOS_EXCEPTION_VALIDATION_FORBIDDEN_RFLAGS = 7,
    GXOS_EXCEPTION_VALIDATION_BAD_BOUNDS = 8
};

int gxos_exception_is_canonical(uint64_t address);
uint32_t gxos_exception_translate_vector_code(uint64_t vector);
int gxos_exception_translate_breakpoint_rip(
    uint64_t captured_rip,
    uintptr_t int3_address,
    uint8_t byte_before_rip,
    uint8_t byte_at_rip,
    uint64_t *exception_address,
    uint32_t *rip_semantics);
int gxos_exception_dispatch_entry_allowed(uint32_t active);
int gxos_exception_trap_is_well_formed(const GXOS_X64_TRAP_FRAME *trap);
int gxos_exception_validate_context_modifications(
    const GXOS_X64_TRAP_FRAME *trap,
    const GXOS_CONTEXT_COMPAT *context,
    const GXOS_EXCEPTION_VALIDATION_BOUNDS *bounds);
void gxos_exception_apply_context_modifications(
    GXOS_X64_TRAP_FRAME *trap,
    const GXOS_CONTEXT_COMPAT *context);

_Static_assert(sizeof(uintptr_t) == 8, "exception context requires x64 pointers");
_Static_assert(sizeof(GXOS_X64_TRAP_FRAME) == 0x0E0, "trap frame size");
_Static_assert(offsetof(GXOS_X64_TRAP_FRAME, vector) == 0x000, "trap vector offset");
_Static_assert(offsetof(GXOS_X64_TRAP_FRAME, error_code) == 0x008, "trap error offset");
_Static_assert(offsetof(GXOS_X64_TRAP_FRAME, rax) == 0x020, "trap RAX offset");
_Static_assert(offsetof(GXOS_X64_TRAP_FRAME, rbx) == 0x028, "trap RBX offset");
_Static_assert(offsetof(GXOS_X64_TRAP_FRAME, rcx) == 0x030, "trap RCX offset");
_Static_assert(offsetof(GXOS_X64_TRAP_FRAME, rdx) == 0x038, "trap RDX offset");
_Static_assert(offsetof(GXOS_X64_TRAP_FRAME, rsi) == 0x040, "trap RSI offset");
_Static_assert(offsetof(GXOS_X64_TRAP_FRAME, rdi) == 0x048, "trap RDI offset");
_Static_assert(offsetof(GXOS_X64_TRAP_FRAME, rbp) == 0x050, "trap RBP offset");
_Static_assert(offsetof(GXOS_X64_TRAP_FRAME, r8) == 0x058, "trap R8 offset");
_Static_assert(offsetof(GXOS_X64_TRAP_FRAME, r9) == 0x060, "trap R9 offset");
_Static_assert(offsetof(GXOS_X64_TRAP_FRAME, r10) == 0x068, "trap R10 offset");
_Static_assert(offsetof(GXOS_X64_TRAP_FRAME, r11) == 0x070, "trap R11 offset");
_Static_assert(offsetof(GXOS_X64_TRAP_FRAME, r12) == 0x078, "trap R12 offset");
_Static_assert(offsetof(GXOS_X64_TRAP_FRAME, r13) == 0x080, "trap R13 offset");
_Static_assert(offsetof(GXOS_X64_TRAP_FRAME, r14) == 0x088, "trap R14 offset");
_Static_assert(offsetof(GXOS_X64_TRAP_FRAME, r15) == 0x090, "trap R15 offset");
_Static_assert(offsetof(GXOS_X64_TRAP_FRAME, rip) == 0x098, "trap RIP offset");
_Static_assert(offsetof(GXOS_X64_TRAP_FRAME, cs) == 0x0A0, "trap CS offset");
_Static_assert(offsetof(GXOS_X64_TRAP_FRAME, rflags) == 0x0A8, "trap RFLAGS offset");
_Static_assert(offsetof(GXOS_X64_TRAP_FRAME, rsp) == 0x0B0, "trap RSP offset");
_Static_assert(offsetof(GXOS_X64_TRAP_FRAME, ss) == 0x0B8, "trap SS offset");
_Static_assert(offsetof(GXOS_X64_TRAP_FRAME, cr2) == 0x0C0, "trap CR2 offset");
_Static_assert(offsetof(GXOS_X64_TRAP_FRAME, raw_frame) == 0x0C8,
               "trap raw-frame offset");
_Static_assert(sizeof(GXOS_EXCEPTION_POINTERS_COMPAT) == 0x10, "exception pointers size");
_Static_assert(offsetof(GXOS_EXCEPTION_POINTERS_COMPAT, exception_record) == 0x00,
               "exception pointers record offset");
_Static_assert(offsetof(GXOS_EXCEPTION_POINTERS_COMPAT, context_record) == 0x08,
               "exception pointers context offset");
_Static_assert(sizeof(GXOS_EXCEPTION_RECORD_COMPAT) == 0x98, "exception record size");
_Static_assert(offsetof(GXOS_EXCEPTION_RECORD_COMPAT, exception_code) == 0x00,
               "exception code offset");
_Static_assert(offsetof(GXOS_EXCEPTION_RECORD_COMPAT, exception_flags) == 0x04,
               "exception flags offset");
_Static_assert(offsetof(GXOS_EXCEPTION_RECORD_COMPAT, nested_record) == 0x08,
               "nested record offset");
_Static_assert(offsetof(GXOS_EXCEPTION_RECORD_COMPAT, exception_address) == 0x10,
               "exception address offset");
_Static_assert(offsetof(GXOS_EXCEPTION_RECORD_COMPAT, number_parameters) == 0x18,
               "parameter count offset");
_Static_assert(offsetof(GXOS_EXCEPTION_RECORD_COMPAT, exception_information) == 0x20,
               "exception information offset");
_Static_assert(sizeof(GXOS_CONTEXT_COMPAT) == 0x100, "bounded context size");
_Static_assert(offsetof(GXOS_CONTEXT_COMPAT, context_flags) == 0x30,
               "context flags offset");
_Static_assert(offsetof(GXOS_CONTEXT_COMPAT, eflags) == 0x44, "context EFLAGS offset");
_Static_assert(offsetof(GXOS_CONTEXT_COMPAT, rax) == 0x78, "context RAX offset");
_Static_assert(offsetof(GXOS_CONTEXT_COMPAT, rcx) == 0x80, "context RCX offset");
_Static_assert(offsetof(GXOS_CONTEXT_COMPAT, rdx) == 0x88, "context RDX offset");
_Static_assert(offsetof(GXOS_CONTEXT_COMPAT, rbx) == 0x90, "context RBX offset");
_Static_assert(offsetof(GXOS_CONTEXT_COMPAT, rsp) == 0x98, "context RSP offset");
_Static_assert(offsetof(GXOS_CONTEXT_COMPAT, rbp) == 0xA0, "context RBP offset");
_Static_assert(offsetof(GXOS_CONTEXT_COMPAT, rsi) == 0xA8, "context RSI offset");
_Static_assert(offsetof(GXOS_CONTEXT_COMPAT, rdi) == 0xB0, "context RDI offset");
_Static_assert(offsetof(GXOS_CONTEXT_COMPAT, r8) == 0xB8, "context R8 offset");
_Static_assert(offsetof(GXOS_CONTEXT_COMPAT, r9) == 0xC0, "context R9 offset");
_Static_assert(offsetof(GXOS_CONTEXT_COMPAT, r10) == 0xC8, "context R10 offset");
_Static_assert(offsetof(GXOS_CONTEXT_COMPAT, r11) == 0xD0, "context R11 offset");
_Static_assert(offsetof(GXOS_CONTEXT_COMPAT, r12) == 0xD8, "context R12 offset");
_Static_assert(offsetof(GXOS_CONTEXT_COMPAT, r13) == 0xE0, "context R13 offset");
_Static_assert(offsetof(GXOS_CONTEXT_COMPAT, r14) == 0xE8, "context R14 offset");
_Static_assert(offsetof(GXOS_CONTEXT_COMPAT, r15) == 0xF0, "context R15 offset");
_Static_assert(offsetof(GXOS_CONTEXT_COMPAT, rip) == 0xF8, "context RIP offset");

#endif

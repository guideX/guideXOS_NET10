#ifndef GXOS_CRT_INITTERM_H
#define GXOS_CRT_INITTERM_H

#include <stdint.h>

#if defined(__x86_64__)
#define GXOS_CRT_INITTERM_MS_ABI __attribute__((ms_abi))
#else
#define GXOS_CRT_INITTERM_MS_ABI
#endif

/* The Microsoft CRT's void initializer callback type. */
typedef void (GXOS_CRT_INITTERM_MS_ABI *GXOS_VOID_INITIALIZER)(void);

#define GXOS_CRT_INITTERM_MAX_MEMORY_REGIONS 32U
#define GXOS_CRT_INITTERM_MAX_ENTRIES 4096U
#define GXOS_CRT_INITTERM_MAX_TRACE_ENTRIES 128U
#define GXOS_CRT_INITTERM_NO_CALLBACK 0xFFFFFFFFFFFFFFFFULL
#define GXOS_CRT_INITTERM_VALIDATION_FAILURE (-1)

typedef struct {
    uintptr_t base;
    uintptr_t end;
    uint32_t readable;
    uint32_t executable;
    uint32_t writable;
} GXOS_CRT_INITTERM_MEMORY_REGION;

/* This is a bounded loaded-image/table validation context, not a callback registry. */
typedef struct {
    uintptr_t image_base;
    uintptr_t image_end;
    uint32_t relocations_applied;
    uint32_t memory_region_count;
    GXOS_CRT_INITTERM_MEMORY_REGION memory_regions[GXOS_CRT_INITTERM_MAX_MEMORY_REGIONS];
} GXOS_CRT_INITTERM_CONTEXT;

typedef struct {
    uint64_t entry_count;
    uint64_t null_entry_count;
    uint64_t nonnull_entry_count;
    uint64_t invoked_count;
    uint64_t returned_count;
    uint64_t current_callback_index;
    uintptr_t current_callback_target;
    uint32_t validation_failure;
    uint32_t trace_truncated;
    uint32_t completed;
    uint32_t callback_fault_observed;
    int status;
} GXOS_CRT_INITTERM_REPORT;

enum {
    GXOS_CRT_INITTERM_TRACE_ENTRY = 1,
    GXOS_CRT_INITTERM_TRACE_CALLBACK_BEGIN = 2,
    GXOS_CRT_INITTERM_TRACE_CALLBACK_RETURN = 3,
    GXOS_CRT_INITTERM_TRACE_VALIDATION_FAILURE = 4
};

typedef void (GXOS_CRT_INITTERM_MS_ABI *GXOS_CRT_INITTERM_TRACE)(
    uint32_t event,
    uint64_t index,
    uintptr_t target,
    int32_t status);

int GXOS_CRT_INITTERM_MS_ABI gxos_crt_initterm_configure(
    const GXOS_CRT_INITTERM_CONTEXT *context);

int GXOS_CRT_INITTERM_MS_ABI gxos_crt_initterm(
    GXOS_VOID_INITIALIZER *first,
    GXOS_VOID_INITIALIZER *last,
    GXOS_CRT_INITTERM_REPORT *report,
    GXOS_CRT_INITTERM_TRACE trace);

#ifdef GXOS_CRT_INITTERM_HOST_TEST
void GXOS_CRT_INITTERM_MS_ABI gxos_crt_initterm_inject_callback_fault(void);
#endif

#endif

#ifndef GXOS_CRT_INITTERM_E_H
#define GXOS_CRT_INITTERM_E_H

#include <stdint.h>

#if defined(__x86_64__)
#define GXOS_CRT_INITTERM_E_MS_ABI __attribute__((ms_abi))
#else
#define GXOS_CRT_INITTERM_E_MS_ABI
#endif

/* The Microsoft CRT's error-returning C initializer type. */
typedef int (GXOS_CRT_INITTERM_E_MS_ABI *GXOS_C_INITIALIZER)(void);

#define GXOS_CRT_INITTERM_E_MAX_EXECUTABLE_REGIONS 8U
#define GXOS_CRT_INITTERM_E_MAX_ENTRIES 4096U
#define GXOS_CRT_INITTERM_E_MAX_TRACE_ENTRIES 64U
#define GXOS_CRT_INITTERM_E_VALIDATION_FAILURE (-1)

typedef struct {
    uintptr_t base;
    uintptr_t end;
} GXOS_CRT_INITTERM_E_EXECUTABLE_REGION;

/* This is a narrow image/table validation context, not a general callback registry. */
typedef struct {
    uintptr_t image_base;
    uintptr_t image_end;
    uintptr_t table_base;
    uintptr_t table_end;
    uint32_t relocations_applied;
    uint32_t executable_region_count;
    GXOS_CRT_INITTERM_E_EXECUTABLE_REGION executable_regions[GXOS_CRT_INITTERM_E_MAX_EXECUTABLE_REGIONS];
} GXOS_CRT_INITTERM_E_CONTEXT;

typedef struct {
    uint64_t entry_count;
    uint64_t null_entry_count;
    uint64_t nonnull_entry_count;
    uint64_t invoked_count;
    uint64_t failure_count;
    uint32_t validation_failure;
    uint32_t trace_truncated;
    int result;
} GXOS_CRT_INITTERM_E_REPORT;

enum {
    GXOS_CRT_INITTERM_E_TRACE_ENTRY = 1,
    GXOS_CRT_INITTERM_E_TRACE_CALLBACK_BEGIN = 2,
    GXOS_CRT_INITTERM_E_TRACE_CALLBACK_RESULT = 3,
    GXOS_CRT_INITTERM_E_TRACE_VALIDATION_FAILURE = 4
};

typedef void (GXOS_CRT_INITTERM_E_MS_ABI *GXOS_CRT_INITTERM_E_TRACE)(
    uint32_t event,
    uint64_t index,
    uintptr_t target,
    int32_t result);

int GXOS_CRT_INITTERM_E_MS_ABI gxos_crt_initterm_e_configure(
    const GXOS_CRT_INITTERM_E_CONTEXT *context);

int GXOS_CRT_INITTERM_E_MS_ABI gxos_crt_initterm_e(
    GXOS_C_INITIALIZER *first,
    GXOS_C_INITIALIZER *last,
    GXOS_CRT_INITTERM_E_REPORT *report,
    GXOS_CRT_INITTERM_E_TRACE trace);

#endif

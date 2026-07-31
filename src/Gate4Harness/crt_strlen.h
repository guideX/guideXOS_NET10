#ifndef GXOS_CRT_STRLEN_H
#define GXOS_CRT_STRLEN_H

#include <stddef.h>
#include <stdint.h>

#include "crt_initterm.h"

#if defined(__x86_64__)
#define GXOS_CRT_STRLEN_MS_ABI __attribute__((ms_abi))
#else
#define GXOS_CRT_STRLEN_MS_ABI
#endif

/* This is a bounded validation view of already-mapped memory, not a VM probe. */
typedef struct {
    uintptr_t image_base;
    uintptr_t image_end;
    uint32_t relocations_applied;
    uint32_t memory_region_count;
    const GXOS_CRT_INITTERM_MEMORY_REGION *memory_regions;
} GXOS_READABLE_IMAGE;

typedef enum {
    GXOS_CRT_STRLEN_STATUS_OK = 0,
    GXOS_CRT_STRLEN_STATUS_NULL_POINTER = 1,
    GXOS_CRT_STRLEN_STATUS_NONCANONICAL_POINTER = 2,
    GXOS_CRT_STRLEN_STATUS_UNREADABLE_POINTER = 3,
    GXOS_CRT_STRLEN_STATUS_UNTERMINATED = 4,
    GXOS_CRT_STRLEN_STATUS_OVERFLOW = 5,
    GXOS_CRT_STRLEN_STATUS_INVALID_CONTEXT = 6,
    GXOS_CRT_STRLEN_STATUS_INVALID_OUTPUT = 7
} GXOS_CRT_STRLEN_STATUS;

/* The current path only needs short static configuration strings. */
#define GXOS_CRT_STRLEN_DEFAULT_MAX_SCAN ((size_t)0x10000U)
#define GXOS_CRT_STRLEN_MAX_MEMORY_REGIONS 32U

GXOS_CRT_STRLEN_STATUS GXOS_CRT_STRLEN_MS_ABI gxos_crt_strlen_checked(
    const char *string,
    const GXOS_READABLE_IMAGE *image,
    size_t maximum_scan,
    size_t *length_out);

#endif

#ifndef GXOS_PLATFORM_GET_MODULE_HANDLE_EX_H
#define GXOS_PLATFORM_GET_MODULE_HANDLE_EX_H

#include <stdint.h>

#include "platform_get_module_handle.h"

#if defined(__x86_64__)
#define GXOS_MODULE_HANDLE_EX_MS_ABI __attribute__((ms_abi))
#else
#define GXOS_MODULE_HANDLE_EX_MS_ABI
#endif

#define GXOS_MODULE_HANDLE_EX_FLAG_PIN ((uint32_t)0x00000001U)
#define GXOS_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS ((uint32_t)0x00000004U)
#define GXOS_MODULE_HANDLE_EX_EXPECTED_FLAGS \
    (GXOS_MODULE_HANDLE_EX_FLAG_PIN | GXOS_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS)

typedef enum GXOS_MODULE_HANDLE_EX_STATUS {
    GXOS_MODULE_HANDLE_EX_STATUS_OK = 0,
    GXOS_MODULE_HANDLE_EX_STATUS_UNSUPPORTED_FLAGS,
    GXOS_MODULE_HANDLE_EX_STATUS_NULL_ADDRESS,
    GXOS_MODULE_HANDLE_EX_STATUS_NONCANONICAL_ADDRESS,
    GXOS_MODULE_HANDLE_EX_STATUS_ADDRESS_OUTSIDE_IMAGE,
    GXOS_MODULE_HANDLE_EX_STATUS_AMBIGUOUS_IMAGE,
    GXOS_MODULE_HANDLE_EX_STATUS_NULL_OUTPUT,
    GXOS_MODULE_HANDLE_EX_STATUS_OUTPUT_NOT_WRITABLE,
    GXOS_MODULE_HANDLE_EX_STATUS_INVALID_IMAGE_FACTS,
    GXOS_MODULE_HANDLE_EX_STATUS_IMAGE_RANGE_OVERFLOW,
    GXOS_MODULE_HANDLE_EX_STATUS_IMAGE_NOT_PERMANENT
} GXOS_MODULE_HANDLE_EX_STATUS;

typedef enum GXOS_MODULE_HANDLE_EX_IMAGE_ID {
    GXOS_MODULE_HANDLE_EX_IMAGE_NONE = 0,
    GXOS_MODULE_HANDLE_EX_IMAGE_MAIN_NATIVEAOT_PAYLOAD
} GXOS_MODULE_HANDLE_EX_IMAGE_ID;

typedef struct GXOS_MODULE_HANDLE_EX_REPORT {
    GXOS_MODULE_HANDLE_EX_STATUS status;
    uint32_t flags;
    uint32_t flags_exact;
    uint32_t unknown_flag_bits;
    uint32_t address_nonnull;
    uint32_t address_canonical;
    uint32_t address_in_image;
    uint32_t lookup_match_count;
    uint32_t lookup_unique;
    uint32_t output_pointer_nonnull;
    uint32_t output_pointer_canonical;
    uint32_t output_pointer_proven_writable;
    uint32_t output_written;
    uint32_t residency_invariant_proven;
    uint32_t prior_pinned;
    uint32_t resulting_pinned;
    uint32_t allocation_occurred;
    uint32_t image_free_or_unload_invoked;
    uint32_t prior_onexit_callback_executed;
    GXOS_MODULE_HANDLE_EX_IMAGE_ID image_identity;
    uintptr_t address;
    uintptr_t output_pointer;
    uintptr_t output_value_before;
    uintptr_t output_value_after;
    uintptr_t selected_image_base;
    uint32_t selected_image_size;
    uint32_t address_rva;
    uintptr_t result;
} GXOS_MODULE_HANDLE_EX_REPORT;

_Static_assert(sizeof(uintptr_t) == 8,
               "GetModuleHandleExW requires x64 pointers");
_Static_assert(sizeof(GXOS_MODULE_HANDLE_HMODULE) == 8,
               "GetModuleHandleExW HMODULE width");

GXOS_MODULE_HANDLE_EX_STATUS GXOS_MODULE_HANDLE_EX_MS_ABI
gxos_get_module_handle_ex_checked(
    uint32_t flags,
    uintptr_t address,
    GXOS_MODULE_HANDLE_HMODULE *module_handle_out,
    const GXOS_MAIN_MODULE_FACTS *main_module,
    uintptr_t output_lower,
    uintptr_t output_upper,
    uint32_t permanent_residency_proven,
    GXOS_MODULE_HANDLE_EX_REPORT *report);

#endif

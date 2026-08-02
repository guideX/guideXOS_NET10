#ifndef GXOS_PLATFORM_GET_PROC_ADDRESS_H
#define GXOS_PLATFORM_GET_PROC_ADDRESS_H

#include <stdint.h>

#include "crt_initterm.h"

#if defined(__x86_64__)
#define GXOS_GET_PROC_ADDRESS_MS_ABI __attribute__((ms_abi))
#else
#define GXOS_GET_PROC_ADDRESS_MS_ABI
#endif

typedef uintptr_t GXOS_GET_PROC_ADDRESS_HMODULE;
typedef const char *GXOS_GET_PROC_ADDRESS_LPCSTR;
typedef void (GXOS_GET_PROC_ADDRESS_MS_ABI *GXOS_GET_PROC_ADDRESS_FARPROC)(void);
typedef uint32_t GXOS_GET_PROC_ADDRESS_DWORD;

#define GXOS_GET_PROC_ADDRESS_ERROR_INVALID_HANDLE ((GXOS_GET_PROC_ADDRESS_DWORD)6U)
#define GXOS_GET_PROC_ADDRESS_ERROR_PROC_NOT_FOUND ((GXOS_GET_PROC_ADDRESS_DWORD)127U)
#define GXOS_GET_PROC_ADDRESS_MAX_NAME_BYTES ((uint32_t)256U)
#define GXOS_GET_PROC_ADDRESS_NAME_PREVIEW_BYTES ((uint32_t)64U)

typedef enum GXOS_PROC_IDENTIFIER_KIND {
    GXOS_PROC_IDENTIFIER_NAME = 0,
    GXOS_PROC_IDENTIFIER_ORDINAL
} GXOS_PROC_IDENTIFIER_KIND;

typedef struct GXOS_PROC_IDENTIFIER {
    uintptr_t raw;
    uint64_t high_order_bits;
    uint16_t low_order_word;
    GXOS_PROC_IDENTIFIER_KIND kind;
    uint16_t ordinal;
    GXOS_GET_PROC_ADDRESS_LPCSTR name;
} GXOS_PROC_IDENTIFIER;

typedef enum GXOS_GET_PROC_ADDRESS_STATUS {
    GXOS_GET_PROC_ADDRESS_STATUS_OK = 0,
    GXOS_GET_PROC_ADDRESS_STATUS_INVALID_MODULE_HANDLE,
    GXOS_GET_PROC_ADDRESS_STATUS_MODULE_NOT_MAPPED,
    GXOS_GET_PROC_ADDRESS_STATUS_NONCANONICAL_NAME,
    GXOS_GET_PROC_ADDRESS_STATUS_UNREADABLE_NAME,
    GXOS_GET_PROC_ADDRESS_STATUS_UNTERMINATED_NAME,
    GXOS_GET_PROC_ADDRESS_STATUS_NAME_SCAN_LIMIT,
    GXOS_GET_PROC_ADDRESS_STATUS_POINTER_OVERFLOW,
    GXOS_GET_PROC_ADDRESS_STATUS_UNSUPPORTED_ORDINAL,
    GXOS_GET_PROC_ADDRESS_STATUS_EXPORT_NOT_FOUND,
    GXOS_GET_PROC_ADDRESS_STATUS_INVALID_IMAGE,
    GXOS_GET_PROC_ADDRESS_STATUS_INVALID_EXPORT_DIRECTORY,
    GXOS_GET_PROC_ADDRESS_STATUS_INVALID_EXPORT_TABLE,
    GXOS_GET_PROC_ADDRESS_STATUS_FORWARDED_EXPORT_UNSUPPORTED,
    GXOS_GET_PROC_ADDRESS_STATUS_INVALID_FUNCTION_RVA
} GXOS_GET_PROC_ADDRESS_STATUS;

typedef GXOS_CRT_INITTERM_MEMORY_REGION GXOS_GET_PROC_ADDRESS_MEMORY_REGION;

typedef struct GXOS_GET_PROC_ADDRESS_MEMORY_CONTEXT {
    const GXOS_GET_PROC_ADDRESS_MEMORY_REGION *regions;
    uint32_t region_count;
} GXOS_GET_PROC_ADDRESS_MEMORY_CONTEXT;

typedef struct GXOS_GET_PROC_ADDRESS_REPORT {
    GXOS_GET_PROC_ADDRESS_STATUS status;
    GXOS_GET_PROC_ADDRESS_HMODULE module_handle;
    GXOS_PROC_IDENTIFIER_KIND identifier_kind;
    uintptr_t identifier_raw;
    uint64_t identifier_high_order_bits;
    uint16_t identifier_low_order_word;
    uint16_t ordinal;
    uint32_t module_is_null;
    uint32_t module_pointer_canonical;
    uint32_t module_approved;
    uint32_t module_valid;
    uint32_t name_pointer_canonical;
    uint32_t name_readable;
    uint32_t name_terminated;
    uint32_t name_all_7bit_ascii;
    uint32_t name_high_bit_count;
    uint32_t name_length;
    uintptr_t name_pointer;
    uintptr_t name_terminator;
    uintptr_t name_region_base;
    uintptr_t name_region_end;
    uint32_t name_region_readable;
    uint32_t name_region_executable;
    uint32_t name_region_writable;
    uint32_t name_preview_length;
    uint32_t name_preview_truncated;
    uint8_t name_preview[GXOS_GET_PROC_ADDRESS_NAME_PREVIEW_BYTES];
    uint32_t export_lookup_attempted;
    GXOS_GET_PROC_ADDRESS_FARPROC result;
    GXOS_GET_PROC_ADDRESS_DWORD last_error_before;
    GXOS_GET_PROC_ADDRESS_DWORD last_error_after;
} GXOS_GET_PROC_ADDRESS_REPORT;

_Static_assert(sizeof(GXOS_GET_PROC_ADDRESS_HMODULE) == 8,
               "GetProcAddress HMODULE must remain 64 bits on x64");
_Static_assert(sizeof(GXOS_GET_PROC_ADDRESS_LPCSTR) == 8,
               "GetProcAddress LPCSTR must remain 64 bits on x64");
_Static_assert(sizeof(GXOS_GET_PROC_ADDRESS_FARPROC) == 8,
               "GetProcAddress FARPROC must remain 64 bits on x64");
_Static_assert(sizeof(GXOS_PROC_IDENTIFIER) >= 32,
               "procedure identifier must retain full raw value");

GXOS_GET_PROC_ADDRESS_STATUS GXOS_GET_PROC_ADDRESS_MS_ABI
gxos_get_proc_address_classify(
    uintptr_t raw_identifier,
    GXOS_PROC_IDENTIFIER *identifier,
    GXOS_GET_PROC_ADDRESS_REPORT *report);

GXOS_GET_PROC_ADDRESS_STATUS GXOS_GET_PROC_ADDRESS_MS_ABI
gxos_get_proc_address_checked(
    GXOS_GET_PROC_ADDRESS_HMODULE module_handle,
    GXOS_GET_PROC_ADDRESS_LPCSTR procedure_identifier,
    const GXOS_GET_PROC_ADDRESS_MEMORY_CONTEXT *memory,
    GXOS_GET_PROC_ADDRESS_DWORD previous_last_error,
    GXOS_GET_PROC_ADDRESS_FARPROC *result,
    GXOS_GET_PROC_ADDRESS_DWORD *last_error,
    GXOS_GET_PROC_ADDRESS_REPORT *report);

const char *gxos_get_proc_address_status_name(
    GXOS_GET_PROC_ADDRESS_STATUS status);

const char *gxos_get_proc_address_identifier_kind_name(
    GXOS_PROC_IDENTIFIER_KIND kind);

#endif

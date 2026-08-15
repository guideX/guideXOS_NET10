#ifndef GXOS_PLATFORM_LOAD_LIBRARY_H
#define GXOS_PLATFORM_LOAD_LIBRARY_H

#include <stdint.h>

#include "crt_initterm.h"

#if defined(__x86_64__)
#define GXOS_LOAD_LIBRARY_MS_ABI __attribute__((ms_abi))
#else
#define GXOS_LOAD_LIBRARY_MS_ABI
#endif

typedef uint16_t GXOS_LOAD_LIBRARY_WCHAR;
typedef const GXOS_LOAD_LIBRARY_WCHAR *GXOS_LOAD_LIBRARY_LPCWSTR;
typedef uintptr_t GXOS_LOAD_LIBRARY_HFILE;
typedef uintptr_t GXOS_LOAD_LIBRARY_HMODULE;

#define GXOS_LOAD_LIBRARY_SEARCH_SYSTEM32 ((uint32_t)0x00000800U)
#define GXOS_LOAD_LIBRARY_ERROR_INVALID_PARAMETER ((uint32_t)87U)
#define GXOS_LOAD_LIBRARY_ERROR_MOD_NOT_FOUND ((uint32_t)126U)
#define GXOS_LOAD_LIBRARY_MAX_NAME_CODE_UNITS 256U

typedef enum GXOS_LOAD_LIBRARY_STATUS {
    GXOS_LOAD_LIBRARY_STATUS_OK = 0,
    GXOS_LOAD_LIBRARY_STATUS_INVALID_PARAMETER,
    GXOS_LOAD_LIBRARY_STATUS_NONCANONICAL_NAME,
    GXOS_LOAD_LIBRARY_STATUS_UNREADABLE_NAME,
    GXOS_LOAD_LIBRARY_STATUS_UNTERMINATED_NAME,
    GXOS_LOAD_LIBRARY_STATUS_NAME_SCAN_LIMIT,
    GXOS_LOAD_LIBRARY_STATUS_POINTER_OVERFLOW,
    GXOS_LOAD_LIBRARY_STATUS_INVALID_HFILE,
    GXOS_LOAD_LIBRARY_STATUS_UNSUPPORTED_FLAGS,
    GXOS_LOAD_LIBRARY_STATUS_MODULE_NOT_FOUND,
    GXOS_LOAD_LIBRARY_STATUS_UNSUPPORTED_PATH
} GXOS_LOAD_LIBRARY_STATUS;

typedef GXOS_CRT_INITTERM_MEMORY_REGION GXOS_LOAD_LIBRARY_MEMORY_REGION;

typedef struct GXOS_LOAD_LIBRARY_MEMORY_CONTEXT {
    const GXOS_LOAD_LIBRARY_MEMORY_REGION *regions;
    uint32_t region_count;
} GXOS_LOAD_LIBRARY_MEMORY_CONTEXT;

typedef enum GXOS_LOAD_LIBRARY_SELECTED_MODULE {
    GXOS_LOAD_LIBRARY_SELECTED_NONE = 0,
    GXOS_LOAD_LIBRARY_SELECTED_BUILTIN_KERNEL32
} GXOS_LOAD_LIBRARY_SELECTED_MODULE;

typedef struct GXOS_LOAD_LIBRARY_REPORT {
    GXOS_LOAD_LIBRARY_STATUS status;
    GXOS_LOAD_LIBRARY_SELECTED_MODULE selected_module;
    GXOS_LOAD_LIBRARY_HFILE hfile;
    uint32_t flags;
    uint32_t flags_exact;
    uint32_t hfile_is_null;
    uint32_t name_is_null;
    uint32_t name_pointer_canonical;
    uint32_t name_readable;
    uint32_t name_region_readable;
    uint32_t name_region_executable;
    uint32_t name_region_writable;
    uintptr_t name_region_base;
    uintptr_t name_region_end;
    uint32_t name_length;
    uintptr_t name_terminator;
    uint32_t name_has_path;
    uint32_t name_has_extension;
    uint32_t name_matches_kernel32;
    uint32_t system32_search_applied;
    uintptr_t result;
    uint32_t last_error_before;
    uint32_t last_error_after;
} GXOS_LOAD_LIBRARY_REPORT;

_Static_assert(sizeof(GXOS_LOAD_LIBRARY_HMODULE) == 8,
               "LoadLibraryExW HMODULE must remain pointer-sized on x64");
_Static_assert(sizeof(GXOS_LOAD_LIBRARY_LPCWSTR) == 8,
               "LoadLibraryExW LPCWSTR must remain pointer-sized on x64");
_Static_assert(sizeof(GXOS_LOAD_LIBRARY_WCHAR) == 2,
               "LoadLibraryExW WCHAR must remain 16 bits");

GXOS_LOAD_LIBRARY_STATUS GXOS_LOAD_LIBRARY_MS_ABI
gxos_load_library_ex_checked(
    GXOS_LOAD_LIBRARY_LPCWSTR module_name,
    GXOS_LOAD_LIBRARY_HFILE hfile,
    uint32_t flags,
    const GXOS_LOAD_LIBRARY_MEMORY_CONTEXT *memory,
    uint32_t previous_last_error,
    GXOS_LOAD_LIBRARY_HMODULE *result,
    uint32_t *last_error,
    GXOS_LOAD_LIBRARY_REPORT *report);

const char *gxos_load_library_status_name(GXOS_LOAD_LIBRARY_STATUS status);

#endif

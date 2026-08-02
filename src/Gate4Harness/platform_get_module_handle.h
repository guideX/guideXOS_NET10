#ifndef GXOS_PLATFORM_GET_MODULE_HANDLE_H
#define GXOS_PLATFORM_GET_MODULE_HANDLE_H

#include <stdint.h>
#include "crt_initterm.h"

#if defined(__x86_64__)
#define GXOS_MODULE_HANDLE_MS_ABI __attribute__((ms_abi))
#else
#define GXOS_MODULE_HANDLE_MS_ABI
#endif

typedef uint16_t GXOS_MODULE_HANDLE_WCHAR;
typedef const GXOS_MODULE_HANDLE_WCHAR *GXOS_MODULE_HANDLE_LPCWSTR;
typedef uintptr_t GXOS_MODULE_HANDLE_HMODULE;

#define GXOS_MODULE_HANDLE_ERROR_INVALID_PARAMETER ((uint32_t)87U)
#define GXOS_MODULE_HANDLE_ERROR_MOD_NOT_FOUND ((uint32_t)126U)

#define GXOS_MODULE_HANDLE_MAX_NAME_CODE_UNITS 256U
#define GXOS_MODULE_HANDLE_EXPECTED_MACHINE ((uint16_t)0x8664U)
#define GXOS_MODULE_HANDLE_EXPECTED_PE32_PLUS ((uint16_t)0x20BU)

typedef enum GXOS_MODULE_HANDLE_STATUS {
    GXOS_MODULE_HANDLE_STATUS_OK = 0,
    GXOS_MODULE_HANDLE_STATUS_UNSUPPORTED_NAME,
    GXOS_MODULE_HANDLE_STATUS_MODULE_NOT_FOUND,
    GXOS_MODULE_HANDLE_STATUS_NONCANONICAL_NAME,
    GXOS_MODULE_HANDLE_STATUS_UNREADABLE_NAME,
    GXOS_MODULE_HANDLE_STATUS_UNTERMINATED_NAME,
    GXOS_MODULE_HANDLE_STATUS_NAME_SCAN_LIMIT,
    GXOS_MODULE_HANDLE_STATUS_POINTER_OVERFLOW,
    GXOS_MODULE_HANDLE_STATUS_INVALID_MODULE_FACTS,
    GXOS_MODULE_HANDLE_STATUS_INVALID_MODULE_BASE,
    GXOS_MODULE_HANDLE_STATUS_UNREADABLE_HEADERS,
    GXOS_MODULE_HANDLE_STATUS_INVALID_DOS_HEADER,
    GXOS_MODULE_HANDLE_STATUS_INVALID_NT_HEADER,
    GXOS_MODULE_HANDLE_STATUS_WRONG_MACHINE,
    GXOS_MODULE_HANDLE_STATUS_WRONG_OPTIONAL_HEADER,
    GXOS_MODULE_HANDLE_STATUS_INVALID_IMAGE_RANGE,
    GXOS_MODULE_HANDLE_STATUS_RELOCATION_MISMATCH
} GXOS_MODULE_HANDLE_STATUS;

typedef GXOS_CRT_INITTERM_MEMORY_REGION GXOS_MODULE_HANDLE_MEMORY_REGION;

typedef struct GXOS_MAIN_MODULE_FACTS {
    uintptr_t preferred_image_base;
    uintptr_t mapped_image_base;
    uintptr_t runtime_entry_point;
    uint64_t relocation_delta;
    uint32_t size_of_image;
    uint32_t size_of_headers;
    uint32_t entry_point_rva;
    uint32_t import_directory_rva;
    uint32_t import_directory_size;
    uint32_t importing_iat_rva;
    uint32_t importing_iat_size;
    uint32_t relocations_applied;
    const GXOS_MODULE_HANDLE_MEMORY_REGION *mapped_regions;
    uint32_t mapped_region_count;
} GXOS_MAIN_MODULE_FACTS;

typedef enum GXOS_MODULE_HANDLE_SELECTED_MODULE {
    GXOS_MODULE_HANDLE_SELECTED_NONE = 0,
    GXOS_MODULE_HANDLE_SELECTED_MAIN_NATIVEAOT_PAYLOAD
} GXOS_MODULE_HANDLE_SELECTED_MODULE;

typedef struct GXOS_MODULE_HANDLE_REPORT {
    GXOS_MODULE_HANDLE_STATUS status;
    GXOS_MODULE_HANDLE_SELECTED_MODULE selected_module;
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
    uint32_t name_exact_observed_form;
    uint32_t dos_header_valid;
    uint32_t nt_header_valid;
    uint32_t machine_valid;
    uint32_t optional_header_valid;
    uint32_t size_of_image_valid;
    uint32_t image_range_valid;
    uint32_t entry_point_valid;
    uint32_t import_ownership_valid;
    uint32_t relocation_valid;
    uint32_t caller_read_mask;
    uint32_t output_written;
    uintptr_t result;
} GXOS_MODULE_HANDLE_REPORT;

_Static_assert(sizeof(GXOS_MODULE_HANDLE_HMODULE) == 8,
               "HMODULE must remain pointer-sized on x64");
_Static_assert(sizeof(GXOS_MODULE_HANDLE_LPCWSTR) == 8,
               "LPCWSTR must remain pointer-sized on x64");
_Static_assert(sizeof(GXOS_MODULE_HANDLE_WCHAR) == 2,
               "Windows UTF-16 code units must remain 16 bits");

GXOS_MODULE_HANDLE_STATUS GXOS_MODULE_HANDLE_MS_ABI
gxos_get_module_handle_checked(
    GXOS_MODULE_HANDLE_LPCWSTR module_name,
    const GXOS_MAIN_MODULE_FACTS *main_module,
    GXOS_MODULE_HANDLE_HMODULE *module_handle_out,
    GXOS_MODULE_HANDLE_REPORT *report);

void gxos_get_module_handle_configure(const GXOS_MAIN_MODULE_FACTS *main_module);

GXOS_MODULE_HANDLE_HMODULE GXOS_MODULE_HANDLE_MS_ABI
gxos_get_module_handle_w(GXOS_MODULE_HANDLE_LPCWSTR module_name);

#endif

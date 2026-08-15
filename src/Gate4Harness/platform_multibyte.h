#ifndef GXOS_PLATFORM_MULTIBYTE_H
#define GXOS_PLATFORM_MULTIBYTE_H

#include <stddef.h>
#include <stdint.h>

#if defined(__x86_64__)
#define GXOS_MULTIBYTE_MS_ABI __attribute__((ms_abi))
#else
#define GXOS_MULTIBYTE_MS_ABI
#endif

#define GXOS_MULTIBYTE_CP_UTF8 ((uint32_t)65001U)
#define GXOS_MULTIBYTE_MB_ERR_INVALID_CHARS ((uint32_t)0x00000008U)
#define GXOS_MULTIBYTE_ERROR_INVALID_PARAMETER ((uint32_t)87U)
#define GXOS_MULTIBYTE_ERROR_INSUFFICIENT_BUFFER ((uint32_t)122U)
#define GXOS_MULTIBYTE_ERROR_NO_UNICODE_TRANSLATION ((uint32_t)1113U)
#define GXOS_MULTIBYTE_MAX_NUL_SCAN ((uint32_t)0x10000U)
#define GXOS_MULTIBYTE_MAX_MEMORY_REGIONS ((uint32_t)128U)
#define GXOS_MULTIBYTE_MAX_CAPTURE_BYTES ((uint32_t)64U)
#define GXOS_MULTIBYTE_MAX_CAPTURE_UNITS ((uint32_t)32U)

typedef struct {
    uintptr_t base;
    uintptr_t end;
    uint32_t readable;
    uint32_t writable;
} GXOS_MULTIBYTE_MEMORY_REGION;

typedef struct {
    uint32_t region_count;
    const GXOS_MULTIBYTE_MEMORY_REGION *regions;
} GXOS_MULTIBYTE_MEMORY_CONTEXT;

typedef enum {
    GXOS_MULTIBYTE_STATUS_OK = 0,
    GXOS_MULTIBYTE_STATUS_INVALID_OUTPUT,
    GXOS_MULTIBYTE_STATUS_INVALID_CONTEXT,
    GXOS_MULTIBYTE_STATUS_INVALID_CODE_PAGE,
    GXOS_MULTIBYTE_STATUS_INVALID_FLAGS,
    GXOS_MULTIBYTE_STATUS_INVALID_BYTE_COUNT,
    GXOS_MULTIBYTE_STATUS_NULL_SOURCE,
    GXOS_MULTIBYTE_STATUS_NONCANONICAL_SOURCE,
    GXOS_MULTIBYTE_STATUS_UNREADABLE_SOURCE,
    GXOS_MULTIBYTE_STATUS_SOURCE_RANGE_OVERFLOW,
    GXOS_MULTIBYTE_STATUS_UNTERMINATED_SOURCE,
    GXOS_MULTIBYTE_STATUS_NULL_DESTINATION,
    GXOS_MULTIBYTE_STATUS_NONCANONICAL_DESTINATION,
    GXOS_MULTIBYTE_STATUS_UNWRITABLE_DESTINATION,
    GXOS_MULTIBYTE_STATUS_DESTINATION_RANGE_OVERFLOW,
    GXOS_MULTIBYTE_STATUS_SIZE_OVERFLOW,
    GXOS_MULTIBYTE_STATUS_INSUFFICIENT_BUFFER,
    GXOS_MULTIBYTE_STATUS_INVALID_UTF8,
    GXOS_MULTIBYTE_STATUS_OVERLAPPING_RANGES
} GXOS_MULTIBYTE_STATUS;

typedef struct {
    uint32_t code_page;
    uint32_t flags;
    uintptr_t source;
    int32_t cb_multi_byte;
    uintptr_t destination;
    int32_t cch_wide_char;
    uint64_t source_bytes_including_terminator;
    uint64_t source_bytes_excluding_terminator;
    uint64_t required_utf16_units;
    uint64_t written_utf16_units;
    uint32_t destination_range_valid;
    uint32_t destination_zeroed_before_call;
    uint32_t source_capture_count;
    uint8_t source_capture[GXOS_MULTIBYTE_MAX_CAPTURE_BYTES];
    uint32_t destination_before_count;
    uint8_t destination_before[GXOS_MULTIBYTE_MAX_CAPTURE_BYTES];
    uint32_t destination_after_count;
    uint8_t destination_after[GXOS_MULTIBYTE_MAX_CAPTURE_BYTES];
    uint32_t output_capture_count;
    uint16_t output_capture[GXOS_MULTIBYTE_MAX_CAPTURE_UNITS];
    GXOS_MULTIBYTE_STATUS status;
    uint32_t last_error_before;
    uint32_t last_error_after;
} GXOS_MULTIBYTE_REPORT;

/* The raw Win32 import-call frame captured before entering C. */
typedef struct {
    uint32_t code_page;
    uint32_t flags;
    uintptr_t source;
    uint64_t cb_multi_byte_raw;
    uintptr_t destination;
    uint64_t cch_wide_char_raw;
    uintptr_t return_address;
} GXOS_MULTIBYTE_CALL;

_Static_assert(offsetof(GXOS_MULTIBYTE_CALL, code_page) == 0,
               "MultiByteToWideChar CodePage ABI offset");
_Static_assert(offsetof(GXOS_MULTIBYTE_CALL, flags) == 4,
               "MultiByteToWideChar flags ABI offset");
_Static_assert(offsetof(GXOS_MULTIBYTE_CALL, source) == 8,
               "MultiByteToWideChar source ABI offset");
_Static_assert(offsetof(GXOS_MULTIBYTE_CALL, cb_multi_byte_raw) == 16,
               "MultiByteToWideChar cbMultiByte ABI offset");
_Static_assert(offsetof(GXOS_MULTIBYTE_CALL, destination) == 24,
               "MultiByteToWideChar destination ABI offset");
_Static_assert(offsetof(GXOS_MULTIBYTE_CALL, cch_wide_char_raw) == 32,
               "MultiByteToWideChar cchWideChar ABI offset");
_Static_assert(offsetof(GXOS_MULTIBYTE_CALL, return_address) == 40,
               "MultiByteToWideChar return address offset");
_Static_assert(sizeof(GXOS_MULTIBYTE_CALL) == 48,
               "MultiByteToWideChar call record size");

int32_t GXOS_MULTIBYTE_MS_ABI gxos_multibyte_to_wide_char_checked(
    uint32_t code_page,
    uint32_t flags,
    const char *source,
    int32_t cb_multi_byte,
    uint16_t *destination,
    int32_t cch_wide_char,
    const GXOS_MULTIBYTE_MEMORY_CONTEXT *memory,
    uint32_t previous_last_error,
    uint32_t *last_error,
    GXOS_MULTIBYTE_REPORT *report);

/* The import target is an assembly shim with the real Microsoft x64 ABI. */
void gxos_multibyte_to_wide_char_entry(void);

#endif

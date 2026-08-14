#ifndef GXOS_WRITE_FILE_H
#define GXOS_WRITE_FILE_H

#include <stddef.h>
#include <stdint.h>

#include "crt_initterm.h"
#include "scheduler_foundation.h"

#if defined(__x86_64__)
#define GXOS_WRITE_FILE_MS_ABI __attribute__((ms_abi))
#else
#define GXOS_WRITE_FILE_MS_ABI
#endif

#define GXOS_WRITE_FILE_ERROR_ACCESS_DENIED ((uint32_t)5U)
#define GXOS_WRITE_FILE_ERROR_INVALID_HANDLE ((uint32_t)6U)
#define GXOS_WRITE_FILE_ERROR_INVALID_PARAMETER ((uint32_t)87U)
#define GXOS_WRITE_FILE_ERROR_NOT_SUPPORTED ((uint32_t)50U)
#define GXOS_WRITE_FILE_ERROR_NOACCESS ((uint32_t)998U)

#define GXOS_WRITE_FILE_MAX_CAPTURE_BYTES 32U
#define GXOS_WRITE_FILE_MAX_MEMORY_REGIONS 64U

typedef enum {
    GXOS_WRITE_FILE_STATUS_OK = 0,
    GXOS_WRITE_FILE_STATUS_INVALID_CONTEXT,
    GXOS_WRITE_FILE_STATUS_INVALID_HANDLE,
    GXOS_WRITE_FILE_STATUS_UNSUPPORTED_OBJECT,
    GXOS_WRITE_FILE_STATUS_ACCESS_DENIED,
    GXOS_WRITE_FILE_STATUS_INVALID_PARAMETER,
    GXOS_WRITE_FILE_STATUS_NULL_BUFFER,
    GXOS_WRITE_FILE_STATUS_NONCANONICAL_BUFFER,
    GXOS_WRITE_FILE_STATUS_UNREADABLE_BUFFER,
    GXOS_WRITE_FILE_STATUS_BUFFER_RANGE_OVERFLOW,
    GXOS_WRITE_FILE_STATUS_NULL_BYTES_WRITTEN,
    GXOS_WRITE_FILE_STATUS_NONCANONICAL_BYTES_WRITTEN,
    GXOS_WRITE_FILE_STATUS_UNWRITABLE_BYTES_WRITTEN,
    GXOS_WRITE_FILE_STATUS_BYTES_WRITTEN_RANGE_OVERFLOW,
    GXOS_WRITE_FILE_STATUS_OVERLAPPED_UNSUPPORTED,
    GXOS_WRITE_FILE_STATUS_BACKEND_FAILURE,
    GXOS_WRITE_FILE_STATUS_BACKEND_COUNT_INVALID
} GXOS_WRITE_FILE_STATUS;

typedef struct {
    GXOS_SCHEDULER_HANDLE h_file;
    const void *buffer;
    uint32_t bytes_to_write;
    uint32_t *bytes_written;
    const void *overlapped;
    uintptr_t return_address;
} GXOS_WRITE_FILE_CALL;

_Static_assert(offsetof(GXOS_WRITE_FILE_CALL, h_file) == 0,
               "WriteFile hFile ABI offset");
_Static_assert(offsetof(GXOS_WRITE_FILE_CALL, buffer) == 8,
               "WriteFile lpBuffer ABI offset");
_Static_assert(offsetof(GXOS_WRITE_FILE_CALL, bytes_to_write) == 16,
               "WriteFile nNumberOfBytesToWrite ABI offset");
_Static_assert(offsetof(GXOS_WRITE_FILE_CALL, bytes_written) == 24,
               "WriteFile lpNumberOfBytesWritten ABI offset");
_Static_assert(offsetof(GXOS_WRITE_FILE_CALL, overlapped) == 32,
               "WriteFile lpOverlapped ABI offset");
_Static_assert(offsetof(GXOS_WRITE_FILE_CALL, return_address) == 40,
               "WriteFile return address offset");
_Static_assert(sizeof(GXOS_WRITE_FILE_CALL) == 48,
               "WriteFile call record size");

typedef int (GXOS_WRITE_FILE_MS_ABI *GXOS_WRITE_FILE_BACKEND_WRITE)(
    void *context,
    const uint8_t *bytes,
    uint32_t length,
    uint32_t *bytes_written);

struct GXOS_WRITE_FILE_REPORT;
typedef void (GXOS_WRITE_FILE_MS_ABI *GXOS_WRITE_FILE_PRE_OUTPUT)(
    const struct GXOS_WRITE_FILE_REPORT *report);

typedef struct {
    GXOS_SCHEDULER *scheduler;
    uint32_t *last_error;
    const GXOS_CRT_INITTERM_MEMORY_REGION *regions;
    uint32_t region_count;
    uintptr_t stack_lower;
    uintptr_t stack_upper;
    GXOS_WRITE_FILE_BACKEND_WRITE backend_write;
    void *backend_context;
    GXOS_WRITE_FILE_PRE_OUTPUT pre_output;
} GXOS_WRITE_FILE_CONTEXT;

typedef struct GXOS_WRITE_FILE_REPORT {
    GXOS_WRITE_FILE_STATUS status;
    uint32_t win32_error;
    uint32_t result_bool;
    uint32_t buffer_range_valid;
    uint32_t bytes_written_range_valid;
    uint32_t output_started;
    uint32_t backend_succeeded;
    uint32_t backend_count_valid;
    uint32_t thread_identity;
    uint32_t object_type;
    uint32_t object_slot;
    uint32_t object_generation;
    uint32_t public_handle_refs_before;
    uint32_t internal_refs_before;
    uint32_t public_handle_refs_after;
    uint32_t internal_refs_after;
    uint32_t stream_backend;
    uint32_t stream_capabilities;
    uint32_t bytes_written_result;
    uint32_t prior_last_error;
    uint32_t last_error_after;
    uint32_t first_capture_count;
    uint32_t last_capture_count;
    uint8_t first_capture[GXOS_WRITE_FILE_MAX_CAPTURE_BYTES];
    uint8_t last_capture[GXOS_WRITE_FILE_MAX_CAPTURE_BYTES];
    GXOS_SCHEDULER_HANDLE h_file;
    uintptr_t buffer;
    uintptr_t bytes_written;
    uintptr_t overlapped;
    uintptr_t caller_return_address;
    uint32_t bytes_to_write;
} GXOS_WRITE_FILE_REPORT;

uint32_t GXOS_WRITE_FILE_MS_ABI gxos_write_file_contract(
    const GXOS_WRITE_FILE_CONTEXT *context,
    const GXOS_WRITE_FILE_CALL *call,
    GXOS_WRITE_FILE_REPORT *report);

/* The import target is an assembly shim with the real Win32 x64 ABI. */
void gxos_write_file_entry(void);

/* The loader owns this adapter so diagnostics and the real backend stay local. */
uint32_t GXOS_WRITE_FILE_MS_ABI gxos_write_file_import(
    const GXOS_WRITE_FILE_CALL *call);

#endif

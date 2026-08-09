#ifndef GXOS_PLATFORM_IS_PROCESS_IN_JOB_H
#define GXOS_PLATFORM_IS_PROCESS_IN_JOB_H

#include <stdint.h>

#include "platform_system_info.h"

#if defined(__x86_64__)
#define GXOS_IS_PROCESS_IN_JOB_MS_ABI __attribute__((ms_abi))
#else
#define GXOS_IS_PROCESS_IN_JOB_MS_ABI
#endif

typedef int32_t GXOS_IS_PROCESS_IN_JOB_BOOL;
typedef uintptr_t GXOS_IS_PROCESS_IN_JOB_HANDLE;
typedef GXOS_IS_PROCESS_IN_JOB_BOOL *GXOS_IS_PROCESS_IN_JOB_RESULT;

#define GXOS_IS_PROCESS_IN_JOB_TRUE ((GXOS_IS_PROCESS_IN_JOB_BOOL)1)
#define GXOS_IS_PROCESS_IN_JOB_FALSE ((GXOS_IS_PROCESS_IN_JOB_BOOL)0)
#define GXOS_IS_PROCESS_IN_JOB_CURRENT_PROCESS ((uintptr_t)(intptr_t)-1)
#define GXOS_IS_PROCESS_IN_JOB_NULL_JOB ((uintptr_t)0)

typedef struct GXOS_IS_PROCESS_IN_JOB_FACTS {
    GXOS_IS_PROCESS_IN_JOB_HANDLE current_process_handle;
} GXOS_IS_PROCESS_IN_JOB_FACTS;

typedef enum GXOS_IS_PROCESS_IN_JOB_STATUS {
    GXOS_IS_PROCESS_IN_JOB_STATUS_OK = 0,
    GXOS_IS_PROCESS_IN_JOB_STATUS_INVALID_PROCESS_HANDLE,
    GXOS_IS_PROCESS_IN_JOB_STATUS_NON_NULL_JOB_HANDLE,
    GXOS_IS_PROCESS_IN_JOB_STATUS_NULL_RESULT,
    GXOS_IS_PROCESS_IN_JOB_STATUS_NONCANONICAL_RESULT,
    GXOS_IS_PROCESS_IN_JOB_STATUS_UNWRITABLE_RESULT,
    GXOS_IS_PROCESS_IN_JOB_STATUS_RANGE_OVERFLOW,
    GXOS_IS_PROCESS_IN_JOB_STATUS_INVALID_MEMORY_CONTEXT
} GXOS_IS_PROCESS_IN_JOB_STATUS;

typedef struct GXOS_IS_PROCESS_IN_JOB_REPORT {
    uint32_t process_handle_valid;
    uint32_t job_handle_valid;
    uint32_t result_pointer_canonical;
    uint32_t result_pointer_writable;
    uint32_t result_range_valid;
    uint32_t result_written;
    uint32_t result_bytes_written;
    uintptr_t result_pointer;
    uintptr_t result_range_base;
    uintptr_t result_range_end;
    uint32_t result_value_before;
    uint32_t result_value_after;
} GXOS_IS_PROCESS_IN_JOB_REPORT;

_Static_assert(sizeof(GXOS_IS_PROCESS_IN_JOB_BOOL) == 4,
               "IsProcessInJob BOOL must remain 32 bits");
_Static_assert(sizeof(GXOS_IS_PROCESS_IN_JOB_HANDLE) == 8,
               "IsProcessInJob HANDLE must remain 64 bits on x64");
_Static_assert(sizeof(GXOS_IS_PROCESS_IN_JOB_RESULT) == 8,
               "IsProcessInJob PBOOL must remain 64 bits on x64");
_Static_assert(sizeof(uintptr_t) == 8,
               "IsProcessInJob requires x64 pointers");

GXOS_IS_PROCESS_IN_JOB_STATUS GXOS_IS_PROCESS_IN_JOB_MS_ABI
gxos_is_process_in_job_checked(
    GXOS_IS_PROCESS_IN_JOB_HANDLE process_handle,
    GXOS_IS_PROCESS_IN_JOB_HANDLE job_handle,
    GXOS_IS_PROCESS_IN_JOB_RESULT result,
    const GXOS_IS_PROCESS_IN_JOB_FACTS *facts,
    const GXOS_SYSTEM_INFO_MEMORY_CONTEXT *memory,
    GXOS_IS_PROCESS_IN_JOB_REPORT *report);

GXOS_IS_PROCESS_IN_JOB_BOOL GXOS_IS_PROCESS_IN_JOB_MS_ABI
gxos_is_process_in_job_abi_probe(
    GXOS_IS_PROCESS_IN_JOB_HANDLE process_handle,
    GXOS_IS_PROCESS_IN_JOB_HANDLE job_handle,
    GXOS_IS_PROCESS_IN_JOB_RESULT result,
    const GXOS_IS_PROCESS_IN_JOB_FACTS *facts,
    const GXOS_SYSTEM_INFO_MEMORY_CONTEXT *memory,
    GXOS_IS_PROCESS_IN_JOB_REPORT *report);

#endif

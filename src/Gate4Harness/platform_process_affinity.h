#ifndef GXOS_PLATFORM_PROCESS_AFFINITY_H
#define GXOS_PLATFORM_PROCESS_AFFINITY_H

#include <stdint.h>

#include "platform_system_info.h"

#if defined(__x86_64__)
#define GXOS_PROCESS_AFFINITY_MS_ABI __attribute__((ms_abi))
#else
#define GXOS_PROCESS_AFFINITY_MS_ABI
#endif

/* Microsoft x64 GetProcessAffinityMask widths. */
typedef int32_t GXOS_PROCESS_AFFINITY_BOOL;
typedef uintptr_t GXOS_PROCESS_AFFINITY_HANDLE;
typedef uint64_t GXOS_PROCESS_AFFINITY_DWORD_PTR;

#define GXOS_PROCESS_AFFINITY_TRUE ((GXOS_PROCESS_AFFINITY_BOOL)1)
#define GXOS_PROCESS_AFFINITY_FALSE ((GXOS_PROCESS_AFFINITY_BOOL)0)
#define GXOS_PROCESS_AFFINITY_CURRENT_PROCESS ((uintptr_t)(intptr_t)-1)
#define GXOS_PROCESS_AFFINITY_TOPOLOGY_FACT_SNAPSHOT ((uint32_t)1U)
#define GXOS_PROCESS_AFFINITY_MAX_PROCESSORS 64U
#define GXOS_PROCESS_AFFINITY_ERROR_INVALID_HANDLE ((uint32_t)6U)
#define GXOS_PROCESS_AFFINITY_ERROR_INVALID_PARAMETER ((uint32_t)87U)
#define GXOS_PROCESS_AFFINITY_ERROR_NOT_SUPPORTED ((uint32_t)50U)

typedef struct GXOS_PROCESS_AFFINITY_FACTS {
    GXOS_PROCESS_AFFINITY_HANDLE supported_process_handle;
    GXOS_PROCESS_AFFINITY_DWORD_PTR process_affinity_mask;
    GXOS_PROCESS_AFFINITY_DWORD_PTR system_affinity_mask;
    GXOS_PROCESS_AFFINITY_DWORD_PTR usable_processor_mask;
    uint32_t usable_processor_count;
    uint32_t system_info_processor_count;
    GXOS_PROCESS_AFFINITY_DWORD_PTR system_info_active_processor_mask;
    uint16_t processor_group_count;
    uint16_t current_group_number;
    uint32_t topology_policy;
} GXOS_PROCESS_AFFINITY_FACTS;

typedef enum GXOS_PROCESS_AFFINITY_STATUS {
    GXOS_PROCESS_AFFINITY_STATUS_OK = 0,
    GXOS_PROCESS_AFFINITY_STATUS_INVALID_PROCESS_HANDLE,
    GXOS_PROCESS_AFFINITY_STATUS_NULL_PROCESS_MASK,
    GXOS_PROCESS_AFFINITY_STATUS_NULL_SYSTEM_MASK,
    GXOS_PROCESS_AFFINITY_STATUS_NONCANONICAL_PROCESS_MASK,
    GXOS_PROCESS_AFFINITY_STATUS_NONCANONICAL_SYSTEM_MASK,
    GXOS_PROCESS_AFFINITY_STATUS_UNWRITABLE_PROCESS_MASK,
    GXOS_PROCESS_AFFINITY_STATUS_UNWRITABLE_SYSTEM_MASK,
    GXOS_PROCESS_AFFINITY_STATUS_RANGE_OVERFLOW,
    GXOS_PROCESS_AFFINITY_STATUS_ZERO_PROCESS_MASK,
    GXOS_PROCESS_AFFINITY_STATUS_ZERO_SYSTEM_MASK,
    GXOS_PROCESS_AFFINITY_STATUS_PROCESS_NOT_SUBSET,
    GXOS_PROCESS_AFFINITY_STATUS_PROCESSOR_COUNT_MISMATCH,
    GXOS_PROCESS_AFFINITY_STATUS_GROUP_POLICY_MISMATCH,
    GXOS_PROCESS_AFFINITY_STATUS_SYSTEM_SNAPSHOT_MISMATCH,
    GXOS_PROCESS_AFFINITY_STATUS_UNSUPPORTED_TOPOLOGY,
    GXOS_PROCESS_AFFINITY_STATUS_ALIASED_OUTPUTS,
    GXOS_PROCESS_AFFINITY_STATUS_INVALID_MEMORY_CONTEXT
} GXOS_PROCESS_AFFINITY_STATUS;

typedef struct GXOS_PROCESS_AFFINITY_REPORT {
    uint32_t process_pointer_canonical;
    uint32_t process_pointer_writable;
    uint32_t process_range_valid;
    uint32_t system_pointer_canonical;
    uint32_t system_pointer_writable;
    uint32_t system_range_valid;
    uint32_t process_written;
    uint32_t system_written;
    uint64_t process_mask_written;
    uint64_t system_mask_written;
} GXOS_PROCESS_AFFINITY_REPORT;

_Static_assert(sizeof(GXOS_PROCESS_AFFINITY_BOOL) == 4,
               "GetProcessAffinityMask BOOL must remain 32 bits");
_Static_assert(sizeof(GXOS_PROCESS_AFFINITY_HANDLE) == 8,
               "GetProcessAffinityMask HANDLE must remain 64 bits on x64");
_Static_assert(sizeof(GXOS_PROCESS_AFFINITY_DWORD_PTR) == 8,
               "GetProcessAffinityMask DWORD_PTR must remain 64 bits on x64");
_Static_assert(sizeof(uintptr_t) == 8,
               "GetProcessAffinityMask requires x64 pointers");

GXOS_PROCESS_AFFINITY_STATUS GXOS_PROCESS_AFFINITY_MS_ABI
gxos_get_process_affinity_mask_checked(
    GXOS_PROCESS_AFFINITY_HANDLE process_handle,
    GXOS_PROCESS_AFFINITY_DWORD_PTR *process_affinity_mask,
    GXOS_PROCESS_AFFINITY_DWORD_PTR *system_affinity_mask,
    const GXOS_PROCESS_AFFINITY_FACTS *facts,
    const GXOS_SYSTEM_INFO_MEMORY_CONTEXT *memory,
    GXOS_PROCESS_AFFINITY_REPORT *report);

GXOS_PROCESS_AFFINITY_BOOL GXOS_PROCESS_AFFINITY_MS_ABI
gxos_get_process_affinity_mask_abi_probe(
    GXOS_PROCESS_AFFINITY_HANDLE process_handle,
    GXOS_PROCESS_AFFINITY_DWORD_PTR *process_affinity_mask,
    GXOS_PROCESS_AFFINITY_DWORD_PTR *system_affinity_mask,
    const GXOS_PROCESS_AFFINITY_FACTS *facts,
    const GXOS_SYSTEM_INFO_MEMORY_CONTEXT *memory,
    GXOS_PROCESS_AFFINITY_REPORT *report);

#endif

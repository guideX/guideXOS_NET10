#ifndef GXOS_PLATFORM_NUMA_H
#define GXOS_PLATFORM_NUMA_H

#include <stdbool.h>
#include <stdint.h>

#include "platform_system_info.h"

#if defined(__x86_64__)
#define GXOS_NUMA_MS_ABI __attribute__((ms_abi))
#else
#define GXOS_NUMA_MS_ABI
#endif

/* BOOL is a signed 32-bit Windows scalar; ULONG is an unsigned 32-bit scalar. */
typedef int32_t GXOS_NUMA_BOOL;
typedef uint32_t GXOS_NUMA_ULONG;

#define GXOS_NUMA_TRUE ((GXOS_NUMA_BOOL)1)
#define GXOS_NUMA_FALSE ((GXOS_NUMA_BOOL)0)
#define GXOS_NUMA_TOPOLOGY_POLICY_FACT_SNAPSHOT ((uint32_t)1U)
#define GXOS_NUMA_ERROR_INVALID_PARAMETER ((uint32_t)87U)
#define GXOS_NUMA_ERROR_NOT_SUPPORTED ((uint32_t)50U)

typedef struct GXOS_NUMA_FACTS {
    uint32_t usable_processor_count;
    uint32_t locality_domain_count;
    uint32_t highest_node_number;
    bool node_targeted_allocation_supported;
    uint32_t system_info_processor_count;
    uintptr_t system_info_active_processor_mask;
    uint32_t topology_policy;
} GXOS_NUMA_FACTS;

typedef enum GXOS_NUMA_HIGHEST_NODE_STATUS {
    GXOS_NUMA_HIGHEST_NODE_STATUS_OK = 0,
    GXOS_NUMA_HIGHEST_NODE_STATUS_NULL_POINTER,
    GXOS_NUMA_HIGHEST_NODE_STATUS_NONCANONICAL_POINTER,
    GXOS_NUMA_HIGHEST_NODE_STATUS_UNWRITABLE_POINTER,
    GXOS_NUMA_HIGHEST_NODE_STATUS_INSUFFICIENT_WRITABLE_RANGE,
    GXOS_NUMA_HIGHEST_NODE_STATUS_INVALID_MEMORY_CONTEXT,
    GXOS_NUMA_HIGHEST_NODE_STATUS_INVALID_PROCESSOR_COUNT,
    GXOS_NUMA_HIGHEST_NODE_STATUS_INVALID_PROCESSOR_MASK,
    GXOS_NUMA_HIGHEST_NODE_STATUS_INVALID_DOMAIN_COUNT,
    GXOS_NUMA_HIGHEST_NODE_STATUS_INVALID_HIGHEST_NODE,
    GXOS_NUMA_HIGHEST_NODE_STATUS_INCONSISTENT_DOMAIN_MODEL,
    GXOS_NUMA_HIGHEST_NODE_STATUS_INVALID_SYSTEM_SNAPSHOT,
    GXOS_NUMA_HIGHEST_NODE_STATUS_UNSUPPORTED_TOPOLOGY
} GXOS_NUMA_HIGHEST_NODE_STATUS;

_Static_assert(sizeof(GXOS_NUMA_BOOL) == 4, "Windows BOOL must remain 32 bits");
_Static_assert(sizeof(GXOS_NUMA_ULONG) == 4, "Windows ULONG must remain 32 bits");
_Static_assert(sizeof(uintptr_t) == 8, "NUMA bootstrap requires x64 pointers");

GXOS_NUMA_HIGHEST_NODE_STATUS GXOS_NUMA_MS_ABI gxos_get_numa_highest_node_checked(
    GXOS_NUMA_ULONG *highest_node_number,
    const GXOS_NUMA_FACTS *facts,
    const GXOS_SYSTEM_INFO_MEMORY_CONTEXT *memory);

GXOS_NUMA_BOOL GXOS_NUMA_MS_ABI gxos_get_numa_highest_node_abi_probe(
    GXOS_NUMA_ULONG *highest_node_number,
    const GXOS_NUMA_FACTS *facts,
    const GXOS_SYSTEM_INFO_MEMORY_CONTEXT *memory);

#endif

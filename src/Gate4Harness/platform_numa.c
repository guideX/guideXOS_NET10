#include "platform_numa.h"

static int gxos_numa_is_canonical(uintptr_t address)
{
    uint64_t high = (uint64_t)address >> 47;
    return high == 0 || high == 0x1FFFFU;
}

static uint32_t gxos_numa_population(uintptr_t value)
{
    uint32_t count = 0;
    while (value != 0) {
        value &= value - (uintptr_t)1U;
        count++;
    }
    return count;
}

static GXOS_NUMA_HIGHEST_NODE_STATUS gxos_numa_validate_memory(
    const GXOS_SYSTEM_INFO_MEMORY_CONTEXT *memory)
{
    uint32_t index;

    if (memory == 0 || memory->regions == 0 || memory->region_count == 0 ||
        memory->region_count > GXOS_SYSTEM_INFO_MAX_MEMORY_REGIONS) {
        return GXOS_NUMA_HIGHEST_NODE_STATUS_INVALID_MEMORY_CONTEXT;
    }
    for (index = 0; index != memory->region_count; index++) {
        const GXOS_SYSTEM_INFO_MEMORY_REGION *region = &memory->regions[index];
        if (region->base == 0 || region->end <= region->base ||
            !gxos_numa_is_canonical(region->base) ||
            !gxos_numa_is_canonical(region->end - (uintptr_t)1U)) {
            return GXOS_NUMA_HIGHEST_NODE_STATUS_INVALID_MEMORY_CONTEXT;
        }
    }
    return GXOS_NUMA_HIGHEST_NODE_STATUS_OK;
}

static GXOS_NUMA_HIGHEST_NODE_STATUS gxos_numa_validate_destination(
    const GXOS_NUMA_ULONG *destination,
    const GXOS_SYSTEM_INFO_MEMORY_CONTEXT *memory)
{
    uintptr_t address;
    uintptr_t end;
    uint32_t index;

    if (destination == 0) return GXOS_NUMA_HIGHEST_NODE_STATUS_NULL_POINTER;
    address = (uintptr_t)destination;
    if (!gxos_numa_is_canonical(address)) {
        return GXOS_NUMA_HIGHEST_NODE_STATUS_NONCANONICAL_POINTER;
    }
    if (address > UINTPTR_MAX - sizeof(GXOS_NUMA_ULONG)) {
        return GXOS_NUMA_HIGHEST_NODE_STATUS_INSUFFICIENT_WRITABLE_RANGE;
    }
    end = address + sizeof(GXOS_NUMA_ULONG);
    if (end == 0 || !gxos_numa_is_canonical(end - (uintptr_t)1U)) {
        return GXOS_NUMA_HIGHEST_NODE_STATUS_NONCANONICAL_POINTER;
    }
    for (index = 0; index != memory->region_count; index++) {
        const GXOS_SYSTEM_INFO_MEMORY_REGION *region = &memory->regions[index];
        if (region->writable != 0 && address >= region->base && end <= region->end) {
            return GXOS_NUMA_HIGHEST_NODE_STATUS_OK;
        }
    }
    return GXOS_NUMA_HIGHEST_NODE_STATUS_UNWRITABLE_POINTER;
}

static GXOS_NUMA_HIGHEST_NODE_STATUS gxos_numa_validate_facts(
    const GXOS_NUMA_FACTS *facts)
{
    uint32_t processor_population;

    if (facts == 0) return GXOS_NUMA_HIGHEST_NODE_STATUS_NULL_POINTER;
    if (facts->topology_policy != GXOS_NUMA_TOPOLOGY_POLICY_FACT_SNAPSHOT) {
        return GXOS_NUMA_HIGHEST_NODE_STATUS_UNSUPPORTED_TOPOLOGY;
    }
    if (facts->usable_processor_count == 0 ||
        facts->usable_processor_count > sizeof(uintptr_t) * 8U) {
        return GXOS_NUMA_HIGHEST_NODE_STATUS_INVALID_PROCESSOR_COUNT;
    }
    if (facts->system_info_processor_count != facts->usable_processor_count) {
        return GXOS_NUMA_HIGHEST_NODE_STATUS_INVALID_SYSTEM_SNAPSHOT;
    }
    if (facts->system_info_active_processor_mask == 0) {
        return GXOS_NUMA_HIGHEST_NODE_STATUS_INVALID_PROCESSOR_MASK;
    }
    processor_population = gxos_numa_population(facts->system_info_active_processor_mask);
    if (processor_population != facts->usable_processor_count) {
        return GXOS_NUMA_HIGHEST_NODE_STATUS_INVALID_PROCESSOR_MASK;
    }
    if (facts->locality_domain_count == 0) {
        return GXOS_NUMA_HIGHEST_NODE_STATUS_INVALID_DOMAIN_COUNT;
    }
    if (facts->highest_node_number == UINT32_MAX) {
        return GXOS_NUMA_HIGHEST_NODE_STATUS_INVALID_HIGHEST_NODE;
    }
    if (facts->highest_node_number >= facts->locality_domain_count) {
        return GXOS_NUMA_HIGHEST_NODE_STATUS_INCONSISTENT_DOMAIN_MODEL;
    }
    if (facts->locality_domain_count == 1 && facts->highest_node_number != 0) {
        return GXOS_NUMA_HIGHEST_NODE_STATUS_INCONSISTENT_DOMAIN_MODEL;
    }
    return GXOS_NUMA_HIGHEST_NODE_STATUS_OK;
}

GXOS_NUMA_HIGHEST_NODE_STATUS GXOS_NUMA_MS_ABI gxos_get_numa_highest_node_checked(
    GXOS_NUMA_ULONG *highest_node_number,
    const GXOS_NUMA_FACTS *facts,
    const GXOS_SYSTEM_INFO_MEMORY_CONTEXT *memory)
{
    GXOS_NUMA_HIGHEST_NODE_STATUS status;

    status = gxos_numa_validate_facts(facts);
    if (status != GXOS_NUMA_HIGHEST_NODE_STATUS_OK) return status;
    status = gxos_numa_validate_memory(memory);
    if (status != GXOS_NUMA_HIGHEST_NODE_STATUS_OK) return status;
    status = gxos_numa_validate_destination(highest_node_number, memory);
    if (status != GXOS_NUMA_HIGHEST_NODE_STATUS_OK) return status;

    /* This is the only output store, after every checked condition has passed. */
    *highest_node_number = facts->highest_node_number;
    return GXOS_NUMA_HIGHEST_NODE_STATUS_OK;
}

GXOS_NUMA_BOOL GXOS_NUMA_MS_ABI gxos_get_numa_highest_node_abi_probe(
    GXOS_NUMA_ULONG *highest_node_number,
    const GXOS_NUMA_FACTS *facts,
    const GXOS_SYSTEM_INFO_MEMORY_CONTEXT *memory)
{
    return gxos_get_numa_highest_node_checked(highest_node_number, facts, memory) ==
                   GXOS_NUMA_HIGHEST_NODE_STATUS_OK
               ? GXOS_NUMA_TRUE
               : GXOS_NUMA_FALSE;
}

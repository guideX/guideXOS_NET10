#include "platform_process_group_affinity.h"

static int gxos_process_group_is_canonical(uintptr_t address)
{
    uint64_t high = (uint64_t)address >> 47;
    return high == 0 || high == 0x1FFFFU;
}

static uint32_t gxos_process_group_population(uintptr_t value)
{
    uint32_t count = 0;
    while (value != 0) {
        value &= value - (uintptr_t)1U;
        count++;
    }
    return count;
}

static void gxos_process_group_zero_report(
    GXOS_PROCESS_GROUP_AFFINITY_REPORT *report)
{
    uint32_t index;
    if (report == 0) return;
    report->count_pointer_canonical = 0;
    report->count_pointer_readable = 0;
    report->count_pointer_writable = 0;
    report->array_pointer_canonical = 0;
    report->array_pointer_writable = 0;
    report->input_capacity_valid = 0;
    report->array_range_valid = 0;
    report->groups_written = 0;
    report->input_capacity = 0;
    report->required_count = 0;
    report->output_count = 0;
    for (index = 0; index != GXOS_PROCESS_GROUP_AFFINITY_MAX_GROUPS; index++) {
        report->group_numbers[index] = 0;
    }
}

static GXOS_PROCESS_GROUP_AFFINITY_STATUS gxos_process_group_validate_memory(
    const GXOS_SYSTEM_INFO_MEMORY_CONTEXT *memory)
{
    uint32_t index;

    if (memory == 0 || memory->regions == 0 || memory->region_count == 0 ||
        memory->region_count > GXOS_SYSTEM_INFO_MAX_MEMORY_REGIONS) {
        return GXOS_PROCESS_GROUP_AFFINITY_STATUS_INVALID_MEMORY_CONTEXT;
    }
    for (index = 0; index != memory->region_count; index++) {
        const GXOS_SYSTEM_INFO_MEMORY_REGION *region = &memory->regions[index];
        if (region->base == 0 || region->end <= region->base ||
            !gxos_process_group_is_canonical(region->base) ||
            !gxos_process_group_is_canonical(region->end - (uintptr_t)1U)) {
            return GXOS_PROCESS_GROUP_AFFINITY_STATUS_INVALID_MEMORY_CONTEXT;
        }
    }
    return GXOS_PROCESS_GROUP_AFFINITY_STATUS_OK;
}

static GXOS_PROCESS_GROUP_AFFINITY_STATUS gxos_process_group_validate_facts(
    const GXOS_PROCESS_GROUP_AFFINITY_FACTS *facts)
{
    uint32_t index;
    uint32_t population;

    if (facts == 0) return GXOS_PROCESS_GROUP_AFFINITY_STATUS_INVALID_TOPOLOGY;
    if (facts->topology_policy != GXOS_PROCESS_GROUP_AFFINITY_FACT_SNAPSHOT) {
        return GXOS_PROCESS_GROUP_AFFINITY_STATUS_UNSUPPORTED_TOPOLOGY;
    }
    if (facts->group_count == 0 ||
        facts->group_count > GXOS_PROCESS_GROUP_AFFINITY_MAX_GROUPS) {
        return GXOS_PROCESS_GROUP_AFFINITY_STATUS_INVALID_TOPOLOGY;
    }
    if (facts->usable_processor_count == 0 ||
        facts->usable_processor_count > sizeof(uintptr_t) * 8U) {
        return GXOS_PROCESS_GROUP_AFFINITY_STATUS_INVALID_TOPOLOGY;
    }
    if (facts->group_count > facts->usable_processor_count) {
        return GXOS_PROCESS_GROUP_AFFINITY_STATUS_INVALID_TOPOLOGY;
    }
    if (facts->system_info_processor_count != facts->usable_processor_count ||
        facts->active_processor_mask == 0 ||
        facts->system_info_active_processor_mask != facts->active_processor_mask) {
        return GXOS_PROCESS_GROUP_AFFINITY_STATUS_INVALID_TOPOLOGY;
    }
    population = gxos_process_group_population(facts->active_processor_mask);
    if (population != facts->usable_processor_count ||
        gxos_process_group_population(facts->system_info_active_processor_mask) !=
            facts->system_info_processor_count) {
        return GXOS_PROCESS_GROUP_AFFINITY_STATUS_INVALID_TOPOLOGY;
    }
    for (index = 0; index != facts->group_count; index++) {
        /* The narrow snapshot is deterministic and contiguous from Group 0. */
        if (facts->group_numbers[index] != index) {
            return GXOS_PROCESS_GROUP_AFFINITY_STATUS_UNSUPPORTED_TOPOLOGY;
        }
    }
    return GXOS_PROCESS_GROUP_AFFINITY_STATUS_OK;
}

static GXOS_PROCESS_GROUP_AFFINITY_STATUS gxos_process_group_validate_count(
    GXOS_PROCESS_GROUP_AFFINITY_USHORT *group_count,
    const GXOS_SYSTEM_INFO_MEMORY_CONTEXT *memory,
    GXOS_PROCESS_GROUP_AFFINITY_REPORT *report)
{
    uintptr_t address;
    uintptr_t end;
    uint32_t index;
    uint32_t readable = 0;
    uint32_t writable = 0;

    if (group_count == 0) {
        return GXOS_PROCESS_GROUP_AFFINITY_STATUS_NULL_GROUP_COUNT;
    }
    address = (uintptr_t)group_count;
    if (!gxos_process_group_is_canonical(address)) {
        return GXOS_PROCESS_GROUP_AFFINITY_STATUS_NONCANONICAL_GROUP_COUNT;
    }
    if (address > UINTPTR_MAX - sizeof(*group_count)) {
        return GXOS_PROCESS_GROUP_AFFINITY_STATUS_RANGE_OVERFLOW;
    }
    end = address + sizeof(*group_count);
    if (end == 0 || !gxos_process_group_is_canonical(end - (uintptr_t)1U)) {
        return GXOS_PROCESS_GROUP_AFFINITY_STATUS_NONCANONICAL_GROUP_COUNT;
    }
    for (index = 0; index != memory->region_count; index++) {
        const GXOS_SYSTEM_INFO_MEMORY_REGION *region = &memory->regions[index];
        if (address >= region->base && end <= region->end) {
            if (region->readable != 0) readable = 1;
            if (region->writable != 0) writable = 1;
        }
    }
    if (report != 0) {
        report->count_pointer_canonical = 1;
        report->count_pointer_readable = readable;
        report->count_pointer_writable = writable;
    }
    if (readable == 0) return GXOS_PROCESS_GROUP_AFFINITY_STATUS_UNREADABLE_GROUP_COUNT;
    if (writable == 0) return GXOS_PROCESS_GROUP_AFFINITY_STATUS_UNWRITABLE_GROUP_COUNT;
    return GXOS_PROCESS_GROUP_AFFINITY_STATUS_OK;
}

static GXOS_PROCESS_GROUP_AFFINITY_STATUS gxos_process_group_validate_array(
    GXOS_PROCESS_GROUP_AFFINITY_USHORT *group_array,
    uint16_t required_count,
    const GXOS_SYSTEM_INFO_MEMORY_CONTEXT *memory,
    GXOS_PROCESS_GROUP_AFFINITY_REPORT *report)
{
    uintptr_t address;
    uintptr_t bytes;
    uintptr_t end;
    uint32_t index;

    if (group_array == 0) return GXOS_PROCESS_GROUP_AFFINITY_STATUS_NULL_GROUP_ARRAY;
    address = (uintptr_t)group_array;
    if (!gxos_process_group_is_canonical(address)) {
        return GXOS_PROCESS_GROUP_AFFINITY_STATUS_NONCANONICAL_GROUP_ARRAY;
    }
    bytes = (uintptr_t)required_count * sizeof(*group_array);
    if (required_count != 0 && bytes / sizeof(*group_array) != required_count) {
        return GXOS_PROCESS_GROUP_AFFINITY_STATUS_COUNT_OVERFLOW;
    }
    if (address > UINTPTR_MAX - bytes) {
        return GXOS_PROCESS_GROUP_AFFINITY_STATUS_RANGE_OVERFLOW;
    }
    end = address + bytes;
    if (end == 0 || (bytes != 0 &&
                     !gxos_process_group_is_canonical(end - (uintptr_t)1U))) {
        return GXOS_PROCESS_GROUP_AFFINITY_STATUS_NONCANONICAL_GROUP_ARRAY;
    }
    for (index = 0; index != memory->region_count; index++) {
        const GXOS_SYSTEM_INFO_MEMORY_REGION *region = &memory->regions[index];
        if (region->writable != 0 && address >= region->base && end <= region->end) {
            if (report != 0) {
                report->array_pointer_canonical = 1;
                report->array_pointer_writable = 1;
                report->array_range_valid = 1;
            }
            return GXOS_PROCESS_GROUP_AFFINITY_STATUS_OK;
        }
    }
    if (report != 0) report->array_pointer_canonical = 1;
    return GXOS_PROCESS_GROUP_AFFINITY_STATUS_UNWRITABLE_GROUP_ARRAY;
}

GXOS_PROCESS_GROUP_AFFINITY_STATUS GXOS_PROCESS_GROUP_AFFINITY_MS_ABI
gxos_get_process_group_affinity_checked(
    GXOS_PROCESS_GROUP_AFFINITY_HANDLE process_handle,
    GXOS_PROCESS_GROUP_AFFINITY_USHORT *group_count,
    GXOS_PROCESS_GROUP_AFFINITY_USHORT *group_array,
    const GXOS_PROCESS_GROUP_AFFINITY_FACTS *facts,
    const GXOS_SYSTEM_INFO_MEMORY_CONTEXT *memory,
    GXOS_PROCESS_GROUP_AFFINITY_REPORT *report)
{
    GXOS_PROCESS_GROUP_AFFINITY_STATUS status;
    uint16_t capacity;
    uint32_t index;

    gxos_process_group_zero_report(report);
    status = gxos_process_group_validate_facts(facts);
    if (status != GXOS_PROCESS_GROUP_AFFINITY_STATUS_OK) return status;
    status = gxos_process_group_validate_memory(memory);
    if (status != GXOS_PROCESS_GROUP_AFFINITY_STATUS_OK) return status;
    if (process_handle != GXOS_PROCESS_GROUP_AFFINITY_CURRENT_PROCESS) {
        return GXOS_PROCESS_GROUP_AFFINITY_STATUS_INVALID_PROCESS_HANDLE;
    }
    status = gxos_process_group_validate_count(group_count, memory, report);
    if (status != GXOS_PROCESS_GROUP_AFFINITY_STATUS_OK) return status;

    capacity = *group_count;
    if (report != 0) {
        report->input_capacity_valid = 1;
        report->input_capacity = capacity;
        report->required_count = facts->group_count;
    }
    if (capacity < facts->group_count) {
        /* This is the authoritative capacity-probe failure: no array access. */
        *group_count = facts->group_count;
        if (report != 0) report->output_count = facts->group_count;
        return GXOS_PROCESS_GROUP_AFFINITY_STATUS_INSUFFICIENT_BUFFER;
    }
    status = gxos_process_group_validate_array(group_array, facts->group_count,
                                               memory, report);
    if (status != GXOS_PROCESS_GROUP_AFFINITY_STATUS_OK) return status;
    for (index = 0; index != facts->group_count; index++) {
        group_array[index] = facts->group_numbers[index];
        if (report != 0) report->group_numbers[index] = facts->group_numbers[index];
    }
    *group_count = facts->group_count;
    if (report != 0) {
        report->groups_written = facts->group_count;
        report->output_count = facts->group_count;
    }
    return GXOS_PROCESS_GROUP_AFFINITY_STATUS_OK;
}

GXOS_PROCESS_GROUP_AFFINITY_BOOL GXOS_PROCESS_GROUP_AFFINITY_MS_ABI
gxos_get_process_group_affinity_abi_probe(
    GXOS_PROCESS_GROUP_AFFINITY_HANDLE process_handle,
    GXOS_PROCESS_GROUP_AFFINITY_USHORT *group_count,
    GXOS_PROCESS_GROUP_AFFINITY_USHORT *group_array,
    const GXOS_PROCESS_GROUP_AFFINITY_FACTS *facts,
    const GXOS_SYSTEM_INFO_MEMORY_CONTEXT *memory,
    GXOS_PROCESS_GROUP_AFFINITY_REPORT *report)
{
    return gxos_get_process_group_affinity_checked(
               process_handle, group_count, group_array, facts, memory, report) ==
                   GXOS_PROCESS_GROUP_AFFINITY_STATUS_OK
               ? GXOS_PROCESS_GROUP_AFFINITY_TRUE
               : GXOS_PROCESS_GROUP_AFFINITY_FALSE;
}

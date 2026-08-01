#include "platform_process_affinity.h"

static int gxos_process_affinity_is_canonical(uintptr_t address)
{
    uint64_t high = (uint64_t)address >> 47;
    return high == 0 || high == 0x1FFFFU;
}

static uint32_t gxos_process_affinity_population(uint64_t value)
{
    uint32_t count = 0;
    while (value != 0) {
        value &= value - 1U;
        count++;
    }
    return count;
}

static void gxos_process_affinity_zero_report(
    GXOS_PROCESS_AFFINITY_REPORT *report)
{
    if (report == 0) return;
    report->process_pointer_canonical = 0;
    report->process_pointer_writable = 0;
    report->process_range_valid = 0;
    report->system_pointer_canonical = 0;
    report->system_pointer_writable = 0;
    report->system_range_valid = 0;
    report->process_written = 0;
    report->system_written = 0;
    report->process_mask_written = 0;
    report->system_mask_written = 0;
}

static GXOS_PROCESS_AFFINITY_STATUS gxos_process_affinity_validate_memory(
    const GXOS_SYSTEM_INFO_MEMORY_CONTEXT *memory)
{
    uint32_t index;

    if (memory == 0 || memory->regions == 0 || memory->region_count == 0 ||
        memory->region_count > GXOS_SYSTEM_INFO_MAX_MEMORY_REGIONS) {
        return GXOS_PROCESS_AFFINITY_STATUS_INVALID_MEMORY_CONTEXT;
    }
    for (index = 0; index != memory->region_count; index++) {
        const GXOS_SYSTEM_INFO_MEMORY_REGION *region = &memory->regions[index];
        if (region->base == 0 || region->end <= region->base ||
            !gxos_process_affinity_is_canonical(region->base) ||
            !gxos_process_affinity_is_canonical(region->end - 1U)) {
            return GXOS_PROCESS_AFFINITY_STATUS_INVALID_MEMORY_CONTEXT;
        }
    }
    return GXOS_PROCESS_AFFINITY_STATUS_OK;
}

static GXOS_PROCESS_AFFINITY_STATUS gxos_process_affinity_validate_pointer(
    GXOS_PROCESS_AFFINITY_DWORD_PTR *pointer,
    const GXOS_SYSTEM_INFO_MEMORY_CONTEXT *memory,
    uint32_t *canonical,
    uint32_t *writable,
    uint32_t *range_valid)
{
    uintptr_t address;
    uintptr_t end;
    uint32_t index;

    if (pointer == 0) return GXOS_PROCESS_AFFINITY_STATUS_RANGE_OVERFLOW;
    address = (uintptr_t)pointer;
    if (!gxos_process_affinity_is_canonical(address)) return GXOS_PROCESS_AFFINITY_STATUS_RANGE_OVERFLOW;
    *canonical = 1;
    if (address > UINTPTR_MAX - sizeof(*pointer)) {
        return GXOS_PROCESS_AFFINITY_STATUS_RANGE_OVERFLOW;
    }
    end = address + sizeof(*pointer);
    if (end == 0 || !gxos_process_affinity_is_canonical(end - 1U)) {
        return GXOS_PROCESS_AFFINITY_STATUS_RANGE_OVERFLOW;
    }
    for (index = 0; index != memory->region_count; index++) {
        const GXOS_SYSTEM_INFO_MEMORY_REGION *region = &memory->regions[index];
        if (region->writable != 0 && address >= region->base && end <= region->end) {
            *writable = 1;
            *range_valid = 1;
            return GXOS_PROCESS_AFFINITY_STATUS_OK;
        }
    }
    return GXOS_PROCESS_AFFINITY_STATUS_UNWRITABLE_PROCESS_MASK;
}

static GXOS_PROCESS_AFFINITY_STATUS gxos_process_affinity_validate_facts(
    GXOS_PROCESS_AFFINITY_HANDLE process_handle,
    const GXOS_PROCESS_AFFINITY_FACTS *facts)
{
    uint32_t system_population;
    uint32_t process_population;

    if (facts == 0) return GXOS_PROCESS_AFFINITY_STATUS_UNSUPPORTED_TOPOLOGY;
    if (process_handle != facts->supported_process_handle ||
        process_handle != GXOS_PROCESS_AFFINITY_CURRENT_PROCESS) {
        return GXOS_PROCESS_AFFINITY_STATUS_INVALID_PROCESS_HANDLE;
    }
    if (facts->topology_policy != GXOS_PROCESS_AFFINITY_TOPOLOGY_FACT_SNAPSHOT) {
        return GXOS_PROCESS_AFFINITY_STATUS_UNSUPPORTED_TOPOLOGY;
    }
    if (facts->processor_group_count != 1 || facts->current_group_number != 0) {
        return GXOS_PROCESS_AFFINITY_STATUS_GROUP_POLICY_MISMATCH;
    }
    if (facts->usable_processor_count == 0 ||
        facts->usable_processor_count > GXOS_PROCESS_AFFINITY_MAX_PROCESSORS) {
        return GXOS_PROCESS_AFFINITY_STATUS_PROCESSOR_COUNT_MISMATCH;
    }
    system_population = gxos_process_affinity_population(facts->system_affinity_mask);
    process_population = gxos_process_affinity_population(facts->process_affinity_mask);
    if (facts->system_affinity_mask == 0) return GXOS_PROCESS_AFFINITY_STATUS_ZERO_SYSTEM_MASK;
    if (facts->process_affinity_mask == 0) return GXOS_PROCESS_AFFINITY_STATUS_ZERO_PROCESS_MASK;
    if ((facts->process_affinity_mask & ~facts->system_affinity_mask) != 0) {
        return GXOS_PROCESS_AFFINITY_STATUS_PROCESS_NOT_SUBSET;
    }
    if (facts->system_affinity_mask != facts->usable_processor_mask ||
        facts->system_info_active_processor_mask != facts->system_affinity_mask ||
        facts->system_info_processor_count != facts->usable_processor_count ||
        system_population != facts->usable_processor_count ||
        process_population == 0 || process_population > system_population) {
        return GXOS_PROCESS_AFFINITY_STATUS_SYSTEM_SNAPSHOT_MISMATCH;
    }
    return GXOS_PROCESS_AFFINITY_STATUS_OK;
}

GXOS_PROCESS_AFFINITY_STATUS GXOS_PROCESS_AFFINITY_MS_ABI
gxos_get_process_affinity_mask_checked(
    GXOS_PROCESS_AFFINITY_HANDLE process_handle,
    GXOS_PROCESS_AFFINITY_DWORD_PTR *process_affinity_mask,
    GXOS_PROCESS_AFFINITY_DWORD_PTR *system_affinity_mask,
    const GXOS_PROCESS_AFFINITY_FACTS *facts,
    const GXOS_SYSTEM_INFO_MEMORY_CONTEXT *memory,
    GXOS_PROCESS_AFFINITY_REPORT *report)
{
    GXOS_PROCESS_AFFINITY_STATUS status;
    uint32_t process_canonical = 0;
    uint32_t process_writable = 0;
    uint32_t process_range_valid = 0;
    uint32_t system_canonical = 0;
    uint32_t system_writable = 0;
    uint32_t system_range_valid = 0;

    gxos_process_affinity_zero_report(report);
    status = gxos_process_affinity_validate_facts(process_handle, facts);
    if (status != GXOS_PROCESS_AFFINITY_STATUS_OK) return status;
    status = gxos_process_affinity_validate_memory(memory);
    if (status != GXOS_PROCESS_AFFINITY_STATUS_OK) return status;
    if (process_affinity_mask == 0) return GXOS_PROCESS_AFFINITY_STATUS_NULL_PROCESS_MASK;
    if (system_affinity_mask == 0) return GXOS_PROCESS_AFFINITY_STATUS_NULL_SYSTEM_MASK;
    status = gxos_process_affinity_validate_pointer(
        process_affinity_mask, memory, &process_canonical,
        &process_writable, &process_range_valid);
    if (status != GXOS_PROCESS_AFFINITY_STATUS_OK) {
        if (status == GXOS_PROCESS_AFFINITY_STATUS_RANGE_OVERFLOW &&
            !gxos_process_affinity_is_canonical((uintptr_t)process_affinity_mask)) {
            status = GXOS_PROCESS_AFFINITY_STATUS_NONCANONICAL_PROCESS_MASK;
        }
        return status;
    }
    status = gxos_process_affinity_validate_pointer(
        system_affinity_mask, memory, &system_canonical,
        &system_writable, &system_range_valid);
    if (status != GXOS_PROCESS_AFFINITY_STATUS_OK) {
        if (status == GXOS_PROCESS_AFFINITY_STATUS_RANGE_OVERFLOW &&
            !gxos_process_affinity_is_canonical((uintptr_t)system_affinity_mask)) {
            status = GXOS_PROCESS_AFFINITY_STATUS_NONCANONICAL_SYSTEM_MASK;
        } else if (status == GXOS_PROCESS_AFFINITY_STATUS_UNWRITABLE_PROCESS_MASK) {
            status = GXOS_PROCESS_AFFINITY_STATUS_UNWRITABLE_SYSTEM_MASK;
        }
        return status;
    }
    if (process_affinity_mask == system_affinity_mask) {
        return GXOS_PROCESS_AFFINITY_STATUS_ALIASED_OUTPUTS;
    }
    if (report != 0) {
        report->process_pointer_canonical = process_canonical;
        report->process_pointer_writable = process_writable;
        report->process_range_valid = process_range_valid;
        report->system_pointer_canonical = system_canonical;
        report->system_pointer_writable = system_writable;
        report->system_range_valid = system_range_valid;
    }
    /* All validation is complete before either eight-byte store. */
    *process_affinity_mask = facts->process_affinity_mask;
    *system_affinity_mask = facts->system_affinity_mask;
    if (report != 0) {
        report->process_written = 1;
        report->system_written = 1;
        report->process_mask_written = facts->process_affinity_mask;
        report->system_mask_written = facts->system_affinity_mask;
    }
    return GXOS_PROCESS_AFFINITY_STATUS_OK;
}

GXOS_PROCESS_AFFINITY_BOOL GXOS_PROCESS_AFFINITY_MS_ABI
gxos_get_process_affinity_mask_abi_probe(
    GXOS_PROCESS_AFFINITY_HANDLE process_handle,
    GXOS_PROCESS_AFFINITY_DWORD_PTR *process_affinity_mask,
    GXOS_PROCESS_AFFINITY_DWORD_PTR *system_affinity_mask,
    const GXOS_PROCESS_AFFINITY_FACTS *facts,
    const GXOS_SYSTEM_INFO_MEMORY_CONTEXT *memory,
    GXOS_PROCESS_AFFINITY_REPORT *report)
{
    return gxos_get_process_affinity_mask_checked(
               process_handle, process_affinity_mask, system_affinity_mask,
               facts, memory, report) == GXOS_PROCESS_AFFINITY_STATUS_OK
               ? GXOS_PROCESS_AFFINITY_TRUE
               : GXOS_PROCESS_AFFINITY_FALSE;
}

#include "platform_is_process_in_job.h"

static int gxos_is_process_in_job_is_canonical(uintptr_t address)
{
    uint64_t high = (uint64_t)address >> 47;
    return high == 0 || high == 0x1FFFFU;
}

static void gxos_is_process_in_job_zero_report(
    GXOS_IS_PROCESS_IN_JOB_REPORT *report)
{
    if (report == 0) return;
    report->process_handle_valid = 0;
    report->job_handle_valid = 0;
    report->result_pointer_canonical = 0;
    report->result_pointer_writable = 0;
    report->result_range_valid = 0;
    report->result_written = 0;
    report->result_bytes_written = 0;
    report->result_pointer = 0;
    report->result_range_base = 0;
    report->result_range_end = 0;
    report->result_value_before = 0;
    report->result_value_after = 0;
}

static GXOS_IS_PROCESS_IN_JOB_STATUS gxos_is_process_in_job_validate_memory(
    const GXOS_SYSTEM_INFO_MEMORY_CONTEXT *memory)
{
    uint32_t index;

    if (memory == 0 || memory->regions == 0 || memory->region_count == 0 ||
        memory->region_count > GXOS_SYSTEM_INFO_MAX_MEMORY_REGIONS) {
        return GXOS_IS_PROCESS_IN_JOB_STATUS_INVALID_MEMORY_CONTEXT;
    }
    for (index = 0; index != memory->region_count; ++index) {
        const GXOS_SYSTEM_INFO_MEMORY_REGION *region = &memory->regions[index];
        if (region->base == 0 || region->end <= region->base ||
            !gxos_is_process_in_job_is_canonical(region->base) ||
            !gxos_is_process_in_job_is_canonical(region->end - 1U)) {
            return GXOS_IS_PROCESS_IN_JOB_STATUS_INVALID_MEMORY_CONTEXT;
        }
    }
    return GXOS_IS_PROCESS_IN_JOB_STATUS_OK;
}

static GXOS_IS_PROCESS_IN_JOB_STATUS gxos_is_process_in_job_validate_result(
    GXOS_IS_PROCESS_IN_JOB_RESULT result,
    const GXOS_SYSTEM_INFO_MEMORY_CONTEXT *memory,
    GXOS_IS_PROCESS_IN_JOB_REPORT *report)
{
    uintptr_t address;
    uintptr_t end;
    uint32_t index;

    if (result == 0) return GXOS_IS_PROCESS_IN_JOB_STATUS_NULL_RESULT;
    address = (uintptr_t)result;
    if (!gxos_is_process_in_job_is_canonical(address)) {
        return GXOS_IS_PROCESS_IN_JOB_STATUS_NONCANONICAL_RESULT;
    }
    if (report != 0) {
        report->result_pointer = address;
        report->result_pointer_canonical = 1;
    }
    if (address > UINTPTR_MAX - sizeof(*result)) {
        return GXOS_IS_PROCESS_IN_JOB_STATUS_RANGE_OVERFLOW;
    }
    end = address + sizeof(*result);
    if (end == 0 || !gxos_is_process_in_job_is_canonical(end - 1U)) {
        return GXOS_IS_PROCESS_IN_JOB_STATUS_RANGE_OVERFLOW;
    }
    for (index = 0; index != memory->region_count; ++index) {
        const GXOS_SYSTEM_INFO_MEMORY_REGION *region = &memory->regions[index];
        if (region->writable != 0 && address >= region->base &&
            end <= region->end) {
            if (report != 0) {
                report->result_pointer_writable = 1;
                report->result_range_valid = 1;
                report->result_range_base = region->base;
                report->result_range_end = region->end;
            }
            return GXOS_IS_PROCESS_IN_JOB_STATUS_OK;
        }
    }
    return GXOS_IS_PROCESS_IN_JOB_STATUS_UNWRITABLE_RESULT;
}

GXOS_IS_PROCESS_IN_JOB_STATUS GXOS_IS_PROCESS_IN_JOB_MS_ABI
gxos_is_process_in_job_checked(
    GXOS_IS_PROCESS_IN_JOB_HANDLE process_handle,
    GXOS_IS_PROCESS_IN_JOB_HANDLE job_handle,
    GXOS_IS_PROCESS_IN_JOB_RESULT result,
    const GXOS_IS_PROCESS_IN_JOB_FACTS *facts,
    const GXOS_SYSTEM_INFO_MEMORY_CONTEXT *memory,
    GXOS_IS_PROCESS_IN_JOB_REPORT *report)
{
    GXOS_IS_PROCESS_IN_JOB_STATUS status;

    gxos_is_process_in_job_zero_report(report);
    if (facts == 0 || facts->current_process_handle !=
                           GXOS_IS_PROCESS_IN_JOB_CURRENT_PROCESS ||
        process_handle != facts->current_process_handle) {
        return GXOS_IS_PROCESS_IN_JOB_STATUS_INVALID_PROCESS_HANDLE;
    }
    if (report != 0) report->process_handle_valid = 1;
    if (job_handle != GXOS_IS_PROCESS_IN_JOB_NULL_JOB) {
        return GXOS_IS_PROCESS_IN_JOB_STATUS_NON_NULL_JOB_HANDLE;
    }
    if (report != 0) report->job_handle_valid = 1;
    status = gxos_is_process_in_job_validate_memory(memory);
    if (status != GXOS_IS_PROCESS_IN_JOB_STATUS_OK) return status;
    status = gxos_is_process_in_job_validate_result(result, memory, report);
    if (status != GXOS_IS_PROCESS_IN_JOB_STATUS_OK) return status;

    /* The guideXOS process model has no Windows-style job association. */
    if (report != 0) {
        report->result_value_before = (uint32_t)*result;
    }
    *result = GXOS_IS_PROCESS_IN_JOB_FALSE;
    if (report != 0) {
        report->result_written = 1;
        report->result_bytes_written = sizeof(*result);
        report->result_value_after = (uint32_t)*result;
    }
    return GXOS_IS_PROCESS_IN_JOB_STATUS_OK;
}

GXOS_IS_PROCESS_IN_JOB_BOOL GXOS_IS_PROCESS_IN_JOB_MS_ABI
gxos_is_process_in_job_abi_probe(
    GXOS_IS_PROCESS_IN_JOB_HANDLE process_handle,
    GXOS_IS_PROCESS_IN_JOB_HANDLE job_handle,
    GXOS_IS_PROCESS_IN_JOB_RESULT result,
    const GXOS_IS_PROCESS_IN_JOB_FACTS *facts,
    const GXOS_SYSTEM_INFO_MEMORY_CONTEXT *memory,
    GXOS_IS_PROCESS_IN_JOB_REPORT *report)
{
    return gxos_is_process_in_job_checked(
               process_handle, job_handle, result, facts, memory, report) ==
               GXOS_IS_PROCESS_IN_JOB_STATUS_OK
               ? GXOS_IS_PROCESS_IN_JOB_TRUE
               : GXOS_IS_PROCESS_IN_JOB_FALSE;
}

#include "platform_query_information_job_object.h"

static const GXOS_QUERY_JOB_FACTS *g_probe_facts;
static const GXOS_SYSTEM_INFO_MEMORY_CONTEXT *g_probe_memory;

static int gxos_query_job_is_canonical(uintptr_t address)
{
    uint64_t high = (uint64_t)address >> 47;
    return high == 0 || high == 0x1FFFFU;
}

static void gxos_query_job_zero_report(GXOS_QUERY_JOB_REPORT *report)
{
    if (report == 0) return;
    report->output_pointer_canonical = 0;
    report->output_pointer_writable = 0;
    report->output_range_valid = 0;
    report->return_length_pointer_canonical = 0;
    report->return_length_pointer_writable = 0;
    report->return_length_range_valid = 0;
    report->output_alignment = 0;
    report->return_length_alignment = 0;
    report->output_length_accepted = 0;
    report->output_bytes_before_valid = 0;
    report->output_bytes_after_valid = 0;
    report->return_length_before_valid = 0;
    report->return_length_after_valid = 0;
    report->output_written = 0;
    report->return_length_written = 0;
    report->output_before_low = 0;
    report->output_before_high = 0;
    report->output_after_low = 0;
    report->output_after_high = 0;
    report->return_length_before = 0;
    report->return_length_after = 0;
}

static GXOS_QUERY_JOB_STATUS gxos_query_job_validate_memory(
    const GXOS_SYSTEM_INFO_MEMORY_CONTEXT *memory)
{
    uint32_t index;

    if (memory == 0 || memory->regions == 0 || memory->region_count == 0 ||
        memory->region_count > GXOS_SYSTEM_INFO_MAX_MEMORY_REGIONS) {
        return GXOS_QUERY_JOB_STATUS_INVALID_JOB_FACTS;
    }
    for (index = 0; index != memory->region_count; ++index) {
        const GXOS_SYSTEM_INFO_MEMORY_REGION *region = &memory->regions[index];
        if (region->base == 0 || region->end <= region->base ||
            !gxos_query_job_is_canonical(region->base) ||
            !gxos_query_job_is_canonical(region->end - 1U)) {
            return GXOS_QUERY_JOB_STATUS_INVALID_JOB_FACTS;
        }
    }
    return GXOS_QUERY_JOB_STATUS_OK;
}

static GXOS_QUERY_JOB_STATUS gxos_query_job_validate_range(
    uintptr_t address,
    uintptr_t bytes,
    const GXOS_SYSTEM_INFO_MEMORY_CONTEXT *memory,
    uint32_t *canonical,
    uint32_t *writable,
    uint32_t *range_valid)
{
    uintptr_t end;
    uint32_t index;

    if (canonical != 0) *canonical = 0;
    if (writable != 0) *writable = 0;
    if (range_valid != 0) *range_valid = 0;
    if (!gxos_query_job_is_canonical(address)) {
        return GXOS_QUERY_JOB_STATUS_NONCANONICAL_OUTPUT;
    }
    if (canonical != 0) *canonical = 1;
    if (bytes > UINTPTR_MAX - address) {
        return GXOS_QUERY_JOB_STATUS_RANGE_OVERFLOW;
    }
    end = address + bytes;
    if (end <= address || !gxos_query_job_is_canonical(end - 1U)) {
        return GXOS_QUERY_JOB_STATUS_RANGE_OVERFLOW;
    }
    for (index = 0; index != memory->region_count; ++index) {
        const GXOS_SYSTEM_INFO_MEMORY_REGION *region = &memory->regions[index];
        if (address >= region->base && end <= region->end) {
            if (region->writable == 0) {
                return GXOS_QUERY_JOB_STATUS_UNWRITABLE_OUTPUT;
            }
            if (writable != 0) *writable = 1;
            if (range_valid != 0) *range_valid = 1;
            return GXOS_QUERY_JOB_STATUS_OK;
        }
    }
    return GXOS_QUERY_JOB_STATUS_UNWRITABLE_OUTPUT;
}

static void gxos_query_job_read_bytes(const uint8_t *source,
                                      uint8_t *destination,
                                      uint32_t count)
{
    uint32_t index;
    for (index = 0; index != count; ++index) destination[index] = source[index];
}

static uint32_t gxos_query_job_read_u32(const uint8_t *source)
{
    uint8_t bytes[4];
    uint32_t value;
    gxos_query_job_read_bytes(source, bytes, sizeof(bytes));
    value = (uint32_t)bytes[0];
    value |= (uint32_t)bytes[1] << 8;
    value |= (uint32_t)bytes[2] << 16;
    value |= (uint32_t)bytes[3] << 24;
    return value;
}

static void gxos_query_job_write_u32(uint8_t *destination, uint32_t value)
{
    destination[0] = (uint8_t)value;
    destination[1] = (uint8_t)(value >> 8);
    destination[2] = (uint8_t)(value >> 16);
    destination[3] = (uint8_t)(value >> 24);
}

static void gxos_query_job_capture_output_before(
    const uint8_t *output,
    GXOS_QUERY_JOB_REPORT *report)
{
    if (report == 0) return;
    report->output_before_low = gxos_query_job_read_u32(output);
    report->output_before_high = gxos_query_job_read_u32(output + 4);
    report->output_bytes_before_valid = 1;
}

static void gxos_query_job_capture_output_after(
    const uint8_t *output,
    GXOS_QUERY_JOB_REPORT *report)
{
    if (report == 0) return;
    report->output_after_low = gxos_query_job_read_u32(output);
    report->output_after_high = gxos_query_job_read_u32(output + 4);
    report->output_bytes_after_valid = 1;
}

static int gxos_query_job_ranges_alias(uintptr_t first,
                                       uintptr_t first_bytes,
                                       uintptr_t second,
                                       uintptr_t second_bytes)
{
    uintptr_t first_end = first + first_bytes;
    uintptr_t second_end = second + second_bytes;
    return first < second_end && second < first_end;
}

static GXOS_QUERY_JOB_STATUS gxos_query_job_validate_facts(
    const GXOS_QUERY_JOB_FACTS *facts)
{
    uint32_t flags;

    if (facts == 0 || facts->supported_job_handle != GXOS_QUERY_JOB_CURRENT_HANDLE ||
        facts->associated_job > 1U) {
        return GXOS_QUERY_JOB_STATUS_INVALID_JOB_FACTS;
    }
    flags = facts->control_flags;
    if ((flags & ~GXOS_QUERY_JOB_CPU_RATE_VALID_FLAGS) != 0) {
        return GXOS_QUERY_JOB_STATUS_INVALID_FLAGS;
    }
    if (facts->associated_job == 0) {
        if (flags != 0 || facts->cpu_rate != 0 || facts->weight != 0 ||
            facts->min_rate != 0 || facts->max_rate != 0) {
            return GXOS_QUERY_JOB_STATUS_INVALID_JOB_FACTS;
        }
        return GXOS_QUERY_JOB_STATUS_OK;
    }
    if ((flags & (GXOS_QUERY_JOB_CPU_RATE_WEIGHT_BASED |
                  GXOS_QUERY_JOB_CPU_RATE_HARD_CAP |
                  GXOS_QUERY_JOB_CPU_RATE_MIN_MAX |
                  GXOS_QUERY_JOB_CPU_RATE_NOTIFY)) != 0 &&
        (flags & GXOS_QUERY_JOB_CPU_RATE_ENABLE) == 0) {
        return GXOS_QUERY_JOB_STATUS_INVALID_FLAGS;
    }
    if ((flags & GXOS_QUERY_JOB_CPU_RATE_WEIGHT_BASED) != 0 &&
        (flags & (GXOS_QUERY_JOB_CPU_RATE_HARD_CAP |
                  GXOS_QUERY_JOB_CPU_RATE_MIN_MAX)) != 0) {
        return GXOS_QUERY_JOB_STATUS_INVALID_FLAGS;
    }
    if ((flags & GXOS_QUERY_JOB_CPU_RATE_HARD_CAP) != 0 &&
        (flags & GXOS_QUERY_JOB_CPU_RATE_MIN_MAX) != 0) {
        return GXOS_QUERY_JOB_STATUS_INVALID_FLAGS;
    }
    if ((flags & GXOS_QUERY_JOB_CPU_RATE_WEIGHT_BASED) != 0) {
        if (facts->weight < 1U || facts->weight > 9U) {
            return GXOS_QUERY_JOB_STATUS_INVALID_RATE;
        }
    }
    if ((flags & GXOS_QUERY_JOB_CPU_RATE_HARD_CAP) != 0) {
        if (facts->cpu_rate == 0 || facts->cpu_rate > 10000U) {
            return GXOS_QUERY_JOB_STATUS_INVALID_RATE;
        }
    }
    if ((flags & GXOS_QUERY_JOB_CPU_RATE_MIN_MAX) != 0) {
        if (facts->min_rate > facts->max_rate || facts->max_rate > 10000U ||
            facts->max_rate == 0) {
            return GXOS_QUERY_JOB_STATUS_INVALID_RATE;
        }
    }
    return GXOS_QUERY_JOB_STATUS_OK;
}

static GXOS_QUERY_JOB_STATUS gxos_query_job_validate_output(
    GXOS_QUERY_JOB_OUTPUT output,
    GXOS_QUERY_JOB_DWORD output_length,
    GXOS_QUERY_JOB_RETURN_LENGTH return_length,
    const GXOS_SYSTEM_INFO_MEMORY_CONTEXT *memory,
    GXOS_QUERY_JOB_REPORT *report)
{
    GXOS_QUERY_JOB_STATUS status;

    if (output == 0) return GXOS_QUERY_JOB_STATUS_NULL_OUTPUT;
    if (output_length < GXOS_QUERY_JOB_CPU_RATE_STRUCTURE_SIZE) {
        return GXOS_QUERY_JOB_STATUS_INSUFFICIENT_OUTPUT;
    }
    status = gxos_query_job_validate_range(
        (uintptr_t)output, GXOS_QUERY_JOB_CPU_RATE_STRUCTURE_SIZE, memory,
        report == 0 ? 0 : &report->output_pointer_canonical,
        report == 0 ? 0 : &report->output_pointer_writable,
        report == 0 ? 0 : &report->output_range_valid);
    if (status != GXOS_QUERY_JOB_STATUS_OK) {
        if (status == GXOS_QUERY_JOB_STATUS_NONCANONICAL_OUTPUT) {
            return GXOS_QUERY_JOB_STATUS_NONCANONICAL_OUTPUT;
        }
        if (status == GXOS_QUERY_JOB_STATUS_RANGE_OVERFLOW) {
            return GXOS_QUERY_JOB_STATUS_RANGE_OVERFLOW;
        }
        return GXOS_QUERY_JOB_STATUS_UNWRITABLE_OUTPUT;
    }
    if (report != 0) {
        report->output_alignment = (uint32_t)((uintptr_t)output & 3U);
        report->output_length_accepted = output_length;
    }
    if (return_length != 0) {
        status = gxos_query_job_validate_range(
            (uintptr_t)return_length, sizeof(uint32_t), memory,
            report == 0 ? 0 : &report->return_length_pointer_canonical,
            report == 0 ? 0 : &report->return_length_pointer_writable,
            report == 0 ? 0 : &report->return_length_range_valid);
        if (status != GXOS_QUERY_JOB_STATUS_OK) {
            if (status == GXOS_QUERY_JOB_STATUS_NONCANONICAL_OUTPUT) {
                return GXOS_QUERY_JOB_STATUS_NONCANONICAL_RETURN_LENGTH;
            }
            if (status == GXOS_QUERY_JOB_STATUS_RANGE_OVERFLOW) {
                return GXOS_QUERY_JOB_STATUS_RANGE_OVERFLOW;
            }
            return GXOS_QUERY_JOB_STATUS_UNWRITABLE_RETURN_LENGTH;
        }
        if (report != 0) {
            report->return_length_alignment = (uint32_t)((uintptr_t)return_length & 3U);
            report->return_length_before = gxos_query_job_read_u32(
                (const uint8_t *)return_length);
            report->return_length_before_valid = 1;
        }
        if (gxos_query_job_ranges_alias(
                (uintptr_t)output, GXOS_QUERY_JOB_CPU_RATE_STRUCTURE_SIZE,
                (uintptr_t)return_length, sizeof(uint32_t))) {
            return GXOS_QUERY_JOB_STATUS_ALIASED_OUTPUTS;
        }
    }
    if (report != 0) {
        gxos_query_job_capture_output_before((const uint8_t *)output, report);
    }
    return GXOS_QUERY_JOB_STATUS_OK;
}

GXOS_QUERY_JOB_STATUS GXOS_QUERY_JOB_MS_ABI
gxos_query_information_job_object_checked(
    GXOS_QUERY_JOB_HANDLE job_handle,
    GXOS_QUERY_JOB_INFO_CLASS information_class,
    GXOS_QUERY_JOB_OUTPUT output,
    GXOS_QUERY_JOB_DWORD output_length,
    GXOS_QUERY_JOB_RETURN_LENGTH return_length,
    const GXOS_QUERY_JOB_FACTS *facts,
    const GXOS_SYSTEM_INFO_MEMORY_CONTEXT *memory,
    GXOS_QUERY_JOB_REPORT *report)
{
    GXOS_QUERY_JOB_STATUS status;
    GXOS_QUERY_JOB_CPU_RATE_INFORMATION local_output;

    gxos_query_job_zero_report(report);
    if (job_handle != GXOS_QUERY_JOB_CURRENT_HANDLE) {
        return GXOS_QUERY_JOB_STATUS_INVALID_HANDLE;
    }
    if (information_class != GXOS_QUERY_JOB_CPU_RATE_CLASS) {
        return GXOS_QUERY_JOB_STATUS_UNSUPPORTED_INFORMATION_CLASS;
    }
    if (sizeof(GXOS_QUERY_JOB_CPU_RATE_INFORMATION) !=
        GXOS_QUERY_JOB_CPU_RATE_STRUCTURE_SIZE) {
        return GXOS_QUERY_JOB_STATUS_LAYOUT_MISMATCH;
    }
    if (gxos_query_job_validate_memory(memory) != GXOS_QUERY_JOB_STATUS_OK) {
        return GXOS_QUERY_JOB_STATUS_INVALID_JOB_FACTS;
    }
    status = gxos_query_job_validate_output(
        output, output_length, return_length, memory, report);
    if (status != GXOS_QUERY_JOB_STATUS_OK) return status;
    status = gxos_query_job_validate_facts(facts);
    if (status != GXOS_QUERY_JOB_STATUS_OK) return status;
    if (facts->associated_job == 0) {
        if (report != 0) {
            gxos_query_job_capture_output_after((const uint8_t *)output, report);
        }
        return GXOS_QUERY_JOB_STATUS_NO_ASSOCIATED_JOB;
    }

    local_output.control_flags = facts->control_flags;
    local_output.rate.cpu_rate = 0;
    if ((facts->control_flags & GXOS_QUERY_JOB_CPU_RATE_WEIGHT_BASED) != 0) {
        local_output.rate.weight = facts->weight;
    } else if ((facts->control_flags & GXOS_QUERY_JOB_CPU_RATE_MIN_MAX) != 0) {
        local_output.rate.rate_range.min_rate = facts->min_rate;
        local_output.rate.rate_range.max_rate = facts->max_rate;
    } else {
        local_output.rate.cpu_rate = facts->cpu_rate;
    }
    gxos_query_job_write_u32((uint8_t *)output, local_output.control_flags);
    gxos_query_job_write_u32((uint8_t *)output + 4, local_output.rate.cpu_rate);
    if (report != 0) report->output_written = 1;
    if (return_length != 0) {
        gxos_query_job_write_u32((uint8_t *)return_length,
                                 GXOS_QUERY_JOB_CPU_RATE_STRUCTURE_SIZE);
        if (report != 0) {
            report->return_length_written = 1;
            report->return_length_after = *return_length;
            report->return_length_after_valid = 1;
        }
    }
    if (report != 0) {
        gxos_query_job_capture_output_after((const uint8_t *)output, report);
    }
    return GXOS_QUERY_JOB_STATUS_OK;
}

void gxos_query_information_job_object_configure_probe(
    const GXOS_QUERY_JOB_FACTS *facts,
    const GXOS_SYSTEM_INFO_MEMORY_CONTEXT *memory)
{
    g_probe_facts = facts;
    g_probe_memory = memory;
}

GXOS_QUERY_JOB_BOOL GXOS_QUERY_JOB_MS_ABI
gxos_query_information_job_object_abi_probe(
    GXOS_QUERY_JOB_HANDLE job_handle,
    GXOS_QUERY_JOB_INFO_CLASS information_class,
    GXOS_QUERY_JOB_OUTPUT output,
    GXOS_QUERY_JOB_DWORD output_length,
    GXOS_QUERY_JOB_RETURN_LENGTH return_length)
{
    GXOS_QUERY_JOB_REPORT report;
    return gxos_query_information_job_object_checked(
               job_handle, information_class, output, output_length,
               return_length, g_probe_facts, g_probe_memory, &report) ==
               GXOS_QUERY_JOB_STATUS_OK
               ? GXOS_QUERY_JOB_TRUE
               : GXOS_QUERY_JOB_FALSE;
}

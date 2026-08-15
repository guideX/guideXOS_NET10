#include "platform_multibyte.h"

static void zero_bytes(void *destination, size_t count)
{
    uint8_t *bytes = (uint8_t *)destination;
    while (count-- != 0) *bytes++ = 0;
}

static int canonical_address(uintptr_t address)
{
#if UINTPTR_MAX > 0xFFFFFFFFU
    uint64_t high = (uint64_t)address >> 47;
    return high == 0 || high == 0x1FFFFU;
#else
    (void)address;
    return 1;
#endif
}

static int context_valid(const GXOS_MULTIBYTE_MEMORY_CONTEXT *memory)
{
    uint32_t index;

    if (memory == 0 || memory->regions == 0 || memory->region_count == 0 ||
        memory->region_count > GXOS_MULTIBYTE_MAX_MEMORY_REGIONS) {
        return 0;
    }
    for (index = 0; index != memory->region_count; ++index) {
        const GXOS_MULTIBYTE_MEMORY_REGION *region = &memory->regions[index];
        if (region->base == 0 || region->base >= region->end ||
            !canonical_address(region->base) ||
            !canonical_address(region->end - 1U)) {
            return 0;
        }
    }
    return 1;
}

static const GXOS_MULTIBYTE_MEMORY_REGION *find_region(
    const GXOS_MULTIBYTE_MEMORY_CONTEXT *memory,
    uintptr_t address)
{
    uint32_t index;

    for (index = 0; index != memory->region_count; ++index) {
        const GXOS_MULTIBYTE_MEMORY_REGION *region = &memory->regions[index];
        if (address >= region->base && address < region->end) return region;
    }
    return 0;
}

static GXOS_MULTIBYTE_STATUS range_covered(
    const GXOS_MULTIBYTE_MEMORY_CONTEXT *memory,
    uintptr_t address,
    uint64_t length,
    uint32_t writable,
    GXOS_MULTIBYTE_STATUS null_status,
    GXOS_MULTIBYTE_STATUS noncanonical_status,
    GXOS_MULTIBYTE_STATUS inaccessible_status,
    GXOS_MULTIBYTE_STATUS overflow_status)
{
    uintptr_t current = address;
    uint64_t remaining = length;

    if (length == 0) return GXOS_MULTIBYTE_STATUS_OK;
    if (address == 0) return null_status;
    if (!canonical_address(address)) return noncanonical_status;
    if (length > (uint64_t)UINTPTR_MAX - (uint64_t)address) {
        return overflow_status;
    }

    while (remaining != 0) {
        const GXOS_MULTIBYTE_MEMORY_REGION *region =
            find_region(memory, current);
        uintptr_t available;

        if (region == 0 || region->readable == 0 ||
            (writable != 0 && region->writable == 0)) {
            return inaccessible_status;
        }
        available = region->end - current;
        if ((uint64_t)available >= remaining) return GXOS_MULTIBYTE_STATUS_OK;
        current += available;
        remaining -= available;
    }
    return GXOS_MULTIBYTE_STATUS_OK;
}

static GXOS_MULTIBYTE_STATUS source_byte_range(
    const char *source,
    int32_t cb_multi_byte,
    const GXOS_MULTIBYTE_MEMORY_CONTEXT *memory,
    uint64_t *source_length,
    uint64_t *source_length_without_terminator)
{
    uintptr_t base;
    uint64_t index;
    GXOS_MULTIBYTE_STATUS status;

    if (source == 0) return GXOS_MULTIBYTE_STATUS_NULL_SOURCE;
    base = (uintptr_t)source;
    if (!canonical_address(base)) return GXOS_MULTIBYTE_STATUS_NONCANONICAL_SOURCE;

    if (cb_multi_byte > 0) {
        status = range_covered(
            memory, base, (uint64_t)(uint32_t)cb_multi_byte, 0,
            GXOS_MULTIBYTE_STATUS_NULL_SOURCE,
            GXOS_MULTIBYTE_STATUS_NONCANONICAL_SOURCE,
            GXOS_MULTIBYTE_STATUS_UNREADABLE_SOURCE,
            GXOS_MULTIBYTE_STATUS_SOURCE_RANGE_OVERFLOW);
        if (status != GXOS_MULTIBYTE_STATUS_OK) return status;
        *source_length = (uint64_t)(uint32_t)cb_multi_byte;
        *source_length_without_terminator = (uint64_t)(uint32_t)cb_multi_byte;
        return GXOS_MULTIBYTE_STATUS_OK;
    }

    for (index = 0; index != GXOS_MULTIBYTE_MAX_NUL_SCAN; ++index) {
        uintptr_t address;

        if (index > (uint64_t)UINTPTR_MAX - (uint64_t)base) {
            return GXOS_MULTIBYTE_STATUS_SOURCE_RANGE_OVERFLOW;
        }
        address = base + (uintptr_t)index;
        status = range_covered(
            memory, address, 1, 0,
            GXOS_MULTIBYTE_STATUS_NULL_SOURCE,
            GXOS_MULTIBYTE_STATUS_NONCANONICAL_SOURCE,
            GXOS_MULTIBYTE_STATUS_UNREADABLE_SOURCE,
            GXOS_MULTIBYTE_STATUS_SOURCE_RANGE_OVERFLOW);
        if (status != GXOS_MULTIBYTE_STATUS_OK) return status;
        if (*(const uint8_t *)(uintptr_t)address == 0) {
            *source_length = index + 1U;
            *source_length_without_terminator = index;
            return GXOS_MULTIBYTE_STATUS_OK;
        }
    }
    return GXOS_MULTIBYTE_STATUS_UNTERMINATED_SOURCE;
}

static int ranges_overlap(uintptr_t left, uint64_t left_length,
                          uintptr_t right, uint64_t right_length)
{
    uintptr_t left_end;
    uintptr_t right_end;

    if (left_length == 0 || right_length == 0) return 0;
    if (left > UINTPTR_MAX - (uintptr_t)left_length ||
        right > UINTPTR_MAX - (uintptr_t)right_length) {
        return 1;
    }
    left_end = left + (uintptr_t)left_length;
    right_end = right + (uintptr_t)right_length;
    return left < right_end && right < left_end;
}

static GXOS_MULTIBYTE_STATUS decode_one(
    const uint8_t *source,
    uint64_t source_length,
    uint64_t offset,
    uint32_t *code_point,
    uint32_t *consumed)
{
    uint8_t first;
    uint32_t value;
    uint32_t count;
    uint32_t minimum;
    uint32_t index;

    if (offset >= source_length || code_point == 0 || consumed == 0) {
        return GXOS_MULTIBYTE_STATUS_INVALID_UTF8;
    }
    first = source[offset];
    if (first <= 0x7FU) {
        *code_point = first;
        *consumed = 1;
        return GXOS_MULTIBYTE_STATUS_OK;
    }
    if (first >= 0xC2U && first <= 0xDFU) {
        count = 2;
        value = first & 0x1FU;
        minimum = 0x80U;
    } else if (first >= 0xE0U && first <= 0xEFU) {
        count = 3;
        value = first & 0x0FU;
        minimum = 0x800U;
    } else if (first >= 0xF0U && first <= 0xF4U) {
        count = 4;
        value = first & 0x07U;
        minimum = 0x10000U;
    } else {
        return GXOS_MULTIBYTE_STATUS_INVALID_UTF8;
    }
    if (count > source_length - offset) {
        return GXOS_MULTIBYTE_STATUS_INVALID_UTF8;
    }
    for (index = 1; index != count; ++index) {
        uint8_t continuation = source[offset + index];
        if ((continuation & 0xC0U) != 0x80U) {
            return GXOS_MULTIBYTE_STATUS_INVALID_UTF8;
        }
        if (index == 1U &&
            ((first == 0xE0U && continuation < 0xA0U) ||
             (first == 0xEDU && continuation > 0x9FU) ||
             (first == 0xF0U && continuation < 0x90U) ||
             (first == 0xF4U && continuation > 0x8FU))) {
            return GXOS_MULTIBYTE_STATUS_INVALID_UTF8;
        }
        value = (value << 6) | (continuation & 0x3FU);
    }
    if (value < minimum || value > 0x10FFFFU ||
        (value >= 0xD800U && value <= 0xDFFFU)) {
        return GXOS_MULTIBYTE_STATUS_INVALID_UTF8;
    }
    *code_point = value;
    *consumed = count;
    return GXOS_MULTIBYTE_STATUS_OK;
}

static void capture_bytes(uint8_t *destination, uint32_t *count,
                          const void *source, uint64_t byte_count)
{
    uint64_t index;
    uint32_t captured = byte_count < GXOS_MULTIBYTE_MAX_CAPTURE_BYTES
        ? (uint32_t)byte_count : GXOS_MULTIBYTE_MAX_CAPTURE_BYTES;
    const uint8_t *bytes = (const uint8_t *)source;

    if (destination == 0 || count == 0 || source == 0) return;
    for (index = 0; index != captured; ++index) destination[index] = bytes[index];
    *count = captured;
}

static void capture_output(GXOS_MULTIBYTE_REPORT *report,
                           const uint16_t *destination,
                           uint64_t unit_count)
{
    uint64_t index;
    uint32_t captured = unit_count < GXOS_MULTIBYTE_MAX_CAPTURE_UNITS
        ? (uint32_t)unit_count : GXOS_MULTIBYTE_MAX_CAPTURE_UNITS;

    if (report == 0 || destination == 0) return;
    for (index = 0; index != captured; ++index) {
        report->output_capture[index] = destination[index];
    }
    report->output_capture_count = captured;
}

static GXOS_MULTIBYTE_STATUS fail_result(
    GXOS_MULTIBYTE_REPORT *report,
    uint32_t *last_error,
    uint32_t error,
    GXOS_MULTIBYTE_STATUS status)
{
    if (report != 0) report->status = status;
    if (last_error != 0) *last_error = error;
    if (report != 0) report->last_error_after = error;
    return status;
}

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
    GXOS_MULTIBYTE_REPORT *report)
{
    uint64_t source_length = 0;
    uint64_t source_length_without_terminator = 0;
    uint64_t destination_bytes = 0;
    uint64_t required_units = 0;
    uint64_t offset = 0;
    uintptr_t source_address = (uintptr_t)source;
    uintptr_t destination_address = (uintptr_t)destination;
    GXOS_MULTIBYTE_STATUS status;
    uint32_t code_point;
    uint32_t consumed;

    if (report != 0) {
        zero_bytes(report, sizeof(*report));
        report->code_page = code_page;
        report->flags = flags;
        report->source = source_address;
        report->cb_multi_byte = cb_multi_byte;
        report->destination = destination_address;
        report->cch_wide_char = cch_wide_char;
        report->last_error_before = previous_last_error;
        report->last_error_after = previous_last_error;
        report->status = GXOS_MULTIBYTE_STATUS_INVALID_OUTPUT;
    }
    if (last_error == 0) {
        if (report != 0) report->status = GXOS_MULTIBYTE_STATUS_INVALID_OUTPUT;
        return 0;
    }
    if (!context_valid(memory)) {
        fail_result(report, last_error, GXOS_MULTIBYTE_ERROR_INVALID_PARAMETER,
                    GXOS_MULTIBYTE_STATUS_INVALID_CONTEXT);
        return 0;
    }
    if (code_page != GXOS_MULTIBYTE_CP_UTF8) {
        fail_result(report, last_error, GXOS_MULTIBYTE_ERROR_INVALID_PARAMETER,
                    GXOS_MULTIBYTE_STATUS_INVALID_CODE_PAGE);
        return 0;
    }
    if (flags != 0 && flags != GXOS_MULTIBYTE_MB_ERR_INVALID_CHARS) {
        fail_result(report, last_error, GXOS_MULTIBYTE_ERROR_INVALID_PARAMETER,
                    GXOS_MULTIBYTE_STATUS_INVALID_FLAGS);
        return 0;
    }
    if (cb_multi_byte == 0 || cb_multi_byte < -1) {
        fail_result(report, last_error, GXOS_MULTIBYTE_ERROR_INVALID_PARAMETER,
                    GXOS_MULTIBYTE_STATUS_INVALID_BYTE_COUNT);
        return 0;
    }
    if (destination == 0) {
        if (cch_wide_char != 0) {
            fail_result(report, last_error,
                        GXOS_MULTIBYTE_ERROR_INVALID_PARAMETER,
                        GXOS_MULTIBYTE_STATUS_NULL_DESTINATION);
            return 0;
        }
    } else {
        if (cch_wide_char <= 0) {
            fail_result(report, last_error,
                        GXOS_MULTIBYTE_ERROR_INVALID_PARAMETER,
                        GXOS_MULTIBYTE_STATUS_INVALID_BYTE_COUNT);
            return 0;
        }
        destination_bytes = (uint64_t)(uint32_t)cch_wide_char * 2U;
        status = range_covered(
            memory, destination_address, destination_bytes, 1,
            GXOS_MULTIBYTE_STATUS_NULL_DESTINATION,
            GXOS_MULTIBYTE_STATUS_NONCANONICAL_DESTINATION,
            GXOS_MULTIBYTE_STATUS_UNWRITABLE_DESTINATION,
            GXOS_MULTIBYTE_STATUS_DESTINATION_RANGE_OVERFLOW);
        if (status != GXOS_MULTIBYTE_STATUS_OK) {
            fail_result(report, last_error, GXOS_MULTIBYTE_ERROR_INVALID_PARAMETER,
                        status);
            return 0;
        }
        if (report != 0) {
            report->destination_range_valid = 1;
            capture_bytes(report->destination_before,
                          &report->destination_before_count,
                          destination, destination_bytes);
        }
    }

    status = source_byte_range(source, cb_multi_byte, memory, &source_length,
                               &source_length_without_terminator);
    if (status != GXOS_MULTIBYTE_STATUS_OK) {
        fail_result(report, last_error, GXOS_MULTIBYTE_ERROR_INVALID_PARAMETER,
                    status);
        return 0;
    }
    if (report != 0) {
        report->source_bytes_including_terminator = source_length;
        report->source_bytes_excluding_terminator =
            source_length_without_terminator;
        capture_bytes(report->source_capture, &report->source_capture_count,
                      source, source_length);
    }
    if (destination != 0 &&
        ranges_overlap(source_address, source_length, destination_address,
                       destination_bytes)) {
        fail_result(report, last_error, GXOS_MULTIBYTE_ERROR_INVALID_PARAMETER,
                    GXOS_MULTIBYTE_STATUS_OVERLAPPING_RANGES);
        return 0;
    }

    while (offset != source_length) {
        status = decode_one((const uint8_t *)source, source_length, offset,
                            &code_point, &consumed);
        if (status != GXOS_MULTIBYTE_STATUS_OK) {
            fail_result(report, last_error,
                        GXOS_MULTIBYTE_ERROR_NO_UNICODE_TRANSLATION, status);
            return 0;
        }
        if (required_units > UINT64_MAX -
                (code_point > 0xFFFFU ? 2U : 1U)) {
            fail_result(report, last_error,
                        GXOS_MULTIBYTE_ERROR_INVALID_PARAMETER,
                        GXOS_MULTIBYTE_STATUS_SIZE_OVERFLOW);
            return 0;
        }
        required_units += code_point > 0xFFFFU ? 2U : 1U;
        offset += consumed;
    }
    if (report != 0) report->required_utf16_units = required_units;
    if (required_units > 0x7FFFFFFFU) {
        fail_result(report, last_error, GXOS_MULTIBYTE_ERROR_INVALID_PARAMETER,
                    GXOS_MULTIBYTE_STATUS_SIZE_OVERFLOW);
        return 0;
    }
    if (destination == 0) {
        if (report != 0) report->status = GXOS_MULTIBYTE_STATUS_OK;
        *last_error = previous_last_error;
        if (report != 0) report->last_error_after = previous_last_error;
        return (int32_t)required_units;
    }
    if (required_units > (uint64_t)(uint32_t)cch_wide_char) {
        fail_result(report, last_error, GXOS_MULTIBYTE_ERROR_INSUFFICIENT_BUFFER,
                    GXOS_MULTIBYTE_STATUS_INSUFFICIENT_BUFFER);
        return 0;
    }

    offset = 0;
    required_units = 0;
    while (offset != source_length) {
        uint16_t *output = destination + required_units;
        status = decode_one((const uint8_t *)source, source_length, offset,
                            &code_point, &consumed);
        if (status != GXOS_MULTIBYTE_STATUS_OK) {
            /* The first pass already proved this cannot happen without an
               intervening memory mutation; retain failure atomicity anyway. */
            fail_result(report, last_error,
                        GXOS_MULTIBYTE_ERROR_NO_UNICODE_TRANSLATION, status);
            return 0;
        }
        if (code_point <= 0xFFFFU) {
            output[0] = (uint16_t)code_point;
            required_units += 1U;
        } else {
            uint32_t adjusted = code_point - 0x10000U;
            output[0] = (uint16_t)(0xD800U + (adjusted >> 10));
            output[1] = (uint16_t)(0xDC00U + (adjusted & 0x3FFU));
            required_units += 2U;
        }
        offset += consumed;
    }
    if (report != 0) {
        report->written_utf16_units = required_units;
        capture_output(report, destination, required_units);
        capture_bytes(report->destination_after,
                      &report->destination_after_count,
                      destination, destination_bytes);
        report->destination_zeroed_before_call = 1;
        for (offset = 0; offset != report->destination_before_count; ++offset) {
            if (report->destination_before[offset] != 0) {
                report->destination_zeroed_before_call = 0;
                break;
            }
        }
        report->status = GXOS_MULTIBYTE_STATUS_OK;
        report->last_error_after = previous_last_error;
    }
    *last_error = previous_last_error;
    return (int32_t)required_units;
}

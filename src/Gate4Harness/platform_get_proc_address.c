#include "platform_get_proc_address.h"

#include "platform_module_registry.h"

static int gxos_get_proc_address_is_canonical(uintptr_t address)
{
    uint64_t high = (uint64_t)address >> 47;
    return high == 0 || high == 0x1FFFFU;
}

static int gxos_get_proc_address_range_readable(
    uintptr_t address,
    const GXOS_GET_PROC_ADDRESS_MEMORY_CONTEXT *memory,
    const GXOS_GET_PROC_ADDRESS_MEMORY_REGION **region_out)
{
    uint32_t index;

    if (region_out != 0) *region_out = 0;
    if (address == 0 || memory == 0 || memory->regions == 0 ||
        memory->region_count == 0 || memory->region_count > 32U) {
        return 0;
    }
    for (index = 0; index != memory->region_count; ++index) {
        const GXOS_GET_PROC_ADDRESS_MEMORY_REGION *region =
            &memory->regions[index];
        if (region->base == 0 || region->end <= region->base ||
            !region->readable || address < region->base ||
            address >= region->end) {
            continue;
        }
        if (region_out != 0) *region_out = region;
        return 1;
    }
    return 0;
}

static void gxos_get_proc_address_zero_report(
    GXOS_GET_PROC_ADDRESS_REPORT *report)
{
    uint32_t index;

    if (report == 0) return;
    report->status = GXOS_GET_PROC_ADDRESS_STATUS_INVALID_MODULE_HANDLE;
    report->module_handle = 0;
    report->identifier_kind = GXOS_PROC_IDENTIFIER_NAME;
    report->identifier_raw = 0;
    report->identifier_high_order_bits = 0;
    report->identifier_low_order_word = 0;
    report->ordinal = 0;
    report->module_is_null = 0;
    report->module_pointer_canonical = 0;
    report->module_approved = 0;
    report->module_valid = 0;
    report->name_pointer_canonical = 0;
    report->name_readable = 0;
    report->name_terminated = 0;
    report->name_all_7bit_ascii = 1;
    report->name_high_bit_count = 0;
    report->name_length = 0;
    report->name_pointer = 0;
    report->name_terminator = 0;
    report->name_region_base = 0;
    report->name_region_end = 0;
    report->name_region_readable = 0;
    report->name_region_executable = 0;
    report->name_region_writable = 0;
    report->name_preview_length = 0;
    report->name_preview_truncated = 0;
    for (index = 0; index != GXOS_GET_PROC_ADDRESS_NAME_PREVIEW_BYTES; ++index) {
        report->name_preview[index] = 0;
    }
    report->export_lookup_attempted = 0;
    report->result = (GXOS_GET_PROC_ADDRESS_FARPROC)0;
    report->last_error_before = 0;
    report->last_error_after = 0;
}

GXOS_GET_PROC_ADDRESS_STATUS GXOS_GET_PROC_ADDRESS_MS_ABI
gxos_get_proc_address_classify(
    uintptr_t raw_identifier,
    GXOS_PROC_IDENTIFIER *identifier,
    GXOS_GET_PROC_ADDRESS_REPORT *report)
{
    if (identifier == 0) {
        return GXOS_GET_PROC_ADDRESS_STATUS_INVALID_MODULE_HANDLE;
    }
    identifier->raw = raw_identifier;
    identifier->high_order_bits = (uint64_t)raw_identifier >> 16;
    identifier->low_order_word = (uint16_t)raw_identifier;
    identifier->ordinal = 0;
    identifier->name = 0;
    if (raw_identifier <= (uintptr_t)0xFFFFU) {
        identifier->kind = GXOS_PROC_IDENTIFIER_ORDINAL;
        identifier->ordinal = identifier->low_order_word;
    } else {
        identifier->kind = GXOS_PROC_IDENTIFIER_NAME;
        identifier->name = (GXOS_GET_PROC_ADDRESS_LPCSTR)raw_identifier;
    }
    if (report != 0) {
        report->identifier_kind = identifier->kind;
        report->identifier_raw = identifier->raw;
        report->identifier_high_order_bits = identifier->high_order_bits;
        report->identifier_low_order_word = identifier->low_order_word;
        report->ordinal = identifier->ordinal;
        report->name_pointer = (uintptr_t)identifier->name;
    }
    return GXOS_GET_PROC_ADDRESS_STATUS_OK;
}

static GXOS_GET_PROC_ADDRESS_STATUS gxos_get_proc_address_scan_name(
    GXOS_PROC_IDENTIFIER *identifier,
    const GXOS_GET_PROC_ADDRESS_MEMORY_CONTEXT *memory,
    GXOS_GET_PROC_ADDRESS_REPORT *report)
{
    uintptr_t pointer;
    uint32_t index;

    if (identifier == 0 || report == 0 ||
        identifier->kind != GXOS_PROC_IDENTIFIER_NAME) {
        return GXOS_GET_PROC_ADDRESS_STATUS_UNSUPPORTED_ORDINAL;
    }
    pointer = (uintptr_t)identifier->name;
    if (!gxos_get_proc_address_is_canonical(pointer)) {
        return GXOS_GET_PROC_ADDRESS_STATUS_NONCANONICAL_NAME;
    }
    report->name_pointer_canonical = 1;
    for (index = 0; index != GXOS_GET_PROC_ADDRESS_MAX_NAME_BYTES; ++index) {
        uintptr_t address;
        const GXOS_GET_PROC_ADDRESS_MEMORY_REGION *region = 0;
        uint8_t value;

        if ((uintptr_t)index > UINTPTR_MAX - pointer) {
            return GXOS_GET_PROC_ADDRESS_STATUS_POINTER_OVERFLOW;
        }
        address = pointer + (uintptr_t)index;
        if (!gxos_get_proc_address_range_readable(address, memory, &region)) {
            return GXOS_GET_PROC_ADDRESS_STATUS_UNREADABLE_NAME;
        }
        report->name_readable = 1;
        if (region != 0) {
            report->name_region_base = region->base;
            report->name_region_end = region->end;
            report->name_region_readable = region->readable;
            report->name_region_executable = region->executable;
            report->name_region_writable = region->writable;
        }
        value = *((const uint8_t *)(uintptr_t)address);
        if (value == 0) {
            report->name_length = index;
            report->name_terminator = address;
            report->name_terminated = 1;
            return GXOS_GET_PROC_ADDRESS_STATUS_OK;
        }
        if (value >= 0x80U) {
            report->name_all_7bit_ascii = 0;
            if (report->name_high_bit_count != UINT32_MAX) {
                report->name_high_bit_count++;
            }
        }
        if (report->name_preview_length <
                GXOS_GET_PROC_ADDRESS_NAME_PREVIEW_BYTES) {
            report->name_preview[report->name_preview_length++] = value;
        } else {
            report->name_preview_truncated = 1;
        }
    }
    return GXOS_GET_PROC_ADDRESS_STATUS_NAME_SCAN_LIMIT;
}

GXOS_GET_PROC_ADDRESS_STATUS GXOS_GET_PROC_ADDRESS_MS_ABI
gxos_get_proc_address_checked(
    GXOS_GET_PROC_ADDRESS_HMODULE module_handle,
    GXOS_GET_PROC_ADDRESS_LPCSTR procedure_identifier,
    const GXOS_GET_PROC_ADDRESS_MEMORY_CONTEXT *memory,
    GXOS_GET_PROC_ADDRESS_DWORD previous_last_error,
    GXOS_GET_PROC_ADDRESS_FARPROC *result,
    GXOS_GET_PROC_ADDRESS_DWORD *last_error,
    GXOS_GET_PROC_ADDRESS_REPORT *report)
{
    GXOS_GET_PROC_ADDRESS_REPORT local_report;
    GXOS_GET_PROC_ADDRESS_REPORT *active_report = report;
    GXOS_PROC_IDENTIFIER identifier;
    GXOS_GET_PROC_ADDRESS_STATUS status;

    if (active_report == 0) active_report = &local_report;
    gxos_get_proc_address_zero_report(active_report);
    if (result == 0 || last_error == 0) {
        return GXOS_GET_PROC_ADDRESS_STATUS_INVALID_MODULE_HANDLE;
    }
    *result = (GXOS_GET_PROC_ADDRESS_FARPROC)0;
    *last_error = previous_last_error;
    active_report->module_handle = module_handle;
    active_report->module_is_null = module_handle == 0;
    active_report->module_pointer_canonical =
        module_handle == 0 || gxos_get_proc_address_is_canonical(module_handle);
    active_report->last_error_before = previous_last_error;
    status = gxos_get_proc_address_classify(
        (uintptr_t)procedure_identifier, &identifier, active_report);
    if (status != GXOS_GET_PROC_ADDRESS_STATUS_OK) {
        active_report->status = status;
        active_report->last_error_after = *last_error;
        return status;
    }
    if (identifier.kind == GXOS_PROC_IDENTIFIER_NAME) {
        status = gxos_get_proc_address_scan_name(&identifier, memory,
                                                 active_report);
        if (status != GXOS_GET_PROC_ADDRESS_STATUS_OK) {
            active_report->status = status;
            *last_error = GXOS_GET_PROC_ADDRESS_ERROR_PROC_NOT_FOUND;
            active_report->last_error_after = *last_error;
            return status;
        }
    } else {
        status = GXOS_GET_PROC_ADDRESS_STATUS_UNSUPPORTED_ORDINAL;
        active_report->status = status;
        *last_error = GXOS_GET_PROC_ADDRESS_ERROR_PROC_NOT_FOUND;
        active_report->last_error_after = *last_error;
        return status;
    }
    if (module_handle == 0) {
        active_report->status = GXOS_GET_PROC_ADDRESS_STATUS_INVALID_MODULE_HANDLE;
        *last_error = GXOS_GET_PROC_ADDRESS_ERROR_PROC_NOT_FOUND;
        active_report->last_error_after = *last_error;
        return active_report->status;
    }
    if (gxos_module_registry_is_kernel32_handle(module_handle)) {
        active_report->module_approved = 1;
        active_report->module_valid = 1;
        active_report->export_lookup_attempted = 1;
        active_report->status = GXOS_GET_PROC_ADDRESS_STATUS_EXPORT_NOT_FOUND;
        *last_error = GXOS_GET_PROC_ADDRESS_ERROR_PROC_NOT_FOUND;
        active_report->last_error_after = *last_error;
        return active_report->status;
    }
    if (!active_report->module_pointer_canonical) {
        active_report->status = GXOS_GET_PROC_ADDRESS_STATUS_INVALID_MODULE_HANDLE;
        *last_error = GXOS_GET_PROC_ADDRESS_ERROR_INVALID_HANDLE;
        active_report->last_error_after = *last_error;
        return active_report->status;
    }
    active_report->status = GXOS_GET_PROC_ADDRESS_STATUS_MODULE_NOT_MAPPED;
    *last_error = GXOS_GET_PROC_ADDRESS_ERROR_INVALID_HANDLE;
    active_report->last_error_after = *last_error;
    return active_report->status;
}

const char *gxos_get_proc_address_status_name(
    GXOS_GET_PROC_ADDRESS_STATUS status)
{
    switch (status) {
        case GXOS_GET_PROC_ADDRESS_STATUS_OK: return "OK";
        case GXOS_GET_PROC_ADDRESS_STATUS_INVALID_MODULE_HANDLE: return "INVALID_MODULE_HANDLE";
        case GXOS_GET_PROC_ADDRESS_STATUS_MODULE_NOT_MAPPED: return "MODULE_NOT_MAPPED";
        case GXOS_GET_PROC_ADDRESS_STATUS_NONCANONICAL_NAME: return "NONCANONICAL_NAME";
        case GXOS_GET_PROC_ADDRESS_STATUS_UNREADABLE_NAME: return "UNREADABLE_NAME";
        case GXOS_GET_PROC_ADDRESS_STATUS_UNTERMINATED_NAME: return "UNTERMINATED_NAME";
        case GXOS_GET_PROC_ADDRESS_STATUS_NAME_SCAN_LIMIT: return "NAME_SCAN_LIMIT";
        case GXOS_GET_PROC_ADDRESS_STATUS_POINTER_OVERFLOW: return "POINTER_OVERFLOW";
        case GXOS_GET_PROC_ADDRESS_STATUS_UNSUPPORTED_ORDINAL: return "UNSUPPORTED_ORDINAL";
        case GXOS_GET_PROC_ADDRESS_STATUS_EXPORT_NOT_FOUND: return "EXPORT_NOT_FOUND";
        case GXOS_GET_PROC_ADDRESS_STATUS_INVALID_IMAGE: return "INVALID_IMAGE";
        case GXOS_GET_PROC_ADDRESS_STATUS_INVALID_EXPORT_DIRECTORY: return "INVALID_EXPORT_DIRECTORY";
        case GXOS_GET_PROC_ADDRESS_STATUS_INVALID_EXPORT_TABLE: return "INVALID_EXPORT_TABLE";
        case GXOS_GET_PROC_ADDRESS_STATUS_FORWARDED_EXPORT_UNSUPPORTED: return "FORWARDED_EXPORT_UNSUPPORTED";
        case GXOS_GET_PROC_ADDRESS_STATUS_INVALID_FUNCTION_RVA: return "INVALID_FUNCTION_RVA";
        default: return "UNKNOWN";
    }
}

const char *gxos_get_proc_address_identifier_kind_name(
    GXOS_PROC_IDENTIFIER_KIND kind)
{
    return kind == GXOS_PROC_IDENTIFIER_ORDINAL ? "ORDINAL" : "NAME";
}

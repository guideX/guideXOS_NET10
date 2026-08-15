#include "platform_load_library.h"

#include "platform_module_registry.h"

static int gxos_load_library_is_canonical(uintptr_t address)
{
    uint64_t high = (uint64_t)address >> 47;
    return high == 0 || high == 0x1FFFFU;
}

static int gxos_load_library_range_inside(uintptr_t address,
                                          uintptr_t bytes,
                                          uintptr_t base,
                                          uintptr_t end)
{
    if (bytes == 0 || base == 0 || end <= base || address < base ||
        address > end || bytes > UINTPTR_MAX - address) {
        return 0;
    }
    return address + bytes <= end;
}

static const GXOS_LOAD_LIBRARY_MEMORY_REGION *gxos_load_library_find_region(
    uintptr_t address,
    uintptr_t bytes,
    const GXOS_LOAD_LIBRARY_MEMORY_CONTEXT *memory)
{
    uint32_t index;

    if (memory == 0 || memory->regions == 0 || memory->region_count == 0 ||
        memory->region_count > 32U) {
        return 0;
    }
    for (index = 0; index != memory->region_count; ++index) {
        const GXOS_LOAD_LIBRARY_MEMORY_REGION *region =
            &memory->regions[index];
        if (!region->readable) continue;
        if (gxos_load_library_range_inside(address, bytes,
                                            region->base, region->end)) {
            return region;
        }
    }
    return 0;
}

static uint16_t gxos_load_library_read_u16(uintptr_t address)
{
    const uint8_t *bytes = (const uint8_t *)(uintptr_t)address;
    return (uint16_t)bytes[0] | ((uint16_t)bytes[1] << 8);
}

static void gxos_load_library_clear_report(
    GXOS_LOAD_LIBRARY_REPORT *report,
    GXOS_LOAD_LIBRARY_HFILE hfile,
    uint32_t flags,
    uint32_t previous_last_error)
{
    if (report == 0) return;
    report->status = GXOS_LOAD_LIBRARY_STATUS_INVALID_PARAMETER;
    report->selected_module = GXOS_LOAD_LIBRARY_SELECTED_NONE;
    report->hfile = hfile;
    report->flags = flags;
    report->flags_exact = flags == GXOS_LOAD_LIBRARY_SEARCH_SYSTEM32;
    report->hfile_is_null = hfile == 0;
    report->name_is_null = 0;
    report->name_pointer_canonical = 0;
    report->name_readable = 0;
    report->name_region_readable = 0;
    report->name_region_executable = 0;
    report->name_region_writable = 0;
    report->name_region_base = 0;
    report->name_region_end = 0;
    report->name_length = 0;
    report->name_terminator = 0;
    report->name_has_path = 0;
    report->name_has_extension = 0;
    report->name_matches_kernel32 = 0;
    report->system32_search_applied = 0;
    report->result = 0;
    report->last_error_before = previous_last_error;
    report->last_error_after = previous_last_error;
}

static GXOS_LOAD_LIBRARY_STATUS gxos_load_library_scan_name(
    GXOS_LOAD_LIBRARY_LPCWSTR module_name,
    const GXOS_LOAD_LIBRARY_MEMORY_CONTEXT *memory,
    GXOS_LOAD_LIBRARY_REPORT *report)
{
    uintptr_t pointer;
    uint32_t length = 0;

    if (module_name == 0) {
        report->name_is_null = 1;
        return GXOS_LOAD_LIBRARY_STATUS_INVALID_PARAMETER;
    }
    pointer = (uintptr_t)module_name;
    if (!gxos_load_library_is_canonical(pointer)) {
        return GXOS_LOAD_LIBRARY_STATUS_NONCANONICAL_NAME;
    }
    report->name_pointer_canonical = 1;
    while (length != GXOS_LOAD_LIBRARY_MAX_NAME_CODE_UNITS) {
        uintptr_t unit_address;
        const GXOS_LOAD_LIBRARY_MEMORY_REGION *region;
        uint16_t unit;

        if ((uintptr_t)length > (UINTPTR_MAX - pointer) / 2U) {
            return GXOS_LOAD_LIBRARY_STATUS_POINTER_OVERFLOW;
        }
        unit_address = pointer + (uintptr_t)length * 2U;
        region = gxos_load_library_find_region(unit_address, 2U, memory);
        if (region == 0) {
            return GXOS_LOAD_LIBRARY_STATUS_UNREADABLE_NAME;
        }
        report->name_readable = 1;
        report->name_region_readable = region->readable;
        report->name_region_executable = region->executable;
        report->name_region_writable = region->writable;
        report->name_region_base = region->base;
        report->name_region_end = region->end;
        unit = gxos_load_library_read_u16(unit_address);
        if (unit == 0) {
            report->name_length = length;
            report->name_terminator = unit_address;
            break;
        }
        if (unit == (uint16_t)'/' || unit == (uint16_t)'\\') {
            report->name_has_path = 1;
        }
        if (unit == (uint16_t)'.') report->name_has_extension = 1;
        ++length;
    }
    if (report->name_terminator == 0) {
        return length == GXOS_LOAD_LIBRARY_MAX_NAME_CODE_UNITS
                   ? GXOS_LOAD_LIBRARY_STATUS_NAME_SCAN_LIMIT
                   : GXOS_LOAD_LIBRARY_STATUS_UNTERMINATED_NAME;
    }
    report->name_matches_kernel32 =
        !report->name_has_path &&
        gxos_module_registry_kernel32_name_matches(module_name,
                                                   report->name_length);
    if (report->name_has_path) {
        return GXOS_LOAD_LIBRARY_STATUS_UNSUPPORTED_PATH;
    }
    if (!report->name_matches_kernel32) {
        return GXOS_LOAD_LIBRARY_STATUS_MODULE_NOT_FOUND;
    }
    return GXOS_LOAD_LIBRARY_STATUS_OK;
}

GXOS_LOAD_LIBRARY_STATUS GXOS_LOAD_LIBRARY_MS_ABI
gxos_load_library_ex_checked(
    GXOS_LOAD_LIBRARY_LPCWSTR module_name,
    GXOS_LOAD_LIBRARY_HFILE hfile,
    uint32_t flags,
    const GXOS_LOAD_LIBRARY_MEMORY_CONTEXT *memory,
    uint32_t previous_last_error,
    GXOS_LOAD_LIBRARY_HMODULE *result,
    uint32_t *last_error,
    GXOS_LOAD_LIBRARY_REPORT *report)
{
    GXOS_LOAD_LIBRARY_REPORT local_report;
    GXOS_LOAD_LIBRARY_REPORT *active_report =
        report == 0 ? &local_report : report;
    GXOS_LOAD_LIBRARY_STATUS status;

    gxos_load_library_clear_report(active_report, hfile, flags,
                                   previous_last_error);
    if (result == 0 || last_error == 0) {
        return GXOS_LOAD_LIBRARY_STATUS_INVALID_PARAMETER;
    }
    *result = 0;
    *last_error = previous_last_error;
    if (hfile != 0) {
        status = GXOS_LOAD_LIBRARY_STATUS_INVALID_HFILE;
    } else if (flags != GXOS_LOAD_LIBRARY_SEARCH_SYSTEM32) {
        status = GXOS_LOAD_LIBRARY_STATUS_UNSUPPORTED_FLAGS;
    } else {
        status = gxos_load_library_scan_name(module_name, memory, active_report);
    }
    active_report->status = status;
    if (status == GXOS_LOAD_LIBRARY_STATUS_OK) {
        *result = gxos_module_registry_kernel32_handle();
        active_report->selected_module =
            GXOS_LOAD_LIBRARY_SELECTED_BUILTIN_KERNEL32;
        active_report->system32_search_applied = 1;
        active_report->result = *result;
        active_report->last_error_after = previous_last_error;
        return status;
    }
    if (status == GXOS_LOAD_LIBRARY_STATUS_MODULE_NOT_FOUND ||
        status == GXOS_LOAD_LIBRARY_STATUS_UNSUPPORTED_PATH) {
        *last_error = GXOS_LOAD_LIBRARY_ERROR_MOD_NOT_FOUND;
    } else {
        *last_error = GXOS_LOAD_LIBRARY_ERROR_INVALID_PARAMETER;
    }
    active_report->last_error_after = *last_error;
    return status;
}

const char *gxos_load_library_status_name(GXOS_LOAD_LIBRARY_STATUS status)
{
    switch (status) {
        case GXOS_LOAD_LIBRARY_STATUS_OK: return "OK";
        case GXOS_LOAD_LIBRARY_STATUS_INVALID_PARAMETER: return "INVALID_PARAMETER";
        case GXOS_LOAD_LIBRARY_STATUS_NONCANONICAL_NAME: return "NONCANONICAL_NAME";
        case GXOS_LOAD_LIBRARY_STATUS_UNREADABLE_NAME: return "UNREADABLE_NAME";
        case GXOS_LOAD_LIBRARY_STATUS_UNTERMINATED_NAME: return "UNTERMINATED_NAME";
        case GXOS_LOAD_LIBRARY_STATUS_NAME_SCAN_LIMIT: return "NAME_SCAN_LIMIT";
        case GXOS_LOAD_LIBRARY_STATUS_POINTER_OVERFLOW: return "POINTER_OVERFLOW";
        case GXOS_LOAD_LIBRARY_STATUS_INVALID_HFILE: return "INVALID_HFILE";
        case GXOS_LOAD_LIBRARY_STATUS_UNSUPPORTED_FLAGS: return "UNSUPPORTED_FLAGS";
        case GXOS_LOAD_LIBRARY_STATUS_MODULE_NOT_FOUND: return "MODULE_NOT_FOUND";
        case GXOS_LOAD_LIBRARY_STATUS_UNSUPPORTED_PATH: return "UNSUPPORTED_PATH";
        default: return "UNKNOWN";
    }
}

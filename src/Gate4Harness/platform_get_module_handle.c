#include "platform_get_module_handle.h"

static GXOS_MAIN_MODULE_FACTS g_main_module;
static uint32_t g_main_module_configured;

static int gxos_module_is_canonical(uintptr_t address)
{
    uint64_t high = (uint64_t)address >> 47;
    return high == 0 || high == 0x1FFFFU;
}

static int gxos_module_range_inside(uintptr_t address,
                                    uintptr_t bytes,
                                    uintptr_t base,
                                    uintptr_t end)
{
    if (bytes == 0 || address < base || end < base || address > end) return 0;
    if (bytes > UINTPTR_MAX - address) return 0;
    return address + bytes <= end;
}

static const GXOS_MODULE_HANDLE_MEMORY_REGION *gxos_module_find_region(
    uintptr_t address,
    uintptr_t bytes,
    const GXOS_MAIN_MODULE_FACTS *main_module)
{
    uint32_t index;

    if (main_module == 0 || main_module->mapped_regions == 0 || bytes == 0) {
        return 0;
    }
    for (index = 0; index != main_module->mapped_region_count; ++index) {
        const GXOS_MODULE_HANDLE_MEMORY_REGION *region =
            &main_module->mapped_regions[index];
        if (region->base == 0 || region->end <= region->base) continue;
        if (gxos_module_range_inside(address, bytes, region->base, region->end)) {
            return region;
        }
    }
    return 0;
}

static int gxos_module_readable_range(
    uintptr_t address,
    uintptr_t bytes,
    const GXOS_MAIN_MODULE_FACTS *main_module,
    const GXOS_MODULE_HANDLE_MEMORY_REGION **region_out)
{
    uintptr_t image_end;
    uintptr_t header_end;
    const GXOS_MODULE_HANDLE_MEMORY_REGION *region;

    if (region_out != 0) *region_out = 0;
    if (main_module == 0 || main_module->mapped_image_base == 0 ||
        main_module->size_of_image == 0 ||
        main_module->size_of_image > UINTPTR_MAX - main_module->mapped_image_base) {
        return 0;
    }
    image_end = main_module->mapped_image_base +
                (uintptr_t)main_module->size_of_image;
    if (!gxos_module_range_inside(address, bytes,
                                  main_module->mapped_image_base, image_end)) {
        return 0;
    }
    if (main_module->size_of_headers > UINTPTR_MAX -
            main_module->mapped_image_base) {
        return 0;
    }
    header_end = main_module->mapped_image_base +
                 (uintptr_t)main_module->size_of_headers;
    if (gxos_module_range_inside(address, bytes,
                                 main_module->mapped_image_base, header_end)) {
        return 1;
    }
    region = gxos_module_find_region(address, bytes, main_module);
    if (region_out != 0) *region_out = region;
    return region != 0 && region->readable != 0;
}

static uint16_t gxos_module_read_u16(uintptr_t address)
{
    const uint8_t *bytes = (const uint8_t *)(uintptr_t)address;
    return (uint16_t)bytes[0] | ((uint16_t)bytes[1] << 8);
}

static uint32_t gxos_module_read_u32(uintptr_t address)
{
    return (uint32_t)gxos_module_read_u16(address) |
           ((uint32_t)gxos_module_read_u16(address + 2U) << 16);
}

static uintptr_t gxos_module_add_rva(uintptr_t base, uint32_t rva)
{
    if ((uintptr_t)rva > UINTPTR_MAX - base) return 0;
    return base + (uintptr_t)rva;
}

static void gxos_module_zero_report(GXOS_MODULE_HANDLE_REPORT *report)
{
    if (report == 0) return;
    report->status = GXOS_MODULE_HANDLE_STATUS_INVALID_MODULE_FACTS;
    report->selected_module = GXOS_MODULE_HANDLE_SELECTED_NONE;
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
    report->name_exact_observed_form = 0;
    report->dos_header_valid = 0;
    report->nt_header_valid = 0;
    report->machine_valid = 0;
    report->optional_header_valid = 0;
    report->size_of_image_valid = 0;
    report->image_range_valid = 0;
    report->entry_point_valid = 0;
    report->import_ownership_valid = 0;
    report->relocation_valid = 0;
    report->caller_read_mask = 0;
    report->output_written = 0;
    report->result = 0;
}

static int gxos_module_ascii_equal_ci(
    const GXOS_MODULE_HANDLE_WCHAR *value,
    uint32_t length,
    const char *ascii)
{
    uint32_t index = 0;
    while (ascii[index] != 0) {
        uint16_t expected;
        uint16_t actual;
        if (index >= length) return 0;
        expected = (uint16_t)(uint8_t)ascii[index];
        actual = value[index];
        if (actual >= (uint16_t)'A' && actual <= (uint16_t)'Z') {
            actual = (uint16_t)(actual + ((uint16_t)'a' - (uint16_t)'A'));
        }
        if (actual != expected) return 0;
        ++index;
    }
    return index == length;
}

static GXOS_MODULE_HANDLE_STATUS gxos_module_scan_name(
    GXOS_MODULE_HANDLE_LPCWSTR module_name,
    const GXOS_MAIN_MODULE_FACTS *main_module,
    GXOS_MODULE_HANDLE_REPORT *report)
{
    const GXOS_MODULE_HANDLE_MEMORY_REGION *region = 0;
    uintptr_t pointer;
    uint32_t length;
    uint32_t index;

    if (module_name == 0) {
        report->name_is_null = 1;
        return GXOS_MODULE_HANDLE_STATUS_OK;
    }
    pointer = (uintptr_t)module_name;
    if (!gxos_module_is_canonical(pointer)) {
        return GXOS_MODULE_HANDLE_STATUS_NONCANONICAL_NAME;
    }
    report->name_pointer_canonical = 1;
    length = 0;
    while (length != GXOS_MODULE_HANDLE_MAX_NAME_CODE_UNITS) {
        uintptr_t unit_address;
        uint16_t unit;
        if ((uintptr_t)length > (UINTPTR_MAX - pointer) / 2U) {
            return GXOS_MODULE_HANDLE_STATUS_POINTER_OVERFLOW;
        }
        unit_address = pointer + (uintptr_t)length * 2U;
        if (!gxos_module_readable_range(unit_address, 2U, main_module, &region)) {
            return GXOS_MODULE_HANDLE_STATUS_UNREADABLE_NAME;
        }
        report->name_readable = 1;
        if (region != 0) {
            report->name_region_readable = region->readable;
            report->name_region_executable = region->executable;
            report->name_region_writable = region->writable;
            report->name_region_base = region->base;
            report->name_region_end = region->end;
        }
        unit = gxos_module_read_u16(unit_address);
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
        return length == GXOS_MODULE_HANDLE_MAX_NAME_CODE_UNITS
                   ? GXOS_MODULE_HANDLE_STATUS_NAME_SCAN_LIMIT
                   : GXOS_MODULE_HANDLE_STATUS_UNTERMINATED_NAME;
    }
    for (index = 0; index != report->name_length; ++index) {
        uint16_t unit = module_name[index];
        if (unit == (uint16_t)'/' || unit == (uint16_t)'\\') report->name_has_path = 1;
        if (unit == (uint16_t)'.') report->name_has_extension = 1;
    }
    if (!report->name_has_path &&
        (gxos_module_ascii_equal_ci(module_name, report->name_length, "ntdll.dll") ||
         gxos_module_ascii_equal_ci(module_name, report->name_length, "kernel32.dll"))) {
        report->name_exact_observed_form = 1;
        return GXOS_MODULE_HANDLE_STATUS_MODULE_NOT_FOUND;
    }
    return GXOS_MODULE_HANDLE_STATUS_UNSUPPORTED_NAME;
}

static GXOS_MODULE_HANDLE_STATUS gxos_module_validate_facts(
    const GXOS_MAIN_MODULE_FACTS *main_module,
    GXOS_MODULE_HANDLE_REPORT *report)
{
    uintptr_t base;
    uintptr_t image_end;
    uintptr_t nt_address;
    uintptr_t optional_address;
    uintptr_t entry_address;
    uintptr_t import_address;
    uintptr_t importing_iat_address;
    uint32_t image_size;
    uint32_t entry_rva;
    uint64_t expected_delta;

    if (main_module == 0 || report == 0 ||
        main_module->preferred_image_base == 0 ||
        main_module->mapped_image_base == 0 ||
        main_module->size_of_image == 0 ||
        main_module->size_of_headers < 0x40U ||
        main_module->size_of_headers > main_module->size_of_image ||
        main_module->mapped_regions == 0 ||
        main_module->mapped_region_count == 0 ||
        main_module->relocations_applied == 0) {
        return GXOS_MODULE_HANDLE_STATUS_INVALID_MODULE_FACTS;
    }
    if (!gxos_module_is_canonical(main_module->mapped_image_base)) {
        return GXOS_MODULE_HANDLE_STATUS_INVALID_MODULE_BASE;
    }
    if (!gxos_module_is_canonical(main_module->preferred_image_base)) {
        return GXOS_MODULE_HANDLE_STATUS_INVALID_MODULE_BASE;
    }
    if (main_module->size_of_image > UINTPTR_MAX -
            main_module->mapped_image_base) {
        return GXOS_MODULE_HANDLE_STATUS_INVALID_IMAGE_RANGE;
    }
    base = main_module->mapped_image_base;
    image_end = base + (uintptr_t)main_module->size_of_image;
    if (!gxos_module_readable_range(base, main_module->size_of_headers,
                                    main_module, 0) ||
        !gxos_module_readable_range(base, 0x40U, main_module, 0)) {
        return GXOS_MODULE_HANDLE_STATUS_UNREADABLE_HEADERS;
    }
    report->image_range_valid = 1;
    if (gxos_module_read_u16(base) != 0x5A4DU) {
        return GXOS_MODULE_HANDLE_STATUS_INVALID_DOS_HEADER;
    }
    report->dos_header_valid = 1;
    nt_address = gxos_module_add_rva(base, gxos_module_read_u32(base + 0x3CU));
    if (nt_address == 0 || nt_address < base ||
        nt_address >= image_end ||
        !gxos_module_readable_range(nt_address, 24U, main_module, 0)) {
        return GXOS_MODULE_HANDLE_STATUS_INVALID_NT_HEADER;
    }
    report->caller_read_mask |= 0x0001U;
    if (gxos_module_read_u32(nt_address) != 0x00004550U) {
        return GXOS_MODULE_HANDLE_STATUS_INVALID_NT_HEADER;
    }
    report->nt_header_valid = 1;
    if (gxos_module_read_u16(nt_address + 4U) !=
            GXOS_MODULE_HANDLE_EXPECTED_MACHINE) {
        return GXOS_MODULE_HANDLE_STATUS_WRONG_MACHINE;
    }
    report->machine_valid = 1;
    if (nt_address > UINTPTR_MAX - 24U) {
        return GXOS_MODULE_HANDLE_STATUS_INVALID_NT_HEADER;
    }
    optional_address = nt_address + 24U;
    if (!gxos_module_readable_range(optional_address, 0x40U, main_module, 0) ||
        gxos_module_read_u16(optional_address) !=
            GXOS_MODULE_HANDLE_EXPECTED_PE32_PLUS) {
        return GXOS_MODULE_HANDLE_STATUS_WRONG_OPTIONAL_HEADER;
    }
    report->optional_header_valid = 1;
    report->caller_read_mask |= 0x0002U;
    image_size = gxos_module_read_u32(optional_address + 0x38U);
    entry_rva = gxos_module_read_u32(optional_address + 0x10U);
    if (image_size == 0 || image_size != main_module->size_of_image ||
        image_size > UINTPTR_MAX - base ||
        base + (uintptr_t)image_size != image_end) {
        return GXOS_MODULE_HANDLE_STATUS_INVALID_IMAGE_RANGE;
    }
    report->size_of_image_valid = 1;
    if (entry_rva != main_module->entry_point_rva ||
        entry_rva >= image_size ||
        main_module->runtime_entry_point == 0 ||
        main_module->runtime_entry_point != base + (uintptr_t)entry_rva) {
        return GXOS_MODULE_HANDLE_STATUS_INVALID_IMAGE_RANGE;
    }
    entry_address = main_module->runtime_entry_point;
    if (!gxos_module_is_canonical(entry_address) ||
        !gxos_module_readable_range(entry_address, 1U, main_module, 0)) {
        return GXOS_MODULE_HANDLE_STATUS_INVALID_IMAGE_RANGE;
    }
    report->entry_point_valid = 1;
    import_address = gxos_module_add_rva(base, main_module->import_directory_rva);
    importing_iat_address = gxos_module_add_rva(base, main_module->importing_iat_rva);
    if (main_module->import_directory_rva == 0 ||
        main_module->importing_iat_rva == 0 ||
        import_address == 0 || main_module->import_directory_size == 0 ||
        !gxos_module_readable_range(import_address,
                                    main_module->import_directory_size,
                                    main_module, 0) ||
        importing_iat_address == 0 ||
        main_module->importing_iat_size < 8U ||
        (uint64_t)main_module->importing_iat_rva +
                main_module->importing_iat_size > image_size ||
        !gxos_module_readable_range(importing_iat_address,
                                    main_module->importing_iat_size,
                                    main_module, 0)) {
        return GXOS_MODULE_HANDLE_STATUS_INVALID_IMAGE_RANGE;
    }
    report->import_ownership_valid = 1;
    expected_delta = (uint64_t)(main_module->mapped_image_base -
                                main_module->preferred_image_base);
    if (expected_delta != main_module->relocation_delta) {
        return GXOS_MODULE_HANDLE_STATUS_RELOCATION_MISMATCH;
    }
    report->relocation_valid = 1;
    return GXOS_MODULE_HANDLE_STATUS_OK;
}

GXOS_MODULE_HANDLE_STATUS GXOS_MODULE_HANDLE_MS_ABI
gxos_get_module_handle_checked(
    GXOS_MODULE_HANDLE_LPCWSTR module_name,
    const GXOS_MAIN_MODULE_FACTS *main_module,
    GXOS_MODULE_HANDLE_HMODULE *module_handle_out,
    GXOS_MODULE_HANDLE_REPORT *report)
{
    GXOS_MODULE_HANDLE_REPORT local_report;
    GXOS_MODULE_HANDLE_STATUS status;
    GXOS_MODULE_HANDLE_REPORT *active_report =
        report == 0 ? &local_report : report;

    if (module_handle_out == 0) return GXOS_MODULE_HANDLE_STATUS_INVALID_MODULE_FACTS;
    gxos_module_zero_report(active_report);
    status = gxos_module_scan_name(module_name, main_module, active_report);
    active_report->status = status;
    if (status != GXOS_MODULE_HANDLE_STATUS_OK) return status;
    status = gxos_module_validate_facts(main_module, active_report);
    active_report->status = status;
    if (status != GXOS_MODULE_HANDLE_STATUS_OK) return status;
    *module_handle_out = main_module->mapped_image_base;
    active_report->selected_module =
        GXOS_MODULE_HANDLE_SELECTED_MAIN_NATIVEAOT_PAYLOAD;
    active_report->output_written = 1;
    active_report->result = *module_handle_out;
    active_report->status = GXOS_MODULE_HANDLE_STATUS_OK;
    return GXOS_MODULE_HANDLE_STATUS_OK;
}

void gxos_get_module_handle_configure(const GXOS_MAIN_MODULE_FACTS *main_module)
{
    if (main_module == 0) {
        g_main_module_configured = 0;
        return;
    }
    g_main_module = *main_module;
    g_main_module_configured = 1;
}

GXOS_MODULE_HANDLE_HMODULE GXOS_MODULE_HANDLE_MS_ABI
gxos_get_module_handle_w(GXOS_MODULE_HANDLE_LPCWSTR module_name)
{
    GXOS_MODULE_HANDLE_HMODULE result = 0;
    GXOS_MODULE_HANDLE_REPORT report;
    if (!g_main_module_configured ||
        gxos_get_module_handle_checked(module_name, &g_main_module, &result,
                                       &report) != GXOS_MODULE_HANDLE_STATUS_OK) {
        return 0;
    }
    return result;
}

#include "platform_get_module_handle_ex.h"

static int gxos_module_ex_is_canonical(uintptr_t address)
{
    uint64_t high = (uint64_t)address >> 47;
    return high == 0 || high == 0x1FFFFU;
}

static void gxos_module_ex_report_clear(GXOS_MODULE_HANDLE_EX_REPORT *report)
{
    if (report == 0) return;
    report->status = GXOS_MODULE_HANDLE_EX_STATUS_INVALID_IMAGE_FACTS;
    report->flags = 0;
    report->flags_exact = 0;
    report->unknown_flag_bits = 0;
    report->address_nonnull = 0;
    report->address_canonical = 0;
    report->address_in_image = 0;
    report->lookup_match_count = 0;
    report->lookup_unique = 0;
    report->output_pointer_nonnull = 0;
    report->output_pointer_canonical = 0;
    report->output_pointer_proven_writable = 0;
    report->output_written = 0;
    report->residency_invariant_proven = 0;
    report->prior_pinned = 0;
    report->resulting_pinned = 0;
    report->allocation_occurred = 0;
    report->image_free_or_unload_invoked = 0;
    report->prior_onexit_callback_executed = 0;
    report->image_identity = GXOS_MODULE_HANDLE_EX_IMAGE_NONE;
    report->address = 0;
    report->output_pointer = 0;
    report->output_value_before = 0;
    report->output_value_after = 0;
    report->selected_image_base = 0;
    report->selected_image_size = 0;
    report->address_rva = 0;
    report->result = 0;
}

static GXOS_MODULE_HANDLE_EX_STATUS gxos_module_ex_validate_image(
    const GXOS_MAIN_MODULE_FACTS *main_module,
    uintptr_t *image_end_out)
{
    uintptr_t image_end;
    uintptr_t expected_entry;
    uint32_t index;

    if (image_end_out != 0) *image_end_out = 0;
    if (main_module == 0 ||
        main_module->preferred_image_base == 0 ||
        main_module->mapped_image_base == 0 ||
        main_module->runtime_entry_point == 0 ||
        main_module->size_of_image == 0 ||
        main_module->size_of_headers == 0 ||
        main_module->size_of_headers > main_module->size_of_image ||
        main_module->mapped_regions == 0 ||
        main_module->mapped_region_count == 0 ||
        main_module->mapped_region_count > GXOS_CRT_INITTERM_MAX_MEMORY_REGIONS ||
        main_module->relocations_applied == 0 ||
        main_module->entry_point_rva >= main_module->size_of_image) {
        return GXOS_MODULE_HANDLE_EX_STATUS_INVALID_IMAGE_FACTS;
    }
    if ((uintptr_t)main_module->size_of_image >
            UINTPTR_MAX - main_module->mapped_image_base) {
        return GXOS_MODULE_HANDLE_EX_STATUS_IMAGE_RANGE_OVERFLOW;
    }
    if (!gxos_module_ex_is_canonical(main_module->preferred_image_base) ||
        !gxos_module_ex_is_canonical(main_module->mapped_image_base)) {
        return GXOS_MODULE_HANDLE_EX_STATUS_INVALID_IMAGE_FACTS;
    }
    if (main_module->entry_point_rva >
            UINTPTR_MAX - main_module->mapped_image_base) {
        return GXOS_MODULE_HANDLE_EX_STATUS_INVALID_IMAGE_FACTS;
    }
    expected_entry = main_module->mapped_image_base +
                     (uintptr_t)main_module->entry_point_rva;
    if (expected_entry != main_module->runtime_entry_point) {
        return GXOS_MODULE_HANDLE_EX_STATUS_INVALID_IMAGE_FACTS;
    }
    image_end = main_module->mapped_image_base +
                (uintptr_t)main_module->size_of_image;
    for (index = 0; index != main_module->mapped_region_count; ++index) {
        const GXOS_MODULE_HANDLE_MEMORY_REGION *region =
            &main_module->mapped_regions[index];
        if (region->base == 0 || region->end <= region->base ||
            region->base < main_module->mapped_image_base ||
            region->end > image_end) {
            return GXOS_MODULE_HANDLE_EX_STATUS_INVALID_IMAGE_FACTS;
        }
    }
    if (image_end_out != 0) *image_end_out = image_end;
    return GXOS_MODULE_HANDLE_EX_STATUS_OK;
}

GXOS_MODULE_HANDLE_EX_STATUS GXOS_MODULE_HANDLE_EX_MS_ABI
gxos_get_module_handle_ex_checked(
    uint32_t flags,
    uintptr_t address,
    GXOS_MODULE_HANDLE_HMODULE *module_handle_out,
    const GXOS_MAIN_MODULE_FACTS *main_module,
    uintptr_t output_lower,
    uintptr_t output_upper,
    uint32_t permanent_residency_proven,
    GXOS_MODULE_HANDLE_EX_REPORT *report)
{
    GXOS_MODULE_HANDLE_EX_REPORT local_report;
    GXOS_MODULE_HANDLE_EX_REPORT *active_report =
        report == 0 ? &local_report : report;
    GXOS_MODULE_HANDLE_EX_STATUS status;
    uintptr_t image_end = 0;
    uintptr_t output_value;

    gxos_module_ex_report_clear(active_report);
    active_report->flags = flags;
    active_report->flags_exact = flags == GXOS_MODULE_HANDLE_EX_EXPECTED_FLAGS;
    active_report->unknown_flag_bits =
        flags & ~(GXOS_MODULE_HANDLE_EX_EXPECTED_FLAGS);
    active_report->address = address;
    active_report->output_pointer = (uintptr_t)module_handle_out;
    active_report->address_nonnull = address != 0;
    active_report->address_canonical = gxos_module_ex_is_canonical(address);
    active_report->output_pointer_nonnull = module_handle_out != 0;
    active_report->output_pointer_canonical =
        module_handle_out != 0 &&
        gxos_module_ex_is_canonical((uintptr_t)module_handle_out);
    active_report->residency_invariant_proven =
        permanent_residency_proven != 0;
    active_report->prior_pinned = permanent_residency_proven != 0;
    active_report->resulting_pinned = permanent_residency_proven != 0;

    if (module_handle_out == 0) {
        active_report->status = GXOS_MODULE_HANDLE_EX_STATUS_NULL_OUTPUT;
        return active_report->status;
    }
    if (output_upper < output_lower ||
        !gxos_module_ex_is_canonical(output_lower) ||
        !gxos_module_ex_is_canonical(output_upper) ||
        (uintptr_t)module_handle_out < output_lower ||
        (uintptr_t)module_handle_out > output_upper ||
        sizeof(uintptr_t) > output_upper - (uintptr_t)module_handle_out) {
        active_report->status =
            GXOS_MODULE_HANDLE_EX_STATUS_OUTPUT_NOT_WRITABLE;
        return active_report->status;
    }
    active_report->output_pointer_proven_writable = 1;
    output_value = *(uintptr_t *)(uintptr_t)module_handle_out;
    active_report->output_value_before = output_value;
    active_report->output_value_after = output_value;

    if (flags != GXOS_MODULE_HANDLE_EX_EXPECTED_FLAGS) {
        active_report->status =
            GXOS_MODULE_HANDLE_EX_STATUS_UNSUPPORTED_FLAGS;
        return active_report->status;
    }
    if (address == 0) {
        active_report->status = GXOS_MODULE_HANDLE_EX_STATUS_NULL_ADDRESS;
        return active_report->status;
    }
    if (!active_report->address_canonical) {
        active_report->status =
            GXOS_MODULE_HANDLE_EX_STATUS_NONCANONICAL_ADDRESS;
        return active_report->status;
    }
    if (permanent_residency_proven == 0) {
        active_report->status =
            GXOS_MODULE_HANDLE_EX_STATUS_IMAGE_NOT_PERMANENT;
        return active_report->status;
    }
    status = gxos_module_ex_validate_image(main_module, &image_end);
    if (status != GXOS_MODULE_HANDLE_EX_STATUS_OK) {
        active_report->status = status;
        return status;
    }
    if (address < main_module->mapped_image_base || address >= image_end) {
        active_report->status =
            GXOS_MODULE_HANDLE_EX_STATUS_ADDRESS_OUTSIDE_IMAGE;
        return active_report->status;
    }
    active_report->address_in_image = 1;
    active_report->lookup_match_count = 1;
    active_report->lookup_unique = 1;
    active_report->image_identity =
        GXOS_MODULE_HANDLE_EX_IMAGE_MAIN_NATIVEAOT_PAYLOAD;
    active_report->selected_image_base = main_module->mapped_image_base;
    active_report->selected_image_size = main_module->size_of_image;
    active_report->address_rva =
        (uint32_t)(address - main_module->mapped_image_base);
    active_report->result = main_module->mapped_image_base;
    *module_handle_out = main_module->mapped_image_base;
    active_report->output_written = 1;
    active_report->output_value_after = *module_handle_out;
    active_report->resulting_pinned = 1;
    active_report->status = GXOS_MODULE_HANDLE_EX_STATUS_OK;
    return active_report->status;
}

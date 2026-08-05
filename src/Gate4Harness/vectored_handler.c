#include "vectored_handler.h"

static uint16_t gxos_veh_read_u16(const uint8_t *p)
{
    return (uint16_t)p[0] | ((uint16_t)p[1] << 8);
}

static uint32_t gxos_veh_read_u32(const uint8_t *p)
{
    return (uint32_t)gxos_veh_read_u16(p) |
           ((uint32_t)gxos_veh_read_u16(p + 2) << 16);
}

static int gxos_veh_range_contains(
    uint64_t offset,
    uint64_t length,
    uint64_t limit)
{
    return offset <= limit && length <= limit - offset;
}

static void gxos_veh_copy_name(char destination[9], const uint8_t *source)
{
    uint32_t i;
    for (i = 0; i != 8; i++) destination[i] = (char)source[i];
    destination[8] = 0;
}

static int gxos_veh_image_bounds(
    const GXOS_VEH_IMAGE *image,
    uintptr_t *image_end)
{
    uintptr_t size;

    if (image == 0 || image->identity == 0 || image->image_base == 0 ||
        image->image_size == 0 || image->image_size > (uint64_t)UINTPTR_MAX ||
        image->section_count == 0 ||
        image->section_count > GXOS_VEH_MAX_IMAGE_SECTIONS) {
        return GXOS_VEH_VALIDATION_BAD_IMAGE;
    }
    size = (uintptr_t)image->image_size;
    if (image->image_base > UINTPTR_MAX - size) {
        return GXOS_VEH_VALIDATION_IMAGE_OVERFLOW;
    }
    *image_end = image->image_base + size;
    if (*image_end <= image->image_base ||
        !gxos_exception_is_canonical((uint64_t)image->image_base) ||
        !gxos_exception_is_canonical((uint64_t)(*image_end - 1U))) {
        return GXOS_VEH_VALIDATION_BAD_IMAGE;
    }
    return GXOS_VEH_VALIDATION_OK;
}

static GXOS_VEH_VALIDATION_RESULT gxos_veh_find_callback(
    const GXOS_VEH_REGISTRY *registry,
    uintptr_t callback_address,
    GXOS_VEH_CALLBACK_DIAGNOSTICS *diagnostics)
{
    uint32_t image_index;

    if (diagnostics != 0) {
        uint32_t i;
        diagnostics->validation = GXOS_VEH_VALIDATION_BAD_IMAGE;
        diagnostics->image = 0;
        diagnostics->section_index = UINT32_MAX;
        diagnostics->section_executable = 0;
        diagnostics->section_readable = 0;
        diagnostics->section_writable = 0;
        diagnostics->callback_address = callback_address;
        diagnostics->image_base = 0;
        diagnostics->callback_rva = 0;
        for (i = 0; i != sizeof(diagnostics->section_name); i++) {
            diagnostics->section_name[i] = 0;
        }
    }
    if (callback_address == 0) return GXOS_VEH_VALIDATION_NULL_CALLBACK;
    if (!gxos_exception_is_canonical((uint64_t)callback_address)) {
        return GXOS_VEH_VALIDATION_NONCANONICAL_CALLBACK;
    }
    if (registry == 0 || registry->image_count > GXOS_VEH_MAX_IMAGES) {
        return GXOS_VEH_VALIDATION_BAD_REGISTRY;
    }
    for (image_index = 0; image_index != registry->image_count; image_index++) {
        const GXOS_VEH_IMAGE *image = registry->images[image_index];
        uintptr_t image_end = 0;
        GXOS_VEH_VALIDATION_RESULT image_result;
        uint32_t section_index;

        image_result = gxos_veh_image_bounds(image, &image_end);
        if (image_result != GXOS_VEH_VALIDATION_OK) return image_result;
        for (section_index = 0; section_index != image->section_count; section_index++) {
            const GXOS_VEH_SECTION *section = &image->sections[section_index];

            if (section->base == 0 || section->base >= section->end ||
                section->base < image->image_base || section->end > image_end ||
                !gxos_exception_is_canonical((uint64_t)section->base) ||
                !gxos_exception_is_canonical((uint64_t)(section->end - 1U))) {
                return GXOS_VEH_VALIDATION_BAD_SECTION;
            }
        }
        if (callback_address < image->image_base || callback_address >= image_end) continue;
        if (diagnostics != 0) {
            diagnostics->image = image;
            diagnostics->image_base = image->image_base;
            diagnostics->callback_rva = callback_address - image->image_base;
        }
        for (section_index = 0; section_index != image->section_count; section_index++) {
            const GXOS_VEH_SECTION *section = &image->sections[section_index];
            uint32_t executable = (section->characteristics & GXOS_VEH_SECTION_EXECUTABLE) != 0;
            uint32_t readable = (section->characteristics & GXOS_VEH_SECTION_READABLE) != 0;
            uint32_t writable = (section->characteristics & GXOS_VEH_SECTION_WRITABLE) != 0;
            if (callback_address < section->base || callback_address >= section->end) continue;
            if (diagnostics != 0) {
                diagnostics->section_index = section_index;
                diagnostics->section_executable = executable;
                diagnostics->section_readable = readable;
                diagnostics->section_writable = writable;
                for (uint32_t i = 0; i != sizeof(section->name); i++) {
                    diagnostics->section_name[i] = section->name[i];
                }
            }
            if (!executable) return GXOS_VEH_VALIDATION_NOT_EXECUTABLE;
            if (!readable) return GXOS_VEH_VALIDATION_NOT_READABLE;
            if (writable) return GXOS_VEH_VALIDATION_WRITABLE_SECTION;
            return GXOS_VEH_VALIDATION_OK;
        }
        return GXOS_VEH_VALIDATION_OUTSIDE_IMAGE;
    }
    return GXOS_VEH_VALIDATION_NO_IMAGE;
}

void gxos_veh_registry_init(GXOS_VEH_REGISTRY *registry)
{
    uint32_t i;
    if (registry == 0) return;
    for (i = 0; i != GXOS_VEH_REGISTRY_CAPACITY; i++) {
        registry->records[i].occupied = 0;
        registry->records[i].slot = i;
        registry->order[i] = 0;
    }
    for (i = 0; i != GXOS_VEH_MAX_IMAGES; i++) registry->images[i] = 0;
    registry->image_count = 0;
    registry->live_count = 0;
    registry->dispatch_active = 0;
    registry->next_registration_sequence = 1;
    registry->registration_attempt_count = 0;
    registry->allocation_count = 0;
}

int gxos_veh_registry_configure_images(
    GXOS_VEH_REGISTRY *registry,
    const GXOS_VEH_IMAGE *const *images,
    uint32_t image_count)
{
    uint32_t i;
    if (registry == 0 || image_count > GXOS_VEH_MAX_IMAGES ||
        (image_count != 0 && images == 0) || registry->live_count != 0 ||
        registry->dispatch_active != 0) {
        return 0;
    }
    for (i = 0; i != image_count; i++) {
        if (images[i] == 0) return 0;
    }
    for (i = 0; i != GXOS_VEH_MAX_IMAGES; i++) {
        registry->images[i] = i < image_count ? images[i] : 0;
    }
    registry->image_count = image_count;
    return 1;
}

#if defined(GXOS_VEH_ENABLE_TEST_RESET)
void gxos_veh_registry_reset_for_test(GXOS_VEH_REGISTRY *registry)
{
    gxos_veh_registry_init(registry);
}
#endif

int gxos_veh_registry_valid(const GXOS_VEH_REGISTRY *registry)
{
    uint32_t i;
    uint32_t j;
    uint32_t occupied_count = 0;
    if (registry == 0 || registry->live_count > GXOS_VEH_REGISTRY_CAPACITY ||
        registry->image_count > GXOS_VEH_MAX_IMAGES ||
        registry->next_registration_sequence == 0) return 0;
    for (i = 0; i != GXOS_VEH_REGISTRY_CAPACITY; i++) {
        if (registry->records[i].slot != i) return 0;
        if (registry->records[i].occupied != 0) {
            occupied_count++;
            if (registry->records[i].opaque_handle == 0 ||
                registry->records[i].registration_sequence == 0 ||
                registry->records[i].registration_sequence >=
                    registry->next_registration_sequence) return 0;
            for (j = i + 1; j != GXOS_VEH_REGISTRY_CAPACITY; j++) {
                if (registry->records[j].occupied != 0 &&
                    (registry->records[j].opaque_handle ==
                         registry->records[i].opaque_handle ||
                     registry->records[j].registration_sequence ==
                         registry->records[i].registration_sequence)) {
                    return 0;
                }
            }
        }
    }
    if (occupied_count != registry->live_count) return 0;
    for (i = 0; i != registry->live_count; i++) {
        uint32_t slot = registry->order[i];
        if (slot >= GXOS_VEH_REGISTRY_CAPACITY ||
            registry->records[slot].occupied == 0 ||
            registry->records[slot].opaque_handle == 0) return 0;
        for (j = i + 1; j != registry->live_count; j++) {
            if (registry->order[j] == slot) return 0;
        }
    }
    for (i = registry->live_count; i != GXOS_VEH_REGISTRY_CAPACITY; i++) {
        if (registry->order[i] >= GXOS_VEH_REGISTRY_CAPACITY) return 0;
    }
    return 1;
}

void *gxos_veh_registry_add(
    GXOS_VEH_REGISTRY *registry,
    uint32_t first,
    GXOS_VEH_CALLBACK callback,
    GXOS_VEH_CALLBACK_DIAGNOSTICS *diagnostics)
{
    GXOS_VEH_CALLBACK_DIAGNOSTICS local_diagnostics = {0};
    GXOS_VEH_VALIDATION_RESULT validation;
    uint32_t free_slot = UINT32_MAX;
    uint32_t insertion_position;
    uint32_t i;
    GXOS_VEH_RECORD *record;

    if (diagnostics == 0) diagnostics = &local_diagnostics;
    if (registry != 0) registry->registration_attempt_count++;
    if (registry == 0 || !gxos_veh_registry_valid(registry)) {
        if (diagnostics != 0) diagnostics->validation = GXOS_VEH_VALIDATION_BAD_REGISTRY;
        return 0;
    }
    if (registry->dispatch_active != 0) {
        if (diagnostics != 0) diagnostics->validation = GXOS_VEH_VALIDATION_REGISTRY_ACTIVE;
        return 0;
    }
    validation = gxos_veh_find_callback(registry, (uintptr_t)callback, diagnostics);
    if (validation != GXOS_VEH_VALIDATION_OK) {
        if (diagnostics != 0) diagnostics->validation = validation;
        return 0;
    }
    if (registry->live_count == GXOS_VEH_REGISTRY_CAPACITY) {
        if (diagnostics != 0) diagnostics->validation = GXOS_VEH_VALIDATION_REGISTRY_FULL;
        return 0;
    }
    if (registry->next_registration_sequence == UINT64_MAX) {
        if (diagnostics != 0) diagnostics->validation = GXOS_VEH_VALIDATION_SEQUENCE_EXHAUSTED;
        return 0;
    }
    for (i = 0; i != GXOS_VEH_REGISTRY_CAPACITY; i++) {
        if (registry->records[i].occupied == 0) {
            free_slot = i;
            break;
        }
    }
    if (free_slot == UINT32_MAX) {
        if (diagnostics != 0) diagnostics->validation = GXOS_VEH_VALIDATION_REGISTRY_FULL;
        return 0;
    }
    insertion_position = first == 0 ? registry->live_count : 0;
    for (i = registry->live_count; i > insertion_position; i--) {
        registry->order[i] = registry->order[i - 1U];
    }
    record = &registry->records[free_slot];
    record->occupied = 1;
    record->slot = free_slot;
    record->requested_first = first;
    record->callback = callback;
    record->callback_address = (uintptr_t)callback;
    record->registration_sequence = registry->next_registration_sequence++;
    record->opaque_handle = (uintptr_t)record;
    record->callback_image = diagnostics->image;
    record->callback_image_base = diagnostics->image_base;
    record->callback_rva = diagnostics->callback_rva;
    record->callback_section_index = diagnostics->section_index;
    record->callback_section_executable = diagnostics->section_executable;
    for (i = 0; i != sizeof(record->callback_section_name); i++) {
        record->callback_section_name[i] = diagnostics->section_name[i];
    }
    record->invocation_count = 0;
    record->last_return_value = 0;
    registry->order[insertion_position] = free_slot;
    registry->live_count++;
    diagnostics->validation = GXOS_VEH_VALIDATION_OK;
    return (void *)record->opaque_handle;
}

int gxos_veh_registry_handle_is_live(
    const GXOS_VEH_REGISTRY *registry,
    const void *handle)
{
    uint32_t i;
    if (registry == 0 || handle == 0) return 0;
    for (i = 0; i != GXOS_VEH_REGISTRY_CAPACITY; i++) {
        if (registry->records[i].occupied != 0 &&
            registry->records[i].opaque_handle == (uintptr_t)handle) return 1;
    }
    return 0;
}

const GXOS_VEH_RECORD *gxos_veh_registry_record(
    const GXOS_VEH_REGISTRY *registry,
    uint32_t slot)
{
    if (registry == 0 || slot >= GXOS_VEH_REGISTRY_CAPACITY) return 0;
    return &registry->records[slot];
}

uint32_t gxos_veh_registry_order_slot(
    const GXOS_VEH_REGISTRY *registry,
    uint32_t position)
{
    if (registry == 0 || position >= registry->live_count) return UINT32_MAX;
    return registry->order[position];
}

uint32_t gxos_veh_registry_live_count(const GXOS_VEH_REGISTRY *registry)
{
    return registry == 0 ? 0 : registry->live_count;
}

uint32_t gxos_veh_registry_dispatch_active(const GXOS_VEH_REGISTRY *registry)
{
    return registry == 0 ? 0 : registry->dispatch_active;
}

uint64_t gxos_veh_registry_allocation_count(const GXOS_VEH_REGISTRY *registry)
{
    return registry == 0 ? 0 : registry->allocation_count;
}

int gxos_veh_image_parse_pe(
    GXOS_VEH_IMAGE *image,
    const void *identity,
    uintptr_t image_base,
    uint64_t image_size)
{
    const uint8_t *base = (const uint8_t *)image_base;
    uint64_t nt_offset;
    uint64_t section_offset;
    uint32_t image_size_from_pe;
    uint16_t section_count;
    uint16_t optional_size;
    uint16_t i;

    if (image == 0 || identity == 0 || image_base == 0 || image_size < 0x40 ||
        image_size > (uint64_t)UINTPTR_MAX ||
        image_base > UINTPTR_MAX - (uintptr_t)image_size) return 0;
    if (gxos_veh_read_u16(base) != 0x5A4D) return 0;
    nt_offset = gxos_veh_read_u32(base + 0x3C);
    if (!gxos_veh_range_contains(nt_offset, 24, image_size)) return 0;
    if (base[nt_offset] != 'P' || base[nt_offset + 1] != 'E' ||
        base[nt_offset + 2] != 0 || base[nt_offset + 3] != 0) return 0;
    section_count = gxos_veh_read_u16(base + nt_offset + 6);
    optional_size = gxos_veh_read_u16(base + nt_offset + 20);
    if (gxos_veh_read_u16(base + nt_offset + 24) != 0x20B ||
        optional_size < 0xF0 || section_count == 0 ||
        section_count > GXOS_VEH_MAX_IMAGE_SECTIONS) return 0;
    if (!gxos_veh_range_contains(nt_offset + 24, optional_size, image_size)) return 0;
    image_size_from_pe = gxos_veh_read_u32(base + nt_offset + 24 + 0x38);
    if (image_size_from_pe == 0 || image_size_from_pe > image_size) return 0;
    section_offset = nt_offset + 24U + optional_size;
    if (section_offset < nt_offset ||
        !gxos_veh_range_contains(section_offset, (uint64_t)section_count * 40U, image_size)) {
        return 0;
    }
    image->identity = identity;
    image->image_base = image_base;
    image->image_size = image_size;
    image->section_count = section_count;
    for (i = 0; i != section_count; i++) {
        const uint8_t *section = base + section_offset + (uint64_t)i * 40U;
        uint32_t virtual_size = gxos_veh_read_u32(section + 8);
        uint32_t virtual_address = gxos_veh_read_u32(section + 12);
        uint32_t raw_size = gxos_veh_read_u32(section + 16);
        uint32_t characteristics = gxos_veh_read_u32(section + 36);
        uint32_t extent = virtual_size > raw_size ? virtual_size : raw_size;
        uintptr_t section_base;

        if (extent == 0 || (uint64_t)virtual_address + extent > image_size_from_pe ||
            image_base > UINTPTR_MAX - (uintptr_t)virtual_address) return 0;
        section_base = image_base + (uintptr_t)virtual_address;
        if ((uintptr_t)extent > UINTPTR_MAX - section_base) return 0;
        image->sections[i].base = section_base;
        image->sections[i].end = section_base + (uintptr_t)extent;
        image->sections[i].characteristics = characteristics;
        gxos_veh_copy_name(image->sections[i].name, section);
    }
    for (; i != GXOS_VEH_MAX_IMAGE_SECTIONS; i++) {
        image->sections[i].base = 0;
        image->sections[i].end = 0;
        image->sections[i].characteristics = 0;
        image->sections[i].name[0] = 0;
    }
    return 1;
}

int32_t gxos_veh_invoke_direct(
    GXOS_VEH_CALLBACK callback,
    GXOS_EXCEPTION_POINTERS_COMPAT *exception_pointers,
    void *context)
{
    (void)context;
    return callback(exception_pointers);
}

int gxos_veh_dispatch(
    GXOS_VEH_REGISTRY *registry,
    GXOS_EXCEPTION_POINTERS_COMPAT *exception_pointers,
    GXOS_VEH_INVOKER invoker,
    void *invoker_context,
    GXOS_VEH_DISPATCH_REPORT *report)
{
    uint32_t snapshot[GXOS_VEH_REGISTRY_CAPACITY];
    uint32_t i;

    if (report != 0) {
        report->snapshot_count = 0;
        report->invoked_count = 0;
        report->invalid_return_count = 0;
        report->stopped_on_continue_execution = 0;
        report->final_continue_search = 0;
        report->final_continue_execution = 0;
        report->final_slot = UINT32_MAX;
    }
    if (registry == 0 || exception_pointers == 0 ||
        !gxos_veh_registry_valid(registry) || registry->dispatch_active != 0) return 0;
    for (i = 0; i != registry->live_count; i++) {
        snapshot[i] = registry->order[i];
        if (report != 0) report->snapshot_slots[i] = snapshot[i];
    }
    if (report != 0) report->snapshot_count = registry->live_count;
    registry->dispatch_active = 1;
    for (i = 0; i != registry->live_count; i++) {
        uint32_t slot = snapshot[i];
        GXOS_VEH_RECORD *record = &registry->records[slot];
        int32_t result;
        record->invocation_count++;
        result = invoker == 0
            ? gxos_veh_invoke_direct(record->callback, exception_pointers, invoker_context)
            : invoker(record->callback, exception_pointers, invoker_context);
        record->last_return_value = result;
        if (report != 0) {
            uint32_t invoked = report->invoked_count;
            report->invoked_slots[invoked] = slot;
            report->invocation_numbers[invoked] = record->invocation_count;
            report->return_values[invoked] = result;
            report->invoked_count++;
        }
        if (result == GXOS_EXCEPTION_CONTINUE_EXECUTION) {
            registry->dispatch_active = 0;
            if (report != 0) {
                report->stopped_on_continue_execution = 1;
                report->final_continue_execution = 1;
                report->final_slot = slot;
            }
            return 1;
        }
        if (result != GXOS_EXCEPTION_CONTINUE_SEARCH && report != 0) {
            report->invalid_return_count++;
        }
    }
    registry->dispatch_active = 0;
    if (report != 0) report->final_continue_search = 1;
    return 1;
}

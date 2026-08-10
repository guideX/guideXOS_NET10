#include "global_memory_status_ex.h"

static void zero_bytes(void *memory, size_t bytes)
{
    uint8_t *cursor = (uint8_t *)memory;
    while (bytes-- != 0) *cursor++ = 0;
}

static void copy_bytes(uint8_t *destination, const uint8_t *source, size_t bytes)
{
    while (bytes-- != 0) *destination++ = *source++;
}

static int is_canonical(uintptr_t address)
{
#if UINTPTR_MAX > 0xFFFFFFFFU
    uint64_t high = (uint64_t)address >> 47;
    return high == 0 || high == 0x1FFFFU;
#else
    (void)address;
    return 1;
#endif
}

static int range_end(uintptr_t base, uint64_t bytes, uintptr_t *end)
{
    if (bytes == 0 || bytes > UINTPTR_MAX ||
        base > UINTPTR_MAX - (uintptr_t)bytes) return 0;
    *end = base + (uintptr_t)bytes;
    return 1;
}

static int range_is_writable(const GXOS_MEMORY_STATUS_EX_CONTEXT *context,
                             uintptr_t base, uint64_t bytes,
                             uint64_t *writable_range_bytes)
{
    uintptr_t end;
    uint32_t index;
    uint32_t region_seen = 0;

    *writable_range_bytes = 0;
    if (!range_end(base, bytes, &end) || !is_canonical(base) ||
        !is_canonical(end - 1U) || context == 0 ||
        context->region_count > GXOS_MEMORY_STATUS_EX_MAX_MEMORY_REGIONS ||
        (context->region_count != 0 && context->regions == 0)) {
        return 0;
    }
    for (index = 0; index != context->region_count; ++index) {
        const GXOS_MEMORY_STATUS_EX_MEMORY_REGION *region =
            &context->regions[index];
        if (region->base >= region->end || !is_canonical(region->base) ||
            !is_canonical(region->end - 1U)) return 0;
        if (base < region->end && region->base < end) {
            region_seen = 1;
            if (base < region->base || end > region->end ||
                region->writable == 0) return 0;
            *writable_range_bytes = (uint64_t)(region->end - base);
            return 1;
        }
    }
    if (region_seen != 0) return 0;

    /* Dynamic process allocations are proven writable only while committed
       in the bounded guideXOS arena and are not allowed to bypass a known
       non-writable image region above. */
    if (context->virtual_arena == 0 || !context->virtual_arena->valid ||
        !gxos_vm_arena_validate(context->virtual_arena)) return 0;
    for (index = 0; index != GXOS_VM_MAX_COMMITMENTS; ++index) {
        const GXOS_VM_COMMITMENT *commitment =
            &context->virtual_arena->commitments[index];
        if (!commitment->live) continue;
        if (base >= commitment->base &&
            range_end(commitment->base, commitment->bytes, &end) &&
            base + (uintptr_t)bytes <= end) {
            *writable_range_bytes = end - base;
            return 1;
        }
    }
    return 0;
}

static GXOS_MEMORY_STATUS_EX_STATUS map_query_status(
    GXOS_SNAPSHOT_STATUS status)
{
    switch (status) {
    case GXOS_SNAPSHOT_STATUS_INVALID_PHYSICAL:
        return GXOS_MEMORY_STATUS_EX_STATUS_INVALID_PHYSICAL;
    case GXOS_SNAPSHOT_STATUS_INVALID_COMMIT:
        return GXOS_MEMORY_STATUS_EX_STATUS_INVALID_COMMIT;
    case GXOS_SNAPSHOT_STATUS_INVALID_VIRTUAL:
        return GXOS_MEMORY_STATUS_EX_STATUS_INVALID_VIRTUAL;
    case GXOS_SNAPSHOT_STATUS_OVERFLOW:
        return GXOS_MEMORY_STATUS_EX_STATUS_INVALID_ACCOUNTING_VIEW;
    default:
        return GXOS_MEMORY_STATUS_EX_STATUS_INVALID_ACCOUNTING_VIEW;
    }
}

static int build_output(const GXOS_MEMORY_SNAPSHOT *view,
                        GXOS_MEMORY_STATUS_EX *output)
{
    if (view == 0 || output == 0 || !view->valid ||
        view->memory_load_percent > 100U ||
        view->available_physical_bytes > view->total_physical_bytes ||
        view->available_commit_bytes > view->commit_limit_bytes ||
        view->process_virtual_available_bytes >
            view->process_virtual_total_bytes) return 0;
    zero_bytes(output, sizeof(*output));
    output->dwLength = (uint32_t)GXOS_MEMORY_STATUS_EX_SIZE;
    output->dwMemoryLoad = view->memory_load_percent;
    output->ullTotalPhys = view->total_physical_bytes;
    output->ullAvailPhys = view->available_physical_bytes;
    output->ullTotalPageFile = view->commit_limit_bytes;
    output->ullAvailPageFile = view->available_commit_bytes;
    output->ullTotalVirtual = view->process_virtual_total_bytes;
    output->ullAvailVirtual = view->process_virtual_available_bytes;
    output->ullAvailExtendedVirtual = 0;
    return 1;
}

int GXOS_MEMORY_STATUS_EX_MS_ABI gxos_global_memory_status_ex_checked(
    GXOS_MEMORY_STATUS_EX *buffer,
    const GXOS_MEMORY_STATUS_EX_CONTEXT *context,
    GXOS_MEMORY_STATUS_EX_REPORT *report)
{
    GXOS_MEMORY_STATUS_EX local_output;
    GXOS_MEMORY_SNAPSHOT view;
    uintptr_t address = (uintptr_t)buffer;
    uint64_t writable_range_bytes = 0;
    uint32_t input_length;
    GXOS_SNAPSHOT_STATUS query_status;

    if (report != 0) zero_bytes(report, sizeof(*report));
    if (report != 0) report->buffer = address;
    if (buffer == 0) {
        if (report != 0) report->status = GXOS_MEMORY_STATUS_EX_STATUS_NULL_BUFFER;
        return 0;
    }
    if (!is_canonical(address)) {
        if (report != 0) {
            report->status = GXOS_MEMORY_STATUS_EX_STATUS_NONCANONICAL_BUFFER;
        }
        return 0;
    }
    if (!range_end(address, GXOS_MEMORY_STATUS_EX_SIZE, &address)) {
        if (report != 0) report->status = GXOS_MEMORY_STATUS_EX_STATUS_RANGE_OVERFLOW;
        return 0;
    }
    address -= (uintptr_t)GXOS_MEMORY_STATUS_EX_SIZE;
    if (context == 0 || context->accounting_generation == 0 ||
        !range_is_writable(context, address, GXOS_MEMORY_STATUS_EX_SIZE,
                           &writable_range_bytes)) {
        if (report != 0) {
            report->status = context == 0
                ? GXOS_MEMORY_STATUS_EX_STATUS_INVALID_CONTEXT
                : GXOS_MEMORY_STATUS_EX_STATUS_UNWRITABLE_BUFFER;
            report->writable_range_bytes = writable_range_bytes;
        }
        return 0;
    }
    if (context->accounting_generation_source != 0 &&
        *context->accounting_generation_source !=
            context->accounting_generation) {
        if (report != 0) {
            report->status = GXOS_MEMORY_STATUS_EX_STATUS_ACCOUNTING_CHANGED;
        }
        return 0;
    }
    if (report != 0) {
        report->buffer_canonical = 1;
        report->input_range_valid = 1;
        report->writable_range_bytes = writable_range_bytes;
    }

    input_length = buffer->dwLength;
    if (report != 0) report->input_length_read = 1;
    if (input_length != (uint32_t)GXOS_MEMORY_STATUS_EX_SIZE) {
        if (report != 0) report->status = GXOS_MEMORY_STATUS_EX_STATUS_INVALID_LENGTH;
        return 0;
    }
    query_status = gxos_memory_snapshot_query_current(
        &view, context->classification, context->startup_snapshot,
        context->ledger, context->virtual_arena,
        context->accounting_generation);
    if (query_status != GXOS_SNAPSHOT_STATUS_OK) {
        if (report != 0) report->status = map_query_status(query_status);
        return 0;
    }
    if (view.memory_load_percent > 100U) {
        if (report != 0) report->status = GXOS_MEMORY_STATUS_EX_STATUS_INVALID_MEMORY_LOAD;
        return 0;
    }
    if (!build_output(&view, &local_output)) {
        if (report != 0) report->status = GXOS_MEMORY_STATUS_EX_STATUS_INVALID_ACCOUNTING_VIEW;
        return 0;
    }

    /* Re-prove the destination immediately before the one complete write. */
    if (context->accounting_generation_source != 0 &&
        *context->accounting_generation_source != view.generation) {
        if (report != 0) {
            report->status = GXOS_MEMORY_STATUS_EX_STATUS_ACCOUNTING_CHANGED;
        }
        return 0;
    }
    address = (uintptr_t)buffer;
    if (!range_is_writable(context, address, GXOS_MEMORY_STATUS_EX_SIZE,
                           &writable_range_bytes)) {
        if (report != 0) {
            report->status = GXOS_MEMORY_STATUS_EX_STATUS_FINAL_RANGE_INVALID;
        }
        return 0;
    }
    copy_bytes((uint8_t *)buffer, (const uint8_t *)&local_output,
               sizeof(local_output));
    if (report != 0) {
        report->status = GXOS_MEMORY_STATUS_EX_STATUS_OK;
        report->output_range_valid = 1;
        report->output_written = 1;
        report->return_value = 1;
        report->accounting_generation = view.generation;
        copy_bytes((uint8_t *)&report->view, (const uint8_t *)&view,
                   sizeof(view));
    }
    return 1;
}

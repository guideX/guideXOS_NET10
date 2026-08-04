#include "crt_malloc.h"

static int gxos_crt_malloc_ranges_overlap(
    uintptr_t left_base,
    uintptr_t left_end,
    uintptr_t right_base,
    uintptr_t right_end)
{
    return left_base < right_end && right_base < left_end;
}

static int gxos_crt_malloc_range_valid(uintptr_t base, uintptr_t end)
{
    return base != 0 && base < end;
}

static void gxos_crt_malloc_zero(void *memory, uint64_t size)
{
    volatile uint8_t *bytes = (volatile uint8_t *)memory;
    while (size-- != 0) *bytes++ = 0;
}

static void gxos_crt_malloc_copy(void *destination, const void *source, uint64_t size)
{
    uint8_t *out = (uint8_t *)destination;
    const uint8_t *in = (const uint8_t *)source;
    while (size-- != 0) *out++ = *in++;
}

static int gxos_crt_malloc_context_ranges_valid(
    const GXOS_CRT_MALLOC_CONTEXT *context)
{
    uint32_t index;

    if (context->protected_range_count > GXOS_CRT_MALLOC_MAX_PROTECTED_RANGES) {
        return 0;
    }
    for (index = 0; index != context->protected_range_count; index++) {
        const GXOS_CRT_MALLOC_PROTECTED_RANGE *range =
            &context->protected_ranges[index];
        if (!gxos_crt_malloc_range_valid(range->base, range->end)) return 0;
    }
    return 1;
}

int gxos_crt_malloc_registry_valid(const GXOS_CRT_MALLOC_CONTEXT *context)
{
    uint32_t index;
    uint32_t live_count = 0;
    uint64_t total_requested_bytes = 0;
    uint64_t largest_request = 0;

    if (context == 0 || context->next_allocation_sequence == 0) return 0;
    if (context->live_count > GXOS_CRT_MALLOC_REGISTRY_CAPACITY) return 0;
    for (index = 0; index != GXOS_CRT_MALLOC_REGISTRY_CAPACITY; index++) {
        const GXOS_CRT_MALLOC_RECORD *record = &context->records[index];
        uint32_t other;
        uintptr_t end;

        if (record->occupied > 1U) return 0;
        if (!record->occupied) continue;
        if (record->pointer == 0 || record->requested_size == 0 ||
            record->requested_size > GXOS_CRT_MALLOC_MAX_REQUEST ||
            record->requested_size > (uint64_t)UINTPTR_MAX ||
            (record->pointer & 7U) != 0 ||
            record->allocation_sequence == 0) {
            return 0;
        }
        end = record->pointer + (uintptr_t)record->requested_size;
        if (end < record->pointer) return 0;
        for (other = index + 1U;
             other != GXOS_CRT_MALLOC_REGISTRY_CAPACITY;
             other++) {
            const GXOS_CRT_MALLOC_RECORD *other_record =
                &context->records[other];
            uintptr_t other_end;
            if (!other_record->occupied) continue;
            other_end = other_record->pointer +
                        (uintptr_t)other_record->requested_size;
            if (other_record->pointer == record->pointer ||
                record->allocation_sequence == other_record->allocation_sequence ||
                gxos_crt_malloc_ranges_overlap(
                    record->pointer, end,
                    other_record->pointer, other_end)) {
                return 0;
            }
        }
        if (total_requested_bytes > UINT64_MAX - record->requested_size) return 0;
        total_requested_bytes += record->requested_size;
        if (record->requested_size > largest_request) {
            largest_request = record->requested_size;
        }
        live_count++;
    }
    return live_count == context->live_count &&
           total_requested_bytes == context->total_requested_bytes &&
           largest_request == context->largest_request;
}

void gxos_crt_malloc_context_reset(GXOS_CRT_MALLOC_CONTEXT *context)
{
    if (context == 0) return;
    gxos_crt_malloc_zero(context, sizeof(*context));
    context->next_allocation_sequence = 1;
}

int gxos_crt_malloc_add_protected_range(
    GXOS_CRT_MALLOC_CONTEXT *context,
    uintptr_t base,
    uintptr_t end,
    uint32_t kind)
{
    GXOS_CRT_MALLOC_PROTECTED_RANGE *range;

    if (context == 0 || !gxos_crt_malloc_range_valid(base, end) ||
        context->protected_range_count >= GXOS_CRT_MALLOC_MAX_PROTECTED_RANGES) {
        return -1;
    }
    range = &context->protected_ranges[context->protected_range_count++];
    range->base = base;
    range->end = end;
    range->kind = kind;
    return 0;
}

const GXOS_CRT_MALLOC_RECORD *gxos_crt_malloc_find_live_record(
    const GXOS_CRT_MALLOC_CONTEXT *context,
    uintptr_t pointer)
{
    uint32_t index;

    if (context == 0 || pointer == 0) return 0;
    for (index = 0; index != GXOS_CRT_MALLOC_REGISTRY_CAPACITY; index++) {
        const GXOS_CRT_MALLOC_RECORD *record = &context->records[index];
        if (record->occupied && record->pointer == pointer) return record;
    }
    return 0;
}

const GXOS_CRT_MALLOC_DIAGNOSTIC *gxos_crt_malloc_get_diagnostic(
    const GXOS_CRT_MALLOC_CONTEXT *context,
    uint32_t index)
{
    if (context == 0 || index >= context->diagnostic_count ||
        index >= GXOS_CRT_MALLOC_MAX_DIAGNOSTICS) {
        return 0;
    }
    return &context->diagnostics[index];
}

static uint32_t gxos_crt_malloc_find_free_slot(
    const GXOS_CRT_MALLOC_CONTEXT *context)
{
    uint32_t index;
    for (index = 0; index != GXOS_CRT_MALLOC_REGISTRY_CAPACITY; index++) {
        if (!context->records[index].occupied) return index;
    }
    return GXOS_CRT_MALLOC_NO_SLOT;
}

static void gxos_crt_malloc_record_diagnostic(
    GXOS_CRT_MALLOC_CONTEXT *context,
    const GXOS_CRT_MALLOC_DIAGNOSTIC *diagnostic)
{
    if (context->diagnostic_count < GXOS_CRT_MALLOC_MAX_DIAGNOSTICS) {
        gxos_crt_malloc_copy(
            &context->diagnostics[context->diagnostic_count],
            diagnostic,
            sizeof(*diagnostic));
        context->diagnostic_count++;
    } else {
        context->diagnostic_overflow_count++;
    }
    if (context->trace != 0) context->trace(diagnostic, context->trace_context);
}

static void gxos_crt_malloc_rollback(
    GXOS_CRT_MALLOC_CONTEXT *context,
    GXOS_CRT_MALLOC_DIAGNOSTIC *diagnostic,
    void *pointer)
{
    diagnostic->rollback_count = 1;
    context->pool_rollback_count++;
    if (context->free_pool != 0) {
        diagnostic->rollback_status = context->free_pool(
            pointer, context->allocator_context);
    }
}

static void gxos_crt_malloc_set_call_sites(
    const GXOS_CRT_MALLOC_CONTEXT *context,
    uintptr_t runtime_return_address,
    uintptr_t *runtime_call_site,
    uintptr_t *static_call_site)
{
    *runtime_call_site = runtime_return_address >= 5U
        ? runtime_return_address - 5U
        : 0;
    *static_call_site = 0;
    if (context->image_base != 0 && context->preferred_image_base != 0 &&
        *runtime_call_site >= context->image_base) {
        *static_call_site = context->preferred_image_base +
            (*runtime_call_site - context->image_base);
    }
}

void *GXOS_CRT_MALLOC_MS_ABI gxos_crt_malloc_call(
    GXOS_CRT_MALLOC_CONTEXT *context,
    uint64_t requested_size,
    uintptr_t runtime_call_site,
    uintptr_t static_call_site)
{
    GXOS_CRT_MALLOC_DIAGNOSTIC diagnostic;
    GXOS_CRT_MALLOC_FAILURE failure = GXOS_CRT_MALLOC_FAILURE_NONE;
    uint32_t slot = GXOS_CRT_MALLOC_NO_SLOT;
    uint32_t index;
    uint32_t registry_valid;
    uint32_t ranges_valid;
    void *allocation = 0;
    uint64_t pool_status = 0;
    uintptr_t allocation_base = 0;
    uintptr_t allocation_end = 0;
    uint64_t allocation_sequence = 0;
    int should_rollback = 0;
    void *result = 0;

    if (context == 0) return 0;
    gxos_crt_malloc_zero(&diagnostic, sizeof(diagnostic));
    context->invocation_count++;
    diagnostic.invocation_number = context->invocation_count;
    diagnostic.static_call_site = static_call_site;
    diagnostic.runtime_call_site = runtime_call_site;
    diagnostic.requested_size = requested_size;
    diagnostic.registry_slot = GXOS_CRT_MALLOC_NO_SLOT;
    diagnostic.live_count_before = context->live_count;

    registry_valid = gxos_crt_malloc_registry_valid(context);
    ranges_valid = gxos_crt_malloc_context_ranges_valid(context);
    if (!registry_valid) {
        diagnostic.live_count_before = GXOS_CRT_MALLOC_NO_LIVE_COUNT;
        failure = GXOS_CRT_MALLOC_FAILURE_MALFORMED_REGISTRY;
        goto complete;
    }
    if (!ranges_valid) {
        failure = GXOS_CRT_MALLOC_FAILURE_INVALID_PROTECTED_RANGE;
        goto complete;
    }
    if (requested_size == 0) {
        failure = GXOS_CRT_MALLOC_FAILURE_ZERO_SIZE;
        goto complete;
    }
    if (requested_size > (uint64_t)UINTPTR_MAX) {
        failure = GXOS_CRT_MALLOC_FAILURE_NOT_UINTN;
        goto complete;
    }
    if (requested_size > GXOS_CRT_MALLOC_MAX_REQUEST) {
        failure = GXOS_CRT_MALLOC_FAILURE_SIZE_LIMIT;
        goto complete;
    }
    if (context->boot_services == 0 || !context->boot_services_available) {
        failure = GXOS_CRT_MALLOC_FAILURE_BOOT_SERVICES_UNAVAILABLE;
        goto complete;
    }
    diagnostic.pool_service_available =
        context->allocate_pool != 0 && context->free_pool != 0;
    if (!diagnostic.pool_service_available) {
        failure = GXOS_CRT_MALLOC_FAILURE_POOL_SERVICE_UNAVAILABLE;
        goto complete;
    }
    slot = gxos_crt_malloc_find_free_slot(context);
    diagnostic.registry_slot = slot;
    if (slot == GXOS_CRT_MALLOC_NO_SLOT) {
        context->metadata_exhaustion_count++;
        failure = GXOS_CRT_MALLOC_FAILURE_METADATA_EXHAUSTED;
        goto complete;
    }
    if (context->next_allocation_sequence == UINT64_MAX) {
        failure = GXOS_CRT_MALLOC_FAILURE_SEQUENCE_EXHAUSTED;
        goto complete;
    }

    pool_status = context->allocate_pool(
        GXOS_CRT_MALLOC_EFI_LOADER_DATA,
        (uintptr_t)requested_size,
        &allocation,
        context->allocator_context);
    diagnostic.allocate_pool_status = pool_status;
    if ((pool_status >> 63) != 0) {
        failure = GXOS_CRT_MALLOC_FAILURE_POOL_ALLOCATION;
        goto complete;
    }
    allocation_base = (uintptr_t)allocation;
    diagnostic.returned_pointer = allocation_base;
    diagnostic.alignment_mod8 = (uint32_t)(allocation_base & 7U);
    diagnostic.alignment_mod16 = (uint32_t)(allocation_base & 15U);
    should_rollback = 1;
    if (allocation == 0) {
        failure = GXOS_CRT_MALLOC_FAILURE_NULL_SUCCESS;
        goto rollback;
    }
    if ((allocation_base & 7U) != 0) {
        failure = GXOS_CRT_MALLOC_FAILURE_UNALIGNED;
        goto rollback;
    }
    if (allocation_base > UINTPTR_MAX - (uintptr_t)requested_size) {
        failure = GXOS_CRT_MALLOC_FAILURE_RANGE_OVERFLOW;
        goto rollback;
    }
    allocation_end = allocation_base + (uintptr_t)requested_size;
    diagnostic.allocation_range_base = allocation_base;
    diagnostic.allocation_range_end = allocation_end;
    for (index = 0; index != context->protected_range_count; index++) {
        const GXOS_CRT_MALLOC_PROTECTED_RANGE *range =
            &context->protected_ranges[index];
        if (gxos_crt_malloc_ranges_overlap(
                allocation_base, allocation_end, range->base, range->end)) {
            failure = GXOS_CRT_MALLOC_FAILURE_PROTECTED_OVERLAP;
            goto rollback;
        }
    }
    for (index = 0; index != GXOS_CRT_MALLOC_REGISTRY_CAPACITY; index++) {
        const GXOS_CRT_MALLOC_RECORD *record = &context->records[index];
        uintptr_t record_end;
        if (!record->occupied) continue;
        if (record->pointer == allocation_base) {
            context->duplicate_pointer_rejection_count++;
            failure = GXOS_CRT_MALLOC_FAILURE_DUPLICATE_POINTER;
            goto rollback;
        }
        record_end = record->pointer + (uintptr_t)record->requested_size;
        if (gxos_crt_malloc_ranges_overlap(
                allocation_base, allocation_end,
                record->pointer, record_end)) {
            failure = GXOS_CRT_MALLOC_FAILURE_EXISTING_OVERLAP;
            goto rollback;
        }
    }
    diagnostic.overlap_validation = 1;
    if (context->total_requested_bytes > UINT64_MAX - requested_size) {
        failure = GXOS_CRT_MALLOC_FAILURE_ACCOUNTING_OVERFLOW;
        goto rollback;
    }

    allocation_sequence = context->next_allocation_sequence;
    context->records[slot].pointer = allocation_base;
    context->records[slot].requested_size = requested_size;
    context->records[slot].allocation_sequence = allocation_sequence;
    context->records[slot].occupied = 1;
    context->next_allocation_sequence++;
    context->live_count++;
    context->total_requested_bytes += requested_size;
    if (requested_size > context->largest_request) {
        context->largest_request = requested_size;
    }
    if ((uint64_t)context->live_count > context->max_live_allocation_count) {
        context->max_live_allocation_count = context->live_count;
    }
    diagnostic.live_count_after = context->live_count;
    diagnostic.return_value = allocation_base;
    result = allocation;
    should_rollback = 0;
    goto complete;

rollback:
    if (should_rollback) gxos_crt_malloc_rollback(context, &diagnostic, allocation);

complete:
    if (result == 0) {
        context->allocation_failure_count++;
    }
    diagnostic.failure = failure;
    if (diagnostic.live_count_after == 0 && context->live_count != 0) {
        diagnostic.live_count_after = context->live_count;
    }
    gxos_crt_malloc_record_diagnostic(context, &diagnostic);
    return result;
}

void *GXOS_CRT_MALLOC_MS_ABI gxos_crt_malloc_entry(
    GXOS_CRT_MALLOC_CONTEXT *context,
    uint64_t requested_size,
    uintptr_t runtime_return_address)
{
    uintptr_t runtime_call_site;
    uintptr_t static_call_site;

    if (context == 0) return 0;
    gxos_crt_malloc_set_call_sites(
        context,
        runtime_return_address,
        &runtime_call_site,
        &static_call_site);
    return gxos_crt_malloc_call(
        context,
        requested_size,
        runtime_call_site,
        static_call_site);
}

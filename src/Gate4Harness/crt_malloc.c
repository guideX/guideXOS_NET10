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
    if (context->live_count > GXOS_CRT_MALLOC_REGISTRY_CAPACITY ||
        context->release_record_count > GXOS_CRT_MALLOC_REGISTRY_CAPACITY ||
        context->next_release_record_slot >=
            GXOS_CRT_MALLOC_REGISTRY_CAPACITY) return 0;
    for (index = 0; index != GXOS_CRT_MALLOC_REGISTRY_CAPACITY; index++) {
        const GXOS_CRT_MALLOC_RECORD *record = &context->records[index];
        uint32_t other;
        uintptr_t end;

        if (record->occupied > 1U) return 0;
        if (!record->occupied) continue;
        if (record->pointer == 0 || record->requested_size == 0 ||
            record->backing_size != record->requested_size ||
            record->requested_size > GXOS_CRT_MALLOC_MAX_REQUEST ||
            record->requested_size > (uint64_t)UINTPTR_MAX ||
            (record->pointer & 7U) != 0 ||
            record->allocation_sequence == 0 ||
            record->occupied != 1U ||
            record->state != GXOS_CRT_MALLOC_RECORD_LIVE ||
            record->owner != GXOS_CRT_MALLOC_OWNER_CRT ||
            record->allocation_class != GXOS_CRT_MALLOC_CLASS_PERSISTENT_POOL) {
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
    context->accounting_generation = 1;
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

const GXOS_CRT_MALLOC_RECORD *gxos_crt_malloc_find_live_containing_record(
    const GXOS_CRT_MALLOC_CONTEXT *context,
    uintptr_t pointer)
{
    uint32_t index;

    if (context == 0 || pointer == 0) return 0;
    for (index = 0; index != GXOS_CRT_MALLOC_REGISTRY_CAPACITY; index++) {
        const GXOS_CRT_MALLOC_RECORD *record = &context->records[index];
        uintptr_t end;
        if (!record->occupied || record->pointer == pointer ||
            record->requested_size == 0 ||
            record->pointer > UINTPTR_MAX -
                (uintptr_t)record->requested_size) {
            continue;
        }
        end = record->pointer + (uintptr_t)record->requested_size;
        if (pointer > record->pointer && pointer < end) return record;
    }
    return 0;
}

const GXOS_CRT_MALLOC_RELEASE_RECORD *gxos_crt_malloc_find_release_record(
    const GXOS_CRT_MALLOC_CONTEXT *context,
    uintptr_t pointer)
{
    uint32_t index;

    if (context == 0 || pointer == 0) return 0;
    for (index = 0; index != context->release_record_count; index++) {
        const GXOS_CRT_MALLOC_RELEASE_RECORD *record =
            &context->release_records[index];
        if (record->state == GXOS_CRT_MALLOC_RECORD_FREED &&
            record->pointer == pointer) {
            return record;
        }
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

const GXOS_CRT_FREE_DIAGNOSTIC *gxos_crt_malloc_get_free_diagnostic(
    const GXOS_CRT_MALLOC_CONTEXT *context,
    uint32_t index)
{
    if (context == 0 || index >= context->free_diagnostic_count ||
        index >= GXOS_CRT_FREE_DIAGNOSTIC_CAPACITY) {
        return 0;
    }
    return &context->free_diagnostics[index];
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
    if (context->accounting_generation == UINT64_MAX) {
        failure = GXOS_CRT_MALLOC_FAILURE_ACCOUNTING_OVERFLOW;
        goto rollback;
    }

    allocation_sequence = context->next_allocation_sequence;
    context->records[slot].pointer = allocation_base;
    context->records[slot].requested_size = requested_size;
    context->records[slot].backing_size = requested_size;
    context->records[slot].allocation_sequence = allocation_sequence;
    context->records[slot].occupied = 1;
    context->records[slot].state = GXOS_CRT_MALLOC_RECORD_LIVE;
    context->records[slot].owner = GXOS_CRT_MALLOC_OWNER_CRT;
    context->records[slot].allocation_class =
        GXOS_CRT_MALLOC_CLASS_PERSISTENT_POOL;
    context->next_allocation_sequence++;
    context->live_count++;
    context->total_requested_bytes += requested_size;
    if (requested_size > context->largest_request) {
        context->largest_request = requested_size;
    }
    if ((uint64_t)context->live_count > context->max_live_allocation_count) {
        context->max_live_allocation_count = context->live_count;
    }
    context->accounting_generation++;
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

static void gxos_crt_free_record_diagnostic(
    GXOS_CRT_MALLOC_CONTEXT *context,
    const GXOS_CRT_FREE_DIAGNOSTIC *diagnostic)
{
    if (context->free_diagnostic_count < GXOS_CRT_FREE_DIAGNOSTIC_CAPACITY) {
        gxos_crt_malloc_copy(
            &context->free_diagnostics[context->free_diagnostic_count],
            diagnostic,
            sizeof(*diagnostic));
        context->free_diagnostic_count++;
    } else {
        context->free_diagnostic_overflow_count++;
    }
}

static void gxos_crt_free_recompute_largest(
    const GXOS_CRT_MALLOC_CONTEXT *context,
    uint64_t *largest_out)
{
    uint32_t index;
    uint64_t largest = 0;

    for (index = 0; index != GXOS_CRT_MALLOC_REGISTRY_CAPACITY; index++) {
        const GXOS_CRT_MALLOC_RECORD *record = &context->records[index];
        if (record->occupied && record->requested_size > largest) {
            largest = record->requested_size;
        }
    }
    *largest_out = largest;
}

static void gxos_crt_free_remember_release(
    GXOS_CRT_MALLOC_CONTEXT *context,
    const GXOS_CRT_MALLOC_RECORD *record,
    uint32_t registry_slot)
{
    GXOS_CRT_MALLOC_RELEASE_RECORD *release_record;
    uint32_t slot;

    if (context->release_record_count < GXOS_CRT_MALLOC_REGISTRY_CAPACITY) {
        slot = context->release_record_count++;
    } else {
        slot = context->next_release_record_slot;
    }
    context->next_release_record_slot =
        (slot + 1U) % GXOS_CRT_MALLOC_REGISTRY_CAPACITY;
    release_record = &context->release_records[slot];
    gxos_crt_malloc_zero(release_record, sizeof(*release_record));
    release_record->pointer = record->pointer;
    release_record->requested_size = record->requested_size;
    release_record->backing_size = record->backing_size;
    release_record->allocation_sequence = record->allocation_sequence;
    release_record->release_sequence = context->successful_free_count;
    release_record->registry_slot = registry_slot;
    release_record->state = GXOS_CRT_MALLOC_RECORD_FREED;
}

void GXOS_CRT_MALLOC_MS_ABI gxos_crt_free_call(
    GXOS_CRT_MALLOC_CONTEXT *context,
    void *pointer,
    uintptr_t runtime_call_site,
    uintptr_t static_call_site)
{
    GXOS_CRT_FREE_DIAGNOSTIC diagnostic;
    const GXOS_CRT_MALLOC_RECORD *record;
    const GXOS_CRT_MALLOC_RELEASE_RECORD *release_record;
    uint32_t index;
    uint32_t slot = GXOS_CRT_MALLOC_NO_SLOT;
    uint64_t largest_after;

    if (context == 0) return;
    gxos_crt_malloc_zero(&diagnostic, sizeof(diagnostic));
    context->free_invocation_count++;
    diagnostic.invocation_number = context->free_invocation_count;
    diagnostic.static_call_site = static_call_site;
    diagnostic.runtime_call_site = runtime_call_site;
    diagnostic.pointer = (uintptr_t)pointer;
    diagnostic.registry_slot = GXOS_CRT_MALLOC_NO_SLOT;
    diagnostic.live_count_before = context->live_count;
    diagnostic.live_count_after = context->live_count;
    diagnostic.total_requested_bytes_before = context->total_requested_bytes;
    diagnostic.total_requested_bytes_after = context->total_requested_bytes;
    diagnostic.largest_request_before = context->largest_request;
    diagnostic.largest_request_after = context->largest_request;
    diagnostic.accounting_generation_before = context->accounting_generation;
    diagnostic.accounting_generation_after = context->accounting_generation;

    if (pointer == 0) {
        context->null_free_count++;
        diagnostic.failure = GXOS_CRT_FREE_FAILURE_NONE;
        gxos_crt_free_record_diagnostic(context, &diagnostic);
        return;
    }
    if (!gxos_crt_malloc_registry_valid(context)) {
        context->invalid_free_count++;
        diagnostic.failure = GXOS_CRT_FREE_FAILURE_MALFORMED_REGISTRY;
        gxos_crt_free_record_diagnostic(context, &diagnostic);
        return;
    }

    record = 0;
    for (index = 0; index != GXOS_CRT_MALLOC_REGISTRY_CAPACITY; index++) {
        if (context->records[index].occupied &&
            context->records[index].pointer == (uintptr_t)pointer) {
            record = &context->records[index];
            slot = index;
            break;
        }
    }
    if (record == 0) {
        if (gxos_crt_malloc_find_live_containing_record(
                context, (uintptr_t)pointer) != 0) {
            diagnostic.failure = GXOS_CRT_FREE_FAILURE_INTERIOR_POINTER;
        } else {
            release_record = gxos_crt_malloc_find_release_record(
                context, (uintptr_t)pointer);
            if (release_record != 0) {
                diagnostic.registry_slot = release_record->registry_slot;
                diagnostic.record_state_before = release_record->state;
                diagnostic.record_state_after = release_record->state;
                diagnostic.allocation_sequence =
                    release_record->allocation_sequence;
                diagnostic.requested_size = release_record->requested_size;
                diagnostic.backing_size = release_record->backing_size;
                diagnostic.failure = GXOS_CRT_FREE_FAILURE_DOUBLE_FREE;
                context->double_free_count++;
            } else {
                diagnostic.failure = GXOS_CRT_FREE_FAILURE_UNKNOWN_POINTER;
            }
        }
        context->invalid_free_count++;
        gxos_crt_free_record_diagnostic(context, &diagnostic);
        return;
    }

    diagnostic.registry_slot = slot;
    diagnostic.record_state_before = record->state;
    diagnostic.record_state_after = GXOS_CRT_MALLOC_RECORD_FREED;
    diagnostic.allocation_sequence = record->allocation_sequence;
    diagnostic.requested_size = record->requested_size;
    diagnostic.backing_size = record->backing_size;
    diagnostic.owner = record->owner;
    diagnostic.allocation_class = record->allocation_class;
    if (context->free_pool == 0) {
        diagnostic.failure =
            GXOS_CRT_FREE_FAILURE_BACKING_SERVICE_UNAVAILABLE;
        context->invalid_free_count++;
        gxos_crt_free_record_diagnostic(context, &diagnostic);
        return;
    }
    if (context->live_count == 0 ||
        context->total_requested_bytes < record->requested_size ||
        context->accounting_generation == UINT64_MAX) {
        diagnostic.failure = GXOS_CRT_FREE_FAILURE_ACCOUNTING;
        context->invalid_free_count++;
        gxos_crt_free_record_diagnostic(context, &diagnostic);
        return;
    }
    diagnostic.backing_release_attempted = 1;
    diagnostic.backing_release_status = context->free_pool(
        pointer, context->allocator_context);
    if (diagnostic.backing_release_status != 0) {
        diagnostic.failure = GXOS_CRT_FREE_FAILURE_BACKING_RELEASE;
        context->invalid_free_count++;
        gxos_crt_free_record_diagnostic(context, &diagnostic);
        return;
    }

    diagnostic.backing_released = 1;
    gxos_crt_free_remember_release(context, record, slot);
    context->records[slot].occupied = 0;
    context->records[slot].state = 0;
    context->records[slot].pointer = 0;
    context->records[slot].requested_size = 0;
    context->records[slot].backing_size = 0;
    context->records[slot].allocation_sequence = 0;
    context->records[slot].owner = 0;
    context->records[slot].allocation_class = 0;
    context->live_count--;
    context->total_requested_bytes -= diagnostic.requested_size;
    gxos_crt_free_recompute_largest(context, &largest_after);
    context->largest_request = largest_after;
    context->accounting_generation++;
    context->successful_free_count++;
    diagnostic.live_count_after = context->live_count;
    diagnostic.total_requested_bytes_after = context->total_requested_bytes;
    diagnostic.largest_request_after = context->largest_request;
    diagnostic.accounting_generation_after = context->accounting_generation;
    diagnostic.failure = GXOS_CRT_FREE_FAILURE_NONE;
    gxos_crt_free_record_diagnostic(context, &diagnostic);
}

void GXOS_CRT_MALLOC_MS_ABI gxos_crt_free_entry(
    GXOS_CRT_MALLOC_CONTEXT *context,
    void *pointer,
    uintptr_t runtime_return_address)
{
    uintptr_t runtime_call_site;
    uintptr_t static_call_site;

    if (context == 0) return;
    gxos_crt_malloc_set_call_sites(
        context,
        runtime_return_address,
        &runtime_call_site,
        &static_call_site);
    gxos_crt_free_call(
        context,
        pointer,
        runtime_call_site,
        static_call_site);
}

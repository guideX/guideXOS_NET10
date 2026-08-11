#include "virtual_memory.h"

#include <stddef.h>

static void zero_bytes(uint8_t *destination, uint64_t bytes)
{
    uint64_t index;
    for (index = 0; index != bytes; ++index) destination[index] = 0;
}

static int add_u64(uint64_t left, uint64_t right, uint64_t *result)
{
    if (result == 0 || left > UINT64_MAX - right) return 0;
    *result = left + right;
    return 1;
}

static int range_end(uint64_t base, uint64_t bytes, uint64_t *end_out)
{
    return add_u64(base, bytes, end_out);
}

static int range_contains(uint64_t outer_base,
                          uint64_t outer_bytes,
                          uint64_t inner_base,
                          uint64_t inner_bytes)
{
    uint64_t outer_end;
    uint64_t inner_end;
    return range_end(outer_base, outer_bytes, &outer_end) &&
        range_end(inner_base, inner_bytes, &inner_end) &&
        inner_base >= outer_base && inner_end <= outer_end;
}

static int page_round(uint64_t value, uint64_t *rounded_out)
{
    uint64_t adjusted;
    if (rounded_out == 0 || value == 0 ||
        value > UINT64_MAX - (GXOS_VM_PAGE_SIZE - 1U)) return 0;
    adjusted = value + GXOS_VM_PAGE_SIZE - 1U;
    *rounded_out = adjusted & ~(GXOS_VM_PAGE_SIZE - 1U);
    return *rounded_out != 0;
}

static uint64_t page_down(uint64_t value)
{
    return value & ~(GXOS_VM_PAGE_SIZE - 1U);
}

static int canonical_address(uint64_t address)
{
    uint64_t high = address >> 48;
    uint64_t sign = (address >> 47) & 1U;
    return sign ? high == 0xFFFFU : high == 0;
}

static void set_error(GXOS_VM_PUBLIC_CONTEXT *context, uint32_t error)
{
    if (context != 0 && context->last_error != 0) {
        *context->last_error = error;
    }
}

static GXOS_VM_PUBLIC_STATUS invalid_argument(
    GXOS_VM_PUBLIC_CONTEXT *context)
{
    set_error(context, GXOS_VM_PUBLIC_ERROR_INVALID_PARAMETER);
    return GXOS_VM_PUBLIC_STATUS_INVALID_ARGUMENT;
}

static GXOS_VM_PUBLIC_STATUS unsupported(GXOS_VM_PUBLIC_CONTEXT *context)
{
    set_error(context, GXOS_VM_PUBLIC_ERROR_NOT_SUPPORTED);
    return GXOS_VM_PUBLIC_STATUS_UNSUPPORTED;
}

static GXOS_VM_PUBLIC_STATUS capacity_failure(
    GXOS_VM_PUBLIC_CONTEXT *context)
{
    set_error(context, GXOS_VM_PUBLIC_ERROR_NOT_ENOUGH_MEMORY);
    return GXOS_VM_PUBLIC_STATUS_CAPACITY;
}

static GXOS_VM_PUBLIC_STATUS allocation_failure(
    GXOS_VM_PUBLIC_CONTEXT *context)
{
    set_error(context, GXOS_VM_PUBLIC_ERROR_NOT_ENOUGH_MEMORY);
    return GXOS_VM_PUBLIC_STATUS_ALLOCATION;
}

static int valid_context(const GXOS_VM_PUBLIC_CONTEXT *context)
{
    return context != 0 && context->arena != 0 && context->paging != 0 &&
        context->generation != 0 && context->arena->valid &&
        context->data_allocator.allocate_page != 0 &&
        context->data_allocator.free_page != 0 &&
        context->data_allocator.physical_alias != 0;
}

static GXOS_VM_PUBLIC_STATUS map_reserve_status(
    GXOS_VM_PUBLIC_CONTEXT *context, GXOS_VM_STATUS status)
{
    switch (status) {
    case GXOS_VM_STATUS_OVERFLOW:
        return invalid_argument(context);
    case GXOS_VM_STATUS_CAPACITY:
    case GXOS_VM_STATUS_OUTSIDE_ARENA:
        return capacity_failure(context);
    case GXOS_VM_STATUS_OK:
        return GXOS_VM_PUBLIC_STATUS_OK;
    default:
        return invalid_argument(context);
    }
}

static GXOS_VM_PUBLIC_STATUS map_commit_status(
    GXOS_VM_PUBLIC_CONTEXT *context,
    GXOS_VM_COMMIT_OPERATION_STATUS status)
{
    switch (status) {
    case GXOS_VM_COMMIT_OPERATION_OK:
        return GXOS_VM_PUBLIC_STATUS_OK;
    case GXOS_VM_COMMIT_OPERATION_OVERFLOW:
        return invalid_argument(context);
    case GXOS_VM_COMMIT_OPERATION_OUTSIDE_RESERVATION:
        return invalid_argument(context);
    case GXOS_VM_COMMIT_OPERATION_CAPACITY:
    case GXOS_VM_COMMIT_OPERATION_ALLOCATION:
        return allocation_failure(context);
    case GXOS_VM_COMMIT_OPERATION_MAPPING:
        set_error(context, GXOS_VM_PUBLIC_ERROR_NOT_ENOUGH_MEMORY);
        return GXOS_VM_PUBLIC_STATUS_MAPPING;
    case GXOS_VM_COMMIT_OPERATION_INCONSISTENT:
    case GXOS_VM_COMMIT_OPERATION_BOOKKEEPING:
    case GXOS_VM_COMMIT_OPERATION_INVALID_ARGUMENT:
    default:
        set_error(context, GXOS_VM_PUBLIC_ERROR_INVALID_PARAMETER);
        return GXOS_VM_PUBLIC_STATUS_INCONSISTENT;
    }
}

static uint32_t count_existing_pages(const GXOS_VM_ARENA *arena,
                                     uint64_t start,
                                     uint64_t end)
{
    uint64_t page;
    uint32_t count = 0;
    for (page = start; page < end; page += GXOS_VM_PAGE_SIZE) {
        {
            uint32_t slot;
            if (gxos_vm_arena_find_commitment(arena, page, &slot) ==
                    GXOS_VM_STATUS_OK) {
                const GXOS_VM_COMMITMENT *commitment =
                    &arena->commitments[slot];
                if (commitment->base != page || commitment->bytes !=
                        GXOS_VM_PAGE_SIZE || count == UINT32_MAX) {
                    return UINT32_MAX;
                }
                ++count;
            }
        }
    }
    return count;
}

static GXOS_VM_PUBLIC_STATUS commit_range(
    GXOS_VM_PUBLIC_CONTEXT *context,
    uint32_t reservation_slot,
    uint64_t requested_start,
    uint64_t requested_bytes,
    GXOS_VM_PUBLIC_RESULT *result)
{
    uint64_t requested_end;
    uint64_t end;
    uint64_t start_page;
    uint64_t rounded_bytes;
    uint32_t existing_count;
    uint32_t new_count = 0;
    GXOS_VM_COMMIT_OPERATION operation;
    GXOS_VM_COMMIT_OPERATION_STATUS commit_status;
    GXOS_VM_PUBLIC_STATUS public_status;

    if (!range_end(requested_start, requested_bytes, &requested_end) ||
        !page_round(requested_end, &end) || requested_start == 0) {
        return invalid_argument(context);
    }
    start_page = page_down(requested_start);
    if (end <= start_page || !range_end(start_page, end - start_page, &end) ||
        !gxos_vm_arena_contains(context->arena, start_page,
                                end - start_page) ||
        reservation_slot >= GXOS_VM_MAX_RESERVATIONS ||
        !context->arena->reservations[reservation_slot].live ||
        !range_contains(
            context->arena->reservations[reservation_slot].base,
            context->arena->reservations[reservation_slot].bytes,
            start_page, end - start_page)) {
        return invalid_argument(context);
    }
    rounded_bytes = end - start_page;
    existing_count = count_existing_pages(context->arena, start_page, end);
    if (existing_count == UINT32_MAX) {
        return invalid_argument(context);
    }
    zero_bytes((uint8_t *)&operation, sizeof(operation));
    operation.arena = context->arena;
    operation.paging = context->paging;
    operation.data_allocator = context->data_allocator;
    operation.generation = context->generation;
    commit_status = gxos_vm_commit_range(&operation, reservation_slot,
                                         requested_start, requested_bytes, 1,
                                         0, &new_count);
    public_status = map_commit_status(context, commit_status);
    if (public_status != GXOS_VM_PUBLIC_STATUS_OK) return public_status;
    result->rounded_bytes = rounded_bytes;
    result->effective_base = start_page;
    result->reservation_base =
        context->arena->reservations[reservation_slot].base;
    result->reservation_slot = reservation_slot;
    result->new_page_count = new_count;
    result->existing_page_count = existing_count;
    result->committed = 1;
    return GXOS_VM_PUBLIC_STATUS_OK;
}

GXOS_VM_PUBLIC_STATUS gxos_vm_public_virtual_alloc(
    GXOS_VM_PUBLIC_CONTEXT *context,
    void *address,
    uint64_t size,
    uint32_t allocation_type,
    uint32_t protection,
    GXOS_VM_PUBLIC_RESULT *result_out,
    void **address_out)
{
    uint64_t rounded_size;
    uint64_t address_value = (uint64_t)(uintptr_t)address;
    uint32_t reservation_slot = UINT32_MAX;
    uint64_t reservation_base = 0;
    GXOS_VM_STATUS reserve_status;
    GXOS_VM_PUBLIC_STATUS public_status;
    GXOS_VM_PUBLIC_RESULT local_result;

    if (result_out == 0 || address_out == 0) {
        return invalid_argument(context);
    }
    zero_bytes((uint8_t *)result_out, sizeof(*result_out));
    *address_out = 0;
    if (!valid_context(context) || size == 0) {
        return invalid_argument(context);
    }
    result_out->requested_bytes = size;
    if (protection != GXOS_VM_PUBLIC_PAGE_READWRITE) {
        return invalid_argument(context);
    }
    if ((allocation_type & GXOS_VM_PUBLIC_MEM_WRITE_WATCH) != 0U ||
        (allocation_type & GXOS_VM_PUBLIC_MEM_RESET) != 0U ||
        (allocation_type & GXOS_VM_PUBLIC_MEM_LARGE_PAGES) != 0U ||
        (allocation_type & GXOS_VM_PUBLIC_MEM_PHYSICAL) != 0U ||
        (allocation_type & GXOS_VM_PUBLIC_MEM_TOP_DOWN) != 0U) {
        return unsupported(context);
    }
    if ((allocation_type & ~(GXOS_VM_PUBLIC_MEM_RESERVE |
                             GXOS_VM_PUBLIC_MEM_COMMIT)) != 0U) {
        return invalid_argument(context);
    }
    if (!page_round(size, &rounded_size)) return invalid_argument(context);
    if (allocation_type == GXOS_VM_PUBLIC_MEM_RESERVE && address == 0) {
        reserve_status = gxos_vm_arena_reserve_any(
            context->arena, size, GXOS_MEMORY_ALLOCATION_VM_DATA,
            GXOS_MEMORY_OWNER_NATIVEAOT, context->generation,
            &reservation_base, &reservation_slot);
        public_status = map_reserve_status(context, reserve_status);
        if (public_status != GXOS_VM_PUBLIC_STATUS_OK) return public_status;
        result_out->rounded_bytes =
            context->arena->reservations[reservation_slot].bytes;
        result_out->effective_base = reservation_base;
        result_out->reservation_base = reservation_base;
        result_out->reservation_slot = reservation_slot;
        result_out->reserved = 1;
        *address_out = (void *)(uintptr_t)reservation_base;
        return GXOS_VM_PUBLIC_STATUS_OK;
    }
    if (allocation_type == (GXOS_VM_PUBLIC_MEM_RESERVE |
                            GXOS_VM_PUBLIC_MEM_COMMIT) && address == 0) {
        reserve_status = gxos_vm_arena_reserve_any(
            context->arena, size, GXOS_MEMORY_ALLOCATION_VM_DATA,
            GXOS_MEMORY_OWNER_NATIVEAOT, context->generation,
            &reservation_base, &reservation_slot);
        public_status = map_reserve_status(context, reserve_status);
        if (public_status != GXOS_VM_PUBLIC_STATUS_OK) return public_status;
        zero_bytes((uint8_t *)&local_result, sizeof(local_result));
        local_result.requested_bytes = size;
        local_result.reserved = 1;
        public_status = commit_range(context, reservation_slot,
                                     reservation_base, size, &local_result);
        if (public_status != GXOS_VM_PUBLIC_STATUS_OK) {
            uint32_t index;
            for (index = 0; index != GXOS_VM_MAX_COMMITMENTS; ++index) {
                if (context->arena->commitments[index].live &&
                    context->arena->commitments[index].reservation_slot ==
                        reservation_slot) {
                    uint64_t page = context->arena->commitments[index].base;
                    uint64_t physical =
                        context->arena->commitments[index].physical_base;
                    uint64_t unmapped = 0;
                    void *alias = context->data_allocator.physical_alias(
                        context->data_allocator.context, physical);
                    (void)gxos_vm_paging_unmap_page(context->paging, page,
                                                    &unmapped);
                    (void)gxos_vm_arena_decommit_page(context->arena, page,
                                                      0);
                    context->data_allocator.free_page(
                        context->data_allocator.context, physical, alias);
                }
            }
            if (gxos_vm_arena_release(context->arena, reservation_slot) !=
                    GXOS_VM_STATUS_OK) {
                set_error(context, GXOS_VM_PUBLIC_ERROR_INVALID_PARAMETER);
                return GXOS_VM_PUBLIC_STATUS_INCONSISTENT;
            }
            return public_status;
        }
        *result_out = local_result;
        *address_out = (void *)(uintptr_t)reservation_base;
        return GXOS_VM_PUBLIC_STATUS_OK;
    }
    if (allocation_type != GXOS_VM_PUBLIC_MEM_COMMIT || address == 0 ||
        !canonical_address(address_value) ||
        !gxos_vm_arena_find_reservation(context->arena, address_value,
                                        &reservation_slot)) {
        return invalid_argument(context);
    }
    public_status = commit_range(context, reservation_slot, address_value,
                                 size, result_out);
    if (public_status != GXOS_VM_PUBLIC_STATUS_OK) return public_status;
    *address_out = (void *)(uintptr_t)result_out->effective_base;
    return GXOS_VM_PUBLIC_STATUS_OK;
}

GXOS_VM_PUBLIC_STATUS gxos_vm_public_virtual_free(
    GXOS_VM_PUBLIC_CONTEXT *context,
    void *address,
    uint64_t size,
    uint32_t free_type,
    GXOS_VM_PUBLIC_RESULT *result_out,
    int *success_out)
{
    uint64_t address_value = (uint64_t)(uintptr_t)address;
    uint32_t reservation_slot;
    uint32_t index;
    uint32_t committed_count = 0;
    uint64_t committed_bytes = 0;

    if (result_out == 0 || success_out == 0) {
        return invalid_argument(context);
    }
    zero_bytes((uint8_t *)result_out, sizeof(*result_out));
    *success_out = 0;
    if (!valid_context(context) || address == 0 || size != 0 ||
        !canonical_address(address_value)) {
        return invalid_argument(context);
    }
    if (free_type == GXOS_VM_PUBLIC_MEM_DECOMMIT ||
        (free_type & GXOS_VM_PUBLIC_MEM_DECOMMIT) != 0U) {
        return unsupported(context);
    }
    if (free_type != GXOS_VM_PUBLIC_MEM_RELEASE ||
        !gxos_vm_arena_find_reservation(context->arena, address_value,
                                        &reservation_slot) ||
        context->arena->reservations[reservation_slot].base != address_value) {
        return invalid_argument(context);
    }
    for (index = 0; index != GXOS_VM_MAX_COMMITMENTS; ++index) {
        const GXOS_VM_COMMITMENT *commitment =
            &context->arena->commitments[index];
        GXOS_VM_MAPPING mapping;
        void *alias;
        if (!commitment->live || commitment->reservation_slot !=
                reservation_slot) continue;
        if (commitment->base == 0 || commitment->bytes != GXOS_VM_PAGE_SIZE ||
            commitment->physical_base == 0 ||
            gxos_vm_paging_query(context->paging, commitment->base, &mapping) !=
                GXOS_VM_PAGING_STATUS_OK || !mapping.present ||
            mapping.page_size != GXOS_VM_PAGE_SIZE ||
            mapping.physical_base != commitment->physical_base) {
            set_error(context, GXOS_VM_PUBLIC_ERROR_INVALID_PARAMETER);
            return GXOS_VM_PUBLIC_STATUS_INCONSISTENT;
        }
        alias = context->data_allocator.physical_alias(
            context->data_allocator.context, commitment->physical_base);
        if (alias == 0) {
            set_error(context, GXOS_VM_PUBLIC_ERROR_INVALID_PARAMETER);
            return GXOS_VM_PUBLIC_STATUS_INCONSISTENT;
        }
        ++committed_count;
        if (committed_bytes > UINT64_MAX - commitment->bytes) {
            return invalid_argument(context);
        }
        committed_bytes += commitment->bytes;
    }
    for (index = 0; index != GXOS_VM_MAX_COMMITMENTS; ++index) {
        GXOS_VM_COMMITMENT *commitment =
            &context->arena->commitments[index];
        uint64_t page;
        uint64_t physical;
        uint64_t unmapped = 0;
        void *alias;
        if (!commitment->live || commitment->reservation_slot !=
                reservation_slot) continue;
        page = commitment->base;
        physical = commitment->physical_base;
        alias = context->data_allocator.physical_alias(
            context->data_allocator.context, physical);
        if (alias == 0 || gxos_vm_paging_unmap_page(context->paging, page,
                                                    &unmapped) !=
                GXOS_VM_PAGING_STATUS_OK || unmapped != physical ||
            gxos_vm_arena_decommit_page(context->arena, page, 0) !=
                GXOS_VM_STATUS_OK) {
            set_error(context, GXOS_VM_PUBLIC_ERROR_INVALID_PARAMETER);
            return GXOS_VM_PUBLIC_STATUS_INCONSISTENT;
        }
        context->data_allocator.free_page(context->data_allocator.context,
                                          physical, alias);
    }
    if (gxos_vm_arena_release(context->arena, reservation_slot) !=
            GXOS_VM_STATUS_OK) {
        set_error(context, GXOS_VM_PUBLIC_ERROR_INVALID_PARAMETER);
        return GXOS_VM_PUBLIC_STATUS_INCONSISTENT;
    }
    result_out->effective_base = address_value;
    result_out->reservation_base = address_value;
    result_out->reservation_slot = reservation_slot;
    result_out->existing_page_count = committed_count;
    result_out->rounded_bytes = committed_bytes;
    result_out->reserved = 1;
    result_out->committed = committed_count != 0;
    *success_out = 1;
    return GXOS_VM_PUBLIC_STATUS_OK;
}

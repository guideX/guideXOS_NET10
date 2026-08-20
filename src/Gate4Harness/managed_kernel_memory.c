#include "managed_kernel_memory.h"

static void zero_bytes(void *memory, uint64_t bytes)
{
    uint8_t *cursor = (uint8_t *)memory;
    while (bytes-- != 0) *cursor++ = 0;
}

static int add_u64(uint64_t left, uint64_t right, uint64_t *result)
{
    if (result == 0 || left > UINT64_MAX - right) return 0;
    *result = left + right;
    return 1;
}

static int multiply_u64(uint64_t left, uint64_t right, uint64_t *result)
{
    if (result == 0 || (right != 0 && left > UINT64_MAX / right)) return 0;
    *result = left * right;
    return 1;
}

static int range_valid(uintptr_t address, uintptr_t bytes)
{
    return address != 0 && bytes != 0 && bytes <= UINTPTR_MAX - address;
}

static int valid_context(const GXOS_MANAGED_KERNEL_MEMORY_CONTEXT *context)
{
    return context != 0 && context->arena != 0 && context->paging != 0 &&
        context->region_ledger != 0 && context->physical_ledger != 0 &&
        context->generation != 0 && context->arena->valid &&
        context->data_allocator.allocate_page != 0 &&
        context->data_allocator.free_page != 0 &&
        context->data_allocator.physical_alias != 0 &&
        context->max_pages_per_allocation != 0 &&
        context->max_pages_per_allocation <=
            GX_MANAGED_KERNEL_MEMORY_MAX_PAGES_PER_ALLOCATION &&
        context->max_live_allocations != 0 &&
        context->max_live_allocations <= GXOS_MANAGED_KERNEL_MEMORY_SLOT_COUNT &&
        context->max_total_pages != 0 &&
        context->max_total_pages <= GX_MANAGED_KERNEL_MEMORY_MAX_TOTAL_PAGES;
}

static int find_free_slot(const GXOS_MANAGED_KERNEL_MEMORY_CONTEXT *context,
                          uint32_t *slot_out)
{
    uint32_t index;
    if (context == 0 || slot_out == 0) return 0;
    for (index = 0; index != GXOS_MANAGED_KERNEL_MEMORY_SLOT_COUNT; ++index) {
        if (!context->allocations[index].live) {
            *slot_out = index;
            return 1;
        }
    }
    return 0;
}

static int find_live_id(const GXOS_MANAGED_KERNEL_MEMORY_CONTEXT *context,
                        uint64_t allocation_id, uint32_t *slot_out)
{
    uint32_t index;
    if (context == 0 || allocation_id == 0 || slot_out == 0) return 0;
    for (index = 0; index != GXOS_MANAGED_KERNEL_MEMORY_SLOT_COUNT; ++index) {
        if (context->allocations[index].live &&
            context->allocations[index].allocation_id == allocation_id) {
            *slot_out = index;
            return 1;
        }
    }
    return 0;
}

static int allocation_id_live(
    const GXOS_MANAGED_KERNEL_MEMORY_CONTEXT *context,
    uint64_t allocation_id)
{
    uint32_t ignored;
    return find_live_id(context, allocation_id, &ignored);
}

static int next_allocation_id(
    GXOS_MANAGED_KERNEL_MEMORY_CONTEXT *context,
    uint64_t *allocation_id_out)
{
    uint32_t attempts;
    uint64_t candidate;
    if (context == 0 || allocation_id_out == 0) return 0;
    candidate = context->next_allocation_id;
    if (candidate == 0) candidate = 1;
    for (attempts = 0; attempts != GXOS_MANAGED_KERNEL_MEMORY_SLOT_COUNT + 1U;
         ++attempts) {
        uint64_t next = candidate == UINT64_MAX ? 1 : candidate + 1U;
        if (!allocation_id_live(context, candidate)) {
            context->next_allocation_id = next;
            *allocation_id_out = candidate;
            return 1;
        }
        candidate = next;
    }
    return 0;
}

static int region_matches(
    const GXOS_VM_REGION_LEDGER *ledger,
    uint64_t base,
    uint64_t bytes,
    uint64_t identity)
{
    uint32_t index;
    if (ledger == 0 || base == 0 || bytes == 0 || identity == 0) return 0;
    for (index = 0; index != GXOS_VM_REGION_LEDGER_CAPACITY; ++index) {
        const GXOS_VM_REGION *region = &ledger->entries[index];
        if (region->live && region->base == base && region->bytes == bytes &&
            region->allocation_base == base &&
            region->allocation_identity == identity &&
            region->state == GXOS_VM_REGION_STATE_COMMIT &&
            region->allocation_protect == GXOS_VM_REGION_PAGE_READWRITE &&
            region->protect == GXOS_VM_REGION_PAGE_READWRITE &&
            region->type == GXOS_VM_REGION_TYPE_PRIVATE) {
            return 1;
        }
    }
    return 0;
}

static int physical_page_is_managed(
    const GXOS_MANAGED_KERNEL_MEMORY_CONTEXT *context,
    uint64_t physical)
{
    uint32_t slot;
    if (context == 0 || physical == 0 ||
        !gxos_physical_ledger_find(context->physical_ledger, physical,
                                   GXOS_VM_PAGE_SIZE, &slot)) {
        return 0;
    }
    return context->physical_ledger->entries[slot].owner ==
        GXOS_MEMORY_OWNER_MANAGED_KERNEL &&
        context->physical_ledger->entries[slot].allocation_class ==
        GXOS_MEMORY_ALLOCATION_MANAGED_KERNEL &&
        context->physical_ledger->entries[slot].pages == 1 &&
        context->physical_ledger->entries[slot].bytes == GXOS_VM_PAGE_SIZE;
}

static int validate_pages(
    const GXOS_MANAGED_KERNEL_MEMORY_CONTEXT *context,
    const GXOS_MANAGED_KERNEL_MEMORY_ALLOCATION *allocation)
{
    uint64_t index;
    if (!valid_context(context) || allocation == 0 || !allocation->live ||
        allocation->reservation_slot >= GXOS_VM_MAX_RESERVATIONS ||
        allocation->page_count == 0 || allocation->page_size != GXOS_VM_PAGE_SIZE ||
        allocation->virtual_address == 0 || allocation->byte_length == 0 ||
        allocation->page_count > UINT64_MAX / GXOS_VM_PAGE_SIZE ||
        allocation->byte_length != allocation->page_count * GXOS_VM_PAGE_SIZE ||
        !range_valid((uintptr_t)allocation->virtual_address,
                     (uintptr_t)allocation->byte_length)) {
        return 0;
    }
    if (!context->arena->reservations[allocation->reservation_slot].live ||
        context->arena->reservations[allocation->reservation_slot].owner !=
            GXOS_MEMORY_OWNER_MANAGED_KERNEL ||
        context->arena->reservations[allocation->reservation_slot].base !=
            allocation->virtual_address ||
        context->arena->reservations[allocation->reservation_slot].bytes !=
            allocation->byte_length) {
        return 0;
    }
    for (index = 0; index != allocation->page_count; ++index) {
        uint64_t virtual_page = allocation->virtual_address +
            index * GXOS_VM_PAGE_SIZE;
        uint32_t commitment_slot;
        GXOS_VM_MAPPING mapping;
        if (gxos_vm_arena_find_commitment(context->arena, virtual_page,
                                          &commitment_slot) != GXOS_VM_STATUS_OK ||
            commitment_slot >= GXOS_VM_MAX_COMMITMENTS ||
            !context->arena->commitments[commitment_slot].live ||
            context->arena->commitments[commitment_slot].base != virtual_page ||
            context->arena->commitments[commitment_slot].bytes != GXOS_VM_PAGE_SIZE ||
            context->arena->commitments[commitment_slot].physical_base == 0 ||
            !physical_page_is_managed(
                context, context->arena->commitments[commitment_slot].physical_base) ||
            gxos_vm_paging_query(context->paging, virtual_page, &mapping) !=
                GXOS_VM_PAGING_STATUS_OK ||
            !mapping.present || mapping.page_size != GXOS_VM_PAGE_SIZE ||
            mapping.physical_base !=
                context->arena->commitments[commitment_slot].physical_base) {
            return 0;
        }
    }
    return 1;
}

static GX_MANAGED_STATUS release_pages_internal(
    GXOS_MANAGED_KERNEL_MEMORY_CONTEXT *context,
    const GXOS_MANAGED_KERNEL_MEMORY_ALLOCATION *allocation)
{
    uint64_t index;
    if (!validate_pages(context, allocation) ||
        !region_matches(context->region_ledger, allocation->virtual_address,
                        allocation->byte_length, allocation->region_identity)) {
        return GX_MANAGED_OWNERSHIP_MISMATCH;
    }
    if (gxos_vm_region_unregister(
            context->region_ledger, allocation->virtual_address,
            allocation->byte_length, allocation->region_identity) !=
        GXOS_VM_STATUS_OK) {
        return GX_MANAGED_INVALID_STATE;
    }
    for (index = 0; index != allocation->page_count; ++index) {
        uint64_t virtual_page = allocation->virtual_address +
            index * GXOS_VM_PAGE_SIZE;
        uint32_t commitment_slot;
        uint64_t physical;
        void *alias;
        uint64_t unmapped = 0;
        if (gxos_vm_arena_find_commitment(context->arena, virtual_page,
                                          &commitment_slot) != GXOS_VM_STATUS_OK ||
            commitment_slot >= GXOS_VM_MAX_COMMITMENTS) {
            return GX_MANAGED_INVALID_STATE;
        }
        physical = context->arena->commitments[commitment_slot].physical_base;
        alias = context->data_allocator.physical_alias(
            context->data_allocator.context, physical);
        if (alias == 0 || gxos_vm_paging_unmap_page(
                context->paging, virtual_page, &unmapped) !=
                GXOS_VM_PAGING_STATUS_OK || unmapped != physical ||
            gxos_vm_arena_decommit_page(context->arena, virtual_page, 0) !=
                GXOS_VM_STATUS_OK) {
            return GX_MANAGED_INVALID_STATE;
        }
        context->data_allocator.free_page(
            context->data_allocator.context, physical, alias);
    }
    if (gxos_vm_arena_release(context->arena, allocation->reservation_slot) !=
        GXOS_VM_STATUS_OK) {
        return GX_MANAGED_INVALID_STATE;
    }
    if (context->live_count == 0 || context->live_pages < allocation->page_count) {
        return GX_MANAGED_INVALID_STATE;
    }
    context->live_count--;
    context->live_pages -= allocation->page_count;
    return GX_MANAGED_OK;
}

static GX_MANAGED_STATUS rollback_reservation(
    GXOS_MANAGED_KERNEL_MEMORY_CONTEXT *context,
    uint32_t reservation_slot)
{
    uint32_t index;
    if (context == 0 || reservation_slot >= GXOS_VM_MAX_RESERVATIONS) {
        return GX_MANAGED_INVALID_STATE;
    }
    for (index = 0; index != GXOS_VM_MAX_COMMITMENTS; ++index) {
        GXOS_VM_COMMITMENT *commitment = &context->arena->commitments[index];
        uint64_t physical;
        uint64_t unmapped;
        void *alias;
        if (!commitment->live || commitment->reservation_slot != reservation_slot) {
            continue;
        }
        physical = commitment->physical_base;
        alias = context->data_allocator.physical_alias(
            context->data_allocator.context, physical);
        unmapped = 0;
        if (alias == 0 || gxos_vm_paging_unmap_page(
                context->paging, commitment->base, &unmapped) !=
                GXOS_VM_PAGING_STATUS_OK || unmapped != physical ||
            gxos_vm_arena_decommit_page(context->arena, commitment->base, 0) !=
                GXOS_VM_STATUS_OK) {
            return GX_MANAGED_INVALID_STATE;
        }
        context->data_allocator.free_page(
            context->data_allocator.context, physical, alias);
        index = 0;
    }
    if (gxos_vm_arena_release(context->arena, reservation_slot) !=
        GXOS_VM_STATUS_OK) {
        return GX_MANAGED_INVALID_STATE;
    }
    return GX_MANAGED_OK;
}

void gxos_managed_kernel_memory_init(
    GXOS_MANAGED_KERNEL_MEMORY_CONTEXT *context,
    GXOS_VM_ARENA *arena,
    GXOS_VM_PAGING *paging,
    GXOS_VM_REGION_LEDGER *region_ledger,
    GXOS_PHYSICAL_LEDGER *physical_ledger,
    GXOS_VM_PAGE_ALLOCATOR data_allocator,
    uint64_t generation)
{
    if (context == 0) return;
    zero_bytes(context, sizeof(*context));
    context->arena = arena;
    context->paging = paging;
    context->region_ledger = region_ledger;
    context->physical_ledger = physical_ledger;
    context->data_allocator = data_allocator;
    context->generation = generation;
    context->max_pages_per_allocation =
        GX_MANAGED_KERNEL_MEMORY_MAX_PAGES_PER_ALLOCATION;
    context->max_live_allocations = GX_MANAGED_KERNEL_MEMORY_MAX_LIVE_ALLOCATIONS;
    context->max_total_pages = GX_MANAGED_KERNEL_MEMORY_MAX_TOTAL_PAGES;
    context->next_allocation_id = 1;
}

void gxos_managed_kernel_memory_set_operational(
    GXOS_MANAGED_KERNEL_MEMORY_CONTEXT *context,
    uint32_t operational)
{
    if (context != 0) context->operational = operational != 0;
}

GX_MANAGED_STATUS gxos_managed_kernel_memory_allocate(
    GXOS_MANAGED_KERNEL_MEMORY_CONTEXT *context,
    uint64_t page_count,
    uint32_t flags,
    uintptr_t output_address,
    uintptr_t output_capacity)
{
    GX_MANAGED_KERNEL_MEMORY_ALLOCATION_V1 result;
    GXOS_VM_COMMIT_OPERATION operation;
    GXOS_VM_STATUS reserve_status;
    GXOS_VM_COMMIT_OPERATION_STATUS commit_status;
    GXOS_VM_STATUS region_status;
    GXOS_MANAGED_KERNEL_MEMORY_ALLOCATION *allocation;
    uint64_t byte_length;
    uint64_t virtual_address;
    uint64_t allocation_id;
    uint64_t region_identity;
    uint32_t slot;
    uint32_t reservation_slot = GXOS_VM_MAX_RESERVATIONS;
    uint32_t new_page_count = 0;

    if (!valid_context(context)) return GX_MANAGED_INVALID_STATE;
    if (!context->operational) return GX_MANAGED_INVALID_STATE;
    if (flags != GX_MANAGED_KERNEL_MEMORY_FLAG_NONE || page_count == 0 ||
        page_count > context->max_pages_per_allocation) {
        return GX_MANAGED_INVALID_ARGUMENT;
    }
    {
        GX_MANAGED_STATUS status =
            gxos_managed_kernel_validate_memory_allocation_output_buffer(
                output_address, output_capacity);
        if (status != GX_MANAGED_OK) return status;
    }
    if (context->live_count >= context->max_live_allocations ||
        context->live_pages > context->max_total_pages ||
        page_count > context->max_total_pages - context->live_pages) {
        return GX_MANAGED_RESOURCE_EXHAUSTED;
    }
    if (!multiply_u64(page_count, GXOS_VM_PAGE_SIZE, &byte_length) ||
        byte_length == 0 || byte_length > UINTPTR_MAX ||
        !find_free_slot(context, &slot) ||
        !next_allocation_id(context, &allocation_id)) {
        return GX_MANAGED_RESOURCE_EXHAUSTED;
    }
    if (context->region_ledger->live_count >= GXOS_VM_REGION_LEDGER_CAPACITY) {
        return GX_MANAGED_RESOURCE_EXHAUSTED;
    }
    reserve_status = gxos_vm_arena_reserve_any(
        context->arena, byte_length, GXOS_MEMORY_ALLOCATION_MANAGED_KERNEL,
        GXOS_MEMORY_OWNER_MANAGED_KERNEL, context->generation,
        &virtual_address, &reservation_slot);
    if (reserve_status != GXOS_VM_STATUS_OK) {
        return reserve_status == GXOS_VM_STATUS_OUTSIDE_ARENA ||
            reserve_status == GXOS_VM_STATUS_CAPACITY
            ? GX_MANAGED_RESOURCE_EXHAUSTED : GX_MANAGED_INVALID_ARGUMENT;
    }
    zero_bytes((uint8_t *)&operation, sizeof(operation));
    operation.arena = context->arena;
    operation.paging = context->paging;
    operation.data_allocator = context->data_allocator;
    operation.generation = context->generation;
    commit_status = gxos_vm_commit_range(
        &operation, reservation_slot, virtual_address, byte_length, 1, 0,
        &new_page_count);
    if (commit_status != GXOS_VM_COMMIT_OPERATION_OK) {
        GX_MANAGED_STATUS rollback_status = rollback_reservation(
            context, reservation_slot);
        return rollback_status == GX_MANAGED_OK
            ? (commit_status == GXOS_VM_COMMIT_OPERATION_CAPACITY ||
               commit_status == GXOS_VM_COMMIT_OPERATION_ALLOCATION
                   ? GX_MANAGED_RESOURCE_EXHAUSTED : GX_MANAGED_INVALID_ARGUMENT)
            : rollback_status;
    }
    if (new_page_count != page_count) {
        (void)rollback_reservation(context, reservation_slot);
        return GX_MANAGED_INVALID_STATE;
    }
    region_status = gxos_vm_region_register(
        context->region_ledger, virtual_address, byte_length, virtual_address,
        GXOS_VM_REGION_PAGE_READWRITE, GXOS_VM_REGION_STATE_COMMIT,
        GXOS_VM_REGION_PAGE_READWRITE, GXOS_VM_REGION_TYPE_PRIVATE,
        &region_identity);
    if (region_status != GXOS_VM_STATUS_OK) {
        GX_MANAGED_STATUS rollback_status = rollback_reservation(
            context, reservation_slot);
        return rollback_status == GX_MANAGED_OK
            ? GX_MANAGED_RESOURCE_EXHAUSTED : rollback_status;
    }
    result.Size = GX_MANAGED_KERNEL_MEMORY_ALLOCATION_V1_SIZE;
    result.AbiVersion = GX_MANAGED_KERNEL_MEMORY_SERVICES_ABI_V1;
    result.AllocationId = allocation_id;
    result.VirtualAddress = virtual_address;
    result.ByteLength = byte_length;
    result.PageCount = page_count;
    result.PageSize = GXOS_VM_PAGE_SIZE;
    result.Flags = flags;
    result.Reserved = 0;
    allocation = &context->allocations[slot];
    zero_bytes(allocation, sizeof(*allocation));
    allocation->live = 1;
    allocation->reservation_slot = reservation_slot;
    allocation->allocation_id = result.AllocationId;
    allocation->virtual_address = virtual_address;
    allocation->byte_length = byte_length;
    allocation->page_count = page_count;
    allocation->page_size = GXOS_VM_PAGE_SIZE;
    allocation->flags = flags;
    allocation->region_identity = region_identity;
    context->live_count++;
    context->live_pages += page_count;
    *(GX_MANAGED_KERNEL_MEMORY_ALLOCATION_V1 *)(uintptr_t)output_address = result;
    return GX_MANAGED_OK;
}

GX_MANAGED_STATUS gxos_managed_kernel_memory_release(
    GXOS_MANAGED_KERNEL_MEMORY_CONTEXT *context,
    uintptr_t request_address,
    uintptr_t request_capacity)
{
    GX_MANAGED_KERNEL_MEMORY_RELEASE_V1 request;
    GXOS_MANAGED_KERNEL_MEMORY_ALLOCATION *allocation;
    uint32_t slot;
    GX_MANAGED_STATUS status;
    if (!valid_context(context)) return GX_MANAGED_INVALID_STATE;
    if (!context->operational) return GX_MANAGED_INVALID_STATE;
    status = gxos_managed_kernel_validate_memory_release_input_buffer(
        request_address, request_capacity);
    if (status != GX_MANAGED_OK) return status;
    request = *(GX_MANAGED_KERNEL_MEMORY_RELEASE_V1 *)(uintptr_t)request_address;
    if (request.Size < GX_MANAGED_KERNEL_MEMORY_RELEASE_V1_SIZE ||
        request.AbiVersion != GX_MANAGED_KERNEL_MEMORY_SERVICES_ABI_V1 ||
        request.AllocationId == 0 || request.VirtualAddress == 0 ||
        request.ByteLength == 0 || request.PageCount == 0 ||
        request.PageSize != GXOS_VM_PAGE_SIZE || request.Flags != 0 ||
        request.Reserved != 0) {
        return GX_MANAGED_INVALID_ARGUMENT;
    }
    if (!find_live_id(context, request.AllocationId, &slot)) {
        return GX_MANAGED_NOT_FOUND;
    }
    allocation = &context->allocations[slot];
    if (allocation->virtual_address != request.VirtualAddress ||
        allocation->byte_length != request.ByteLength ||
        allocation->page_count != request.PageCount ||
        allocation->page_size != request.PageSize ||
        allocation->flags != request.Flags) {
        return GX_MANAGED_OWNERSHIP_MISMATCH;
    }
    status = release_pages_internal(context, allocation);
    if (status == GX_MANAGED_OK) zero_bytes(allocation, sizeof(*allocation));
    return status;
}

int gxos_managed_kernel_memory_validate(
    const GXOS_MANAGED_KERNEL_MEMORY_CONTEXT *context)
{
    uint32_t index;
    uint32_t live_count = 0;
    uint64_t live_pages = 0;
    if (!valid_context(context) || context->live_count > context->max_live_allocations ||
        context->live_pages > context->max_total_pages ||
        !gxos_vm_arena_validate(context->arena) ||
        !gxos_physical_ledger_validate(context->physical_ledger) ||
        !gxos_vm_region_ledger_validate(context->region_ledger)) {
        return 0;
    }
    for (index = 0; index != GXOS_MANAGED_KERNEL_MEMORY_SLOT_COUNT; ++index) {
        const GXOS_MANAGED_KERNEL_MEMORY_ALLOCATION *allocation =
            &context->allocations[index];
        if (!allocation->live) continue;
        if (!validate_pages(context, allocation) ||
            !region_matches(context->region_ledger, allocation->virtual_address,
                            allocation->byte_length, allocation->region_identity) ||
            !add_u64(live_pages, allocation->page_count, &live_pages)) {
            return 0;
        }
        ++live_count;
    }
    return live_count == context->live_count && live_pages == context->live_pages;
}

int gxos_managed_kernel_memory_has_no_live_allocations(
    const GXOS_MANAGED_KERNEL_MEMORY_CONTEXT *context)
{
    uint32_t index;
    if (context == 0 || context->arena == 0 ||
        context->physical_ledger == 0 || context->live_count != 0 ||
        context->live_pages != 0) {
        return 0;
    }
    for (index = 0; index != GXOS_PHYSICAL_LEDGER_CAPACITY; ++index) {
        const GXOS_PHYSICAL_ALLOCATION *entry =
            &context->physical_ledger->entries[index];
        if (entry->live && entry->allocation_class ==
                GXOS_MEMORY_ALLOCATION_MANAGED_KERNEL &&
            entry->owner == GXOS_MEMORY_OWNER_MANAGED_KERNEL) {
            return 0;
        }
    }
    for (index = 0; index != GXOS_VM_MAX_RESERVATIONS; ++index) {
        const GXOS_VM_RESERVATION *reservation =
            &context->arena->reservations[index];
        if (reservation->live && reservation->owner ==
            GXOS_MEMORY_OWNER_MANAGED_KERNEL) {
            return 0;
        }
    }
    for (index = 0; index != GXOS_VM_MAX_COMMITMENTS; ++index) {
        const GXOS_VM_COMMITMENT *commitment =
            &context->arena->commitments[index];
        if (commitment->live && commitment->reservation_slot <
                GXOS_VM_MAX_RESERVATIONS &&
            context->arena->reservations[commitment->reservation_slot].live &&
            context->arena->reservations[commitment->reservation_slot].owner ==
                GXOS_MEMORY_OWNER_MANAGED_KERNEL) {
            return 0;
        }
    }
    return 1;
}

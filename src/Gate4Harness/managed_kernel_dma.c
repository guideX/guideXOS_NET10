#include "managed_kernel_dma.h"

#include <stddef.h>

static GXOS_DMA_SERVICE *g_callback_service;

static void zero_bytes(void *memory, uint64_t bytes)
{
    uint8_t *cursor = (uint8_t *)memory;
    while (bytes-- != 0) *cursor++ = 0;
}

static void copy_bytes(void *destination, const void *source, uint64_t bytes)
{
    uint8_t *out = (uint8_t *)destination;
    const uint8_t *in = (const uint8_t *)source;
    while (bytes-- != 0) *out++ = *in++;
}

static int add_u64(uint64_t left, uint64_t right, uint64_t *result)
{
    if (result == 0 || left > UINT64_MAX - right) return 0;
    *result = left + right;
    return 1;
}

static int range_valid(uintptr_t address, uint64_t length)
{
    return address != 0 && length != 0 && length <= UINTPTR_MAX &&
        address <= UINTPTR_MAX - (uintptr_t)length;
}

int gxos_dma_validate_request(uint64_t requested_bytes, uint64_t alignment,
                              uint32_t max_pages)
{
    uint64_t rounded;
    if (requested_bytes == 0 || alignment == 0 ||
        (alignment & (alignment - 1U)) != 0U || alignment > GXOS_VM_PAGE_SIZE ||
        max_pages == 0 || max_pages > GX_MANAGED_KERNEL_DMA_MAX_PAGES_PER_ALLOCATION ||
        !add_u64(requested_bytes, GXOS_VM_PAGE_SIZE - 1U, &rounded) ||
        rounded / GXOS_VM_PAGE_SIZE == 0 ||
        rounded / GXOS_VM_PAGE_SIZE > max_pages) return 0;
    return 1;
}

int gxos_dma_validate_handle(uint64_t handle, uint32_t capacity,
                             uint32_t *slot_out, uint32_t *generation_out)
{
    uint32_t slot;
    uint32_t generation;
    if (slot_out != 0) *slot_out = UINT32_MAX;
    if (generation_out != 0) *generation_out = 0;
    if (handle == 0 || capacity == 0 || capacity > UINT32_MAX) return 0;
    slot = (uint32_t)(handle & UINT32_MAX);
    generation = (uint32_t)(handle >> 32);
    if (slot == 0 || slot > capacity || generation == 0) return 0;
    if (slot_out != 0) *slot_out = slot - 1U;
    if (generation_out != 0) *generation_out = generation;
    return 1;
}

static GXOS_DMA_ALLOCATION *find_allocation(GXOS_DMA_SERVICE *service,
                                             uint64_t handle)
{
    uint32_t slot;
    uint32_t generation;
    if (service == 0 || !gxos_dma_validate_handle(
            handle, GX_MANAGED_KERNEL_DMA_MAX_ALLOCATIONS, &slot, &generation)) {
        return 0;
    }
    return service->allocations[slot].live &&
           service->allocations[slot].generation == generation
        ? &service->allocations[slot] : 0;
}

static int service_valid(const GXOS_DMA_SERVICE *service)
{
    return service != 0 && service->initialized && service->mmio != 0 &&
        service->arena != 0 && service->paging != 0 &&
        service->region_ledger != 0 && service->physical_ledger != 0 &&
        service->generation != 0 && service->max_bus_address != 0 &&
        service->platform.allocate_contiguous != 0 &&
        service->platform.free_contiguous != 0 &&
        service->platform.physical_alias != 0;
}

typedef struct {
    GXOS_DMA_SERVICE *service;
    uint64_t physical_base;
    uint32_t page_count;
    uint32_t next_page;
} GXOS_DMA_PAGE_SOURCE;

static int dma_allocate_page(void *opaque, uint64_t *physical_out,
                             void **alias_out)
{
    GXOS_DMA_PAGE_SOURCE *source = (GXOS_DMA_PAGE_SOURCE *)opaque;
    GXOS_PHYSICAL_ALLOCATION allocation;
    uint64_t physical;
    uint32_t ledger_slot;
    if (physical_out == 0 || alias_out == 0 || source == 0 ||
        source->service == 0 || source->next_page >= source->page_count ||
        source->physical_base > UINT64_MAX -
            (uint64_t)source->next_page * GXOS_VM_PAGE_SIZE) {
        return 0;
    }
    physical = source->physical_base +
        (uint64_t)source->next_page * GXOS_VM_PAGE_SIZE;
    *alias_out = source->service->platform.physical_alias(
        source->service->platform.context, physical);
    if (*alias_out == 0) {
        return 0;
    }
    zero_bytes((uint8_t *)&allocation, sizeof(allocation));
    allocation.base = physical;
    allocation.bytes = GXOS_VM_PAGE_SIZE;
    allocation.pages = 1;
    allocation.allocation_class = GXOS_MEMORY_ALLOCATION_OTHER;
    allocation.owner = GXOS_MEMORY_OWNER_MANAGED_KERNEL;
    allocation.physical_impact_bytes = GXOS_VM_PAGE_SIZE;
    allocation.commit_impact_bytes = GXOS_VM_PAGE_SIZE;
    allocation.generation = source->service->generation;
    if (gxos_physical_ledger_insert(source->service->physical_ledger,
                                    &allocation, &ledger_slot) !=
            GXOS_LEDGER_STATUS_OK) {
        return 0;
    }
    source->next_page++;
    *physical_out = physical;
    return 1;
}

static void dma_free_page(void *opaque, uint64_t physical, void *alias)
{
    GXOS_DMA_PAGE_SOURCE *source = (GXOS_DMA_PAGE_SOURCE *)opaque;
    uint32_t ledger_slot;
    (void)alias;
    if (source == 0 || source->service == 0 ||
        !gxos_physical_ledger_find(source->service->physical_ledger,
                                   physical, GXOS_VM_PAGE_SIZE, &ledger_slot) ||
        gxos_physical_ledger_remove(source->service->physical_ledger,
                                    ledger_slot) != GXOS_LEDGER_STATUS_OK) {
        return;
    }
}

static void dma_cleanup_pages(GXOS_DMA_SERVICE *service,
                              uint64_t virtual_address, uint64_t physical_base,
                              uint32_t page_count, uint32_t reservation_slot,
                              uint64_t region_identity)
{
    uint32_t index;
    if (service == 0) return;
    if (region_identity != 0) {
        (void)gxos_vm_region_unregister(service->region_ledger,
                                        virtual_address,
                                        (uint64_t)page_count * GXOS_VM_PAGE_SIZE,
                                        region_identity);
    }
    for (index = 0; index != page_count; ++index) {
        uint64_t virtual_page = virtual_address +
            (uint64_t)index * GXOS_VM_PAGE_SIZE;
        uint64_t physical_page = 0;
        uint32_t ledger_slot;
        (void)gxos_vm_paging_unmap_page(service->paging, virtual_page,
                                        &physical_page);
        (void)gxos_vm_arena_decommit_page(service->arena, virtual_page,
                                          0);
        if (gxos_physical_ledger_find(service->physical_ledger,
                                      physical_page != 0 ? physical_page :
                                          physical_base + (uint64_t)index * GXOS_VM_PAGE_SIZE,
                                      GXOS_VM_PAGE_SIZE, &ledger_slot)) {
            (void)gxos_physical_ledger_remove(service->physical_ledger,
                                              ledger_slot);
        }
    }
    (void)gxos_vm_arena_release(service->arena, reservation_slot);
    (void)service->platform.free_contiguous(
        service->platform.context, physical_base, page_count);
}

GXOS_DMA_SERVICE_STATUS gxos_dma_service_init(
    GXOS_DMA_SERVICE *service, GXOS_MMIO_SERVICE *mmio,
    GXOS_VM_ARENA *arena, GXOS_VM_PAGING *paging,
    GXOS_VM_REGION_LEDGER *region_ledger, GXOS_PHYSICAL_LEDGER *physical_ledger,
    GXOS_DMA_PLATFORM platform, uint64_t generation, uint64_t max_bus_address)
{
    if (service == 0 || mmio == 0 || arena == 0 || paging == 0 ||
        region_ledger == 0 || physical_ledger == 0 || generation == 0 ||
        max_bus_address == 0 || platform.allocate_contiguous == 0 ||
        platform.free_contiguous == 0 || platform.physical_alias == 0 ||
        !arena->valid || !mmio->initialized) return GXOS_DMA_SERVICE_INVALID_ARGUMENT;
    zero_bytes(service, sizeof(*service));
    service->mmio = mmio;
    service->arena = arena;
    service->paging = paging;
    service->region_ledger = region_ledger;
    service->physical_ledger = physical_ledger;
    service->platform = platform;
    service->generation = generation;
    service->max_bus_address = max_bus_address;
    service->next_generation = 1;
    service->initialized = 1;
    return GXOS_DMA_SERVICE_OK;
}

GXOS_DMA_SERVICE_STATUS gxos_dma_service_teardown(GXOS_DMA_SERVICE *service)
{
    uint32_t index;
    if (!service_valid(service)) return GXOS_DMA_SERVICE_INVALID_STATE;
    for (index = 0; index != GX_MANAGED_KERNEL_DMA_MAX_ALLOCATIONS; ++index) {
        if (service->allocations[index].live) return GXOS_DMA_SERVICE_INVALID_STATE;
    }
    zero_bytes(service, sizeof(*service));
    return GXOS_DMA_SERVICE_OK;
}

GXOS_DMA_SERVICE_STATUS gxos_dma_allocate(
    GXOS_DMA_SERVICE *service, uint64_t claim_handle, uint32_t driver_id,
    uint64_t requested_bytes, uint64_t alignment,
    uintptr_t result_address, uintptr_t result_capacity)
{
    GXOS_DMA_ALLOCATION *allocation = 0;
    GX_MANAGED_KERNEL_DMA_ALLOCATION_RESULT_V1 result;
    GXOS_DMA_PAGE_SOURCE source;
    GXOS_VM_PAGE_ALLOCATOR allocator;
    GXOS_VM_COMMIT_OPERATION operation;
    GXOS_VM_COMMIT_OPERATION_STATUS commit_status;
    uint64_t resource_id;
    uint32_t owner_kind;
    uint32_t owner_id;
    uint64_t rounded_bytes;
    uint64_t page_count64;
    uint64_t virtual_address;
    uint64_t physical_base;
    uint64_t allocation_generation;
    uint64_t allocation_id;
    uint32_t reservation_slot;
    uint32_t index;
    uint32_t new_page_count = 0;
    uint64_t region_identity = 0;
    GXOS_VM_STATUS reserve_status;
    GXOS_VM_STATUS region_status;

    if (!service_valid(service) || claim_handle == 0 || driver_id == 0 ||
        result_address == 0 || result_capacity < sizeof(result) ||
        result_capacity > UINTPTR_MAX - result_address ||
        !gxos_mmio_validate_claim(service->mmio, claim_handle, driver_id,
                                  &resource_id, &owner_kind, &owner_id) ||
        owner_kind != GX_MANAGED_DEVICE_KIND_PCI || owner_id != 0x808610D3U ||
        !gxos_dma_validate_request(requested_bytes, alignment,
                                    GX_MANAGED_KERNEL_DMA_MAX_PAGES_PER_ALLOCATION) ||
        !add_u64(requested_bytes, GXOS_VM_PAGE_SIZE - 1U, &rounded_bytes)) {
        return GXOS_DMA_SERVICE_INVALID_ARGUMENT;
    }
    (void)resource_id;
    (void)owner_kind;
    (void)owner_id;
    rounded_bytes &= ~(GXOS_VM_PAGE_SIZE - 1U);
    page_count64 = rounded_bytes / GXOS_VM_PAGE_SIZE;
    if (page_count64 > UINT32_MAX || service->live_count >=
            GX_MANAGED_KERNEL_DMA_MAX_ALLOCATIONS ||
        service->live_pages > GX_MANAGED_KERNEL_DMA_MAX_TOTAL_PAGES ||
        page_count64 > GX_MANAGED_KERNEL_DMA_MAX_TOTAL_PAGES - service->live_pages ||
        service->next_generation == 0 || service->next_generation > UINT32_MAX) {
        return GXOS_DMA_SERVICE_RESOURCE_EXHAUSTED;
    }
    for (index = 0; index != GX_MANAGED_KERNEL_DMA_MAX_ALLOCATIONS; ++index) {
        if (!service->allocations[index].live) {
            allocation = &service->allocations[index];
            break;
        }
    }
    if (allocation == 0) {
        return GXOS_DMA_SERVICE_RESOURCE_EXHAUSTED;
    }
    physical_base = 0;
    if (!service->platform.allocate_contiguous(
            service->platform.context, (uint32_t)page_count64,
            &physical_base) || physical_base == 0 ||
        physical_base % GXOS_VM_PAGE_SIZE != 0 ||
        physical_base > service->max_bus_address ||
        rounded_bytes > service->max_bus_address - physical_base) {
        if (physical_base != 0) (void)service->platform.free_contiguous(
            service->platform.context, physical_base, (uint32_t)page_count64);
        return GXOS_DMA_SERVICE_RESOURCE_EXHAUSTED;
    }
    reserve_status = gxos_vm_arena_reserve_any(
            service->arena, rounded_bytes, GXOS_MEMORY_ALLOCATION_OTHER,
            GXOS_MEMORY_OWNER_MANAGED_KERNEL, service->generation,
            &virtual_address, &reservation_slot);
    if (reserve_status != GXOS_VM_STATUS_OK) {
        (void)service->platform.free_contiguous(service->platform.context,
                                                physical_base,
                                                (uint32_t)page_count64);
        return GXOS_DMA_SERVICE_RESOURCE_EXHAUSTED;
    }
    zero_bytes((uint8_t *)&source, sizeof(source));
    source.service = service;
    source.physical_base = physical_base;
    source.page_count = (uint32_t)page_count64;
    zero_bytes((uint8_t *)&allocator, sizeof(allocator));
    allocator.context = &source;
    allocator.allocate_page = dma_allocate_page;
    allocator.free_page = dma_free_page;
    allocator.physical_alias = service->platform.physical_alias;
    zero_bytes((uint8_t *)&operation, sizeof(operation));
    operation.arena = service->arena;
    operation.paging = service->paging;
    operation.data_allocator = allocator;
    operation.generation = service->generation;
    commit_status = gxos_vm_commit_range(
        &operation, reservation_slot, virtual_address, rounded_bytes, 1, 0,
        &new_page_count);
    if (commit_status != GXOS_VM_COMMIT_OPERATION_OK ||
        new_page_count != (uint32_t)page_count64) {
        (void)gxos_vm_arena_release(service->arena, reservation_slot);
        (void)service->platform.free_contiguous(
            service->platform.context, physical_base, (uint32_t)page_count64);
        return commit_status == GXOS_VM_COMMIT_OPERATION_CAPACITY ||
            commit_status == GXOS_VM_COMMIT_OPERATION_ALLOCATION
            ? GXOS_DMA_SERVICE_RESOURCE_EXHAUSTED
            : GXOS_DMA_SERVICE_INVALID_STATE;
    }
    region_status = gxos_vm_region_register(
        service->region_ledger, virtual_address, rounded_bytes, virtual_address,
        GXOS_VM_REGION_PAGE_READWRITE, GXOS_VM_REGION_STATE_COMMIT,
        GXOS_VM_REGION_PAGE_READWRITE, GXOS_VM_REGION_TYPE_PRIVATE,
        &region_identity);
    if (region_status != GXOS_VM_STATUS_OK) {
        dma_cleanup_pages(service, virtual_address, physical_base,
                          (uint32_t)page_count64, reservation_slot, 0);
        return GXOS_DMA_SERVICE_RESOURCE_EXHAUSTED;
    }
    allocation_generation = service->next_generation;
    allocation_id = (allocation_generation << 32) | (uint64_t)(index + 1U);
    service->next_generation++;
    zero_bytes(allocation, sizeof(*allocation));
    allocation->live = 1;
    allocation->reservation_slot = reservation_slot;
    allocation->allocation_id = allocation_id;
    allocation->generation = allocation_generation;
    allocation->claim_handle = claim_handle;
    allocation->owner_driver_id = driver_id;
    allocation->virtual_address = virtual_address;
    allocation->physical_address = physical_base;
    allocation->byte_length = rounded_bytes;
    allocation->requested_bytes = requested_bytes;
    allocation->page_count = page_count64;
    allocation->alignment = alignment;
    allocation->region_identity = region_identity;
    service->live_count++;
    service->live_pages += page_count64;
    zero_bytes((uint8_t *)&result, sizeof(result));
    result.Size = GX_MANAGED_KERNEL_DMA_ALLOCATION_RESULT_V1_SIZE;
    result.AbiVersion = GX_MANAGED_KERNEL_DMA_SERVICES_ABI_V1;
    result.Handle = allocation_id;
    result.BusAddress = physical_base;
    result.ByteLength = rounded_bytes;
    result.PageCount = page_count64;
    result.Alignment = alignment;
    result.Reserved = 0;
    *(GX_MANAGED_KERNEL_DMA_ALLOCATION_RESULT_V1 *)(uintptr_t)result_address = result;
    return GXOS_DMA_SERVICE_OK;
}

GXOS_DMA_SERVICE_STATUS gxos_dma_release(
    GXOS_DMA_SERVICE *service, uint64_t handle, uint32_t driver_id)
{
    GXOS_DMA_ALLOCATION *allocation = find_allocation(service, handle);
    uint64_t resource_id;
    uint32_t owner_kind;
    uint32_t owner_id;
    if (allocation == 0) return GXOS_DMA_SERVICE_NOT_FOUND;
    if (allocation->owner_driver_id != driver_id) return GXOS_DMA_SERVICE_OWNERSHIP_MISMATCH;
    if (allocation->reference_count != 0) return GXOS_DMA_SERVICE_INVALID_STATE;
    if (!gxos_mmio_validate_claim(service->mmio, allocation->claim_handle,
                                  driver_id, &resource_id, &owner_kind,
                                  &owner_id)) return GXOS_DMA_SERVICE_INVALID_STATE;
    if (gxos_vm_region_unregister(service->region_ledger,
                                  allocation->virtual_address,
                                  allocation->byte_length,
                                  allocation->region_identity) != GXOS_VM_STATUS_OK) {
        return GXOS_DMA_SERVICE_INVALID_STATE;
    }
    dma_cleanup_pages(service, allocation->virtual_address,
                      allocation->physical_address,
                      (uint32_t)allocation->page_count,
                      allocation->reservation_slot, 0);
    if (service->live_count == 0 || service->live_pages < allocation->page_count) {
        return GXOS_DMA_SERVICE_INVALID_STATE;
    }
    service->live_count--;
    service->live_pages -= allocation->page_count;
    zero_bytes(allocation, sizeof(*allocation));
    return GXOS_DMA_SERVICE_OK;
}

static GXOS_DMA_SERVICE_STATUS validate_io(
    GXOS_DMA_SERVICE *service, uint64_t handle, uint32_t driver_id,
    uint64_t offset, uintptr_t pointer, uint64_t length,
    GXOS_DMA_ALLOCATION **allocation_out)
{
    GXOS_DMA_ALLOCATION *allocation = find_allocation(service, handle);
    if (allocation_out != 0) *allocation_out = 0;
    if (allocation == 0) return GXOS_DMA_SERVICE_NOT_FOUND;
    if (allocation->owner_driver_id != driver_id) return GXOS_DMA_SERVICE_OWNERSHIP_MISMATCH;
    if (pointer == 0 || length == 0 || !range_valid(pointer, length) ||
        offset > allocation->byte_length || length > allocation->byte_length - offset) {
        return GXOS_DMA_SERVICE_INVALID_ARGUMENT;
    }
    if (allocation_out != 0) *allocation_out = allocation;
    return GXOS_DMA_SERVICE_OK;
}

GXOS_DMA_SERVICE_STATUS gxos_dma_read(
    GXOS_DMA_SERVICE *service, uint64_t handle, uint32_t driver_id,
    uint64_t offset, uintptr_t destination, uint64_t length)
{
    GXOS_DMA_ALLOCATION *allocation;
    GXOS_DMA_SERVICE_STATUS status = validate_io(
        service, handle, driver_id, offset, destination, length, &allocation);
    if (status != GXOS_DMA_SERVICE_OK) return status;
    copy_bytes((void *)(uintptr_t)destination,
               (const void *)(uintptr_t)(allocation->virtual_address + offset),
               length);
    return GXOS_DMA_SERVICE_OK;
}

GXOS_DMA_SERVICE_STATUS gxos_dma_write(
    GXOS_DMA_SERVICE *service, uint64_t handle, uint32_t driver_id,
    uint64_t offset, uintptr_t source_address, uint64_t length)
{
    GXOS_DMA_ALLOCATION *allocation;
    GXOS_DMA_SERVICE_STATUS status = validate_io(
        service, handle, driver_id, offset, source_address, length, &allocation);
    if (status != GXOS_DMA_SERVICE_OK) return status;
    copy_bytes((void *)(uintptr_t)(allocation->virtual_address + offset),
               (const void *)(uintptr_t)source_address, length);
    return GXOS_DMA_SERVICE_OK;
}

GXOS_DMA_SERVICE_STATUS gxos_dma_retain(
    GXOS_DMA_SERVICE *service, uint64_t handle, uint32_t driver_id)
{
    GXOS_DMA_ALLOCATION *allocation = find_allocation(service, handle);
    if (allocation == 0) return GXOS_DMA_SERVICE_NOT_FOUND;
    if (allocation->owner_driver_id != driver_id) return GXOS_DMA_SERVICE_OWNERSHIP_MISMATCH;
    if (allocation->reference_count == UINT32_MAX) return GXOS_DMA_SERVICE_RESOURCE_EXHAUSTED;
    allocation->reference_count++;
    return GXOS_DMA_SERVICE_OK;
}

GXOS_DMA_SERVICE_STATUS gxos_dma_release_reference(
    GXOS_DMA_SERVICE *service, uint64_t handle, uint32_t driver_id)
{
    GXOS_DMA_ALLOCATION *allocation = find_allocation(service, handle);
    if (allocation == 0) return GXOS_DMA_SERVICE_NOT_FOUND;
    if (allocation->owner_driver_id != driver_id) return GXOS_DMA_SERVICE_OWNERSHIP_MISMATCH;
    if (allocation->reference_count == 0) return GXOS_DMA_SERVICE_INVALID_STATE;
    allocation->reference_count--;
    return GXOS_DMA_SERVICE_OK;
}

void gxos_dma_set_callback_service(GXOS_DMA_SERVICE *service)
{
    g_callback_service = service;
}

uint32_t gxos_dma_allocate_callback(
    uint64_t claim_handle, uint32_t driver_id, uint64_t requested_bytes,
    uint64_t alignment, uintptr_t result_address, uintptr_t result_capacity)
{
    return (uint32_t)gxos_dma_allocate(g_callback_service, claim_handle,
                                       driver_id, requested_bytes, alignment,
                                       result_address, result_capacity);
}

uint32_t gxos_dma_release_callback(uint64_t handle, uint32_t driver_id)
{
    return (uint32_t)gxos_dma_release(g_callback_service, handle, driver_id);
}

uint32_t gxos_dma_read_callback(uint64_t handle, uint32_t driver_id,
                                uint64_t offset, uintptr_t destination,
                                uint64_t length)
{
    return (uint32_t)gxos_dma_read(g_callback_service, handle, driver_id,
                                   offset, destination, length);
}

uint32_t gxos_dma_write_callback(uint64_t handle, uint32_t driver_id,
                                 uint64_t offset, uintptr_t source,
                                 uint64_t length)
{
    return (uint32_t)gxos_dma_write(g_callback_service, handle, driver_id,
                                    offset, source, length);
}

uint32_t gxos_dma_retain_callback(uint64_t handle, uint32_t driver_id)
{
    return (uint32_t)gxos_dma_retain(g_callback_service, handle, driver_id);
}

uint32_t gxos_dma_release_reference_callback(uint64_t handle,
                                             uint32_t driver_id)
{
    return (uint32_t)gxos_dma_release_reference(g_callback_service, handle,
                                                 driver_id);
}

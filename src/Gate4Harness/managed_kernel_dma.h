#ifndef GXOS_MANAGED_KERNEL_DMA_H
#define GXOS_MANAGED_KERNEL_DMA_H

#include <stdint.h>

#include "managed_kernel_abi.h"
#include "managed_kernel_mmio.h"
#include "memory_accounting.h"
#include "vm_substrate.h"

typedef enum {
    GXOS_DMA_SERVICE_OK = 0,
    GXOS_DMA_SERVICE_INVALID_ARGUMENT = 1,
    GXOS_DMA_SERVICE_NOT_INITIALIZED = 4,
    GXOS_DMA_SERVICE_RESOURCE_EXHAUSTED = 8,
    GXOS_DMA_SERVICE_NOT_FOUND = 9,
    GXOS_DMA_SERVICE_OWNERSHIP_MISMATCH = 10,
    GXOS_DMA_SERVICE_INVALID_STATE = 7,
    GXOS_DMA_SERVICE_OVERFLOW = 13
} GXOS_DMA_SERVICE_STATUS;

typedef int (*GXOS_DMA_ALLOCATE_CONTIGUOUS)(
    void *context, uint32_t page_count, uint64_t *physical_base_out);
typedef int (*GXOS_DMA_FREE_CONTIGUOUS)(
    void *context, uint64_t physical_base, uint32_t page_count);
typedef void *(*GXOS_DMA_PHYSICAL_ALIAS)(
    void *context, uint64_t physical_address);

typedef struct {
    void *context;
    GXOS_DMA_ALLOCATE_CONTIGUOUS allocate_contiguous;
    GXOS_DMA_FREE_CONTIGUOUS free_contiguous;
    GXOS_DMA_PHYSICAL_ALIAS physical_alias;
} GXOS_DMA_PLATFORM;

typedef struct {
    uint32_t live;
    uint32_t reservation_slot;
    uint64_t allocation_id;
    uint64_t generation;
    uint64_t claim_handle;
    uint32_t owner_driver_id;
    uint32_t reference_count;
    uint64_t virtual_address;
    uint64_t physical_address;
    uint64_t byte_length;
    uint64_t requested_bytes;
    uint64_t page_count;
    uint64_t alignment;
    uint64_t region_identity;
} GXOS_DMA_ALLOCATION;

typedef struct {
    GXOS_MMIO_SERVICE *mmio;
    GXOS_VM_ARENA *arena;
    GXOS_VM_PAGING *paging;
    GXOS_VM_REGION_LEDGER *region_ledger;
    GXOS_PHYSICAL_LEDGER *physical_ledger;
    GXOS_DMA_PLATFORM platform;
    uint64_t generation;
    uint64_t max_bus_address;
    uint32_t initialized;
    uint32_t live_count;
    uint64_t live_pages;
    uint64_t next_generation;
    GXOS_DMA_ALLOCATION allocations[GX_MANAGED_KERNEL_DMA_MAX_ALLOCATIONS];
} GXOS_DMA_SERVICE;

int gxos_dma_validate_request(uint64_t requested_bytes, uint64_t alignment,
                              uint32_t max_pages);
int gxos_dma_validate_handle(uint64_t handle, uint32_t capacity,
                             uint32_t *slot_out, uint32_t *generation_out);
GXOS_DMA_SERVICE_STATUS gxos_dma_service_init(
    GXOS_DMA_SERVICE *service, GXOS_MMIO_SERVICE *mmio,
    GXOS_VM_ARENA *arena, GXOS_VM_PAGING *paging,
    GXOS_VM_REGION_LEDGER *region_ledger, GXOS_PHYSICAL_LEDGER *physical_ledger,
    GXOS_DMA_PLATFORM platform, uint64_t generation, uint64_t max_bus_address);
GXOS_DMA_SERVICE_STATUS gxos_dma_service_teardown(GXOS_DMA_SERVICE *service);
GXOS_DMA_SERVICE_STATUS gxos_dma_allocate(
    GXOS_DMA_SERVICE *service, uint64_t claim_handle, uint32_t driver_id,
    uint64_t requested_bytes, uint64_t alignment,
    uintptr_t result_address, uintptr_t result_capacity);
GXOS_DMA_SERVICE_STATUS gxos_dma_release(
    GXOS_DMA_SERVICE *service, uint64_t handle, uint32_t driver_id);
GXOS_DMA_SERVICE_STATUS gxos_dma_read(
    GXOS_DMA_SERVICE *service, uint64_t handle, uint32_t driver_id,
    uint64_t offset, uintptr_t destination, uint64_t length);
GXOS_DMA_SERVICE_STATUS gxos_dma_write(
    GXOS_DMA_SERVICE *service, uint64_t handle, uint32_t driver_id,
    uint64_t offset, uintptr_t source, uint64_t length);
GXOS_DMA_SERVICE_STATUS gxos_dma_retain(
    GXOS_DMA_SERVICE *service, uint64_t handle, uint32_t driver_id);
GXOS_DMA_SERVICE_STATUS gxos_dma_release_reference(
    GXOS_DMA_SERVICE *service, uint64_t handle, uint32_t driver_id);

void gxos_dma_set_callback_service(GXOS_DMA_SERVICE *service);
uint32_t gxos_dma_allocate_callback(
    uint64_t claim_handle, uint32_t driver_id, uint64_t requested_bytes,
    uint64_t alignment, uintptr_t result_address, uintptr_t result_capacity);
uint32_t gxos_dma_release_callback(uint64_t handle, uint32_t driver_id);
uint32_t gxos_dma_read_callback(
    uint64_t handle, uint32_t driver_id, uint64_t offset,
    uintptr_t destination, uint64_t length);
uint32_t gxos_dma_write_callback(
    uint64_t handle, uint32_t driver_id, uint64_t offset,
    uintptr_t source, uint64_t length);
uint32_t gxos_dma_retain_callback(uint64_t handle, uint32_t driver_id);
uint32_t gxos_dma_release_reference_callback(uint64_t handle,
                                             uint32_t driver_id);

#endif

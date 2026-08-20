#ifndef GXOS_MANAGED_KERNEL_MEMORY_H
#define GXOS_MANAGED_KERNEL_MEMORY_H

#include <stdint.h>

#include "managed_kernel_abi.h"
#include "memory_accounting.h"
#include "vm_substrate.h"

#define GXOS_MANAGED_KERNEL_MEMORY_SLOT_COUNT \
    GX_MANAGED_KERNEL_MEMORY_MAX_LIVE_ALLOCATIONS

typedef struct {
    uint32_t live;
    uint32_t reservation_slot;
    uint64_t allocation_id;
    uint64_t virtual_address;
    uint64_t byte_length;
    uint64_t page_count;
    uint64_t page_size;
    uint32_t flags;
    uint64_t region_identity;
} GXOS_MANAGED_KERNEL_MEMORY_ALLOCATION;

typedef struct {
    GXOS_VM_ARENA *arena;
    GXOS_VM_PAGING *paging;
    GXOS_VM_REGION_LEDGER *region_ledger;
    GXOS_PHYSICAL_LEDGER *physical_ledger;
    GXOS_VM_PAGE_ALLOCATOR data_allocator;
    uint64_t generation;
    uint32_t operational;
    uint32_t max_pages_per_allocation;
    uint32_t max_live_allocations;
    uint64_t max_total_pages;
    uint32_t live_count;
    uint64_t live_pages;
    uint64_t next_allocation_id;
    GXOS_MANAGED_KERNEL_MEMORY_ALLOCATION allocations[
        GXOS_MANAGED_KERNEL_MEMORY_SLOT_COUNT];
} GXOS_MANAGED_KERNEL_MEMORY_CONTEXT;

void gxos_managed_kernel_memory_init(
    GXOS_MANAGED_KERNEL_MEMORY_CONTEXT *context,
    GXOS_VM_ARENA *arena,
    GXOS_VM_PAGING *paging,
    GXOS_VM_REGION_LEDGER *region_ledger,
    GXOS_PHYSICAL_LEDGER *physical_ledger,
    GXOS_VM_PAGE_ALLOCATOR data_allocator,
    uint64_t generation);

void gxos_managed_kernel_memory_set_operational(
    GXOS_MANAGED_KERNEL_MEMORY_CONTEXT *context,
    uint32_t operational);

GX_MANAGED_STATUS gxos_managed_kernel_memory_allocate(
    GXOS_MANAGED_KERNEL_MEMORY_CONTEXT *context,
    uint64_t page_count,
    uint32_t flags,
    uintptr_t output_address,
    uintptr_t output_capacity);

GX_MANAGED_STATUS gxos_managed_kernel_memory_release(
    GXOS_MANAGED_KERNEL_MEMORY_CONTEXT *context,
    uintptr_t request_address,
    uintptr_t request_capacity);

int gxos_managed_kernel_memory_validate(
    const GXOS_MANAGED_KERNEL_MEMORY_CONTEXT *context);
int gxos_managed_kernel_memory_has_no_live_allocations(
    const GXOS_MANAGED_KERNEL_MEMORY_CONTEXT *context);

#endif

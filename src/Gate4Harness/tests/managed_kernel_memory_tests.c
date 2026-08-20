#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "managed_kernel_memory.h"

#define FAKE_PAGE_CAPACITY 2048U

static uint32_t g_failures;

#define REQUIRE(condition) do { \
    if (!(condition)) { \
        fprintf(stderr, "managed memory test failure: %s:%d: %s\n", \
                __FILE__, __LINE__, #condition); \
        ++g_failures; \
    } \
} while (0)

typedef struct {
    uint64_t physical;
    uint8_t *alias;
    uint32_t live;
} FAKE_PAGE;

typedef struct {
    FAKE_PAGE pages[FAKE_PAGE_CAPACITY];
    GXOS_PHYSICAL_LEDGER *ledger;
    uint32_t allocation_count;
    uint32_t fail_after;
    GXOS_MEMORY_ALLOCATION_CLASS allocation_class;
    GXOS_MEMORY_OWNER owner;
    uint64_t commit_impact_bytes;
    uint64_t physical_base;
} FAKE_MEMORY;

static void fake_init(FAKE_MEMORY *memory, GXOS_PHYSICAL_LEDGER *ledger,
                      GXOS_MEMORY_ALLOCATION_CLASS allocation_class,
                      GXOS_MEMORY_OWNER owner, uint64_t physical_base)
{
    memset(memory, 0, sizeof(*memory));
    memory->ledger = ledger;
    memory->fail_after = UINT32_MAX;
    memory->allocation_class = allocation_class;
    memory->owner = owner;
    memory->commit_impact_bytes = GXOS_VM_PAGE_SIZE;
    memory->physical_base = physical_base;
}

static void *fake_alias(void *context, uint64_t physical)
{
    FAKE_MEMORY *memory = (FAKE_MEMORY *)context;
    uint32_t index;
    for (index = 0; index != FAKE_PAGE_CAPACITY; ++index) {
        if (memory->pages[index].live && memory->pages[index].physical == physical) {
            return memory->pages[index].alias;
        }
    }
    return 0;
}

static int fake_allocate(void *context, uint64_t *physical_out,
                         void **alias_out)
{
    FAKE_MEMORY *memory = (FAKE_MEMORY *)context;
    uint32_t index;
    GXOS_PHYSICAL_ALLOCATION allocation;
    uint32_t ledger_slot;
    if (physical_out == 0 || alias_out == 0 ||
        memory->allocation_count >= memory->fail_after) return 0;
    for (index = 0; index != FAKE_PAGE_CAPACITY; ++index) {
        if (!memory->pages[index].live) {
            uint8_t *alias = (uint8_t *)calloc(1, GXOS_VM_PAGE_SIZE);
            if (alias == 0) return 0;
            memory->pages[index].physical = memory->physical_base +
                (uint64_t)index * GXOS_VM_PAGE_SIZE;
            memory->pages[index].alias = alias;
            memory->pages[index].live = 1;
            memset(&allocation, 0, sizeof(allocation));
            allocation.base = memory->pages[index].physical;
            allocation.bytes = GXOS_VM_PAGE_SIZE;
            allocation.pages = 1;
            allocation.allocation_class = memory->allocation_class;
            allocation.owner = memory->owner;
            allocation.physical_impact_bytes = GXOS_VM_PAGE_SIZE;
            allocation.commit_impact_bytes = memory->commit_impact_bytes;
            allocation.generation = memory->ledger->generation;
            if (gxos_physical_ledger_insert(memory->ledger, &allocation,
                                            &ledger_slot) != GXOS_LEDGER_STATUS_OK) {
                free(alias);
                memset(&memory->pages[index], 0, sizeof(memory->pages[index]));
                return 0;
            }
            memory->allocation_count++;
            *physical_out = memory->pages[index].physical;
            *alias_out = alias;
            return 1;
        }
    }
    return 0;
}

static void fake_free(void *context, uint64_t physical, void *alias)
{
    FAKE_MEMORY *memory = (FAKE_MEMORY *)context;
    uint32_t index;
    uint32_t ledger_slot;
    (void)alias;
    for (index = 0; index != FAKE_PAGE_CAPACITY; ++index) {
        if (memory->pages[index].live && memory->pages[index].physical == physical) {
            if (gxos_physical_ledger_find(memory->ledger, physical,
                                          GXOS_VM_PAGE_SIZE, &ledger_slot)) {
                REQUIRE(gxos_physical_ledger_remove(memory->ledger, ledger_slot) ==
                        GXOS_LEDGER_STATUS_OK);
            }
            free(memory->pages[index].alias);
            memset(&memory->pages[index], 0, sizeof(memory->pages[index]));
            return;
        }
    }
    REQUIRE(0);
}

static GXOS_VM_PAGE_ALLOCATOR allocator(FAKE_MEMORY *memory)
{
    GXOS_VM_PAGE_ALLOCATOR value;
    memset(&value, 0, sizeof(value));
    value.context = memory;
    value.allocate_page = fake_allocate;
    value.free_page = fake_free;
    value.physical_alias = fake_alias;
    return value;
}

static void seed_root(FAKE_MEMORY *memory, uint64_t *root_out)
{
    uint64_t root;
    uint64_t pdpt;
    uint64_t pd;
    void *root_alias;
    void *pdpt_alias;
    void *pd_alias;
    GXOS_VM_PAGE_ALLOCATOR pages = allocator(memory);
    REQUIRE(pages.allocate_page(pages.context, &root, &root_alias));
    REQUIRE(pages.allocate_page(pages.context, &pdpt, &pdpt_alias));
    REQUIRE(pages.allocate_page(pages.context, &pd, &pd_alias));
    ((uint64_t *)root_alias)[0] = pdpt | 3U;
    ((uint64_t *)pdpt_alias)[0] = pd | 3U;
    (void)pd_alias;
    *root_out = root;
}

static void free_all_pages(FAKE_MEMORY *memory)
{
    uint32_t index;
    for (index = 0; index != FAKE_PAGE_CAPACITY; ++index) {
        if (memory->pages[index].live) {
            fake_free(memory, memory->pages[index].physical,
                      memory->pages[index].alias);
        }
    }
}

static GX_MANAGED_KERNEL_MEMORY_RELEASE_V1 release_request(
    const GX_MANAGED_KERNEL_MEMORY_ALLOCATION_V1 *allocation)
{
    GX_MANAGED_KERNEL_MEMORY_RELEASE_V1 request;
    memset(&request, 0, sizeof(request));
    request.Size = GX_MANAGED_KERNEL_MEMORY_RELEASE_V1_SIZE;
    request.AbiVersion = GX_MANAGED_KERNEL_MEMORY_SERVICES_ABI_V1;
    request.AllocationId = allocation->AllocationId;
    request.VirtualAddress = allocation->VirtualAddress;
    request.ByteLength = allocation->ByteLength;
    request.PageCount = allocation->PageCount;
    request.PageSize = allocation->PageSize;
    return request;
}

static void test_memory_service(void)
{
    FAKE_MEMORY table_memory;
    FAKE_MEMORY data_memory;
    GXOS_PHYSICAL_LEDGER ledger;
    GXOS_VM_ARENA arena;
    GXOS_VM_PAGING paging;
    GXOS_VM_REGION_LEDGER regions;
    GXOS_MANAGED_KERNEL_MEMORY_CONTEXT context;
    GXOS_VM_PAGE_ALLOCATOR table_allocator;
    GXOS_VM_PAGE_ALLOCATOR data_allocator;
    GXOS_VM_COMMIT_OPERATION operation;
    GX_MANAGED_KERNEL_MEMORY_ALLOCATION_V1 first;
    GX_MANAGED_KERNEL_MEMORY_ALLOCATION_V1 second;
    GX_MANAGED_KERNEL_MEMORY_RELEASE_V1 request;
    uint64_t root;
    uint32_t baseline_live;
    uint64_t baseline_physical;
    uint64_t baseline_commit;
    uint32_t baseline_reservations;
    uint32_t baseline_commitments;
    uint64_t baseline_reserved;
    uint64_t baseline_committed;
    GX_MANAGED_STATUS status;

    gxos_physical_ledger_init(&ledger, 1);
    fake_init(&table_memory, &ledger, GXOS_MEMORY_ALLOCATION_PAGE_TABLE,
              GXOS_MEMORY_OWNER_PAGING, 0x100000ULL);
    fake_init(&data_memory, &ledger, GXOS_MEMORY_ALLOCATION_MANAGED_KERNEL,
              GXOS_MEMORY_OWNER_MANAGED_KERNEL,
              0x100000ULL + (uint64_t)FAKE_PAGE_CAPACITY * GXOS_VM_PAGE_SIZE);
    seed_root(&table_memory, &root);
    gxos_vm_arena_init(&arena, GXOS_VM_ARENA_BASE, GXOS_VM_ARENA_LENGTH, 1);
    table_allocator = allocator(&table_memory);
    REQUIRE(gxos_vm_paging_create(&paging, root, arena.base, arena.length, 1,
                                  &table_allocator) == GXOS_VM_PAGING_STATUS_OK);
    gxos_vm_region_ledger_init(&regions);
    data_allocator = allocator(&data_memory);
    {
        uint64_t warm_base;
        uint32_t warm_slot;
        uint32_t warm_pages;
        uint32_t commitment_slot;
        uint64_t physical;
        REQUIRE(gxos_vm_arena_reserve_any(
                    &arena, GXOS_VM_PAGE_SIZE, GXOS_MEMORY_ALLOCATION_VM_DATA,
                    GXOS_MEMORY_OWNER_VM, 1, &warm_base, &warm_slot) ==
                GXOS_VM_STATUS_OK);
        memset(&operation, 0, sizeof(operation));
        operation.arena = &arena;
        operation.paging = &paging;
        operation.data_allocator = data_allocator;
        operation.generation = 1;
        REQUIRE(gxos_vm_commit_range(&operation, warm_slot, warm_base,
                                     GXOS_VM_PAGE_SIZE, 1, 0, &warm_pages) ==
                GXOS_VM_COMMIT_OPERATION_OK && warm_pages == 1);
        REQUIRE(gxos_vm_arena_find_commitment(&arena, warm_base,
                                              &commitment_slot) == GXOS_VM_STATUS_OK);
        physical = arena.commitments[commitment_slot].physical_base;
        REQUIRE(gxos_vm_paging_unmap_page(&paging, warm_base, 0) ==
                GXOS_VM_PAGING_STATUS_OK);
        REQUIRE(gxos_vm_arena_decommit_page(&arena, warm_base, 0) ==
                GXOS_VM_STATUS_OK);
        data_allocator.free_page(data_allocator.context, physical,
                                 data_allocator.physical_alias(
                                     data_allocator.context, physical));
        REQUIRE(gxos_vm_arena_release(&arena, warm_slot) == GXOS_VM_STATUS_OK);
    }
    gxos_managed_kernel_memory_init(&context, &arena, &paging, &regions,
                                    &ledger, data_allocator, 1);
    REQUIRE(context.data_allocator.context == &data_memory);
    REQUIRE(gxos_managed_kernel_memory_validate(&context));
    REQUIRE(gxos_managed_kernel_memory_allocate(
                &context, 1, 0, (uintptr_t)&first, sizeof(first)) ==
            GX_MANAGED_INVALID_STATE);
    REQUIRE(gxos_managed_kernel_memory_allocate(
                &context, 1, 0, (uintptr_t)&first, sizeof(first)) ==
            GX_MANAGED_INVALID_STATE);

    gxos_managed_kernel_memory_set_operational(&context, 1);
    REQUIRE(gxos_managed_kernel_memory_allocate(
                &context, 0, 0, (uintptr_t)&first, sizeof(first)) ==
            GX_MANAGED_INVALID_ARGUMENT);
    REQUIRE(gxos_managed_kernel_memory_allocate(
                &context, 1, 1, (uintptr_t)&first, sizeof(first)) ==
            GX_MANAGED_INVALID_ARGUMENT);
    memset(&first, 0xA5, sizeof(first));
    baseline_live = ledger.live_count;
    baseline_physical = ledger.physical_bytes;
    baseline_commit = ledger.commit_bytes;
    baseline_reservations = arena.reservation_count;
    baseline_commitments = arena.commitment_count;
    baseline_reserved = arena.total_reserved_bytes;
    baseline_committed = arena.total_committed_bytes;
    data_memory.fail_after = data_memory.allocation_count + 1U;
    status = gxos_managed_kernel_memory_allocate(
        &context, 4, 0, (uintptr_t)&first, sizeof(first));
    REQUIRE(status == GX_MANAGED_RESOURCE_EXHAUSTED);
    REQUIRE(first.Size == 0xA5A5A5A5U && first.AllocationId ==
            0xA5A5A5A5A5A5A5A5ULL);
    REQUIRE(ledger.live_count == baseline_live &&
            ledger.physical_bytes == baseline_physical &&
            ledger.commit_bytes == baseline_commit &&
            arena.reservation_count == baseline_reservations &&
            arena.commitment_count == baseline_commitments &&
            arena.total_reserved_bytes == baseline_reserved &&
            arena.total_committed_bytes == baseline_committed);
    data_memory.fail_after = UINT32_MAX;

    REQUIRE(gxos_managed_kernel_memory_allocate(
                &context, 4, 0, (uintptr_t)&first, sizeof(first)) ==
            GX_MANAGED_OK);
    REQUIRE(gxos_managed_kernel_memory_allocate(
                &context, 2, 0, (uintptr_t)&second, sizeof(second)) ==
            GX_MANAGED_OK);
    REQUIRE(first.AllocationId != second.AllocationId &&
            first.VirtualAddress != second.VirtualAddress &&
            gxos_managed_kernel_memory_validate(&context));
    request = release_request(&first);
    request.VirtualAddress++;
    REQUIRE(gxos_managed_kernel_memory_release(
                &context, (uintptr_t)&request, sizeof(request)) ==
            GX_MANAGED_OWNERSHIP_MISMATCH);
    request = release_request(&first);
    REQUIRE(gxos_managed_kernel_memory_release(
                &context, (uintptr_t)&request, sizeof(request)) ==
            GX_MANAGED_OK);
    REQUIRE(gxos_managed_kernel_memory_validate(&context));
    request = release_request(&first);
    REQUIRE(gxos_managed_kernel_memory_release(
                &context, (uintptr_t)&request, sizeof(request)) ==
            GX_MANAGED_NOT_FOUND);
    request = release_request(&second);
    REQUIRE(gxos_managed_kernel_memory_release(
                &context, (uintptr_t)&request, sizeof(request)) ==
            GX_MANAGED_OK);
    REQUIRE(gxos_managed_kernel_memory_has_no_live_allocations(&context));
    REQUIRE(ledger.live_count == baseline_live &&
            ledger.physical_bytes == baseline_physical &&
            ledger.commit_bytes == baseline_commit &&
            arena.reservation_count == baseline_reservations &&
            arena.commitment_count == baseline_commitments &&
            arena.total_reserved_bytes == baseline_reserved &&
            arena.total_committed_bytes == baseline_committed &&
            gxos_physical_ledger_validate(&ledger) &&
            gxos_vm_arena_validate(&arena) &&
            gxos_vm_region_ledger_validate(&regions));
    free_all_pages(&table_memory);
    free_all_pages(&data_memory);
}

int main(void)
{
    test_memory_service();
    if (g_failures != 0) {
        printf("MANAGED_KERNEL_MEMORY_HOST_TESTS=FAILED failures=%u\n", g_failures);
        return 1;
    }
    printf("MANAGED_KERNEL_MEMORY_HOST_TESTS=PASSED\n");
    return 0;
}

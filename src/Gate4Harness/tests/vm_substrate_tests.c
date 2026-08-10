#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "vm_substrate.h"

static unsigned g_failures;

#define REQUIRE(condition) do { \
    if (!(condition)) { \
        fprintf(stderr, "VM substrate test failure: %s:%d: %s\n", \
                __FILE__, __LINE__, #condition); \
        g_failures++; \
    } \
} while (0)

#define FAKE_PAGE_CAPACITY 256U

typedef struct {
    uint64_t physical;
    uint8_t *alias;
    uint32_t live;
} FAKE_PAGE;

typedef struct {
    FAKE_PAGE pages[FAKE_PAGE_CAPACITY];
    uint32_t live_count;
    uint32_t allocation_count;
    uint32_t fail_after;
} FAKE_MEMORY;

static void fake_init(FAKE_MEMORY *memory)
{
    memset(memory, 0, sizeof(*memory));
    memory->fail_after = UINT32_MAX;
}

static void *fake_alias(void *context, uint64_t physical)
{
    FAKE_MEMORY *memory = (FAKE_MEMORY *)context;
    uint32_t index;
    for (index = 0; index != FAKE_PAGE_CAPACITY; ++index) {
        if (memory->pages[index].live &&
            memory->pages[index].physical == physical) {
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
    if (memory->allocation_count >= memory->fail_after) return 0;
    for (index = 0; index != FAKE_PAGE_CAPACITY; ++index) {
        if (!memory->pages[index].live) {
            uint8_t *alias = (uint8_t *)calloc(1, 4096);
            if (alias == 0) return 0;
            memory->pages[index].physical = 0x100000ULL +
                (uint64_t)index * 0x1000ULL;
            memory->pages[index].alias = alias;
            memory->pages[index].live = 1;
            memory->live_count++;
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
    (void)alias;
    for (index = 0; index != FAKE_PAGE_CAPACITY; ++index) {
        if (memory->pages[index].live &&
            memory->pages[index].physical == physical) {
            free(memory->pages[index].alias);
            memset(&memory->pages[index], 0, sizeof(memory->pages[index]));
            memory->live_count--;
            return;
        }
    }
    g_failures++;
}

static GXOS_VM_PAGE_ALLOCATOR fake_allocator(FAKE_MEMORY *memory)
{
    GXOS_VM_PAGE_ALLOCATOR allocator;
    memset(&allocator, 0, sizeof(allocator));
    allocator.context = memory;
    allocator.allocate_page = fake_allocate;
    allocator.free_page = fake_free;
    allocator.physical_alias = fake_alias;
    return allocator;
}

static uint64_t fake_seed_current_root(FAKE_MEMORY *memory)
{
    uint64_t root_physical;
    uint64_t pdpt_physical;
    uint64_t pd_physical;
    void *root_alias;
    void *pdpt_alias;
    void *pd_alias;
    GXOS_VM_PAGE_ALLOCATOR allocator = fake_allocator(memory);
    REQUIRE(fake_allocate(memory, &root_physical, &root_alias));
    REQUIRE(fake_allocate(memory, &pdpt_physical, &pdpt_alias));
    REQUIRE(fake_allocate(memory, &pd_physical, &pd_alias));
    ((uint64_t *)root_alias)[0] = pdpt_physical | 3U;
    ((uint64_t *)pdpt_alias)[0] = pd_physical | 3U;
    /* Foreign 2-MiB mapping used to prove large-page traversal/refusal. */
    ((uint64_t *)pd_alias)[1] = 0x400000ULL | 0x83ULL;
    (void)allocator;
    return root_physical;
}

static int prepare_paging(FAKE_MEMORY *memory, GXOS_VM_PAGING *paging,
                          GXOS_VM_ARENA *arena,
                          GXOS_VM_PAGE_ALLOCATOR *allocator_out)
{
    uint64_t current_root = fake_seed_current_root(memory);
    GXOS_VM_PAGE_ALLOCATOR allocator = fake_allocator(memory);
    gxos_vm_arena_init(arena, GXOS_VM_ARENA_BASE, GXOS_VM_ARENA_LENGTH, 1);
    *allocator_out = allocator;
    return gxos_vm_paging_create(
               paging, current_root, arena->base, arena->length, 1,
               allocator_out) == GXOS_VM_PAGING_STATUS_OK;
}

static void test_sparse_arena(void)
{
    GXOS_VM_ARENA arena;
    uint64_t first;
    uint64_t second;
    uint32_t first_slot;
    uint32_t second_slot;
    gxos_vm_arena_init(&arena, GXOS_VM_ARENA_BASE, GXOS_VM_ARENA_LENGTH, 1);
    REQUIRE(gxos_vm_arena_available(&arena) == GXOS_VM_ARENA_LENGTH);
    REQUIRE(gxos_vm_arena_reserve_any(&arena, 1, 7, 8, 1, &first,
                                      &first_slot) == GXOS_VM_STATUS_OK);
    REQUIRE(first == GXOS_VM_ARENA_BASE &&
            arena.reservations[first_slot].requested_bytes == 1 &&
            arena.reservations[first_slot].bytes == GXOS_VM_PAGE_SIZE);
    REQUIRE(gxos_vm_arena_reserve_any(&arena, 0x1000, 7, 8, 1, &second,
                                      &second_slot) == GXOS_VM_STATUS_OK);
    REQUIRE(second == first + GXOS_VM_RESERVATION_GRANULARITY);
    REQUIRE(gxos_vm_arena_find_reservation(&arena, first + 0x100,
                                           &first_slot));
    REQUIRE(gxos_vm_arena_reserve_fixed(&arena, first, 0x1000, 7, 8, 1,
                                        &second_slot) == GXOS_VM_STATUS_OVERLAP);
    REQUIRE(gxos_vm_arena_reserve_fixed(
                &arena, first + 1, 0x1000, 7, 8, 1, &second_slot) ==
            GXOS_VM_STATUS_ALIGNMENT);
    REQUIRE(gxos_vm_arena_release(&arena, first_slot) == GXOS_VM_STATUS_OK);
    REQUIRE(gxos_vm_arena_reserve_any(&arena, 0x1000, 7, 8, 1, &first,
                                      &first_slot) == GXOS_VM_STATUS_OK &&
            first == GXOS_VM_ARENA_BASE);
    REQUIRE(gxos_vm_arena_validate(&arena));
    {
        GXOS_VM_ARENA large;
        uint64_t large_base;
        uint32_t large_slot;
        gxos_vm_arena_init(&large, 0x0000100000000000ULL,
                           0x0000100000000000ULL, 1);
        REQUIRE(gxos_vm_arena_reserve_any(
                    &large, 63ULL * 1024ULL * 1024ULL * 1024ULL,
                    1, 1, 1, &large_base, &large_slot) ==
                GXOS_VM_STATUS_OK);
        REQUIRE(large.reservations[large_slot].bytes ==
                63ULL * 1024ULL * 1024ULL * 1024ULL &&
                large.reservation_count == 1 && large.commitment_count == 0);
        REQUIRE(gxos_vm_arena_release(&large, large_slot) == GXOS_VM_STATUS_OK);
        REQUIRE(gxos_vm_arena_available(&large) == large.length);
    }
}

static void test_page_tables_and_commit(void)
{
    FAKE_MEMORY memory;
    GXOS_VM_PAGING paging;
    GXOS_VM_ARENA arena;
    GXOS_VM_PAGE_ALLOCATOR allocator;
    GXOS_VM_COMMIT_OPERATION operation;
    GXOS_VM_MAPPING mapping;
    uint64_t base;
    uint32_t reservation_slot;
    uint32_t new_pages;
    uint32_t before_live;
    fake_init(&memory);
    REQUIRE(prepare_paging(&memory, &paging, &arena, &allocator));
    REQUIRE(((uint64_t *)paging.root_alias)[0] != 0 &&
            (((uint64_t *)paging.root_alias)[GXOS_VM_ARENA_BASE >> 39] & 1U) == 0);
    REQUIRE(gxos_vm_paging_query(&paging, 0x200000, &mapping) ==
                GXOS_VM_PAGING_STATUS_OK && mapping.page_size == 0x200000ULL);
    REQUIRE(gxos_vm_paging_map_page(&paging, 0x200000, 0x500000, 1, 0) ==
                GXOS_VM_PAGING_STATUS_OUTSIDE_ARENA);
    REQUIRE(gxos_vm_paging_map_page(&paging, 0x0000800000000000ULL,
                                    0x500000, 1, 0) ==
                GXOS_VM_PAGING_STATUS_NONCANONICAL);
    REQUIRE(gxos_vm_paging_map_page(&paging, GXOS_VM_ARENA_BASE + 1,
                                    0x500000, 1, 0) ==
                GXOS_VM_PAGING_STATUS_ALIGNMENT);
    REQUIRE(gxos_vm_arena_reserve_any(&arena, 0x4000, 1, 1, 1, &base,
                                      &reservation_slot) == GXOS_VM_STATUS_OK);
    REQUIRE(gxos_vm_paging_query(&paging, base, &mapping) ==
                GXOS_VM_PAGING_STATUS_NOT_PRESENT);
    memset(&operation, 0, sizeof(operation));
    operation.arena = &arena;
    operation.paging = &paging;
    operation.data_allocator = allocator;
    operation.generation = 1;
    before_live = memory.live_count;
    REQUIRE(gxos_vm_commit_range(&operation, reservation_slot, base, 0x1000,
                                 1, 0, &new_pages) ==
                GXOS_VM_COMMIT_OPERATION_OK && new_pages == 1);
    REQUIRE(memory.live_count == before_live + 4U);
    REQUIRE(gxos_vm_commit_range(&operation, reservation_slot, base, 0x1000,
                                 1, 0, &new_pages) ==
                GXOS_VM_COMMIT_OPERATION_OK && new_pages == 0);
    REQUIRE(memory.live_count == before_live + 4U);
    REQUIRE(gxos_vm_paging_query(&paging, base, &mapping) ==
                GXOS_VM_PAGING_STATUS_OK && mapping.page_size == 0x1000ULL);
    REQUIRE(gxos_vm_paging_map_page(&paging, base, 0x600000, 1, 0) ==
                GXOS_VM_PAGING_STATUS_CONFLICT);
    {
        uint64_t physical = mapping.physical_base;
        REQUIRE(gxos_vm_paging_unmap_page(&paging, base, 0) ==
                GXOS_VM_PAGING_STATUS_OK);
        REQUIRE(gxos_vm_arena_decommit_page(&arena, base, &physical) ==
                GXOS_VM_STATUS_OK && physical != 0);
        allocator.free_page(allocator.context, physical,
                            allocator.physical_alias(allocator.context,
                                                     physical));
    }
    REQUIRE(gxos_vm_arena_release(&arena, reservation_slot) ==
                GXOS_VM_STATUS_OK);
    REQUIRE(gxos_vm_paging_query(&paging, 0x200000, &mapping) ==
                GXOS_VM_PAGING_STATUS_OK && mapping.page_size == 0x200000ULL);
    {
        FAKE_MEMORY large_memory;
        GXOS_VM_PAGING large_paging;
        GXOS_VM_ARENA large_arena;
        GXOS_VM_PAGE_ALLOCATOR large_allocator;
        uint64_t pdpt_physical;
        void *pdpt_alias;
        fake_init(&large_memory);
        REQUIRE(prepare_paging(&large_memory, &large_paging, &large_arena,
                               &large_allocator));
        REQUIRE(fake_allocate(&large_memory, &pdpt_physical, &pdpt_alias));
        ((uint64_t *)pdpt_alias)[0] = 0x800000ULL | 0x83ULL;
        ((uint64_t *)large_paging.root_alias)[GXOS_VM_ARENA_BASE >> 39] =
            pdpt_physical | 3U;
        REQUIRE(gxos_vm_paging_map_page(&large_paging, GXOS_VM_ARENA_BASE,
                                        0x900000, 1, 0) ==
                GXOS_VM_PAGING_STATUS_LARGE_PAGE);
        while (large_memory.live_count != 0) {
            uint32_t index;
            for (index = 0; index != FAKE_PAGE_CAPACITY; ++index) {
                if (large_memory.pages[index].live) {
                    large_allocator.free_page(
                        large_allocator.context,
                        large_memory.pages[index].physical,
                        large_memory.pages[index].alias);
                    break;
                }
            }
        }
    }
    while (memory.live_count != 0) {
        uint32_t index;
        for (index = 0; index != FAKE_PAGE_CAPACITY; ++index) {
            if (memory.pages[index].live) {
                allocator.free_page(allocator.context, memory.pages[index].physical,
                                    memory.pages[index].alias);
                break;
            }
        }
    }
}

static void test_commit_rollbacks(void)
{
    FAKE_MEMORY memory;
    GXOS_VM_PAGING paging;
    GXOS_VM_ARENA arena;
    GXOS_VM_PAGE_ALLOCATOR allocator;
    GXOS_VM_COMMIT_OPERATION operation;
    uint64_t base;
    uint32_t slot;
    uint32_t before_live;
    uint32_t new_pages;
    fake_init(&memory);
    REQUIRE(prepare_paging(&memory, &paging, &arena, &allocator));
    REQUIRE(gxos_vm_arena_reserve_any(&arena, 0x3000, 1, 1, 1, &base, &slot) ==
            GXOS_VM_STATUS_OK);
    memset(&operation, 0, sizeof(operation));
    operation.arena = &arena;
    operation.paging = &paging;
    operation.data_allocator = allocator;
    operation.generation = 1;
    before_live = memory.live_count;
    memory.fail_after = memory.allocation_count + 1U;
    REQUIRE(gxos_vm_commit_range(&operation, slot, base, 0x3000, 1, 0,
                                 &new_pages) == GXOS_VM_COMMIT_OPERATION_ALLOCATION);
    REQUIRE(new_pages == 0 && arena.total_committed_bytes == 0 &&
            memory.live_count == before_live);
    memory.fail_after = UINT32_MAX;
    REQUIRE(gxos_vm_paging_map_page(&paging, base, 0x700000, 1, 0) ==
            GXOS_VM_PAGING_STATUS_OK);
    before_live = memory.live_count;
    REQUIRE(gxos_vm_commit_range(&operation, slot, base, 0x1000, 1, 0,
                                 &new_pages) == GXOS_VM_COMMIT_OPERATION_ALLOCATION);
    REQUIRE(new_pages == 0 && arena.total_committed_bytes == 0 &&
            memory.live_count == before_live);
    (void)gxos_vm_paging_unmap_page(&paging, base, 0);
    REQUIRE(gxos_vm_arena_release(&arena, slot) == GXOS_VM_STATUS_OK);
    while (memory.live_count != 0) {
        uint32_t index;
        for (index = 0; index != FAKE_PAGE_CAPACITY; ++index) {
            if (memory.pages[index].live) {
                allocator.free_page(allocator.context, memory.pages[index].physical,
                                    memory.pages[index].alias);
                break;
            }
        }
    }
}

int main(void)
{
    test_sparse_arena();
    test_page_tables_and_commit();
    test_commit_rollbacks();
    if (g_failures != 0) return 1;
    puts("VM_SUBSTRATE_HOST_TESTS=PASSED");
    return 0;
}

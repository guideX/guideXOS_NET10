#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "virtual_memory.h"
#include "global_memory_status_ex.h"

static unsigned g_failures;

#define REQUIRE(condition) do { \
    if (!(condition)) { \
        fprintf(stderr, "Virtual memory test failure: %s:%d: %s\n", \
                __FILE__, __LINE__, #condition); \
        g_failures++; \
    } \
} while (0)

#define FAKE_PAGE_CAPACITY 1024U

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

typedef struct {
    FAKE_MEMORY memory;
    GXOS_VM_PAGING paging;
    GXOS_VM_ARENA arena;
    GXOS_VM_PUBLIC_CONTEXT context;
    uint32_t last_error;
} TEST_ENV;

typedef struct {
    FAKE_MEMORY *memory;
    GXOS_PHYSICAL_LEDGER *ledger;
} ACCOUNTED_ALLOCATOR;

typedef struct {
    TEST_ENV environment;
    ACCOUNTED_ALLOCATOR allocator_context;
    GXOS_PHYSICAL_LEDGER ledger;
    GXOS_MEMORY_CLASSIFICATION classification;
    GXOS_MEMORY_SNAPSHOT startup;
    GXOS_MEMORY_STATUS_EX_MEMORY_REGION region;
    GXOS_MEMORY_STATUS_EX_CONTEXT status_context;
} ACCOUNTING_ENV;

static int env_init(TEST_ENV *environment);

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
            uint8_t *alias = (uint8_t *)calloc(1, GXOS_VM_PAGE_SIZE);
            if (alias == 0) return 0;
            memory->pages[index].physical = 0x100000ULL +
                (uint64_t)index * GXOS_VM_PAGE_SIZE;
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

static void *accounted_alias(void *context, uint64_t physical)
{
    ACCOUNTED_ALLOCATOR *allocator = (ACCOUNTED_ALLOCATOR *)context;
    return fake_alias(allocator->memory, physical);
}

static int accounted_allocate(void *context, uint64_t *physical_out,
                              void **alias_out)
{
    ACCOUNTED_ALLOCATOR *allocator = (ACCOUNTED_ALLOCATOR *)context;
    GXOS_PHYSICAL_ALLOCATION allocation;
    uint32_t ledger_slot;
    if (!fake_allocate(allocator->memory, physical_out, alias_out)) return 0;
    memset(&allocation, 0, sizeof(allocation));
    allocation.base = *physical_out;
    allocation.bytes = GXOS_VM_PAGE_SIZE;
    allocation.pages = 1;
    allocation.allocation_class = GXOS_MEMORY_ALLOCATION_VM_DATA;
    allocation.owner = GXOS_MEMORY_OWNER_VM;
    allocation.physical_impact_bytes = GXOS_VM_PAGE_SIZE;
    allocation.commit_impact_bytes = GXOS_VM_PAGE_SIZE;
    allocation.generation = 1;
    if (gxos_physical_ledger_insert(allocator->ledger, &allocation,
                                    &ledger_slot) != GXOS_LEDGER_STATUS_OK) {
        fake_free(allocator->memory, *physical_out, *alias_out);
        *physical_out = 0;
        *alias_out = 0;
        return 0;
    }
    return 1;
}

static void accounted_free(void *context, uint64_t physical, void *alias)
{
    ACCOUNTED_ALLOCATOR *allocator = (ACCOUNTED_ALLOCATOR *)context;
    uint32_t ledger_slot;
    if (!gxos_physical_ledger_find(allocator->ledger, physical,
                                   GXOS_VM_PAGE_SIZE, &ledger_slot) ||
        gxos_physical_ledger_remove(allocator->ledger, ledger_slot) !=
            GXOS_LEDGER_STATUS_OK) {
        g_failures++;
        return;
    }
    fake_free(allocator->memory, physical, alias);
}

static int accounting_env_init(ACCOUNTING_ENV *state)
{
    GXOS_VM_PAGE_ALLOCATOR allocator;
    GXOS_PHYSICAL_SNAPSHOT physical;
    GXOS_COMMIT_MODEL commit;
    memset(state, 0, sizeof(*state));
    if (!env_init(&state->environment)) return 0;
    state->classification.valid = 1;
    state->classification.total_ram_like_bytes = 1ULL << 30;
    state->classification.conventional_bytes = 1ULL << 30;
    state->classification.class_bytes[GXOS_MEMORY_CLASS_CONVENTIONAL] =
        1ULL << 30;
    gxos_physical_ledger_init(&state->ledger, 1);
    if (gxos_physical_snapshot_create(&physical, &state->classification,
                                      &state->ledger, 2) !=
            GXOS_SNAPSHOT_STATUS_OK ||
        gxos_commit_model_create_no_pagefile(
            &commit, physical.total_ram_like_bytes,
            physical.available_physical_bytes,
            state->environment.arena.total_committed_bytes, 2) !=
            GXOS_COMMIT_STATUS_OK ||
        gxos_memory_snapshot_create(&state->startup, &physical,
                                    &state->environment.arena, &commit, 2) !=
            GXOS_SNAPSHOT_STATUS_OK) {
        return 0;
    }
    state->allocator_context.memory = &state->environment.memory;
    state->allocator_context.ledger = &state->ledger;
    memset(&allocator, 0, sizeof(allocator));
    allocator.context = &state->allocator_context;
    allocator.allocate_page = accounted_allocate;
    allocator.free_page = accounted_free;
    allocator.physical_alias = accounted_alias;
    state->environment.context.data_allocator = allocator;
    state->status_context.classification = &state->classification;
    state->status_context.startup_snapshot = &state->startup;
    state->status_context.ledger = &state->ledger;
    state->status_context.virtual_arena = &state->environment.arena;
    state->status_context.regions = &state->region;
    state->status_context.region_count = 1;
    state->status_context.accounting_generation = 2;
    state->status_context.accounting_generation_source = 0;
    return 1;
}

static int accounting_query(ACCOUNTING_ENV *state,
                            GXOS_MEMORY_STATUS_EX *buffer,
                            GXOS_MEMORY_STATUS_EX_REPORT *report)
{
    state->region.base = (uintptr_t)buffer;
    state->region.end = state->region.base + sizeof(*buffer);
    state->region.readable = 1;
    state->region.writable = 1;
    memset(buffer, 0, sizeof(*buffer));
    buffer->dwLength = GXOS_MEMORY_STATUS_EX_SIZE;
    return gxos_global_memory_status_ex_checked(buffer,
                                                &state->status_context,
                                                report);
}

static uint64_t fake_seed_current_root(FAKE_MEMORY *memory)
{
    uint64_t root_physical;
    uint64_t pdpt_physical;
    uint64_t pd_physical;
    void *root_alias;
    void *pdpt_alias;
    void *pd_alias;
    REQUIRE(fake_allocate(memory, &root_physical, &root_alias));
    REQUIRE(fake_allocate(memory, &pdpt_physical, &pdpt_alias));
    REQUIRE(fake_allocate(memory, &pd_physical, &pd_alias));
    ((uint64_t *)root_alias)[0] = pdpt_physical | 3U;
    ((uint64_t *)pdpt_alias)[0] = pd_physical | 3U;
    ((uint64_t *)pd_alias)[1] = 0x400000ULL | 0x83ULL;
    return root_physical;
}

static int env_init(TEST_ENV *environment)
{
    GXOS_VM_PAGE_ALLOCATOR allocator;
    uint64_t current_root;
    memset(environment, 0, sizeof(*environment));
    fake_init(&environment->memory);
    current_root = fake_seed_current_root(&environment->memory);
    gxos_vm_arena_init(&environment->arena, GXOS_VM_ARENA_BASE,
                       GXOS_VM_ARENA_LENGTH, 1);
    allocator = fake_allocator(&environment->memory);
    if (gxos_vm_paging_create(&environment->paging, current_root,
                              environment->arena.base,
                              environment->arena.length, 1, &allocator) !=
            GXOS_VM_PAGING_STATUS_OK) {
        return 0;
    }
    environment->context.arena = &environment->arena;
    environment->context.paging = &environment->paging;
    environment->context.data_allocator = allocator;
    environment->context.generation = 1;
    environment->context.last_error = &environment->last_error;
    return 1;
}

static void env_cleanup(TEST_ENV *environment)
{
    uint32_t index;
    for (index = 0; index != FAKE_PAGE_CAPACITY; ++index) {
        if (environment->memory.pages[index].live) {
            fake_free(&environment->memory,
                      environment->memory.pages[index].physical,
                      environment->memory.pages[index].alias);
        }
    }
}

static GXOS_VM_PUBLIC_STATUS allocate_public(
    TEST_ENV *environment, void *address, uint64_t size, uint32_t flags,
    uint32_t protection, GXOS_VM_PUBLIC_RESULT *result, void **returned)
{
    return gxos_vm_public_virtual_alloc(&environment->context, address, size,
                                        flags, protection, result, returned);
}

static void test_basic_validation(void)
{
    TEST_ENV environment;
    GXOS_VM_PUBLIC_RESULT result;
    void *returned;
    if (!env_init(&environment)) {
        REQUIRE(0);
        return;
    }
    REQUIRE(allocate_public(&environment, 0, 0,
                            GXOS_VM_PUBLIC_MEM_RESERVE,
                            GXOS_VM_PUBLIC_PAGE_READWRITE, &result,
                            &returned) == GXOS_VM_PUBLIC_STATUS_INVALID_ARGUMENT);
    REQUIRE(returned == 0 && environment.last_error ==
            GXOS_VM_PUBLIC_ERROR_INVALID_PARAMETER);
    REQUIRE(allocate_public(&environment, 0, 0x1000,
                            GXOS_VM_PUBLIC_MEM_RESERVE, 0x20, &result,
                            &returned) == GXOS_VM_PUBLIC_STATUS_INVALID_ARGUMENT);
    REQUIRE(allocate_public(&environment, 0, 0x1000,
                            GXOS_VM_PUBLIC_MEM_RESERVE |
                                GXOS_VM_PUBLIC_MEM_WRITE_WATCH,
                            GXOS_VM_PUBLIC_PAGE_READWRITE, &result,
                            &returned) == GXOS_VM_PUBLIC_STATUS_UNSUPPORTED);
    REQUIRE(environment.last_error == GXOS_VM_PUBLIC_ERROR_NOT_SUPPORTED);
    REQUIRE(allocate_public(&environment, 0, 0x1000,
                            GXOS_VM_PUBLIC_MEM_RESERVE |
                                GXOS_VM_PUBLIC_MEM_RESET,
                            GXOS_VM_PUBLIC_PAGE_READWRITE, &result,
                            &returned) == GXOS_VM_PUBLIC_STATUS_UNSUPPORTED);
    REQUIRE(allocate_public(&environment, 0, 0x1000,
                            GXOS_VM_PUBLIC_MEM_RESERVE |
                                GXOS_VM_PUBLIC_MEM_LARGE_PAGES,
                            GXOS_VM_PUBLIC_PAGE_READWRITE, &result,
                            &returned) == GXOS_VM_PUBLIC_STATUS_UNSUPPORTED);
    REQUIRE(allocate_public(&environment, 0, 0x1000,
                            GXOS_VM_PUBLIC_MEM_RESERVE |
                                GXOS_VM_PUBLIC_MEM_PHYSICAL,
                            GXOS_VM_PUBLIC_PAGE_READWRITE, &result,
                            &returned) == GXOS_VM_PUBLIC_STATUS_UNSUPPORTED);
    REQUIRE(allocate_public(&environment, 0, 0x1000,
                            GXOS_VM_PUBLIC_MEM_RESERVE |
                                GXOS_VM_PUBLIC_MEM_TOP_DOWN,
                            GXOS_VM_PUBLIC_PAGE_READWRITE, &result,
                            &returned) == GXOS_VM_PUBLIC_STATUS_UNSUPPORTED);
    REQUIRE(allocate_public(&environment, 0, 0x1000, 0x80000000U,
                            GXOS_VM_PUBLIC_PAGE_READWRITE, &result,
                            &returned) == GXOS_VM_PUBLIC_STATUS_INVALID_ARGUMENT);
    REQUIRE(allocate_public(&environment,
                            (void *)(uintptr_t)0x0000800000000000ULL,
                            0x1000, GXOS_VM_PUBLIC_MEM_COMMIT,
                            GXOS_VM_PUBLIC_PAGE_READWRITE, &result,
                            &returned) == GXOS_VM_PUBLIC_STATUS_INVALID_ARGUMENT);
    REQUIRE(allocate_public(&environment,
                            (void *)(uintptr_t)GXOS_VM_ARENA_BASE,
                            UINT64_MAX, GXOS_VM_PUBLIC_MEM_COMMIT,
                            GXOS_VM_PUBLIC_PAGE_READWRITE, &result,
                            &returned) == GXOS_VM_PUBLIC_STATUS_INVALID_ARGUMENT);
    env_cleanup(&environment);
}

static void test_reserve_and_commit(void)
{
    TEST_ENV environment;
    GXOS_VM_PUBLIC_RESULT result;
    GXOS_VM_MAPPING mapping;
    void *returned;
    uint64_t available_before;
    uint32_t live_before;
    uint32_t reservation_count;
    uint64_t base;
    uint32_t page;
    if (!env_init(&environment)) {
        REQUIRE(0);
        return;
    }
    available_before = gxos_vm_arena_available(&environment.arena);
    live_before = environment.memory.live_count;
    REQUIRE(allocate_public(&environment, 0, 0x1001,
                            GXOS_VM_PUBLIC_MEM_RESERVE,
                            GXOS_VM_PUBLIC_PAGE_READWRITE, &result,
                            &returned) == GXOS_VM_PUBLIC_STATUS_OK);
    base = (uint64_t)(uintptr_t)returned;
    REQUIRE(base == GXOS_VM_ARENA_BASE &&
            base % GXOS_VM_RESERVATION_GRANULARITY == 0 &&
            result.rounded_bytes == 0x2000 &&
            environment.arena.reservations[result.reservation_slot].bytes ==
                0x2000 && returned != 0);
    REQUIRE(gxos_vm_arena_available(&environment.arena) ==
            available_before - 0x2000);
    REQUIRE(environment.memory.live_count == live_before &&
            environment.arena.commitment_count == 0 &&
            gxos_vm_paging_query(&environment.paging, base, &mapping) ==
                GXOS_VM_PAGING_STATUS_NOT_PRESENT);
    reservation_count = environment.arena.reservation_count;
    REQUIRE(allocate_public(&environment, 0, 0x4000,
                            GXOS_VM_PUBLIC_MEM_RESERVE,
                            GXOS_VM_PUBLIC_PAGE_READWRITE, &result,
                            &returned) == GXOS_VM_PUBLIC_STATUS_OK);
    REQUIRE((uint64_t)(uintptr_t)returned ==
            base + GXOS_VM_RESERVATION_GRANULARITY &&
            environment.arena.reservation_count == reservation_count + 1U);
    REQUIRE(allocate_public(&environment, returned, 0x3000,
                            GXOS_VM_PUBLIC_MEM_COMMIT,
                            GXOS_VM_PUBLIC_PAGE_READWRITE, &result,
                            &returned) == GXOS_VM_PUBLIC_STATUS_OK);
    REQUIRE((uint64_t)(uintptr_t)returned == base +
            GXOS_VM_RESERVATION_GRANULARITY && result.new_page_count == 3U &&
            result.existing_page_count == 0 && result.rounded_bytes == 0x3000 &&
            environment.arena.reservation_count == reservation_count + 1U &&
            environment.arena.total_committed_bytes == 0x3000);
    for (page = 0; page != 3U; ++page) {
        uint32_t commitment_slot;
        const GXOS_VM_COMMITMENT *commitment;
        void *alias;
        REQUIRE(gxos_vm_paging_query(
                    &environment.paging,
                    base + GXOS_VM_RESERVATION_GRANULARITY +
                        page * GXOS_VM_PAGE_SIZE, &mapping) ==
                GXOS_VM_PAGING_STATUS_OK);
        REQUIRE(mapping.page_size == GXOS_VM_PAGE_SIZE && mapping.present &&
                (mapping.entry_flags & GXOS_X64_PAGING_ENTRY_WRITABLE) != 0 &&
                (mapping.entry_flags & GXOS_X64_PAGING_ENTRY_NO_EXECUTE) != 0);
        REQUIRE(gxos_vm_arena_find_commitment(
                    &environment.arena,
                    base + GXOS_VM_RESERVATION_GRANULARITY +
                        page * GXOS_VM_PAGE_SIZE, &commitment_slot) ==
                GXOS_VM_STATUS_OK);
        commitment = &environment.arena.commitments[commitment_slot];
        alias = environment.context.data_allocator.physical_alias(
            environment.context.data_allocator.context,
            commitment->physical_base);
        REQUIRE(alias != 0);
        for (uint32_t offset = 0; offset != GXOS_VM_PAGE_SIZE; ++offset) {
            REQUIRE(((uint8_t *)alias)[offset] == 0);
        }
    }
    live_before = environment.memory.live_count;
    REQUIRE(allocate_public(&environment, returned, 0x3000,
                            GXOS_VM_PUBLIC_MEM_COMMIT,
                            GXOS_VM_PUBLIC_PAGE_READWRITE, &result,
                            &returned) == GXOS_VM_PUBLIC_STATUS_OK &&
            result.new_page_count == 0 && result.existing_page_count == 3U &&
            environment.memory.live_count == live_before);
    REQUIRE(allocate_public(&environment, (void *)(uintptr_t)(base + 0x5000),
                            0x1000, GXOS_VM_PUBLIC_MEM_COMMIT,
                            GXOS_VM_PUBLIC_PAGE_READWRITE, &result,
                            &returned) == GXOS_VM_PUBLIC_STATUS_INVALID_ARGUMENT);
    REQUIRE(allocate_public(&environment, (void *)(uintptr_t)(base + 0x1F000),
                            0x20000, GXOS_VM_PUBLIC_MEM_COMMIT,
                            GXOS_VM_PUBLIC_PAGE_READWRITE, &result,
                            &returned) == GXOS_VM_PUBLIC_STATUS_INVALID_ARGUMENT);
    env_cleanup(&environment);
}

static void test_transactional_failures(void)
{
    TEST_ENV environment;
    GXOS_VM_PUBLIC_RESULT result;
    void *returned;
    uint64_t base;
    uint32_t slot;
    uint32_t live_before;
    uint32_t reservation_count_before;
    uint64_t available_before;
    if (!env_init(&environment)) {
        REQUIRE(0);
        return;
    }
    available_before = gxos_vm_arena_available(&environment.arena);
    REQUIRE(allocate_public(&environment, 0, 0x3000,
                            GXOS_VM_PUBLIC_MEM_RESERVE,
                            GXOS_VM_PUBLIC_PAGE_READWRITE, &result,
                            &returned) == GXOS_VM_PUBLIC_STATUS_OK);
    base = (uint64_t)(uintptr_t)returned;
    live_before = environment.memory.live_count;
    environment.memory.fail_after = environment.memory.allocation_count;
    REQUIRE(allocate_public(&environment, returned, 0x3000,
                            GXOS_VM_PUBLIC_MEM_COMMIT,
                            GXOS_VM_PUBLIC_PAGE_READWRITE, &result,
                            &returned) == GXOS_VM_PUBLIC_STATUS_ALLOCATION);
    REQUIRE(environment.memory.live_count == live_before &&
            environment.arena.total_committed_bytes == 0 &&
            environment.arena.reservation_count == 1U);
    environment.memory.fail_after = UINT32_MAX;
    REQUIRE(gxos_vm_arena_find_reservation(&environment.arena, base, &slot));
    REQUIRE(fake_allocate(&environment.memory, &base, &returned));
    /* Use the reserved base for the conflict, retaining the probe page. */
    {
        uint64_t probe_physical = base;
        void *probe_alias = returned;
        REQUIRE(gxos_vm_paging_map_page(&environment.paging,
                                        (uint64_t)(uintptr_t)(void *)(uintptr_t)
                                            GXOS_VM_ARENA_BASE,
                                        probe_physical, 1, 0) ==
                GXOS_VM_PAGING_STATUS_OK);
        live_before = environment.memory.live_count;
        REQUIRE(allocate_public(&environment,
                                (void *)(uintptr_t)GXOS_VM_ARENA_BASE, 0x1000,
                                GXOS_VM_PUBLIC_MEM_COMMIT,
                                GXOS_VM_PUBLIC_PAGE_READWRITE, &result,
                                &returned) == GXOS_VM_PUBLIC_STATUS_ALLOCATION);
        REQUIRE(environment.memory.live_count == live_before &&
                environment.arena.total_committed_bytes == 0);
        (void)gxos_vm_paging_unmap_page(&environment.paging,
                                        GXOS_VM_ARENA_BASE, 0);
        fake_free(&environment.memory, probe_physical, probe_alias);
    }
    reservation_count_before = environment.arena.reservation_count;
    environment.memory.fail_after = environment.memory.allocation_count;
    REQUIRE(allocate_public(&environment, 0, 0x2000,
                            GXOS_VM_PUBLIC_MEM_RESERVE |
                                GXOS_VM_PUBLIC_MEM_COMMIT,
                            GXOS_VM_PUBLIC_PAGE_READWRITE, &result,
                            &returned) == GXOS_VM_PUBLIC_STATUS_ALLOCATION);
    REQUIRE(returned == 0 && environment.arena.reservation_count ==
            reservation_count_before && gxos_vm_arena_available(&environment.arena) ==
            available_before - 0x3000);
    env_cleanup(&environment);
}

static void test_sparse_and_free(void)
{
    TEST_ENV environment;
    GXOS_VM_PUBLIC_RESULT result;
    void *returned;
    uint64_t base;
    uint64_t available_before;
    uint32_t live_before;
    uint32_t table_pages_before;
    if (!env_init(&environment)) {
        REQUIRE(0);
        return;
    }
    environment.arena.base = 0x0000100000000000ULL;
    environment.arena.length = 0x0000100000000000ULL;
    environment.arena.valid = 1;
    available_before = gxos_vm_arena_available(&environment.arena);
    live_before = environment.memory.live_count;
    REQUIRE(allocate_public(&environment, 0,
                            63ULL * 1024ULL * 1024ULL * 1024ULL,
                            GXOS_VM_PUBLIC_MEM_RESERVE,
                            GXOS_VM_PUBLIC_PAGE_READWRITE, &result,
                            &returned) == GXOS_VM_PUBLIC_STATUS_OK);
    REQUIRE(result.rounded_bytes == 63ULL * 1024ULL * 1024ULL * 1024ULL &&
            gxos_vm_arena_available(&environment.arena) == available_before -
                result.rounded_bytes && environment.memory.live_count ==
                live_before && environment.arena.commitment_count == 0);
    env_cleanup(&environment);

    REQUIRE(env_init(&environment));
    available_before = gxos_vm_arena_available(&environment.arena);
    live_before = environment.memory.live_count;
    REQUIRE(allocate_public(&environment, 0, 0x3000,
                            GXOS_VM_PUBLIC_MEM_RESERVE,
                            GXOS_VM_PUBLIC_PAGE_READWRITE, &result,
                            &returned) == GXOS_VM_PUBLIC_STATUS_OK);
    base = (uint64_t)(uintptr_t)returned;
    table_pages_before = environment.paging.owned_table_page_count;
    REQUIRE(allocate_public(&environment, returned, 0x3000,
                            GXOS_VM_PUBLIC_MEM_COMMIT,
                            GXOS_VM_PUBLIC_PAGE_READWRITE, &result,
                            &returned) == GXOS_VM_PUBLIC_STATUS_OK);
    REQUIRE(allocate_public(&environment, (void *)(uintptr_t)base, 0,
                            GXOS_VM_PUBLIC_MEM_RELEASE,
                            GXOS_VM_PUBLIC_PAGE_READWRITE, &result,
                            &returned) == GXOS_VM_PUBLIC_STATUS_INVALID_ARGUMENT);
    {
        int success;
        REQUIRE(gxos_vm_public_virtual_free(
                    &environment.context, (void *)(uintptr_t)base, 0,
                    GXOS_VM_PUBLIC_MEM_RELEASE, &result, &success) ==
                GXOS_VM_PUBLIC_STATUS_OK && success);
    }
    REQUIRE(environment.arena.reservation_count == 0);
    REQUIRE(environment.arena.commitment_count == 0);
    REQUIRE(gxos_vm_arena_available(&environment.arena) == available_before);
    REQUIRE(environment.memory.live_count == live_before +
            environment.paging.owned_table_page_count - table_pages_before);
    REQUIRE(gxos_vm_paging_query(&environment.paging, base, 0) ==
            GXOS_VM_PAGING_STATUS_INVALID_ARGUMENT);
    {
        GXOS_VM_MAPPING mapping;
        REQUIRE(gxos_vm_paging_query(&environment.paging, base, &mapping) ==
                GXOS_VM_PAGING_STATUS_NOT_PRESENT);
    }
    REQUIRE(gxos_vm_public_virtual_free(
                &environment.context, (void *)(uintptr_t)base, 0,
                GXOS_VM_PUBLIC_MEM_RELEASE, &result, &(int){0}) ==
            GXOS_VM_PUBLIC_STATUS_INVALID_ARGUMENT);
    REQUIRE(allocate_public(&environment, 0, 0x1000,
                            GXOS_VM_PUBLIC_MEM_RESERVE,
                            GXOS_VM_PUBLIC_PAGE_READWRITE, &result,
                            &returned) == GXOS_VM_PUBLIC_STATUS_OK);
    base = (uint64_t)(uintptr_t)returned;
    REQUIRE(gxos_vm_public_virtual_free(
                &environment.context, (void *)(uintptr_t)(base + 0x1000), 0,
                GXOS_VM_PUBLIC_MEM_RELEASE, &result, &(int){0}) ==
            GXOS_VM_PUBLIC_STATUS_INVALID_ARGUMENT);
    REQUIRE(gxos_vm_public_virtual_free(
                &environment.context, (void *)(uintptr_t)base, 1,
                GXOS_VM_PUBLIC_MEM_RELEASE, &result, &(int){0}) ==
            GXOS_VM_PUBLIC_STATUS_INVALID_ARGUMENT);
    REQUIRE(gxos_vm_public_virtual_free(
                &environment.context, (void *)(uintptr_t)base, 0,
                GXOS_VM_PUBLIC_MEM_DECOMMIT, &result, &(int){0}) ==
            GXOS_VM_PUBLIC_STATUS_UNSUPPORTED);
    {
        int success = 0;
        REQUIRE(gxos_vm_public_virtual_free(
                    &environment.context, (void *)(uintptr_t)base, 0,
                    GXOS_VM_PUBLIC_MEM_RELEASE, &result, &success) ==
                GXOS_VM_PUBLIC_STATUS_OK && success);
    }
    REQUIRE(environment.arena.reservation_count == 0);
    env_cleanup(&environment);
}

static void test_reserve_commit_atomic_success(void)
{
    TEST_ENV environment;
    GXOS_VM_PUBLIC_RESULT result;
    void *returned;
    int success = 0;
    if (!env_init(&environment)) {
        REQUIRE(0);
        return;
    }
    REQUIRE(allocate_public(&environment, 0, 0x2000,
                            GXOS_VM_PUBLIC_MEM_RESERVE |
                                GXOS_VM_PUBLIC_MEM_COMMIT,
                            GXOS_VM_PUBLIC_PAGE_READWRITE, &result,
                            &returned) == GXOS_VM_PUBLIC_STATUS_OK);
    REQUIRE(returned != 0 && result.reserved && result.committed &&
            result.new_page_count == 2U && environment.arena.reservation_count ==
            1U && environment.arena.commitment_count == 2U);
    REQUIRE(gxos_vm_public_virtual_free(
                &environment.context, returned, 0,
                GXOS_VM_PUBLIC_MEM_RELEASE, &result, &success) ==
            GXOS_VM_PUBLIC_STATUS_OK && success &&
            environment.arena.reservation_count == 0);
    env_cleanup(&environment);
}

static void test_global_memory_status_ex_integration(void)
{
    ACCOUNTING_ENV state;
    GXOS_MEMORY_STATUS_EX before;
    GXOS_MEMORY_STATUS_EX reserve_only;
    GXOS_MEMORY_STATUS_EX committed;
    GXOS_MEMORY_STATUS_EX released;
    GXOS_MEMORY_STATUS_EX_REPORT report;
    GXOS_VM_PUBLIC_RESULT result;
    void *returned;
    int success = 0;
    if (!accounting_env_init(&state)) {
        REQUIRE(0);
        return;
    }
    REQUIRE(accounting_query(&state, &before, &report));
    REQUIRE(report.status == GXOS_MEMORY_STATUS_EX_STATUS_OK);
    REQUIRE(allocate_public(&state.environment, 0, 0x3000,
                            GXOS_VM_PUBLIC_MEM_RESERVE,
                            GXOS_VM_PUBLIC_PAGE_READWRITE, &result,
                            &returned) == GXOS_VM_PUBLIC_STATUS_OK);
    state.status_context.accounting_generation++;
    REQUIRE(accounting_query(&state, &reserve_only, &report));
    REQUIRE(reserve_only.ullTotalVirtual == before.ullTotalVirtual &&
            reserve_only.ullAvailVirtual == before.ullAvailVirtual - 0x3000 &&
            reserve_only.ullAvailPhys == before.ullAvailPhys &&
            reserve_only.ullAvailPageFile == before.ullAvailPageFile);
    REQUIRE(allocate_public(&state.environment, returned, 0x2000,
                            GXOS_VM_PUBLIC_MEM_COMMIT,
                            GXOS_VM_PUBLIC_PAGE_READWRITE, &result,
                            &returned) == GXOS_VM_PUBLIC_STATUS_OK);
    state.status_context.accounting_generation++;
    REQUIRE(accounting_query(&state, &committed, &report));
    REQUIRE(committed.ullTotalVirtual == before.ullTotalVirtual &&
            committed.ullAvailVirtual == reserve_only.ullAvailVirtual &&
            committed.ullAvailPhys == reserve_only.ullAvailPhys - 0x2000 &&
            committed.ullAvailPageFile == reserve_only.ullAvailPageFile -
                0x2000 && committed.ullAvailPhys < before.ullAvailPhys);
    REQUIRE(gxos_vm_public_virtual_free(
                &state.environment.context, returned, 0,
                GXOS_VM_PUBLIC_MEM_RELEASE, &result, &success) ==
            GXOS_VM_PUBLIC_STATUS_OK && success);
    state.status_context.accounting_generation++;
    REQUIRE(accounting_query(&state, &released, &report));
    REQUIRE(released.ullTotalVirtual == before.ullTotalVirtual &&
            released.ullAvailVirtual == before.ullAvailVirtual &&
            released.ullAvailPhys == before.ullAvailPhys &&
            released.ullAvailPageFile == before.ullAvailPageFile &&
            released.ullAvailExtendedVirtual == before.ullAvailExtendedVirtual);
    env_cleanup(&state.environment);
}

int main(void)
{
    test_basic_validation();
    test_reserve_and_commit();
    test_transactional_failures();
    test_sparse_and_free();
    test_reserve_commit_atomic_success();
    test_global_memory_status_ex_integration();
    if (g_failures != 0) {
        fprintf(stderr, "virtual memory failure count=%u\n", g_failures);
        return 1;
    }
    puts("VIRTUAL_MEMORY_HOST_TESTS=PASSED");
    return 0;
}

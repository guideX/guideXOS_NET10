#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "memory_accounting.h"

static unsigned g_failures;

#define REQUIRE(condition) do { \
    if (!(condition)) { \
        fprintf(stderr, "memory accounting test failure: %s:%d: %s\n", \
                __FILE__, __LINE__, #condition); \
        g_failures++; \
    } \
} while (0)

static void set_descriptor(uint8_t *buffer, uint64_t stride, uint32_t index,
                           uint32_t type, uint64_t physical_start,
                           uint64_t pages)
{
    EFI_MEMORY_DESCRIPTOR *descriptor =
        (EFI_MEMORY_DESCRIPTOR *)(void *)(buffer + stride * index);
    memset(buffer + stride * index, 0, (size_t)stride);
    descriptor->Type = type;
    descriptor->PhysicalStart = physical_start;
    descriptor->NumberOfPages = pages;
}

static GXOS_UEFI_MEMORY_MAP map_from(void *buffer, uint64_t bytes,
                                     uint64_t stride, uint32_t count)
{
    GXOS_UEFI_MEMORY_MAP map;
    memset(&map, 0, sizeof(map));
    map.backing = (uint8_t *)buffer;
    map.backing_bytes = bytes;
    map.map_bytes = bytes;
    map.descriptor_size = stride;
    map.descriptor_count = count;
    map.generation = 1;
    map.valid = 1;
    return map;
}

static void test_map_parser_and_classification(void)
{
    uint8_t buffer[5 * 56];
    uint32_t count = 0;
    GXOS_UEFI_MEMORY_MAP map;
    GXOS_MEMORY_CLASSIFICATION classification;
    memset(buffer, 0, sizeof(buffer));
    set_descriptor(buffer, 56, 0, GXOS_EFI_CONVENTIONAL_MEMORY_TYPE,
                   0x100000, 4);
    set_descriptor(buffer, 56, 1, GXOS_EFI_LOADER_CODE_MEMORY_TYPE,
                   0x200000, 2);
    set_descriptor(buffer, 56, 2, GXOS_EFI_MEMORY_MAPPED_IO_MEMORY_TYPE,
                   0x300000, 10);
    set_descriptor(buffer, 56, 3, GXOS_EFI_RESERVED_MEMORY_TYPE,
                   0x400000, 5);
    set_descriptor(buffer, 56, 4, 99, 0x500000, 1);
    REQUIRE(gxos_uefi_memory_map_parse(buffer, sizeof(buffer), 56, &count));
    REQUIRE(count == 5);
    REQUIRE(!gxos_uefi_memory_map_parse(buffer, sizeof(buffer), 39, &count));
    REQUIRE(!gxos_uefi_memory_map_parse(buffer, 55, 56, &count));
    REQUIRE(gxos_uefi_memory_map_parse(0, 0, 40, &count) && count == 0);
    map = map_from(buffer, sizeof(buffer), 56, 5);
    REQUIRE(gxos_uefi_memory_map_descriptor(&map, 4)->Type == 99);
    REQUIRE(gxos_uefi_memory_map_classify(&map, &classification) ==
            GXOS_MEMORY_CLASSIFICATION_OK);
    REQUIRE(classification.total_ram_like_bytes == 6 * 4096ULL);
    REQUIRE(classification.conventional_bytes == 4 * 4096ULL);
    REQUIRE(classification.class_bytes[GXOS_MEMORY_CLASS_MMIO] == 10 * 4096ULL);
    REQUIRE(classification.class_bytes[GXOS_MEMORY_CLASS_RESERVED] == 5 * 4096ULL);
    REQUIRE(classification.class_bytes[GXOS_MEMORY_CLASS_UNKNOWN] == 4096ULL);
    REQUIRE(!gxos_memory_class_is_ram_like(GXOS_MEMORY_CLASS_MMIO));
    REQUIRE(gxos_memory_class_is_ram_like(GXOS_MEMORY_CLASS_ACPI_NVS));
}

static void test_map_overflow(void)
{
    EFI_MEMORY_DESCRIPTOR descriptor;
    GXOS_UEFI_MEMORY_MAP map;
    GXOS_MEMORY_CLASSIFICATION classification;
    memset(&descriptor, 0, sizeof(descriptor));
    descriptor.Type = GXOS_EFI_CONVENTIONAL_MEMORY_TYPE;
    descriptor.NumberOfPages = UINT64_MAX;
    map = map_from(&descriptor, sizeof(descriptor), sizeof(descriptor), 1);
    REQUIRE(gxos_uefi_memory_map_classify(&map, &classification) ==
            GXOS_MEMORY_CLASSIFICATION_OVERFLOW);
    descriptor.NumberOfPages = 1;
    descriptor.PhysicalStart = UINT64_MAX;
    REQUIRE(gxos_uefi_memory_map_classify(&map, &classification) ==
            GXOS_MEMORY_CLASSIFICATION_OVERFLOW);
}

static uint32_t g_fake_map_call;
static GXOS_EFI_STATUS GXOS_MEMORY_EFIAPI fake_get_map(
    GXOS_EFI_UINTN *size, void *buffer, GXOS_EFI_UINTN *key,
    GXOS_EFI_UINTN *stride, uint32_t *version)
{
    g_fake_map_call++;
    *stride = 40;
    *version = 1;
    if (buffer == 0) {
        *size = 80;
        return GXOS_EFI_BUFFER_TOO_SMALL;
    }
    if (g_fake_map_call == 2) {
        *size = 600;
        return GXOS_EFI_BUFFER_TOO_SMALL;
    }
    set_descriptor((uint8_t *)buffer, 40, 0,
                   GXOS_EFI_CONVENTIONAL_MEMORY_TYPE, 0x1000, 1);
    set_descriptor((uint8_t *)buffer, 40, 1,
                   GXOS_EFI_ACPI_RECLAIM_MEMORY_TYPE, 0x2000, 1);
    *size = 80;
    *key = 0xABC;
    return GXOS_EFI_SUCCESS;
}

static GXOS_EFI_STATUS GXOS_MEMORY_EFIAPI fake_allocate_pool(
    uint32_t pool_type, GXOS_EFI_UINTN size, void **buffer)
{
    (void)pool_type;
    *buffer = malloc((size_t)size);
    return *buffer == 0 ? 1 : GXOS_EFI_SUCCESS;
}

static GXOS_EFI_STATUS GXOS_MEMORY_EFIAPI fake_free_pool(void *buffer)
{
    free(buffer);
    return GXOS_EFI_SUCCESS;
}

static void test_map_acquisition(void)
{
    GXOS_UEFI_MEMORY_MAP map;
    g_fake_map_call = 0;
    REQUIRE(gxos_uefi_memory_map_acquire(&map, fake_get_map,
                                         fake_allocate_pool,
                                         fake_free_pool) ==
            GXOS_MEMORY_MAP_STATUS_OK);
    REQUIRE(map.valid && map.map_key == 0xABC && map.descriptor_count == 2);
    REQUIRE(map.descriptor_size == 40 && map.descriptor_version == 1);
    REQUIRE(g_fake_map_call == 3);
    fake_free_pool(map.backing);
}

static GXOS_PHYSICAL_ALLOCATION allocation(uint64_t base, uint64_t bytes,
                                           uint64_t generation)
{
    GXOS_PHYSICAL_ALLOCATION value;
    memset(&value, 0, sizeof(value));
    value.base = base;
    value.bytes = bytes;
    value.pages = bytes / GXOS_MEMORY_PAGE_SIZE;
    value.allocation_class = GXOS_MEMORY_ALLOCATION_OTHER;
    value.owner = GXOS_MEMORY_OWNER_OTHER;
    value.physical_impact_bytes = bytes;
    value.commit_impact_bytes = bytes;
    value.virtual_reservation_impact_bytes = bytes;
    value.generation = generation;
    return value;
}

static void test_ledger(void)
{
    GXOS_PHYSICAL_LEDGER ledger;
    GXOS_PHYSICAL_ALLOCATION value;
    uint32_t slot;
    uint32_t index;
    gxos_physical_ledger_init(&ledger, 1);
    value = allocation(0x10000, 4096, 2);
    REQUIRE(gxos_physical_ledger_insert(&ledger, &value, &slot) ==
            GXOS_LEDGER_STATUS_OK);
    REQUIRE(ledger.live_count == 1 && ledger.physical_bytes == 4096);
    REQUIRE(gxos_physical_ledger_insert(&ledger, &value, &index) ==
            GXOS_LEDGER_STATUS_OVERLAP);
    value = allocation(0x10800, 4096, 2);
    REQUIRE(gxos_physical_ledger_insert(&ledger, &value, &index) ==
            GXOS_LEDGER_STATUS_OVERLAP);
    value = allocation(0x20000, 0, 2);
    REQUIRE(gxos_physical_ledger_insert(&ledger, &value, &index) ==
            GXOS_LEDGER_STATUS_ZERO_LENGTH);
    value = allocation(0x30000, 4096, 2);
    value.pages = UINT64_MAX;
    REQUIRE(gxos_physical_ledger_insert(&ledger, &value, &index) ==
            GXOS_LEDGER_STATUS_OVERFLOW);
    REQUIRE(gxos_physical_ledger_validate(&ledger));
    for (index = 1; index != GXOS_PHYSICAL_LEDGER_CAPACITY; ++index) {
        value = allocation(0x10000ULL + index * 0x2000ULL, 4096, 2);
        REQUIRE(gxos_physical_ledger_insert(&ledger, &value, &slot) ==
                GXOS_LEDGER_STATUS_OK);
    }
    value = allocation(0x10000ULL +
                           ((uint64_t)GXOS_PHYSICAL_LEDGER_CAPACITY + 1U) *
                               0x2000ULL,
                       4096, 2);
    REQUIRE(gxos_physical_ledger_insert(&ledger, &value, &slot) ==
            GXOS_LEDGER_STATUS_CAPACITY);
    REQUIRE(ledger.exhausted && gxos_physical_ledger_validate(&ledger));
    REQUIRE(gxos_physical_ledger_find(&ledger, 0x10000, 4096, &slot));
    REQUIRE(gxos_physical_ledger_remove(&ledger, slot) == GXOS_LEDGER_STATUS_OK);
    REQUIRE(!gxos_physical_ledger_find(&ledger, 0x10000, 4096, &slot));
}

static void test_virtual_arena(void)
{
    GXOS_VM_ARENA arena;
    uint32_t slot;
    gxos_vm_arena_init(&arena, 0x10000, 0x10000, 1);
    REQUIRE(arena.valid && gxos_vm_arena_available(&arena) == 0x10000);
    REQUIRE(gxos_vm_arena_reserve(&arena, 0x10000, 0x4000, 1, 1, &slot) ==
            GXOS_VM_STATUS_OK);
    REQUIRE(gxos_vm_arena_commit(&arena, 0x10000, 0x1000, 1) ==
            GXOS_VM_STATUS_OK);
    REQUIRE(gxos_vm_arena_commit(&arena, 0x10800, 0x1000, 1) ==
            GXOS_VM_STATUS_COMMIT_OVERLAP);
    REQUIRE(gxos_vm_arena_commit(&arena, 0x14000, 0x1000, 1) ==
            GXOS_VM_STATUS_COMMIT_OUTSIDE_RESERVATION);
    REQUIRE(gxos_vm_arena_reserve(&arena, 0x12000, 0x1000, 1, 1, &slot) ==
            GXOS_VM_STATUS_OVERLAP);
    REQUIRE(gxos_vm_arena_reserve(&arena, 0x1FFFF, 2, 1, 1, &slot) ==
            GXOS_VM_STATUS_OUTSIDE_ARENA);
    REQUIRE(gxos_vm_arena_reserve(&arena, UINT64_MAX, 2, 1, 1, &slot) ==
            GXOS_VM_STATUS_OVERFLOW);
    REQUIRE(arena.total_reserved_bytes == 0x4000 &&
            arena.total_committed_bytes == 0x1000 &&
            gxos_vm_arena_available(&arena) == 0xC000);
    REQUIRE(gxos_vm_arena_release(&arena, 0) ==
            GXOS_VM_STATUS_COMMITTED_RESERVATION);
    REQUIRE(gxos_vm_arena_validate(&arena));

    gxos_vm_arena_init(&arena, 0x10000, 0x10000, 1);
    REQUIRE(gxos_vm_arena_reserve(&arena, 0x10000, 0x10000, 1, 1, &slot) ==
            GXOS_VM_STATUS_OK);
    REQUIRE(gxos_vm_arena_available(&arena) == 0);
    REQUIRE(gxos_vm_arena_validate(&arena));
}

static void test_commit_and_snapshot(void)
{
    GXOS_COMMIT_MODEL commit;
    GXOS_MEMORY_CLASSIFICATION classification;
    GXOS_PHYSICAL_LEDGER ledger;
    GXOS_PHYSICAL_SNAPSHOT physical;
    GXOS_MEMORY_SNAPSHOT snapshot;
    GXOS_VM_ARENA arena;
    uint32_t slot;
    memset(&classification, 0, sizeof(classification));
    classification.valid = 1;
    classification.total_ram_like_bytes = 1000;
    classification.conventional_bytes = 1000;
    gxos_physical_ledger_init(&ledger, 1);
    REQUIRE(gxos_physical_snapshot_create(&physical, &classification, &ledger, 2) ==
            GXOS_SNAPSHOT_STATUS_OK);
    gxos_vm_arena_init(&arena, 0x10000, 10000, 2);
    REQUIRE(gxos_vm_arena_reserve(&arena, 0x10000, 1000, 1, 2, &slot) ==
            GXOS_VM_STATUS_OK);
    REQUIRE(gxos_vm_arena_commit(&arena, 0x10000, 500, 2) == GXOS_VM_STATUS_OK);
    REQUIRE(gxos_commit_model_create(&commit, 500, 0, 3) == GXOS_COMMIT_STATUS_OK);
    REQUIRE(commit.available_commit == 500);
    REQUIRE(gxos_commit_model_create(&commit, 500, 500, 3) == GXOS_COMMIT_STATUS_OK);
    REQUIRE(commit.available_commit == 0);
    REQUIRE(gxos_commit_model_create(&commit, 500, 501, 3) ==
            GXOS_COMMIT_STATUS_OVERCOMMIT);
    REQUIRE(gxos_commit_model_create(&commit, UINT64_MAX, 0, 3) ==
            GXOS_COMMIT_STATUS_OK);
    REQUIRE(gxos_commit_model_create_no_pagefile(&commit, 1000, 500, 500, 3) ==
            GXOS_COMMIT_STATUS_OK && commit.no_pagefile);
    REQUIRE(commit.commit_limit == 1000 && commit.available_commit == 500);
    REQUIRE(gxos_commit_model_create_no_pagefile(&commit, UINT64_MAX,
                                                 UINT64_MAX, 1, 3) ==
            GXOS_COMMIT_STATUS_OVERFLOW);
    REQUIRE(gxos_memory_snapshot_create(&snapshot, &physical, &arena, &commit, 4) ==
            GXOS_SNAPSHOT_STATUS_OK);
    REQUIRE(snapshot.memory_load_percent == 0);
    REQUIRE(snapshot.process_virtual_available_bytes == 9000);
    {
        GXOS_PHYSICAL_SNAPSHOT bad_physical = physical;
        GXOS_COMMIT_MODEL bad_commit = commit;
        GXOS_VM_ARENA bad_arena = arena;
        bad_physical.available_physical_bytes =
            bad_physical.total_ram_like_bytes + 1;
        REQUIRE(gxos_memory_snapshot_create(&snapshot, &bad_physical, &arena,
                                             &commit, 4) ==
                GXOS_SNAPSHOT_STATUS_INVALID_PHYSICAL);
        bad_commit.committed_bytes = bad_commit.commit_limit + 1;
        REQUIRE(gxos_memory_snapshot_create(&snapshot, &physical, &arena,
                                             &bad_commit, 4) ==
                GXOS_SNAPSHOT_STATUS_INVALID_COMMIT);
        bad_arena.total_reserved_bytes = bad_arena.length + 1;
        REQUIRE(gxos_memory_snapshot_create(&snapshot, &physical, &bad_arena,
                                             &commit, 4) ==
                GXOS_SNAPSHOT_STATUS_INVALID_VIRTUAL);
    }
    classification.total_ram_like_bytes = 100;
    classification.conventional_bytes = 0;
    REQUIRE(gxos_physical_snapshot_create(&physical, &classification, &ledger, 2) ==
            GXOS_SNAPSHOT_STATUS_OK);
    REQUIRE(gxos_memory_snapshot_create(&snapshot, &physical, &arena, &commit, 4) ==
            GXOS_SNAPSHOT_STATUS_OK && snapshot.memory_load_percent == 100);
    classification.conventional_bytes = 67;
    REQUIRE(gxos_physical_snapshot_create(&physical, &classification, &ledger, 2) ==
            GXOS_SNAPSHOT_STATUS_OK);
    REQUIRE(gxos_memory_snapshot_create(&snapshot, &physical, &arena, &commit, 4) ==
            GXOS_SNAPSHOT_STATUS_OK && snapshot.memory_load_percent == 33);
    classification.conventional_bytes = 0;
    classification.total_ram_like_bytes = 1000;
    REQUIRE(gxos_physical_snapshot_create(&physical, &classification, &ledger, 2) ==
            GXOS_SNAPSHOT_STATUS_OK);
    classification.total_ram_like_bytes = 0;
    REQUIRE(gxos_physical_snapshot_create(&physical, &classification, &ledger, 2) ==
            GXOS_SNAPSHOT_STATUS_INVALID_PHYSICAL);
}

int main(void)
{
    test_map_parser_and_classification();
    test_map_overflow();
    test_map_acquisition();
    test_ledger();
    test_virtual_arena();
    test_commit_and_snapshot();
    if (g_failures != 0) return 1;
    puts("MEMORY_ACCOUNTING_HOST_TESTS=PASSED");
    return 0;
}

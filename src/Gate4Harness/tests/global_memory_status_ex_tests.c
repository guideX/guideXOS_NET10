#include <stdio.h>
#include <stdint.h>
#include <string.h>

#include "global_memory_status_ex.h"

static unsigned g_failures;

#define REQUIRE(condition) do { \
    if (!(condition)) { \
        fprintf(stderr, "GlobalMemoryStatusEx test failure: %s:%d: %s\n", \
                __FILE__, __LINE__, #condition); \
        g_failures++; \
    } \
} while (0)

typedef struct {
    uint8_t before[8];
    GXOS_MEMORY_STATUS_EX status;
    uint8_t after[8];
} GUARDED_STATUS;

typedef struct {
    GXOS_MEMORY_CLASSIFICATION classification;
    GXOS_PHYSICAL_LEDGER ledger;
    GXOS_VM_ARENA arena;
    GXOS_MEMORY_SNAPSHOT startup;
    GXOS_MEMORY_STATUS_EX_MEMORY_REGION region;
    GXOS_MEMORY_STATUS_EX_CONTEXT context;
} TEST_STATE;

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

static void initialize_state(TEST_STATE *state, GUARDED_STATUS *buffer)
{
    GXOS_PHYSICAL_SNAPSHOT physical;
    GXOS_COMMIT_MODEL commit;
    memset(state, 0, sizeof(*state));
    state->classification.valid = 1;
    state->classification.total_ram_like_bytes = 1000;
    state->classification.conventional_bytes = 1000;
    gxos_physical_ledger_init(&state->ledger, 1);
    gxos_vm_arena_init(&state->arena, 0x10000, 10000, 1);
    REQUIRE(gxos_physical_snapshot_create(&physical, &state->classification,
                                           &state->ledger, 2) ==
            GXOS_SNAPSHOT_STATUS_OK);
    REQUIRE(gxos_commit_model_create_no_pagefile(
                &commit, physical.total_ram_like_bytes,
                physical.available_physical_bytes,
                state->arena.total_committed_bytes, 2) ==
            GXOS_COMMIT_STATUS_OK);
    REQUIRE(gxos_memory_snapshot_create(&state->startup, &physical,
                                        &state->arena, &commit, 2) ==
            GXOS_SNAPSHOT_STATUS_OK);
    state->region.base = (uintptr_t)&buffer->status;
    state->region.end = state->region.base + sizeof(buffer->status);
    state->region.readable = 1;
    state->region.writable = 1;
    state->context.classification = &state->classification;
    state->context.startup_snapshot = &state->startup;
    state->context.ledger = &state->ledger;
    state->context.virtual_arena = &state->arena;
    state->context.regions = &state->region;
    state->context.region_count = 1;
    state->context.accounting_generation = 2;
    state->context.accounting_generation_source = 0;
}

static void initialize_buffer(GUARDED_STATUS *buffer, uint32_t length)
{
    memset(buffer, 0xCC, sizeof(*buffer));
    buffer->status.dwLength = length;
}

static int guards_are_intact(const GUARDED_STATUS *buffer)
{
    uint32_t index;
    for (index = 0; index != sizeof(buffer->before); ++index) {
        if (buffer->before[index] != 0xCC || buffer->after[index] != 0xCC) {
            return 0;
        }
    }
    return 1;
}

static void test_valid_mapping_and_guards(void)
{
    GUARDED_STATUS buffer;
    TEST_STATE state;
    GXOS_MEMORY_STATUS_EX_REPORT report;
    initialize_buffer(&buffer, 0x40);
    initialize_state(&state, &buffer);
    REQUIRE(gxos_global_memory_status_ex_checked(&buffer.status,
                                                 &state.context, &report));
    REQUIRE(report.status == GXOS_MEMORY_STATUS_EX_STATUS_OK);
    REQUIRE(buffer.status.dwLength == 0x40);
    REQUIRE(buffer.status.dwMemoryLoad == 0);
    REQUIRE(buffer.status.ullTotalPhys == 1000);
    REQUIRE(buffer.status.ullAvailPhys == 1000);
    REQUIRE(buffer.status.ullTotalPageFile == 1000);
    REQUIRE(buffer.status.ullAvailPageFile == 1000);
    REQUIRE(buffer.status.ullTotalVirtual == 10000);
    REQUIRE(buffer.status.ullAvailVirtual == 10000);
    REQUIRE(buffer.status.ullAvailExtendedVirtual == 0);
    REQUIRE(report.output_written && report.output_range_valid);
    REQUIRE(guards_are_intact(&buffer));
}

static void test_pointer_and_length_failures(void)
{
    GUARDED_STATUS buffer;
    TEST_STATE state;
    GXOS_MEMORY_STATUS_EX_REPORT report;
    uint8_t before[sizeof(buffer.status)];
    GXOS_MEMORY_STATUS_EX_MEMORY_REGION short_region;

    initialize_buffer(&buffer, 0x40);
    initialize_state(&state, &buffer);
    memcpy(before, &buffer.status, sizeof(before));
    REQUIRE(!gxos_global_memory_status_ex_checked(0, &state.context, &report));
    REQUIRE(report.status == GXOS_MEMORY_STATUS_EX_STATUS_NULL_BUFFER);

    REQUIRE(!gxos_global_memory_status_ex_checked(
                (GXOS_MEMORY_STATUS_EX *)(uintptr_t)0x0000800000000000ULL,
                &state.context, &report));
    REQUIRE(report.status == GXOS_MEMORY_STATUS_EX_STATUS_NONCANONICAL_BUFFER);

    short_region = state.region;
    short_region.end = short_region.base + 0x3F;
    state.context.regions = &short_region;
    initialize_buffer(&buffer, 0x40);
    memcpy(before, &buffer.status, sizeof(before));
    REQUIRE(!gxos_global_memory_status_ex_checked(&buffer.status,
                                                  &state.context, &report));
    REQUIRE(report.status == GXOS_MEMORY_STATUS_EX_STATUS_UNWRITABLE_BUFFER);
    REQUIRE(memcmp(before, &buffer.status, sizeof(before)) == 0);
    REQUIRE(guards_are_intact(&buffer));

    state.context.regions = &state.region;
    initialize_buffer(&buffer, 0);
    memcpy(before, &buffer.status, sizeof(before));
    REQUIRE(!gxos_global_memory_status_ex_checked(&buffer.status,
                                                  &state.context, &report));
    REQUIRE(report.status == GXOS_MEMORY_STATUS_EX_STATUS_INVALID_LENGTH);
    REQUIRE(memcmp(before, &buffer.status, sizeof(before)) == 0);

    initialize_buffer(&buffer, 0x3F);
    memcpy(before, &buffer.status, sizeof(before));
    REQUIRE(!gxos_global_memory_status_ex_checked(&buffer.status,
                                                  &state.context, &report));
    REQUIRE(report.status == GXOS_MEMORY_STATUS_EX_STATUS_INVALID_LENGTH);
    REQUIRE(memcmp(before, &buffer.status, sizeof(before)) == 0);

    initialize_buffer(&buffer, 0x41);
    memcpy(before, &buffer.status, sizeof(before));
    REQUIRE(!gxos_global_memory_status_ex_checked(&buffer.status,
                                                  &state.context, &report));
    REQUIRE(report.status == GXOS_MEMORY_STATUS_EX_STATUS_INVALID_LENGTH);
    REQUIRE(memcmp(before, &buffer.status, sizeof(before)) == 0);

    REQUIRE(!gxos_global_memory_status_ex_checked(
                (GXOS_MEMORY_STATUS_EX *)(uintptr_t)(UINTPTR_MAX - 0x1FULL),
                &state.context, &report));
    REQUIRE(report.status == GXOS_MEMORY_STATUS_EX_STATUS_RANGE_OVERFLOW);
}

static void test_invalid_accounting_views(void)
{
    GUARDED_STATUS buffer;
    TEST_STATE state;
    GXOS_MEMORY_STATUS_EX_REPORT report;
    uint8_t before[sizeof(buffer.status)];
    GXOS_MEMORY_SNAPSHOT startup_copy;
    GXOS_VM_ARENA arena_copy;

    initialize_buffer(&buffer, 0x40);
    initialize_state(&state, &buffer);
    memcpy(before, &buffer.status, sizeof(before));

    state.startup.valid = 0;
    REQUIRE(!gxos_global_memory_status_ex_checked(&buffer.status,
                                                  &state.context, &report));
    REQUIRE(report.status == GXOS_MEMORY_STATUS_EX_STATUS_INVALID_ACCOUNTING_VIEW);
    REQUIRE(memcmp(before, &buffer.status, sizeof(before)) == 0);
    state.startup.valid = 1;

    startup_copy = state.startup;
    startup_copy.available_commit_bytes = startup_copy.commit_limit_bytes + 1;
    state.context.startup_snapshot = &startup_copy;
    REQUIRE(!gxos_global_memory_status_ex_checked(&buffer.status,
                                                  &state.context, &report));
    REQUIRE(report.status == GXOS_MEMORY_STATUS_EX_STATUS_INVALID_ACCOUNTING_VIEW);
    state.context.startup_snapshot = &state.startup;

    state.classification.conventional_bytes = 1001;
    REQUIRE(!gxos_global_memory_status_ex_checked(&buffer.status,
                                                  &state.context, &report));
    REQUIRE(report.status == GXOS_MEMORY_STATUS_EX_STATUS_INVALID_PHYSICAL);
    state.classification.conventional_bytes = 1000;

    arena_copy = state.arena;
    arena_copy.total_reserved_bytes = arena_copy.length + 1;
    state.context.virtual_arena = &arena_copy;
    REQUIRE(!gxos_global_memory_status_ex_checked(&buffer.status,
                                                  &state.context, &report));
    REQUIRE(report.status == GXOS_MEMORY_STATUS_EX_STATUS_INVALID_VIRTUAL);
    state.context.virtual_arena = &state.arena;

    startup_copy = state.startup;
    startup_copy.memory_load_percent = 101;
    state.context.startup_snapshot = &startup_copy;
    REQUIRE(!gxos_global_memory_status_ex_checked(&buffer.status,
                                                  &state.context, &report));
    REQUIRE(report.status == GXOS_MEMORY_STATUS_EX_STATUS_INVALID_ACCOUNTING_VIEW);
}

static void test_current_view_changes_and_generation(void)
{
    GUARDED_STATUS buffer;
    TEST_STATE state;
    GXOS_MEMORY_STATUS_EX_REPORT report;
    GXOS_PHYSICAL_ALLOCATION value;
    uint32_t ledger_slot;
    uint32_t arena_slot;

    initialize_buffer(&buffer, 0x40);
    initialize_state(&state, &buffer);
    REQUIRE(gxos_global_memory_status_ex_checked(&buffer.status,
                                                 &state.context, &report));
    REQUIRE(buffer.status.ullAvailPhys == 1000 &&
            buffer.status.ullAvailPageFile == 1000 &&
            buffer.status.ullAvailVirtual == 10000);

    value = allocation(0x20000, 100, 1);
    REQUIRE(gxos_physical_ledger_insert(&state.ledger, &value, &ledger_slot) ==
            GXOS_LEDGER_STATUS_OK);
    REQUIRE(gxos_vm_arena_reserve(&state.arena, 0x10000, 1000, 1, 1,
                                  &arena_slot) == GXOS_VM_STATUS_OK);
    REQUIRE(gxos_vm_arena_commit(&state.arena, 0x10000, 1000, 1) ==
            GXOS_VM_STATUS_OK);
    state.context.accounting_generation = 3;
    REQUIRE(gxos_global_memory_status_ex_checked(&buffer.status,
                                                 &state.context, &report));
    REQUIRE(report.view.generation == 3);
    REQUIRE(buffer.status.dwMemoryLoad == 10);
    REQUIRE(buffer.status.ullAvailPhys == 900);
    REQUIRE(buffer.status.ullTotalPageFile == 1000);
    REQUIRE(buffer.status.ullAvailPageFile == 0);
    REQUIRE(buffer.status.ullAvailVirtual == 9000);
    REQUIRE(buffer.status.ullAvailExtendedVirtual == 0);
}

static void test_generation_retry_boundary(void)
{
    GUARDED_STATUS buffer;
    TEST_STATE state;
    GXOS_MEMORY_STATUS_EX_REPORT report;
    volatile uint64_t generation = 2;
    uint8_t before[sizeof(buffer.status)];

    initialize_buffer(&buffer, 0x40);
    initialize_state(&state, &buffer);
    state.context.accounting_generation_source = &generation;
    generation = 3;
    memcpy(before, &buffer.status, sizeof(before));
    REQUIRE(!gxos_global_memory_status_ex_checked(&buffer.status,
                                                  &state.context, &report));
    REQUIRE(report.status == GXOS_MEMORY_STATUS_EX_STATUS_ACCOUNTING_CHANGED);
    REQUIRE(memcmp(before, &buffer.status, sizeof(before)) == 0);
    generation = 2;
    REQUIRE(gxos_global_memory_status_ex_checked(&buffer.status,
                                                 &state.context, &report));
}

int main(void)
{
    test_valid_mapping_and_guards();
    test_pointer_and_length_failures();
    test_invalid_accounting_views();
    test_current_view_changes_and_generation();
    test_generation_retry_boundary();
    if (g_failures != 0) return 1;
    puts("GLOBAL_MEMORY_STATUS_EX_HOST_TESTS=PASSED");
    return 0;
}

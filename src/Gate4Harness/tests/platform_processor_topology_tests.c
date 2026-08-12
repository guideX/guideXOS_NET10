#include <stddef.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>

#include "../platform_processor_topology.h"

typedef struct {
    uint8_t before[8];
    uint8_t data[0x80];
    uint8_t after[8];
} GUARDED_OUTPUT;

static GXOS_PROCESSOR_TOPOLOGY_SNAPSHOT g_snapshot;
static GXOS_MEMORY_STATUS_EX_MEMORY_REGION g_regions[2];
static GXOS_MEMORY_STATUS_EX_CONTEXT g_memory;
static GXOS_PHYSICAL_LEDGER g_ledger;
static uint32_t g_returned_length;
static GUARDED_OUTPUT g_output;
static unsigned g_failures;

#define CHECK(condition) do { \
    if (!(condition)) { \
        fprintf(stderr, "PROCESSOR_TOPOLOGY_TEST_FAILURE=%s:%d:%s\n", \
                __FILE__, __LINE__, #condition); \
        ++g_failures; \
    } \
} while (0)

static void configure_memory(void)
{
    memset(&g_memory, 0, sizeof(g_memory));
    memset(g_regions, 0, sizeof(g_regions));
    gxos_physical_ledger_init(&g_ledger, 1);
    g_regions[0].base = (uintptr_t)&g_returned_length;
    g_regions[0].end = g_regions[0].base + sizeof(g_returned_length);
    g_regions[0].readable = 1;
    g_regions[0].writable = 1;
    g_regions[1].base = (uintptr_t)&g_output;
    g_regions[1].end = g_regions[1].base + sizeof(g_output);
    g_regions[1].readable = 1;
    g_regions[1].writable = 1;
    g_memory.regions = g_regions;
    g_memory.region_count = 2;
    g_memory.ledger = &g_ledger;
}

static void initialize_case(uint32_t length)
{
    CHECK(gxos_processor_topology_make_single_cpu(&g_snapshot, 1) ==
          GXOS_PROCESSOR_TOPOLOGY_STATUS_OK);
    memset(&g_output, 0xA5, sizeof(g_output));
    g_returned_length = length;
    configure_memory();
}

static GXOS_PROCESSOR_TOPOLOGY_STATUS call_api(
    GXOS_LOGICAL_PROCESSOR_INFORMATION *buffer,
    GXOS_PROCESSOR_TOPOLOGY_REPORT *report)
{
    return gxos_get_logical_processor_information_checked(
        buffer, &g_returned_length, &g_snapshot, &g_memory, report);
}

static int guards_are_intact(void)
{
    uint32_t index;
    for (index = 0; index != sizeof(g_output.before); ++index) {
        if (g_output.before[index] != 0xA5 ||
            g_output.after[index] != 0xA5) return 0;
    }
    return 1;
}

static void test_snapshot_model(void)
{
    GXOS_PROCESSOR_TOPOLOGY_STATUS status;
    GXOS_PROCESSOR_TOPOLOGY_SNAPSHOT mutant;
    uint32_t count = 0;

    CHECK(gxos_processor_topology_make_single_cpu(&g_snapshot, 7) ==
          GXOS_PROCESSOR_TOPOLOGY_STATUS_OK);
    CHECK(g_snapshot.valid == 1 && g_snapshot.generation == 7);
    CHECK(g_snapshot.logical_processor_count == 1);
    CHECK(g_snapshot.logical_processor_numbers[0] == 0);
    CHECK(g_snapshot.active_processor_mask == 1);
    CHECK(g_snapshot.core_count == 1 && g_snapshot.cores[0].processor_mask == 1);
    CHECK(g_snapshot.cores[0].flags == 0);
    CHECK(g_snapshot.numa_node_count == 1 &&
          g_snapshot.numa_nodes[0].processor_mask == 1 &&
          g_snapshot.numa_nodes[0].node_number == 0);
    CHECK(g_snapshot.package_count == 1 &&
          g_snapshot.packages[0].processor_mask == 1);
    CHECK(g_snapshot.cache_count == 0);
    CHECK(gxos_processor_topology_record_count(&g_snapshot, &count) ==
          GXOS_PROCESSOR_TOPOLOGY_STATUS_OK && count == 3);

    mutant = g_snapshot;
    mutant.valid = 0;
    CHECK(gxos_processor_topology_validate(&mutant) ==
          GXOS_PROCESSOR_TOPOLOGY_STATUS_INVALID_SNAPSHOT);
    mutant = g_snapshot;
    mutant.generation = 0;
    CHECK(gxos_processor_topology_validate(&mutant) ==
          GXOS_PROCESSOR_TOPOLOGY_STATUS_INVALID_GENERATION);
    mutant = g_snapshot;
    mutant.logical_processor_count = 0;
    CHECK(gxos_processor_topology_validate(&mutant) ==
          GXOS_PROCESSOR_TOPOLOGY_STATUS_INVALID_LOGICAL_PROCESSOR_COUNT);
    mutant = g_snapshot;
    mutant.active_processor_mask = 3;
    CHECK(gxos_processor_topology_validate(&mutant) ==
          GXOS_PROCESSOR_TOPOLOGY_STATUS_INVALID_ACTIVE_PROCESSOR_MASK);
    mutant = g_snapshot;
    mutant.logical_processor_count = 2;
    mutant.active_processor_mask = 3;
    mutant.logical_processor_numbers[1] = 0;
    CHECK(gxos_processor_topology_validate(&mutant) ==
          GXOS_PROCESSOR_TOPOLOGY_STATUS_DUPLICATE_LOGICAL_PROCESSOR);
    mutant = g_snapshot;
    mutant.logical_processor_numbers[0] = 64;
    CHECK(gxos_processor_topology_validate(&mutant) ==
          GXOS_PROCESSOR_TOPOLOGY_STATUS_OUT_OF_RANGE_LOGICAL_PROCESSOR);
    mutant = g_snapshot;
    mutant.core_count = GXOS_PROCESSOR_TOPOLOGY_MAX_RELATIONSHIPS + 1U;
    CHECK(gxos_processor_topology_validate(&mutant) ==
          GXOS_PROCESSOR_TOPOLOGY_STATUS_RELATIONSHIP_CAPACITY);
    mutant = g_snapshot;
    mutant.cores[0].processor_mask = 0;
    CHECK(gxos_processor_topology_validate(&mutant) ==
          GXOS_PROCESSOR_TOPOLOGY_STATUS_INVALID_CORE_RELATIONSHIPS);
    mutant = g_snapshot;
    mutant.numa_nodes[0].node_number = 0;
    mutant.numa_nodes[0].processor_mask = 2;
    CHECK(gxos_processor_topology_validate(&mutant) ==
          GXOS_PROCESSOR_TOPOLOGY_STATUS_INVALID_NUMA_RELATIONSHIPS);
    mutant = g_snapshot;
    mutant.packages[0].processor_mask = 2;
    CHECK(gxos_processor_topology_validate(&mutant) ==
          GXOS_PROCESSOR_TOPOLOGY_STATUS_INVALID_PACKAGE_RELATIONSHIPS);
    mutant = g_snapshot;
    mutant.cache_count = 1;
    mutant.caches[0].processor_mask = 2;
    CHECK(gxos_processor_topology_validate(&mutant) ==
          GXOS_PROCESSOR_TOPOLOGY_STATUS_INVALID_CACHE_RELATIONSHIPS);
    mutant = g_snapshot;
    mutant.numa_node_count = 2;
    mutant.numa_nodes[1] = mutant.numa_nodes[0];
    CHECK(gxos_processor_topology_validate(&mutant) ==
          GXOS_PROCESSOR_TOPOLOGY_STATUS_INVALID_NUMA_RELATIONSHIPS);
    (void)status;
}

static void test_abi_and_serialization(void)
{
    GXOS_LOGICAL_PROCESSOR_INFORMATION records[3];
    uint8_t *bytes = (uint8_t *)records;
    uint32_t count = 0;
    uint32_t index;
    size_t required_size;

    CHECK(sizeof(GXOS_LOGICAL_PROCESSOR_INFORMATION) == 0x20);
    CHECK(offsetof(GXOS_LOGICAL_PROCESSOR_INFORMATION, processor_mask) == 0x00);
    CHECK(offsetof(GXOS_LOGICAL_PROCESSOR_INFORMATION, relationship) == 0x08);
    CHECK(offsetof(GXOS_LOGICAL_PROCESSOR_INFORMATION, reserved) == 0x0C);
    CHECK(offsetof(GXOS_LOGICAL_PROCESSOR_INFORMATION, relationship_info) == 0x10);
    CHECK(offsetof(GXOS_LOGICAL_PROCESSOR_INFORMATION,
                   relationship_info.processor_core.flags) == 0x10);
    CHECK(offsetof(GXOS_LOGICAL_PROCESSOR_INFORMATION,
                   relationship_info.numa_node.node_number) == 0x10);
    CHECK(offsetof(GXOS_LOGICAL_PROCESSOR_INFORMATION,
                   relationship_info.cache.level) == 0x10);
    CHECK(offsetof(GXOS_LOGICAL_PROCESSOR_INFORMATION,
                   relationship_info.cache.associativity) == 0x11);
    CHECK(offsetof(GXOS_LOGICAL_PROCESSOR_INFORMATION,
                   relationship_info.cache.line_size) == 0x12);
    CHECK(offsetof(GXOS_LOGICAL_PROCESSOR_INFORMATION,
                   relationship_info.cache.size) == 0x14);
    CHECK(offsetof(GXOS_LOGICAL_PROCESSOR_INFORMATION,
                   relationship_info.cache.type) == 0x18);
    CHECK(gxos_processor_topology_make_single_cpu(&g_snapshot, 1) ==
          GXOS_PROCESSOR_TOPOLOGY_STATUS_OK);
    memset(records, 0xA5, sizeof(records));
    CHECK(gxos_processor_topology_build_records(&g_snapshot, records, 3,
                                                &count) ==
          GXOS_PROCESSOR_TOPOLOGY_STATUS_OK);
    CHECK(count == 3);
    CHECK(records[0].processor_mask == 1 &&
          records[1].processor_mask == 1 && records[2].processor_mask == 1);
    CHECK(records[0].relationship == GXOS_RELATION_PROCESSOR_CORE);
    CHECK(records[1].relationship == GXOS_RELATION_NUMA_NODE);
    CHECK(records[2].relationship == GXOS_RELATION_PROCESSOR_PACKAGE);
    CHECK(records[0].relationship_info.processor_core.flags == 0);
    CHECK(records[1].relationship_info.numa_node.node_number == 0);
    CHECK(memcmp(bytes + 0x10, bytes + 0x30, 16) == 0);
    for (index = 0; index != sizeof(records); ++index) {
        if ((index % 0x20) >= 0x0C && (index % 0x20) < 0x10) {
            CHECK(bytes[index] == 0);
        }
        if ((index % 0x20) >= 0x10) CHECK(bytes[index] == 0);
    }
    CHECK(gxos_processor_topology_required_size(3, &required_size) != 0);
    CHECK(required_size == 0x60);
    CHECK(gxos_processor_topology_required_size(
              (uint64_t)(SIZE_MAX / 0x20U) + 1U, &required_size) == 0);
    CHECK(gxos_processor_topology_build_records(&g_snapshot, records, 2,
                                                &count) ==
          GXOS_PROCESSOR_TOPOLOGY_STATUS_INVALID_RECORD_STORAGE);
}

static void test_public_api_validation_and_sizing(void)
{
    GXOS_PROCESSOR_TOPOLOGY_REPORT report;
    uint8_t before[sizeof(g_output.data)];
    GXOS_PROCESSOR_TOPOLOGY_SNAPSHOT invalid;

    initialize_case(0);
    CHECK(gxos_get_logical_processor_information_checked(
              0, 0, &g_snapshot, &g_memory, &report) ==
          GXOS_PROCESSOR_TOPOLOGY_STATUS_NULL_RETURNED_LENGTH);

    initialize_case(0);
    CHECK(call_api(0, &report) ==
          GXOS_PROCESSOR_TOPOLOGY_STATUS_INSUFFICIENT_BUFFER);
    CHECK(g_returned_length == 0x60 && report.output_written == 0);
    {
        uint32_t last_error = 0;
        CHECK(gxos_processor_topology_status_last_error(
                  report.status, &last_error) ==
              GXOS_PROCESSOR_TOPOLOGY_STATUS_OK && last_error == 122);
    }
    CHECK(guards_are_intact());

    initialize_case(0x5A);
    CHECK(call_api(0, &report) ==
          GXOS_PROCESSOR_TOPOLOGY_STATUS_INSUFFICIENT_BUFFER);
    CHECK(g_returned_length == 0x60 && report.input_length == 0x5A);
    CHECK(guards_are_intact());

    initialize_case(0);
    memcpy(before, g_output.data, sizeof(before));
    CHECK(call_api((GXOS_LOGICAL_PROCESSOR_INFORMATION *)g_output.data,
                   &report) == GXOS_PROCESSOR_TOPOLOGY_STATUS_INSUFFICIENT_BUFFER);
    CHECK(g_returned_length == 0x60 && memcmp(before, g_output.data,
                                              sizeof(before)) == 0);
    CHECK(guards_are_intact());

    initialize_case(0x5F);
    memcpy(before, g_output.data, sizeof(before));
    CHECK(call_api((GXOS_LOGICAL_PROCESSOR_INFORMATION *)g_output.data,
                   &report) == GXOS_PROCESSOR_TOPOLOGY_STATUS_INSUFFICIENT_BUFFER);
    CHECK(g_returned_length == 0x60 && memcmp(before, g_output.data,
                                              sizeof(before)) == 0);
    CHECK(guards_are_intact());

    initialize_case(0x60);
    CHECK(call_api((GXOS_LOGICAL_PROCESSOR_INFORMATION *)g_output.data,
                   &report) == GXOS_PROCESSOR_TOPOLOGY_STATUS_OK);
    CHECK(g_returned_length == 0x60 && report.output_written == 1 &&
          report.return_value == 1 && report.buffer_range_valid == 1);
    CHECK(guards_are_intact());

    initialize_case(0x80);
    memset(g_output.data, 0xA5, sizeof(g_output.data));
    CHECK(call_api((GXOS_LOGICAL_PROCESSOR_INFORMATION *)g_output.data,
                   &report) == GXOS_PROCESSOR_TOPOLOGY_STATUS_OK);
    CHECK(g_returned_length == 0x60 && guards_are_intact());
    for (uint32_t index = 0x60; index != sizeof(g_output.data); ++index) {
        CHECK(g_output.data[index] == 0xA5);
    }

    initialize_case(0x60);
    CHECK(call_api((GXOS_LOGICAL_PROCESSOR_INFORMATION *)(uintptr_t)
                       0x0000800000000000ULL, &report) ==
          GXOS_PROCESSOR_TOPOLOGY_STATUS_NONCANONICAL_BUFFER);
    CHECK(g_returned_length == 0x60 && guards_are_intact());

    initialize_case(0x60);
    CHECK(call_api((GXOS_LOGICAL_PROCESSOR_INFORMATION *)(uintptr_t)
                       (UINTPTR_MAX - 0x1FU), &report) ==
          GXOS_PROCESSOR_TOPOLOGY_STATUS_BUFFER_RANGE_OVERFLOW);
    CHECK(g_returned_length == 0x60 && guards_are_intact());

    initialize_case(0x60);
    g_regions[1].end = g_regions[1].base + 0x5FU;
    CHECK(call_api((GXOS_LOGICAL_PROCESSOR_INFORMATION *)g_output.data,
                   &report) == GXOS_PROCESSOR_TOPOLOGY_STATUS_UNWRITABLE_BUFFER);
    CHECK(g_returned_length == 0x60 && guards_are_intact());

    initialize_case(0);
    CHECK(gxos_get_logical_processor_information_checked(
              0, (uint32_t *)(uintptr_t)0x0000800000000000ULL,
              &g_snapshot, &g_memory, &report) ==
          GXOS_PROCESSOR_TOPOLOGY_STATUS_NONCANONICAL_RETURNED_LENGTH);
    CHECK(gxos_get_logical_processor_information_checked(
              0, (uint32_t *)(UINTPTR_MAX - 1U), &g_snapshot, &g_memory,
              &report) ==
          GXOS_PROCESSOR_TOPOLOGY_STATUS_RETURNED_LENGTH_RANGE_OVERFLOW);

    invalid = g_snapshot;
    invalid.valid = 0;
    CHECK(gxos_get_logical_processor_information_checked(
              0, &g_returned_length, &invalid, &g_memory, &report) ==
          GXOS_PROCESSOR_TOPOLOGY_STATUS_INVALID_SNAPSHOT);
    CHECK(g_returned_length == 0);
}

static void test_determinism_and_no_cache(void)
{
    GXOS_PROCESSOR_TOPOLOGY_REPORT report;
    uint8_t first[0x60];
    uint8_t second[0x60];
    uint32_t index;

    initialize_case(0x60);
    CHECK(call_api((GXOS_LOGICAL_PROCESSOR_INFORMATION *)g_output.data,
                   &report) == GXOS_PROCESSOR_TOPOLOGY_STATUS_OK);
    memcpy(first, g_output.data, sizeof(first));
    initialize_case(0x60);
    CHECK(call_api((GXOS_LOGICAL_PROCESSOR_INFORMATION *)g_output.data,
                   &report) == GXOS_PROCESSOR_TOPOLOGY_STATUS_OK);
    memcpy(second, g_output.data, sizeof(second));
    CHECK(memcmp(first, second, sizeof(first)) == 0);
    for (index = 0; index != 3; ++index) {
        const GXOS_LOGICAL_PROCESSOR_INFORMATION *record =
            (const GXOS_LOGICAL_PROCESSOR_INFORMATION *)(first + index * 0x20U);
        CHECK(record->processor_mask == 1);
        CHECK(record->relationship != GXOS_RELATION_CACHE);
    }
}

static void test_ledger_backed_buffer_range(void)
{
    GXOS_PHYSICAL_ALLOCATION allocation;
    GXOS_PROCESSOR_TOPOLOGY_REPORT report;
    uint32_t slot = 0;

    initialize_case(0x60);
    g_memory.region_count = 1;
    memset(&allocation, 0, sizeof(allocation));
    allocation.base = (uintptr_t)&g_output;
    allocation.bytes = sizeof(g_output);
    allocation.allocation_class = GXOS_MEMORY_ALLOCATION_OTHER;
    allocation.owner = GXOS_MEMORY_OWNER_OTHER;
    allocation.physical_impact_bytes = allocation.bytes;
    allocation.generation = 1;
    CHECK(gxos_physical_ledger_insert(&g_ledger, &allocation, &slot) ==
          GXOS_LEDGER_STATUS_OK);
    CHECK(call_api((GXOS_LOGICAL_PROCESSOR_INFORMATION *)g_output.data,
                   &report) == GXOS_PROCESSOR_TOPOLOGY_STATUS_OK);
    CHECK(report.buffer_range_valid == 1 && report.output_written == 1);
    CHECK(g_returned_length == 0x60 && guards_are_intact());
}

int main(void)
{
    test_snapshot_model();
    test_abi_and_serialization();
    test_public_api_validation_and_sizing();
    test_determinism_and_no_cache();
    test_ledger_backed_buffer_range();
    if (g_failures != 0) return 1;
    puts("PROCESSOR_TOPOLOGY_HOST_TESTS=PASSED");
    return 0;
}

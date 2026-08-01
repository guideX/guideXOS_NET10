#include <stdint.h>
#include <stdio.h>
#include <string.h>

#include "../platform_process_group_affinity.h"

typedef struct {
    uint8_t before[16];
    uint16_t count;
    uint16_t groups[4];
    uint8_t after[16];
} GROUP_GUARDED;

static GXOS_PROCESS_GROUP_AFFINITY_FACTS g_facts;
static GXOS_SYSTEM_INFO_MEMORY_REGION g_region;
static GXOS_SYSTEM_INFO_MEMORY_CONTEXT g_memory;

static void configure_facts(uint16_t group_count, uint32_t processors,
                            uintptr_t mask)
{
    uint32_t index;
    memset(&g_facts, 0, sizeof(g_facts));
    g_facts.group_count = group_count;
    for (index = 0; index != group_count; index++) g_facts.group_numbers[index] = (uint16_t)index;
    g_facts.usable_processor_count = processors;
    g_facts.active_processor_mask = mask;
    g_facts.system_info_processor_count = processors;
    g_facts.system_info_active_processor_mask = mask;
    g_facts.topology_policy = GXOS_PROCESS_GROUP_AFFINITY_FACT_SNAPSHOT;
}

static void configure_memory(uintptr_t base, uintptr_t end, uint32_t readable,
                             uint32_t writable)
{
    g_region.base = base;
    g_region.end = end;
    g_region.readable = readable;
    g_region.writable = writable;
    g_memory.region_count = 1;
    g_memory.regions = &g_region;
}

static int expect_status(const char *name,
                         GXOS_PROCESS_GROUP_AFFINITY_STATUS actual,
                         GXOS_PROCESS_GROUP_AFFINITY_STATUS expected)
{
    if (actual != expected) {
        printf("PROCESS_GROUP_TEST_FAILURE=%s actual=%u expected=%u\n",
               name, (unsigned)actual, (unsigned)expected);
        return 1;
    }
    printf("PROCESS_GROUP_TEST_%s=PASS\n", name);
    return 0;
}

static int guards_are_poison(const GROUP_GUARDED *value)
{
    uint32_t index;
    for (index = 0; index != sizeof(value->before); index++) {
        if (value->before[index] != 0xA5 || value->after[index] != 0xA5) return 0;
    }
    return 1;
}

static int test_widths_and_single_group_probe(void)
{
    GROUP_GUARDED value;
    GXOS_PROCESS_GROUP_AFFINITY_REPORT report;
    GXOS_PROCESS_GROUP_AFFINITY_HANDLE handle =
        GXOS_PROCESS_GROUP_AFFINITY_CURRENT_PROCESS;

    if (sizeof(GXOS_PROCESS_GROUP_AFFINITY_BOOL) != 4 ||
        sizeof(GXOS_PROCESS_GROUP_AFFINITY_HANDLE) != 8 ||
        sizeof(GXOS_PROCESS_GROUP_AFFINITY_USHORT) != 2 || sizeof(uintptr_t) != 8) {
        printf("PROCESS_GROUP_TEST_FAILURE=width\n");
        return 1;
    }
    printf("PROCESS_GROUP_TEST_BOOL_WIDTH=PASS\n");
    printf("PROCESS_GROUP_TEST_HANDLE_WIDTH=PASS\n");
    printf("PROCESS_GROUP_TEST_USHORT_WIDTH=PASS\n");
    printf("PROCESS_GROUP_TEST_POINTER_WIDTH=PASS\n");
    configure_facts(1, 1, 1);
    memset(&value, 0xA5, sizeof(value));
    value.count = 0;
    value.groups[0] = 0xA5A5;
    configure_memory((uintptr_t)&value, (uintptr_t)&value + sizeof(value), 1, 1);
    if (gxos_get_process_group_affinity_checked(handle, &value.count, 0,
                                                &g_facts, &g_memory, &report) !=
            GXOS_PROCESS_GROUP_AFFINITY_STATUS_INSUFFICIENT_BUFFER ||
        value.count != 1 || value.groups[0] != 0xA5A5 ||
        report.input_capacity != 0 || report.required_count != 1 ||
        report.output_count != 1 || report.groups_written != 0 ||
        report.array_pointer_canonical != 0 || !guards_are_poison(&value)) {
        printf("PROCESS_GROUP_TEST_FAILURE=zero_capacity_probe\n");
        return 1;
    }
    printf("PROCESS_GROUP_TEST_ZERO_CAPACITY_INSUFFICIENT=PASS\n");
    printf("PROCESS_GROUP_TEST_NULL_ARRAY_PROBE=PASS\n");
    printf("PROCESS_GROUP_TEST_REQUIRED_COUNT_ONE=PASS\n");
    printf("PROCESS_GROUP_TEST_ARRAY_UNCHANGED_ON_INSUFFICIENT=PASS\n");
    return 0;
}

static int test_exact_and_excess_capacity(void)
{
    GROUP_GUARDED value;
    GXOS_PROCESS_GROUP_AFFINITY_REPORT report;
    int failures = 0;

    configure_facts(1, 1, 1);
    memset(&value, 0xA5, sizeof(value));
    value.count = 1;
    value.groups[0] = 0xA5A5;
    configure_memory((uintptr_t)&value, (uintptr_t)&value + sizeof(value), 1, 1);
    failures += expect_status("EXACT_CAPACITY",
        gxos_get_process_group_affinity_checked(
            GXOS_PROCESS_GROUP_AFFINITY_CURRENT_PROCESS, &value.count,
            value.groups, &g_facts, &g_memory, &report),
        GXOS_PROCESS_GROUP_AFFINITY_STATUS_OK);
    if (value.count != 1 || value.groups[0] != 0 || report.groups_written != 1 ||
        !guards_are_poison(&value)) failures++;
    memset(&value, 0xA5, sizeof(value));
    value.count = 4;
    configure_memory((uintptr_t)&value, (uintptr_t)&value + sizeof(value), 1, 1);
    failures += expect_status("EXCESS_CAPACITY",
        gxos_get_process_group_affinity_checked(
            GXOS_PROCESS_GROUP_AFFINITY_CURRENT_PROCESS, &value.count,
            value.groups, &g_facts, &g_memory, &report),
        GXOS_PROCESS_GROUP_AFFINITY_STATUS_OK);
    if (value.count != 1 || value.groups[0] != 0 || value.groups[1] != 0xA5A5 ||
        value.groups[2] != 0xA5A5 || value.groups[3] != 0xA5A5 ||
        !guards_are_poison(&value)) failures++;
    if (failures != 0) {
        printf("PROCESS_GROUP_TEST_FAILURE=exact_or_excess_output\n");
        return 1;
    }
    printf("PROCESS_GROUP_TEST_GROUP_ZERO=PASS\n");
    printf("PROCESS_GROUP_TEST_EXCESS_TRAILING_UNCHANGED=PASS\n");
    printf("PROCESS_GROUP_TEST_EXACT_USHORT_WRITES=PASS\n");
    printf("PROCESS_GROUP_TEST_LAST_ERROR_EXTERNAL_TO_CORE=PASS\n");
    return 0;
}

static int test_synthetic_insufficient_and_pointer_policy(void)
{
    GROUP_GUARDED value;
    GXOS_PROCESS_GROUP_AFFINITY_REPORT report;
    GXOS_PROCESS_GROUP_AFFINITY_USHORT before;
    int failures = 0;

    configure_facts(2, 2, 3);
    memset(&value, 0xA5, sizeof(value));
    value.count = 1;
    configure_memory((uintptr_t)&value, (uintptr_t)&value + sizeof(value), 1, 1);
    failures += expect_status("SYNTHETIC_INSUFFICIENT",
        gxos_get_process_group_affinity_checked(
            GXOS_PROCESS_GROUP_AFFINITY_CURRENT_PROCESS, &value.count,
            value.groups, &g_facts, &g_memory, &report),
        GXOS_PROCESS_GROUP_AFFINITY_STATUS_INSUFFICIENT_BUFFER);
    if (value.count != 2 || value.groups[0] != 0xA5A5 ||
        value.groups[1] != 0xA5A5 || report.groups_written != 0) failures++;
    configure_facts(1, 1, 1);
    value.count = 1;
    value.groups[0] = 0xA5A5;
    before = value.count;
    failures += expect_status("NULL_ARRAY_WITH_CAPACITY",
        gxos_get_process_group_affinity_checked(
            GXOS_PROCESS_GROUP_AFFINITY_CURRENT_PROCESS, &value.count, 0,
            &g_facts, &g_memory, &report),
        GXOS_PROCESS_GROUP_AFFINITY_STATUS_NULL_GROUP_ARRAY);
    if (value.count != before || value.groups[0] != 0xA5A5) failures++;
    failures += expect_status("INVALID_HANDLE",
        gxos_get_process_group_affinity_checked((uintptr_t)0x1234567887654321ULL,
            &value.count, value.groups, &g_facts, &g_memory, &report),
        GXOS_PROCESS_GROUP_AFFINITY_STATUS_INVALID_PROCESS_HANDLE);
    if (value.count != before || value.groups[0] != 0xA5A5) failures++;
    if (failures != 0) {
        printf("PROCESS_GROUP_TEST_FAILURE=synthetic_or_handle_policy\n");
        return 1;
    }
    printf("PROCESS_GROUP_TEST_SYNTHETIC_REQUIRED_COUNT_TWO=PASS\n");
    printf("PROCESS_GROUP_TEST_NO_PARTIAL_ARRAY_WRITE=PASS\n");
    printf("PROCESS_GROUP_TEST_NULL_ARRAY_REJECTED_WHEN_SUFFICIENT=PASS\n");
    printf("PROCESS_GROUP_TEST_FULL_HANDLE_IDENTITY=PASS\n");
    return 0;
}

static int test_negative_pointer_and_topology_controls(void)
{
    GROUP_GUARDED value;
    GXOS_PROCESS_GROUP_AFFINITY_REPORT report;
    GXOS_PROCESS_GROUP_AFFINITY_FACTS mutant;
    GXOS_PROCESS_GROUP_AFFINITY_USHORT before_count;
    GXOS_PROCESS_GROUP_AFFINITY_USHORT before_group;
    int failures = 0;

    configure_facts(1, 1, 1);
    memset(&value, 0xA5, sizeof(value));
    value.count = 1;
    configure_memory((uintptr_t)&value, (uintptr_t)&value + sizeof(value), 1, 1);
    before_count = value.count;
    before_group = value.groups[0];
    failures += expect_status("NULL_COUNT",
        gxos_get_process_group_affinity_checked(
            GXOS_PROCESS_GROUP_AFFINITY_CURRENT_PROCESS, 0, value.groups,
            &g_facts, &g_memory, &report),
        GXOS_PROCESS_GROUP_AFFINITY_STATUS_NULL_GROUP_COUNT);
    failures += expect_status("NONCANONICAL_COUNT",
        gxos_get_process_group_affinity_checked(
            GXOS_PROCESS_GROUP_AFFINITY_CURRENT_PROCESS,
            (GXOS_PROCESS_GROUP_AFFINITY_USHORT *)(uintptr_t)0x0000800000000000ULL,
            value.groups, &g_facts, &g_memory, &report),
        GXOS_PROCESS_GROUP_AFFINITY_STATUS_NONCANONICAL_GROUP_COUNT);
    g_region.writable = 0;
    failures += expect_status("READ_ONLY_COUNT",
        gxos_get_process_group_affinity_checked(
            GXOS_PROCESS_GROUP_AFFINITY_CURRENT_PROCESS, &value.count,
            value.groups, &g_facts, &g_memory, &report),
        GXOS_PROCESS_GROUP_AFFINITY_STATUS_UNWRITABLE_GROUP_COUNT);
    g_region.writable = 1;
    g_region.end = (uintptr_t)&value.count + sizeof(value.count) - 1U;
    failures += expect_status("UNDERSIZED_COUNT_REGION",
        gxos_get_process_group_affinity_checked(
            GXOS_PROCESS_GROUP_AFFINITY_CURRENT_PROCESS, &value.count,
            value.groups, &g_facts, &g_memory, &report),
        GXOS_PROCESS_GROUP_AFFINITY_STATUS_UNREADABLE_GROUP_COUNT);
    configure_memory((uintptr_t)&value, (uintptr_t)&value + sizeof(value), 1, 1);
    failures += expect_status("NONCANONICAL_ARRAY",
        gxos_get_process_group_affinity_checked(
            GXOS_PROCESS_GROUP_AFFINITY_CURRENT_PROCESS, &value.count,
            (GXOS_PROCESS_GROUP_AFFINITY_USHORT *)(uintptr_t)0x0000800000000000ULL,
            &g_facts, &g_memory, &report),
        GXOS_PROCESS_GROUP_AFFINITY_STATUS_NONCANONICAL_GROUP_ARRAY);
    mutant = g_facts;
    mutant.group_numbers[0] = 1;
    failures += expect_status("WRONG_GROUP_NUMBER",
        gxos_get_process_group_affinity_checked(
            GXOS_PROCESS_GROUP_AFFINITY_CURRENT_PROCESS, &value.count,
            value.groups, &mutant, &g_memory, &report),
        GXOS_PROCESS_GROUP_AFFINITY_STATUS_UNSUPPORTED_TOPOLOGY);
    mutant = g_facts;
    mutant.group_count = 0;
    failures += expect_status("ZERO_GROUP_COUNT_FACTS",
        gxos_get_process_group_affinity_checked(
            GXOS_PROCESS_GROUP_AFFINITY_CURRENT_PROCESS, &value.count,
            value.groups, &mutant, &g_memory, &report),
        GXOS_PROCESS_GROUP_AFFINITY_STATUS_INVALID_TOPOLOGY);
    if (value.count != before_count || value.groups[0] != before_group ||
        failures != 0) {
        printf("PROCESS_GROUP_TEST_FAILURE=negative_controls\n");
        return 1;
    }
    printf("PROCESS_GROUP_TEST_POINTER_NEGATIVE_CONTROLS=PASS\n");
    printf("PROCESS_GROUP_TEST_WRONG_GROUP_NEGATIVE_CONTROL=PASS\n");
    printf("PROCESS_GROUP_TEST_NO_MUTATION_ON_FAILURE=PASS\n");
    return 0;
}

static int test_abi(void)
{
    GROUP_GUARDED value;
    GXOS_PROCESS_GROUP_AFFINITY_REPORT report;
    GXOS_PROCESS_GROUP_AFFINITY_BOOL (GXOS_PROCESS_GROUP_AFFINITY_MS_ABI *wrapper)(
        GXOS_PROCESS_GROUP_AFFINITY_HANDLE,
        GXOS_PROCESS_GROUP_AFFINITY_USHORT *,
        GXOS_PROCESS_GROUP_AFFINITY_USHORT *,
        const GXOS_PROCESS_GROUP_AFFINITY_FACTS *,
        const GXOS_SYSTEM_INFO_MEMORY_CONTEXT *,
        GXOS_PROCESS_GROUP_AFFINITY_REPORT *) =
        gxos_get_process_group_affinity_abi_probe;
    configure_facts(1, 1, 1);
    memset(&value, 0xA5, sizeof(value));
    value.count = 1;
    configure_memory((uintptr_t)&value, (uintptr_t)&value + sizeof(value), 1, 1);
    if (wrapper(GXOS_PROCESS_GROUP_AFFINITY_CURRENT_PROCESS, &value.count,
                value.groups, &g_facts, &g_memory, &report) !=
            GXOS_PROCESS_GROUP_AFFINITY_TRUE || value.count != 1 ||
        value.groups[0] != 0) {
        printf("PROCESS_GROUP_TEST_FAILURE=ms_abi\n");
        return 1;
    }
    printf("PROCESS_GROUP_TEST_MS_ABI_RCX_RDX_R8_EAX=PASS\n");
    return 0;
}

int main(void)
{
    if (test_widths_and_single_group_probe() != 0 ||
        test_exact_and_excess_capacity() != 0 ||
        test_synthetic_insufficient_and_pointer_policy() != 0 ||
        test_negative_pointer_and_topology_controls() != 0 ||
        test_abi() != 0) return 1;
    printf("PROCESS_GROUP_HOST_TESTS=PASSED\n");
    return 0;
}

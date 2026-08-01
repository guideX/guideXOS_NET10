#include <stdint.h>
#include <stdio.h>
#include <string.h>

#include "../platform_numa.h"

typedef struct {
    uint8_t before[16];
    GXOS_NUMA_ULONG value;
    uint8_t after[16];
} NUMA_GUARDED;

static GXOS_NUMA_FACTS g_facts;
static GXOS_SYSTEM_INFO_MEMORY_REGION g_region;
static GXOS_SYSTEM_INFO_MEMORY_CONTEXT g_memory;

static void configure_facts(uint32_t processors, uintptr_t mask,
                            uint32_t domains, uint32_t highest)
{
    memset(&g_facts, 0, sizeof(g_facts));
    g_facts.usable_processor_count = processors;
    g_facts.locality_domain_count = domains;
    g_facts.highest_node_number = highest;
    g_facts.node_targeted_allocation_supported = false;
    g_facts.system_info_processor_count = processors;
    g_facts.system_info_active_processor_mask = mask;
    g_facts.topology_policy = GXOS_NUMA_TOPOLOGY_POLICY_FACT_SNAPSHOT;
}

static void configure_memory(uintptr_t base, uintptr_t end, uint32_t writable)
{
    g_region.base = base;
    g_region.end = end;
    g_region.readable = 1;
    g_region.writable = writable;
    g_memory.region_count = 1;
    g_memory.regions = &g_region;
}

static int expect_status(const char *name,
                         GXOS_NUMA_HIGHEST_NODE_STATUS actual,
                         GXOS_NUMA_HIGHEST_NODE_STATUS expected)
{
    if (actual != expected) {
        printf("NUMA_TEST_FAILURE=%s actual=%u expected=%u\n",
               name, (unsigned)actual, (unsigned)expected);
        return 1;
    }
    printf("NUMA_TEST_%s=PASS\n", name);
    return 0;
}

static int test_layout_and_single_domain(void)
{
    NUMA_GUARDED guarded;
    NUMA_GUARDED second;
    GXOS_NUMA_FACTS before;
    uint32_t index;

    if (sizeof(GXOS_NUMA_ULONG) != 4 || sizeof(GXOS_NUMA_BOOL) != 4 ||
        sizeof(uintptr_t) != 8) {
        printf("NUMA_TEST_FAILURE=width\n");
        return 1;
    }
    printf("NUMA_TEST_ULONG_WIDTH=PASS\n");
    printf("NUMA_TEST_BOOL_WIDTH=PASS\n");
    configure_facts(1, 1, 1, 0);
    before = g_facts;
    memset(&guarded, 0xA5, sizeof(guarded));
    memset(&second, 0xA5, sizeof(second));
    configure_memory((uintptr_t)&guarded.value,
                     (uintptr_t)&guarded.value + sizeof(guarded.value), 1);
    if (gxos_get_numa_highest_node_checked(&guarded.value, &g_facts, &g_memory) !=
            GXOS_NUMA_HIGHEST_NODE_STATUS_OK ||
        guarded.value != 0) {
        printf("NUMA_TEST_FAILURE=single_domain_result\n");
        return 1;
    }
    if (memcmp(guarded.before, second.before, sizeof(guarded.before)) == 0) {
        /* Both are poison, so this is only a compile-time/use sanity check. */
    }
    for (index = 0; index != sizeof(guarded.before); index++) {
        if (guarded.before[index] != 0xA5 || guarded.after[index] != 0xA5) {
            printf("NUMA_TEST_FAILURE=guard\n");
            return 1;
        }
    }
    if (memcmp(&before, &g_facts, sizeof(before)) != 0) {
        printf("NUMA_TEST_FAILURE=facts_mutated\n");
        return 1;
    }
    configure_memory((uintptr_t)&second.value,
                     (uintptr_t)&second.value + sizeof(second.value), 1);
    if (gxos_get_numa_highest_node_checked(&second.value, &g_facts, &g_memory) !=
            GXOS_NUMA_HIGHEST_NODE_STATUS_OK ||
        second.value != 0 || guarded.value != second.value) {
        printf("NUMA_TEST_FAILURE=repeatability\n");
        return 1;
    }
    printf("NUMA_TEST_SINGLE_DOMAIN_HIGHEST_ZERO=PASS\n");
    printf("NUMA_TEST_ZERO_IS_ONE_VALID_NODE=PASS\n");
    printf("NUMA_TEST_GUARD=PASS\n");
    printf("NUMA_TEST_FACTS_PRESERVED=PASS\n");
    printf("NUMA_TEST_REPEATABILITY=PASS\n");
    printf("NUMA_TEST_SEPARATE_OUTPUTS=PASS\n");
    printf("NUMA_TEST_EXACT_OUTPUT_WIDTH=PASS\n");
    return 0;
}

static int test_synthetic_domains(void)
{
    GXOS_NUMA_ULONG output = 0xA5A5A5A5U;
    GXOS_NUMA_FACTS before;
    int failures = 0;

    configure_facts(2, 3, 2, 1);
    before = g_facts;
    configure_memory((uintptr_t)&output, (uintptr_t)&output + sizeof(output), 1);
    failures += expect_status("TWO_DOMAIN_HIGHEST_ONE",
        gxos_get_numa_highest_node_checked(&output, &g_facts, &g_memory),
        GXOS_NUMA_HIGHEST_NODE_STATUS_OK);
    if (output != 1 || memcmp(&before, &g_facts, sizeof(before)) != 0) failures++;
    configure_facts(1, 1, 3, 2);
    output = 0xA5A5A5A5U;
    if (gxos_get_numa_highest_node_checked(&output, &g_facts, &g_memory) !=
            GXOS_NUMA_HIGHEST_NODE_STATUS_OK || output != 2) {
        failures++;
    }
    printf("NUMA_TEST_MULTI_DOMAIN_DOMAIN_COUNT_MINUS_ONE=%s\n",
           failures == 0 ? "PASS" : "FAIL");
    return failures;
}

static int test_negative_cases(void)
{
    GXOS_NUMA_ULONG output;
    GXOS_NUMA_FACTS mutant;
    GXOS_SYSTEM_INFO_MEMORY_REGION saved;
    int failures = 0;

    configure_facts(1, 1, 1, 0);
    output = 0xA5A5A5A5U;
    configure_memory((uintptr_t)&output, (uintptr_t)&output + sizeof(output), 1);
    saved = g_region;
    failures += expect_status("NULL_OUTPUT",
        gxos_get_numa_highest_node_checked(0, &g_facts, &g_memory),
        GXOS_NUMA_HIGHEST_NODE_STATUS_NULL_POINTER);
    failures += expect_status("NONCANONICAL_OUTPUT",
        gxos_get_numa_highest_node_checked((GXOS_NUMA_ULONG *)(uintptr_t)0x0000800000000000ULL,
                                            &g_facts, &g_memory),
        GXOS_NUMA_HIGHEST_NODE_STATUS_NONCANONICAL_POINTER);
    g_region.writable = 0;
    failures += expect_status("READ_ONLY_OUTPUT",
        gxos_get_numa_highest_node_checked(&output, &g_facts, &g_memory),
        GXOS_NUMA_HIGHEST_NODE_STATUS_UNWRITABLE_POINTER);
    g_region = saved;
    g_region.end = (uintptr_t)&output + sizeof(output) - 1U;
    failures += expect_status("INSUFFICIENT_WRITABLE_RANGE",
        gxos_get_numa_highest_node_checked(&output, &g_facts, &g_memory),
        GXOS_NUMA_HIGHEST_NODE_STATUS_UNWRITABLE_POINTER);
    g_region = saved;
    failures += expect_status("POINTER_OVERFLOW",
        gxos_get_numa_highest_node_checked((GXOS_NUMA_ULONG *)(UINTPTR_MAX - 3U),
                                            &g_facts, &g_memory),
        GXOS_NUMA_HIGHEST_NODE_STATUS_INSUFFICIENT_WRITABLE_RANGE);

    mutant = g_facts;
    mutant.usable_processor_count = 0;
    output = 0xA5A5A5A5U;
    failures += expect_status("ZERO_PROCESSOR_COUNT",
        gxos_get_numa_highest_node_checked(&output, &mutant, &g_memory),
        GXOS_NUMA_HIGHEST_NODE_STATUS_INVALID_PROCESSOR_COUNT);
    if (output != 0xA5A5A5A5U) failures++;
    mutant = g_facts;
    mutant.locality_domain_count = 0;
    failures += expect_status("ZERO_DOMAIN_COUNT",
        gxos_get_numa_highest_node_checked(&output, &mutant, &g_memory),
        GXOS_NUMA_HIGHEST_NODE_STATUS_INVALID_DOMAIN_COUNT);
    mutant = g_facts;
    mutant.highest_node_number = 1;
    failures += expect_status("HIGHEST_DOMAIN_INCONSISTENCY",
        gxos_get_numa_highest_node_checked(&output, &mutant, &g_memory),
        GXOS_NUMA_HIGHEST_NODE_STATUS_INCONSISTENT_DOMAIN_MODEL);
    mutant = g_facts;
    mutant.highest_node_number = UINT32_MAX;
    failures += expect_status("HIGHEST_OVERFLOW_RISK",
        gxos_get_numa_highest_node_checked(&output, &mutant, &g_memory),
        GXOS_NUMA_HIGHEST_NODE_STATUS_INVALID_HIGHEST_NODE);
    mutant = g_facts;
    mutant.system_info_processor_count = 2;
    failures += expect_status("PROCESSOR_SNAPSHOT_MISMATCH",
        gxos_get_numa_highest_node_checked(&output, &mutant, &g_memory),
        GXOS_NUMA_HIGHEST_NODE_STATUS_INVALID_SYSTEM_SNAPSHOT);
    mutant = g_facts;
    mutant.system_info_active_processor_mask = 3;
    failures += expect_status("ACTIVE_MASK_MISMATCH",
        gxos_get_numa_highest_node_checked(&output, &mutant, &g_memory),
        GXOS_NUMA_HIGHEST_NODE_STATUS_INVALID_PROCESSOR_MASK);
    mutant = g_facts;
    mutant.topology_policy = 0;
    failures += expect_status("UNSUPPORTED_TOPOLOGY_POLICY",
        gxos_get_numa_highest_node_checked(&output, &mutant, &g_memory),
        GXOS_NUMA_HIGHEST_NODE_STATUS_UNSUPPORTED_TOPOLOGY);
    if (output != 0xA5A5A5A5U) failures++;
    if (failures != 0) return 1;
    printf("NUMA_TEST_FAILURE_OUTPUT_UNCHANGED=PASS\n");
    printf("NUMA_TEST_NEGATIVE_CONTROLS=PASS\n");
    return 0;
}

static int test_ms_abi(void)
{
    GXOS_NUMA_ULONG output = 0xA5A5A5A5U;
    GXOS_NUMA_BOOL (GXOS_NUMA_MS_ABI *wrapper)(GXOS_NUMA_ULONG *,
                                                const GXOS_NUMA_FACTS *,
                                                const GXOS_SYSTEM_INFO_MEMORY_CONTEXT *) =
        gxos_get_numa_highest_node_abi_probe;
    configure_facts(1, 1, 1, 0);
    configure_memory((uintptr_t)&output, (uintptr_t)&output + sizeof(output), 1);
    if (wrapper(&output, &g_facts, &g_memory) != GXOS_NUMA_TRUE || output != 0) {
        printf("NUMA_TEST_FAILURE=ms_abi\n");
        return 1;
    }
    printf("NUMA_TEST_MS_ABI_RCX_RAX=PASS\n");
    return 0;
}

int main(void)
{
    if (test_layout_and_single_domain() != 0 ||
        test_synthetic_domains() != 0 ||
        test_negative_cases() != 0 ||
        test_ms_abi() != 0) return 1;
    printf("NUMA_HOST_TESTS=PASSED\n");
    return 0;
}

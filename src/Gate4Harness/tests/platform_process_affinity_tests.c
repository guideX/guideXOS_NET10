#include <stdint.h>
#include <stdio.h>
#include <string.h>

#include "../platform_process_affinity.h"

typedef struct {
    uint8_t before[16];
    uint64_t process_mask;
    uint64_t system_mask;
    uint8_t after[16];
} AFFINITY_GUARDED;

static GXOS_PROCESS_AFFINITY_FACTS g_facts;
static GXOS_SYSTEM_INFO_MEMORY_REGION g_regions[2];
static GXOS_SYSTEM_INFO_MEMORY_CONTEXT g_memory;
static unsigned g_test_count;
static unsigned g_failure_count;

static uint32_t population(uint64_t value)
{
    uint32_t count = 0;
    while (value != 0) {
        value &= value - 1U;
        count++;
    }
    return count;
}

static void configure_facts(uint64_t process_mask, uint64_t system_mask,
                            uint64_t usable_mask, uint32_t usable_count)
{
    memset(&g_facts, 0, sizeof(g_facts));
    g_facts.supported_process_handle = GXOS_PROCESS_AFFINITY_CURRENT_PROCESS;
    g_facts.process_affinity_mask = process_mask;
    g_facts.system_affinity_mask = system_mask;
    g_facts.usable_processor_mask = usable_mask;
    g_facts.usable_processor_count = usable_count;
    g_facts.system_info_processor_count = usable_count;
    g_facts.system_info_active_processor_mask = system_mask;
    g_facts.processor_group_count = 1;
    g_facts.current_group_number = 0;
    g_facts.topology_policy = GXOS_PROCESS_AFFINITY_TOPOLOGY_FACT_SNAPSHOT;
}

static void configure_memory(uintptr_t base, uintptr_t end, uint32_t readable,
                             uint32_t writable)
{
    memset(g_regions, 0, sizeof(g_regions));
    g_regions[0].base = base;
    g_regions[0].end = end;
    g_regions[0].readable = readable;
    g_regions[0].writable = writable;
    g_memory.region_count = 1;
    g_memory.regions = g_regions;
}

static void configure_split_memory(AFFINITY_GUARDED *value,
                                   uint32_t process_writable,
                                   uint32_t system_writable,
                                   uintptr_t system_end)
{
    memset(g_regions, 0, sizeof(g_regions));
    g_regions[0].base = (uintptr_t)&value->process_mask;
    g_regions[0].end = (uintptr_t)&value->process_mask + sizeof(value->process_mask);
    g_regions[0].readable = 1;
    g_regions[0].writable = process_writable;
    g_regions[1].base = (uintptr_t)&value->system_mask;
    g_regions[1].end = system_end == 0
                           ? (uintptr_t)&value->system_mask + sizeof(value->system_mask)
                           : system_end;
    g_regions[1].readable = 1;
    g_regions[1].writable = system_writable;
    g_memory.region_count = 2;
    g_memory.regions = g_regions;
}

static void prepare(AFFINITY_GUARDED *value, uint64_t process_value,
                    uint64_t system_value, uint32_t writable)
{
    memset(value, 0xA5, sizeof(*value));
    value->process_mask = process_value;
    value->system_mask = system_value;
    configure_memory((uintptr_t)value, (uintptr_t)value + sizeof(*value), 1,
                     writable);
}

static int guards_ok(const AFFINITY_GUARDED *value)
{
    uint32_t index;
    for (index = 0; index != sizeof(value->before); index++) {
        if (value->before[index] != 0xA5 || value->after[index] != 0xA5) return 0;
    }
    return 1;
}

static void check(const char *name, int passed)
{
    g_test_count++;
    if (!passed) {
        printf("PROCESS_AFFINITY_TEST_FAILURE=%s\n", name);
        g_failure_count++;
    } else {
        printf("PROCESS_AFFINITY_TEST_%s=PASS\n", name);
    }
}

static void check_status(const char *name, GXOS_PROCESS_AFFINITY_STATUS actual,
                         GXOS_PROCESS_AFFINITY_STATUS expected)
{
    if (actual != expected) {
        printf("PROCESS_AFFINITY_TEST_STATUS_DETAIL=%s actual=%u expected=%u\n",
               name, (unsigned)actual, (unsigned)expected);
    }
    check(name, actual == expected);
}

static GXOS_PROCESS_AFFINITY_STATUS call_checked(
    AFFINITY_GUARDED *value, GXOS_PROCESS_AFFINITY_DWORD_PTR *process_pointer,
    GXOS_PROCESS_AFFINITY_DWORD_PTR *system_pointer,
    GXOS_PROCESS_AFFINITY_REPORT *report)
{
    (void)value;
    return gxos_get_process_affinity_mask_checked(
        GXOS_PROCESS_AFFINITY_CURRENT_PROCESS, process_pointer, system_pointer,
        &g_facts, &g_memory, report);
}

static void test_widths_and_success(void)
{
    AFFINITY_GUARDED value;
    GXOS_PROCESS_AFFINITY_REPORT report;
    uint64_t before_facts[3];
    GXOS_PROCESS_AFFINITY_BOOL (GXOS_PROCESS_AFFINITY_MS_ABI *abi_wrapper)(
        GXOS_PROCESS_AFFINITY_HANDLE,
        GXOS_PROCESS_AFFINITY_DWORD_PTR *,
        GXOS_PROCESS_AFFINITY_DWORD_PTR *,
        const GXOS_PROCESS_AFFINITY_FACTS *,
        const GXOS_SYSTEM_INFO_MEMORY_CONTEXT *,
        GXOS_PROCESS_AFFINITY_REPORT *) = gxos_get_process_affinity_mask_abi_probe;
    GXOS_PROCESS_AFFINITY_BOOL result;

    check("BOOL_WIDTH_4", sizeof(GXOS_PROCESS_AFFINITY_BOOL) == 4);
    check("HANDLE_WIDTH_8", sizeof(GXOS_PROCESS_AFFINITY_HANDLE) == 8);
    check("DWORD_PTR_WIDTH_8", sizeof(GXOS_PROCESS_AFFINITY_DWORD_PTR) == 8);
    check("POINTER_WIDTH_8", sizeof(uintptr_t) == 8);
    configure_facts(1, 1, 1, 1);
    before_facts[0] = g_facts.process_affinity_mask;
    before_facts[1] = g_facts.system_affinity_mask;
    before_facts[2] = g_facts.usable_processor_mask;
    prepare(&value, 0x1122334455667788ULL, 0x8877665544332211ULL, 1);
    result = gxos_get_process_affinity_mask_abi_probe(
        GXOS_PROCESS_AFFINITY_CURRENT_PROCESS, &value.process_mask,
        &value.system_mask, &g_facts, &g_memory, &report);
    check("CURRENT_PROCESS_SUCCESS", result == GXOS_PROCESS_AFFINITY_TRUE);
    check("PROCESS_MASK_EXACT_64BIT", value.process_mask == 1);
    check("SYSTEM_MASK_EXACT_64BIT", value.system_mask == 1);
    check("PROCESS_OUTPUT_WRITTEN_8", report.process_written == 1 &&
                                      report.process_mask_written == 1);
    check("SYSTEM_OUTPUT_WRITTEN_8", report.system_written == 1 &&
                                     report.system_mask_written == 1);
    check("OUTPUTS_DISTINCT", &value.process_mask != &value.system_mask);
    check("OUTPUT_GUARDS_UNCHANGED", guards_ok(&value));
    check("FACTS_UNCHANGED", before_facts[0] == g_facts.process_affinity_mask &&
                                before_facts[1] == g_facts.system_affinity_mask &&
                                before_facts[2] == g_facts.usable_processor_mask);
    result = abi_wrapper(GXOS_PROCESS_AFFINITY_CURRENT_PROCESS,
                         &value.process_mask, &value.system_mask, &g_facts,
                         &g_memory, &report);
    check("MS_ABI_RCX_RDX_R8_EAX", result == GXOS_PROCESS_AFFINITY_TRUE &&
                                     value.process_mask == 1 && value.system_mask == 1);
    check("BOOL_RESULT_IS_32BIT", sizeof(result) == 4);
    check("REPORT_POINTERS_VALID", report.process_pointer_canonical == 1 &&
                                    report.process_pointer_writable == 1 &&
                                    report.process_range_valid == 1 &&
                                    report.system_pointer_canonical == 1 &&
                                    report.system_pointer_writable == 1 &&
                                    report.system_range_valid == 1);
}

static void test_pointer_and_handle_controls(void)
{
    AFFINITY_GUARDED value;
    GXOS_PROCESS_AFFINITY_REPORT report;
    GXOS_PROCESS_AFFINITY_FACTS mutant;
    GXOS_PROCESS_AFFINITY_STATUS status;
    uint64_t process_before;
    uint64_t system_before;

    configure_facts(1, 1, 1, 1);
    prepare(&value, 0x1111222233334444ULL, 0x5555666677778888ULL, 1);
    process_before = value.process_mask;
    system_before = value.system_mask;
    status = gxos_get_process_affinity_mask_checked(
        GXOS_PROCESS_AFFINITY_CURRENT_PROCESS, &value.process_mask,
        &value.process_mask, &g_facts, &g_memory, &report);
    check_status("ALIASED_OUTPUTS", status,
                 GXOS_PROCESS_AFFINITY_STATUS_ALIASED_OUTPUTS);
    check("ALIAS_NO_MUTATION", value.process_mask == process_before &&
                                value.system_mask == system_before);
    status = gxos_get_process_affinity_mask_checked(
        (GXOS_PROCESS_AFFINITY_HANDLE)0x00000000FFFFFFFFULL,
        &value.process_mask, &value.system_mask, &g_facts, &g_memory, &report);
    check_status("FULL_HANDLE_IDENTITY", status,
                 GXOS_PROCESS_AFFINITY_STATUS_INVALID_PROCESS_HANDLE);
    check("INVALID_HANDLE_NO_MUTATION", value.process_mask == process_before &&
                                         value.system_mask == system_before);
    status = gxos_get_process_affinity_mask_checked(
        GXOS_PROCESS_AFFINITY_CURRENT_PROCESS, 0, &value.system_mask,
        &g_facts, &g_memory, &report);
    check_status("NULL_PROCESS_POINTER", status,
                 GXOS_PROCESS_AFFINITY_STATUS_NULL_PROCESS_MASK);
    status = gxos_get_process_affinity_mask_checked(
        GXOS_PROCESS_AFFINITY_CURRENT_PROCESS, &value.process_mask, 0,
        &g_facts, &g_memory, &report);
    check_status("NULL_SYSTEM_POINTER", status,
                 GXOS_PROCESS_AFFINITY_STATUS_NULL_SYSTEM_MASK);
    status = gxos_get_process_affinity_mask_checked(
        GXOS_PROCESS_AFFINITY_CURRENT_PROCESS,
        (GXOS_PROCESS_AFFINITY_DWORD_PTR *)(uintptr_t)0x0000800000000000ULL,
        &value.system_mask, &g_facts, &g_memory, &report);
    check_status("NONCANONICAL_PROCESS_POINTER", status,
                 GXOS_PROCESS_AFFINITY_STATUS_NONCANONICAL_PROCESS_MASK);
    status = gxos_get_process_affinity_mask_checked(
        GXOS_PROCESS_AFFINITY_CURRENT_PROCESS, &value.process_mask,
        (GXOS_PROCESS_AFFINITY_DWORD_PTR *)(uintptr_t)0x0000800000000000ULL,
        &g_facts, &g_memory, &report);
    check_status("NONCANONICAL_SYSTEM_POINTER", status,
                 GXOS_PROCESS_AFFINITY_STATUS_NONCANONICAL_SYSTEM_MASK);
    configure_split_memory(&value, 0, 1, 0);
    status = call_checked(&value, &value.process_mask, &value.system_mask, &report);
    check_status("READ_ONLY_PROCESS_POINTER", status,
                 GXOS_PROCESS_AFFINITY_STATUS_UNWRITABLE_PROCESS_MASK);
    g_regions[0].writable = 1;
    g_regions[0].end = (uintptr_t)&value.process_mask + sizeof(value.process_mask) - 1U;
    status = call_checked(&value, &value.process_mask, &value.system_mask, &report);
    check_status("UNDERSIZED_PROCESS_RANGE", status,
                 GXOS_PROCESS_AFFINITY_STATUS_UNWRITABLE_PROCESS_MASK);
    configure_split_memory(&value, 1, 0, 0);
    status = call_checked(&value, &value.process_mask, &value.system_mask, &report);
    check_status("READ_ONLY_SYSTEM_POINTER", status,
                 GXOS_PROCESS_AFFINITY_STATUS_UNWRITABLE_SYSTEM_MASK);
    configure_split_memory(&value, 1, 1,
                           (uintptr_t)&value.system_mask + sizeof(value.system_mask) - 1U);
    status = call_checked(&value, &value.process_mask, &value.system_mask, &report);
    check_status("UNDERSIZED_SYSTEM_RANGE", status,
                 GXOS_PROCESS_AFFINITY_STATUS_UNWRITABLE_SYSTEM_MASK);
    configure_memory((uintptr_t)&value, (uintptr_t)&value + sizeof(value), 1, 1);
    status = gxos_get_process_affinity_mask_checked(
        GXOS_PROCESS_AFFINITY_CURRENT_PROCESS,
        (GXOS_PROCESS_AFFINITY_DWORD_PTR *)(uintptr_t)0xFFFFFFFFFFFFFFFCULL,
        &value.system_mask, &g_facts, &g_memory, &report);
    check_status("PROCESS_POINTER_RANGE_OVERFLOW", status,
                 GXOS_PROCESS_AFFINITY_STATUS_RANGE_OVERFLOW);
    status = gxos_get_process_affinity_mask_checked(
        GXOS_PROCESS_AFFINITY_CURRENT_PROCESS, &value.process_mask,
        (GXOS_PROCESS_AFFINITY_DWORD_PTR *)(uintptr_t)0xFFFFFFFFFFFFFFFCULL,
        &g_facts, &g_memory, &report);
    check_status("SYSTEM_POINTER_RANGE_OVERFLOW", status,
                 GXOS_PROCESS_AFFINITY_STATUS_RANGE_OVERFLOW);
    mutant = g_facts;
    mutant.supported_process_handle = 0x1234567887654321ULL;
    status = gxos_get_process_affinity_mask_checked(
        GXOS_PROCESS_AFFINITY_CURRENT_PROCESS, &value.process_mask,
        &value.system_mask, &mutant, &g_memory, &report);
    check_status("FACT_SUPPORTED_HANDLE_MISMATCH", status,
                 GXOS_PROCESS_AFFINITY_STATUS_INVALID_PROCESS_HANDLE);
}

static void test_fact_controls(void)
{
    AFFINITY_GUARDED value;
    GXOS_PROCESS_AFFINITY_REPORT report;
    GXOS_PROCESS_AFFINITY_FACTS mutant;
    GXOS_PROCESS_AFFINITY_STATUS status;

    configure_facts(1, 1, 1, 1);
    prepare(&value, 0xAAAAAAAAAAAAAAAAULL, 0xBBBBBBBBBBBBBBBBULL, 1);
    mutant = g_facts;
    mutant.process_affinity_mask = 0;
    status = gxos_get_process_affinity_mask_checked(
        GXOS_PROCESS_AFFINITY_CURRENT_PROCESS, &value.process_mask,
        &value.system_mask, &mutant, &g_memory, &report);
    check_status("ZERO_PROCESS_MASK", status,
                 GXOS_PROCESS_AFFINITY_STATUS_ZERO_PROCESS_MASK);
    mutant = g_facts;
    mutant.system_affinity_mask = 0;
    status = gxos_get_process_affinity_mask_checked(
        GXOS_PROCESS_AFFINITY_CURRENT_PROCESS, &value.process_mask,
        &value.system_mask, &mutant, &g_memory, &report);
    check_status("ZERO_SYSTEM_MASK", status,
                 GXOS_PROCESS_AFFINITY_STATUS_ZERO_SYSTEM_MASK);
    mutant = g_facts;
    mutant.process_affinity_mask = 2;
    status = gxos_get_process_affinity_mask_checked(
        GXOS_PROCESS_AFFINITY_CURRENT_PROCESS, &value.process_mask,
        &value.system_mask, &mutant, &g_memory, &report);
    check_status("PROCESS_NOT_SUBSET", status,
                 GXOS_PROCESS_AFFINITY_STATUS_PROCESS_NOT_SUBSET);
    mutant = g_facts;
    mutant.system_affinity_mask = 2;
    mutant.system_info_active_processor_mask = 2;
    mutant.usable_processor_mask = 2;
    status = gxos_get_process_affinity_mask_checked(
        GXOS_PROCESS_AFFINITY_CURRENT_PROCESS, &value.process_mask,
        &value.system_mask, &mutant, &g_memory, &report);
    check_status("FABRICATED_PROCESSOR_BIT", status,
                 GXOS_PROCESS_AFFINITY_STATUS_PROCESS_NOT_SUBSET);
    mutant = g_facts;
    mutant.topology_policy = 99;
    status = gxos_get_process_affinity_mask_checked(
        GXOS_PROCESS_AFFINITY_CURRENT_PROCESS, &value.process_mask,
        &value.system_mask, &mutant, &g_memory, &report);
    check_status("UNSUPPORTED_TOPOLOGY", status,
                 GXOS_PROCESS_AFFINITY_STATUS_UNSUPPORTED_TOPOLOGY);
    mutant = g_facts;
    mutant.usable_processor_count = 0;
    status = gxos_get_process_affinity_mask_checked(
        GXOS_PROCESS_AFFINITY_CURRENT_PROCESS, &value.process_mask,
        &value.system_mask, &mutant, &g_memory, &report);
    check_status("ZERO_PROCESSOR_COUNT", status,
                 GXOS_PROCESS_AFFINITY_STATUS_PROCESSOR_COUNT_MISMATCH);
    mutant = g_facts;
    mutant.usable_processor_count = 65;
    status = gxos_get_process_affinity_mask_checked(
        GXOS_PROCESS_AFFINITY_CURRENT_PROCESS, &value.process_mask,
        &value.system_mask, &mutant, &g_memory, &report);
    check_status("PROCESSOR_COUNT_OVER_64", status,
                 GXOS_PROCESS_AFFINITY_STATUS_PROCESSOR_COUNT_MISMATCH);
    mutant = g_facts;
    mutant.processor_group_count = 0;
    status = gxos_get_process_affinity_mask_checked(
        GXOS_PROCESS_AFFINITY_CURRENT_PROCESS, &value.process_mask,
        &value.system_mask, &mutant, &g_memory, &report);
    check_status("GROUP_COUNT_MISMATCH", status,
                 GXOS_PROCESS_AFFINITY_STATUS_GROUP_POLICY_MISMATCH);
    mutant = g_facts;
    mutant.current_group_number = 1;
    status = gxos_get_process_affinity_mask_checked(
        GXOS_PROCESS_AFFINITY_CURRENT_PROCESS, &value.process_mask,
        &value.system_mask, &mutant, &g_memory, &report);
    check_status("GROUP_NUMBER_MISMATCH", status,
                 GXOS_PROCESS_AFFINITY_STATUS_GROUP_POLICY_MISMATCH);
    mutant = g_facts;
    mutant.system_info_processor_count = 2;
    status = gxos_get_process_affinity_mask_checked(
        GXOS_PROCESS_AFFINITY_CURRENT_PROCESS, &value.process_mask,
        &value.system_mask, &mutant, &g_memory, &report);
    check_status("SYSTEM_INFO_COUNT_MISMATCH", status,
                 GXOS_PROCESS_AFFINITY_STATUS_SYSTEM_SNAPSHOT_MISMATCH);
    mutant = g_facts;
    mutant.system_info_active_processor_mask = 2;
    status = gxos_get_process_affinity_mask_checked(
        GXOS_PROCESS_AFFINITY_CURRENT_PROCESS, &value.process_mask,
        &value.system_mask, &mutant, &g_memory, &report);
    check_status("SYSTEM_INFO_MASK_MISMATCH", status,
                 GXOS_PROCESS_AFFINITY_STATUS_SYSTEM_SNAPSHOT_MISMATCH);
    mutant = g_facts;
    mutant.usable_processor_mask = 2;
    status = gxos_get_process_affinity_mask_checked(
        GXOS_PROCESS_AFFINITY_CURRENT_PROCESS, &value.process_mask,
        &value.system_mask, &mutant, &g_memory, &report);
    check_status("USABLE_MASK_MISMATCH", status,
                 GXOS_PROCESS_AFFINITY_STATUS_SYSTEM_SNAPSHOT_MISMATCH);
}

static void test_memory_and_synthetic_controls(void)
{
    AFFINITY_GUARDED value;
    GXOS_PROCESS_AFFINITY_REPORT report;
    GXOS_PROCESS_AFFINITY_FACTS mutant;
    GXOS_PROCESS_AFFINITY_STATUS status;
    uint64_t process_before;
    uint64_t system_before;

    configure_facts(1, 1, 1, 1);
    prepare(&value, 0x1111111111111111ULL, 0x2222222222222222ULL, 1);
    process_before = value.process_mask;
    system_before = value.system_mask;
    status = gxos_get_process_affinity_mask_checked(
        GXOS_PROCESS_AFFINITY_CURRENT_PROCESS, &value.process_mask,
        &value.system_mask, &g_facts, 0, &report);
    check_status("NULL_MEMORY_CONTEXT", status,
                 GXOS_PROCESS_AFFINITY_STATUS_INVALID_MEMORY_CONTEXT);
    check("OUTPUTS_UNCHANGED_ON_FAILURE", value.process_mask == process_before &&
                                           value.system_mask == system_before);
    g_memory.region_count = 0;
    status = call_checked(&value, &value.process_mask, &value.system_mask, &report);
    check_status("EMPTY_MEMORY_CONTEXT", status,
                 GXOS_PROCESS_AFFINITY_STATUS_INVALID_MEMORY_CONTEXT);
    configure_memory((uintptr_t)&value, (uintptr_t)&value.process_mask + 4U, 1, 1);
    status = call_checked(&value, &value.process_mask, &value.system_mask, &report);
    check_status("CROSS_REGION_PROCESS_RANGE", status,
                 GXOS_PROCESS_AFFINITY_STATUS_UNWRITABLE_PROCESS_MASK);
    configure_memory((uintptr_t)&value, (uintptr_t)&value + sizeof(value), 1, 1);
    mutant = g_facts;
    mutant.process_affinity_mask = 2;
    mutant.system_affinity_mask = 1;
    status = gxos_get_process_affinity_mask_checked(
        GXOS_PROCESS_AFFINITY_CURRENT_PROCESS, &value.process_mask,
        &value.system_mask, &mutant, &g_memory, &report);
    check_status("FABRICATED_PROCESS_MASK", status,
                 GXOS_PROCESS_AFFINITY_STATUS_PROCESS_NOT_SUBSET);
    configure_facts(1, 3, 3, 2);
    prepare(&value, 0xAAAAAAAAAAAAAAAAULL, 0xBBBBBBBBBBBBBBBBULL, 1);
    status = call_checked(&value, &value.process_mask, &value.system_mask, &report);
    check_status("SYNTHETIC_SUBSET_SUCCESS", status,
                 GXOS_PROCESS_AFFINITY_STATUS_OK);
    check("SYNTHETIC_SUBSET_OUTPUTS", value.process_mask == 1 &&
                                        value.system_mask == 3);
    configure_facts(3, 3, 3, 2);
    prepare(&value, 0, 0, 1);
    status = call_checked(&value, &value.process_mask, &value.system_mask, &report);
    check_status("SYNTHETIC_FULL_SUCCESS", status,
                 GXOS_PROCESS_AFFINITY_STATUS_OK);
    check("SYNTHETIC_FULL_OUTPUTS", value.process_mask == 3 &&
                                      value.system_mask == 3);
    configure_facts(2, 1, 1, 1);
    prepare(&value, 0xAAAAAAAAAAAAAAAAULL, 0xBBBBBBBBBBBBBBBBULL, 1);
    status = call_checked(&value, &value.process_mask, &value.system_mask, &report);
    check_status("SYNTHETIC_NON_SUBSET_FAILURE", status,
                 GXOS_PROCESS_AFFINITY_STATUS_PROCESS_NOT_SUBSET);
    check("SYNTHETIC_FAILURE_NO_PARTIAL_WRITE", value.process_mask ==
             0xAAAAAAAAAAAAAAAAULL && value.system_mask == 0xBBBBBBBBBBBBBBBBULL);
    configure_facts(1, 1, 1, 1);
    prepare(&value, 0xA5A5A5A5A5A5A5A5ULL, 0x5A5A5A5A5A5A5A5AULL, 1);
    status = call_checked(&value, &value.process_mask, &value.system_mask, &report);
    check_status("CORE_DOES_NOT_TOUCH_LAST_ERROR", status,
                 GXOS_PROCESS_AFFINITY_STATUS_OK);
    check("OUTPUT_PUBLISH_ORDER_COMPLETE", value.process_mask == 1 &&
                                             value.system_mask == 1 &&
                                             report.process_written == 1 &&
                                             report.system_written == 1);
    check("OUTPUT_GUARDS_AFTER_SYNTHETIC", guards_ok(&value));
    check("MASK_POPULATION_HELPER_EXPECTATION", population(value.process_mask) == 1 &&
                                                 population(value.system_mask) == 1);
}

int main(void)
{
    test_widths_and_success();
    test_pointer_and_handle_controls();
    test_fact_controls();
    test_memory_and_synthetic_controls();
    printf("PROCESS_AFFINITY_HOST_TEST_COUNT=%u\n", g_test_count);
    printf("PROCESS_AFFINITY_HOST_TEST_FAILURE_COUNT=%u\n", g_failure_count);
    if (g_failure_count != 0) return 1;
    printf("PROCESS_AFFINITY_HOST_TESTS=PASSED\n");
    return 0;
}

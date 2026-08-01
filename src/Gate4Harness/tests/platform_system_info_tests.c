#include <stdint.h>
#include <stdio.h>
#include <string.h>

#include "../platform_system_info.h"

typedef struct {
    uint8_t before[16];
    GXOS_SYSTEM_INFO value;
    uint8_t after[16];
} SYSTEM_INFO_GUARDED;

static GXOS_SYSTEM_FACTS g_facts;
static GXOS_SYSTEM_INFO_MEMORY_REGION g_regions[2];
static GXOS_SYSTEM_INFO_MEMORY_CONTEXT g_memory;

static void configure_facts(void)
{
    memset(&g_facts, 0, sizeof(g_facts));
    g_facts.processor_architecture = GXOS_SYSTEM_INFO_PROCESSOR_ARCHITECTURE_AMD64;
    g_facts.page_size = 0x1000;
    g_facts.minimum_application_address = 0x100000;
    g_facts.maximum_application_address = 0x1FFFFF;
    g_facts.active_processor_mask = 1;
    g_facts.number_of_processors = 1;
    g_facts.processor_type = GXOS_SYSTEM_INFO_PROCESSOR_TYPE_AMD_X8664;
    g_facts.allocation_granularity = 0x1000;
    g_facts.processor_level = 0;
    g_facts.processor_revision = 0;
    g_facts.address_range_policy = GXOS_SYSTEM_INFO_ADDRESS_RANGE_IMAGE_BACKED;
}

static void configure_memory(uintptr_t base, uintptr_t end, uint32_t writable)
{
    g_regions[0].base = base;
    g_regions[0].end = end;
    g_regions[0].readable = 1;
    g_regions[0].writable = writable;
    g_memory.region_count = 1;
    g_memory.regions = g_regions;
}

static int expect_status(const char *name,
                         GXOS_SYSTEM_INFO_STATUS actual,
                         GXOS_SYSTEM_INFO_STATUS expected)
{
    if (actual != expected) {
        printf("SYSTEM_INFO_TEST_FAILURE=%s actual=%u expected=%u\n",
               name, (unsigned)actual, (unsigned)expected);
        return 1;
    }
    printf("SYSTEM_INFO_TEST_%s=PASS\n", name);
    return 0;
}

static int valid_call(GXOS_SYSTEM_INFO *destination)
{
    return (int)gxos_get_system_info_checked(destination, &g_facts, &g_memory);
}

static int test_layout(void)
{
    if (sizeof(GXOS_SYSTEM_INFO) != 0x30 || _Alignof(GXOS_SYSTEM_INFO) != 8 ||
        offsetof(GXOS_SYSTEM_INFO, architecture_union) != 0 ||
        offsetof(GXOS_SYSTEM_INFO, dwPageSize) != 4 ||
        offsetof(GXOS_SYSTEM_INFO, lpMinimumApplicationAddress) != 8 ||
        offsetof(GXOS_SYSTEM_INFO, lpMaximumApplicationAddress) != 16 ||
        offsetof(GXOS_SYSTEM_INFO, dwActiveProcessorMask) != 24 ||
        offsetof(GXOS_SYSTEM_INFO, dwNumberOfProcessors) != 32 ||
        offsetof(GXOS_SYSTEM_INFO, dwProcessorType) != 36 ||
        offsetof(GXOS_SYSTEM_INFO, dwAllocationGranularity) != 40 ||
        offsetof(GXOS_SYSTEM_INFO, wProcessorLevel) != 44 ||
        offsetof(GXOS_SYSTEM_INFO, wProcessorRevision) != 46 ||
        sizeof(GXOS_SYSTEM_INFO_ARCHITECTURE_UNION) != 4 ||
        offsetof(GXOS_SYSTEM_INFO_ARCHITECTURE_UNION, architecture.wProcessorArchitecture) != 0 ||
        offsetof(GXOS_SYSTEM_INFO_ARCHITECTURE_UNION, architecture.wReserved) != 2) {
        printf("SYSTEM_INFO_TEST_FAILURE=layout\n");
        return 1;
    }
    printf("SYSTEM_INFO_TEST_LAYOUT=PASS\n");
    return 0;
}

static int test_complete_population(void)
{
    SYSTEM_INFO_GUARDED guarded;
    SYSTEM_INFO_GUARDED second;
    GXOS_SYSTEM_FACTS before = g_facts;
    uint8_t expected_before[sizeof(guarded.before)];
    uint8_t expected_after[sizeof(guarded.after)];
    uint32_t index;
    memset(&guarded, 0xA5, sizeof(guarded));
    memset(&second, 0xA5, sizeof(second));
    memset(expected_before, 0xA5, sizeof(expected_before));
    memset(expected_after, 0xA5, sizeof(expected_after));
    configure_memory((uintptr_t)&guarded.value,
                     (uintptr_t)&guarded.value + sizeof(guarded.value), 1);
    if (valid_call(&guarded.value) != GXOS_SYSTEM_INFO_STATUS_OK ||
        valid_call(&second.value) != GXOS_SYSTEM_INFO_STATUS_UNWRITABLE_POINTER) return 1;
    configure_memory((uintptr_t)&second.value,
                     (uintptr_t)&second.value + sizeof(second.value), 1);
    if (valid_call(&second.value) != GXOS_SYSTEM_INFO_STATUS_OK) return 1;
    if (memcmp(guarded.before, expected_before, sizeof(guarded.before)) != 0 ||
        memcmp(guarded.after, expected_after, sizeof(guarded.after)) != 0) {
        printf("SYSTEM_INFO_TEST_DEBUG=guard\n");
        printf("SYSTEM_INFO_TEST_FAILURE=guard\n");
        return 1;
    }
    for (index = 0; index != sizeof(guarded.value); index++) {
        if (((const uint8_t *)&guarded.value)[index] == 0xA5) {
            printf("SYSTEM_INFO_TEST_FAILURE=poison-byte-%u\n", index);
            return 1;
        }
    }
    if (guarded.value.architecture_union.architecture.wProcessorArchitecture != 9 ||
        guarded.value.architecture_union.architecture.wReserved != 0 ||
        guarded.value.architecture_union.dwOemId != 9 ||
        guarded.value.dwPageSize != 0x1000 ||
        guarded.value.lpMinimumApplicationAddress != (void *)0x100000 ||
        guarded.value.lpMaximumApplicationAddress != (void *)0x1FFFFF ||
        guarded.value.dwActiveProcessorMask != 1 ||
        guarded.value.dwNumberOfProcessors != 1 ||
        guarded.value.dwProcessorType != 8664 ||
        guarded.value.dwAllocationGranularity != 0x1000 ||
        guarded.value.wProcessorLevel != 0 ||
        guarded.value.wProcessorRevision != 0 ||
        memcmp(&guarded.value, &second.value, sizeof(guarded.value)) != 0 ||
        memcmp(&before, &g_facts, sizeof(before)) != 0) {
        printf("SYSTEM_INFO_TEST_FAILURE=population\n");
        return 1;
    }
    printf("SYSTEM_INFO_TEST_COMPLETE_INITIALIZATION=PASS\n");
    printf("SYSTEM_INFO_TEST_GUARD=PASS\n");
    printf("SYSTEM_INFO_TEST_REPEATABILITY=PASS\n");
    printf("SYSTEM_INFO_TEST_FACTS_PRESERVED=PASS\n");
    return 0;
}

static int test_negative_cases(void)
{
    GXOS_SYSTEM_INFO destination;
    GXOS_SYSTEM_FACTS mutant;
    GXOS_SYSTEM_INFO_MEMORY_REGION saved_region;
    GXOS_SYSTEM_INFO_STATUS status;
    int failures = 0;

    memset(&destination, 0xA5, sizeof(destination));
    configure_memory((uintptr_t)&destination,
                     (uintptr_t)&destination + sizeof(destination), 1);
    saved_region = g_regions[0];
    failures += expect_status("NULL_DESTINATION",
        gxos_get_system_info_checked(0, &g_facts, &g_memory),
        GXOS_SYSTEM_INFO_STATUS_NULL_POINTER);
    failures += expect_status("NONCANONICAL_DESTINATION",
        gxos_get_system_info_checked((GXOS_SYSTEM_INFO *)(uintptr_t)0x0000800000000000ULL,
                                     &g_facts, &g_memory),
        GXOS_SYSTEM_INFO_STATUS_NONCANONICAL_POINTER);
    g_regions[0].writable = 0;
    failures += expect_status("READ_ONLY_DESTINATION", valid_call(&destination),
                              GXOS_SYSTEM_INFO_STATUS_UNWRITABLE_POINTER);
    g_regions[0] = saved_region;
    g_regions[0].end = (uintptr_t)&destination + sizeof(destination) - 1U;
    failures += expect_status("UNDERSIZED_DESTINATION", valid_call(&destination),
                              GXOS_SYSTEM_INFO_STATUS_UNWRITABLE_POINTER);
    g_regions[0] = saved_region;
    failures += expect_status("POINTER_OVERFLOW",
        gxos_get_system_info_checked((GXOS_SYSTEM_INFO *)(UINTPTR_MAX - 0x2FULL),
                                     &g_facts, &g_memory),
        GXOS_SYSTEM_INFO_STATUS_INSUFFICIENT_WRITABLE_RANGE);

    mutant = g_facts;
    mutant.processor_architecture = 0;
    failures += expect_status("INVALID_ARCHITECTURE",
        gxos_get_system_info_checked(&destination, &mutant, &g_memory),
        GXOS_SYSTEM_INFO_STATUS_INVALID_ARCHITECTURE);
    mutant = g_facts;
    mutant.page_size = 0;
    failures += expect_status("ZERO_PAGE_SIZE",
        gxos_get_system_info_checked(&destination, &mutant, &g_memory),
        GXOS_SYSTEM_INFO_STATUS_INVALID_PAGE_SIZE);
    mutant.page_size = 0x1800;
    failures += expect_status("NON_POWER_OF_TWO_PAGE_SIZE",
        gxos_get_system_info_checked(&destination, &mutant, &g_memory),
        GXOS_SYSTEM_INFO_STATUS_INVALID_PAGE_SIZE);
    mutant = g_facts;
    mutant.allocation_granularity = 0;
    failures += expect_status("ZERO_ALLOCATION_GRANULARITY",
        gxos_get_system_info_checked(&destination, &mutant, &g_memory),
        GXOS_SYSTEM_INFO_STATUS_INVALID_ALLOCATION_GRANULARITY);
    mutant.allocation_granularity = 0x800;
    failures += expect_status("SMALL_ALLOCATION_GRANULARITY",
        gxos_get_system_info_checked(&destination, &mutant, &g_memory),
        GXOS_SYSTEM_INFO_STATUS_INVALID_ALLOCATION_GRANULARITY);
    mutant.allocation_granularity = 0x3000;
    failures += expect_status("NON_DIVISIBLE_ALLOCATION_GRANULARITY",
        gxos_get_system_info_checked(&destination, &mutant, &g_memory),
        GXOS_SYSTEM_INFO_STATUS_INVALID_ALLOCATION_GRANULARITY);
    mutant = g_facts;
    mutant.number_of_processors = 0;
    failures += expect_status("ZERO_PROCESSOR_COUNT",
        gxos_get_system_info_checked(&destination, &mutant, &g_memory),
        GXOS_SYSTEM_INFO_STATUS_INVALID_PROCESSOR_COUNT);
    mutant = g_facts;
    mutant.active_processor_mask = 0;
    failures += expect_status("ZERO_PROCESSOR_MASK",
        gxos_get_system_info_checked(&destination, &mutant, &g_memory),
        GXOS_SYSTEM_INFO_STATUS_INVALID_PROCESSOR_MASK);
    mutant = g_facts;
    mutant.number_of_processors = 2;
    failures += expect_status("MASK_COUNT_MISMATCH",
        gxos_get_system_info_checked(&destination, &mutant, &g_memory),
        GXOS_SYSTEM_INFO_STATUS_INVALID_PROCESSOR_MASK);
    mutant = g_facts;
    mutant.minimum_application_address = mutant.maximum_application_address + 1;
    failures += expect_status("INVERTED_APPLICATION_RANGE",
        gxos_get_system_info_checked(&destination, &mutant, &g_memory),
        GXOS_SYSTEM_INFO_STATUS_INVALID_ADDRESS_RANGE);
    mutant = g_facts;
    mutant.minimum_application_address = 0x0000800000000000ULL;
    failures += expect_status("NONCANONICAL_APPLICATION_RANGE",
        gxos_get_system_info_checked(&destination, &mutant, &g_memory),
        GXOS_SYSTEM_INFO_STATUS_INVALID_ADDRESS_RANGE);
    mutant = g_facts;
    mutant.address_range_policy = 0;
    failures += expect_status("UNSUPPORTED_APPLICATION_RANGE",
        gxos_get_system_info_checked(&destination, &mutant, &g_memory),
        GXOS_SYSTEM_INFO_STATUS_INVALID_ADDRESS_RANGE);
    status = gxos_system_info_configure(&g_facts, &g_memory);
    failures += expect_status("CONFIGURE_VALID_SNAPSHOT", status,
                              GXOS_SYSTEM_INFO_STATUS_OK);
    if (failures != 0) return 1;
    printf("SYSTEM_INFO_TEST_NEGATIVE_CONTROLS=PASS\n");
    return 0;
}

static void GXOS_SYSTEM_INFO_MS_ABI test_pe_wrapper(GXOS_SYSTEM_INFO *destination)
{
    (void)gxos_get_system_info_checked(destination, &g_facts, &g_memory);
}

static int test_abi(void)
{
    GXOS_SYSTEM_INFO destination;
    void (GXOS_SYSTEM_INFO_MS_ABI *wrapper)(GXOS_SYSTEM_INFO *) = test_pe_wrapper;
    configure_memory((uintptr_t)&destination,
                     (uintptr_t)&destination + sizeof(destination), 1);
    memset(&destination, 0xA5, sizeof(destination));
    wrapper(&destination);
    if (destination.dwPageSize != 0x1000 || destination.dwNumberOfProcessors != 1) {
        printf("SYSTEM_INFO_TEST_FAILURE=abi\n");
        return 1;
    }
    printf("SYSTEM_INFO_TEST_MS_ABI=PASS\n");
    return 0;
}

int main(void)
{
    configure_facts();
    if (test_layout() != 0 || test_complete_population() != 0 ||
        test_negative_cases() != 0 || test_abi() != 0) return 1;
    printf("SYSTEM_INFO_HOST_TESTS=PASSED\n");
    return 0;
}

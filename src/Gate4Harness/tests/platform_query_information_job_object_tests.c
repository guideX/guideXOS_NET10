#include <stdint.h>
#include <stdio.h>
#include <string.h>

#include "platform_query_information_job_object.h"

_Static_assert(sizeof(GXOS_QUERY_JOB_BOOL) == 4, "BOOL width");
_Static_assert(sizeof(GXOS_QUERY_JOB_HANDLE) == 8, "HANDLE width");
_Static_assert(sizeof(GXOS_QUERY_JOB_INFO_CLASS) == 4, "enum width");
_Static_assert(sizeof(GXOS_QUERY_JOB_DWORD) == 4, "DWORD width");
_Static_assert(sizeof(GXOS_QUERY_JOB_OUTPUT) == 8, "LPVOID width");
_Static_assert(sizeof(GXOS_QUERY_JOB_RETURN_LENGTH) == 8, "LPDWORD width");

static unsigned failures;

static void expect(int condition, const char *name)
{
    if (!condition) {
        ++failures;
        printf("FAIL:%s\n", name);
    }
}

static GXOS_SYSTEM_INFO_MEMORY_CONTEXT memory_for(
    GXOS_SYSTEM_INFO_MEMORY_REGION *regions,
    uint32_t count)
{
    GXOS_SYSTEM_INFO_MEMORY_CONTEXT memory;
    memory.region_count = count;
    memory.regions = regions;
    return memory;
}

static GXOS_QUERY_JOB_FACTS no_job_facts(void)
{
    GXOS_QUERY_JOB_FACTS facts;
    memset(&facts, 0, sizeof(facts));
    facts.supported_job_handle = GXOS_QUERY_JOB_CURRENT_HANDLE;
    return facts;
}

static GXOS_QUERY_JOB_FACTS associated_facts(void)
{
    GXOS_QUERY_JOB_FACTS facts = no_job_facts();
    facts.associated_job = 1;
    return facts;
}

static void initialize_output(uint8_t *output, size_t count, uint8_t value)
{
    size_t index;
    for (index = 0; index != count; ++index) output[index] = value;
}

static GXOS_QUERY_JOB_STATUS query_status(
    GXOS_QUERY_JOB_HANDLE handle,
    GXOS_QUERY_JOB_INFO_CLASS class_value,
    void *output,
    uint32_t output_length,
    uint32_t *return_length,
    const GXOS_QUERY_JOB_FACTS *facts,
    const GXOS_SYSTEM_INFO_MEMORY_CONTEXT *memory,
    GXOS_QUERY_JOB_REPORT *report)
{
    return gxos_query_information_job_object_checked(
        handle, class_value, output, output_length, return_length, facts,
        memory, report);
}

static void test_no_job_failure_preserves_output(void)
{
    uint8_t output[16];
    uint8_t before[16];
    GXOS_SYSTEM_INFO_MEMORY_REGION region;
    GXOS_SYSTEM_INFO_MEMORY_CONTEXT memory;
    GXOS_QUERY_JOB_FACTS facts = no_job_facts();
    GXOS_QUERY_JOB_REPORT report;
    GXOS_QUERY_JOB_STATUS status;

    initialize_output(output, sizeof(output), 0xCC);
    memcpy(before, output, sizeof(output));
    region.base = (uintptr_t)output;
    region.end = region.base + sizeof(output);
    region.readable = 1;
    region.writable = 1;
    memory = memory_for(&region, 1);
    status = query_status(0, GXOS_QUERY_JOB_CPU_RATE_CLASS, output, 8, 0,
                          &facts, &memory, &report);
    expect(status == GXOS_QUERY_JOB_STATUS_NO_ASSOCIATED_JOB,
           "no-job status");
    expect(memcmp(output, before, sizeof(output)) == 0,
           "no-job output preservation");
    expect(report.output_written == 0 && report.return_length_written == 0,
           "no-job no publication");
    expect(report.output_before_low == 0xCCCCCCCCU &&
               report.output_before_high == 0xCCCCCCCCU &&
               report.output_after_low == 0xCCCCCCCCU &&
               report.output_after_high == 0xCCCCCCCCU,
           "no-job output bytes preserved");
}

static void test_no_limit_success(void)
{
    uint8_t output[16];
    uint32_t return_length = 0xA5A5A5A5U;
    GXOS_SYSTEM_INFO_MEMORY_REGION regions[2];
    GXOS_SYSTEM_INFO_MEMORY_CONTEXT memory;
    GXOS_QUERY_JOB_FACTS facts = associated_facts();
    GXOS_QUERY_JOB_REPORT report;
    GXOS_QUERY_JOB_STATUS status;

    initialize_output(output, sizeof(output), 0xCC);
    regions[0].base = (uintptr_t)output;
    regions[0].end = regions[0].base + sizeof(output);
    regions[0].readable = 1;
    regions[0].writable = 1;
    regions[1].base = (uintptr_t)&return_length;
    regions[1].end = regions[1].base + sizeof(return_length);
    regions[1].readable = 1;
    regions[1].writable = 1;
    memory = memory_for(regions, 2);
    status = query_status(0, GXOS_QUERY_JOB_CPU_RATE_CLASS, output,
                          (uint32_t)sizeof(output), &return_length, &facts,
                          &memory, &report);
    expect(status == GXOS_QUERY_JOB_STATUS_OK, "no-limit success status");
    expect(output[0] == 0 && output[1] == 0 && output[2] == 0 &&
               output[3] == 0 && output[4] == 0 && output[5] == 0 &&
               output[6] == 0 && output[7] == 0,
           "no-limit complete output");
    expect(return_length == 8, "no-limit return length");
    expect(output[8] == 0xCC && output[15] == 0xCC,
           "no-limit guard bytes");
    expect(report.output_written == 1 && report.return_length_written == 1,
           "no-limit publication report");
}

static void test_active_structures(void)
{
    uint8_t output[16];
    uint32_t return_length;
    GXOS_SYSTEM_INFO_MEMORY_REGION regions[2];
    GXOS_SYSTEM_INFO_MEMORY_CONTEXT memory;
    GXOS_QUERY_JOB_FACTS facts = associated_facts();
    GXOS_QUERY_JOB_REPORT report;

    regions[0].base = (uintptr_t)output;
    regions[0].end = regions[0].base + sizeof(output);
    regions[0].readable = 1;
    regions[0].writable = 1;
    regions[1].base = (uintptr_t)&return_length;
    regions[1].end = regions[1].base + sizeof(return_length);
    regions[1].readable = 1;
    regions[1].writable = 1;
    memory = memory_for(regions, 2);

    initialize_output(output, sizeof(output), 0xCC);
    facts.control_flags = GXOS_QUERY_JOB_CPU_RATE_ENABLE |
                          GXOS_QUERY_JOB_CPU_RATE_HARD_CAP;
    facts.cpu_rate = 5000;
    return_length = 0;
    expect(query_status(0, GXOS_QUERY_JOB_CPU_RATE_CLASS, output, 8,
                        &return_length, &facts, &memory, &report) ==
               GXOS_QUERY_JOB_STATUS_OK,
           "hard-cap success");
    expect(*(uint32_t *)(void *)output == 5 &&
               *(uint32_t *)(void *)(output + 4) == 5000,
           "hard-cap structure fields");
    expect(return_length == 8, "hard-cap return length");

    initialize_output(output, sizeof(output), 0xCC);
    facts = associated_facts();
    facts.control_flags = GXOS_QUERY_JOB_CPU_RATE_ENABLE |
                          GXOS_QUERY_JOB_CPU_RATE_MIN_MAX;
    facts.min_rate = 2500;
    facts.max_rate = 7500;
    return_length = 0;
    expect(query_status(0, GXOS_QUERY_JOB_CPU_RATE_CLASS, output, 8,
                        &return_length, &facts, &memory, &report) ==
               GXOS_QUERY_JOB_STATUS_OK,
           "min-max success");
    expect(*(uint32_t *)(void *)output == 0x11 &&
               *(uint16_t *)(void *)(output + 4) == 2500 &&
               *(uint16_t *)(void *)(output + 6) == 7500,
           "min-max structure fields");

    initialize_output(output, sizeof(output), 0xCC);
    facts = associated_facts();
    facts.control_flags = GXOS_QUERY_JOB_CPU_RATE_ENABLE |
                          GXOS_QUERY_JOB_CPU_RATE_WEIGHT_BASED;
    facts.weight = 5;
    return_length = 0;
    expect(query_status(0, GXOS_QUERY_JOB_CPU_RATE_CLASS, output, 8,
                        &return_length, &facts, &memory, &report) ==
               GXOS_QUERY_JOB_STATUS_OK,
           "weight success");
    expect(*(uint32_t *)(void *)output == 3 &&
               *(uint32_t *)(void *)(output + 4) == 5,
           "weight structure fields");
}

static void test_argument_and_pointer_rejection(void)
{
    uint8_t output[16];
    uint8_t small_output[4];
    uint32_t return_length = 0xA5A5A5A5U;
    uint32_t read_only_return_length = 0xA5A5A5A5U;
    GXOS_SYSTEM_INFO_MEMORY_REGION regions[3];
    GXOS_SYSTEM_INFO_MEMORY_CONTEXT memory;
    GXOS_QUERY_JOB_FACTS facts = associated_facts();
    GXOS_QUERY_JOB_REPORT report;

    regions[0].base = (uintptr_t)output;
    regions[0].end = regions[0].base + sizeof(output);
    regions[0].readable = 1;
    regions[0].writable = 1;
    regions[1].base = (uintptr_t)&return_length;
    regions[1].end = regions[1].base + sizeof(return_length);
    regions[1].readable = 1;
    regions[1].writable = 1;
    regions[2].base = (uintptr_t)&read_only_return_length;
    regions[2].end = regions[2].base + sizeof(read_only_return_length);
    regions[2].readable = 1;
    regions[2].writable = 0;
    memory = memory_for(regions, 3);

    expect(query_status(0, GXOS_QUERY_JOB_CPU_RATE_CLASS, output, 7, 0,
                        &facts, &memory, &report) ==
               GXOS_QUERY_JOB_STATUS_INSUFFICIENT_OUTPUT,
           "one-byte-too-small buffer");
    expect(query_status(0, GXOS_QUERY_JOB_CPU_RATE_CLASS, 0, 8, 0, &facts,
                        &memory, &report) == GXOS_QUERY_JOB_STATUS_NULL_OUTPUT,
           "null output");
    expect(query_status(0, GXOS_QUERY_JOB_CPU_RATE_CLASS,
                        (void *)(uintptr_t)0x0001000000000000ULL, 8, 0,
                        &facts, &memory, &report) ==
               GXOS_QUERY_JOB_STATUS_NONCANONICAL_OUTPUT,
           "noncanonical output");
    regions[0].end = regions[0].base + 4;
    expect(query_status(0, GXOS_QUERY_JOB_CPU_RATE_CLASS, output, 8, 0,
                        &facts, &memory, &report) ==
               GXOS_QUERY_JOB_STATUS_UNWRITABLE_OUTPUT,
           "undersized writable output range");
    regions[0].end = regions[0].base + sizeof(output);
    regions[0].writable = 0;
    expect(query_status(0, GXOS_QUERY_JOB_CPU_RATE_CLASS, output, 8, 0,
                        &facts, &memory, &report) ==
               GXOS_QUERY_JOB_STATUS_UNWRITABLE_OUTPUT,
           "read-only output");
    regions[0].writable = 1;
    expect(query_status(0, GXOS_QUERY_JOB_CPU_RATE_CLASS, output, 8,
                        (uint32_t *)(void *)(output + 2), &facts, &memory,
                        &report) == GXOS_QUERY_JOB_STATUS_ALIASED_OUTPUTS,
           "output return-length alias");
    expect(query_status(0, GXOS_QUERY_JOB_CPU_RATE_CLASS, output, 8,
                        (uint32_t *)(uintptr_t)0x0001000000000000ULL,
                        &facts, &memory, &report) ==
               GXOS_QUERY_JOB_STATUS_NONCANONICAL_RETURN_LENGTH,
           "noncanonical return length");
    expect(query_status(0, GXOS_QUERY_JOB_CPU_RATE_CLASS, output, 8,
                        &read_only_return_length, &facts, &memory, &report) ==
               GXOS_QUERY_JOB_STATUS_UNWRITABLE_RETURN_LENGTH,
           "read-only return length");
    expect(query_status(0, GXOS_QUERY_JOB_CPU_RATE_CLASS, small_output, 8, 0,
                        &facts, &memory, &report) ==
               GXOS_QUERY_JOB_STATUS_UNWRITABLE_OUTPUT,
           "unlisted undersized output");
    expect(query_status(0, GXOS_QUERY_JOB_CPU_RATE_CLASS,
                        (void *)(UINTPTR_MAX - 3U), 8, 0, &facts, &memory,
                        &report) == GXOS_QUERY_JOB_STATUS_RANGE_OVERFLOW,
           "output pointer arithmetic overflow");
    expect(query_status(0xFFFFFFFFFFFFFFFFULL, GXOS_QUERY_JOB_CPU_RATE_CLASS,
                        output, 8, 0, &facts, &memory, &report) ==
               GXOS_QUERY_JOB_STATUS_INVALID_HANDLE,
           "full-width unsupported handle");
    expect(query_status(0x00000000FFFFFFFFULL, GXOS_QUERY_JOB_CPU_RATE_CLASS,
                        output, 8, 0, &facts, &memory, &report) ==
               GXOS_QUERY_JOB_STATUS_INVALID_HANDLE,
           "handle truncation rejected");
    expect(query_status(0, GXOS_QUERY_JOB_CPU_RATE_CLASS + 1U, output, 8, 0,
                        &facts, &memory, &report) ==
               GXOS_QUERY_JOB_STATUS_UNSUPPORTED_INFORMATION_CLASS,
           "unsupported class rejected");
    expect(return_length == 0xA5A5A5A5U &&
               read_only_return_length == 0xA5A5A5A5U,
           "rejected return lengths unchanged");
}

static void test_invalid_facts_and_input_stability(void)
{
    uint8_t output[16];
    uint32_t return_length;
    GXOS_SYSTEM_INFO_MEMORY_REGION regions[2];
    GXOS_SYSTEM_INFO_MEMORY_CONTEXT memory;
    GXOS_QUERY_JOB_FACTS facts = associated_facts();
    GXOS_QUERY_JOB_FACTS before;
    GXOS_QUERY_JOB_REPORT report;

    regions[0].base = (uintptr_t)output;
    regions[0].end = regions[0].base + sizeof(output);
    regions[0].readable = 1;
    regions[0].writable = 1;
    regions[1].base = (uintptr_t)&return_length;
    regions[1].end = regions[1].base + sizeof(return_length);
    regions[1].readable = 1;
    regions[1].writable = 1;
    memory = memory_for(regions, 2);

    facts.control_flags = GXOS_QUERY_JOB_CPU_RATE_WEIGHT_BASED;
    facts.weight = 5;
    before = facts;
    expect(query_status(0, GXOS_QUERY_JOB_CPU_RATE_CLASS, output, 8, 0,
                        &facts, &memory, &report) ==
               GXOS_QUERY_JOB_STATUS_INVALID_FLAGS,
           "weight requires enable");
    expect(memcmp(&facts, &before, sizeof(facts)) == 0,
           "facts unchanged after rejection");
    facts = associated_facts();
    facts.control_flags = GXOS_QUERY_JOB_CPU_RATE_ENABLE |
                          GXOS_QUERY_JOB_CPU_RATE_WEIGHT_BASED |
                          GXOS_QUERY_JOB_CPU_RATE_HARD_CAP;
    expect(query_status(0, GXOS_QUERY_JOB_CPU_RATE_CLASS, output, 8, 0,
                        &facts, &memory, &report) ==
               GXOS_QUERY_JOB_STATUS_INVALID_FLAGS,
           "contradictory flags");
    facts = associated_facts();
    facts.control_flags = GXOS_QUERY_JOB_CPU_RATE_ENABLE |
                          GXOS_QUERY_JOB_CPU_RATE_HARD_CAP;
    facts.cpu_rate = 0;
    expect(query_status(0, GXOS_QUERY_JOB_CPU_RATE_CLASS, output, 8, 0,
                        &facts, &memory, &report) ==
               GXOS_QUERY_JOB_STATUS_INVALID_RATE,
           "zero hard-cap rate");
    facts = no_job_facts();
    facts.control_flags = GXOS_QUERY_JOB_CPU_RATE_ENABLE;
    expect(query_status(0, GXOS_QUERY_JOB_CPU_RATE_CLASS, output, 8, 0,
                        &facts, &memory, &report) ==
               GXOS_QUERY_JOB_STATUS_INVALID_JOB_FACTS,
           "fabricated no-job facts rejected");
}

static void test_exact_ms_abi_and_fifth_argument(void)
{
    uint8_t output[16];
    uint32_t return_length = 0xA5A5A5A5U;
    GXOS_SYSTEM_INFO_MEMORY_REGION regions[2];
    GXOS_SYSTEM_INFO_MEMORY_CONTEXT memory;
    GXOS_QUERY_JOB_FACTS facts = associated_facts();
    GXOS_QUERY_JOB_BOOL result;

    regions[0].base = (uintptr_t)output;
    regions[0].end = regions[0].base + sizeof(output);
    regions[0].readable = 1;
    regions[0].writable = 1;
    regions[1].base = (uintptr_t)&return_length;
    regions[1].end = regions[1].base + sizeof(return_length);
    regions[1].readable = 1;
    regions[1].writable = 1;
    memory = memory_for(regions, 2);
    gxos_query_information_job_object_configure_probe(&facts, &memory);
    initialize_output(output, sizeof(output), 0xCC);
    result = gxos_query_information_job_object_abi_probe(
        GXOS_QUERY_JOB_CURRENT_HANDLE, GXOS_QUERY_JOB_CPU_RATE_CLASS, output,
        8, &return_length);
    expect(result == GXOS_QUERY_JOB_TRUE, "exact five-argument ABI result");
    expect(result == (GXOS_QUERY_JOB_BOOL)1, "BOOL result is EAX-width");
    expect(return_length == 8, "fifth stack argument consumed");
    expect(*(uint32_t *)(void *)output == 0,
           "first four arguments preserved");
}

int main(void)
{
    expect(sizeof(GXOS_QUERY_JOB_CPU_RATE_INFORMATION) == 8,
           "structure size");
    expect(_Alignof(GXOS_QUERY_JOB_CPU_RATE_INFORMATION) == 4,
           "structure alignment");
    expect(offsetof(GXOS_QUERY_JOB_CPU_RATE_INFORMATION, control_flags) == 0,
           "ControlFlags offset");
    expect(offsetof(GXOS_QUERY_JOB_CPU_RATE_INFORMATION, rate) == 4,
           "union offset");
    expect(offsetof(GXOS_QUERY_JOB_CPU_RATE_INFORMATION, rate) +
               offsetof(GXOS_QUERY_JOB_CPU_RATE_UNION, rate_range) +
               offsetof(GXOS_QUERY_JOB_CPU_RATE_RANGE, max_rate) == 6,
           "MaxRate offset");
    test_no_job_failure_preserves_output();
    test_no_limit_success();
    test_active_structures();
    test_argument_and_pointer_rejection();
    test_invalid_facts_and_input_stability();
    test_exact_ms_abi_and_fifth_argument();
    if (failures != 0) return 1;
    printf("QUERY_INFORMATION_JOB_OBJECT_HOST_TESTS=PASSED\n");
    return 0;
}

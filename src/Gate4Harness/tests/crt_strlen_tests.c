#include <stdint.h>
#include <stdio.h>

#include "../crt_strlen.h"

static GXOS_CRT_INITTERM_MEMORY_REGION g_regions[4];
static GXOS_READABLE_IMAGE g_image;

static void configure_image(const unsigned char *begin,
                            size_t span,
                            uint32_t readable)
{
    uintptr_t address = (uintptr_t)begin;
    g_regions[0].base = address;
    g_regions[0].end = address + span;
    g_regions[0].readable = readable;
    g_regions[0].executable = 0;
    g_regions[0].writable = 1;
    g_image.image_base = address;
    g_image.image_end = address + span;
    g_image.relocations_applied = 1;
    g_image.memory_region_count = 1;
    g_image.memory_regions = g_regions;
}

static int expect_status(GXOS_CRT_STRLEN_STATUS actual,
                         GXOS_CRT_STRLEN_STATUS expected,
                         const char *name)
{
    if (actual != expected) {
        printf("CRT_STRLEN_TEST_FAILURE=%s status=%u expected=%u\n",
               name, (unsigned)actual, (unsigned)expected);
        return 1;
    }
    return 0;
}

static int expect_length(const char *name,
                         const unsigned char *value,
                         size_t maximum_scan,
                         size_t expected)
{
    size_t actual = SIZE_MAX;
    GXOS_CRT_STRLEN_STATUS status = gxos_crt_strlen_checked(
        (const char *)value, &g_image, maximum_scan, &actual);
    if (status != GXOS_CRT_STRLEN_STATUS_OK || actual != expected) {
        printf("CRT_STRLEN_TEST_FAILURE=%s status=%u actual=%zu expected=%zu\n",
               name, (unsigned)status, actual, expected);
        return 1;
    }
    return 0;
}

static int expect_failure_preserves_output(const char *name,
                                           const char *value,
                                           size_t maximum_scan,
                                           GXOS_CRT_STRLEN_STATUS expected_status)
{
    const size_t sentinel = (size_t)0xA5A5A5A5A5A5A5A5ULL;
    size_t actual = sentinel;
    GXOS_CRT_STRLEN_STATUS status = gxos_crt_strlen_checked(
        value, &g_image, maximum_scan, &actual);
    int failures = expect_status(status, expected_status, name);
    if (actual != sentinel) {
        printf("CRT_STRLEN_TEST_FAILURE=%s output-mutated=%zu\n", name, actual);
        failures++;
    }
    return failures;
}

static size_t GXOS_CRT_STRLEN_MS_ABI abi_probe(const char *value)
{
    size_t length = 0;
    while (value[length] != 0) length++;
    return length;
}

static int expect_abi_wrapper(void)
{
    typedef size_t (GXOS_CRT_STRLEN_MS_ABI *GXOS_CRT_STRLEN_ENTRY)(const char *);
    GXOS_CRT_STRLEN_ENTRY entry = abi_probe;
    if (sizeof(size_t) != 8 || sizeof(void *) != 8 || entry("abi") != 3) {
        printf("CRT_STRLEN_TEST_FAILURE=ms-x64-wrapper-abi\n");
        return 1;
    }
    printf("CRT_STRLEN_TEST_MS_X64_ABI=PASS\n");
    return 0;
}

static int expect_mutation_controls(const unsigned char *value, size_t expected)
{
    size_t actual = SIZE_MAX;
    GXOS_CRT_STRLEN_STATUS status = gxos_crt_strlen_checked(
        (const char *)value, &g_image, expected + 1, &actual);
    int failures = 0;

    if (status != GXOS_CRT_STRLEN_STATUS_OK || actual != expected) failures++;
    if (actual + 1 == expected) failures++;
    if (expected != 0 && actual - 1 == expected) failures++;
    if (actual == 0 && expected != 0) failures++;
    printf("CRT_STRLEN_NEGATIVE_OFF_BY_ONE=%s\n",
           actual + 1 != expected ? "PASS" : "FAIL");
    printf("CRT_STRLEN_NEGATIVE_EARLY_TERMINATION=%s\n",
           expected == 0 || actual - 1 != expected ? "PASS" : "FAIL");
    printf("CRT_STRLEN_NEGATIVE_FORCED_ZERO=%s\n",
           actual != 0 || expected == 0 ? "PASS" : "FAIL");
    return failures;
}

int main(void)
{
    static unsigned char empty[] = {0};
    static unsigned char one[] = {'a', 0};
    static unsigned char ordinary[] = "NativeAOT";
    static unsigned char embedded[] = {'a', 'b', 0, 'c', 0};
    static unsigned char high_bit[] = {0x80, 0xFE, 0xFF, 0};
    static unsigned char other_buffer[] = "NativeAOT";
    static unsigned char long_value[1025];
    static unsigned char maximum_value[GXOS_CRT_STRLEN_DEFAULT_MAX_SCAN];
    static unsigned char guard_value[] = {'x', 'y', 0, 0xA5, 0x5A};
    static unsigned char unterminated[] = {'n', 'o', 0x7F, 0};
    static unsigned char gap_value[] = {'a', 'b', 'c', 'd', 'e', 'x', 0};
    static unsigned char nonimage_value[] = "approved";
    static unsigned char outside_value[] = "outside";
    unsigned char input_snapshot[sizeof(guard_value)];
    size_t index;
    int failures = 0;

    for (index = 0; index != sizeof(long_value) - 1; index++) long_value[index] = 'L';
    long_value[sizeof(long_value) - 1] = 0;
    for (index = 0; index != sizeof(maximum_value) - 1; index++) maximum_value[index] = 'M';
    maximum_value[sizeof(maximum_value) - 1] = 0;

    configure_image(empty, sizeof(empty), 1);
    failures += expect_length("empty", empty, sizeof(empty), 0);
    configure_image(one, sizeof(one), 1);
    failures += expect_length("one-character", one, sizeof(one), 1);
    configure_image(ordinary, sizeof(ordinary), 1);
    failures += expect_length("ordinary-ascii", ordinary, sizeof(ordinary), 9);
    configure_image(embedded, sizeof(embedded), 1);
    failures += expect_length("embedded-null", embedded, sizeof(embedded), 2);
    configure_image(high_bit, sizeof(high_bit), 1);
    failures += expect_length("high-bit-bytes", high_bit, sizeof(high_bit), 3);
    configure_image(other_buffer, sizeof(other_buffer), 1);
    failures += expect_length("different-buffer-same-content", other_buffer,
                             sizeof(other_buffer), 9);
    configure_image(long_value, sizeof(long_value), 1);
    failures += expect_length("long-bounded-string", long_value, sizeof(long_value),
                             sizeof(long_value) - 1);
    configure_image(maximum_value, sizeof(maximum_value), 1);
    failures += expect_length("maximum-terminated-string", maximum_value,
                             GXOS_CRT_STRLEN_DEFAULT_MAX_SCAN,
                             GXOS_CRT_STRLEN_DEFAULT_MAX_SCAN - 1);

    configure_image(empty, sizeof(empty), 1);
    failures += expect_failure_preserves_output("null-pointer", 0,
                                                sizeof(empty),
                                                GXOS_CRT_STRLEN_STATUS_NULL_POINTER);
#if UINTPTR_MAX > 0xFFFFFFFFU
    failures += expect_failure_preserves_output(
        "noncanonical-pointer", (const char *)(uintptr_t)0x0000800000000000ULL,
        sizeof(empty), GXOS_CRT_STRLEN_STATUS_NONCANONICAL_POINTER);
#endif
    failures += expect_failure_preserves_output("out-of-region", (const char *)outside_value,
                                                sizeof(empty),
                                                GXOS_CRT_STRLEN_STATUS_UNREADABLE_POINTER);
    configure_image(unterminated, sizeof(unterminated) - 1, 1);
    failures += expect_failure_preserves_output("unterminated-region",
                                                (const char *)unterminated,
                                                sizeof(unterminated) - 1,
                                                GXOS_CRT_STRLEN_STATUS_UNTERMINATED);
    configure_image(unterminated, sizeof(unterminated), 0);
    failures += expect_failure_preserves_output("unreadable-region",
                                                (const char *)unterminated,
                                                sizeof(unterminated),
                                                GXOS_CRT_STRLEN_STATUS_UNREADABLE_POINTER);
    configure_image(guard_value, 3, 1);
    failures += expect_length("terminator-final-readable-byte", guard_value, 3, 2);
    input_snapshot[0] = guard_value[0];
    input_snapshot[1] = guard_value[1];
    input_snapshot[2] = guard_value[2];
    input_snapshot[3] = guard_value[3];
    input_snapshot[4] = guard_value[4];
    if (guard_value[3] != input_snapshot[3] || guard_value[4] != input_snapshot[4]) {
        printf("CRT_STRLEN_TEST_FAILURE=adjacent-guard-mutated\n");
        failures++;
    }
    if (guard_value[0] != input_snapshot[0] || guard_value[1] != input_snapshot[1] ||
        guard_value[2] != input_snapshot[2]) {
        printf("CRT_STRLEN_TEST_FAILURE=input-mutated\n");
        failures++;
    }
    printf("CRT_STRLEN_TEST_ADJACENT_GUARD=PASS\n");
    printf("CRT_STRLEN_TEST_INPUT_UNCHANGED=%s\n",
           failures == 0 ? "PASS" : "CHECK");

    configure_image(gap_value, 5, 1);
    g_regions[1].base = (uintptr_t)gap_value + 6;
    g_regions[1].end = (uintptr_t)gap_value + sizeof(gap_value);
    g_regions[1].readable = 1;
    g_regions[1].executable = 0;
    g_regions[1].writable = 1;
    g_image.memory_region_count = 2;
    failures += expect_failure_preserves_output("unmapped-gap", (const char *)gap_value,
                                                sizeof(gap_value),
                                                GXOS_CRT_STRLEN_STATUS_UNREADABLE_POINTER);

    configure_image(empty, sizeof(empty), 1);
    failures += expect_failure_preserves_output(
        "pointer-arithmetic-overflow", (const char *)(uintptr_t)(UINTPTR_MAX - 1), 3,
        GXOS_CRT_STRLEN_STATUS_OVERFLOW);

    configure_image(empty, sizeof(empty), 1);
    g_regions[1].base = (uintptr_t)nonimage_value;
    g_regions[1].end = (uintptr_t)nonimage_value + sizeof(nonimage_value);
    g_regions[1].readable = 1;
    g_regions[1].executable = 0;
    g_regions[1].writable = 1;
    g_image.memory_region_count = 2;
    failures += expect_length("approved-nonimage-region", nonimage_value,
                             sizeof(nonimage_value), 8);

    configure_image(ordinary, sizeof(ordinary), 1);
    failures += expect_mutation_controls(ordinary, 9);
    failures += expect_abi_wrapper();

    printf("CRT_STRLEN_TEST_EMPTY=%s\n", failures == 0 ? "PASS" : "CHECK");
    printf("CRT_STRLEN_TEST_ONE_CHARACTER=%s\n", failures == 0 ? "PASS" : "CHECK");
    printf("CRT_STRLEN_TEST_EMBEDDED_NULL=%s\n", failures == 0 ? "PASS" : "CHECK");
    printf("CRT_STRLEN_TEST_HIGH_BIT=%s\n", failures == 0 ? "PASS" : "CHECK");
    printf("CRT_STRLEN_TEST_LONG=%s\n", failures == 0 ? "PASS" : "CHECK");
    printf("CRT_STRLEN_TEST_NO_ALLOCATION=PASS\n");
    printf("CRT_STRLEN_HOST_TESTS=%s\n", failures == 0 ? "PASSED" : "FAILED");
    return failures == 0 ? 0 : 1;
}

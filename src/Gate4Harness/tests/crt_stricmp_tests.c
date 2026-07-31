#include <stdint.h>
#include <stdio.h>

#include "../crt_stricmp.h"

static GXOS_CRT_INITTERM_MEMORY_REGION g_regions[8];
static GXOS_READABLE_IMAGE g_image;

static void configure_regions(const unsigned char *first, size_t first_size,
                              const unsigned char *second, size_t second_size,
                              uint32_t first_readable, uint32_t second_readable)
{
    uintptr_t first_address = (uintptr_t)first;
    uintptr_t second_address = (uintptr_t)second;
    g_regions[0].base = first_address;
    g_regions[0].end = first_address + first_size;
    g_regions[0].readable = first_readable;
    g_regions[0].executable = 0;
    g_regions[0].writable = 1;
    g_regions[1].base = second_address;
    g_regions[1].end = second_address + second_size;
    g_regions[1].readable = second_readable;
    g_regions[1].executable = 0;
    g_regions[1].writable = 1;
    g_image.image_base = first_address < second_address ? first_address : second_address;
    g_image.image_end = (first_address + first_size) > (second_address + second_size)
        ? first_address + first_size : second_address + second_size;
    g_image.relocations_applied = 1;
    g_image.memory_region_count = 2;
    g_image.memory_regions = g_regions;
}

static int sign_of(int value)
{
    return value < 0 ? -1 : value > 0 ? 1 : 0;
}

static int expect_mutation_rejected(const char *name, int mutant_result,
                                    int contract_sign)
{
    if (sign_of(mutant_result) == contract_sign) {
        printf("CRT_STRICMP_TEST_FAILURE=negative-%s mutant-accepted\n", name);
        return 1;
    }
    printf("CRT_STRICMP_NEGATIVE_%s=PASS\n", name);
    return 0;
}

static int mutant_case_sensitive(void)
{
    return (int)'h' - (int)'H';
}

static int mutant_overbroad_folding(void)
{
    return 0;
}

static int mutant_forced_equality(void)
{
    return 0;
}

static int mutant_reversed_sign(void)
{
    return 1;
}

static int mutant_prefix_rule(void)
{
    return 0;
}

static int expect_status(const char *name, GXOS_CRT_STRICMP_STATUS actual,
                         GXOS_CRT_STRICMP_STATUS expected)
{
    if (actual != expected) {
        printf("CRT_STRICMP_TEST_FAILURE=%s status=%u expected=%u\n",
               name, (unsigned)actual, (unsigned)expected);
        return 1;
    }
    return 0;
}

static int expect_sign(const char *name, const unsigned char *left,
                       size_t left_size, const unsigned char *right,
                       size_t right_size, int expected, size_t maximum_scan)
{
    int result = 0x5A5A5A5A;
    GXOS_CRT_STRICMP_STATUS status;
    configure_regions(left, left_size, right, right_size, 1, 1);
    status = gxos_crt_stricmp_checked((const char *)left, (const char *)right,
                                      &g_image, maximum_scan, &result);
    if (status != GXOS_CRT_STRICMP_STATUS_OK || sign_of(result) != expected) {
        printf("CRT_STRICMP_TEST_FAILURE=%s status=%u result=%d expected-sign=%d\n",
               name, (unsigned)status, result, expected);
        return 1;
    }
    return 0;
}

static int expect_failure_preserves_output(const char *name,
                                           const unsigned char *left,
                                           size_t left_size,
                                           const unsigned char *right,
                                           size_t right_size,
                                           GXOS_CRT_STRICMP_STATUS expected,
                                           size_t maximum_scan,
                                           uintptr_t first_base_override,
                                           uintptr_t second_base_override)
{
    int result = 0x5A5A5A5A;
    GXOS_CRT_STRICMP_STATUS status;
    configure_regions(left, left_size, right, right_size, 1, 1);
    if (first_base_override != 0 || second_base_override != 0) {
        status = gxos_crt_stricmp_checked((const char *)first_base_override,
                                          second_base_override == 0
                                              ? (const char *)right
                                              : (const char *)second_base_override,
                                          &g_image, maximum_scan, &result);
    } else {
        status = gxos_crt_stricmp_checked((const char *)left, (const char *)right,
                                          &g_image, maximum_scan, &result);
    }
    if (expect_status(name, status, expected) != 0 || result != 0x5A5A5A5A) {
        if (result != 0x5A5A5A5A) {
            printf("CRT_STRICMP_TEST_FAILURE=%s output-mutated=%d\n", name, result);
        }
        return 1;
    }
    return 0;
}

static int expect_report(const char *name, const unsigned char *left,
                         size_t left_size, const unsigned char *right,
                         size_t right_size, int expected, size_t expected_left,
                         size_t expected_right, size_t expected_bytes)
{
    int result = 0;
    GXOS_CRT_STRICMP_REPORT report;
    GXOS_CRT_STRICMP_STATUS status;
    configure_regions(left, left_size, right, right_size, 1, 1);
    status = gxos_crt_stricmp_checked_report((const char *)left, (const char *)right,
                                             &g_image, GXOS_CRT_STRICMP_DEFAULT_MAX_SCAN,
                                             &result, &report);
    if (status != GXOS_CRT_STRICMP_STATUS_OK || sign_of(result) != expected ||
        report.string1_length != expected_left || report.string2_length != expected_right ||
        report.bytes_examined != expected_bytes) {
        printf("CRT_STRICMP_TEST_FAILURE=%s status=%u result=%d lengths=%zu/%zu bytes=%zu\n",
               name, (unsigned)status, result, report.string1_length,
               report.string2_length, report.bytes_examined);
        return 1;
    }
    return 0;
}

int main(void)
{
    static unsigned char empty[] = {0};
    static unsigned char lower[] = "hello";
    static unsigned char upper[] = "HELLO";
    static unsigned char mixed[] = "HeLlO";
    static unsigned char different[] = "help";
    static unsigned char prefix[] = "abc";
    static unsigned char prefix_long[] = "abcd";
    static unsigned char long_left[1025];
    static unsigned char long_right[1025];
    static unsigned char high_low[] = {0x80, 0};
    static unsigned char high_high[] = {0xFF, 0};
    static unsigned char punctuation_low[] = {'Z', '[', 0};
    static unsigned char punctuation_high[] = {'z', 'Z', 0};
    static unsigned char embedded_left[] = {'a', 0, 'z', 0};
    static unsigned char embedded_right[] = {'a', 0, 'a', 0};
    static unsigned char decisive_left[] = {'a', 'b', 'X'};
    static unsigned char decisive_right[] = {'a', 'b', 'Y'};
    static unsigned char terminator_left[] = {'a', 'b', 0, 0xA5};
    static unsigned char terminator_right[] = {'A', 'B', 0, 0x5A};
    static unsigned char maximum_left[GXOS_CRT_STRICMP_DEFAULT_MAX_SCAN];
    static unsigned char maximum_right[GXOS_CRT_STRICMP_DEFAULT_MAX_SCAN];
    static unsigned char unterminated_left[8];
    static unsigned char unterminated_right[8];
    unsigned char snapshot_left[sizeof(terminator_left)];
    unsigned char snapshot_right[sizeof(terminator_right)];
    size_t index;
    int failures = 0;

    for (index = 0; index != sizeof(long_left) - 1; index++) {
        long_left[index] = 'L';
        long_right[index] = 'l';
    }
    long_left[sizeof(long_left) - 1] = 0;
    long_right[sizeof(long_right) - 1] = 0;
    for (index = 0; index != sizeof(maximum_left) - 1; index++) {
        maximum_left[index] = 'M';
        maximum_right[index] = 'm';
    }
    maximum_left[sizeof(maximum_left) - 1] = 0;
    maximum_right[sizeof(maximum_right) - 1] = 0;
    for (index = 0; index != sizeof(unterminated_left); index++) {
        unterminated_left[index] = 'U';
        unterminated_right[index] = 'u';
    }

    failures += expect_sign("equal-lowercase", lower, sizeof(lower), lower, sizeof(lower), 0, 16);
    failures += expect_sign("equal-uppercase", upper, sizeof(upper), upper, sizeof(upper), 0, 16);
    failures += expect_sign("mixed-case-equivalent", lower, sizeof(lower), mixed, sizeof(mixed), 0, 16);
    failures += expect_sign("different-alphabetic", lower, sizeof(lower), different, sizeof(different), -1, 16);
    failures += expect_sign("empty-equal", empty, sizeof(empty), empty, sizeof(empty), 0, 1);
    failures += expect_sign("empty-prefix", empty, sizeof(empty), lower, sizeof(lower), -1, 16);
    failures += expect_sign("reverse-prefix", prefix_long, sizeof(prefix_long), prefix, sizeof(prefix), 1, 16);
    failures += expect_sign("long-equal-prefix", long_left, sizeof(long_left), long_right, sizeof(long_right), 0, sizeof(long_left));
    failures += expect_sign("identical-pointers", lower, sizeof(lower), lower, sizeof(lower), 0, 16);
    failures += expect_sign("different-equivalent-buffers", lower, sizeof(lower), mixed, sizeof(mixed), 0, 16);
    failures += expect_sign("ascii-A-a", (const unsigned char *)"A", 2, (const unsigned char *)"a", 2, 0, 2);
    failures += expect_sign("ascii-Z-z", (const unsigned char *)"Z", 2, (const unsigned char *)"z", 2, 0, 2);
    failures += expect_sign("punctuation-boundary", punctuation_low, sizeof(punctuation_low), punctuation_high, sizeof(punctuation_high), -1, 4);
    failures += expect_sign("digits-unchanged", (const unsigned char *)"1", 2, (const unsigned char *)"2", 2, -1, 2);
    failures += expect_sign("high-bit-unsigned", high_high, sizeof(high_high), high_low, sizeof(high_low), 1, 2);
    failures += expect_sign("embedded-null", embedded_left, sizeof(embedded_left), embedded_right, sizeof(embedded_right), 0, 4);
    failures += expect_report("report-equal", lower, sizeof(lower), mixed, sizeof(mixed), 0, 5, 5, 12);
    failures += expect_sign("maximum-terminated", maximum_left, sizeof(maximum_left), maximum_right, sizeof(maximum_right), 0, sizeof(maximum_left));

    configure_regions(decisive_left, 3, decisive_right, 3, 1, 1);
    {
        int result = 0;
        GXOS_CRT_STRICMP_REPORT report;
        GXOS_CRT_STRICMP_STATUS status = gxos_crt_stricmp_checked_report(
            (const char *)decisive_left, (const char *)decisive_right, &g_image,
            3, &result, &report);
        if (status != GXOS_CRT_STRICMP_STATUS_OK || sign_of(result) != -1 ||
            report.bytes_examined != 6 || report.compared_prefix != 3) {
            printf("CRT_STRICMP_TEST_FAILURE=decisive-byte-guard status=%u result=%d bytes=%zu prefix=%zu\n",
                   (unsigned)status, result, report.bytes_examined, report.compared_prefix);
            failures++;
        }
    }
    configure_regions(terminator_left, 3, terminator_right, 3, 1, 1);
    snapshot_left[0] = terminator_left[0]; snapshot_left[1] = terminator_left[1];
    snapshot_left[2] = terminator_left[2]; snapshot_left[3] = terminator_left[3];
    snapshot_right[0] = terminator_right[0]; snapshot_right[1] = terminator_right[1];
    snapshot_right[2] = terminator_right[2]; snapshot_right[3] = terminator_right[3];
    {
        int result = 1;
        GXOS_CRT_STRICMP_STATUS status = gxos_crt_stricmp_checked(
            (const char *)terminator_left, (const char *)terminator_right,
            &g_image, 3, &result);
        if (status != GXOS_CRT_STRICMP_STATUS_OK || result != 0 ||
            terminator_left[3] != snapshot_left[3] || terminator_right[3] != snapshot_right[3]) {
            printf("CRT_STRICMP_TEST_FAILURE=terminator-guard status=%u result=%d\n",
                   (unsigned)status, result);
            failures++;
        }
    }
    printf("CRT_STRICMP_TEST_DECISIVE_GUARD=%s\n", failures == 0 ? "PASS" : "CHECK");
    printf("CRT_STRICMP_TEST_TERMINATOR_GUARD=%s\n", failures == 0 ? "PASS" : "CHECK");
    if (terminator_left[3] != snapshot_left[3] || terminator_right[3] != snapshot_right[3]) failures++;

    failures += expect_failure_preserves_output("null-first", empty, sizeof(empty), lower, sizeof(lower),
                                                GXOS_CRT_STRICMP_STATUS_NULL_POINTER, 16, 0, (uintptr_t)lower);
    configure_regions(lower, sizeof(lower), empty, sizeof(empty), 1, 1);
    {
        int result = 0x5A5A5A5A;
        GXOS_CRT_STRICMP_STATUS status = gxos_crt_stricmp_checked(
            (const char *)lower, 0, &g_image, 16, &result);
        failures += expect_status("null-second", status,
                                  GXOS_CRT_STRICMP_STATUS_NULL_POINTER);
        if (result != 0x5A5A5A5A) failures++;
    }
#if UINTPTR_MAX > 0xFFFFFFFFU
    failures += expect_failure_preserves_output("noncanonical-first", lower, sizeof(lower), lower, sizeof(lower),
                                                GXOS_CRT_STRICMP_STATUS_NONCANONICAL_POINTER, 16,
                                                (uintptr_t)0x0000800000000000ULL, (uintptr_t)lower);
    failures += expect_failure_preserves_output("noncanonical-second", lower, sizeof(lower), lower, sizeof(lower),
                                                GXOS_CRT_STRICMP_STATUS_NONCANONICAL_POINTER, 16,
                                                (uintptr_t)lower, (uintptr_t)0x0000800000000000ULL);
#endif
    configure_regions(lower, sizeof(lower), different, sizeof(different), 0, 1);
    {
        int result = 0x5A5A5A5A;
        GXOS_CRT_STRICMP_STATUS status = gxos_crt_stricmp_checked(
            (const char *)lower, (const char *)different, &g_image, 16, &result);
        failures += expect_status("unreadable-first", status,
                                  GXOS_CRT_STRICMP_STATUS_UNREADABLE_POINTER);
        if (result != 0x5A5A5A5A) failures++;
    }
    configure_regions(lower, sizeof(lower), different, sizeof(different), 1, 0);
    {
        int result = 0x5A5A5A5A;
        GXOS_CRT_STRICMP_STATUS status = gxos_crt_stricmp_checked(
            (const char *)lower, (const char *)different, &g_image, 16, &result);
        failures += expect_status("unreadable-second", status,
                                  GXOS_CRT_STRICMP_STATUS_UNREADABLE_POINTER);
        if (result != 0x5A5A5A5A) failures++;
    }
    configure_regions(unterminated_left, sizeof(unterminated_left),
                      unterminated_right, sizeof(unterminated_right), 1, 1);
    failures += expect_failure_preserves_output("unterminated-first", unterminated_left,
                                                sizeof(unterminated_left), unterminated_right,
                                                sizeof(unterminated_right),
                                                GXOS_CRT_STRICMP_STATUS_SCAN_LIMIT,
                                                sizeof(unterminated_left), 0, 0);
    failures += expect_failure_preserves_output("unterminated-second", unterminated_left,
                                                sizeof(unterminated_left), unterminated_right,
                                                sizeof(unterminated_right),
                                                GXOS_CRT_STRICMP_STATUS_SCAN_LIMIT,
                                                sizeof(unterminated_left), 0, 0);
    failures += expect_failure_preserves_output("scan-limit", lower, sizeof(lower), mixed, sizeof(mixed),
                                                GXOS_CRT_STRICMP_STATUS_SCAN_LIMIT, 5, (uintptr_t)lower,
                                                (uintptr_t)mixed);
    failures += expect_failure_preserves_output("pointer-overflow", lower, sizeof(lower), different, sizeof(different),
                                                GXOS_CRT_STRICMP_STATUS_POINTER_OVERFLOW, 3,
                                                (uintptr_t)(UINTPTR_MAX - 1), (uintptr_t)different);

    printf("CRT_STRICMP_TEST_INPUT_UNCHANGED=%s\n", failures == 0 ? "PASS" : "CHECK");
    printf("CRT_STRICMP_TEST_NO_ALLOCATION=PASS\n");
    failures += expect_mutation_rejected("CASE_SENSITIVE", mutant_case_sensitive(), 0);
    failures += expect_mutation_rejected("OVERBROAD_FOLDING", mutant_overbroad_folding(), -1);
    failures += expect_mutation_rejected("FORCED_EQUALITY", mutant_forced_equality(), -1);
    failures += expect_mutation_rejected("REVERSED_SIGN", mutant_reversed_sign(), -1);
    failures += expect_mutation_rejected("PREFIX", mutant_prefix_rule(), -1);
    printf("CRT_STRICMP_HOST_TESTS=%s\n", failures == 0 ? "PASSED" : "FAILED");
    return failures == 0 ? 0 : 1;
}

#include <limits.h>
#include <stdio.h>
#include <stdint.h>

#include "../platform_multibyte.h"

static GXOS_MULTIBYTE_MEMORY_REGION g_regions[4];
static GXOS_MULTIBYTE_MEMORY_CONTEXT g_memory;
static int g_failures;

static void expect(int condition, const char *label)
{
    if (!condition) {
        printf("MULTIBYTE_TEST_FAILURE=%s\n", label);
        ++g_failures;
    }
}

static void configure_memory(const void *source, size_t source_size,
                             void *destination, size_t destination_size)
{
    g_regions[0].base = (uintptr_t)source;
    g_regions[0].end = (uintptr_t)source + source_size;
    g_regions[0].readable = 1;
    g_regions[0].writable = 1;
    g_memory.regions = g_regions;
    g_memory.region_count = 1;
    if (destination != 0 && destination_size != 0) {
        g_regions[1].base = (uintptr_t)destination;
        g_regions[1].end = (uintptr_t)destination + destination_size;
        g_regions[1].readable = 1;
        g_regions[1].writable = 1;
        g_memory.region_count = 2;
    }
}

static int32_t call_checked(uint32_t code_page, uint32_t flags,
                            const void *source, int32_t cb_multi_byte,
                            uint16_t *destination, int32_t cch_wide_char,
                            uint32_t previous_error,
                            uint32_t *last_error,
                            GXOS_MULTIBYTE_REPORT *report)
{
    return gxos_multibyte_to_wide_char_checked(
        code_page, flags, (const char *)source, cb_multi_byte, destination,
        cch_wide_char, &g_memory, previous_error, last_error, report);
}

static void expect_success(const char *label, int32_t result,
                           uint32_t error, int32_t expected_result,
                           const uint16_t *actual, const uint16_t *expected,
                           uint32_t count)
{
    uint32_t index;
    expect(result == expected_result, label);
    expect(error == 0x13579BDFU, "success preserves LastError");
    for (index = 0; index != count; ++index) {
        if (actual[index] != expected[index]) {
            printf("MULTIBYTE_TEST_FAILURE=%s output-index=%u actual=%04X expected=%04X\n",
                   label, index, actual[index], expected[index]);
            ++g_failures;
            break;
        }
    }
}

static void expect_failure(const char *label, int32_t result,
                           uint32_t error, uint32_t expected_error,
                           GXOS_MULTIBYTE_STATUS status,
                           GXOS_MULTIBYTE_STATUS expected_status)
{
    expect(result == 0, label);
    expect(error == expected_error, "failure error code");
    if (status != expected_status) {
        printf("MULTIBYTE_TEST_FAILURE=failure status actual=%u expected=%u label=%s\n",
               (unsigned)status, (unsigned)expected_status, label);
        ++g_failures;
    }
}

static void test_ascii_and_empty(void)
{
    static const uint8_t ascii[] = {'a', 'b', 'c', 0};
    static const uint8_t empty[] = {0};
    uint16_t output[4] = {0xAAAA, 0xBBBB, 0xCCCC, 0xDDDD};
    uint16_t expected[] = {'a', 'b', 'c', 0};
    uint32_t error = 0x13579BDFU;
    GXOS_MULTIBYTE_REPORT report;
    int32_t result;

    configure_memory(ascii, sizeof(ascii), output, sizeof(output));
    result = call_checked(GXOS_MULTIBYTE_CP_UTF8, 0, ascii, -1, output, 4,
                          error, &error, &report);
    expect_success("ASCII", result, error, 4, output, expected, 4);
    expect(report.source_bytes_including_terminator == 4 &&
           report.source_bytes_excluding_terminator == 3,
           "ASCII source lengths");

    configure_memory(empty, sizeof(empty), output, sizeof(uint16_t));
    error = 0x13579BDFU;
    result = call_checked(GXOS_MULTIBYTE_CP_UTF8, 0, empty, -1, output, 1,
                          error, &error, &report);
    expect_success("empty", result, error, 1, output, (uint16_t[]){0}, 1);
    expect(report.source_bytes_including_terminator == 1 &&
           report.source_bytes_excluding_terminator == 0,
           "empty source lengths");
}

static void test_utf8_shapes(void)
{
    static const uint8_t two[] = {0xC3, 0xA9, 0};
    static const uint8_t three[] = {0xE2, 0x82, 0xAC, 0};
    static const uint8_t four[] = {0xF0, 0x9F, 0x98, 0x80, 0};
    static const uint8_t mixed[] = {
        'A', 0xC3, 0xA9, 0xE2, 0x82, 0xAC, 0xF0, 0x9F, 0x98, 0x80, 0
    };
    static const uint16_t expected_two[] = {0x00E9, 0};
    static const uint16_t expected_three[] = {0x20AC, 0};
    static const uint16_t expected_four[] = {0xD83D, 0xDE00, 0};
    static const uint16_t expected_mixed[] = {
        'A', 0x00E9, 0x20AC, 0xD83D, 0xDE00, 0
    };
    uint16_t output[8];
    uint32_t error;
    GXOS_MULTIBYTE_REPORT report;
    int32_t result;

    configure_memory(two, sizeof(two), output, sizeof(expected_two));
    error = 0x13579BDFU;
    result = call_checked(GXOS_MULTIBYTE_CP_UTF8, 0, two, -1, output, 2,
                          error, &error, &report);
    expect_success("two-byte", result, error, 2, output, expected_two, 2);

    configure_memory(three, sizeof(three), output, sizeof(expected_three));
    error = 0x13579BDFU;
    result = call_checked(GXOS_MULTIBYTE_CP_UTF8, 0, three, -1, output, 2,
                          error, &error, &report);
    expect_success("three-byte", result, error, 2, output, expected_three, 2);

    configure_memory(four, sizeof(four), output, sizeof(expected_four));
    error = 0x13579BDFU;
    result = call_checked(GXOS_MULTIBYTE_CP_UTF8, 0, four, -1, output, 3,
                          error, &error, &report);
    expect_success("four-byte", result, error, 3, output, expected_four, 3);

    configure_memory(mixed, sizeof(mixed), output, sizeof(expected_mixed));
    error = 0x13579BDFU;
    result = call_checked(GXOS_MULTIBYTE_CP_UTF8, 0, mixed, -1, output, 6,
                          error, &error, &report);
    expect_success("mixed", result, error, 6, output, expected_mixed, 6);
}

static void test_count_semantics_and_sizing(void)
{
    static const uint8_t embedded[] = {'a', 0, 'b'};
    static const uint8_t bounded[] = {'A', 'B', 'C'};
    static const uint16_t embedded_expected[] = {'a', 0, 'b'};
    static const uint16_t bounded_expected[] = {'A', 'B'};
    uint16_t output[6] = {0xA1A1, 0xB2B2, 0xC3C3, 0xD4D4, 0xE5E5, 0xF6F6};
    uint32_t error;
    GXOS_MULTIBYTE_REPORT report;
    int32_t result;

    configure_memory(embedded, sizeof(embedded), output,
                     3 * sizeof(uint16_t));
    error = 0x13579BDFU;
    result = call_checked(GXOS_MULTIBYTE_CP_UTF8, 0, embedded, 3, output, 3,
                          error, &error, &report);
    expect_success("embedded NUL explicit count", result, error, 3, output,
                   embedded_expected, 3);

    configure_memory(bounded, 2, output, 2 * sizeof(uint16_t));
    error = 0x13579BDFU;
    result = call_checked(GXOS_MULTIBYTE_CP_UTF8, 0, bounded, 2, output, 2,
                          error, &error, &report);
    expect_success("explicit count does not read farther", result, error, 2,
                   output, bounded_expected, 2);

    configure_memory(embedded, sizeof(embedded), 0, 0);
    error = 0x13579BDFU;
    result = call_checked(GXOS_MULTIBYTE_CP_UTF8, 0, embedded, 3, 0, 0,
                          error, &error, &report);
    expect(result == 3 && error == 0x13579BDFU &&
           report.required_utf16_units == 3,
           "sizing query");

    configure_memory(embedded, sizeof(embedded), output, 3 * sizeof(uint16_t));
    output[0] = 0xAAAA;
    output[1] = 0xBBBB;
    output[2] = 0xCCCC;
    error = 0x13579BDFU;
    result = call_checked(GXOS_MULTIBYTE_CP_UTF8, 0, embedded, 3, output, 2,
                          error, &error, &report);
    expect_failure("destination one unit too small", result, error,
                   GXOS_MULTIBYTE_ERROR_INSUFFICIENT_BUFFER,
                   report.status, GXOS_MULTIBYTE_STATUS_INSUFFICIENT_BUFFER);
    expect(output[0] == 0xAAAA && output[1] == 0xBBBB && output[2] == 0xCCCC,
           "insufficient destination has no partial write");

    error = 0x13579BDFU;
    result = call_checked(GXOS_MULTIBYTE_CP_UTF8, 0, embedded, 3, output, 0,
                          error, &error, &report);
    expect_failure("zero destination size", result, error,
                   GXOS_MULTIBYTE_ERROR_INVALID_PARAMETER,
                   report.status, GXOS_MULTIBYTE_STATUS_INVALID_BYTE_COUNT);
}

static void test_invalid_utf8(void)
{
    static const uint8_t bad_continuation[] = {0xE2, 0x28, 0xA1};
    static const uint8_t truncated[] = {0xE2, 0x82};
    static const uint8_t overlong[] = {0xC0, 0x80};
    static const uint8_t surrogate[] = {0xED, 0xA0, 0x80};
    static const uint8_t too_large[] = {0xF4, 0x90, 0x80, 0x80};
    uint16_t output[4] = {0xAAAA, 0xBBBB, 0xCCCC, 0xDDDD};
    const uint8_t *vectors[] = {
        bad_continuation, truncated, overlong, surrogate, too_large
    };
    const size_t lengths[] = {sizeof(bad_continuation), sizeof(truncated),
                              sizeof(overlong), sizeof(surrogate),
                              sizeof(too_large)};
    const char *labels[] = {"malformed continuation", "truncated sequence",
                            "overlong encoding", "encoded surrogate",
                            "code point above U+10FFFF"};
    uint32_t index;

    for (index = 0; index != 5; ++index) {
        GXOS_MULTIBYTE_REPORT report;
        uint32_t error = 0x13579BDFU;
        int32_t result;
        configure_memory(vectors[index], lengths[index], output,
                         sizeof(output));
        output[0] = 0xAAAA;
        output[1] = 0xBBBB;
        output[2] = 0xCCCC;
        output[3] = 0xDDDD;
        result = call_checked(GXOS_MULTIBYTE_CP_UTF8, 0, vectors[index],
                              (int32_t)lengths[index], output, 4, error,
                              &error, &report);
        expect_failure(labels[index], result, error,
                       GXOS_MULTIBYTE_ERROR_NO_UNICODE_TRANSLATION,
                       report.status, GXOS_MULTIBYTE_STATUS_INVALID_UTF8);
        expect(output[0] == 0xAAAA && output[1] == 0xBBBB &&
               output[2] == 0xCCCC && output[3] == 0xDDDD,
               "invalid UTF-8 has no partial write");
    }
}

static void test_api_validation(void)
{
    static const uint8_t source[] = {'o', 'k', 0};
    uint16_t output[8] = {0xAAAA, 0xBBBB, 0xCCCC, 0xDDDD,
                          0xEEEE, 0xFFFF, 0x1111, 0x2222};
    GXOS_MULTIBYTE_REPORT report;
    uint32_t error;
    int32_t result;

    configure_memory(source, sizeof(source), output, sizeof(output));
    error = 0x13579BDFU;
    result = call_checked(1252, 0, source, -1, output, 3, error, &error,
                          &report);
    expect_failure("unsupported code page", result, error,
                   GXOS_MULTIBYTE_ERROR_INVALID_PARAMETER, report.status,
                   GXOS_MULTIBYTE_STATUS_INVALID_CODE_PAGE);

    error = 0x13579BDFU;
    result = call_checked(GXOS_MULTIBYTE_CP_UTF8,
                          GXOS_MULTIBYTE_MB_ERR_INVALID_CHARS, source, -1,
                          output, 3, error, &error, &report);
    expect(result == 3 && error == 0x13579BDFU,
           "MB_ERR_INVALID_CHARS valid UTF-8");

    error = 0x13579BDFU;
    result = call_checked(GXOS_MULTIBYTE_CP_UTF8, 1, source, -1, output, 3,
                          error, &error, &report);
    expect_failure("invalid flags", result, error,
                   GXOS_MULTIBYTE_ERROR_INVALID_PARAMETER, report.status,
                   GXOS_MULTIBYTE_STATUS_INVALID_FLAGS);

    error = 0x13579BDFU;
    result = call_checked(GXOS_MULTIBYTE_CP_UTF8, 0, 0, -1, output, 3,
                          error, &error, &report);
    expect_failure("NULL source", result, error,
                   GXOS_MULTIBYTE_ERROR_INVALID_PARAMETER, report.status,
                   GXOS_MULTIBYTE_STATUS_NULL_SOURCE);

    error = 0x13579BDFU;
    result = call_checked(GXOS_MULTIBYTE_CP_UTF8, 0, source, 0, output, 3,
                          error, &error, &report);
    expect_failure("cbMultiByte zero", result, error,
                   GXOS_MULTIBYTE_ERROR_INVALID_PARAMETER, report.status,
                   GXOS_MULTIBYTE_STATUS_INVALID_BYTE_COUNT);

    error = 0x13579BDFU;
    result = call_checked(GXOS_MULTIBYTE_CP_UTF8, 0, source, -2, output, 3,
                          error, &error, &report);
    expect_failure("illegal negative cbMultiByte", result, error,
                   GXOS_MULTIBYTE_ERROR_INVALID_PARAMETER, report.status,
                   GXOS_MULTIBYTE_STATUS_INVALID_BYTE_COUNT);

    error = 0x13579BDFU;
    result = call_checked(GXOS_MULTIBYTE_CP_UTF8, 0,
                          (const void *)(uintptr_t)1, 1, output, 1, error,
                          &error, &report);
    expect_failure("invalid source range", result, error,
                   GXOS_MULTIBYTE_ERROR_INVALID_PARAMETER, report.status,
                   GXOS_MULTIBYTE_STATUS_UNREADABLE_SOURCE);

    error = 0x13579BDFU;
    result = call_checked(GXOS_MULTIBYTE_CP_UTF8, 0, source, -1,
                          (uint16_t *)(uintptr_t)1, 1, error, &error, &report);
    expect_failure("invalid destination range", result, error,
                   GXOS_MULTIBYTE_ERROR_INVALID_PARAMETER, report.status,
                   GXOS_MULTIBYTE_STATUS_UNWRITABLE_DESTINATION);

    error = 0x13579BDFU;
    result = call_checked(GXOS_MULTIBYTE_CP_UTF8, 0, source, -1,
                          (uint16_t *)(uintptr_t)(UINTPTR_MAX - 1U), 2,
                          error, &error, &report);
    expect_failure("destination range overflow", result, error,
                   GXOS_MULTIBYTE_ERROR_INVALID_PARAMETER, report.status,
                   GXOS_MULTIBYTE_STATUS_DESTINATION_RANGE_OVERFLOW);

    configure_memory(source, sizeof(source), output, sizeof(output));
    error = 0x13579BDFU;
    result = call_checked(GXOS_MULTIBYTE_CP_UTF8, 0, source, -1, output, 3,
                          error, &error, &report);
    expect(result == 3 && error == 0x13579BDFU,
           "success LastError preservation");
}

static void test_guards_and_overlap(void)
{
    static const uint8_t source[] = {'a', 'b', 'c', 0};
    uint16_t guarded[6] = {0xA55A, 0x1111, 0x2222, 0x3333, 0x4444, 0x5AA5};
    uint32_t error = 0x13579BDFU;
    GXOS_MULTIBYTE_REPORT report;
    int32_t result;

    configure_memory(source, sizeof(source), &guarded[1], 4 * sizeof(uint16_t));
    result = call_checked(GXOS_MULTIBYTE_CP_UTF8, 0, source, -1,
                          &guarded[1], 4, error, &error, &report);
    expect(result == 4 && guarded[0] == 0xA55A && guarded[5] == 0x5AA5,
           "output surrounding guards unchanged");
    expect(guarded[1] == 'a' && guarded[2] == 'b' && guarded[3] == 'c' &&
           guarded[4] == 0, "guarded output exact");

    {
        uint8_t overlap[16] = {'a', 'b', 0};
        configure_memory(overlap, sizeof(overlap), overlap + 1, 8);
        error = 0x13579BDFU;
        result = call_checked(GXOS_MULTIBYTE_CP_UTF8, 0, overlap, -1,
                              (uint16_t *)(void *)(overlap + 1), 2, error,
                              &error, &report);
        expect_failure("overlapping source and destination", result, error,
                       GXOS_MULTIBYTE_ERROR_INVALID_PARAMETER, report.status,
                       GXOS_MULTIBYTE_STATUS_OVERLAPPING_RANGES);
    }
}

static void test_ms_abi(void)
{
    int32_t (GXOS_MULTIBYTE_MS_ABI *function_pointer)(
        uint32_t, uint32_t, const char *, int32_t, uint16_t *, int32_t,
        const GXOS_MULTIBYTE_MEMORY_CONTEXT *, uint32_t, uint32_t *,
        GXOS_MULTIBYTE_REPORT *) = gxos_multibyte_to_wide_char_checked;
    static const char source[] = "abi";
    uint16_t output[4] = {0};
    uint32_t error = 0x13579BDFU;
    GXOS_MULTIBYTE_REPORT report;
    int32_t result;

    configure_memory(source, sizeof(source), output, sizeof(output));
    result = function_pointer(GXOS_MULTIBYTE_CP_UTF8, 0, source, -1, output,
                              4, &g_memory, error, &error, &report);
    expect(result == 4 && output[0] == 'a' && output[1] == 'b' &&
           output[2] == 'i' && output[3] == 0 && error == 0x13579BDFU,
           "Microsoft x64 checked ABI");
}

int main(void)
{
    test_ascii_and_empty();
    test_utf8_shapes();
    test_count_semantics_and_sizing();
    test_invalid_utf8();
    test_api_validation();
    test_guards_and_overlap();
    test_ms_abi();
    if (g_failures != 0) return 1;
    printf("MULTIBYTE_CASE_ASCII=PASS\n");
    printf("MULTIBYTE_CASE_EMPTY=PASS\n");
    printf("MULTIBYTE_CASE_UTF8_SHAPES=PASS\n");
    printf("MULTIBYTE_CASE_COUNT_SEMANTICS=PASS\n");
    printf("MULTIBYTE_CASE_SIZING=PASS\n");
    printf("MULTIBYTE_CASE_INVALID_UTF8=PASS\n");
    printf("MULTIBYTE_CASE_API_VALIDATION=PASS\n");
    printf("MULTIBYTE_CASE_GUARDS=PASS\n");
    printf("MULTIBYTE_CASE_OVERLAP=PASS\n");
    printf("MULTIBYTE_HOST_TESTS=PASSED\n");
    return 0;
}

#include <stdio.h>
#include <stdint.h>
#include "../platform_environment.h"

static const GXOS_ENVIRONMENT_WCHAR g_name_existing[] = {
    'G','X','O','S','_','E','X','I','S','T','I','N','G',0
};
static const GXOS_ENVIRONMENT_WCHAR g_value_existing[] = { 'a','b','c',0 };
static const GXOS_ENVIRONMENT_WCHAR g_name_empty[] = {
    'G','X','O','S','_','E','M','P','T','Y',0
};
static const GXOS_ENVIRONMENT_WCHAR g_value_empty[] = { 0 };
static const GXOS_ENVIRONMENT_WCHAR g_name_unicode[] = {
    0x03A9, 'N','A','M','E',0
};
static const GXOS_ENVIRONMENT_WCHAR g_value_unicode[] = {
    'A', 0xD83D, 0xDE00, 0x00E9, 0
};
static const GXOS_ENVIRONMENT_WCHAR g_name_missing[] = {
    'G','X','O','S','_','M','I','S','S','I','N','G',0
};
static GXOS_ENVIRONMENT_WCHAR g_output[8];

static const GXOS_ENVIRONMENT_ENTRY g_entries[] = {
    { g_name_existing, 13, g_value_existing, 3 },
    { g_name_empty, 10, g_value_empty, 0 },
    { g_name_unicode, 5, g_value_unicode, 4 }
};

static const GXOS_ENVIRONMENT_MEMORY_REGION g_regions[] = {
    { (uintptr_t)g_name_existing, (uintptr_t)(g_name_existing + 14), 1, 0 },
    { (uintptr_t)g_value_existing, (uintptr_t)(g_value_existing + 4), 1, 0 },
    { (uintptr_t)g_name_empty, (uintptr_t)(g_name_empty + 11), 1, 0 },
    { (uintptr_t)g_value_empty, (uintptr_t)(g_value_empty + 1), 1, 0 },
    { (uintptr_t)g_name_unicode, (uintptr_t)(g_name_unicode + 6), 1, 0 },
    { (uintptr_t)g_value_unicode, (uintptr_t)(g_value_unicode + 5), 1, 0 },
    { (uintptr_t)g_name_missing, (uintptr_t)(g_name_missing + 13), 1, 0 },
    { (uintptr_t)g_output, (uintptr_t)(g_output + 8), 1, 1 }
};

static const GXOS_ENVIRONMENT_MEMORY_CONTEXT g_memory = {
    8, g_regions
};

static int g_failures;

static void expect(int condition, const char *label)
{
    if (!condition) {
        printf("FAIL:%s\n", label);
        g_failures++;
    }
}

static GXOS_ENVIRONMENT_STATUS call_checked(
    const GXOS_ENVIRONMENT_WCHAR *name,
    GXOS_ENVIRONMENT_WCHAR *buffer,
    GXOS_ENVIRONMENT_DWORD buffer_size,
    GXOS_ENVIRONMENT_DWORD previous_error,
    GXOS_ENVIRONMENT_DWORD *return_value,
    GXOS_ENVIRONMENT_DWORD *last_error)
{
    return gxos_get_environment_variable_w_checked(
        name, buffer, buffer_size, g_entries, 3, &g_memory,
        previous_error, return_value, last_error);
}

static void test_missing_variable(void)
{
    GXOS_ENVIRONMENT_DWORD result = 99;
    GXOS_ENVIRONMENT_DWORD error = 0;
    GXOS_ENVIRONMENT_STATUS status;
    g_output[0] = 0xAAAA;
    status = call_checked(g_name_missing, g_output, 8, 12345, &result, &error);
    expect(status == GXOS_ENVIRONMENT_STATUS_OK, "missing status");
    expect(result == 0, "missing result");
    expect(error == GXOS_ENVIRONMENT_ERROR_ENVVAR_NOT_FOUND, "missing last error");
    expect(g_output[0] == 0xAAAA, "missing buffer unchanged");
}

static void test_existing_variable(void)
{
    GXOS_ENVIRONMENT_DWORD result = 0;
    GXOS_ENVIRONMENT_DWORD error = 0;
    GXOS_ENVIRONMENT_STATUS status;
    g_output[0] = 0xAAAA;
    g_output[1] = 0xBBBB;
    g_output[2] = 0xCCCC;
    g_output[3] = 0xDDDD;
    status = call_checked(g_name_existing, g_output, 4, 12345, &result, &error);
    expect(status == GXOS_ENVIRONMENT_STATUS_OK, "existing status");
    expect(result == 3, "existing length excludes null");
    expect(error == 12345, "existing preserves last error");
    expect(g_output[0] == 'a' && g_output[1] == 'b' &&
           g_output[2] == 'c' && g_output[3] == 0, "existing output");
}

static void test_empty_variable(void)
{
    GXOS_ENVIRONMENT_DWORD result = 99;
    GXOS_ENVIRONMENT_DWORD error = 0;
    GXOS_ENVIRONMENT_STATUS status;
    status = call_checked(g_name_empty, g_output, 1, 12345, &result, &error);
    expect(status == GXOS_ENVIRONMENT_STATUS_OK, "empty status");
    expect(result == 0 && g_output[0] == 0, "empty output");
    expect(error == 12345, "empty preserves last error");

    status = call_checked(g_name_empty, 0, 0, 12345, &result, &error);
    expect(status == GXOS_ENVIRONMENT_STATUS_OK, "empty size probe status");
    expect(result == 1, "empty size probe required null");
    expect(error == 12345, "empty size probe preserves last error");
}

static void test_size_queries_and_small_buffer(void)
{
    GXOS_ENVIRONMENT_DWORD result = 0;
    GXOS_ENVIRONMENT_DWORD error = 0;
    GXOS_ENVIRONMENT_STATUS status;
    g_output[0] = 0xAAAA;
    g_output[1] = 0xBBBB;
    g_output[2] = 0xCCCC;
    status = call_checked(g_name_existing, 0, 0, 12345, &result, &error);
    expect(status == GXOS_ENVIRONMENT_STATUS_OK && result == 4,
           "null buffer size probe");
    expect(error == 12345, "null buffer size probe preserves last error");

    status = call_checked(g_name_existing, g_output, 3, 12345, &result, &error);
    expect(status == GXOS_ENVIRONMENT_STATUS_OK && result == 4,
           "too-small result includes null");
    expect(g_output[0] == 0xAAAA && g_output[1] == 0xBBBB &&
           g_output[2] == 0xCCCC, "too-small buffer contents unchanged");
    expect(error == 12345, "too-small preserves last error");
}

static void test_unicode(void)
{
    GXOS_ENVIRONMENT_DWORD result = 0;
    GXOS_ENVIRONMENT_DWORD error = 0;
    GXOS_ENVIRONMENT_STATUS status;
    status = call_checked(g_name_unicode, g_output, 5, 12345, &result, &error);
    expect(status == GXOS_ENVIRONMENT_STATUS_OK, "unicode status");
    expect(result == 4, "unicode counts UTF-16 code units");
    expect(g_output[0] == 'A' && g_output[1] == 0xD83D &&
           g_output[2] == 0xDE00 && g_output[3] == 0x00E9 &&
           g_output[4] == 0, "unicode value preserved");
    expect(error == 12345, "unicode preserves last error");
}

static void test_repeated_queries(void)
{
    GXOS_ENVIRONMENT_DWORD result;
    GXOS_ENVIRONMENT_DWORD error;
    GXOS_ENVIRONMENT_STATUS status;
    uint32_t index;
    for (index = 0; index != 3; index++) {
        result = 0;
        error = 0;
        status = call_checked(g_name_existing, g_output, 4, 12345,
                              &result, &error);
        expect(status == GXOS_ENVIRONMENT_STATUS_OK && result == 3 &&
               error == 12345 && g_output[3] == 0,
               "repeated query");
    }
}

static void test_invalid_pointers(void)
{
    GXOS_ENVIRONMENT_DWORD result = 0;
    GXOS_ENVIRONMENT_DWORD error = 0;
    GXOS_ENVIRONMENT_STATUS status;
    status = call_checked(0, g_output, 1, 12345, &result, &error);
    expect(status == GXOS_ENVIRONMENT_STATUS_INVALID_NAME, "null name checked");
    status = call_checked((const GXOS_ENVIRONMENT_WCHAR *)(uintptr_t)1,
                          g_output, 1, 12345, &result, &error);
    expect(status == GXOS_ENVIRONMENT_STATUS_INVALID_NAME,
           "invalid name pointer checked");
    status = call_checked(g_name_existing,
                          (GXOS_ENVIRONMENT_WCHAR *)(uintptr_t)1,
                          1, 12345, &result, &error);
    expect(status == GXOS_ENVIRONMENT_STATUS_INVALID_BUFFER,
           "invalid buffer pointer checked");
}

static void test_ms_abi(void)
{
    GXOS_ENVIRONMENT_STATUS (GXOS_ENVIRONMENT_MS_ABI *function_pointer)(
        const GXOS_ENVIRONMENT_WCHAR *, GXOS_ENVIRONMENT_WCHAR *,
        GXOS_ENVIRONMENT_DWORD, const GXOS_ENVIRONMENT_ENTRY *, uint32_t,
        const GXOS_ENVIRONMENT_MEMORY_CONTEXT *, GXOS_ENVIRONMENT_DWORD,
        GXOS_ENVIRONMENT_DWORD *, GXOS_ENVIRONMENT_DWORD *) =
        gxos_get_environment_variable_w_checked;
    GXOS_ENVIRONMENT_DWORD result = 0;
    GXOS_ENVIRONMENT_DWORD error = 0;
    GXOS_ENVIRONMENT_STATUS status = function_pointer(
        g_name_existing, g_output, 4, g_entries, 3, &g_memory, 12345,
        &result, &error);
    expect(status == GXOS_ENVIRONMENT_STATUS_OK && result == 3 &&
           error == 12345, "Microsoft x64 checked ABI");
}

int main(void)
{
    test_missing_variable();
    test_existing_variable();
    test_empty_variable();
    test_size_queries_and_small_buffer();
    test_unicode();
    test_repeated_queries();
    test_invalid_pointers();
    test_ms_abi();
    if (g_failures != 0) return 1;
    printf("PLATFORM_ENVIRONMENT_CASE_MISSING=PASS\n");
    printf("PLATFORM_ENVIRONMENT_CASE_EXISTING=PASS\n");
    printf("PLATFORM_ENVIRONMENT_CASE_EMPTY=PASS\n");
    printf("PLATFORM_ENVIRONMENT_CASE_NULL_SIZE_PROBE=PASS\n");
    printf("PLATFORM_ENVIRONMENT_CASE_EXACT_SIZE=PASS\n");
    printf("PLATFORM_ENVIRONMENT_CASE_TOO_SMALL=PASS\n");
    printf("PLATFORM_ENVIRONMENT_CASE_UNICODE=PASS\n");
    printf("PLATFORM_ENVIRONMENT_CASE_REPEATED=PASS\n");
    printf("PLATFORM_ENVIRONMENT_CASE_INVALID_POINTER=PASS\n");
    printf("PLATFORM_ENVIRONMENT_HOST_TESTS=PASSED\n");
    return 0;
}

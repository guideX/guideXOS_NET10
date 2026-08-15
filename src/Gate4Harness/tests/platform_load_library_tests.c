#include <stdint.h>
#include <stdio.h>
#include <string.h>

#include "platform_get_module_handle.h"
#include "platform_get_proc_address.h"
#include "platform_load_library.h"
#include "platform_module_registry.h"

_Static_assert(sizeof(GXOS_LOAD_LIBRARY_HMODULE) == 8, "HMODULE width");
_Static_assert(sizeof(GXOS_LOAD_LIBRARY_LPCWSTR) == 8, "LPCWSTR width");

static unsigned failures;
static uint16_t storage[1024];
static const char proc_name[] = "SetThreadDescription";
static GXOS_LOAD_LIBRARY_MEMORY_REGION region;
static GXOS_GET_PROC_ADDRESS_MEMORY_REGION proc_region;
static GXOS_LOAD_LIBRARY_MEMORY_CONTEXT memory;

static void expect(int condition, const char *name)
{
    if (!condition) {
        ++failures;
        printf("FAIL:%s\n", name);
    }
}

static GXOS_LOAD_LIBRARY_LPCWSTR write_name(uint32_t offset, const char *ascii)
{
    uint32_t index = 0;
    while (ascii[index] != 0) {
        storage[offset + index] = (uint16_t)(uint8_t)ascii[index];
        ++index;
    }
    storage[offset + index] = 0;
    return &storage[offset];
}

static GXOS_LOAD_LIBRARY_STATUS load(
    GXOS_LOAD_LIBRARY_LPCWSTR name,
    GXOS_LOAD_LIBRARY_HFILE hfile,
    uint32_t flags,
    uint32_t previous_error,
    GXOS_LOAD_LIBRARY_HMODULE *result,
    uint32_t *last_error,
    GXOS_LOAD_LIBRARY_REPORT *report)
{
    return gxos_load_library_ex_checked(name, hfile, flags, &memory,
                                         previous_error, result, last_error,
                                         report);
}

static void test_exact_request_and_stable_handle(void)
{
    GXOS_LOAD_LIBRARY_REPORT report;
    GXOS_LOAD_LIBRARY_HMODULE first = 0;
    GXOS_LOAD_LIBRARY_HMODULE second = 0;
    uint32_t error = 0;
    const GXOS_LOAD_LIBRARY_HMODULE expected =
        gxos_module_registry_kernel32_handle();

    expect(load(write_name(0, "kernel32"), 0,
                GXOS_LOAD_LIBRARY_SEARCH_SYSTEM32, 0x13572468U,
                &first, &error, &report) == GXOS_LOAD_LIBRARY_STATUS_OK,
           "exact NativeAOT basename succeeds");
    expect(first == expected && first != 0, "returned handle is registered");
    expect(report.name_length == 8 && report.name_terminator ==
               (uintptr_t)&storage[8], "exact UTF-16 length and terminator");
    expect(report.name_matches_kernel32 == 1 &&
               report.system32_search_applied == 1,
           "kernel32 and system32 semantics recorded");
    expect(error == 0x13572468U && report.last_error_before == error &&
               report.last_error_after == error,
           "success preserves LastError");

    expect(load(write_name(32, "KERNEL32.DLL"), 0,
                GXOS_LOAD_LIBRARY_SEARCH_SYSTEM32, error,
                &second, &error, &report) == GXOS_LOAD_LIBRARY_STATUS_OK,
           "case-insensitive DLL spelling succeeds");
    expect(second == first && second == expected,
           "repeated names return stable handle");
    expect(load(write_name(64, "kernel32.dll"), 0,
                GXOS_LOAD_LIBRARY_SEARCH_SYSTEM32, error,
                &second, &error, &report) == GXOS_LOAD_LIBRARY_STATUS_OK &&
               second == expected,
           "lowercase DLL spelling succeeds");
}

static void test_parameter_and_name_failures(void)
{
    GXOS_LOAD_LIBRARY_REPORT report;
    GXOS_LOAD_LIBRARY_HMODULE result = (GXOS_LOAD_LIBRARY_HMODULE)0x1234;
    uint32_t error = 0;
    uint32_t index;

    expect(load(0, 0, GXOS_LOAD_LIBRARY_SEARCH_SYSTEM32, 41,
                &result, &error, &report) ==
               GXOS_LOAD_LIBRARY_STATUS_INVALID_PARAMETER &&
               report.name_is_null == 1 && result == 0 && error == 87,
           "NULL module name is invalid");

    expect(load((GXOS_LOAD_LIBRARY_LPCWSTR)(uintptr_t)0x1000000000000ULL,
                0, GXOS_LOAD_LIBRARY_SEARCH_SYSTEM32, 41,
                &result, &error, &report) ==
               GXOS_LOAD_LIBRARY_STATUS_NONCANONICAL_NAME && error == 87,
           "noncanonical module pointer is rejected");

    expect(load((GXOS_LOAD_LIBRARY_LPCWSTR)(uintptr_t)0x700000000000ULL,
                0, GXOS_LOAD_LIBRARY_SEARCH_SYSTEM32, 41,
                &result, &error, &report) ==
               GXOS_LOAD_LIBRARY_STATUS_UNREADABLE_NAME && error == 87,
           "unreadable module pointer is rejected");

    for (index = 0; index != GXOS_LOAD_LIBRARY_MAX_NAME_CODE_UNITS; ++index) {
        storage[128 + index] = (uint16_t)'X';
    }
    expect(load(&storage[128], 0, GXOS_LOAD_LIBRARY_SEARCH_SYSTEM32, 41,
                &result, &error, &report) ==
               GXOS_LOAD_LIBRARY_STATUS_NAME_SCAN_LIMIT && error == 87,
           "unterminated module name is bounded");

    expect(load(write_name(400, "kernel32"), (GXOS_LOAD_LIBRARY_HFILE)1,
                GXOS_LOAD_LIBRARY_SEARCH_SYSTEM32, 41,
                &result, &error, &report) ==
               GXOS_LOAD_LIBRARY_STATUS_INVALID_HFILE && error == 87,
           "non-null hFile is rejected");

    expect(load(write_name(420, "kernel32"), 0, 0, 41,
                &result, &error, &report) ==
               GXOS_LOAD_LIBRARY_STATUS_UNSUPPORTED_FLAGS && error == 87,
           "unsupported flags are rejected");

    expect(load(write_name(440, "user32.dll"), 0,
                GXOS_LOAD_LIBRARY_SEARCH_SYSTEM32, 41,
                &result, &error, &report) ==
               GXOS_LOAD_LIBRARY_STATUS_MODULE_NOT_FOUND && error == 126,
           "unsupported module fails with module-not-found");

    expect(load(write_name(460, "C:\\Windows\\System32\\kernel32.dll"), 0,
                GXOS_LOAD_LIBRARY_SEARCH_SYSTEM32, 41,
                &result, &error, &report) ==
               GXOS_LOAD_LIBRARY_STATUS_UNSUPPORTED_PATH &&
               report.name_has_path == 1 && error == 126,
           "path is not silently normalized");

    region.readable = 0;
    expect(load(&storage[0], 0, GXOS_LOAD_LIBRARY_SEARCH_SYSTEM32, 41,
                &result, &error, &report) ==
               GXOS_LOAD_LIBRARY_STATUS_UNREADABLE_NAME && error == 87,
           "unreadable region is rejected");
    region.readable = 1;
}

static void test_registered_handle_interactions(void)
{
    GXOS_LOAD_LIBRARY_REPORT load_report;
    GXOS_LOAD_LIBRARY_HMODULE handle = 0;
    uint32_t error = 0;
    GXOS_GET_PROC_ADDRESS_MEMORY_CONTEXT proc_memory;
    GXOS_GET_PROC_ADDRESS_REPORT proc_report;
    GXOS_GET_PROC_ADDRESS_FARPROC proc_result = 0;
    GXOS_GET_PROC_ADDRESS_DWORD proc_error = 0;
    GXOS_MAIN_MODULE_FACTS facts;
    GXOS_MODULE_HANDLE_REPORT module_report;
    GXOS_MODULE_HANDLE_HMODULE module_result = 0;
    GXOS_MODULE_HANDLE_MEMORY_REGION module_region;

    expect(load(write_name(520, "kernel32"), 0,
                GXOS_LOAD_LIBRARY_SEARCH_SYSTEM32, 9,
                &handle, &error, &load_report) == GXOS_LOAD_LIBRARY_STATUS_OK,
           "interaction load succeeds");

    proc_memory.regions = &region;
    proc_region.base = (uintptr_t)proc_name;
    proc_region.end = proc_region.base + sizeof(proc_name);
    proc_region.readable = 1;
    proc_region.executable = 0;
    proc_region.writable = 0;
    proc_memory.regions = &proc_region;
    proc_memory.region_count = 1;
    expect(gxos_get_proc_address_checked(
               handle, proc_name, &proc_memory, error,
               &proc_result, &proc_error, &proc_report) ==
               GXOS_GET_PROC_ADDRESS_STATUS_EXPORT_NOT_FOUND,
           "registered handle reaches export lookup");
    expect(proc_report.module_approved == 1 && proc_report.module_valid == 1 &&
               proc_report.export_lookup_attempted == 1 && proc_result == 0 &&
               proc_error == GXOS_GET_PROC_ADDRESS_ERROR_PROC_NOT_FOUND,
           "missing built-in export is reported truthfully");

    module_region.base = (uintptr_t)storage;
    module_region.end = module_region.base + sizeof(storage);
    module_region.readable = 1;
    module_region.executable = 0;
    module_region.writable = 1;
    memset(&facts, 0, sizeof(facts));
    facts.mapped_image_base = (uintptr_t)storage;
    facts.size_of_image = (uint32_t)sizeof(storage);
    facts.mapped_regions = &module_region;
    facts.mapped_region_count = 1;
    expect(gxos_get_module_handle_checked(
               &storage[520], &facts, &module_result, &module_report) ==
               GXOS_MODULE_HANDLE_STATUS_OK && module_result == handle &&
               module_report.selected_module ==
                   GXOS_MODULE_HANDLE_SELECTED_BUILTIN_KERNEL32,
           "GetModuleHandleW shares the registered handle");
}

int main(void)
{
    memset(storage, 0, sizeof(storage));
    region.base = (uintptr_t)storage;
    region.end = region.base + sizeof(storage);
    region.readable = 1;
    region.executable = 0;
    region.writable = 1;
    memory.regions = &region;
    memory.region_count = 1;
    test_exact_request_and_stable_handle();
    test_parameter_and_name_failures();
    test_registered_handle_interactions();
    if (failures != 0) {
        printf("LOADLIBRARYEXW_HOST_FAILURES=%u\n", failures);
        return 1;
    }
    printf("LOADLIBRARYEXW_HOST_TESTS=PASSED\n");
    return 0;
}

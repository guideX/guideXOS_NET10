#include <stdint.h>
#include <stdio.h>
#include <string.h>

#include "platform_get_module_handle.h"

_Static_assert(sizeof(GXOS_MODULE_HANDLE_HMODULE) == 8, "HMODULE width");
_Static_assert(sizeof(GXOS_MODULE_HANDLE_LPCWSTR) == 8, "LPCWSTR width");
_Static_assert(sizeof(GXOS_MODULE_HANDLE_WCHAR) == 2, "WCHAR width");

static unsigned failures;
static GXOS_MODULE_HANDLE_LPCWSTR captured_name;

static void expect(int condition, const char *name)
{
    if (!condition) {
        ++failures;
        printf("FAIL:%s\n", name);
    }
}

static uint8_t image[0x2000];
static GXOS_MODULE_HANDLE_MEMORY_REGION image_region;

static void put16(uint8_t *address, uint16_t value)
{
    address[0] = (uint8_t)value;
    address[1] = (uint8_t)(value >> 8);
}

static void put32(uint8_t *address, uint32_t value)
{
    put16(address, (uint16_t)value);
    put16(address + 2, (uint16_t)(value >> 16));
}

static GXOS_MAIN_MODULE_FACTS valid_facts(void)
{
    GXOS_MAIN_MODULE_FACTS facts;
    memset(image, 0, sizeof(image));
    put16(image, 0x5A4D);
    put32(image + 0x3C, 0x80);
    put32(image + 0x80, 0x00004550);
    put16(image + 0x84, GXOS_MODULE_HANDLE_EXPECTED_MACHINE);
    put16(image + 0x98, GXOS_MODULE_HANDLE_EXPECTED_PE32_PLUS);
    put32(image + 0xA8, 0x100);
    put32(image + 0xD0, (uint32_t)sizeof(image));
    image_region.base = (uintptr_t)image;
    image_region.end = image_region.base + sizeof(image);
    image_region.readable = 1;
    image_region.executable = 1;
    image_region.writable = 1;
    memset(&facts, 0, sizeof(facts));
    facts.preferred_image_base = (uintptr_t)0x180000000ULL;
    facts.mapped_image_base = (uintptr_t)image;
    facts.runtime_entry_point = facts.mapped_image_base + 0x100U;
    facts.relocation_delta = (uint64_t)(facts.mapped_image_base -
                                        facts.preferred_image_base);
    facts.size_of_image = (uint32_t)sizeof(image);
    facts.size_of_headers = 0x400;
    facts.entry_point_rva = 0x100;
    facts.import_directory_rva = 0x500;
    facts.import_directory_size = 0x20;
    facts.importing_iat_rva = 0x600;
    facts.importing_iat_size = 8;
    facts.relocations_applied = 1;
    facts.mapped_regions = &image_region;
    facts.mapped_region_count = 1;
    return facts;
}

static GXOS_MODULE_HANDLE_LPCWSTR image_name(uint32_t offset, const char *ascii)
{
    GXOS_MODULE_HANDLE_WCHAR *value =
        (GXOS_MODULE_HANDLE_WCHAR *)(void *)(image + offset);
    uint32_t index = 0;
    while (ascii[index] != 0) {
        value[index] = (GXOS_MODULE_HANDLE_WCHAR)(uint8_t)ascii[index];
        ++index;
    }
    value[index] = 0;
    return value;
}

static GXOS_MODULE_HANDLE_STATUS checked(
    GXOS_MODULE_HANDLE_LPCWSTR name,
    GXOS_MAIN_MODULE_FACTS *facts,
    GXOS_MODULE_HANDLE_HMODULE *result,
    GXOS_MODULE_HANDLE_REPORT *report)
{
    return gxos_get_module_handle_checked(name, facts, result, report);
}

static void test_null_name_and_abi(void)
{
    GXOS_MAIN_MODULE_FACTS facts = valid_facts();
    GXOS_MODULE_HANDLE_REPORT report;
    GXOS_MODULE_HANDLE_HMODULE result = 0;

    expect(checked(0, &facts, &result, &report) == GXOS_MODULE_HANDLE_STATUS_OK,
           "null name succeeds");
    expect(result == facts.mapped_image_base, "null name returns mapped base");
    expect(report.name_is_null == 1 && report.output_written == 1,
           "null name report");
    expect(report.selected_module == GXOS_MODULE_HANDLE_SELECTED_MAIN_NATIVEAOT_PAYLOAD,
           "null name selected main payload");
}

static void test_observed_names_and_name_rules(void)
{
    GXOS_MAIN_MODULE_FACTS facts = valid_facts();
    GXOS_MODULE_HANDLE_REPORT report;
    GXOS_MODULE_HANDLE_HMODULE result;
    GXOS_MODULE_HANDLE_LPCWSTR name;

    result = (GXOS_MODULE_HANDLE_HMODULE)0xA5A5A5A5A5A5A5A5ULL;
    name = image_name(0x800, "ntdll.dll");
    expect(checked(name, &facts, &result, &report) ==
               GXOS_MODULE_HANDLE_STATUS_MODULE_NOT_FOUND,
           "ntdll is not a mapped module");
    expect(report.name_exact_observed_form == 1 && report.name_length == 9,
           "ntdll exact form report");
    expect(result == (GXOS_MODULE_HANDLE_HMODULE)0xA5A5A5A5A5A5A5A5ULL,
           "not-found preserves output");

    name = image_name(0x900, "KERNEL32.DLL");
    expect(checked(name, &facts, &result, &report) ==
               GXOS_MODULE_HANDLE_STATUS_OK,
           "kernel32 case-insensitive succeeds");
    expect(result == gxos_module_registry_kernel32_handle() &&
               report.selected_module ==
                   GXOS_MODULE_HANDLE_SELECTED_BUILTIN_KERNEL32,
           "kernel32 returns registered builtin handle");
    name = image_name(0x980, "kernel32");
    expect(checked(name, &facts, &result, &report) ==
               GXOS_MODULE_HANDLE_STATUS_OK &&
               result == gxos_module_registry_kernel32_handle(),
           "kernel32 basename succeeds");
    name = image_name(0xA00, "C:\\Windows\\System32\\ntdll.dll");
    expect(checked(name, &facts, &result, &report) ==
               GXOS_MODULE_HANDLE_STATUS_UNSUPPORTED_NAME &&
               report.name_has_path == 1,
           "path name is not silently normalized");
    name = image_name(0xB00, "not-a-loaded-module.dll");
    expect(checked(name, &facts, &result, &report) ==
               GXOS_MODULE_HANDLE_STATUS_UNSUPPORTED_NAME,
           "unsupported name rejected");
    name = image_name(0xC00, "");
    expect(checked(name, &facts, &result, &report) ==
               GXOS_MODULE_HANDLE_STATUS_UNSUPPORTED_NAME &&
               report.name_length == 0,
           "empty name rejected");
}

static void test_pointer_and_termination_rules(void)
{
    GXOS_MAIN_MODULE_FACTS facts = valid_facts();
    GXOS_MODULE_HANDLE_REPORT report;
    GXOS_MODULE_HANDLE_HMODULE result = 0x1234;
    GXOS_MODULE_HANDLE_WCHAR *unterminated =
        (GXOS_MODULE_HANDLE_WCHAR *)(void *)(image + 0x1000);
    uint32_t index;

    for (index = 0; index != GXOS_MODULE_HANDLE_MAX_NAME_CODE_UNITS; ++index) {
        unterminated[index] = (GXOS_MODULE_HANDLE_WCHAR)'X';
    }
    expect(checked(unterminated, &facts, &result, &report) ==
               GXOS_MODULE_HANDLE_STATUS_NAME_SCAN_LIMIT,
           "bounded name scan limit");
    expect(result == 0x1234 && report.name_length == 0,
           "scan limit preserves output");
    expect(checked((GXOS_MODULE_HANDLE_LPCWSTR)(uintptr_t)0x0001000000000000ULL,
                   &facts, &result, &report) ==
               GXOS_MODULE_HANDLE_STATUS_NONCANONICAL_NAME,
           "noncanonical name rejected");
    expect(checked((GXOS_MODULE_HANDLE_LPCWSTR)(uintptr_t)0x700000000000ULL,
                   &facts, &result, &report) ==
               GXOS_MODULE_HANDLE_STATUS_UNREADABLE_NAME,
           "unmapped canonical name rejected");
    image_region.readable = 0;
    expect(checked(image_name(0x1200, "ntdll.dll"), &facts, &result, &report) ==
               GXOS_MODULE_HANDLE_STATUS_UNREADABLE_NAME,
           "unreadable name region rejected");
    image_region.readable = 1;
}

static void expect_invalid_facts(GXOS_MAIN_MODULE_FACTS *facts,
                                 GXOS_MODULE_HANDLE_STATUS expected,
                                 const char *name)
{
    GXOS_MAIN_MODULE_FACTS before = *facts;
    GXOS_MODULE_HANDLE_REPORT report;
    GXOS_MODULE_HANDLE_HMODULE result =
        (GXOS_MODULE_HANDLE_HMODULE)0xCAFEBABECAFEBABEULL;
    GXOS_MODULE_HANDLE_STATUS status = checked(0, facts, &result, &report);
    expect(status == expected, name);
    expect(result == (GXOS_MODULE_HANDLE_HMODULE)0xCAFEBABECAFEBABEULL,
           "invalid facts preserve output");
    expect(memcmp(facts, &before, sizeof(before)) == 0,
           "invalid facts preserve input facts");
}

static void test_image_validation(void)
{
    GXOS_MAIN_MODULE_FACTS facts;

    facts = valid_facts();
    put16(image, 0x1234);
    expect_invalid_facts(&facts, GXOS_MODULE_HANDLE_STATUS_INVALID_DOS_HEADER,
                         "invalid DOS header");
    facts = valid_facts();
    put32(image + 0x80, 0x12345678);
    expect_invalid_facts(&facts, GXOS_MODULE_HANDLE_STATUS_INVALID_NT_HEADER,
                         "invalid NT signature");
    facts = valid_facts();
    put16(image + 0x84, 0x014C);
    expect_invalid_facts(&facts, GXOS_MODULE_HANDLE_STATUS_WRONG_MACHINE,
                         "wrong machine");
    facts = valid_facts();
    put16(image + 0x98, 0x10B);
    expect_invalid_facts(&facts, GXOS_MODULE_HANDLE_STATUS_WRONG_OPTIONAL_HEADER,
                         "wrong optional header");
    facts = valid_facts();
    put32(image + 0xD0, 0x1000);
    expect_invalid_facts(&facts, GXOS_MODULE_HANDLE_STATUS_INVALID_IMAGE_RANGE,
                         "image size mismatch");
    facts = valid_facts();
    facts.entry_point_rva = 0x101;
    expect_invalid_facts(&facts, GXOS_MODULE_HANDLE_STATUS_INVALID_IMAGE_RANGE,
                         "entry RVA mismatch");
    facts = valid_facts();
    facts.runtime_entry_point += 1;
    expect_invalid_facts(&facts, GXOS_MODULE_HANDLE_STATUS_INVALID_IMAGE_RANGE,
                         "entry pointer mismatch");
    facts = valid_facts();
    facts.import_directory_rva = 0;
    expect_invalid_facts(&facts, GXOS_MODULE_HANDLE_STATUS_INVALID_IMAGE_RANGE,
                         "missing import directory");
    facts = valid_facts();
    facts.import_directory_rva = 0x1FF0;
    facts.import_directory_size = 0x40;
    expect_invalid_facts(&facts, GXOS_MODULE_HANDLE_STATUS_INVALID_IMAGE_RANGE,
                         "import range overflow");
    facts = valid_facts();
    facts.importing_iat_rva = 0;
    expect_invalid_facts(&facts, GXOS_MODULE_HANDLE_STATUS_INVALID_IMAGE_RANGE,
                         "missing importing IAT");
    facts = valid_facts();
    facts.importing_iat_rva = 0x1FFC;
    expect_invalid_facts(&facts, GXOS_MODULE_HANDLE_STATUS_INVALID_IMAGE_RANGE,
                         "IAT range overflow");
    facts = valid_facts();
    facts.importing_iat_size = 4;
    expect_invalid_facts(&facts, GXOS_MODULE_HANDLE_STATUS_INVALID_IMAGE_RANGE,
                         "short IAT");
    facts = valid_facts();
    facts.relocation_delta += 1;
    expect_invalid_facts(&facts, GXOS_MODULE_HANDLE_STATUS_RELOCATION_MISMATCH,
                         "relocation delta mismatch");
}

static void test_fact_shape_rejection(void)
{
    GXOS_MAIN_MODULE_FACTS facts;

    facts = valid_facts();
    facts.mapped_image_base = (uintptr_t)0x0001000000000000ULL;
    expect_invalid_facts(&facts, GXOS_MODULE_HANDLE_STATUS_INVALID_MODULE_BASE,
                         "noncanonical module base");
    facts = valid_facts();
    facts.preferred_image_base = 0;
    expect_invalid_facts(&facts, GXOS_MODULE_HANDLE_STATUS_INVALID_MODULE_FACTS,
                         "zero preferred base");
    facts = valid_facts();
    facts.size_of_headers = 0;
    expect_invalid_facts(&facts, GXOS_MODULE_HANDLE_STATUS_INVALID_MODULE_FACTS,
                         "zero header size");
    facts = valid_facts();
    facts.size_of_headers = facts.size_of_image + 1;
    expect_invalid_facts(&facts, GXOS_MODULE_HANDLE_STATUS_INVALID_MODULE_FACTS,
                         "headers beyond image");
    facts = valid_facts();
    facts.relocations_applied = 0;
    expect_invalid_facts(&facts, GXOS_MODULE_HANDLE_STATUS_INVALID_MODULE_FACTS,
                         "relocations not applied");
    facts = valid_facts();
    facts.mapped_regions = 0;
    expect_invalid_facts(&facts, GXOS_MODULE_HANDLE_STATUS_INVALID_MODULE_FACTS,
                         "missing mapped regions");
    facts = valid_facts();
    facts.mapped_region_count = 0;
    expect_invalid_facts(&facts, GXOS_MODULE_HANDLE_STATUS_INVALID_MODULE_FACTS,
                         "empty mapped regions");
    facts = valid_facts();
    expect(checked(0, 0, &(GXOS_MODULE_HANDLE_HMODULE){0},
                   &(GXOS_MODULE_HANDLE_REPORT){0}) ==
               GXOS_MODULE_HANDLE_STATUS_INVALID_MODULE_FACTS,
           "null facts rejected");
    facts = valid_facts();
    expect(checked(0, &facts, 0, &(GXOS_MODULE_HANDLE_REPORT){0}) ==
               GXOS_MODULE_HANDLE_STATUS_INVALID_MODULE_FACTS,
           "null result rejected");
}

static GXOS_MODULE_HANDLE_HMODULE GXOS_MODULE_HANDLE_MS_ABI
capture_abi(GXOS_MODULE_HANDLE_LPCWSTR name)
{
    captured_name = name;
    return (GXOS_MODULE_HANDLE_HMODULE)0x1122334455667788ULL;
}

static void test_abi_declaration(void)
{
    GXOS_MODULE_HANDLE_HMODULE (GXOS_MODULE_HANDLE_MS_ABI *function)(
        GXOS_MODULE_HANDLE_LPCWSTR) = capture_abi;
    GXOS_MODULE_HANDLE_WCHAR name[] = {'a', 0};
    expect(function(name) == (GXOS_MODULE_HANDLE_HMODULE)0x1122334455667788ULL,
           "ms ABI callback result");
    expect(captured_name == name, "ms ABI callback argument");
}

static void test_configured_wrapper(void)
{
    GXOS_MAIN_MODULE_FACTS facts = valid_facts();
    GXOS_MODULE_HANDLE_LPCWSTR name = image_name(0x1400, "ntdll.dll");

    gxos_get_module_handle_configure(&facts);
    expect(gxos_get_module_handle_w(0) == facts.mapped_image_base,
           "configured wrapper null query");
    expect(gxos_get_module_handle_w(name) == 0,
           "configured wrapper named failure");
    gxos_get_module_handle_configure(0);
    expect(gxos_get_module_handle_w(0) == 0,
           "unconfigured wrapper failure");
}

int main(void)
{
    test_null_name_and_abi();
    test_observed_names_and_name_rules();
    test_pointer_and_termination_rules();
    test_image_validation();
    test_fact_shape_rejection();
    test_abi_declaration();
    test_configured_wrapper();
    if (failures != 0) {
        printf("GETMODULEHANDLEW_HOST_FAILURES=%u\n", failures);
        return 1;
    }
    puts("GETMODULEHANDLEW_HOST_TESTS_OK");
    return 0;
}

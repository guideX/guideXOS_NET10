#include <stdint.h>
#include <stdio.h>

#include "platform_get_proc_address.h"

static uint8_t storage[4096];
static GXOS_GET_PROC_ADDRESS_MEMORY_REGION region;
static GXOS_GET_PROC_ADDRESS_MEMORY_CONTEXT memory;
static uint32_t failures;
static GXOS_GET_PROC_ADDRESS_HMODULE observed_module;
static GXOS_GET_PROC_ADDRESS_LPCSTR observed_identifier;

static void expect(int condition, const char *message)
{
    if (!condition) {
        ++failures;
        printf("FAIL: %s\n", message);
    }
}

static void clear_storage(void)
{
    uint32_t index;
    for (index = 0; index != sizeof(storage); ++index) storage[index] = 0;
    region.base = (uintptr_t)storage;
    region.end = (uintptr_t)storage + sizeof(storage);
    region.readable = 1;
    region.executable = 0;
    region.writable = 1;
    memory.regions = &region;
    memory.region_count = 1;
}

static GXOS_GET_PROC_ADDRESS_LPCSTR put_name(uint32_t offset, const uint8_t *bytes,
                                             uint32_t length)
{
    uint32_t index;
    for (index = 0; index != length; ++index) storage[offset + index] = bytes[index];
    storage[offset + length] = 0;
    return (GXOS_GET_PROC_ADDRESS_LPCSTR)(storage + offset);
}

static GXOS_GET_PROC_ADDRESS_STATUS checked(
    GXOS_GET_PROC_ADDRESS_HMODULE module_handle,
    GXOS_GET_PROC_ADDRESS_LPCSTR identifier,
    GXOS_GET_PROC_ADDRESS_DWORD previous_error,
    GXOS_GET_PROC_ADDRESS_FARPROC *result,
    GXOS_GET_PROC_ADDRESS_REPORT *report)
{
    GXOS_GET_PROC_ADDRESS_DWORD last_error = previous_error;
    return gxos_get_proc_address_checked(
        module_handle, identifier, &memory, previous_error, result,
        &last_error, report);
}

static void test_abi_and_classification(void)
{
    GXOS_PROC_IDENTIFIER identifier;
    GXOS_GET_PROC_ADDRESS_REPORT report;
    const uint8_t name[] = "RtlDllShutdownInProgress";
    uintptr_t raw_name = (uintptr_t)put_name(64, name, sizeof(name) - 1U);

    expect(sizeof(GXOS_GET_PROC_ADDRESS_HMODULE) == 8,
           "HMODULE is 64 bits");
    expect(sizeof(GXOS_GET_PROC_ADDRESS_LPCSTR) == 8,
           "LPCSTR is 64 bits");
    expect(sizeof(GXOS_GET_PROC_ADDRESS_FARPROC) == 8,
           "FARPROC is 64 bits");
    expect(gxos_get_proc_address_classify((uintptr_t)0x1234U,
                                          &identifier, &report) ==
               GXOS_GET_PROC_ADDRESS_STATUS_OK,
           "small value classifies");
    expect(identifier.kind == GXOS_PROC_IDENTIFIER_ORDINAL &&
               identifier.ordinal == 0x1234U &&
               identifier.high_order_bits == 0 &&
               identifier.name == 0,
           "small value is ordinal without a name pointer");
    expect(gxos_get_proc_address_classify(raw_name, &identifier, &report) ==
               GXOS_GET_PROC_ADDRESS_STATUS_OK,
           "name pointer classifies");
    expect(identifier.kind == GXOS_PROC_IDENTIFIER_NAME &&
               identifier.name == (GXOS_GET_PROC_ADDRESS_LPCSTR)raw_name &&
               identifier.low_order_word == (uint16_t)raw_name &&
               identifier.high_order_bits == ((uint64_t)raw_name >> 16),
           "full name pointer is preserved");
}

static void test_live_null_name(void)
{
    const uint8_t name[] = "RtlDllShutdownInProgress";
    GXOS_GET_PROC_ADDRESS_REPORT report;
    GXOS_GET_PROC_ADDRESS_FARPROC result =
        (GXOS_GET_PROC_ADDRESS_FARPROC)(uintptr_t)0x1122334455667788ULL;
    GXOS_GET_PROC_ADDRESS_STATUS status;

    clear_storage();
    status = checked(0, put_name(128, name, sizeof(name) - 1U), 0xCBU,
                     &result, &report);
    expect(status == GXOS_GET_PROC_ADDRESS_STATUS_INVALID_MODULE_HANDLE,
           "null live module is invalid-module status");
    expect(result == (GXOS_GET_PROC_ADDRESS_FARPROC)0,
           "null live module returns null");
    expect(report.module_handle == 0 && report.module_is_null == 1 &&
               report.module_approved == 0 && report.module_valid == 0,
           "null module is not the main executable");
    expect(report.identifier_kind == GXOS_PROC_IDENTIFIER_NAME &&
               report.name_length == sizeof(name) - 1U &&
               report.name_terminated == 1 && report.name_readable == 1 &&
               report.name_all_7bit_ascii == 1 && report.name_high_bit_count == 0,
           "exact live ANSI name is read as bytes");
    expect(report.name_terminator == report.name_pointer + report.name_length,
           "name terminator address is exact");
    expect(report.export_lookup_attempted == 0 &&
               report.last_error_before == 0xCBU &&
               report.last_error_after == 127U,
           "null failure has no export access and selected error");
    expect(report.name_preview_length == sizeof(name) - 1U &&
               report.name_preview[0] == 'R' &&
               report.name_preview[23] == 's',
           "exact requested name remains unchanged");
}

static void test_ordinal_is_never_dereferenced(void)
{
    GXOS_GET_PROC_ADDRESS_REPORT report;
    GXOS_GET_PROC_ADDRESS_FARPROC result = 0;
    GXOS_GET_PROC_ADDRESS_STATUS status;

    clear_storage();
    status = checked(0, (GXOS_GET_PROC_ADDRESS_LPCSTR)(uintptr_t)0x0042U,
                     0xCBU, &result, &report);
    expect(status == GXOS_GET_PROC_ADDRESS_STATUS_UNSUPPORTED_ORDINAL,
           "ordinal policy rejects unsupported ordinal");
    expect(report.identifier_kind == GXOS_PROC_IDENTIFIER_ORDINAL &&
               report.ordinal == 0x42U && report.name_readable == 0 &&
               report.name_pointer == 0 && report.export_lookup_attempted == 0,
           "ordinal path performs no pointer read");
    expect(result == (GXOS_GET_PROC_ADDRESS_FARPROC)0 &&
               report.last_error_after == 127U,
           "ordinal failure is deterministic");
}

static void test_name_safety_and_region_metadata(void)
{
    GXOS_GET_PROC_ADDRESS_REPORT report;
    GXOS_GET_PROC_ADDRESS_FARPROC result = 0;
    GXOS_GET_PROC_ADDRESS_STATUS status;
    const uint8_t high_bit_name[] = {'A', 0x80U, 'B'};
    uint32_t index;

    clear_storage();
    status = checked(0, put_name(256, high_bit_name, 3), 0xCBU,
                     &result, &report);
    expect(status == GXOS_GET_PROC_ADDRESS_STATUS_INVALID_MODULE_HANDLE &&
               report.name_all_7bit_ascii == 0 && report.name_high_bit_count == 1,
           "high-bit ANSI bytes are preserved");
    expect(report.name_region_base == region.base &&
               report.name_region_end == region.end &&
               report.name_region_readable == 1 &&
               report.name_region_writable == 1 &&
               report.name_region_executable == 0,
           "name region permissions are recorded");

    clear_storage();
    status = checked(0, (GXOS_GET_PROC_ADDRESS_LPCSTR)
                         (uintptr_t)0x0001000000000000ULL,
                     0xCBU, &result, &report);
    expect(status == GXOS_GET_PROC_ADDRESS_STATUS_NONCANONICAL_NAME,
           "noncanonical name is rejected before reading");

    clear_storage();
    region.readable = 0;
    status = checked(0, put_name(512, (const uint8_t *)"x", 1),
                     0xCBU, &result, &report);
    expect(status == GXOS_GET_PROC_ADDRESS_STATUS_UNREADABLE_NAME,
           "unreadable name is rejected");

    clear_storage();
    for (index = 0; index != GXOS_GET_PROC_ADDRESS_MAX_NAME_BYTES + 1U; ++index) {
        storage[768 + index] = 'X';
    }
    status = checked(0, (GXOS_GET_PROC_ADDRESS_LPCSTR)(storage + 768),
                     0xCBU, &result, &report);
    expect(status == GXOS_GET_PROC_ADDRESS_STATUS_NAME_SCAN_LIMIT &&
               report.name_terminated == 0 && report.name_preview_truncated == 1,
           "unterminated name is bounded");

    clear_storage();
    region.base = (uintptr_t)(storage + 1024);
    region.end = region.base + 3U;
    storage[1024] = 'a';
    storage[1025] = 'b';
    storage[1026] = 0;
    status = checked(0, (GXOS_GET_PROC_ADDRESS_LPCSTR)(storage + 1024),
                     0xCBU, &result, &report);
    expect(status == GXOS_GET_PROC_ADDRESS_STATUS_INVALID_MODULE_HANDLE &&
               report.name_length == 2 && report.name_terminator == region.end - 1,
           "terminator at final readable byte succeeds");

    clear_storage();
    region.base = (uintptr_t)(storage + 1200);
    region.end = region.base + 2U;
    storage[1200] = 'a';
    storage[1201] = 'b';
    status = checked(0, (GXOS_GET_PROC_ADDRESS_LPCSTR)(storage + 1200),
                     0xCBU, &result, &report);
    expect(status == GXOS_GET_PROC_ADDRESS_STATUS_UNREADABLE_NAME,
           "gap crossing is rejected");
}

static void test_module_policy_and_empty_name(void)
{
    GXOS_GET_PROC_ADDRESS_REPORT report;
    GXOS_GET_PROC_ADDRESS_FARPROC result = 0;
    GXOS_GET_PROC_ADDRESS_STATUS status;
    GXOS_GET_PROC_ADDRESS_LPCSTR empty;

    clear_storage();
    empty = put_name(1400, (const uint8_t *)"", 0);
    status = checked(0, empty, 0xCBU, &result, &report);
    expect(status == GXOS_GET_PROC_ADDRESS_STATUS_INVALID_MODULE_HANDLE &&
               report.identifier_kind == GXOS_PROC_IDENTIFIER_NAME &&
               report.name_length == 0,
           "empty identifier remains a named lookup");
    clear_storage();
    status = checked((GXOS_GET_PROC_ADDRESS_HMODULE)
                         0x1122334455667788ULL,
                     put_name(1500, (const uint8_t *)"Export", 6),
                     0xCBU, &result, &report);
    expect((status == GXOS_GET_PROC_ADDRESS_STATUS_MODULE_NOT_MAPPED ||
            status == GXOS_GET_PROC_ADDRESS_STATUS_INVALID_MODULE_HANDLE) &&
               report.module_handle == (GXOS_GET_PROC_ADDRESS_HMODULE)
                   0x1122334455667788ULL &&
               report.last_error_after == 6U &&
               report.export_lookup_attempted == 0,
           "full non-null handle is rejected without export parsing");
}

static GXOS_GET_PROC_ADDRESS_FARPROC GXOS_GET_PROC_ADDRESS_MS_ABI capture_abi(
    GXOS_GET_PROC_ADDRESS_HMODULE module_handle,
    GXOS_GET_PROC_ADDRESS_LPCSTR procedure_identifier)
{
    observed_module = module_handle;
    observed_identifier = procedure_identifier;
    return (GXOS_GET_PROC_ADDRESS_FARPROC)(uintptr_t)
        0x8877665544332211ULL;
}

static void test_ms_abi(void)
{
    GXOS_GET_PROC_ADDRESS_FARPROC (GXOS_GET_PROC_ADDRESS_MS_ABI *function)(
        GXOS_GET_PROC_ADDRESS_HMODULE, GXOS_GET_PROC_ADDRESS_LPCSTR) =
        capture_abi;
    const char name[] = "x";
    GXOS_GET_PROC_ADDRESS_FARPROC result = function(
        (GXOS_GET_PROC_ADDRESS_HMODULE)0xFFEEDDCCBBAA9988ULL, name);

    expect(observed_module == (GXOS_GET_PROC_ADDRESS_HMODULE)
               0xFFEEDDCCBBAA9988ULL,
           "RCX preserves the full HMODULE");
    expect(observed_identifier == name, "RDX preserves LPCSTR");
    expect((uintptr_t)result == (uintptr_t)0x8877665544332211ULL,
           "FARPROC result is returned in the pointer-sized result register");
}

int main(void)
{
    test_abi_and_classification();
    test_live_null_name();
    test_ordinal_is_never_dereferenced();
    test_name_safety_and_region_metadata();
    test_module_policy_and_empty_name();
    test_ms_abi();
    if (failures != 0) {
        printf("GETPROCADDRESS_HOST_FAILURES=%u\n", failures);
        return 1;
    }
    puts("GETPROCADDRESS_HOST_TESTS_OK");
    return 0;
}

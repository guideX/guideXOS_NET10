#include <stdint.h>
#include <stdio.h>
#include <string.h>

#include "../crt_onexit.h"

static unsigned g_callback_calls;

static int GXOS_CRT_ONEXIT_MS_ABI test_callback(void)
{
    ++g_callback_calls;
    return 0;
}

static int expect(int condition, const char *name)
{
    if (!condition) {
        printf("CRT_REGISTER_TEST_FAILURE=%s\n", name);
        return 1;
    }
    return 0;
}

int main(void)
{
    const uintptr_t cookie = (uintptr_t)0xA5A5A5A5A5A5A5A5ULL;
    GXOS_CRT_ONEXIT_TABLE table;
    uintptr_t storage[4];
    GXOS_CRT_ONEXIT_CONTEXT context;
    GXOS_CRT_ONEXIT_REPORT report;
    uintptr_t table_addresses[1];
    GXOS_CRT_ONEXIT_TABLE other_table;
    uintptr_t raw_first;
    uintptr_t raw_last;
    uintptr_t raw_end;
    GXOS_CRT_ONEXIT_STATUS status;
    int failures = 0;

    memset(&table, 0, sizeof(table));
    memset(storage, 0, sizeof(storage));
    memset(&context, 0, sizeof(context));
    gxos_crt_onexit_set_encoded_null(cookie);

    context.image_base = (uintptr_t)0x1000;
    context.image_end = (uintptr_t)0x00007FFFFFFFFFFFULL;
    context.encoded_null = cookie;
    context.relocations_applied = 1;
    context.region_count = 1;
    context.regions[0].base = context.image_base;
    context.regions[0].end = context.image_end;
    context.regions[0].readable = 1;
    context.regions[0].writable = 1;
    context.regions[0].executable = 1;
    context.initialized_table_count = 1;
    context.initialized_tables[0] = (uintptr_t)&table;
    failures += expect(gxos_crt_onexit_configure(&context) == 0, "context-configured");
    failures += expect(gxos_crt_initialize_onexit_table(&table) == 0,
                       "table-initialized");
    failures += expect(gxos_crt_onexit_decode_pointer((uintptr_t)table.first) == 0 &&
                       gxos_crt_onexit_decode_pointer((uintptr_t)table.last) == 0 &&
                       gxos_crt_onexit_decode_pointer((uintptr_t)table.end) == 0,
                       "encoded-empty-state");

    raw_first = (uintptr_t)table.first;
    raw_last = (uintptr_t)table.last;
    raw_end = (uintptr_t)table.end;
    status = gxos_crt_onexit_register_checked(
        &table, (GXOS_CRT_ONEXIT_T)test_callback, &report);
    failures += expect(status == GXOS_CRT_ONEXIT_STATUS_GROWTH_REQUIRED,
                       "empty-table-growth-boundary");
    failures += expect(report.growth_required == 1 && report.allocation_attempted == 0,
                       "empty-table-growth-report");
    failures += expect((uintptr_t)table.first == raw_first &&
                       (uintptr_t)table.last == raw_last &&
                       (uintptr_t)table.end == raw_end,
                       "empty-table-unchanged");
    printf("CRT_REGISTER_TEST_GROWTH_BOUNDARY=%s\n",
           failures == 0 ? "PASS" : "FAIL");

    table.first = (GXOS_CRT_ONEXIT_PVFV *)(uintptr_t)
        gxos_crt_onexit_encode_pointer((uintptr_t)&storage[0]);
    table.last = (GXOS_CRT_ONEXIT_PVFV *)(uintptr_t)
        gxos_crt_onexit_encode_pointer((uintptr_t)&storage[0]);
    table.end = (GXOS_CRT_ONEXIT_PVFV *)(uintptr_t)
        gxos_crt_onexit_encode_pointer((uintptr_t)&storage[4]);
    status = gxos_crt_onexit_register_checked(
        &table, (GXOS_CRT_ONEXIT_T)test_callback, &report);
    failures += expect(status == GXOS_CRT_ONEXIT_STATUS_OK,
                       "existing-capacity-register");
    failures += expect(report.entry_index == 0 && report.used_count == 0 &&
                       report.capacity == 4 && report.pointer_encoded == 1,
                       "existing-capacity-report");
    failures += expect(gxos_crt_onexit_decode_pointer((uintptr_t)storage[0]) ==
                       (uintptr_t)(GXOS_CRT_ONEXIT_T)test_callback,
                       "callback-encoded-in-slot");
    failures += expect(gxos_crt_onexit_decode_pointer((uintptr_t)table.last) ==
                       (uintptr_t)&storage[1], "last-advanced");
    failures += expect(g_callback_calls == 0, "callback-not-executed");

    status = gxos_crt_onexit_register_checked(&table, 0, &report);
    failures += expect(status == GXOS_CRT_ONEXIT_STATUS_OK,
                       "nullable-callback-register");
    failures += expect(gxos_crt_onexit_decode_pointer((uintptr_t)storage[1]) == 0,
                       "nullable-callback-encoded-null");
    failures += expect(g_callback_calls == 0, "nullable-callback-not-executed");

    table.last = (GXOS_CRT_ONEXIT_PVFV *)(uintptr_t)
        gxos_crt_onexit_encode_pointer((uintptr_t)&storage[4]);
    raw_first = (uintptr_t)table.first;
    raw_last = (uintptr_t)table.last;
    raw_end = (uintptr_t)table.end;
    status = gxos_crt_onexit_register_checked(
        &table, (GXOS_CRT_ONEXIT_T)test_callback, &report);
    failures += expect(status == GXOS_CRT_ONEXIT_STATUS_GROWTH_REQUIRED,
                       "full-table-growth-boundary");
    failures += expect(report.used_count == 4 && report.capacity == 4 &&
                       report.remaining_capacity == 0 && report.allocation_attempted == 0,
                       "full-table-growth-report");
    failures += expect((uintptr_t)table.first == raw_first &&
                       (uintptr_t)table.last == raw_last &&
                       (uintptr_t)table.end == raw_end,
                       "full-table-unchanged");
    printf("CRT_REGISTER_TEST_EXISTING_STORAGE=%s\n",
           failures == 0 ? "PASS" : "FAIL");

    table_addresses[0] = (uintptr_t)&table;
    failures += expect(gxos_crt_onexit_set_initialized_tables(table_addresses, 1) == 0,
                       "initialized-table-refresh");
    failures += expect(gxos_crt_onexit_status_name(
                           GXOS_CRT_ONEXIT_STATUS_GROWTH_REQUIRED) != 0,
                       "status-name");
    printf("CRT_REGISTER_TEST_LAYOUT=%s\n", failures == 0 ? "PASS" : "FAIL");

    memset(&other_table, 0, sizeof(other_table));
    failures += expect(gxos_crt_initialize_onexit_table(&other_table) == 0,
                       "second-table-initialized");
    status = gxos_crt_onexit_register_checked(
        &other_table, (GXOS_CRT_ONEXIT_T)test_callback, &report);
    failures += expect(status == GXOS_CRT_ONEXIT_STATUS_TABLE_NOT_INITIALIZED,
                       "unlisted-table-rejected");

    context.region_count = 3;
    context.regions[0].base = (uintptr_t)&table;
    context.regions[0].end = (uintptr_t)&table + sizeof(table);
    context.regions[0].readable = 1;
    context.regions[0].writable = 1;
    context.regions[0].executable = 0;
    context.regions[1].base = (uintptr_t)&storage[0];
    context.regions[1].end = (uintptr_t)&storage[4];
    context.regions[1].readable = 1;
    context.regions[1].writable = 1;
    context.regions[1].executable = 0;
    context.regions[2].base = (uintptr_t)(GXOS_CRT_ONEXIT_T)test_callback;
    context.regions[2].end = context.regions[2].base + 1;
    context.regions[2].readable = 1;
    context.regions[2].writable = 0;
    context.regions[2].executable = 1;
    context.initialized_table_count = 1;
    context.initialized_tables[0] = (uintptr_t)&table;
    failures += expect(gxos_crt_onexit_configure(&context) == 0,
                       "split-region-context-configured");

    table.first = (GXOS_CRT_ONEXIT_PVFV *)(uintptr_t)
        gxos_crt_onexit_encode_pointer((uintptr_t)&storage[3]);
    table.last = (GXOS_CRT_ONEXIT_PVFV *)(uintptr_t)
        gxos_crt_onexit_encode_pointer((uintptr_t)&storage[1]);
    table.end = (GXOS_CRT_ONEXIT_PVFV *)(uintptr_t)
        gxos_crt_onexit_encode_pointer((uintptr_t)&storage[4]);
    status = gxos_crt_onexit_register_checked(
        &table, (GXOS_CRT_ONEXIT_T)test_callback, &report);
    failures += expect(status == GXOS_CRT_ONEXIT_STATUS_INVALID_TABLE_STATE,
                       "reversed-storage-range-rejected");

    table.first = (GXOS_CRT_ONEXIT_PVFV *)(uintptr_t)
        gxos_crt_onexit_encode_pointer((uintptr_t)&storage[0] + 1U);
    table.last = table.first;
    table.end = table.first;
    status = gxos_crt_onexit_register_checked(
        &table, (GXOS_CRT_ONEXIT_T)test_callback, &report);
    failures += expect(status == GXOS_CRT_ONEXIT_STATUS_UNALIGNED_STORAGE,
                       "unaligned-storage-rejected");

    table.first = (GXOS_CRT_ONEXIT_PVFV *)(uintptr_t)
        gxos_crt_onexit_encode_pointer((uintptr_t)&storage[0]);
    table.last = table.first;
    table.end = (GXOS_CRT_ONEXIT_PVFV *)(uintptr_t)
        gxos_crt_onexit_encode_pointer((uintptr_t)&storage[4] + sizeof(uintptr_t));
    status = gxos_crt_onexit_register_checked(
        &table, (GXOS_CRT_ONEXIT_T)test_callback, &report);
    failures += expect(status == GXOS_CRT_ONEXIT_STATUS_STORAGE_RANGE_INVALID,
                       "mis-sized-storage-rejected");

    table.end = (GXOS_CRT_ONEXIT_PVFV *)(uintptr_t)
        gxos_crt_onexit_encode_pointer((uintptr_t)&storage[4]);
    context.regions[2].executable = 0;
    failures += expect(gxos_crt_onexit_configure(&context) == 0,
                       "nonexecutable-callback-context-configured");
    status = gxos_crt_onexit_register_checked(
        &table, (GXOS_CRT_ONEXIT_T)test_callback, &report);
    failures += expect(status == GXOS_CRT_ONEXIT_STATUS_NONEXECUTABLE_CALLBACK,
                       "nonexecutable-callback-rejected");
    printf("CRT_REGISTER_TEST_NEGATIVE_STATES=%s\n",
           failures == 0 ? "PASS" : "FAIL");

    if (failures != 0) return 1;
    printf("CRT_ONEXIT_REGISTER_HOST_TESTS=PASSED\n");
    return 0;
}

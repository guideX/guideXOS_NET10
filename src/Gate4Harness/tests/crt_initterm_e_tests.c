#include <stdint.h>
#include <stdio.h>
#include <string.h>
#include "../crt_initterm_e.h"

static uint32_t g_calls;
static uint32_t g_order[8];
static uint32_t g_order_count;
static uint32_t g_poison_calls;
static uint32_t g_unrelated_state;

static int expect(int condition, const char *name)
{
    if (condition) return 0;
    printf("CRT_INITTERM_E_TEST_FAILURE=%s\n", name);
    return 1;
}

static int GXOS_CRT_INITTERM_E_MS_ABI callback_a(void)
{
    g_calls++;
    g_order[g_order_count++] = 1;
    return 0;
}

static int GXOS_CRT_INITTERM_E_MS_ABI callback_b(void)
{
    g_calls++;
    g_order[g_order_count++] = 2;
    return 0;
}

static int GXOS_CRT_INITTERM_E_MS_ABI callback_fail(void)
{
    g_calls++;
    g_order[g_order_count++] = 3;
    return 0x4321;
}

static int GXOS_CRT_INITTERM_E_MS_ABI callback_mutates_unrelated_state(void)
{
    g_calls++;
    g_unrelated_state = 0xC0DEC0DEU;
    return 0;
}

static int GXOS_CRT_INITTERM_E_MS_ABI callback_poison(void)
{
    g_poison_calls++;
    return 0x7BAD;
}

static uintptr_t function_address(GXOS_C_INITIALIZER callback)
{
    return (uintptr_t)callback;
}

static int configure_for(GXOS_C_INITIALIZER *table, uint32_t slots,
                         GXOS_C_INITIALIZER first_callback,
                         GXOS_C_INITIALIZER second_callback)
{
    GXOS_CRT_INITTERM_E_CONTEXT context = {0};
    uintptr_t first = function_address(first_callback);
    uintptr_t second = function_address(second_callback);
    uintptr_t base = first < second ? first : second;
    uintptr_t end = first > second ? first : second;

    if (first_callback == 0) first = function_address(callback_a);
    if (second_callback == 0) second = function_address(callback_a);
    base = first < second ? first : second;
    end = first > second ? first : second;
    if (end == UINTPTR_MAX) return 1;

    context.image_base = 0;
    context.image_end = UINTPTR_MAX;
    context.table_base = (uintptr_t)table;
    context.table_end = (uintptr_t)table + (uintptr_t)(slots == 0 ? 1 : slots) * sizeof(uintptr_t);
    context.relocations_applied = 1;
    context.executable_region_count = 1;
    context.executable_regions[0].base = base;
    context.executable_regions[0].end = end + 1;
    return gxos_crt_initterm_e_configure(&context);
}

static int run_table(GXOS_C_INITIALIZER *table, uint32_t slots,
                     GXOS_CRT_INITTERM_E_REPORT *report)
{
    return gxos_crt_initterm_e(table, table + slots, report, 0);
}

int main(void)
{
    GXOS_C_INITIALIZER table[8];
    GXOS_CRT_INITTERM_E_REPORT report;
    struct {
        uint8_t leading[16];
        GXOS_C_INITIALIZER entries[1];
        uint8_t trailing[16];
    } guarded;
    GXOS_CRT_INITTERM_E_CONTEXT overflow_context = {0};
    uint8_t misaligned_storage[32] __attribute__((aligned(8)));
    int failures = 0;
    uint32_t i;

    memset(table, 0, sizeof(table));
    failures += expect(configure_for(table, 1, callback_a, callback_a) == 0, "configure-empty");
    g_calls = 0;
    failures += expect(gxos_crt_initterm_e(table, table, &report, 0) == 0, "empty-return");
    failures += expect(g_calls == 0 && report.entry_count == 0, "empty-no-callback");
    printf("CRT_INITTERM_E_TEST_EMPTY_RANGE=PASS\n");

    memset(table, 0, sizeof(table));
    failures += expect(configure_for(table, 3, callback_a, callback_a) == 0, "configure-nulls");
    g_calls = 0;
    failures += expect(run_table(table, 3, &report) == 0, "null-return");
    failures += expect(g_calls == 0 && report.null_entry_count == 3 && report.nonnull_entry_count == 0,
                       "null-only-skips");
    printf("CRT_INITTERM_E_TEST_NULL_ENTRIES=PASS\n");

    memset(table, 0, sizeof(table));
    table[0] = callback_a;
    failures += expect(configure_for(table, 1, callback_a, callback_a) == 0, "configure-one");
    g_calls = 0;
    failures += expect(run_table(table, 1, &report) == 0 && g_calls == 1 && report.invoked_count == 1,
                       "one-success");
    printf("CRT_INITTERM_E_TEST_ONE_SUCCESS=PASS\n");

    memset(table, 0, sizeof(table));
    table[0] = callback_a;
    table[1] = 0;
    table[2] = callback_b;
    failures += expect(configure_for(table, 3, callback_a, callback_b) == 0, "configure-order");
    g_calls = 0;
    g_order_count = 0;
    failures += expect(run_table(table, 3, &report) == 0 && g_calls == 2 &&
                       g_order_count == 2 && g_order[0] == 1 && g_order[1] == 2,
                       "forward-order-and-null");
    printf("CRT_INITTERM_E_TEST_FORWARD_ORDER=PASS\n");

    memset(table, 0, sizeof(table));
    table[0] = callback_fail;
    table[1] = callback_b;
    failures += expect(configure_for(table, 2, callback_fail, callback_b) == 0, "configure-failure");
    g_calls = 0;
    g_order_count = 0;
    failures += expect(run_table(table, 2, &report) == 0x4321 && g_calls == 1 &&
                       report.failure_count == 1 && g_order_count == 1 && g_order[0] == 3,
                       "failure-propagation-and-stop");
    printf("CRT_INITTERM_E_TEST_FAILURE_PROPAGATION=PASS\n");

    memset(table, 0, sizeof(table));
    table[0] = callback_a;
    failures += expect(configure_for(table, 1, callback_a, callback_a) == 0, "configure-abi");
    g_calls = 0;
    failures += expect(run_table(table, 1, &report) == 0 && g_calls == 1, "ms-abi-callback");
    printf("CRT_INITTERM_E_TEST_CALLBACK_ABI=PASS\n");

    memset(table, 0, sizeof(table));
    failures += expect(configure_for(table, 2, callback_a, callback_a) == 0, "configure-equal");
    failures += expect(gxos_crt_initterm_e(table + 1, table + 1, &report, 0) == 0 &&
                       report.entry_count == 0, "equal-pointers");
    printf("CRT_INITTERM_E_TEST_EQUAL_POINTERS=PASS\n");

    failures += expect(gxos_crt_initterm_e(table + 1, table, &report, 0) ==
                       GXOS_CRT_INITTERM_E_VALIDATION_FAILURE && g_calls == 1,
                       "reversed-range");
    printf("CRT_INITTERM_E_TEST_REVERSED_RANGE=PASS\n");

    memset(misaligned_storage, 0, sizeof(misaligned_storage));
    failures += expect(gxos_crt_initterm_e(
                           (GXOS_C_INITIALIZER *)(uintptr_t)(misaligned_storage + 1),
                           (GXOS_C_INITIALIZER *)(uintptr_t)(misaligned_storage + 9),
                           &report, 0) == GXOS_CRT_INITTERM_E_VALIDATION_FAILURE,
                       "misaligned-range");
    printf("CRT_INITTERM_E_TEST_MISALIGNED_RANGE=PASS\n");

    overflow_context.image_base = UINTPTR_MAX - 3;
    overflow_context.image_end = 4;
    overflow_context.table_base = 0;
    overflow_context.table_end = 8;
    overflow_context.relocations_applied = 1;
    overflow_context.executable_region_count = 1;
    overflow_context.executable_regions[0].base = function_address(callback_a);
    overflow_context.executable_regions[0].end = function_address(callback_a) + 1;
    failures += expect(gxos_crt_initterm_e_configure(&overflow_context) ==
                       GXOS_CRT_INITTERM_E_VALIDATION_FAILURE, "pointer-overflow");
    printf("CRT_INITTERM_E_TEST_POINTER_OVERFLOW=PASS\n");

    memset(table, 0, sizeof(table));
    table[0] = (GXOS_C_INITIALIZER)(uintptr_t)0x0000800000000000ULL;
    failures += expect(configure_for(table, 1, callback_a, callback_a) == 0, "configure-noncanonical");
    g_calls = 0;
    failures += expect(run_table(table, 1, &report) == GXOS_CRT_INITTERM_E_VALIDATION_FAILURE &&
                       g_calls == 0 && report.failure_count == 0, "noncanonical-target");
    printf("CRT_INITTERM_E_TEST_NONCANONICAL_TARGET=PASS\n");

    memset(table, 0, sizeof(table));
    table[0] = callback_b;
    failures += expect(configure_for(table, 1, callback_a, callback_a) == 0, "configure-out-of-image");
    g_calls = 0;
    failures += expect(run_table(table, 1, &report) == GXOS_CRT_INITTERM_E_VALIDATION_FAILURE &&
                       g_calls == 0, "out-of-image-target");
    printf("CRT_INITTERM_E_TEST_OUT_OF_IMAGE=PASS\n");

    memset(&guarded, 0xC7, sizeof(guarded));
    guarded.entries[0] = callback_a;
    failures += expect(configure_for(guarded.entries, 1, callback_a, callback_a) == 0, "configure-guards");
    g_calls = 0;
    failures += expect(run_table(guarded.entries, 1, &report) == 0 && g_calls == 1,
                       "guard-run");
    for (i = 0; i != sizeof(guarded.leading); i++) failures += expect(guarded.leading[i] == 0xC7, "leading-guard");
    for (i = 0; i != sizeof(guarded.trailing); i++) failures += expect(guarded.trailing[i] == 0xC7, "trailing-guard");
    printf("CRT_INITTERM_E_TEST_GUARDS=PASS\n");

    memset(table, 0, sizeof(table));
    table[0] = callback_mutates_unrelated_state;
    failures += expect(configure_for(table, 1, callback_mutates_unrelated_state,
                                     callback_mutates_unrelated_state) == 0,
                       "configure-unrelated-state");
    g_calls = 0;
    g_unrelated_state = 0;
    failures += expect(run_table(table, 1, &report) == 0 && g_calls == 1 &&
                       g_unrelated_state == 0xC0DEC0DEU && report.entry_count == 1,
                       "unrelated-state-mutation");
    printf("CRT_INITTERM_E_TEST_UNRELATED_MUTATION=PASS\n");

    memset(table, 0, sizeof(table));
    table[0] = callback_a;
    table[1] = callback_a;
    failures += expect(configure_for(table, 2, callback_a, callback_a) == 0, "configure-duplicates");
    g_calls = 0;
    failures += expect(run_table(table, 2, &report) == 0 && g_calls == 2 && report.invoked_count == 2,
                       "duplicate-entries");
    printf("CRT_INITTERM_E_TEST_DUPLICATES=PASS\n");

    memset(table, 0, sizeof(table));
    table[0] = callback_a;
    table[1] = callback_poison;
    failures += expect(configure_for(table, 2, callback_a, callback_poison) == 0, "configure-exclusive-end");
    g_calls = 0;
    g_poison_calls = 0;
    failures += expect(gxos_crt_initterm_e(table, table + 1, &report, 0) == 0 &&
                       g_calls == 1 && g_poison_calls == 0,
                       "exclusive-end");
    printf("CRT_INITTERM_E_TEST_EXCLUSIVE_END=PASS\n");

    if (failures != 0) return 1;
    printf("CRT_INITTERM_E_TEST_NO_ALLOCATION=PASS\n");
    printf("CRT_INITTERM_E_HOST_TESTS=PASSED\n");
    return 0;
}

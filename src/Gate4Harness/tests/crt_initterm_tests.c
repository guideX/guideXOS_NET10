#include <stdint.h>
#include <stdio.h>
#include "../crt_initterm.h"

static uint32_t g_calls;
static uint32_t g_order[8];
static uint32_t g_order_count;
static uint32_t g_state;
static uint32_t g_poison_calls;

static int expect(int condition, const char *name)
{
    if (!condition) {
        printf("CRT_INITTERM_TEST_FAILURE=%s\n", name);
        return 1;
    }
    return 0;
}

static void GXOS_CRT_INITTERM_MS_ABI callback_a(void)
{
    g_calls++;
    g_order[g_order_count++] = 1;
}

static void GXOS_CRT_INITTERM_MS_ABI callback_b(void)
{
    g_calls++;
    g_order[g_order_count++] = 2;
}

static void GXOS_CRT_INITTERM_MS_ABI callback_c(void)
{
    g_calls++;
    g_order[g_order_count++] = 3;
}

static void GXOS_CRT_INITTERM_MS_ABI callback_mutates_state(void)
{
    g_calls++;
    g_state = 0x13579BDFU;
}

static void GXOS_CRT_INITTERM_MS_ABI callback_poison(void)
{
    g_poison_calls++;
}

static void GXOS_CRT_INITTERM_MS_ABI callback_rax_sentinel(void)
{
    g_calls++;
    __asm__ volatile("mov $0x1122334455667788, %%rax" ::: "rax");
}

static void GXOS_CRT_INITTERM_MS_ABI callback_injected_fault(void)
{
    gxos_crt_initterm_inject_callback_fault();
}

static void reset_state(void)
{
    g_calls = 0;
    g_order_count = 0;
    g_state = 0;
    g_poison_calls = 0;
}

static void add_code_region(GXOS_CRT_INITTERM_CONTEXT *context,
                            GXOS_VOID_INITIALIZER callback)
{
    uintptr_t base = (uintptr_t)callback & ~(uintptr_t)0x0F;
    uint32_t index = context->memory_region_count++;
    context->memory_regions[index].base = base;
    context->memory_regions[index].end = base + 0x100;
    context->memory_regions[index].readable = 1;
    context->memory_regions[index].executable = 1;
    context->memory_regions[index].writable = 0;
}

static int configure_context(GXOS_VOID_INITIALIZER *table,
                             uint32_t slots,
                             int include_code,
                             int executable_table)
{
    GXOS_CRT_INITTERM_CONTEXT context = {0};
    uintptr_t table_base = (uintptr_t)table;
    uint32_t table_slots = slots == 0 ? 1 : slots;

    context.image_base = 0x1000;
    context.image_end = 0x00007FFFFFFFFFFFULL;
    context.relocations_applied = 1;
    context.memory_region_count = 1;
    context.memory_regions[0].base = table_base;
    context.memory_regions[0].end = table_base + (uintptr_t)table_slots * sizeof(uintptr_t);
    context.memory_regions[0].readable = 1;
    context.memory_regions[0].executable = executable_table ? 1U : 0U;
    context.memory_regions[0].writable = 1;
    if (include_code != 0) {
        add_code_region(&context, callback_a);
        add_code_region(&context, callback_b);
        add_code_region(&context, callback_c);
        add_code_region(&context, callback_mutates_state);
        add_code_region(&context, callback_poison);
        add_code_region(&context, callback_rax_sentinel);
        add_code_region(&context, callback_injected_fault);
    }
    return gxos_crt_initterm_configure(&context);
}

static int run_table(GXOS_VOID_INITIALIZER *table,
                     uint32_t slots,
                     int include_code,
                     GXOS_CRT_INITTERM_REPORT *report)
{
    if (configure_context(table, slots, include_code, 0) != 0) return -2;
    return gxos_crt_initterm(table, table + slots, report, 0);
}

int main(void)
{
    GXOS_VOID_INITIALIZER table[8] = {0};
    GXOS_CRT_INITTERM_REPORT report;
    int failures = 0;
    int result;

    reset_state();
    table[0] = callback_a;
    failures += expect(run_table(table, 0, 1, &report) == 0 && g_calls == 0,
                       "empty-range");
    printf("CRT_INITTERM_TEST_EMPTY_RANGE=%s\n", failures == 0 ? "PASS" : "FAIL");

    reset_state();
    table[0] = 0; table[1] = 0; table[2] = 0;
    failures += expect(run_table(table, 3, 1, &report) == 0 &&
                       report.entry_count == 3 && report.null_entry_count == 3 &&
                       report.invoked_count == 0 && report.returned_count == 0,
                       "null-only");
    printf("CRT_INITTERM_TEST_NULL_ONLY=%s\n", failures == 0 ? "PASS" : "FAIL");

    reset_state();
    table[0] = callback_a;
    failures += expect(run_table(table, 1, 1, &report) == 0 &&
                       g_calls == 1 && report.invoked_count == 1 &&
                       report.returned_count == 1 && report.completed == 1,
                       "one-callback");
    printf("CRT_INITTERM_TEST_ONE_CALLBACK=%s\n", failures == 0 ? "PASS" : "FAIL");

    reset_state();
    table[0] = callback_a; table[1] = callback_b; table[2] = callback_c;
    failures += expect(run_table(table, 3, 1, &report) == 0 &&
                       g_order_count == 3 && g_order[0] == 1 &&
                       g_order[1] == 2 && g_order[2] == 3,
                       "forward-order");
    printf("CRT_INITTERM_TEST_FORWARD_ORDER=%s\n", failures == 0 ? "PASS" : "FAIL");

    reset_state();
    table[0] = callback_a; table[1] = 0; table[2] = callback_b;
    failures += expect(run_table(table, 3, 1, &report) == 0 &&
                       g_order_count == 2 && g_order[0] == 1 && g_order[1] == 2 &&
                       report.null_entry_count == 1,
                       "null-between");
    printf("CRT_INITTERM_TEST_NULL_BETWEEN=%s\n", failures == 0 ? "PASS" : "FAIL");

    reset_state();
    table[0] = callback_a; table[1] = callback_a;
    failures += expect(run_table(table, 2, 1, &report) == 0 &&
                       g_calls == 2 && report.invoked_count == 2 &&
                       report.returned_count == 2,
                       "duplicates");
    printf("CRT_INITTERM_TEST_DUPLICATES=%s\n", failures == 0 ? "PASS" : "FAIL");

    reset_state();
    table[0] = callback_a; table[1] = callback_poison;
    if (configure_context(table, 2, 1, 0) != 0) result = -2;
    else result = gxos_crt_initterm(table, table + 1, &report, 0);
    failures += expect(result == 0 && g_calls == 1 && g_poison_calls == 0,
                       "exclusive-end");
    printf("CRT_INITTERM_TEST_EXCLUSIVE_END=%s\n", failures == 0 ? "PASS" : "FAIL");

    reset_state();
    table[0] = callback_a;
    if (configure_context(table, 1, 1, 0) != 0) result = -2;
    else result = gxos_crt_initterm(table + 1, table, &report, 0);
    failures += expect(result == GXOS_CRT_INITTERM_VALIDATION_FAILURE && g_calls == 0,
                       "reversed-range");
    printf("CRT_INITTERM_TEST_REVERSED_RANGE=%s\n", failures == 0 ? "PASS" : "FAIL");

    reset_state();
    failures += expect(configure_context(table, 1, 1, 0) == 0 &&
                       gxos_crt_initterm((GXOS_VOID_INITIALIZER *)((uintptr_t)table + 1),
                                         (GXOS_VOID_INITIALIZER *)((uintptr_t)table + 9),
                                         &report, 0) == GXOS_CRT_INITTERM_VALIDATION_FAILURE,
                       "misaligned-range");
    printf("CRT_INITTERM_TEST_MISALIGNED_RANGE=%s\n", failures == 0 ? "PASS" : "FAIL");

    {
        GXOS_CRT_INITTERM_CONTEXT overflow = {0};
        overflow.image_base = 0x00007FFFFFFFFFF0ULL;
        overflow.image_end = 0x00007FFFFFFFFFFFULL;
        overflow.relocations_applied = 1;
        overflow.memory_region_count = 1;
        overflow.memory_regions[0].base = 0x00007FFFFFFFFFF0ULL;
        overflow.memory_regions[0].end = 0x0000800000000000ULL;
        overflow.memory_regions[0].readable = 1;
        failures += expect(gxos_crt_initterm_configure(&overflow) ==
                           GXOS_CRT_INITTERM_VALIDATION_FAILURE,
                           "pointer-overflow");
    }
    printf("CRT_INITTERM_TEST_POINTER_OVERFLOW=%s\n", failures == 0 ? "PASS" : "FAIL");

    reset_state();
    table[0] = (GXOS_VOID_INITIALIZER)(uintptr_t)0x0000800000000000ULL;
    failures += expect(run_table(table, 1, 1, &report) ==
                       GXOS_CRT_INITTERM_VALIDATION_FAILURE && g_calls == 0,
                       "noncanonical-target");
    printf("CRT_INITTERM_TEST_NONCANONICAL_TARGET=%s\n", failures == 0 ? "PASS" : "FAIL");

    reset_state();
    table[0] = callback_a;
    failures += expect(run_table(table, 1, 0, &report) ==
                       GXOS_CRT_INITTERM_VALIDATION_FAILURE && g_calls == 0,
                       "out-of-image-target");
    printf("CRT_INITTERM_TEST_OUT_OF_IMAGE_TARGET=%s\n", failures == 0 ? "PASS" : "FAIL");

    reset_state();
    table[0] = (GXOS_VOID_INITIALIZER)(uintptr_t)table;
    failures += expect(run_table(table, 1, 1, &report) ==
                       GXOS_CRT_INITTERM_VALIDATION_FAILURE && g_calls == 0,
                       "non-executable-target");
    printf("CRT_INITTERM_TEST_NONEXECUTABLE_TARGET=%s\n", failures == 0 ? "PASS" : "FAIL");

    {
        struct {
            uint64_t before;
            GXOS_VOID_INITIALIZER slots[3];
            uint64_t after;
        } guarded = {0xA5A5A5A5A5A5A5A5ULL, {callback_a, 0, callback_b},
                     0x5A5A5A5A5A5A5A5AULL};
        reset_state();
        failures += expect(run_table(guarded.slots, 3, 1, &report) == 0 &&
                           guarded.before == 0xA5A5A5A5A5A5A5A5ULL &&
                           guarded.after == 0x5A5A5A5A5A5A5A5AULL,
                           "adjacent-guards");
    }
    printf("CRT_INITTERM_TEST_ADJACENT_GUARDS=%s\n", failures == 0 ? "PASS" : "FAIL");

    reset_state();
    table[0] = callback_mutates_state;
    failures += expect(run_table(table, 1, 1, &report) == 0 &&
                       g_state == 0x13579BDFU && report.completed == 1,
                       "callback-state-mutation");
    printf("CRT_INITTERM_TEST_CALLBACK_STATE_MUTATION=%s\n", failures == 0 ? "PASS" : "FAIL");

    reset_state();
    table[0] = callback_a;
    failures += expect(run_table(table, 1, 1, &report) == 0 && g_calls == 1,
                       "callback-abi");
    printf("CRT_INITTERM_TEST_CALLBACK_ABI=%s\n", failures == 0 ? "PASS" : "FAIL");

    reset_state();
    table[0] = callback_rax_sentinel;
    failures += expect(run_table(table, 1, 1, &report) == 0 &&
                       report.completed == 1 && report.status == 0,
                       "void-return-not-read");
    printf("CRT_INITTERM_TEST_VOID_RETURN_NOT_READ=%s\n", failures == 0 ? "PASS" : "FAIL");

    reset_state();
    table[0] = callback_injected_fault;
    result = run_table(table, 1, 1, &report);
    failures += expect(result == GXOS_CRT_INITTERM_VALIDATION_FAILURE &&
                       report.callback_fault_observed == 1 &&
                       report.completed == 0 && report.returned_count == 0,
                       "injected-callback-fault");
    printf("CRT_INITTERM_TEST_INJECTED_CALLBACK_FAULT=%s\n", failures == 0 ? "PASS" : "FAIL");

    reset_state();
    table[0] = callback_a;
    failures += expect(run_table(table, 0, 1, &report) == 0 &&
                       report.entry_count == 0 && g_calls == 0,
                       "equal-pointers");
    printf("CRT_INITTERM_TEST_EQUAL_POINTERS=%s\n", failures == 0 ? "PASS" : "FAIL");

    printf("CRT_INITTERM_HOST_TESTS=%s\n", failures == 0 ? "PASSED" : "FAILED");
    return failures == 0 ? 0 : 1;
}

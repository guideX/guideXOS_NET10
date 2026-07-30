#include "crt_initterm_e.h"

static GXOS_CRT_INITTERM_E_CONTEXT g_context;
static uint32_t g_context_valid;

static int gxos_crt_initterm_e_is_canonical(uintptr_t value)
{
#if UINTPTR_MAX > 0xFFFFFFFFULL
    return value <= (uintptr_t)0x00007FFFFFFFFFFFULL ||
           value >= (uintptr_t)0xFFFF800000000000ULL;
#else
    (void)value;
    return 1;
#endif
}

static int gxos_crt_initterm_e_is_range(uintptr_t base, uintptr_t end)
{
    return gxos_crt_initterm_e_is_canonical(base) &&
           gxos_crt_initterm_e_is_canonical(end) &&
           base < end;
}

static int gxos_crt_initterm_e_contains(uintptr_t base, uintptr_t end, uintptr_t value)
{
    return value >= base && value < end;
}

static void gxos_crt_initterm_e_report_clear(GXOS_CRT_INITTERM_E_REPORT *report)
{
    if (report == 0) return;
    report->entry_count = 0;
    report->null_entry_count = 0;
    report->nonnull_entry_count = 0;
    report->invoked_count = 0;
    report->failure_count = 0;
    report->validation_failure = 0;
    report->trace_truncated = 0;
    report->result = 0;
}

static void gxos_crt_initterm_e_trace(
    GXOS_CRT_INITTERM_E_TRACE trace,
    GXOS_CRT_INITTERM_E_REPORT *report,
    uint32_t event,
    uint64_t index,
    uintptr_t target,
    int32_t result,
    uint64_t *trace_count)
{
    if (trace == 0) return;
    if (*trace_count >= GXOS_CRT_INITTERM_E_MAX_TRACE_ENTRIES) {
        if (report != 0) report->trace_truncated = 1;
        return;
    }
    (*trace_count)++;
    trace(event, index, target, result);
}

static int gxos_crt_initterm_e_fail(
    GXOS_CRT_INITTERM_E_REPORT *report,
    GXOS_CRT_INITTERM_E_TRACE trace,
    uint32_t reason,
    uint64_t index,
    uintptr_t target,
    uint64_t *trace_count)
{
    if (report != 0) {
        report->validation_failure = reason;
        report->result = GXOS_CRT_INITTERM_E_VALIDATION_FAILURE;
    }
    gxos_crt_initterm_e_trace(trace, report,
                              GXOS_CRT_INITTERM_E_TRACE_VALIDATION_FAILURE,
                              index, target, (int32_t)reason, trace_count);
    return GXOS_CRT_INITTERM_E_VALIDATION_FAILURE;
}

int GXOS_CRT_INITTERM_E_MS_ABI gxos_crt_initterm_e_configure(
    const GXOS_CRT_INITTERM_E_CONTEXT *context)
{
    uint32_t index;

    g_context_valid = 0;
    if (context == 0 || context->relocations_applied == 0 ||
        !gxos_crt_initterm_e_is_range(context->image_base, context->image_end) ||
        !gxos_crt_initterm_e_is_range(context->table_base, context->table_end) ||
        context->table_base < context->image_base ||
        context->table_end > context->image_end ||
        context->executable_region_count == 0 ||
        context->executable_region_count > GXOS_CRT_INITTERM_E_MAX_EXECUTABLE_REGIONS) {
        return GXOS_CRT_INITTERM_E_VALIDATION_FAILURE;
    }

    for (index = 0; index != context->executable_region_count; index++) {
        GXOS_CRT_INITTERM_E_EXECUTABLE_REGION region = context->executable_regions[index];
        if (!gxos_crt_initterm_e_is_range(region.base, region.end) ||
            region.base < context->image_base || region.end > context->image_end) {
            return GXOS_CRT_INITTERM_E_VALIDATION_FAILURE;
        }
        g_context.executable_regions[index] = region;
    }

    g_context.image_base = context->image_base;
    g_context.image_end = context->image_end;
    g_context.table_base = context->table_base;
    g_context.table_end = context->table_end;
    g_context.relocations_applied = context->relocations_applied;
    g_context.executable_region_count = context->executable_region_count;
    g_context_valid = 1;
    return 0;
}

static int gxos_crt_initterm_e_is_executable(uintptr_t target)
{
    uint32_t index;

    if (target == 0 || !gxos_crt_initterm_e_is_canonical(target)) return 0;
    for (index = 0; index != g_context.executable_region_count; index++) {
        GXOS_CRT_INITTERM_E_EXECUTABLE_REGION region = g_context.executable_regions[index];
        if (gxos_crt_initterm_e_contains(region.base, region.end, target)) return 1;
    }
    return 0;
}

int GXOS_CRT_INITTERM_E_MS_ABI gxos_crt_initterm_e(
    GXOS_C_INITIALIZER *first,
    GXOS_C_INITIALIZER *last,
    GXOS_CRT_INITTERM_E_REPORT *report,
    GXOS_CRT_INITTERM_E_TRACE trace)
{
    uintptr_t first_value = (uintptr_t)first;
    uintptr_t last_value = (uintptr_t)last;
    uintptr_t byte_count;
    uint64_t entry_count;
    uint64_t index;
    uint64_t trace_count = 0;

    gxos_crt_initterm_e_report_clear(report);
    if (!g_context_valid || !gxos_crt_initterm_e_is_canonical(first_value) ||
        !gxos_crt_initterm_e_is_canonical(last_value) ||
        (first_value & (sizeof(uintptr_t) - 1U)) != 0 ||
        (last_value & (sizeof(uintptr_t) - 1U)) != 0 ||
        first_value > last_value ||
        first_value < g_context.table_base || last_value > g_context.table_end) {
        return gxos_crt_initterm_e_fail(report, trace,
                                        GXOS_CRT_INITTERM_E_VALIDATION_FAILURE,
                                        0, 0, &trace_count);
    }

    byte_count = last_value - first_value;
    if ((byte_count & (sizeof(uintptr_t) - 1U)) != 0) {
        return gxos_crt_initterm_e_fail(report, trace, 2, 0, 0, &trace_count);
    }
    entry_count = byte_count / sizeof(uintptr_t);
    if (entry_count > GXOS_CRT_INITTERM_E_MAX_ENTRIES) {
        return gxos_crt_initterm_e_fail(report, trace, 3, 0, 0, &trace_count);
    }
    if (report != 0) report->entry_count = entry_count;

    for (index = 0; index != entry_count; index++) {
        uintptr_t offset;
        uintptr_t slot_address;
        GXOS_C_INITIALIZER initializer;
        uintptr_t target;
        int result;

        if (index > UINTPTR_MAX / sizeof(uintptr_t)) {
            return gxos_crt_initterm_e_fail(report, trace, 4, index, 0, &trace_count);
        }
        offset = (uintptr_t)index * sizeof(uintptr_t);
        if (offset > UINTPTR_MAX - first_value) {
            return gxos_crt_initterm_e_fail(report, trace, 4, index, 0, &trace_count);
        }
        slot_address = first_value + offset;
        initializer = *(GXOS_C_INITIALIZER *)(uintptr_t)slot_address;
        target = (uintptr_t)initializer;
        gxos_crt_initterm_e_trace(trace, report,
                                  GXOS_CRT_INITTERM_E_TRACE_ENTRY,
                                  index, target, 0, &trace_count);
        if (initializer == 0) {
            if (report != 0) report->null_entry_count++;
            continue;
        }

        if (report != 0) report->nonnull_entry_count++;
        if (!gxos_crt_initterm_e_is_executable(target)) {
            return gxos_crt_initterm_e_fail(report, trace, 5, index, target, &trace_count);
        }
        gxos_crt_initterm_e_trace(trace, report,
                                  GXOS_CRT_INITTERM_E_TRACE_CALLBACK_BEGIN,
                                  index, target, 0, &trace_count);
        if (report != 0) report->invoked_count++;
        result = initializer();
        if (report != 0) report->result = result;
        gxos_crt_initterm_e_trace(trace, report,
                                  GXOS_CRT_INITTERM_E_TRACE_CALLBACK_RESULT,
                                  index, target, (int32_t)result, &trace_count);
        if (result != 0) {
            if (report != 0) report->failure_count++;
            return result;
        }
    }

    if (report != 0) report->result = 0;
    return 0;
}

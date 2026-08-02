#include "crt_onexit.h"

static uintptr_t g_encoded_null;
static const uintptr_t *g_encoded_null_address;
static GXOS_CRT_ONEXIT_CONTEXT g_context;
static uint32_t g_context_valid;

static int gxos_crt_onexit_is_canonical(uintptr_t address)
{
#if UINTPTR_MAX > 0xFFFFFFFFULL
    return address <= (uintptr_t)0x00007FFFFFFFFFFFULL ||
           address >= (uintptr_t)0xFFFF800000000000ULL;
#else
    (void)address;
    return 1;
#endif
}

static int gxos_crt_onexit_is_range(uintptr_t base, uintptr_t end)
{
    return base != 0 && end > base &&
           gxos_crt_onexit_is_canonical(base) &&
           gxos_crt_onexit_is_canonical(end);
}

static int gxos_crt_onexit_range_contains(
    const GXOS_CRT_ONEXIT_MEMORY_REGION *region,
    uintptr_t base,
    uintptr_t end)
{
    return region != 0 && region->base != 0 && region->end > region->base &&
           base >= region->base && end >= base && end <= region->end;
}

static const GXOS_CRT_ONEXIT_MEMORY_REGION *gxos_crt_onexit_find_range(
    uintptr_t base,
    uintptr_t end,
    uint32_t require_readable,
    uint32_t require_writable)
{
    uint32_t index;

    for (index = 0; index != g_context.region_count; ++index) {
        const GXOS_CRT_ONEXIT_MEMORY_REGION *region = &g_context.regions[index];
        if ((!require_readable || region->readable) &&
            (!require_writable || region->writable) &&
            gxos_crt_onexit_range_contains(region, base, end)) {
            return region;
        }
    }
    return 0;
}

static const GXOS_CRT_ONEXIT_MEMORY_REGION *gxos_crt_onexit_find_address(
    uintptr_t address,
    uint32_t require_executable)
{
    uint32_t index;

    if (address == 0) return 0;
    for (index = 0; index != g_context.region_count; ++index) {
        const GXOS_CRT_ONEXIT_MEMORY_REGION *region = &g_context.regions[index];
        if (address >= region->base && address < region->end &&
            region->readable &&
            (!require_executable || region->executable)) {
            return region;
        }
    }
    return 0;
}

static uintptr_t gxos_crt_onexit_rotate_right(uintptr_t value, uint32_t shift)
{
    if (shift == 0) return value;
    return (value >> shift) | (value << (64U - shift));
}

void gxos_crt_onexit_set_encoded_null(uintptr_t encoded_null)
{
    g_encoded_null_address = 0;
    g_encoded_null = encoded_null;
}

void gxos_crt_onexit_set_encoded_null_address(const uintptr_t *encoded_null_address)
{
    g_encoded_null_address = encoded_null_address;
}

uintptr_t gxos_crt_onexit_get_encoded_null(void)
{
    return g_encoded_null_address != 0 ? *g_encoded_null_address : g_encoded_null;
}

uintptr_t gxos_crt_onexit_encode_pointer(uintptr_t pointer)
{
    uintptr_t cookie = gxos_crt_onexit_get_encoded_null();
    uint32_t shift;

    if (cookie == 0) return 0;
    shift = (uint32_t)(cookie % 64U);
    return gxos_crt_onexit_rotate_right(pointer, (64U - shift) & 63U) ^ cookie;
}

uintptr_t gxos_crt_onexit_decode_pointer(uintptr_t pointer)
{
    uintptr_t cookie = gxos_crt_onexit_get_encoded_null();
    uint32_t shift;

    if (cookie == 0) return 0;
    shift = (uint32_t)(cookie % 64U);
    return gxos_crt_onexit_rotate_right(pointer ^ cookie, shift);
}

static void gxos_crt_onexit_report_clear(GXOS_CRT_ONEXIT_REPORT *report)
{
    uint32_t index;

    if (report == 0) return;
    report->status = GXOS_CRT_ONEXIT_STATUS_INVALID_CONTEXT;
    report->table = 0;
    report->callback = 0;
    report->table_first_raw = 0;
    report->table_last_raw = 0;
    report->table_end_raw = 0;
    report->first = 0;
    report->last = 0;
    report->end = 0;
    report->table_region_base = 0;
    report->table_region_end = 0;
    report->table_region_readable = 0;
    report->table_region_writable = 0;
    report->storage_region_base = 0;
    report->storage_region_end = 0;
    report->storage_region_readable = 0;
    report->storage_region_writable = 0;
    report->callback_region_base = 0;
    report->callback_region_end = 0;
    report->callback_region_executable = 0;
    report->used_count = 0;
    report->capacity = 0;
    report->remaining_capacity = 0;
    report->entry_index = UINT32_MAX;
    report->encoded_callback = 0;
    report->stored_value = 0;
    report->pointer_encoded = 0;
    report->initialized_table_match = 0;
    report->initialized_table_index = UINT32_MAX;
    report->growth_required = 0;
    report->allocation_attempted = 0;
    report->callback_executed = 0;
    report->census_count = 0;
    for (index = 0; index != GXOS_CRT_ONEXIT_MAX_CENSUS_ENTRIES; ++index) {
        report->census_values[index] = 0;
    }
}

static GXOS_CRT_ONEXIT_STATUS gxos_crt_onexit_fail(
    GXOS_CRT_ONEXIT_REPORT *report,
    GXOS_CRT_ONEXIT_STATUS status)
{
    if (report != 0) report->status = status;
    return status;
}

const char *gxos_crt_onexit_status_name(GXOS_CRT_ONEXIT_STATUS status)
{
    switch (status) {
        case GXOS_CRT_ONEXIT_STATUS_OK: return "OK";
        case GXOS_CRT_ONEXIT_STATUS_INVALID_CONTEXT: return "INVALID_CONTEXT";
        case GXOS_CRT_ONEXIT_STATUS_NULL_TABLE: return "NULL_TABLE";
        case GXOS_CRT_ONEXIT_STATUS_NONCANONICAL_TABLE: return "NONCANONICAL_TABLE";
        case GXOS_CRT_ONEXIT_STATUS_UNREADABLE_TABLE: return "UNREADABLE_TABLE";
        case GXOS_CRT_ONEXIT_STATUS_UNWRITABLE_TABLE: return "UNWRITABLE_TABLE";
        case GXOS_CRT_ONEXIT_STATUS_TABLE_NOT_INITIALIZED: return "TABLE_NOT_INITIALIZED";
        case GXOS_CRT_ONEXIT_STATUS_NONCANONICAL_CALLBACK: return "NONCANONICAL_CALLBACK";
        case GXOS_CRT_ONEXIT_STATUS_NONEXECUTABLE_CALLBACK: return "NONEXECUTABLE_CALLBACK";
        case GXOS_CRT_ONEXIT_STATUS_NONCANONICAL_STORAGE: return "NONCANONICAL_STORAGE";
        case GXOS_CRT_ONEXIT_STATUS_UNALIGNED_STORAGE: return "UNALIGNED_STORAGE";
        case GXOS_CRT_ONEXIT_STATUS_STORAGE_RANGE_INVALID: return "STORAGE_RANGE_INVALID";
        case GXOS_CRT_ONEXIT_STATUS_INVALID_TABLE_STATE: return "INVALID_TABLE_STATE";
        case GXOS_CRT_ONEXIT_STATUS_STORAGE_FULL: return "STORAGE_FULL";
        case GXOS_CRT_ONEXIT_STATUS_GROWTH_REQUIRED: return "GROWTH_REQUIRED";
        case GXOS_CRT_ONEXIT_STATUS_ALLOCATION_FAILED: return "ALLOCATION_FAILED";
        case GXOS_CRT_ONEXIT_STATUS_CAPACITY_OVERFLOW: return "CAPACITY_OVERFLOW";
        case GXOS_CRT_ONEXIT_STATUS_POINTER_OVERFLOW: return "POINTER_OVERFLOW";
        case GXOS_CRT_ONEXIT_STATUS_ENCODING_UNAVAILABLE: return "ENCODING_UNAVAILABLE";
        default: return "UNKNOWN";
    }
}

int GXOS_CRT_ONEXIT_MS_ABI gxos_crt_onexit_configure(
    const GXOS_CRT_ONEXIT_CONTEXT *context)
{
    uint32_t index;

    g_context_valid = 0;
    if (context == 0 || context->relocations_applied == 0 ||
        !gxos_crt_onexit_is_range(context->image_base, context->image_end) ||
        context->encoded_null == 0 || context->region_count == 0 ||
        context->region_count > GXOS_CRT_ONEXIT_MAX_MEMORY_REGIONS ||
        context->initialized_table_count > GXOS_CRT_ONEXIT_MAX_INITIALIZED_TABLES) {
        return -2;
    }
    for (index = 0; index != context->region_count; ++index) {
        GXOS_CRT_ONEXIT_MEMORY_REGION region = context->regions[index];
        if (!gxos_crt_onexit_is_range(region.base, region.end) ||
            region.base < context->image_base || region.end > context->image_end ||
            region.readable == 0) {
            return -3;
        }
        g_context.regions[index] = region;
    }
    g_context.image_base = context->image_base;
    g_context.image_end = context->image_end;
    g_context.encoded_null = context->encoded_null;
    g_context.relocations_applied = context->relocations_applied;
    g_context.region_count = context->region_count;
    g_context.initialized_table_count = context->initialized_table_count;
    for (index = 0; index != context->initialized_table_count; ++index) {
        if (!gxos_crt_onexit_is_canonical(context->initialized_tables[index])) {
            return -4;
        }
        g_context.initialized_tables[index] = context->initialized_tables[index];
    }
    g_context_valid = 1;
    return 0;
}

int GXOS_CRT_ONEXIT_MS_ABI gxos_crt_onexit_set_initialized_tables(
    const uintptr_t *tables,
    uint32_t table_count)
{
    uint32_t index;

    if (!g_context_valid || table_count > GXOS_CRT_ONEXIT_MAX_INITIALIZED_TABLES ||
        (table_count != 0 && tables == 0)) {
        return -1;
    }
    for (index = 0; index != table_count; ++index) {
        if (!gxos_crt_onexit_is_canonical(tables[index])) return -1;
        g_context.initialized_tables[index] = tables[index];
    }
    g_context.initialized_table_count = table_count;
    return 0;
}

int GXOS_CRT_ONEXIT_MS_ABI gxos_crt_initialize_onexit_table(
    GXOS_CRT_ONEXIT_TABLE *table)
{
    uintptr_t encoded_null;

    if (table == 0) return -1;

    /* Microsoft CRT semantics: a non-empty state is already initialized. */
    if (table->first != table->end) return 0;

    /* A valid NativeAOT security cookie supplies a non-zero encoded null. */
    encoded_null = gxos_crt_onexit_get_encoded_null();
    if (encoded_null == 0) return -1;

    table->first = (void *)encoded_null;
    table->last = (void *)encoded_null;
    table->end = (void *)encoded_null;
    return 0;
}

GXOS_CRT_ONEXIT_STATUS GXOS_CRT_ONEXIT_MS_ABI
gxos_crt_onexit_register_checked(
    GXOS_CRT_ONEXIT_TABLE *table,
    GXOS_CRT_ONEXIT_T function,
    GXOS_CRT_ONEXIT_REPORT *report)
{
    uintptr_t table_value = (uintptr_t)table;
    uintptr_t callback_value = (uintptr_t)function;
    uintptr_t object_end;
    uintptr_t first;
    uintptr_t last;
    uintptr_t end;
    uintptr_t old_first_raw;
    uintptr_t old_last_raw;
    uintptr_t old_end_raw;
    uintptr_t encoded_callback;
    uintptr_t new_last;
    uintptr_t slot_address;
    uintptr_t old_slot_value;
    uintptr_t offset;
    uint32_t index;
    uint32_t used;
    uint32_t capacity;
    const GXOS_CRT_ONEXIT_MEMORY_REGION *region;

    gxos_crt_onexit_report_clear(report);
    if (report != 0) {
        report->table = table_value;
        report->callback = callback_value;
    }
    if (!g_context_valid || gxos_crt_onexit_get_encoded_null() == 0) {
        return gxos_crt_onexit_fail(report, GXOS_CRT_ONEXIT_STATUS_INVALID_CONTEXT);
    }
    if (table == 0) return gxos_crt_onexit_fail(report, GXOS_CRT_ONEXIT_STATUS_NULL_TABLE);
    if (!gxos_crt_onexit_is_canonical(table_value)) {
        return gxos_crt_onexit_fail(report, GXOS_CRT_ONEXIT_STATUS_NONCANONICAL_TABLE);
    }
    if (table_value > UINTPTR_MAX - sizeof(*table)) {
        return gxos_crt_onexit_fail(report, GXOS_CRT_ONEXIT_STATUS_POINTER_OVERFLOW);
    }
    object_end = table_value + sizeof(*table);
    region = gxos_crt_onexit_find_range(table_value, object_end, 1, 0);
    if (region == 0) return gxos_crt_onexit_fail(report, GXOS_CRT_ONEXIT_STATUS_UNREADABLE_TABLE);
    if (report != 0) {
        report->table_region_base = region->base;
        report->table_region_end = region->end;
        report->table_region_readable = region->readable;
        report->table_region_writable = region->writable;
    }
    if (!region->writable) {
        return gxos_crt_onexit_fail(report, GXOS_CRT_ONEXIT_STATUS_UNWRITABLE_TABLE);
    }
    for (index = 0; index != g_context.initialized_table_count; ++index) {
        if (g_context.initialized_tables[index] == table_value) {
            if (report != 0) {
                report->initialized_table_match = 1;
                report->initialized_table_index = index;
            }
            break;
        }
    }
    if (g_context.initialized_table_count != 0 && index == g_context.initialized_table_count) {
        return gxos_crt_onexit_fail(report, GXOS_CRT_ONEXIT_STATUS_TABLE_NOT_INITIALIZED);
    }

    old_first_raw = (uintptr_t)table->first;
    old_last_raw = (uintptr_t)table->last;
    old_end_raw = (uintptr_t)table->end;
    first = gxos_crt_onexit_decode_pointer(old_first_raw);
    last = gxos_crt_onexit_decode_pointer(old_last_raw);
    end = gxos_crt_onexit_decode_pointer(old_end_raw);
    if (report != 0) {
        report->table_first_raw = old_first_raw;
        report->table_last_raw = old_last_raw;
        report->table_end_raw = old_end_raw;
        report->first = first;
        report->last = last;
        report->end = end;
    }
    if (gxos_crt_onexit_get_encoded_null() == 0) {
        return gxos_crt_onexit_fail(report, GXOS_CRT_ONEXIT_STATUS_ENCODING_UNAVAILABLE);
    }
    if (callback_value != 0) {
        if (!gxos_crt_onexit_is_canonical(callback_value)) {
            return gxos_crt_onexit_fail(report, GXOS_CRT_ONEXIT_STATUS_NONCANONICAL_CALLBACK);
        }
        region = gxos_crt_onexit_find_address(callback_value, 1);
        if (region == 0) {
            return gxos_crt_onexit_fail(report, GXOS_CRT_ONEXIT_STATUS_NONEXECUTABLE_CALLBACK);
        }
        if (report != 0) {
            report->callback_region_base = region->base;
            report->callback_region_end = region->end;
            report->callback_region_executable = region->executable;
        }
    }
    if ((first == 0 || last == 0 || end == 0) &&
        !(first == 0 && last == 0 && end == 0)) {
        return gxos_crt_onexit_fail(report, GXOS_CRT_ONEXIT_STATUS_INVALID_TABLE_STATE);
    }
    if (first != 0) {
        if (!gxos_crt_onexit_is_canonical(first) ||
            !gxos_crt_onexit_is_canonical(last) ||
            !gxos_crt_onexit_is_canonical(end)) {
            return gxos_crt_onexit_fail(report, GXOS_CRT_ONEXIT_STATUS_NONCANONICAL_STORAGE);
        }
        if ((first & (sizeof(uintptr_t) - 1U)) != 0 ||
            (last & (sizeof(uintptr_t) - 1U)) != 0 ||
            (end & (sizeof(uintptr_t) - 1U)) != 0) {
            return gxos_crt_onexit_fail(report, GXOS_CRT_ONEXIT_STATUS_UNALIGNED_STORAGE);
        }
        if (first > last || last > end ||
            end - first > UINT32_MAX * sizeof(uintptr_t) ||
            ((end - first) & (sizeof(uintptr_t) - 1U)) != 0) {
            return gxos_crt_onexit_fail(report, GXOS_CRT_ONEXIT_STATUS_INVALID_TABLE_STATE);
        }
        region = gxos_crt_onexit_find_range(first, end, 1, 1);
        if (region == 0) {
            return gxos_crt_onexit_fail(report, GXOS_CRT_ONEXIT_STATUS_STORAGE_RANGE_INVALID);
        }
        if (report != 0) {
            report->storage_region_base = region->base;
            report->storage_region_end = region->end;
            report->storage_region_readable = region->readable;
            report->storage_region_writable = region->writable;
        }
        used = (uint32_t)((last - first) / sizeof(uintptr_t));
        capacity = (uint32_t)((end - first) / sizeof(uintptr_t));
    } else {
        used = 0;
        capacity = 0;
    }
    if (report != 0) {
        report->used_count = used;
        report->capacity = capacity;
        report->remaining_capacity = capacity - used;
    }
    if (last == end) {
        if (report != 0) report->growth_required = 1;
        return gxos_crt_onexit_fail(report, GXOS_CRT_ONEXIT_STATUS_GROWTH_REQUIRED);
    }
    if (last == 0 || end == 0 || last > UINTPTR_MAX - sizeof(uintptr_t)) {
        return gxos_crt_onexit_fail(report, GXOS_CRT_ONEXIT_STATUS_POINTER_OVERFLOW);
    }
    offset = last - first;
    slot_address = last;
    new_last = last + sizeof(uintptr_t);
    if (new_last > end || offset / sizeof(uintptr_t) > UINT32_MAX) {
        return gxos_crt_onexit_fail(report, GXOS_CRT_ONEXIT_STATUS_CAPACITY_OVERFLOW);
    }
    encoded_callback = gxos_crt_onexit_encode_pointer(callback_value);
    if (encoded_callback == 0) {
        return gxos_crt_onexit_fail(report, GXOS_CRT_ONEXIT_STATUS_ENCODING_UNAVAILABLE);
    }
    old_slot_value = (uintptr_t)*(GXOS_CRT_ONEXIT_PVFV *)(uintptr_t)slot_address;
    *(GXOS_CRT_ONEXIT_PVFV *)(uintptr_t)slot_address =
        (GXOS_CRT_ONEXIT_PVFV)(uintptr_t)encoded_callback;
    table->first = (GXOS_CRT_ONEXIT_PVFV *)(uintptr_t)
        gxos_crt_onexit_encode_pointer(first);
    table->last = (GXOS_CRT_ONEXIT_PVFV *)(uintptr_t)
        gxos_crt_onexit_encode_pointer(new_last);
    table->end = (GXOS_CRT_ONEXIT_PVFV *)(uintptr_t)
        gxos_crt_onexit_encode_pointer(end);
    if (report != 0) {
        report->entry_index = used;
        report->encoded_callback = encoded_callback;
        report->stored_value = encoded_callback;
        report->pointer_encoded = encoded_callback != callback_value;
        report->last = new_last;
    }
    if (gxos_crt_onexit_decode_pointer((uintptr_t)table->last) != new_last ||
        gxos_crt_onexit_decode_pointer((uintptr_t)table->first) != first ||
        gxos_crt_onexit_decode_pointer((uintptr_t)table->end) != end ||
        (uintptr_t)*(GXOS_CRT_ONEXIT_PVFV *)(uintptr_t)slot_address != encoded_callback) {
        *(GXOS_CRT_ONEXIT_PVFV *)(uintptr_t)slot_address =
            (GXOS_CRT_ONEXIT_PVFV)(uintptr_t)old_slot_value;
        table->first = (GXOS_CRT_ONEXIT_PVFV *)(uintptr_t)old_first_raw;
        table->last = (GXOS_CRT_ONEXIT_PVFV *)(uintptr_t)old_last_raw;
        table->end = (GXOS_CRT_ONEXIT_PVFV *)(uintptr_t)old_end_raw;
        if (report != 0) report->last = last;
        return gxos_crt_onexit_fail(report, GXOS_CRT_ONEXIT_STATUS_INVALID_TABLE_STATE);
    }
    if (report != 0) report->status = GXOS_CRT_ONEXIT_STATUS_OK;
    return GXOS_CRT_ONEXIT_STATUS_OK;
}

#include "platform_performance.h"

#include <limits.h>
#include <stddef.h>

#define GXOS_PERF_CPUID_LEAF_TSC_FREQUENCY 0x15u
#define GXOS_PERF_CPUID_EXTENDED_FEATURES 0x80000007u
#define GXOS_PERF_INVARIANT_TSC_BIT 8u
#define GXOS_PERF_ACPI_CONFIG_TABLE_GUID 1u
#define GXOS_PERF_ACPI_RSDP_REVISION 2u
#define GXOS_PERF_ACPI_PM_TIMER_FREQUENCY 3579545ULL
#define GXOS_PERF_ACPI_TMR_VAL_EXT (1u << 8)
#define GXOS_PERF_ACPI_MAX_TABLE_LENGTH 0x100000u
#define GXOS_PERF_MAX_CONFIG_TABLES 256u

static GXOS_PERF_CONTEXT g_perf_context;
static GXOS_PERF_STATE g_perf_state;
static uint64_t g_perf_frequency;
static uint32_t g_perf_initialized;
static uint32_t g_perf_first_trace_emitted;
static uint32_t g_perf_ok_trace_emitted;
static uint32_t g_perf_source;

typedef struct {
    uint16_t port;
    uint32_t width;
    uint32_t mask;
    uint32_t last_raw;
    uint64_t extended;
    uint32_t initialized;
} GXOS_PERF_PM_TIMER;

static GXOS_PERF_PM_TIMER g_pm_timer;

static void gxos_perf_trace(const char *marker, uint64_t value, uint32_t has_value)
{
    if (g_perf_context.trace != 0) g_perf_context.trace(marker, value, has_value);
}

static void gxos_perf_set_phase(uint32_t phase)
{
    if (g_perf_context.set_phase != 0) g_perf_context.set_phase(phase);
}

static void gxos_perf_publish_state(void)
{
    if (g_perf_context.frequency != 0) *g_perf_context.frequency = g_perf_frequency;
    if (g_perf_context.last_raw != 0) *g_perf_context.last_raw = g_perf_state.last_raw;
    if (g_perf_context.last_normalized != 0) *g_perf_context.last_normalized = g_perf_state.last_value;
    if (g_perf_context.call_count != 0) *g_perf_context.call_count = g_perf_state.call_count;
    if (g_perf_context.first_value != 0) *g_perf_context.first_value = g_perf_state.first_value;
    if (g_perf_context.last_value != 0) *g_perf_context.last_value = g_perf_state.last_value;
    if (g_perf_context.minimum_delta != 0) *g_perf_context.minimum_delta = g_perf_state.minimum_delta;
    if (g_perf_context.maximum_delta != 0) *g_perf_context.maximum_delta = g_perf_state.maximum_delta;
    if (g_perf_context.regressions != 0) *g_perf_context.regressions = g_perf_state.regressions;
}

int gxos_perf_calculate_cpuid15_frequency(uint32_t denominator,
                                          uint32_t numerator,
                                          uint32_t crystal_hz,
                                          uint64_t *frequency)
{
    uint64_t product;
    uint64_t quotient;

    if (frequency == 0) return 0;
    *frequency = 0;
    if (denominator == 0 || numerator == 0 || crystal_hz == 0) return 0;
    product = (uint64_t)numerator * (uint64_t)crystal_hz;
    quotient = product / denominator;
    if (quotient == 0 || quotient > (uint64_t)INT64_MAX) return 0;
    if ((product % denominator) != 0) return 0;
    *frequency = (uint64_t)quotient;
    return 1;
}

int gxos_perf_calculate_calibrated_frequency(uint64_t counter_delta,
                                             uint64_t reference_ticks,
                                             uint64_t reference_frequency,
                                             uint64_t *frequency)
{
    uint64_t product;
    uint64_t quotient;

    if (frequency == 0) return 0;
    *frequency = 0;
    if (counter_delta == 0 || reference_ticks == 0 || reference_frequency == 0) return 0;
    if (counter_delta > UINT64_MAX / reference_frequency) return 0;
    product = counter_delta * reference_frequency;
    quotient = product / reference_ticks;
    if (quotient == 0 || quotient > (uint64_t)INT64_MAX) return 0;
    *frequency = (uint64_t)quotient;
    return 1;
}

int gxos_perf_normalize_counter(uint64_t start_raw, uint64_t raw, int64_t *normalized)
{
    uint64_t delta;

    if (normalized == 0) return 0;
    if (raw < start_raw) return 0;
    delta = raw - start_raw;
    if (delta > (uint64_t)INT64_MAX) return 0;
    *normalized = (int64_t)delta;
    return 1;
}

int gxos_perf_extend_wrapping_counter(uint32_t width, uint32_t previous,
                                      uint32_t current, uint64_t *extension,
                                      uint32_t *normalized)
{
    uint32_t mask;
    uint32_t delta;

    if (extension == 0 || normalized == 0) return 0;
    if (width != 24u && width != 32u) return 0;
    mask = width == 32u ? UINT32_MAX : ((1u << width) - 1u);
    previous &= mask;
    current &= mask;
    delta = (current - previous) & mask;
    if (delta > (mask >> 1)) return 0;
    if (*extension > UINT64_MAX - delta) return 0;
    *extension += delta;
    *normalized = current;
    return 1;
}

int gxos_perf_record_counter(GXOS_PERF_STATE *state, uint64_t raw, int64_t *normalized)
{
    int64_t candidate;
    uint64_t delta;

    if (state == 0 || normalized == 0 || !state->initialized) return GXOS_PERF_NOT_INITIALIZED;
    if (!gxos_perf_normalize_counter(state->start_raw, raw, &candidate)) {
        state->regressions++;
        return GXOS_PERF_REGRESSION;
    }
    if (state->call_count != 0) {
        if (candidate < state->last_value) {
            state->regressions++;
            return GXOS_PERF_REGRESSION;
        }
        delta = (uint64_t)(candidate - state->last_value);
        if (state->call_count == 1 || delta < state->minimum_delta) state->minimum_delta = delta;
        if (delta > state->maximum_delta) state->maximum_delta = delta;
    } else {
        state->first_value = candidate;
        state->minimum_delta = 0;
        state->maximum_delta = 0;
    }
    state->last_raw = raw;
    state->last_value = candidate;
    state->call_count++;
    *normalized = candidate;
    return GXOS_PERF_OK;
}

int gxos_perf_source_is_supported(uint32_t invariant_tsc, uint32_t max_basic,
                                  uint32_t cpuid15_denominator, uint32_t cpuid15_numerator,
                                  uint32_t cpuid15_crystal_hz)
{
    return invariant_tsc != 0 && max_basic >= GXOS_PERF_CPUID_LEAF_TSC_FREQUENCY &&
           cpuid15_denominator != 0 && cpuid15_numerator != 0 && cpuid15_crystal_hz != 0;
}

static void gxos_perf_cpuid(uint32_t leaf, uint32_t subleaf, uint32_t registers[4])
{
    __asm__ volatile ("cpuid"
                     : "=a"(registers[0]), "=b"(registers[1]),
                       "=c"(registers[2]), "=d"(registers[3])
                     : "a"(leaf), "c"(subleaf));
}

typedef struct {
    uint32_t data1;
    uint16_t data2;
    uint16_t data3;
    uint8_t data4[8];
} GXOS_PERF_GUID;

typedef struct {
    GXOS_PERF_GUID vendor_guid;
    void *vendor_table;
} GXOS_PERF_CONFIGURATION_TABLE;

static const GXOS_PERF_GUID g_acpi20_guid = {
    0x8868E871u, 0xE4F1u, 0x11D3u, {0xBC, 0x22, 0x00, 0x80, 0xC7, 0x3C, 0x88, 0x81}
};

static const GXOS_PERF_GUID g_acpi10_guid = {
    0xEB9D2D30u, 0x2D88u, 0x11D3u, {0x9A, 0x16, 0x00, 0x90, 0x27, 0x3F, 0xC1, 0x4D}
};

static uint32_t gxos_perf_acpi_u32(const uint8_t *bytes)
{
    return (uint32_t)bytes[0] |
           ((uint32_t)bytes[1] << 8) |
           ((uint32_t)bytes[2] << 16) |
           ((uint32_t)bytes[3] << 24);
}

static uint64_t gxos_perf_acpi_u64(const uint8_t *bytes)
{
    return (uint64_t)gxos_perf_acpi_u32(bytes) |
           ((uint64_t)gxos_perf_acpi_u32(bytes + 4) << 32);
}

static int gxos_perf_acpi_signature(const uint8_t *bytes, const char *signature)
{
    uint32_t i;
    for (i = 0; i != 4u; i++) if (bytes[i] != (uint8_t)signature[i]) return 0;
    return 1;
}

static int gxos_perf_acpi_guid_equal(const GXOS_PERF_GUID *left, const GXOS_PERF_GUID *right)
{
    uint32_t i;
    if (left->data1 != right->data1 || left->data2 != right->data2 || left->data3 != right->data3) return 0;
    for (i = 0; i != 8u; i++) if (left->data4[i] != right->data4[i]) return 0;
    return 1;
}

static int gxos_perf_acpi_checksum(const uint8_t *bytes, uint32_t length)
{
    uint32_t i;
    uint8_t sum = 0;
    if (bytes == 0 || length == 0 || length > GXOS_PERF_ACPI_MAX_TABLE_LENGTH) return 0;
    for (i = 0; i != length; i++) sum = (uint8_t)(sum + bytes[i]);
    return sum == 0;
}

static int gxos_perf_acpi_root(const uint8_t *rsdp, uint64_t *root_address, uint32_t *entry_size)
{
    uint8_t revision;
    uint32_t length;
    uint64_t xsdt;
    uint32_t rsdt;

    if (rsdp == 0 || root_address == 0 || entry_size == 0) return 0;
    if (!gxos_perf_acpi_signature(rsdp, "RSD ") || rsdp[4] != 'P' || rsdp[5] != 'T' ||
        rsdp[6] != 'R' || rsdp[7] != ' ') return 0;
    if (!gxos_perf_acpi_checksum(rsdp, 20u)) return 0;
    revision = rsdp[15];
    rsdt = gxos_perf_acpi_u32(rsdp + 16);
    if (revision >= GXOS_PERF_ACPI_RSDP_REVISION) {
        length = gxos_perf_acpi_u32(rsdp + 20);
        if (length < 36u || !gxos_perf_acpi_checksum(rsdp, length)) return 0;
        xsdt = gxos_perf_acpi_u64(rsdp + 24);
        if (xsdt != 0) {
            *root_address = xsdt;
            *entry_size = 8u;
            return 1;
        }
    }
    if (rsdt == 0) return 0;
    *root_address = rsdt;
    *entry_size = 4u;
    return 1;
}

static int gxos_perf_acpi_find_pm_timer(const GXOS_PERF_CONTEXT *context,
                                        uint16_t *port, uint32_t *width)
{
    const GXOS_PERF_CONFIGURATION_TABLE *tables;
    const uint8_t *rsdp = 0;
    const uint8_t *root;
    const uint8_t *fadt = 0;
    uint64_t root_address;
    uint32_t entry_size;
    uint32_t root_length;
    uint32_t entry_count;
    uint32_t i;
    uint32_t pm_timer_port;
    uint32_t fadt_flags;
    uint32_t table_count;

    if (context == 0 || context->configuration_table == 0 || context->configuration_table_count == 0 ||
        port == 0 || width == 0) return 0;
    tables = (const GXOS_PERF_CONFIGURATION_TABLE *)context->configuration_table;
    table_count = context->configuration_table_count > GXOS_PERF_MAX_CONFIG_TABLES
                      ? GXOS_PERF_MAX_CONFIG_TABLES : (uint32_t)context->configuration_table_count;
    for (i = 0; i != table_count; i++) {
        if (gxos_perf_acpi_guid_equal(&tables[i].vendor_guid, &g_acpi20_guid) ||
            gxos_perf_acpi_guid_equal(&tables[i].vendor_guid, &g_acpi10_guid)) {
            rsdp = (const uint8_t *)tables[i].vendor_table;
            break;
        }
    }
    if (!gxos_perf_acpi_root(rsdp, &root_address, &entry_size)) return 0;
    root = (const uint8_t *)(uintptr_t)root_address;
    if (root == 0 || !gxos_perf_acpi_signature(root, entry_size == 8u ? "XSDT" : "RSDT")) return 0;
    root_length = gxos_perf_acpi_u32(root + 4);
    if (root_length < 36u || root_length > GXOS_PERF_ACPI_MAX_TABLE_LENGTH ||
        !gxos_perf_acpi_checksum(root, root_length) ||
        ((root_length - 36u) % entry_size) != 0u) return 0;
    entry_count = (root_length - 36u) / entry_size;
    for (i = 0; i != entry_count; i++) {
        uint64_t address = entry_size == 8u
            ? gxos_perf_acpi_u64(root + 36u + i * entry_size)
            : gxos_perf_acpi_u32(root + 36u + i * entry_size);
        const uint8_t *candidate = (const uint8_t *)(uintptr_t)address;
        uint32_t candidate_length;
        if (candidate == 0 || !gxos_perf_acpi_signature(candidate, "FACP")) continue;
        candidate_length = gxos_perf_acpi_u32(candidate + 4);
        if (candidate_length < 116u || candidate_length > GXOS_PERF_ACPI_MAX_TABLE_LENGTH ||
            !gxos_perf_acpi_checksum(candidate, candidate_length)) continue;
        fadt = candidate;
        break;
    }
    if (fadt == 0) return 0;
    pm_timer_port = gxos_perf_acpi_u32(fadt + 76);
    fadt_flags = gxos_perf_acpi_u32(fadt + 112);
    if (pm_timer_port == 0 || pm_timer_port > UINT16_MAX) return 0;
    *port = (uint16_t)pm_timer_port;
    *width = (fadt_flags & GXOS_PERF_ACPI_TMR_VAL_EXT) != 0 ? 32u : 24u;
    return 1;
}

static uint32_t gxos_perf_read_pm_timer(void)
{
    uint32_t value;
    __asm__ volatile ("inl %1, %0" : "=a"(value) : "Nd"(g_pm_timer.port));
    return value & g_pm_timer.mask;
}

static int gxos_perf_initialize_pm_timer(uint64_t *start_raw)
{
    uint16_t port;
    uint32_t width;
    uint32_t initial;

    if (!gxos_perf_acpi_find_pm_timer(&g_perf_context, &port, &width)) return 0;
    g_pm_timer.port = port;
    g_pm_timer.width = width;
    g_pm_timer.mask = width == 32u ? UINT32_MAX : ((1u << width) - 1u);
    initial = gxos_perf_read_pm_timer();
    g_pm_timer.last_raw = initial;
    g_pm_timer.extended = initial;
    g_pm_timer.initialized = 1;
    g_perf_source = GXOS_PERF_SOURCE_ACPI_PM_TIMER;
    g_perf_frequency = GXOS_PERF_ACPI_PM_TIMER_FREQUENCY;
    *start_raw = g_pm_timer.extended;
    gxos_perf_trace("PERF_SOURCE_ACPI_PM_TIMER", 0, 0);
    gxos_perf_trace("PERF_ACPI_PM_TIMER_PORT", port, 1);
    gxos_perf_trace("PERF_ACPI_PM_TIMER_WIDTH", width, 1);
    return 1;
}

static int gxos_perf_read_source(uint64_t *raw)
{
    uint32_t current;
    uint32_t delta;

    if (raw == 0) return 0;
    if (g_perf_source == GXOS_PERF_SOURCE_INVARIANT_TSC_CPUID_15) {
        extern uint64_t gxos_perf_read_tsc_for_source(void);
        *raw = gxos_perf_read_tsc_for_source();
        return 1;
    }
    if (g_perf_source != GXOS_PERF_SOURCE_ACPI_PM_TIMER || !g_pm_timer.initialized) return 0;
    current = gxos_perf_read_pm_timer();
    delta = (current - g_pm_timer.last_raw) & g_pm_timer.mask;
    if (delta > (g_pm_timer.mask >> 1) || g_pm_timer.extended > UINT64_MAX - delta) return 0;
    g_pm_timer.last_raw = current;
    g_pm_timer.extended += delta;
    *raw = g_pm_timer.extended;
    return 1;
}

uint64_t gxos_perf_read_tsc_for_source(void)
{
    uint32_t low;
    uint32_t high;
    __asm__ volatile ("lfence\n\t"
                      "rdtsc\n\t"
                      "lfence"
                      : "=a"(low), "=d"(high)
                      :
                      : "memory");
    return ((uint64_t)high << 32) | low;
}

int gxos_perf_configure(const GXOS_PERF_CONTEXT *context)
{
    uint32_t basic[4];
    uint32_t extended[4];
    uint32_t leaf15[4];
    uint32_t max_basic;
    uint32_t max_extended;
    uint32_t invariant_tsc;
    uint64_t frequency;
    uint64_t start_raw = 0;

    g_perf_context = (GXOS_PERF_CONTEXT){0};
    g_perf_state = (GXOS_PERF_STATE){0};
    g_perf_frequency = 0;
    g_perf_initialized = 0;
    g_perf_first_trace_emitted = 0;
    g_perf_ok_trace_emitted = 0;
    g_perf_source = GXOS_PERF_SOURCE_NONE;
    g_pm_timer = (GXOS_PERF_PM_TIMER){0};
    if (context != 0) g_perf_context = *context;

#ifdef GXOS_PERF_TEST_DISABLED
    gxos_perf_trace("PERF_SOURCE_DISCOVERY_BEGIN", 0, 0);
    gxos_perf_trace("PERF_SOURCE_UNAVAILABLE", 0, 0);
    return 0;
#endif

    gxos_perf_set_phase(11u);
    gxos_perf_trace("PERF_SOURCE_DISCOVERY_BEGIN", 0, 0);
    gxos_perf_set_phase(12u);
    gxos_perf_cpuid(0, 0, basic);
    max_basic = basic[0];
    gxos_perf_cpuid(0x80000000u, 0, extended);
    max_extended = extended[0];
    gxos_perf_trace("PERF_CPUID_MAX_BASIC", max_basic, 1);
    gxos_perf_trace("PERF_CPUID_MAX_EXTENDED", max_extended, 1);
    invariant_tsc = 0;
    if (max_extended >= GXOS_PERF_CPUID_EXTENDED_FEATURES) {
        gxos_perf_cpuid(GXOS_PERF_CPUID_EXTENDED_FEATURES, 0, extended);
        invariant_tsc = (extended[3] >> GXOS_PERF_INVARIANT_TSC_BIT) & 1u;
    }
    gxos_perf_trace("PERF_INVARIANT_TSC", invariant_tsc, 1);
    leaf15[0] = 0;
    leaf15[1] = 0;
    leaf15[2] = 0;
    leaf15[3] = 0;
    if (max_basic >= GXOS_PERF_CPUID_LEAF_TSC_FREQUENCY) {
        gxos_perf_cpuid(GXOS_PERF_CPUID_LEAF_TSC_FREQUENCY, 0, leaf15);
    }
    gxos_perf_trace("PERF_CPUID_15_DENOMINATOR", leaf15[0], 1);
    gxos_perf_trace("PERF_CPUID_15_NUMERATOR", leaf15[1], 1);
    gxos_perf_trace("PERF_CPUID_15_CRYSTAL_HZ", leaf15[2], 1);
    if (gxos_perf_source_is_supported(invariant_tsc, max_basic, leaf15[0], leaf15[1], leaf15[2]) &&
        gxos_perf_calculate_cpuid15_frequency(leaf15[0], leaf15[1], leaf15[2], &frequency)) {
        g_perf_source = GXOS_PERF_SOURCE_INVARIANT_TSC_CPUID_15;
        g_perf_frequency = frequency;
        gxos_perf_trace("PERF_SOURCE_TSC_INVARIANT_CPUID_15", 0, 0);
    } else if (!gxos_perf_initialize_pm_timer(&start_raw)) {
        gxos_perf_trace("PERF_SOURCE_UNAVAILABLE", 0, 0);
        return 0;
    }
    gxos_perf_set_phase(13u);
    gxos_perf_set_phase(14u);
    if (g_perf_source == GXOS_PERF_SOURCE_INVARIANT_TSC_CPUID_15) {
        start_raw = gxos_perf_read_tsc_for_source();
    }
    g_perf_state.start_raw = start_raw;
    g_perf_state.last_raw = start_raw;
    g_perf_state.last_value = 0;
    g_perf_state.initialized = 1;
    if (g_perf_context.source_code != 0) *g_perf_context.source_code = g_perf_source;
    if (g_perf_context.source_address != 0) {
        *g_perf_context.source_address = g_perf_source == GXOS_PERF_SOURCE_INVARIANT_TSC_CPUID_15
            ? (uint64_t)(uintptr_t)&gxos_perf_read_tsc_for_source
            : ((uint64_t)0x10000u | g_pm_timer.port);
    }
    if (g_perf_context.frequency != 0) *g_perf_context.frequency = g_perf_frequency;
    gxos_perf_trace("PERF_SOURCE_INIT_OK", 0, 0);
    gxos_perf_trace("PERF_FREQUENCY", g_perf_frequency, 1);
    gxos_perf_trace("PERF_INITIAL_RAW", start_raw, 1);
    g_perf_initialized = 1;
    gxos_perf_publish_state();
    return 1;
}

int gxos_perf_is_initialized(void)
{
    return g_perf_initialized != 0;
}

int gxos_perf_get_state(const GXOS_PERF_STATE **state)
{
    if (state == 0) return 0;
    *state = &g_perf_state;
    return 1;
}

int GXOS_PERF_EFIAPI gxos_query_performance_counter(void *output)
{
    uint64_t raw;
    int64_t normalized;
    uint32_t first_call;

    if (output == 0) return 0;
    if (!g_perf_initialized) return 0;
    gxos_perf_set_phase(15u);
    if (!g_perf_first_trace_emitted) {
        gxos_perf_trace("QPC_CALL", 0, 0);
        g_perf_first_trace_emitted = 1;
    }
    gxos_perf_set_phase(16u);
    if (!gxos_perf_read_source(&raw)) {
        g_perf_state.regressions++;
        gxos_perf_publish_state();
        return 0;
    }
    if (g_perf_context.last_raw != 0) *g_perf_context.last_raw = raw;
    first_call = g_perf_state.call_count == 0;
    if (gxos_perf_record_counter(&g_perf_state, raw, &normalized) != GXOS_PERF_OK) {
        gxos_perf_publish_state();
        return 0;
    }
    *(int64_t *)output = normalized;
    if (g_perf_context.last_normalized != 0) *g_perf_context.last_normalized = normalized;
    gxos_perf_publish_state();
    if (first_call && !g_perf_ok_trace_emitted) {
        gxos_perf_trace("QPC_OK", (uint64_t)normalized, 1);
        g_perf_ok_trace_emitted = 1;
    }
    gxos_perf_set_phase(17u);
    return 1;
}

int GXOS_PERF_EFIAPI gxos_query_performance_frequency(void *output)
{
    if (output == 0 || !g_perf_initialized || g_perf_frequency == 0 || g_perf_frequency > (uint64_t)INT64_MAX) {
        return 0;
    }
    *(int64_t *)output = (int64_t)g_perf_frequency;
    return 1;
}

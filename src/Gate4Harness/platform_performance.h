#ifndef GXOS_PLATFORM_PERFORMANCE_H
#define GXOS_PLATFORM_PERFORMANCE_H

#include <stdint.h>

#if defined(__x86_64__)
#define GXOS_PERF_EFIAPI __attribute__((ms_abi))
#else
#define GXOS_PERF_EFIAPI
#endif

typedef void (*GXOS_PERF_TRACE)(const char *marker, uint64_t value, uint32_t has_value);
typedef void (*GXOS_PERF_PHASE_SETTER)(uint32_t phase);
typedef void (*GXOS_PERF_HALT)(void);

typedef struct {
    uint64_t start_raw;
    uint64_t last_raw;
    int64_t last_value;
    int64_t first_value;
    uint64_t minimum_delta;
    uint64_t maximum_delta;
    uint64_t call_count;
    uint64_t regressions;
    uint32_t initialized;
} GXOS_PERF_STATE;

typedef struct {
    GXOS_PERF_TRACE trace;
    GXOS_PERF_PHASE_SETTER set_phase;
    GXOS_PERF_HALT halt;
    const void *configuration_table;
    uint64_t configuration_table_count;
    uint64_t *source_code;
    uint64_t *source_address;
    uint64_t *frequency;
    uint64_t *last_raw;
    int64_t *last_normalized;
    uint64_t *call_count;
    int64_t *first_value;
    int64_t *last_value;
    uint64_t *minimum_delta;
    uint64_t *maximum_delta;
    uint64_t *regressions;
} GXOS_PERF_CONTEXT;

enum {
    GXOS_PERF_SOURCE_NONE = 0,
    GXOS_PERF_SOURCE_INVARIANT_TSC_CPUID_15 = 1,
    GXOS_PERF_SOURCE_ACPI_PM_TIMER = 2
};

enum {
    GXOS_PERF_OK = 0,
    GXOS_PERF_NULL_OUTPUT = 1,
    GXOS_PERF_NOT_INITIALIZED = 2,
    GXOS_PERF_UNAVAILABLE = 3,
    GXOS_PERF_INVALID_METADATA = 4,
    GXOS_PERF_OVERFLOW = 5,
    GXOS_PERF_REGRESSION = 6
};

int gxos_perf_calculate_cpuid15_frequency(uint32_t denominator,
                                          uint32_t numerator,
                                          uint32_t crystal_hz,
                                          uint64_t *frequency);
int gxos_perf_calculate_calibrated_frequency(uint64_t counter_delta,
                                             uint64_t reference_ticks,
                                             uint64_t reference_frequency,
                                             uint64_t *frequency);
int gxos_perf_normalize_counter(uint64_t start_raw, uint64_t raw, int64_t *normalized);
int gxos_perf_record_counter(GXOS_PERF_STATE *state, uint64_t raw, int64_t *normalized);
int gxos_perf_extend_wrapping_counter(uint32_t width, uint32_t previous,
                                      uint32_t current, uint64_t *extension,
                                      uint32_t *normalized);
int gxos_perf_source_is_supported(uint32_t invariant_tsc, uint32_t max_basic,
                                  uint32_t cpuid15_denominator, uint32_t cpuid15_numerator,
                                  uint32_t cpuid15_crystal_hz);

int gxos_perf_configure(const GXOS_PERF_CONTEXT *context);
int gxos_perf_is_initialized(void);
int gxos_perf_get_state(const GXOS_PERF_STATE **state);
int GXOS_PERF_EFIAPI gxos_query_performance_counter(void *output);
int GXOS_PERF_EFIAPI gxos_query_performance_frequency(void *output);

#endif

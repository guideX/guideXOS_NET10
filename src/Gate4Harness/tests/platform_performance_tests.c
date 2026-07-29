#include "../platform_performance.h"

#include <stdio.h>
#include <stdint.h>

static int check(int condition, const char *name)
{
    if (!condition) {
        printf("FAIL %s\n", name);
        return 1;
    }
    return 0;
}

int main(void)
{
    GXOS_PERF_STATE state = {0};
    uint64_t frequency = UINT64_MAX;
    uint64_t extension;
    uint32_t normalized;
    int64_t value;
    int failures = 0;

    failures += check(gxos_query_performance_counter(0) == 0, "QPC null output");
    failures += check(gxos_query_performance_frequency(0) == 0, "QPF null output");

    failures += check(gxos_perf_calculate_cpuid15_frequency(3, 100, 24000000, &frequency) && frequency == 800000000ULL,
                      "cpuid15 exact frequency");
    failures += check(!gxos_perf_calculate_cpuid15_frequency(0, 100, 24000000, &frequency), "cpuid15 zero denominator");
    failures += check(!gxos_perf_calculate_cpuid15_frequency(3, 0, 24000000, &frequency), "cpuid15 zero numerator");
    failures += check(!gxos_perf_calculate_cpuid15_frequency(3, 100, 0, &frequency), "cpuid15 zero crystal");
    failures += check(!gxos_perf_calculate_cpuid15_frequency(1, UINT32_MAX, UINT32_MAX, &frequency),
                      "cpuid15 signed overflow");
    failures += check(!gxos_perf_calculate_cpuid15_frequency(3, 100, 24000000, 0), "cpuid15 null output");

    failures += check(gxos_perf_calculate_calibrated_frequency(1000, 10, 1000000, &frequency) && frequency == 100000000ULL,
                      "calibrated frequency");
    failures += check(!gxos_perf_calculate_calibrated_frequency(0, 10, 1000000, &frequency), "calibration zero counter interval");
    failures += check(!gxos_perf_calculate_calibrated_frequency(1000, 0, 1000000, &frequency), "calibration zero reference interval");
    failures += check(!gxos_perf_calculate_calibrated_frequency(UINT64_MAX, 2, 2, &frequency),
                      "calibration multiplication overflow");
    failures += check(!gxos_perf_calculate_calibrated_frequency(1, UINT64_MAX, 1, &frequency),
                      "calibration quotient below one");

    failures += check(gxos_perf_normalize_counter(100, 100, &value) && value == 0, "counter equality");
    failures += check(gxos_perf_normalize_counter(100, 101, &value) && value == 1, "counter increment");
    failures += check(!gxos_perf_normalize_counter(100, 99, &value), "counter regression");
    failures += check(gxos_perf_normalize_counter(0, (uint64_t)INT64_MAX, &value) && value == INT64_MAX,
                      "signed 64-bit boundary");
    failures += check(!gxos_perf_normalize_counter(0, (uint64_t)INT64_MAX + 1ULL, &value),
                      "signed 64-bit overflow");
    failures += check(!gxos_perf_normalize_counter(0, 1, 0), "normalize null output");

    state.start_raw = 1000;
    state.initialized = 1;
    failures += check(gxos_perf_record_counter(&state, 1000, &value) == GXOS_PERF_OK && value == 0,
                      "state first value");
    failures += check(gxos_perf_record_counter(&state, 1000, &value) == GXOS_PERF_OK && value == 0,
                      "state equal value");
    failures += check(gxos_perf_record_counter(&state, 1010, &value) == GXOS_PERF_OK && value == 10 &&
                      state.minimum_delta == 0 && state.maximum_delta == 10,
                      "state delta statistics");
    failures += check(gxos_perf_record_counter(&state, 1005, &value) == GXOS_PERF_REGRESSION && state.regressions == 1,
                      "state forced regression");
    failures += check(state.call_count == 3, "state rejected call not counted");

    extension = 0;
    failures += check(gxos_perf_extend_wrapping_counter(24, 0x00FFFFFEu, 0x00000001u, &extension, &normalized) &&
                      extension == 3 && normalized == 1,
                      "24-bit wrap");
    extension = 0;
    failures += check(gxos_perf_extend_wrapping_counter(32, 0xFFFFFFFEu, 0x00000001u, &extension, &normalized) &&
                      extension == 3 && normalized == 1,
                      "32-bit wrap");
    failures += check(!gxos_perf_extend_wrapping_counter(16, 0, 1, &extension, &normalized), "invalid wrap width");
    failures += check(!gxos_perf_extend_wrapping_counter(24, 0, 0x00800000u, &extension, &normalized),
                      "ambiguous wrap interval");
    extension = UINT64_MAX;
    failures += check(!gxos_perf_extend_wrapping_counter(24, 0, 1, &extension, &normalized), "wrap extension overflow");

    failures += check(gxos_perf_source_is_supported(1, 0x15, 3, 100, 24000000), "invariant TSC source selection");
    failures += check(!gxos_perf_source_is_supported(0, 0x15, 3, 100, 24000000), "non-invariant TSC rejected");
    failures += check(!gxos_perf_source_is_supported(1, 0x14, 3, 100, 24000000), "missing CPUID leaf rejected");
    failures += check(!gxos_perf_source_is_supported(1, 0x15, 0, 100, 24000000), "invalid TSC metadata rejected");

    printf("PLATFORM_PERFORMANCE_TESTS=%s failures=%d\n", failures == 0 ? "PASSED" : "FAILED", failures);
    return failures == 0 ? 0 : 1;
}

#include <stdint.h>
#include <stdio.h>

#include "nativeaot_gc_probe_contract.h"

static uint32_t failures;

static void expect(int condition, const char *message)
{
    if (!condition) {
        ++failures;
        printf("FAIL: %s\n", message);
    }
}

static void test_expected_checksums(void)
{
    expect((gxos_nativeaot_gc_expected_checksum(0x31U) & 0x0FFFU) == 0x908U,
           "main seed checksum is deterministic");
    expect((gxos_nativeaot_gc_expected_checksum(0x51U) & 0x0FFFU) == 0xA08U,
           "first worker seed checksum is deterministic");
    expect((gxos_nativeaot_gc_expected_checksum(0x52U) & 0x0FFFU) == 0xC00U,
           "repeat worker seed checksum is deterministic");
}

static void test_result_decoding(void)
{
    uint32_t delta = 0;
    uint32_t generation = 0;
    uint32_t checksum = 0;
    int32_t result = (int32_t)(GXOS_NATIVEAOT_GC_PROBE_SUCCESS |
                               (1U << 16) | (1U << 12) | 0xA08U);

    expect(gxos_nativeaot_gc_result_valid(
               result, 0x51U, &delta, &generation, &checksum),
           "valid result is accepted");
    expect(delta == 1U && generation == 1U && checksum == 0xA08U,
           "valid result fields decode");
    expect(!gxos_nativeaot_gc_result_valid(
                (int32_t)(GXOS_NATIVEAOT_GC_PROBE_SUCCESS |
                          (1U << 12) | 0xA08U),
                0x51U, 0, 0, 0),
           "zero collection delta is rejected");
    expect(!gxos_nativeaot_gc_result_valid(
                (int32_t)(GXOS_NATIVEAOT_GC_PROBE_SUCCESS |
                          (1U << 16) | (1U << 12) | 0xA09U),
                0x51U, 0, 0, 0),
           "wrong checksum is rejected");
    expect(!gxos_nativeaot_gc_result_valid(
                (int32_t)(0xB0000000U | (1U << 16) | (1U << 12) | 0xA08U),
                0x51U, 0, 0, 0),
           "wrong result signature is rejected");
}

int main(void)
{
    test_expected_checksums();
    test_result_decoding();
    if (failures != 0) {
        printf("NATIVEAOT_GC_PROBE_CONTRACT_TESTS=FAILED failures=%u\n",
               failures);
        return 1;
    }
    printf("NATIVEAOT_GC_PROBE_CONTRACT_TESTS=PASSED checks=8\n");
    return 0;
}

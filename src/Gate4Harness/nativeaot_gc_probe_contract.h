#ifndef GXOS_NATIVEAOT_GC_PROBE_CONTRACT_H
#define GXOS_NATIVEAOT_GC_PROBE_CONTRACT_H

#include <stdint.h>

#define GXOS_NATIVEAOT_GC_PROBE_SUCCESS 0xC0000000U

static inline uint32_t gxos_nativeaot_gc_expected_checksum(uint32_t seed)
{
    uint32_t checksum = 0;
    uint32_t index;
    for (index = 0; index != 8U; ++index) {
        uint32_t value = seed * 257U + 0x1234U + index * 17U;
        checksum = (checksum * 33U) ^ value;
    }
    return checksum;
}

static inline int gxos_nativeaot_gc_result_valid(
    int32_t result, uint32_t seed, uint32_t *collection_delta,
    uint32_t *generation, uint32_t *checksum)
{
    uint32_t encoded = (uint32_t)result;
    uint32_t expected_checksum = gxos_nativeaot_gc_expected_checksum(seed);
    if ((encoded & 0xF0000000U) != GXOS_NATIVEAOT_GC_PROBE_SUCCESS ||
        ((encoded >> 16) & 0xFFU) == 0U ||
        (encoded & 0x0FFFU) != (expected_checksum & 0x0FFFU)) {
        return 0;
    }
    if (collection_delta != 0) *collection_delta = (encoded >> 16) & 0xFFU;
    if (generation != 0) *generation = (encoded >> 12) & 0x0FU;
    if (checksum != 0) *checksum = encoded & 0x0FFFU;
    return 1;
}

#endif

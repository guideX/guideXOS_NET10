#ifndef GXOS_CRT_MALLOC_TRACE_FIXTURE_H
#define GXOS_CRT_MALLOC_TRACE_FIXTURE_H

#include <stdint.h>

/*
 * Verified source:
 * artifacts/windows-malloc-oracle-20260804-033340/native-run-3/
 *   malloc-events.csv and canonical-sequence.txt
 *
 * Native-run 1 and 2 contain the same 39 payload calls but report 64184 at
 * position 6 instead of 64188.  This fixture preserves the selected native
 * run 3 trace used by the captured 40-item transcription after removing its
 * duplicated 8-byte entry.
 */
#define GXOS_CRT_MALLOC_CANONICAL_CALL_COUNT 39U

static const uint64_t gxos_crt_malloc_canonical_sizes[
    GXOS_CRT_MALLOC_CANONICAL_CALL_COUNT] = {
    88, 72, 56, 8, 8, 64188, 80, 864, 819200, 6448,
    8, 8, 8, 8, 8, 8, 64, 40, 32, 8,
    8, 8, 147456, 88, 800, 1368, 640, 80, 24, 8,
    12520, 32, 30, 64, 24, 16, 48, 16, 168
};

_Static_assert(
    sizeof(gxos_crt_malloc_canonical_sizes) /
        sizeof(gxos_crt_malloc_canonical_sizes[0]) ==
        GXOS_CRT_MALLOC_CANONICAL_CALL_COUNT,
    "canonical malloc trace count changed");

#endif

#ifndef GXOS_CRT_STRICMP_H
#define GXOS_CRT_STRICMP_H

#include <stddef.h>
#include <stdint.h>

#include "crt_strlen.h"

#if defined(__x86_64__)
#define GXOS_CRT_STRICMP_MS_ABI __attribute__((ms_abi))
#else
#define GXOS_CRT_STRICMP_MS_ABI
#endif

typedef enum {
    GXOS_CRT_STRICMP_STATUS_OK = 0,
    GXOS_CRT_STRICMP_STATUS_NULL_POINTER,
    GXOS_CRT_STRICMP_STATUS_NONCANONICAL_POINTER,
    GXOS_CRT_STRICMP_STATUS_UNREADABLE_POINTER,
    GXOS_CRT_STRICMP_STATUS_UNTERMINATED,
    GXOS_CRT_STRICMP_STATUS_SCAN_LIMIT,
    GXOS_CRT_STRICMP_STATUS_POINTER_OVERFLOW,
    GXOS_CRT_STRICMP_STATUS_UNSUPPORTED_LOCALE,
    GXOS_CRT_STRICMP_STATUS_INVALID_CONTEXT,
    GXOS_CRT_STRICMP_STATUS_INVALID_OUTPUT
} GXOS_CRT_STRICMP_STATUS;

typedef struct {
    size_t string1_length;
    size_t string2_length;
    uintptr_t string1_terminator;
    uintptr_t string2_terminator;
    size_t bytes_examined;
    size_t compared_prefix;
} GXOS_CRT_STRICMP_REPORT;

#define GXOS_CRT_STRICMP_DEFAULT_MAX_SCAN ((size_t)0x10000U)
#define GXOS_CRT_STRICMP_MAX_MEMORY_REGIONS 32U

GXOS_CRT_STRICMP_STATUS GXOS_CRT_STRICMP_MS_ABI gxos_crt_stricmp_checked(
    const char *string1,
    const char *string2,
    const GXOS_READABLE_IMAGE *image,
    size_t maximum_scan_per_string,
    int *comparison_out);

GXOS_CRT_STRICMP_STATUS GXOS_CRT_STRICMP_MS_ABI gxos_crt_stricmp_checked_report(
    const char *string1,
    const char *string2,
    const GXOS_READABLE_IMAGE *image,
    size_t maximum_scan_per_string,
    int *comparison_out,
    GXOS_CRT_STRICMP_REPORT *report);

#endif

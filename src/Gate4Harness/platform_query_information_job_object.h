#ifndef GXOS_PLATFORM_QUERY_INFORMATION_JOB_OBJECT_H
#define GXOS_PLATFORM_QUERY_INFORMATION_JOB_OBJECT_H

#include <stddef.h>
#include <stdint.h>

#include "platform_system_info.h"

#if defined(__x86_64__)
#define GXOS_QUERY_JOB_MS_ABI __attribute__((ms_abi))
#else
#define GXOS_QUERY_JOB_MS_ABI
#endif

typedef int32_t GXOS_QUERY_JOB_BOOL;
typedef uintptr_t GXOS_QUERY_JOB_HANDLE;
typedef uint32_t GXOS_QUERY_JOB_INFO_CLASS;
typedef void *GXOS_QUERY_JOB_OUTPUT;
typedef uint32_t GXOS_QUERY_JOB_DWORD;
typedef GXOS_QUERY_JOB_DWORD *GXOS_QUERY_JOB_RETURN_LENGTH;

#define GXOS_QUERY_JOB_TRUE ((GXOS_QUERY_JOB_BOOL)1)
#define GXOS_QUERY_JOB_FALSE ((GXOS_QUERY_JOB_BOOL)0)
#define GXOS_QUERY_JOB_CURRENT_HANDLE ((uintptr_t)0)
#define GXOS_QUERY_JOB_CPU_RATE_CLASS ((uint32_t)15U)
#define GXOS_QUERY_JOB_CPU_RATE_STRUCTURE_SIZE ((uint32_t)8U)
#define GXOS_QUERY_JOB_CPU_RATE_VALID_FLAGS ((uint32_t)0x1FU)
#define GXOS_QUERY_JOB_CPU_RATE_ENABLE ((uint32_t)0x1U)
#define GXOS_QUERY_JOB_CPU_RATE_WEIGHT_BASED ((uint32_t)0x2U)
#define GXOS_QUERY_JOB_CPU_RATE_HARD_CAP ((uint32_t)0x4U)
#define GXOS_QUERY_JOB_CPU_RATE_NOTIFY ((uint32_t)0x8U)
#define GXOS_QUERY_JOB_CPU_RATE_MIN_MAX ((uint32_t)0x10U)

#define GXOS_QUERY_JOB_ERROR_ACCESS_DENIED ((uint32_t)5U)
#define GXOS_QUERY_JOB_ERROR_INVALID_HANDLE ((uint32_t)6U)
#define GXOS_QUERY_JOB_ERROR_INVALID_PARAMETER ((uint32_t)87U)
#define GXOS_QUERY_JOB_ERROR_INSUFFICIENT_BUFFER ((uint32_t)122U)
#define GXOS_QUERY_JOB_ERROR_NOACCESS ((uint32_t)998U)

typedef struct GXOS_QUERY_JOB_CPU_RATE_RANGE {
    uint16_t min_rate;
    uint16_t max_rate;
} GXOS_QUERY_JOB_CPU_RATE_RANGE;

typedef union GXOS_QUERY_JOB_CPU_RATE_UNION {
    uint32_t cpu_rate;
    uint32_t weight;
    GXOS_QUERY_JOB_CPU_RATE_RANGE rate_range;
} GXOS_QUERY_JOB_CPU_RATE_UNION;

typedef struct GXOS_QUERY_JOB_CPU_RATE_INFORMATION {
    uint32_t control_flags;
    GXOS_QUERY_JOB_CPU_RATE_UNION rate;
} GXOS_QUERY_JOB_CPU_RATE_INFORMATION;

typedef struct GXOS_QUERY_JOB_FACTS {
    GXOS_QUERY_JOB_HANDLE supported_job_handle;
    uint32_t associated_job;
    uint32_t control_flags;
    uint32_t cpu_rate;
    uint32_t weight;
    uint16_t min_rate;
    uint16_t max_rate;
} GXOS_QUERY_JOB_FACTS;

typedef enum GXOS_QUERY_JOB_STATUS {
    GXOS_QUERY_JOB_STATUS_OK = 0,
    GXOS_QUERY_JOB_STATUS_NO_ASSOCIATED_JOB,
    GXOS_QUERY_JOB_STATUS_INVALID_HANDLE,
    GXOS_QUERY_JOB_STATUS_UNSUPPORTED_INFORMATION_CLASS,
    GXOS_QUERY_JOB_STATUS_NULL_OUTPUT,
    GXOS_QUERY_JOB_STATUS_NONCANONICAL_OUTPUT,
    GXOS_QUERY_JOB_STATUS_UNWRITABLE_OUTPUT,
    GXOS_QUERY_JOB_STATUS_INSUFFICIENT_OUTPUT,
    GXOS_QUERY_JOB_STATUS_NONCANONICAL_RETURN_LENGTH,
    GXOS_QUERY_JOB_STATUS_UNWRITABLE_RETURN_LENGTH,
    GXOS_QUERY_JOB_STATUS_LAYOUT_MISMATCH,
    GXOS_QUERY_JOB_STATUS_INVALID_JOB_FACTS,
    GXOS_QUERY_JOB_STATUS_RANGE_OVERFLOW,
    GXOS_QUERY_JOB_STATUS_ALIASED_OUTPUTS,
    GXOS_QUERY_JOB_STATUS_INVALID_FLAGS,
    GXOS_QUERY_JOB_STATUS_INVALID_RATE
} GXOS_QUERY_JOB_STATUS;

typedef struct GXOS_QUERY_JOB_REPORT {
    uint32_t output_pointer_canonical;
    uint32_t output_pointer_writable;
    uint32_t output_range_valid;
    uint32_t return_length_pointer_canonical;
    uint32_t return_length_pointer_writable;
    uint32_t return_length_range_valid;
    uint32_t output_alignment;
    uint32_t return_length_alignment;
    uint32_t output_length_accepted;
    uint32_t output_bytes_before_valid;
    uint32_t output_bytes_after_valid;
    uint32_t return_length_before_valid;
    uint32_t return_length_after_valid;
    uint32_t output_written;
    uint32_t return_length_written;
    uint32_t output_before_low;
    uint32_t output_before_high;
    uint32_t output_after_low;
    uint32_t output_after_high;
    uint32_t return_length_before;
    uint32_t return_length_after;
} GXOS_QUERY_JOB_REPORT;

_Static_assert(sizeof(GXOS_QUERY_JOB_BOOL) == 4,
               "QueryInformationJobObject BOOL must remain 32 bits");
_Static_assert(sizeof(GXOS_QUERY_JOB_HANDLE) == 8,
               "QueryInformationJobObject HANDLE must remain 64 bits on x64");
_Static_assert(sizeof(GXOS_QUERY_JOB_INFO_CLASS) == 4,
               "JOBOBJECTINFOCLASS must remain 32 bits");
_Static_assert(sizeof(GXOS_QUERY_JOB_DWORD) == 4,
               "QueryInformationJobObject DWORD must remain 32 bits");
_Static_assert(sizeof(GXOS_QUERY_JOB_OUTPUT) == 8,
               "QueryInformationJobObject LPVOID must remain 64 bits on x64");
_Static_assert(sizeof(GXOS_QUERY_JOB_RETURN_LENGTH) == 8,
               "QueryInformationJobObject LPDWORD must remain 64 bits on x64");
_Static_assert(sizeof(GXOS_QUERY_JOB_CPU_RATE_RANGE) == 4,
               "CPU-rate range layout changed");
_Static_assert(_Alignof(GXOS_QUERY_JOB_CPU_RATE_RANGE) == 2,
               "CPU-rate range alignment changed");
_Static_assert(offsetof(GXOS_QUERY_JOB_CPU_RATE_RANGE, min_rate) == 0,
               "CPU-rate MinRate offset changed");
_Static_assert(offsetof(GXOS_QUERY_JOB_CPU_RATE_RANGE, max_rate) == 2,
               "CPU-rate MaxRate offset changed");
_Static_assert(sizeof(GXOS_QUERY_JOB_CPU_RATE_UNION) == 4,
               "CPU-rate union size changed");
_Static_assert(_Alignof(GXOS_QUERY_JOB_CPU_RATE_UNION) == 4,
               "CPU-rate union alignment changed");
_Static_assert(offsetof(GXOS_QUERY_JOB_CPU_RATE_UNION, cpu_rate) == 0,
               "CPU-rate CpuRate union offset changed");
_Static_assert(offsetof(GXOS_QUERY_JOB_CPU_RATE_UNION, weight) == 0,
               "CPU-rate Weight union offset changed");
_Static_assert(offsetof(GXOS_QUERY_JOB_CPU_RATE_UNION, rate_range) == 0,
               "CPU-rate range union offset changed");
_Static_assert(sizeof(GXOS_QUERY_JOB_CPU_RATE_INFORMATION) == 8,
               "Microsoft x64 CPU-rate structure size changed");
_Static_assert(_Alignof(GXOS_QUERY_JOB_CPU_RATE_INFORMATION) == 4,
               "Microsoft x64 CPU-rate structure alignment changed");
_Static_assert(offsetof(GXOS_QUERY_JOB_CPU_RATE_INFORMATION, control_flags) == 0,
               "CPU-rate ControlFlags offset changed");
_Static_assert(offsetof(GXOS_QUERY_JOB_CPU_RATE_INFORMATION, rate) == 4,
               "CPU-rate union offset changed");
_Static_assert(offsetof(GXOS_QUERY_JOB_CPU_RATE_INFORMATION, rate) +
                   offsetof(GXOS_QUERY_JOB_CPU_RATE_UNION, cpu_rate) == 4,
               "CPU-rate CpuRate offset changed");
_Static_assert(offsetof(GXOS_QUERY_JOB_CPU_RATE_INFORMATION, rate) +
                   offsetof(GXOS_QUERY_JOB_CPU_RATE_UNION, weight) == 4,
               "CPU-rate Weight offset changed");
_Static_assert(offsetof(GXOS_QUERY_JOB_CPU_RATE_INFORMATION, rate) +
                   offsetof(GXOS_QUERY_JOB_CPU_RATE_UNION, rate_range) +
                   offsetof(GXOS_QUERY_JOB_CPU_RATE_RANGE, min_rate) == 4,
               "CPU-rate MinRate offset changed");
_Static_assert(offsetof(GXOS_QUERY_JOB_CPU_RATE_INFORMATION, rate) +
                   offsetof(GXOS_QUERY_JOB_CPU_RATE_UNION, rate_range) +
                   offsetof(GXOS_QUERY_JOB_CPU_RATE_RANGE, max_rate) == 6,
               "CPU-rate MaxRate offset changed");

#ifdef GXOS_QUERY_JOB_WRONG_LAYOUT
_Static_assert(sizeof(GXOS_QUERY_JOB_CPU_RATE_INFORMATION) == 9,
               "intentional wrong-layout negative control");
#endif

GXOS_QUERY_JOB_STATUS GXOS_QUERY_JOB_MS_ABI
gxos_query_information_job_object_checked(
    GXOS_QUERY_JOB_HANDLE job_handle,
    GXOS_QUERY_JOB_INFO_CLASS information_class,
    GXOS_QUERY_JOB_OUTPUT output,
    GXOS_QUERY_JOB_DWORD output_length,
    GXOS_QUERY_JOB_RETURN_LENGTH return_length,
    const GXOS_QUERY_JOB_FACTS *facts,
    const GXOS_SYSTEM_INFO_MEMORY_CONTEXT *memory,
    GXOS_QUERY_JOB_REPORT *report);

void gxos_query_information_job_object_configure_probe(
    const GXOS_QUERY_JOB_FACTS *facts,
    const GXOS_SYSTEM_INFO_MEMORY_CONTEXT *memory);

GXOS_QUERY_JOB_BOOL GXOS_QUERY_JOB_MS_ABI
gxos_query_information_job_object_abi_probe(
    GXOS_QUERY_JOB_HANDLE job_handle,
    GXOS_QUERY_JOB_INFO_CLASS information_class,
    GXOS_QUERY_JOB_OUTPUT output,
    GXOS_QUERY_JOB_DWORD output_length,
    GXOS_QUERY_JOB_RETURN_LENGTH return_length);

#endif

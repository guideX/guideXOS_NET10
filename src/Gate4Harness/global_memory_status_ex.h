#ifndef GXOS_GLOBAL_MEMORY_STATUS_EX_H
#define GXOS_GLOBAL_MEMORY_STATUS_EX_H

#include <stddef.h>
#include <stdint.h>

#include "memory_accounting.h"

#if defined(__x86_64__)
#define GXOS_MEMORY_STATUS_EX_MS_ABI __attribute__((ms_abi))
#else
#define GXOS_MEMORY_STATUS_EX_MS_ABI
#endif

#define GXOS_MEMORY_STATUS_EX_SIZE ((uint64_t)0x40U)
#define GXOS_MEMORY_STATUS_EX_MAX_MEMORY_REGIONS 40U
#define GXOS_MEMORY_STATUS_EX_MAX_QUERY_RETRIES 4U
#define GXOS_MEMORY_STATUS_EX_ERROR_INVALID_PARAMETER 87U

/* Exact x64-compatible MEMORYSTATUSEX layout. */
typedef struct GXOS_MEMORY_STATUS_EX {
    uint32_t dwLength;
    uint32_t dwMemoryLoad;
    uint64_t ullTotalPhys;
    uint64_t ullAvailPhys;
    uint64_t ullTotalPageFile;
    uint64_t ullAvailPageFile;
    uint64_t ullTotalVirtual;
    uint64_t ullAvailVirtual;
    uint64_t ullAvailExtendedVirtual;
} GXOS_MEMORY_STATUS_EX;

_Static_assert(sizeof(GXOS_MEMORY_STATUS_EX) == 0x40,
               "MEMORYSTATUSEX ABI size changed");
_Static_assert(offsetof(GXOS_MEMORY_STATUS_EX, dwLength) == 0x00,
               "MEMORYSTATUSEX dwLength offset changed");
_Static_assert(offsetof(GXOS_MEMORY_STATUS_EX, dwMemoryLoad) == 0x04,
               "MEMORYSTATUSEX dwMemoryLoad offset changed");
_Static_assert(offsetof(GXOS_MEMORY_STATUS_EX, ullTotalPhys) == 0x08,
               "MEMORYSTATUSEX ullTotalPhys offset changed");
_Static_assert(offsetof(GXOS_MEMORY_STATUS_EX, ullAvailPhys) == 0x10,
               "MEMORYSTATUSEX ullAvailPhys offset changed");
_Static_assert(offsetof(GXOS_MEMORY_STATUS_EX, ullTotalPageFile) == 0x18,
               "MEMORYSTATUSEX ullTotalPageFile offset changed");
_Static_assert(offsetof(GXOS_MEMORY_STATUS_EX, ullAvailPageFile) == 0x20,
               "MEMORYSTATUSEX ullAvailPageFile offset changed");
_Static_assert(offsetof(GXOS_MEMORY_STATUS_EX, ullTotalVirtual) == 0x28,
               "MEMORYSTATUSEX ullTotalVirtual offset changed");
_Static_assert(offsetof(GXOS_MEMORY_STATUS_EX, ullAvailVirtual) == 0x30,
               "MEMORYSTATUSEX ullAvailVirtual offset changed");
_Static_assert(offsetof(GXOS_MEMORY_STATUS_EX, ullAvailExtendedVirtual) == 0x38,
               "MEMORYSTATUSEX ullAvailExtendedVirtual offset changed");

typedef struct GXOS_MEMORY_STATUS_EX_MEMORY_REGION {
    uintptr_t base;
    uintptr_t end;
    uint32_t readable;
    uint32_t writable;
} GXOS_MEMORY_STATUS_EX_MEMORY_REGION;

typedef struct GXOS_MEMORY_STATUS_EX_CONTEXT {
    const GXOS_MEMORY_CLASSIFICATION *classification;
    const GXOS_MEMORY_SNAPSHOT *startup_snapshot;
    const GXOS_PHYSICAL_LEDGER *ledger;
    const GXOS_VM_ARENA *virtual_arena;
    const GXOS_MEMORY_STATUS_EX_MEMORY_REGION *regions;
    uint32_t region_count;
    uint64_t accounting_generation;
    const volatile uint64_t *accounting_generation_source;
} GXOS_MEMORY_STATUS_EX_CONTEXT;

typedef enum GXOS_MEMORY_STATUS_EX_STATUS {
    GXOS_MEMORY_STATUS_EX_STATUS_OK = 0,
    GXOS_MEMORY_STATUS_EX_STATUS_NULL_BUFFER,
    GXOS_MEMORY_STATUS_EX_STATUS_NONCANONICAL_BUFFER,
    GXOS_MEMORY_STATUS_EX_STATUS_RANGE_OVERFLOW,
    GXOS_MEMORY_STATUS_EX_STATUS_UNWRITABLE_BUFFER,
    GXOS_MEMORY_STATUS_EX_STATUS_INVALID_CONTEXT,
    GXOS_MEMORY_STATUS_EX_STATUS_INVALID_LENGTH,
    GXOS_MEMORY_STATUS_EX_STATUS_INVALID_ACCOUNTING_VIEW,
    GXOS_MEMORY_STATUS_EX_STATUS_INVALID_PHYSICAL,
    GXOS_MEMORY_STATUS_EX_STATUS_INVALID_COMMIT,
    GXOS_MEMORY_STATUS_EX_STATUS_INVALID_VIRTUAL,
    GXOS_MEMORY_STATUS_EX_STATUS_INVALID_MEMORY_LOAD,
    GXOS_MEMORY_STATUS_EX_STATUS_ACCOUNTING_CHANGED,
    GXOS_MEMORY_STATUS_EX_STATUS_FINAL_RANGE_INVALID
} GXOS_MEMORY_STATUS_EX_STATUS;

typedef struct GXOS_MEMORY_STATUS_EX_REPORT {
    GXOS_MEMORY_STATUS_EX_STATUS status;
    uintptr_t buffer;
    uint64_t writable_range_bytes;
    uint32_t buffer_canonical;
    uint32_t input_range_valid;
    uint32_t input_length_read;
    uint32_t output_range_valid;
    uint32_t output_written;
    uint32_t return_value;
    uint32_t reserved;
    uint64_t accounting_generation;
    GXOS_MEMORY_SNAPSHOT view;
} GXOS_MEMORY_STATUS_EX_REPORT;

int GXOS_MEMORY_STATUS_EX_MS_ABI gxos_global_memory_status_ex_checked(
    GXOS_MEMORY_STATUS_EX *buffer,
    const GXOS_MEMORY_STATUS_EX_CONTEXT *context,
    GXOS_MEMORY_STATUS_EX_REPORT *report);

#endif

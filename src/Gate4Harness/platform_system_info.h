#ifndef GXOS_PLATFORM_SYSTEM_INFO_H
#define GXOS_PLATFORM_SYSTEM_INFO_H

#include <stddef.h>
#include <stdint.h>

#if defined(__x86_64__)
#define GXOS_SYSTEM_INFO_MS_ABI __attribute__((ms_abi))
#else
#define GXOS_SYSTEM_INFO_MS_ABI
#endif

#define GXOS_SYSTEM_INFO_PROCESSOR_ARCHITECTURE_AMD64 ((uint16_t)9U)
#define GXOS_SYSTEM_INFO_PROCESSOR_TYPE_AMD_X8664 ((uint32_t)8664U)
#define GXOS_SYSTEM_INFO_ADDRESS_RANGE_IMAGE_BACKED ((uint32_t)1U)
#define GXOS_SYSTEM_INFO_MAX_MEMORY_REGIONS 40U

typedef union GXOS_SYSTEM_INFO_ARCHITECTURE_UNION {
    uint32_t dwOemId;
    struct {
        uint16_t wProcessorArchitecture;
        uint16_t wReserved;
    } architecture;
} GXOS_SYSTEM_INFO_ARCHITECTURE_UNION;

typedef struct GXOS_SYSTEM_INFO {
    GXOS_SYSTEM_INFO_ARCHITECTURE_UNION architecture_union;
    uint32_t dwPageSize;
    void *lpMinimumApplicationAddress;
    void *lpMaximumApplicationAddress;
    uintptr_t dwActiveProcessorMask;
    uint32_t dwNumberOfProcessors;
    uint32_t dwProcessorType;
    uint32_t dwAllocationGranularity;
    uint16_t wProcessorLevel;
    uint16_t wProcessorRevision;
} GXOS_SYSTEM_INFO;

typedef struct GXOS_SYSTEM_FACTS {
    uint16_t processor_architecture;
    uint32_t page_size;
    uintptr_t minimum_application_address;
    uintptr_t maximum_application_address;
    uintptr_t active_processor_mask;
    uint32_t number_of_processors;
    uint32_t processor_type;
    uint32_t allocation_granularity;
    uint16_t processor_level;
    uint16_t processor_revision;
    uint32_t address_range_policy;
} GXOS_SYSTEM_FACTS;

typedef struct GXOS_SYSTEM_INFO_MEMORY_REGION {
    uintptr_t base;
    uintptr_t end;
    uint32_t readable;
    uint32_t writable;
} GXOS_SYSTEM_INFO_MEMORY_REGION;

typedef struct GXOS_SYSTEM_INFO_MEMORY_CONTEXT {
    uint32_t region_count;
    const GXOS_SYSTEM_INFO_MEMORY_REGION *regions;
} GXOS_SYSTEM_INFO_MEMORY_CONTEXT;

typedef enum GXOS_SYSTEM_INFO_STATUS {
    GXOS_SYSTEM_INFO_STATUS_OK = 0,
    GXOS_SYSTEM_INFO_STATUS_NULL_POINTER,
    GXOS_SYSTEM_INFO_STATUS_NONCANONICAL_POINTER,
    GXOS_SYSTEM_INFO_STATUS_UNWRITABLE_POINTER,
    GXOS_SYSTEM_INFO_STATUS_INSUFFICIENT_WRITABLE_RANGE,
    GXOS_SYSTEM_INFO_STATUS_INVALID_ARCHITECTURE,
    GXOS_SYSTEM_INFO_STATUS_INVALID_PAGE_SIZE,
    GXOS_SYSTEM_INFO_STATUS_INVALID_ALLOCATION_GRANULARITY,
    GXOS_SYSTEM_INFO_STATUS_INVALID_PROCESSOR_COUNT,
    GXOS_SYSTEM_INFO_STATUS_INVALID_PROCESSOR_MASK,
    GXOS_SYSTEM_INFO_STATUS_INVALID_ADDRESS_RANGE,
    GXOS_SYSTEM_INFO_STATUS_LAYOUT_MISMATCH,
    GXOS_SYSTEM_INFO_STATUS_INVALID_MEMORY_CONTEXT
} GXOS_SYSTEM_INFO_STATUS;

_Static_assert(sizeof(uintptr_t) == 8, "GetSystemInfo requires x64 pointers");
_Static_assert(sizeof(void *) == 8, "SYSTEM_INFO pointers must be x64");
_Static_assert(sizeof(GXOS_SYSTEM_INFO_ARCHITECTURE_UNION) == 4,
               "SYSTEM_INFO architecture union size changed");
_Static_assert(offsetof(GXOS_SYSTEM_INFO_ARCHITECTURE_UNION, dwOemId) == 0,
               "SYSTEM_INFO dwOemId offset changed");
_Static_assert(offsetof(GXOS_SYSTEM_INFO_ARCHITECTURE_UNION, architecture.wProcessorArchitecture) == 0,
               "SYSTEM_INFO architecture offset changed");
_Static_assert(offsetof(GXOS_SYSTEM_INFO_ARCHITECTURE_UNION, architecture.wReserved) == 2,
               "SYSTEM_INFO reserved architecture offset changed");
_Static_assert(sizeof(GXOS_SYSTEM_INFO) == 0x30,
               "Microsoft x64 SYSTEM_INFO size changed");
_Static_assert(_Alignof(GXOS_SYSTEM_INFO) == 8,
               "Microsoft x64 SYSTEM_INFO alignment changed");
_Static_assert(offsetof(GXOS_SYSTEM_INFO, architecture_union) == 0,
               "SYSTEM_INFO union offset changed");
_Static_assert(offsetof(GXOS_SYSTEM_INFO, dwPageSize) == 4,
               "SYSTEM_INFO dwPageSize offset changed");
_Static_assert(offsetof(GXOS_SYSTEM_INFO, lpMinimumApplicationAddress) == 8,
               "SYSTEM_INFO minimum address offset changed");
_Static_assert(offsetof(GXOS_SYSTEM_INFO, lpMaximumApplicationAddress) == 16,
               "SYSTEM_INFO maximum address offset changed");
_Static_assert(offsetof(GXOS_SYSTEM_INFO, dwActiveProcessorMask) == 24,
               "SYSTEM_INFO active mask offset changed");
_Static_assert(offsetof(GXOS_SYSTEM_INFO, dwNumberOfProcessors) == 32,
               "SYSTEM_INFO processor count offset changed");
_Static_assert(offsetof(GXOS_SYSTEM_INFO, dwProcessorType) == 36,
               "SYSTEM_INFO processor type offset changed");
_Static_assert(offsetof(GXOS_SYSTEM_INFO, dwAllocationGranularity) == 40,
               "SYSTEM_INFO allocation granularity offset changed");
_Static_assert(offsetof(GXOS_SYSTEM_INFO, wProcessorLevel) == 44,
               "SYSTEM_INFO processor level offset changed");
_Static_assert(offsetof(GXOS_SYSTEM_INFO, wProcessorRevision) == 46,
               "SYSTEM_INFO processor revision offset changed");

#ifdef GXOS_SYSTEM_INFO_TEST_WRONG_LAYOUT
_Static_assert(sizeof(GXOS_SYSTEM_INFO) == 0x31,
               "intentional wrong-layout negative control");
#endif

GXOS_SYSTEM_INFO_STATUS GXOS_SYSTEM_INFO_MS_ABI gxos_get_system_info_checked(
    GXOS_SYSTEM_INFO *destination,
    const GXOS_SYSTEM_FACTS *facts,
    const GXOS_SYSTEM_INFO_MEMORY_CONTEXT *memory);

GXOS_SYSTEM_INFO_STATUS GXOS_SYSTEM_INFO_MS_ABI gxos_system_info_configure(
    const GXOS_SYSTEM_FACTS *facts,
    const GXOS_SYSTEM_INFO_MEMORY_CONTEXT *memory);

GXOS_SYSTEM_INFO_STATUS GXOS_SYSTEM_INFO_MS_ABI gxos_system_info_get_snapshot(
    GXOS_SYSTEM_FACTS *facts_out);

#endif

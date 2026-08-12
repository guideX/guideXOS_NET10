#ifndef GXOS_PLATFORM_PROCESSOR_TOPOLOGY_H
#define GXOS_PLATFORM_PROCESSOR_TOPOLOGY_H

#include <stddef.h>
#include <stdint.h>

#include "global_memory_status_ex.h"

#if defined(__x86_64__)
#define GXOS_PROCESSOR_TOPOLOGY_MS_ABI __attribute__((ms_abi))
#else
#define GXOS_PROCESSOR_TOPOLOGY_MS_ABI
#endif

#define GXOS_PROCESSOR_TOPOLOGY_MAX_LOGICAL_PROCESSORS 64U
#define GXOS_PROCESSOR_TOPOLOGY_MAX_RELATIONSHIPS 64U
#define GXOS_PROCESSOR_TOPOLOGY_MAX_RECORDS 256U

#define GXOS_RELATION_PROCESSOR_CORE ((uint32_t)0U)
#define GXOS_RELATION_NUMA_NODE ((uint32_t)1U)
#define GXOS_RELATION_CACHE ((uint32_t)2U)
#define GXOS_RELATION_PROCESSOR_PACKAGE ((uint32_t)3U)

#define GXOS_PROCESSOR_TOPOLOGY_ERROR_INVALID_PARAMETER ((uint32_t)87U)
#define GXOS_PROCESSOR_TOPOLOGY_ERROR_INSUFFICIENT_BUFFER ((uint32_t)122U)

typedef struct GXOS_PROCESSOR_TOPOLOGY_CORE_RELATIONSHIP {
    uint64_t processor_mask;
    uint8_t flags;
} GXOS_PROCESSOR_TOPOLOGY_CORE_RELATIONSHIP;

typedef struct GXOS_PROCESSOR_TOPOLOGY_NUMA_RELATIONSHIP {
    uint64_t processor_mask;
    uint32_t node_number;
} GXOS_PROCESSOR_TOPOLOGY_NUMA_RELATIONSHIP;

typedef struct GXOS_PROCESSOR_TOPOLOGY_PACKAGE_RELATIONSHIP {
    uint64_t processor_mask;
} GXOS_PROCESSOR_TOPOLOGY_PACKAGE_RELATIONSHIP;

typedef struct GXOS_PROCESSOR_TOPOLOGY_CACHE_RELATIONSHIP {
    uint64_t processor_mask;
    uint8_t level;
    uint8_t associativity;
    uint16_t line_size;
    uint32_t size;
    uint32_t type;
} GXOS_PROCESSOR_TOPOLOGY_CACHE_RELATIONSHIP;

typedef struct GXOS_PROCESSOR_TOPOLOGY_SNAPSHOT {
    uint32_t valid;
    uint64_t generation;
    uint32_t logical_processor_count;
    uint8_t logical_processor_numbers[GXOS_PROCESSOR_TOPOLOGY_MAX_LOGICAL_PROCESSORS];
    uint64_t active_processor_mask;
    uint32_t core_count;
    GXOS_PROCESSOR_TOPOLOGY_CORE_RELATIONSHIP
        cores[GXOS_PROCESSOR_TOPOLOGY_MAX_RELATIONSHIPS];
    uint32_t numa_node_count;
    GXOS_PROCESSOR_TOPOLOGY_NUMA_RELATIONSHIP
        numa_nodes[GXOS_PROCESSOR_TOPOLOGY_MAX_RELATIONSHIPS];
    uint32_t package_count;
    GXOS_PROCESSOR_TOPOLOGY_PACKAGE_RELATIONSHIP
        packages[GXOS_PROCESSOR_TOPOLOGY_MAX_RELATIONSHIPS];
    uint32_t cache_count;
    GXOS_PROCESSOR_TOPOLOGY_CACHE_RELATIONSHIP
        caches[GXOS_PROCESSOR_TOPOLOGY_MAX_RELATIONSHIPS];
} GXOS_PROCESSOR_TOPOLOGY_SNAPSHOT;

/* Exact x64-compatible SYSTEM_LOGICAL_PROCESSOR_INFORMATION record. */
typedef union GXOS_LOGICAL_PROCESSOR_INFORMATION_RELATIONSHIP {
    struct {
        uint8_t flags;
        uint8_t reserved[15];
    } processor_core;
    struct {
        uint32_t node_number;
        uint8_t reserved[12];
    } numa_node;
    struct {
        uint8_t level;
        uint8_t associativity;
        uint16_t line_size;
        uint32_t size;
        uint32_t type;
        uint8_t reserved[4];
    } cache;
    uint8_t reserved[16];
} GXOS_LOGICAL_PROCESSOR_INFORMATION_RELATIONSHIP;

typedef struct GXOS_LOGICAL_PROCESSOR_INFORMATION {
    uint64_t processor_mask;
    uint32_t relationship;
    uint32_t reserved;
    GXOS_LOGICAL_PROCESSOR_INFORMATION_RELATIONSHIP relationship_info;
} GXOS_LOGICAL_PROCESSOR_INFORMATION;

_Static_assert(sizeof(GXOS_LOGICAL_PROCESSOR_INFORMATION) == 0x20,
               "SYSTEM_LOGICAL_PROCESSOR_INFORMATION size changed");
_Static_assert(offsetof(GXOS_LOGICAL_PROCESSOR_INFORMATION, processor_mask) == 0x00,
               "SYSTEM_LOGICAL_PROCESSOR_INFORMATION mask offset changed");
_Static_assert(offsetof(GXOS_LOGICAL_PROCESSOR_INFORMATION, relationship) == 0x08,
               "SYSTEM_LOGICAL_PROCESSOR_INFORMATION relationship offset changed");
_Static_assert(offsetof(GXOS_LOGICAL_PROCESSOR_INFORMATION, reserved) == 0x0C,
               "SYSTEM_LOGICAL_PROCESSOR_INFORMATION padding offset changed");
_Static_assert(offsetof(GXOS_LOGICAL_PROCESSOR_INFORMATION, relationship_info) == 0x10,
               "SYSTEM_LOGICAL_PROCESSOR_INFORMATION union offset changed");
_Static_assert(offsetof(GXOS_LOGICAL_PROCESSOR_INFORMATION,
                        relationship_info.processor_core.flags) == 0x10,
               "SYSTEM_LOGICAL_PROCESSOR_INFORMATION core flags offset changed");
_Static_assert(offsetof(GXOS_LOGICAL_PROCESSOR_INFORMATION,
                        relationship_info.numa_node.node_number) == 0x10,
               "SYSTEM_LOGICAL_PROCESSOR_INFORMATION node offset changed");
_Static_assert(offsetof(GXOS_LOGICAL_PROCESSOR_INFORMATION,
                        relationship_info.cache.level) == 0x10,
               "SYSTEM_LOGICAL_PROCESSOR_INFORMATION cache level offset changed");
_Static_assert(offsetof(GXOS_LOGICAL_PROCESSOR_INFORMATION,
                        relationship_info.cache.associativity) == 0x11,
               "SYSTEM_LOGICAL_PROCESSOR_INFORMATION cache associativity offset changed");
_Static_assert(offsetof(GXOS_LOGICAL_PROCESSOR_INFORMATION,
                        relationship_info.cache.line_size) == 0x12,
               "SYSTEM_LOGICAL_PROCESSOR_INFORMATION cache line-size offset changed");
_Static_assert(offsetof(GXOS_LOGICAL_PROCESSOR_INFORMATION,
                        relationship_info.cache.size) == 0x14,
               "SYSTEM_LOGICAL_PROCESSOR_INFORMATION cache size offset changed");
_Static_assert(offsetof(GXOS_LOGICAL_PROCESSOR_INFORMATION,
                        relationship_info.cache.type) == 0x18,
               "SYSTEM_LOGICAL_PROCESSOR_INFORMATION cache type offset changed");

typedef enum GXOS_PROCESSOR_TOPOLOGY_STATUS {
    GXOS_PROCESSOR_TOPOLOGY_STATUS_OK = 0,
    GXOS_PROCESSOR_TOPOLOGY_STATUS_INSUFFICIENT_BUFFER,
    GXOS_PROCESSOR_TOPOLOGY_STATUS_INVALID_SNAPSHOT,
    GXOS_PROCESSOR_TOPOLOGY_STATUS_INVALID_GENERATION,
    GXOS_PROCESSOR_TOPOLOGY_STATUS_INVALID_LOGICAL_PROCESSOR_COUNT,
    GXOS_PROCESSOR_TOPOLOGY_STATUS_INVALID_ACTIVE_PROCESSOR_MASK,
    GXOS_PROCESSOR_TOPOLOGY_STATUS_DUPLICATE_LOGICAL_PROCESSOR,
    GXOS_PROCESSOR_TOPOLOGY_STATUS_OUT_OF_RANGE_LOGICAL_PROCESSOR,
    GXOS_PROCESSOR_TOPOLOGY_STATUS_INVALID_CORE_RELATIONSHIPS,
    GXOS_PROCESSOR_TOPOLOGY_STATUS_INVALID_NUMA_RELATIONSHIPS,
    GXOS_PROCESSOR_TOPOLOGY_STATUS_INVALID_PACKAGE_RELATIONSHIPS,
    GXOS_PROCESSOR_TOPOLOGY_STATUS_INVALID_CACHE_RELATIONSHIPS,
    GXOS_PROCESSOR_TOPOLOGY_STATUS_RELATIONSHIP_CAPACITY,
    GXOS_PROCESSOR_TOPOLOGY_STATUS_RECORD_COUNT_OVERFLOW,
    GXOS_PROCESSOR_TOPOLOGY_STATUS_SIZE_OVERFLOW,
    GXOS_PROCESSOR_TOPOLOGY_STATUS_NULL_RETURNED_LENGTH,
    GXOS_PROCESSOR_TOPOLOGY_STATUS_NONCANONICAL_RETURNED_LENGTH,
    GXOS_PROCESSOR_TOPOLOGY_STATUS_RETURNED_LENGTH_RANGE_OVERFLOW,
    GXOS_PROCESSOR_TOPOLOGY_STATUS_UNREADABLE_RETURNED_LENGTH,
    GXOS_PROCESSOR_TOPOLOGY_STATUS_UNWRITABLE_RETURNED_LENGTH,
    GXOS_PROCESSOR_TOPOLOGY_STATUS_NULL_BUFFER,
    GXOS_PROCESSOR_TOPOLOGY_STATUS_NONCANONICAL_BUFFER,
    GXOS_PROCESSOR_TOPOLOGY_STATUS_BUFFER_RANGE_OVERFLOW,
    GXOS_PROCESSOR_TOPOLOGY_STATUS_UNWRITABLE_BUFFER,
    GXOS_PROCESSOR_TOPOLOGY_STATUS_INVALID_MEMORY_CONTEXT,
    GXOS_PROCESSOR_TOPOLOGY_STATUS_INVALID_RECORD_STORAGE
} GXOS_PROCESSOR_TOPOLOGY_STATUS;

typedef struct GXOS_PROCESSOR_TOPOLOGY_REPORT {
    GXOS_PROCESSOR_TOPOLOGY_STATUS status;
    uintptr_t buffer;
    uintptr_t returned_length;
    uint32_t returned_length_pointer_canonical;
    uint32_t returned_length_pointer_readable;
    uint32_t returned_length_pointer_writable;
    uint32_t input_length_read;
    uint32_t input_length;
    uint32_t buffer_pointer_canonical;
    uint32_t buffer_range_valid;
    uint32_t output_written;
    uint32_t return_value;
    uint32_t required_length;
    uint32_t record_count;
    uint32_t cache_record_count;
    uint64_t snapshot_generation;
} GXOS_PROCESSOR_TOPOLOGY_REPORT;

GXOS_PROCESSOR_TOPOLOGY_STATUS GXOS_PROCESSOR_TOPOLOGY_MS_ABI
gxos_processor_topology_make_single_cpu(
    GXOS_PROCESSOR_TOPOLOGY_SNAPSHOT *snapshot, uint64_t generation);

GXOS_PROCESSOR_TOPOLOGY_STATUS GXOS_PROCESSOR_TOPOLOGY_MS_ABI
gxos_processor_topology_validate(
    const GXOS_PROCESSOR_TOPOLOGY_SNAPSHOT *snapshot);

GXOS_PROCESSOR_TOPOLOGY_STATUS GXOS_PROCESSOR_TOPOLOGY_MS_ABI
gxos_processor_topology_record_count(
    const GXOS_PROCESSOR_TOPOLOGY_SNAPSHOT *snapshot,
    uint32_t *record_count);

int GXOS_PROCESSOR_TOPOLOGY_MS_ABI gxos_processor_topology_required_size(
    uint64_t record_count, size_t *required_size);

GXOS_PROCESSOR_TOPOLOGY_STATUS GXOS_PROCESSOR_TOPOLOGY_MS_ABI
gxos_processor_topology_build_records(
    const GXOS_PROCESSOR_TOPOLOGY_SNAPSHOT *snapshot,
    GXOS_LOGICAL_PROCESSOR_INFORMATION *records,
    uint32_t record_capacity,
    uint32_t *record_count);

GXOS_PROCESSOR_TOPOLOGY_STATUS GXOS_PROCESSOR_TOPOLOGY_MS_ABI
gxos_get_logical_processor_information_checked(
    GXOS_LOGICAL_PROCESSOR_INFORMATION *buffer,
    uint32_t *returned_length,
    const GXOS_PROCESSOR_TOPOLOGY_SNAPSHOT *snapshot,
    const GXOS_MEMORY_STATUS_EX_CONTEXT *memory,
    GXOS_PROCESSOR_TOPOLOGY_REPORT *report);

GXOS_PROCESSOR_TOPOLOGY_STATUS GXOS_PROCESSOR_TOPOLOGY_MS_ABI
gxos_processor_topology_status_last_error(
    GXOS_PROCESSOR_TOPOLOGY_STATUS status, uint32_t *last_error);

#endif

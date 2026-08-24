#ifndef GXOS_MANAGED_KERNEL_MMIO_H
#define GXOS_MANAGED_KERNEL_MMIO_H

#include <stdint.h>

#include "managed_kernel_abi.h"
#include "memory_accounting.h"
#include "vm_substrate.h"

#define GXOS_MMIO_MAPPING_CAPACITY 8U
#define GXOS_MMIO_WINDOW_BASE 0x0000400040000000ULL
#define GXOS_MMIO_WINDOW_LENGTH 0x0000000010000000ULL
#define GXOS_MMIO_MAX_MAPPING_PAGES 64U
#define GXOS_MMIO_WINDOW_OWNER 0x4D4D494FU
#define GXOS_VM_RESERVATION_KIND_MMIO 0x4D4D494FU

#define GXOS_MMIO_CACHE_IA32_PAT 0x277U
#define GXOS_MMIO_CACHE_IA32_MTRR_DEF_TYPE 0x2FFU
#define GXOS_MMIO_CACHE_PTE_FLAGS \
    (GXOS_X64_PAGING_ENTRY_WRITE_THROUGH | GXOS_X64_PAGING_ENTRY_CACHE_DISABLE)

typedef enum {
    GXOS_MMIO_CACHE_STATUS_OK = 0,
    GXOS_MMIO_CACHE_STATUS_INVALID_ARGUMENT,
    GXOS_MMIO_CACHE_STATUS_UNSUPPORTED,
    GXOS_MMIO_CACHE_STATUS_UNPROVEN
} GXOS_MMIO_CACHE_STATUS;

typedef struct {
    uint32_t pat_supported;
    uint32_t safe_uncacheable;
    uint32_t mtrr_enabled;
    uint32_t fixed_mtrr_enabled;
    uint64_t pat_msr;
    uint64_t mtrr_default_type;
    uint64_t pte_flags;
} GXOS_MMIO_CACHE_POLICY;

GXOS_MMIO_CACHE_STATUS gxos_mmio_cache_policy_validate(
    uint32_t pat_supported,
    uint64_t pat_msr,
    uint64_t mtrr_default_type,
    GXOS_MMIO_CACHE_POLICY *policy_out);
GXOS_MMIO_CACHE_STATUS gxos_mmio_cache_policy_probe(
    GXOS_MMIO_CACHE_POLICY *policy_out);

typedef enum {
    GXOS_MMIO_SERVICE_OK = 0,
    GXOS_MMIO_SERVICE_INVALID_ARGUMENT = 1,
    GXOS_MMIO_SERVICE_UNSUPPORTED = 2,
    GXOS_MMIO_SERVICE_NOT_INITIALIZED = 4,
    GXOS_MMIO_SERVICE_RESOURCE_EXHAUSTED = 8,
    GXOS_MMIO_SERVICE_NOT_FOUND = 9,
    GXOS_MMIO_SERVICE_OWNERSHIP_MISMATCH = 10,
    GXOS_MMIO_SERVICE_INVALID_STATE = 7,
    GXOS_MMIO_SERVICE_OVERFLOW = 13
} GXOS_MMIO_SERVICE_STATUS;

typedef struct {
    uint32_t live;
    uint32_t reserved;
    uint64_t resource_id;
    uint32_t owner_driver_id;
    uint32_t mapping_count;
    uint64_t generation;
    uint64_t claim_handle;
} GXOS_MMIO_CLAIM_RECORD;

typedef struct {
    uint32_t live;
    uint32_t reserved;
    uint64_t resource_id;
    uint64_t claim_handle;
    uint32_t owner_driver_id;
    uint32_t page_count;
    uint64_t virtual_base;
    uint64_t physical_base;
    uint64_t requested_offset;
    uint64_t requested_length;
    uint64_t mapped_length;
    uint32_t access;
    uint32_t reserved0;
    uint64_t generation;
} GXOS_MMIO_MAPPING_RECORD;

typedef struct {
    uint32_t Size;
    uint32_t AbiVersion;
    uint64_t Handle;
    uint64_t Reserved;
} GXOS_MMIO_CLAIM_RESULT_V1;

typedef struct {
    uint32_t Size;
    uint32_t AbiVersion;
    uint64_t Handle;
    uint64_t ResourceId;
    uint64_t Offset;
    uint64_t Length;
    uint32_t Access;
    uint32_t Reserved0;
} GXOS_MMIO_MAPPING_RESULT_V1;

typedef struct {
    uint32_t Size;
    uint32_t AbiVersion;
    uint32_t Width;
    uint32_t Reserved0;
    uint64_t Value;
    uint64_t Reserved1;
} GXOS_MMIO_READ_RESULT_V1;

typedef struct {
    GXOS_VM_PAGING *paging;
    GXOS_VM_ARENA *arena;
    const GX_MANAGED_KERNEL_DEVICE_RESOURCE_V1 *resources;
    uint32_t resource_count;
    uint64_t resource_generation;
    uint64_t window_base;
    uint64_t window_length;
    GXOS_MMIO_CACHE_POLICY cache_policy;
    GXOS_MMIO_CLAIM_RECORD claims[GX_MANAGED_KERNEL_DEVICE_RESOURCE_MAX_CLAIMS];
    GXOS_MMIO_MAPPING_RECORD mappings[GXOS_MMIO_MAPPING_CAPACITY];
    uint32_t initialized;
    uint32_t reservation_slot;
    uint64_t next_claim_generation;
    uint64_t next_mapping_generation;
} GXOS_MMIO_SERVICE;

GXOS_MMIO_SERVICE_STATUS gxos_mmio_service_init(
    GXOS_MMIO_SERVICE *service,
    GXOS_VM_PAGING *paging,
    GXOS_VM_ARENA *arena,
    const GX_MANAGED_KERNEL_DEVICE_RESOURCE_V1 *resources,
    uint32_t resource_count,
    uint64_t resource_generation,
    const GXOS_UEFI_MEMORY_MAP *memory_map,
    const GXOS_MMIO_CACHE_POLICY *cache_policy);
GXOS_MMIO_SERVICE_STATUS gxos_mmio_service_teardown(
    GXOS_MMIO_SERVICE *service);

GXOS_MMIO_SERVICE_STATUS gxos_mmio_claim(
    GXOS_MMIO_SERVICE *service,
    uint64_t resource_id,
    uint32_t driver_id,
    uint32_t expected_owner_kind,
    uint32_t expected_owner_id,
    uint64_t *claim_handle_out);
GXOS_MMIO_SERVICE_STATUS gxos_mmio_release(
    GXOS_MMIO_SERVICE *service,
    uint64_t claim_handle,
    uint32_t driver_id);
GXOS_MMIO_SERVICE_STATUS gxos_mmio_map(
    GXOS_MMIO_SERVICE *service,
    uint64_t claim_handle,
    uint32_t driver_id,
    uint64_t offset,
    uint64_t length,
    uint32_t access,
    uint64_t *mapping_handle_out);
GXOS_MMIO_SERVICE_STATUS gxos_mmio_unmap(
    GXOS_MMIO_SERVICE *service,
    uint64_t mapping_handle,
    uint32_t driver_id);
GXOS_MMIO_SERVICE_STATUS gxos_mmio_read(
    GXOS_MMIO_SERVICE *service,
    uint64_t mapping_handle,
    uint32_t driver_id,
    uint64_t offset,
    uint32_t width,
    uint64_t *value_out);
GXOS_MMIO_SERVICE_STATUS gxos_mmio_write(
    GXOS_MMIO_SERVICE *service,
    uint64_t mapping_handle,
    uint32_t driver_id,
    uint64_t offset,
    uint32_t width,
    uint64_t value);
int gxos_mmio_validate_claim(
    const GXOS_MMIO_SERVICE *service,
    uint64_t claim_handle,
    uint32_t driver_id,
    uint64_t *resource_id_out,
    uint32_t *owner_kind_out,
    uint32_t *owner_id_out);

/* These are the only callbacks exposed to managed code. They carry opaque
   handles and validated ranges, never a physical or virtual address. */
uint32_t gxos_mmio_claim_callback(
    uint64_t resource_id, uint32_t driver_id,
    uint32_t expected_owner_kind, uint32_t expected_owner_id,
    uintptr_t result_address, uintptr_t result_capacity);
uint32_t gxos_mmio_release_callback(uint64_t claim_handle,
                                    uint32_t driver_id);
uint32_t gxos_mmio_map_callback(
    uint64_t claim_handle, uint32_t driver_id, uint64_t offset,
    uint64_t length, uint32_t access,
    uintptr_t result_address, uintptr_t result_capacity);
uint32_t gxos_mmio_unmap_callback(uint64_t mapping_handle,
                                  uint32_t driver_id);
uint32_t gxos_mmio_read_callback(
    uint64_t mapping_handle, uint32_t driver_id, uint64_t offset,
    uint32_t width, uintptr_t result_address, uintptr_t result_capacity);
uint32_t gxos_mmio_write_callback(
    uint64_t mapping_handle, uint32_t driver_id, uint64_t offset,
    uint32_t width, uint64_t value);
void gxos_mmio_set_callback_service(GXOS_MMIO_SERVICE *service);

#endif

#ifndef GXOS_MEMORY_ACCOUNTING_H
#define GXOS_MEMORY_ACCOUNTING_H

#include <stdint.h>
#include <stddef.h>

#if defined(__x86_64__)
#define GXOS_MEMORY_EFIAPI __attribute__((ms_abi))
#else
#define GXOS_MEMORY_EFIAPI
#endif

/* EFI error values are UINT64 values with the high bit set. */
#define GXOS_EFI_SUCCESS ((uint64_t)0)
#define GXOS_EFI_BUFFER_TOO_SMALL ((uint64_t)0x8000000000000005ULL)
#define GXOS_EFI_LOADER_DATA ((uint32_t)4U)
#define GXOS_MEMORY_PAGE_SIZE ((uint64_t)4096U)

#define GXOS_MEMORY_MAP_MAX_BYTES (128U * 1024U)
#define GXOS_MEMORY_MAP_MAX_DESCRIPTORS 2048U
#define GXOS_MEMORY_MAP_GROWTH_SLACK_DESCRIPTORS 8U
#define GXOS_MEMORY_MAP_MAX_RETRIES 4U
#define GXOS_PHYSICAL_LEDGER_CAPACITY 128U
#define GXOS_VM_MAX_RESERVATIONS 128U
#define GXOS_VM_MAX_COMMITMENTS 128U

typedef uint64_t GXOS_EFI_STATUS;
typedef uint64_t GXOS_EFI_UINTN;
typedef uint64_t GXOS_EFI_PHYSICAL_ADDRESS;

/* UEFI 2.x EFI_MEMORY_DESCRIPTOR.  The Pad field is part of the ABI. */
typedef struct {
    uint32_t Type;
    uint32_t Pad;
    uint64_t PhysicalStart;
    uint64_t VirtualStart;
    uint64_t NumberOfPages;
    uint64_t Attribute;
} EFI_MEMORY_DESCRIPTOR;

_Static_assert(offsetof(EFI_MEMORY_DESCRIPTOR, Type) == 0,
               "EFI_MEMORY_DESCRIPTOR.Type offset");
_Static_assert(offsetof(EFI_MEMORY_DESCRIPTOR, Pad) == 4,
               "EFI_MEMORY_DESCRIPTOR.Pad offset");
_Static_assert(offsetof(EFI_MEMORY_DESCRIPTOR, PhysicalStart) == 8,
               "EFI_MEMORY_DESCRIPTOR.PhysicalStart offset");
_Static_assert(offsetof(EFI_MEMORY_DESCRIPTOR, VirtualStart) == 16,
               "EFI_MEMORY_DESCRIPTOR.VirtualStart offset");
_Static_assert(offsetof(EFI_MEMORY_DESCRIPTOR, NumberOfPages) == 24,
               "EFI_MEMORY_DESCRIPTOR.NumberOfPages offset");
_Static_assert(offsetof(EFI_MEMORY_DESCRIPTOR, Attribute) == 32,
               "EFI_MEMORY_DESCRIPTOR.Attribute offset");
_Static_assert(sizeof(EFI_MEMORY_DESCRIPTOR) == 40,
               "EFI_MEMORY_DESCRIPTOR ABI size");

enum {
    GXOS_EFI_RESERVED_MEMORY_TYPE = 0,
    GXOS_EFI_LOADER_CODE_MEMORY_TYPE = 1,
    GXOS_EFI_LOADER_DATA_MEMORY_TYPE = 2,
    GXOS_EFI_BOOT_SERVICES_CODE_MEMORY_TYPE = 3,
    GXOS_EFI_BOOT_SERVICES_DATA_MEMORY_TYPE = 4,
    GXOS_EFI_RUNTIME_SERVICES_CODE_MEMORY_TYPE = 5,
    GXOS_EFI_RUNTIME_SERVICES_DATA_MEMORY_TYPE = 6,
    GXOS_EFI_CONVENTIONAL_MEMORY_TYPE = 7,
    GXOS_EFI_UNUSABLE_MEMORY_TYPE = 8,
    GXOS_EFI_ACPI_RECLAIM_MEMORY_TYPE = 9,
    GXOS_EFI_ACPI_NVS_MEMORY_TYPE = 10,
    GXOS_EFI_MEMORY_MAPPED_IO_MEMORY_TYPE = 11,
    GXOS_EFI_MEMORY_MAPPED_IO_PORT_SPACE_TYPE = 12,
    GXOS_EFI_PAL_CODE_MEMORY_TYPE = 13,
    GXOS_EFI_PERSISTENT_MEMORY_TYPE = 14
};

typedef GXOS_EFI_STATUS (GXOS_MEMORY_EFIAPI *GXOS_EFI_GET_MEMORY_MAP)(
    GXOS_EFI_UINTN *memory_map_size,
    void *memory_map,
    GXOS_EFI_UINTN *map_key,
    GXOS_EFI_UINTN *descriptor_size,
    uint32_t *descriptor_version);
typedef GXOS_EFI_STATUS (GXOS_MEMORY_EFIAPI *GXOS_EFI_ALLOCATE_POOL)(
    uint32_t pool_type,
    GXOS_EFI_UINTN size,
    void **buffer);
typedef GXOS_EFI_STATUS (GXOS_MEMORY_EFIAPI *GXOS_EFI_FREE_POOL)(void *buffer);

typedef enum {
    GXOS_MEMORY_MAP_STATUS_OK = 0,
    GXOS_MEMORY_MAP_STATUS_INVALID_ARGUMENT,
    GXOS_MEMORY_MAP_STATUS_FIRMWARE_QUERY,
    GXOS_MEMORY_MAP_STATUS_ALLOCATION,
    GXOS_MEMORY_MAP_STATUS_CAPACITY,
    GXOS_MEMORY_MAP_STATUS_MALFORMED,
    GXOS_MEMORY_MAP_STATUS_OVERFLOW,
    GXOS_MEMORY_MAP_STATUS_RETRY_EXHAUSTED
} GXOS_MEMORY_MAP_STATUS;

typedef struct {
    uint8_t *backing;
    uint64_t backing_bytes;
    uint64_t map_bytes;
    uint64_t descriptor_size;
    uint32_t descriptor_version;
    uint64_t map_key;
    uint32_t descriptor_count;
    uint64_t generation;
    uint32_t valid;
} GXOS_UEFI_MEMORY_MAP;

GXOS_MEMORY_MAP_STATUS gxos_uefi_memory_map_acquire(
    GXOS_UEFI_MEMORY_MAP *map,
    GXOS_EFI_GET_MEMORY_MAP get_memory_map,
    GXOS_EFI_ALLOCATE_POOL allocate_pool,
    GXOS_EFI_FREE_POOL free_pool);
const EFI_MEMORY_DESCRIPTOR *gxos_uefi_memory_map_descriptor(
    const GXOS_UEFI_MEMORY_MAP *map,
    uint32_t index);
int gxos_uefi_memory_map_parse(const void *memory_map,
                               uint64_t map_bytes,
                               uint64_t descriptor_size,
                               uint32_t *descriptor_count_out);

typedef enum {
    GXOS_MEMORY_CLASS_CONVENTIONAL = 0,
    GXOS_MEMORY_CLASS_LOADER_CODE,
    GXOS_MEMORY_CLASS_LOADER_DATA,
    GXOS_MEMORY_CLASS_BOOT_SERVICES_CODE,
    GXOS_MEMORY_CLASS_BOOT_SERVICES_DATA,
    GXOS_MEMORY_CLASS_RUNTIME_SERVICES_CODE,
    GXOS_MEMORY_CLASS_RUNTIME_SERVICES_DATA,
    GXOS_MEMORY_CLASS_ACPI_RECLAIM,
    GXOS_MEMORY_CLASS_ACPI_NVS,
    GXOS_MEMORY_CLASS_RESERVED,
    GXOS_MEMORY_CLASS_UNUSABLE,
    GXOS_MEMORY_CLASS_MMIO,
    GXOS_MEMORY_CLASS_MMIO_PORT_SPACE,
    GXOS_MEMORY_CLASS_PERSISTENT,
    GXOS_MEMORY_CLASS_PAL_CODE,
    GXOS_MEMORY_CLASS_UNKNOWN,
    GXOS_MEMORY_CLASS_COUNT
} GXOS_MEMORY_CLASS;

typedef enum {
    GXOS_MEMORY_CLASSIFICATION_OK = 0,
    GXOS_MEMORY_CLASSIFICATION_INVALID_ARGUMENT,
    GXOS_MEMORY_CLASSIFICATION_MALFORMED,
    GXOS_MEMORY_CLASSIFICATION_OVERFLOW
} GXOS_MEMORY_CLASSIFICATION_STATUS;

typedef struct {
    uint32_t valid;
    uint32_t descriptor_count;
    uint64_t class_bytes[GXOS_MEMORY_CLASS_COUNT];
    uint64_t class_pages[GXOS_MEMORY_CLASS_COUNT];
    uint64_t total_ram_like_bytes;
    uint64_t conventional_bytes;
} GXOS_MEMORY_CLASSIFICATION;

GXOS_MEMORY_CLASSIFICATION_STATUS gxos_uefi_memory_map_classify(
    const GXOS_UEFI_MEMORY_MAP *map,
    GXOS_MEMORY_CLASSIFICATION *classification);
const char *gxos_memory_class_name(GXOS_MEMORY_CLASS memory_class);
GXOS_MEMORY_CLASS gxos_memory_class_for_efi_type(uint32_t type);
int gxos_memory_class_is_ram_like(GXOS_MEMORY_CLASS memory_class);

typedef enum {
    GXOS_MEMORY_ALLOCATION_IMAGE = 0,
    GXOS_MEMORY_ALLOCATION_PAYLOAD_STAGING,
    GXOS_MEMORY_ALLOCATION_IMPORT_STUB,
    GXOS_MEMORY_ALLOCATION_TLS_VECTOR,
    GXOS_MEMORY_ALLOCATION_TLS_BLOCK,
    GXOS_MEMORY_ALLOCATION_GS,
    GXOS_MEMORY_ALLOCATION_TEB,
    GXOS_MEMORY_ALLOCATION_MAIN_STACK,
    GXOS_MEMORY_ALLOCATION_SCHEDULER_STACK,
    GXOS_MEMORY_ALLOCATION_SCHEDULER_PAGE,
    GXOS_MEMORY_ALLOCATION_MEMORY_MAP,
    GXOS_MEMORY_ALLOCATION_PERSISTENT_POOL,
    GXOS_MEMORY_ALLOCATION_OTHER,
    GXOS_MEMORY_ALLOCATION_COUNT
} GXOS_MEMORY_ALLOCATION_CLASS;

typedef enum {
    GXOS_MEMORY_OWNER_LOADER = 0,
    GXOS_MEMORY_OWNER_NATIVEAOT,
    GXOS_MEMORY_OWNER_IMPORTS,
    GXOS_MEMORY_OWNER_TLS,
    GXOS_MEMORY_OWNER_SCHEDULER,
    GXOS_MEMORY_OWNER_CRT,
    GXOS_MEMORY_OWNER_MEMORY_ACCOUNTING,
    GXOS_MEMORY_OWNER_OTHER,
    GXOS_MEMORY_OWNER_COUNT
} GXOS_MEMORY_OWNER;

typedef enum {
    GXOS_LEDGER_STATUS_OK = 0,
    GXOS_LEDGER_STATUS_INVALID_ARGUMENT,
    GXOS_LEDGER_STATUS_ZERO_LENGTH,
    GXOS_LEDGER_STATUS_OVERFLOW,
    GXOS_LEDGER_STATUS_OVERLAP,
    GXOS_LEDGER_STATUS_CAPACITY,
    GXOS_LEDGER_STATUS_NOT_FOUND,
    GXOS_LEDGER_STATUS_INVALID_STATE
} GXOS_LEDGER_STATUS;

typedef struct {
    uint32_t live;
    uint64_t base;
    uint64_t bytes;
    uint64_t pages;
    GXOS_MEMORY_ALLOCATION_CLASS allocation_class;
    GXOS_MEMORY_OWNER owner;
    uint64_t physical_impact_bytes;
    uint64_t commit_impact_bytes;
    uint64_t virtual_reservation_impact_bytes;
    uint64_t generation;
} GXOS_PHYSICAL_ALLOCATION;

typedef struct {
    GXOS_PHYSICAL_ALLOCATION entries[GXOS_PHYSICAL_LEDGER_CAPACITY];
    uint32_t live_count;
    uint32_t exhausted;
    uint64_t physical_bytes;
    uint64_t commit_bytes;
    uint64_t virtual_reservation_bytes;
    uint64_t generation;
} GXOS_PHYSICAL_LEDGER;

void gxos_physical_ledger_init(GXOS_PHYSICAL_LEDGER *ledger,
                               uint64_t generation);
GXOS_LEDGER_STATUS gxos_physical_ledger_insert(
    GXOS_PHYSICAL_LEDGER *ledger,
    const GXOS_PHYSICAL_ALLOCATION *allocation,
    uint32_t *slot_out);
GXOS_LEDGER_STATUS gxos_physical_ledger_remove(
    GXOS_PHYSICAL_LEDGER *ledger,
    uint32_t slot);
int gxos_physical_ledger_find(const GXOS_PHYSICAL_LEDGER *ledger,
                              uint64_t base,
                              uint64_t bytes,
                              uint32_t *slot_out);
int gxos_physical_ledger_validate(const GXOS_PHYSICAL_LEDGER *ledger);
const char *gxos_memory_allocation_class_name(
    GXOS_MEMORY_ALLOCATION_CLASS allocation_class);
const char *gxos_memory_owner_name(GXOS_MEMORY_OWNER owner);

typedef enum {
    GXOS_VM_STATUS_OK = 0,
    GXOS_VM_STATUS_INVALID_ARGUMENT,
    GXOS_VM_STATUS_OUTSIDE_ARENA,
    GXOS_VM_STATUS_OVERFLOW,
    GXOS_VM_STATUS_OVERLAP,
    GXOS_VM_STATUS_CAPACITY,
    GXOS_VM_STATUS_NOT_FOUND,
    GXOS_VM_STATUS_COMMIT_OVERLAP,
    GXOS_VM_STATUS_COMMIT_OUTSIDE_RESERVATION,
    GXOS_VM_STATUS_COMMITTED_RESERVATION
} GXOS_VM_STATUS;

typedef struct {
    uint32_t live;
    uint64_t base;
    uint64_t bytes;
    uint64_t committed_bytes;
    uint32_t kind;
    uint64_t generation;
} GXOS_VM_RESERVATION;

typedef struct {
    uint32_t live;
    uint32_t reservation_slot;
    uint64_t base;
    uint64_t bytes;
    uint64_t generation;
} GXOS_VM_COMMITMENT;

/* This is a guideXOS-owned, bounded identity-mapped arena, not Windows VM. */
#define GXOS_VM_ARENA_BASE 0x0000000000010000ULL
#define GXOS_VM_ARENA_LENGTH 0x000000003FFF0000ULL

typedef struct {
    uint64_t base;
    uint64_t length;
    GXOS_VM_RESERVATION reservations[GXOS_VM_MAX_RESERVATIONS];
    GXOS_VM_COMMITMENT commitments[GXOS_VM_MAX_COMMITMENTS];
    uint32_t reservation_count;
    uint32_t commitment_count;
    uint64_t total_reserved_bytes;
    uint64_t total_committed_bytes;
    uint64_t generation;
    uint32_t valid;
} GXOS_VM_ARENA;

void gxos_vm_arena_init(GXOS_VM_ARENA *arena,
                        uint64_t base,
                        uint64_t length,
                        uint64_t generation);
int gxos_vm_arena_contains(const GXOS_VM_ARENA *arena,
                           uint64_t base,
                           uint64_t bytes);
GXOS_VM_STATUS gxos_vm_arena_reserve(GXOS_VM_ARENA *arena,
                                     uint64_t base,
                                     uint64_t bytes,
                                     uint32_t kind,
                                     uint64_t generation,
                                     uint32_t *slot_out);
GXOS_VM_STATUS gxos_vm_arena_commit(GXOS_VM_ARENA *arena,
                                    uint64_t base,
                                    uint64_t bytes,
                                    uint64_t generation);
GXOS_VM_STATUS gxos_vm_arena_decommit(GXOS_VM_ARENA *arena,
                                      uint64_t base,
                                      uint64_t bytes);
GXOS_VM_STATUS gxos_vm_arena_release(GXOS_VM_ARENA *arena,
                                     uint32_t slot);
int gxos_vm_arena_validate(const GXOS_VM_ARENA *arena);
uint64_t gxos_vm_arena_available(const GXOS_VM_ARENA *arena);

typedef enum {
    GXOS_COMMIT_STATUS_OK = 0,
    GXOS_COMMIT_STATUS_INVALID_ARGUMENT,
    GXOS_COMMIT_STATUS_OVERFLOW,
    GXOS_COMMIT_STATUS_OVERCOMMIT,
    GXOS_COMMIT_STATUS_INVALID_STATE
} GXOS_COMMIT_STATUS;

typedef struct {
    uint64_t commit_limit;
    uint64_t committed_bytes;
    uint64_t available_commit;
    uint64_t generation;
    uint32_t valid;
    uint32_t no_pagefile;
} GXOS_COMMIT_MODEL;

GXOS_COMMIT_STATUS gxos_commit_model_create(GXOS_COMMIT_MODEL *model,
                                            uint64_t commit_limit,
                                            uint64_t committed_bytes,
                                            uint64_t generation);
GXOS_COMMIT_STATUS gxos_commit_model_create_no_pagefile(
    GXOS_COMMIT_MODEL *model,
    uint64_t total_physical_bytes,
    uint64_t available_physical_bytes,
    uint64_t committed_bytes,
    uint64_t generation);

typedef enum {
    GXOS_SNAPSHOT_STATUS_OK = 0,
    GXOS_SNAPSHOT_STATUS_INVALID_ARGUMENT,
    GXOS_SNAPSHOT_STATUS_INVALID_PHYSICAL,
    GXOS_SNAPSHOT_STATUS_INVALID_COMMIT,
    GXOS_SNAPSHOT_STATUS_INVALID_VIRTUAL,
    GXOS_SNAPSHOT_STATUS_OVERFLOW
} GXOS_SNAPSHOT_STATUS;

typedef struct {
    uint64_t generation;
    uint32_t valid;
    uint64_t total_physical_bytes;
    uint64_t available_physical_bytes;
    uint32_t memory_load_percent;
    uint64_t commit_limit_bytes;
    uint64_t available_commit_bytes;
    uint64_t process_virtual_total_bytes;
    uint64_t process_virtual_available_bytes;
    uint64_t accounted_physical_usage_bytes;
    uint64_t process_reserved_virtual_bytes;
    uint64_t process_committed_virtual_bytes;
} GXOS_MEMORY_SNAPSHOT;

typedef struct {
    uint32_t valid;
    uint64_t total_ram_like_bytes;
    uint64_t available_physical_bytes;
    uint64_t accounted_used_bytes;
    uint64_t post_epoch_physical_bytes;
    uint64_t generation;
    uint64_t descriptor_class_bytes[GXOS_MEMORY_CLASS_COUNT];
} GXOS_PHYSICAL_SNAPSHOT;

GXOS_SNAPSHOT_STATUS gxos_physical_snapshot_create(
    GXOS_PHYSICAL_SNAPSHOT *snapshot,
    const GXOS_MEMORY_CLASSIFICATION *classification,
    const GXOS_PHYSICAL_LEDGER *ledger,
    uint64_t generation);
GXOS_SNAPSHOT_STATUS gxos_memory_snapshot_create(
    GXOS_MEMORY_SNAPSHOT *snapshot,
    const GXOS_PHYSICAL_SNAPSHOT *physical,
    const GXOS_VM_ARENA *virtual_arena,
    const GXOS_COMMIT_MODEL *commit,
    uint64_t generation);

/*
 * Derive a current view without refreshing firmware state.  The retained
 * classification is the firmware baseline; startup_snapshot is the
 * immutable generation-2 validation epoch; ledger and virtual_arena are the
 * current post-epoch state.  The caller supplies one accounting generation
 * sampled around the operation.
 */
GXOS_SNAPSHOT_STATUS gxos_memory_snapshot_query_current(
    GXOS_MEMORY_SNAPSHOT *view,
    const GXOS_MEMORY_CLASSIFICATION *classification,
    const GXOS_MEMORY_SNAPSHOT *startup_snapshot,
    const GXOS_PHYSICAL_LEDGER *ledger,
    const GXOS_VM_ARENA *virtual_arena,
    uint64_t generation);

#endif

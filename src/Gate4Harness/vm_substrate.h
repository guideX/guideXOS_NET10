#ifndef GXOS_VM_SUBSTRATE_H
#define GXOS_VM_SUBSTRATE_H

#include <stdint.h>

#include "memory_accounting.h"

#define GXOS_X64_PAGING_ENTRY_PRESENT ((uint64_t)1U)
#define GXOS_X64_PAGING_ENTRY_WRITABLE ((uint64_t)1U << 1)
#define GXOS_X64_PAGING_ENTRY_PAGE_SIZE ((uint64_t)1U << 7)
#define GXOS_X64_PAGING_ENTRY_NO_EXECUTE ((uint64_t)1U << 63)
#define GXOS_X64_PAGING_PHYSICAL_MASK ((uint64_t)0x000FFFFFFFFFF000ULL)
#define GXOS_VM_MAX_OWNED_TABLE_PAGES 64U

typedef void *(*GXOS_VM_PHYSICAL_ALIAS)(void *context,
                                        uint64_t physical_address);
typedef int (*GXOS_VM_ALLOCATE_PAGE)(void *context,
                                     uint64_t *physical_address_out,
                                     void **alias_out);
typedef void (*GXOS_VM_FREE_PAGE)(void *context,
                                  uint64_t physical_address,
                                  void *alias);

typedef struct {
    void *context;
    GXOS_VM_ALLOCATE_PAGE allocate_page;
    GXOS_VM_FREE_PAGE free_page;
    GXOS_VM_PHYSICAL_ALIAS physical_alias;
} GXOS_VM_PAGE_ALLOCATOR;

typedef enum {
    GXOS_VM_PAGING_STATUS_OK = 0,
    GXOS_VM_PAGING_STATUS_INVALID_ARGUMENT,
    GXOS_VM_PAGING_STATUS_NONCANONICAL,
    GXOS_VM_PAGING_STATUS_ALIGNMENT,
    GXOS_VM_PAGING_STATUS_OUTSIDE_ARENA,
    GXOS_VM_PAGING_STATUS_NOT_PRESENT,
    GXOS_VM_PAGING_STATUS_CONFLICT,
    GXOS_VM_PAGING_STATUS_LARGE_PAGE,
    GXOS_VM_PAGING_STATUS_ARENA_CONFLICT,
    GXOS_VM_PAGING_STATUS_ALLOCATION,
    GXOS_VM_PAGING_STATUS_INVALID_TABLE,
    GXOS_VM_PAGING_STATUS_OVERFLOW,
    GXOS_VM_PAGING_STATUS_CAPACITY
} GXOS_VM_PAGING_STATUS;

typedef struct {
    uint64_t physical_base;
    uint64_t page_size;
    uint64_t entry_flags;
    uint32_t level;
    uint32_t present;
} GXOS_VM_MAPPING;

typedef struct {
    uint64_t cr0;
    uint64_t cr3;
    uint64_t cr4;
    uint64_t efer;
    uint32_t pae_enabled;
    uint32_t la57_enabled;
    uint32_t nx_enabled;
    uint32_t page_4k_count;
    uint32_t page_2m_count;
    uint32_t page_1g_count;
    uint64_t direct_identity_bytes;
} GXOS_X64_PAGING_AUDIT;

typedef struct {
    uint64_t root_physical;
    void *root_alias;
    uint64_t arena_base;
    uint64_t arena_length;
    uint32_t arena_pml4_index;
    uint32_t nx_enabled;
    uint32_t active;
    uint32_t owned_table_page_count;
    uint64_t previous_cr3;
    struct {
        uint64_t physical_base;
        void *alias;
    } owned_table_pages[GXOS_VM_MAX_OWNED_TABLE_PAGES];
    GXOS_VM_PAGE_ALLOCATOR table_allocator;
} GXOS_VM_PAGING;

GXOS_VM_PAGING_STATUS gxos_vm_paging_audit_current(
    GXOS_X64_PAGING_AUDIT *audit,
    GXOS_VM_PHYSICAL_ALIAS physical_alias,
    void *alias_context);

GXOS_VM_PAGING_STATUS gxos_vm_paging_query_root(
    uint64_t root_physical,
    uint64_t virtual_address,
    GXOS_VM_PHYSICAL_ALIAS physical_alias,
    void *alias_context,
    GXOS_VM_MAPPING *mapping_out);

GXOS_VM_PAGING_STATUS gxos_vm_paging_create(
    GXOS_VM_PAGING *paging,
    uint64_t current_cr3,
    uint64_t arena_base,
    uint64_t arena_length,
    uint32_t nx_enabled,
    const GXOS_VM_PAGE_ALLOCATOR *table_allocator);

GXOS_VM_PAGING_STATUS gxos_vm_paging_switch_to_owned_root(
    GXOS_VM_PAGING *paging,
    uint64_t *old_cr3_out,
    uint64_t *new_cr3_out);
GXOS_VM_PAGING_STATUS gxos_vm_paging_switch_to_previous_root(
    GXOS_VM_PAGING *paging);
GXOS_VM_PAGING_STATUS gxos_vm_paging_destroy(
    GXOS_VM_PAGING *paging);
void gxos_vm_paging_mark_active(GXOS_VM_PAGING *paging, uint32_t active);

GXOS_VM_PAGING_STATUS gxos_vm_paging_query(
    const GXOS_VM_PAGING *paging,
    uint64_t virtual_address,
    GXOS_VM_MAPPING *mapping_out);
GXOS_VM_PAGING_STATUS gxos_vm_paging_map_page(
    GXOS_VM_PAGING *paging,
    uint64_t virtual_page,
    uint64_t physical_page,
    uint32_t writable,
    uint32_t executable);
GXOS_VM_PAGING_STATUS gxos_vm_paging_map_range(
    GXOS_VM_PAGING *paging,
    uint64_t virtual_start,
    uint64_t physical_start,
    uint64_t page_count,
    uint32_t writable,
    uint32_t executable);
GXOS_VM_PAGING_STATUS gxos_vm_paging_unmap_page(
    GXOS_VM_PAGING *paging,
    uint64_t virtual_page,
    uint64_t *physical_page_out);

typedef enum {
    GXOS_VM_COMMIT_OPERATION_OK = 0,
    GXOS_VM_COMMIT_OPERATION_INVALID_ARGUMENT,
    GXOS_VM_COMMIT_OPERATION_OVERFLOW,
    GXOS_VM_COMMIT_OPERATION_OUTSIDE_RESERVATION,
    GXOS_VM_COMMIT_OPERATION_ALLOCATION,
    GXOS_VM_COMMIT_OPERATION_MAPPING,
    GXOS_VM_COMMIT_OPERATION_BOOKKEEPING,
    GXOS_VM_COMMIT_OPERATION_INCONSISTENT,
    GXOS_VM_COMMIT_OPERATION_CAPACITY
} GXOS_VM_COMMIT_OPERATION_STATUS;

typedef struct {
    GXOS_VM_ARENA *arena;
    GXOS_VM_PAGING *paging;
    GXOS_VM_PAGE_ALLOCATOR data_allocator;
    uint64_t generation;
} GXOS_VM_COMMIT_OPERATION;

GXOS_VM_COMMIT_OPERATION_STATUS gxos_vm_commit_range(
    GXOS_VM_COMMIT_OPERATION *operation,
    uint32_t reservation_slot,
    uint64_t requested_start,
    uint64_t requested_bytes,
    uint32_t writable,
    uint32_t executable,
    uint32_t *new_page_count_out);

#endif

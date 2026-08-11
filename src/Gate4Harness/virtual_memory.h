#ifndef GXOS_VIRTUAL_MEMORY_H
#define GXOS_VIRTUAL_MEMORY_H

#include <stdint.h>

#include "memory_accounting.h"
#include "vm_substrate.h"

#if defined(__x86_64__)
#define GXOS_VM_PUBLIC_MS_ABI __attribute__((ms_abi))
#else
#define GXOS_VM_PUBLIC_MS_ABI
#endif

#define GXOS_VM_PUBLIC_MEM_COMMIT ((uint32_t)0x00001000U)
#define GXOS_VM_PUBLIC_MEM_RESERVE ((uint32_t)0x00002000U)
#define GXOS_VM_PUBLIC_MEM_DECOMMIT ((uint32_t)0x00004000U)
#define GXOS_VM_PUBLIC_MEM_RELEASE ((uint32_t)0x00008000U)
#define GXOS_VM_PUBLIC_MEM_RESET ((uint32_t)0x00080000U)
#define GXOS_VM_PUBLIC_MEM_TOP_DOWN ((uint32_t)0x00100000U)
#define GXOS_VM_PUBLIC_MEM_WRITE_WATCH ((uint32_t)0x00200000U)
#define GXOS_VM_PUBLIC_MEM_PHYSICAL ((uint32_t)0x00400000U)
#define GXOS_VM_PUBLIC_MEM_LARGE_PAGES ((uint32_t)0x20000000U)

#define GXOS_VM_PUBLIC_PAGE_READWRITE ((uint32_t)0x00000004U)

#define GXOS_VM_PUBLIC_ERROR_INVALID_PARAMETER ((uint32_t)87U)
#define GXOS_VM_PUBLIC_ERROR_NOT_ENOUGH_MEMORY ((uint32_t)8U)
#define GXOS_VM_PUBLIC_ERROR_NOT_SUPPORTED ((uint32_t)50U)

typedef enum {
    GXOS_VM_PUBLIC_STATUS_OK = 0,
    GXOS_VM_PUBLIC_STATUS_INVALID_ARGUMENT,
    GXOS_VM_PUBLIC_STATUS_UNSUPPORTED,
    GXOS_VM_PUBLIC_STATUS_CAPACITY,
    GXOS_VM_PUBLIC_STATUS_ALLOCATION,
    GXOS_VM_PUBLIC_STATUS_MAPPING,
    GXOS_VM_PUBLIC_STATUS_INCONSISTENT
} GXOS_VM_PUBLIC_STATUS;

typedef struct {
    GXOS_VM_ARENA *arena;
    GXOS_VM_PAGING *paging;
    GXOS_VM_PAGE_ALLOCATOR data_allocator;
    uint64_t generation;
    uint32_t *last_error;
} GXOS_VM_PUBLIC_CONTEXT;

typedef struct {
    uint64_t requested_bytes;
    uint64_t rounded_bytes;
    uint64_t effective_base;
    uint64_t reservation_base;
    uint32_t reservation_slot;
    uint32_t new_page_count;
    uint32_t existing_page_count;
    uint32_t reserved;
    uint32_t committed;
} GXOS_VM_PUBLIC_RESULT;

GXOS_VM_PUBLIC_STATUS gxos_vm_public_virtual_alloc(
    GXOS_VM_PUBLIC_CONTEXT *context,
    void *address,
    uint64_t size,
    uint32_t allocation_type,
    uint32_t protection,
    GXOS_VM_PUBLIC_RESULT *result_out,
    void **address_out);

GXOS_VM_PUBLIC_STATUS gxos_vm_public_virtual_free(
    GXOS_VM_PUBLIC_CONTEXT *context,
    void *address,
    uint64_t size,
    uint32_t free_type,
    GXOS_VM_PUBLIC_RESULT *result_out,
    int *success_out);

#endif

#ifndef GXOS_CRT_MALLOC_H
#define GXOS_CRT_MALLOC_H

#include <stdint.h>

#if defined(__x86_64__)
#define GXOS_CRT_MALLOC_MS_ABI __attribute__((ms_abi))
#else
#define GXOS_CRT_MALLOC_MS_ABI
#endif

#define GXOS_CRT_MALLOC_EFI_LOADER_DATA 4U
#define GXOS_CRT_MALLOC_MAX_REQUEST ((uint64_t)0xC8000U)
#define GXOS_CRT_MALLOC_REGISTRY_CAPACITY 64U
#define GXOS_CRT_MALLOC_MAX_PROTECTED_RANGES 8U
#define GXOS_CRT_MALLOC_MAX_DIAGNOSTICS 256U
#define GXOS_CRT_FREE_DIAGNOSTIC_CAPACITY 64U
#define GXOS_CRT_MALLOC_NO_SLOT UINT32_MAX
#define GXOS_CRT_MALLOC_NO_LIVE_COUNT UINT32_MAX
#define GXOS_CRT_MALLOC_NO_RELEASE_SLOT UINT32_MAX

#define GXOS_CRT_HEAP_API_SET_DLL "api-ms-win-crt-heap-l1-1-0.dll"
#define GXOS_CRT_HEAP_MALLOC_SYMBOL "malloc"
#define GXOS_CRT_HEAP_FREE_SYMBOL "free"

#define GXOS_CRT_MALLOC_RECORD_LIVE 1U
#define GXOS_CRT_MALLOC_RECORD_FREED 2U

#define GXOS_CRT_MALLOC_OWNER_CRT 1U
#define GXOS_CRT_MALLOC_CLASS_PERSISTENT_POOL 1U

typedef uint64_t (GXOS_CRT_MALLOC_MS_ABI *GXOS_CRT_MALLOC_ALLOCATE_POOL)(
    uint32_t pool_type,
    uintptr_t size,
    void **buffer,
    void *context);
typedef uint64_t (GXOS_CRT_MALLOC_MS_ABI *GXOS_CRT_MALLOC_FREE_POOL)(
    void *buffer,
    void *context);

typedef enum GXOS_CRT_MALLOC_FAILURE {
    GXOS_CRT_MALLOC_FAILURE_NONE = 0,
    GXOS_CRT_MALLOC_FAILURE_NULL_CONTEXT,
    GXOS_CRT_MALLOC_FAILURE_MALFORMED_REGISTRY,
    GXOS_CRT_MALLOC_FAILURE_INVALID_PROTECTED_RANGE,
    GXOS_CRT_MALLOC_FAILURE_ZERO_SIZE,
    GXOS_CRT_MALLOC_FAILURE_NOT_UINTN,
    GXOS_CRT_MALLOC_FAILURE_SIZE_LIMIT,
    GXOS_CRT_MALLOC_FAILURE_BOOT_SERVICES_UNAVAILABLE,
    GXOS_CRT_MALLOC_FAILURE_POOL_SERVICE_UNAVAILABLE,
    GXOS_CRT_MALLOC_FAILURE_METADATA_EXHAUSTED,
    GXOS_CRT_MALLOC_FAILURE_POOL_ALLOCATION,
    GXOS_CRT_MALLOC_FAILURE_NULL_SUCCESS,
    GXOS_CRT_MALLOC_FAILURE_UNALIGNED,
    GXOS_CRT_MALLOC_FAILURE_RANGE_OVERFLOW,
    GXOS_CRT_MALLOC_FAILURE_PROTECTED_OVERLAP,
    GXOS_CRT_MALLOC_FAILURE_EXISTING_OVERLAP,
    GXOS_CRT_MALLOC_FAILURE_DUPLICATE_POINTER,
    GXOS_CRT_MALLOC_FAILURE_SEQUENCE_EXHAUSTED,
    GXOS_CRT_MALLOC_FAILURE_ACCOUNTING_OVERFLOW
} GXOS_CRT_MALLOC_FAILURE;

typedef enum GXOS_CRT_FREE_FAILURE {
    GXOS_CRT_FREE_FAILURE_NONE = 0,
    GXOS_CRT_FREE_FAILURE_NULL_CONTEXT,
    GXOS_CRT_FREE_FAILURE_MALFORMED_REGISTRY,
    GXOS_CRT_FREE_FAILURE_BACKING_SERVICE_UNAVAILABLE,
    GXOS_CRT_FREE_FAILURE_BACKING_RELEASE,
    GXOS_CRT_FREE_FAILURE_INTERIOR_POINTER,
    GXOS_CRT_FREE_FAILURE_UNKNOWN_POINTER,
    GXOS_CRT_FREE_FAILURE_DOUBLE_FREE,
    GXOS_CRT_FREE_FAILURE_ACCOUNTING
} GXOS_CRT_FREE_FAILURE;

typedef struct GXOS_CRT_MALLOC_PROTECTED_RANGE {
    uintptr_t base;
    uintptr_t end;
    uint32_t kind;
} GXOS_CRT_MALLOC_PROTECTED_RANGE;

typedef struct GXOS_CRT_MALLOC_RECORD {
    uintptr_t pointer;
    uint64_t requested_size;
    uint64_t backing_size;
    uint64_t allocation_sequence;
    uint32_t occupied;
    uint32_t state;
    uint32_t owner;
    uint32_t allocation_class;
} GXOS_CRT_MALLOC_RECORD;

typedef struct GXOS_CRT_MALLOC_RELEASE_RECORD {
    uintptr_t pointer;
    uint64_t requested_size;
    uint64_t backing_size;
    uint64_t allocation_sequence;
    uint64_t release_sequence;
    uint32_t registry_slot;
    uint32_t state;
} GXOS_CRT_MALLOC_RELEASE_RECORD;

typedef struct GXOS_CRT_MALLOC_DIAGNOSTIC {
    uint64_t invocation_number;
    uintptr_t static_call_site;
    uintptr_t runtime_call_site;
    uint64_t requested_size;
    uint32_t live_count_before;
    uint32_t registry_slot;
    uint32_t pool_service_available;
    uint64_t allocate_pool_status;
    uintptr_t returned_pointer;
    uint32_t alignment_mod8;
    uint32_t alignment_mod16;
    uintptr_t allocation_range_base;
    uintptr_t allocation_range_end;
    uint32_t overlap_validation;
    uint32_t live_count_after;
    uint32_t rollback_count;
    uint64_t rollback_status;
    uintptr_t return_value;
    GXOS_CRT_MALLOC_FAILURE failure;
} GXOS_CRT_MALLOC_DIAGNOSTIC;

typedef struct GXOS_CRT_FREE_DIAGNOSTIC {
    uint64_t invocation_number;
    uintptr_t static_call_site;
    uintptr_t runtime_call_site;
    uintptr_t pointer;
    uint32_t live_count_before;
    uint32_t live_count_after;
    uint32_t registry_slot;
    uint32_t record_state_before;
    uint32_t record_state_after;
    uint64_t allocation_sequence;
    uint64_t requested_size;
    uint64_t backing_size;
    uint32_t owner;
    uint32_t allocation_class;
    uint64_t total_requested_bytes_before;
    uint64_t total_requested_bytes_after;
    uint64_t largest_request_before;
    uint64_t largest_request_after;
    uint64_t accounting_generation_before;
    uint64_t accounting_generation_after;
    uint64_t backing_release_status;
    uint32_t backing_release_attempted;
    uint32_t backing_released;
    GXOS_CRT_FREE_FAILURE failure;
} GXOS_CRT_FREE_DIAGNOSTIC;

typedef void (GXOS_CRT_MALLOC_MS_ABI *GXOS_CRT_MALLOC_TRACE)(
    const GXOS_CRT_MALLOC_DIAGNOSTIC *diagnostic,
    void *context);

typedef struct GXOS_CRT_MALLOC_CONTEXT {
    void *boot_services;
    uint32_t boot_services_available;
    GXOS_CRT_MALLOC_ALLOCATE_POOL allocate_pool;
    GXOS_CRT_MALLOC_FREE_POOL free_pool;
    void *allocator_context;
    uintptr_t preferred_image_base;
    uintptr_t image_base;
    uintptr_t image_end;
    uint32_t protected_range_count;
    GXOS_CRT_MALLOC_PROTECTED_RANGE protected_ranges[
        GXOS_CRT_MALLOC_MAX_PROTECTED_RANGES];
    uint32_t live_count;
    uint64_t total_requested_bytes;
    uint64_t largest_request;
    uint64_t max_live_allocation_count;
    uint64_t allocation_failure_count;
    uint64_t metadata_exhaustion_count;
    uint64_t duplicate_pointer_rejection_count;
    uint64_t pool_rollback_count;
    uint64_t callnewh_reached;
    uint64_t invocation_count;
    uint64_t next_allocation_sequence;
    uint64_t accounting_generation;
    uint64_t free_invocation_count;
    uint64_t successful_free_count;
    uint64_t null_free_count;
    uint64_t invalid_free_count;
    uint64_t double_free_count;
    uint32_t release_record_count;
    uint32_t next_release_record_slot;
    uint32_t diagnostic_count;
    uint32_t diagnostic_overflow_count;
    uint32_t free_diagnostic_count;
    uint32_t free_diagnostic_overflow_count;
    GXOS_CRT_MALLOC_RECORD records[GXOS_CRT_MALLOC_REGISTRY_CAPACITY];
    GXOS_CRT_MALLOC_RELEASE_RECORD release_records[
        GXOS_CRT_MALLOC_REGISTRY_CAPACITY];
    GXOS_CRT_MALLOC_DIAGNOSTIC diagnostics[GXOS_CRT_MALLOC_MAX_DIAGNOSTICS];
    GXOS_CRT_FREE_DIAGNOSTIC free_diagnostics[
        GXOS_CRT_FREE_DIAGNOSTIC_CAPACITY];
    GXOS_CRT_MALLOC_TRACE trace;
    void *trace_context;
} GXOS_CRT_MALLOC_CONTEXT;

void gxos_crt_malloc_context_reset(GXOS_CRT_MALLOC_CONTEXT *context);
int gxos_crt_malloc_add_protected_range(
    GXOS_CRT_MALLOC_CONTEXT *context,
    uintptr_t base,
    uintptr_t end,
    uint32_t kind);
int gxos_crt_malloc_registry_valid(const GXOS_CRT_MALLOC_CONTEXT *context);
const GXOS_CRT_MALLOC_RECORD *gxos_crt_malloc_find_live_record(
    const GXOS_CRT_MALLOC_CONTEXT *context,
    uintptr_t pointer);
const GXOS_CRT_MALLOC_RECORD *gxos_crt_malloc_find_live_containing_record(
    const GXOS_CRT_MALLOC_CONTEXT *context,
    uintptr_t pointer);
const GXOS_CRT_MALLOC_RELEASE_RECORD *gxos_crt_malloc_find_release_record(
    const GXOS_CRT_MALLOC_CONTEXT *context,
    uintptr_t pointer);
const GXOS_CRT_MALLOC_DIAGNOSTIC *gxos_crt_malloc_get_diagnostic(
    const GXOS_CRT_MALLOC_CONTEXT *context,
    uint32_t index);
const GXOS_CRT_FREE_DIAGNOSTIC *gxos_crt_malloc_get_free_diagnostic(
    const GXOS_CRT_MALLOC_CONTEXT *context,
    uint32_t index);

void *GXOS_CRT_MALLOC_MS_ABI gxos_crt_malloc_call(
    GXOS_CRT_MALLOC_CONTEXT *context,
    uint64_t requested_size,
    uintptr_t runtime_call_site,
    uintptr_t static_call_site);
void *GXOS_CRT_MALLOC_MS_ABI gxos_crt_malloc_entry(
    GXOS_CRT_MALLOC_CONTEXT *context,
    uint64_t requested_size,
    uintptr_t runtime_return_address);
void GXOS_CRT_MALLOC_MS_ABI gxos_crt_free_call(
    GXOS_CRT_MALLOC_CONTEXT *context,
    void *pointer,
    uintptr_t runtime_call_site,
    uintptr_t static_call_site);
void GXOS_CRT_MALLOC_MS_ABI gxos_crt_free_entry(
    GXOS_CRT_MALLOC_CONTEXT *context,
    void *pointer,
    uintptr_t runtime_return_address);

#endif

#ifndef GXOS_CRT_ONEXIT_H
#define GXOS_CRT_ONEXIT_H

#include <stddef.h>
#include <stdint.h>

#if defined(__x86_64__)
#define GXOS_CRT_ONEXIT_MS_ABI __attribute__((ms_abi))
#else
#define GXOS_CRT_ONEXIT_MS_ABI
#endif
#define GXOS_CRT_EFIAPI GXOS_CRT_ONEXIT_MS_ABI

/* Microsoft UCRT declarations from corecrt_startup.h. */
typedef void (GXOS_CRT_ONEXIT_MS_ABI *GXOS_CRT_ONEXIT_PVFV)(void);
typedef int (GXOS_CRT_ONEXIT_MS_ABI *GXOS_CRT_ONEXIT_T)(void);

typedef struct _onexit_table_t {
    GXOS_CRT_ONEXIT_PVFV *first;
    GXOS_CRT_ONEXIT_PVFV *last;
    GXOS_CRT_ONEXIT_PVFV *end;
} GXOS_CRT_ONEXIT_TABLE;

#define GXOS_CRT_ONEXIT_MAX_MEMORY_REGIONS 32U
#define GXOS_CRT_ONEXIT_MAX_INITIALIZED_TABLES 4U
#define GXOS_CRT_ONEXIT_MAX_CENSUS_ENTRIES 16U
#define GXOS_CRT_ONEXIT_FAILURE (-1)

typedef struct {
    uintptr_t base;
    uintptr_t end;
    uint32_t readable;
    uint32_t executable;
    uint32_t writable;
} GXOS_CRT_ONEXIT_MEMORY_REGION;

typedef struct {
    uintptr_t image_base;
    uintptr_t image_end;
    uintptr_t encoded_null;
    uint32_t relocations_applied;
    uint32_t region_count;
    uint32_t initialized_table_count;
    uintptr_t initialized_tables[GXOS_CRT_ONEXIT_MAX_INITIALIZED_TABLES];
    GXOS_CRT_ONEXIT_MEMORY_REGION regions[GXOS_CRT_ONEXIT_MAX_MEMORY_REGIONS];
} GXOS_CRT_ONEXIT_CONTEXT;

typedef enum GXOS_CRT_ONEXIT_STATUS {
    GXOS_CRT_ONEXIT_STATUS_OK = 0,
    GXOS_CRT_ONEXIT_STATUS_INVALID_CONTEXT,
    GXOS_CRT_ONEXIT_STATUS_NULL_TABLE,
    GXOS_CRT_ONEXIT_STATUS_NONCANONICAL_TABLE,
    GXOS_CRT_ONEXIT_STATUS_UNREADABLE_TABLE,
    GXOS_CRT_ONEXIT_STATUS_UNWRITABLE_TABLE,
    GXOS_CRT_ONEXIT_STATUS_TABLE_NOT_INITIALIZED,
    GXOS_CRT_ONEXIT_STATUS_NONCANONICAL_CALLBACK,
    GXOS_CRT_ONEXIT_STATUS_NONEXECUTABLE_CALLBACK,
    GXOS_CRT_ONEXIT_STATUS_NONCANONICAL_STORAGE,
    GXOS_CRT_ONEXIT_STATUS_UNALIGNED_STORAGE,
    GXOS_CRT_ONEXIT_STATUS_STORAGE_RANGE_INVALID,
    GXOS_CRT_ONEXIT_STATUS_INVALID_TABLE_STATE,
    GXOS_CRT_ONEXIT_STATUS_STORAGE_FULL,
    GXOS_CRT_ONEXIT_STATUS_GROWTH_REQUIRED,
    GXOS_CRT_ONEXIT_STATUS_ALLOCATION_FAILED,
    GXOS_CRT_ONEXIT_STATUS_CAPACITY_OVERFLOW,
    GXOS_CRT_ONEXIT_STATUS_POINTER_OVERFLOW,
    GXOS_CRT_ONEXIT_STATUS_ENCODING_UNAVAILABLE
} GXOS_CRT_ONEXIT_STATUS;

typedef struct {
    GXOS_CRT_ONEXIT_STATUS status;
    uintptr_t table;
    uintptr_t callback;
    uintptr_t table_first_raw;
    uintptr_t table_last_raw;
    uintptr_t table_end_raw;
    uintptr_t first;
    uintptr_t last;
    uintptr_t end;
    uintptr_t table_region_base;
    uintptr_t table_region_end;
    uint32_t table_region_readable;
    uint32_t table_region_writable;
    uintptr_t storage_region_base;
    uintptr_t storage_region_end;
    uint32_t storage_region_readable;
    uint32_t storage_region_writable;
    uintptr_t callback_region_base;
    uintptr_t callback_region_end;
    uint32_t callback_region_executable;
    uint32_t used_count;
    uint32_t capacity;
    uint32_t remaining_capacity;
    uint32_t entry_index;
    uintptr_t encoded_callback;
    uintptr_t stored_value;
    uint32_t pointer_encoded;
    uint32_t initialized_table_match;
    uint32_t initialized_table_index;
    uint32_t growth_required;
    uint32_t allocation_attempted;
    uint32_t callback_executed;
    uint32_t census_count;
    uintptr_t census_values[GXOS_CRT_ONEXIT_MAX_CENSUS_ENTRIES];
} GXOS_CRT_ONEXIT_REPORT;

_Static_assert(sizeof(uintptr_t) == 8,
               "Microsoft x64 on-exit pointers must remain 64 bits");
_Static_assert(sizeof(GXOS_CRT_ONEXIT_PVFV) == 8,
               "_PVFV must remain an 8-byte function pointer on x64");
_Static_assert(sizeof(GXOS_CRT_ONEXIT_T) == 8,
               "_onexit_t must remain an 8-byte function pointer on x64");
_Static_assert(offsetof(GXOS_CRT_ONEXIT_TABLE, first) == 0,
               "_onexit_table_t first offset changed");
_Static_assert(offsetof(GXOS_CRT_ONEXIT_TABLE, last) == 8,
               "_onexit_table_t last offset changed");
_Static_assert(offsetof(GXOS_CRT_ONEXIT_TABLE, end) == 16,
               "_onexit_table_t end offset changed");
_Static_assert(_Alignof(GXOS_CRT_ONEXIT_TABLE) == 8,
               "_onexit_table_t alignment changed");
_Static_assert(sizeof(GXOS_CRT_ONEXIT_TABLE) == 24,
               "_onexit_table_t size changed");

void gxos_crt_onexit_set_encoded_null(uintptr_t encoded_null);
void gxos_crt_onexit_set_encoded_null_address(const uintptr_t *encoded_null_address);
uintptr_t gxos_crt_onexit_get_encoded_null(void);
uintptr_t gxos_crt_onexit_encode_pointer(uintptr_t pointer);
uintptr_t gxos_crt_onexit_decode_pointer(uintptr_t pointer);
const char *gxos_crt_onexit_status_name(GXOS_CRT_ONEXIT_STATUS status);
int GXOS_CRT_ONEXIT_MS_ABI gxos_crt_onexit_configure(
    const GXOS_CRT_ONEXIT_CONTEXT *context);
int GXOS_CRT_ONEXIT_MS_ABI gxos_crt_onexit_set_initialized_tables(
    const uintptr_t *tables,
    uint32_t table_count);
int GXOS_CRT_ONEXIT_MS_ABI gxos_crt_initialize_onexit_table(
    GXOS_CRT_ONEXIT_TABLE *table);
GXOS_CRT_ONEXIT_STATUS GXOS_CRT_ONEXIT_MS_ABI
gxos_crt_onexit_register_checked(
    GXOS_CRT_ONEXIT_TABLE *table,
    GXOS_CRT_ONEXIT_T function,
    GXOS_CRT_ONEXIT_REPORT *report);

#endif

#ifndef GXOS_VECTORED_HANDLER_H
#define GXOS_VECTORED_HANDLER_H

#include <stdint.h>
#include "exception_context.h"

#if defined(__x86_64__)
#define GXOS_VEH_MS_ABI __attribute__((ms_abi))
#else
#define GXOS_VEH_MS_ABI
#endif

#define GXOS_VEH_REGISTRY_CAPACITY 8U
#define GXOS_VEH_MAX_IMAGES 2U
#define GXOS_VEH_MAX_IMAGE_SECTIONS 32U
#define GXOS_VEH_SECTION_READABLE 0x40000000U
#define GXOS_VEH_SECTION_EXECUTABLE 0x20000000U
#define GXOS_VEH_SECTION_WRITABLE 0x80000000U

typedef int32_t (GXOS_VEH_MS_ABI *GXOS_VEH_CALLBACK)(
    GXOS_EXCEPTION_POINTERS_COMPAT *exception_pointers);

typedef struct GXOS_VEH_SECTION {
    uintptr_t base;
    uintptr_t end;
    uint32_t characteristics;
    char name[9];
} GXOS_VEH_SECTION;

/* A persistent, bounded image identity and PE section snapshot. */
typedef struct GXOS_VEH_IMAGE {
    const void *identity;
    uintptr_t image_base;
    uint64_t image_size;
    uint32_t section_count;
    GXOS_VEH_SECTION sections[GXOS_VEH_MAX_IMAGE_SECTIONS];
} GXOS_VEH_IMAGE;

typedef struct GXOS_VEH_RECORD {
    uint32_t occupied;
    uint32_t slot;
    uint32_t requested_first;
    uint32_t callback_section_index;
    uint32_t callback_section_executable;
    uintptr_t callback_address;
    GXOS_VEH_CALLBACK callback;
    uint64_t registration_sequence;
    uintptr_t opaque_handle;
    const GXOS_VEH_IMAGE *callback_image;
    uintptr_t callback_image_base;
    uint64_t callback_rva;
    char callback_section_name[9];
    uint64_t invocation_count;
    int32_t last_return_value;
} GXOS_VEH_RECORD;

/* The caller owns this fixed object; the loader keeps one in static storage. */
typedef struct GXOS_VEH_REGISTRY {
    GXOS_VEH_RECORD records[GXOS_VEH_REGISTRY_CAPACITY];
    uint32_t order[GXOS_VEH_REGISTRY_CAPACITY];
    const GXOS_VEH_IMAGE *images[GXOS_VEH_MAX_IMAGES];
    uint32_t image_count;
    uint32_t live_count;
    uint32_t dispatch_active;
    uint64_t next_registration_sequence;
    uint64_t registration_attempt_count;
    uint64_t allocation_count;
} GXOS_VEH_REGISTRY;

typedef enum GXOS_VEH_VALIDATION_RESULT {
    GXOS_VEH_VALIDATION_OK = 0,
    GXOS_VEH_VALIDATION_NULL_CALLBACK = 1,
    GXOS_VEH_VALIDATION_NONCANONICAL_CALLBACK = 2,
    GXOS_VEH_VALIDATION_NO_IMAGE = 3,
    GXOS_VEH_VALIDATION_BAD_IMAGE = 4,
    GXOS_VEH_VALIDATION_IMAGE_OVERFLOW = 5,
    GXOS_VEH_VALIDATION_OUTSIDE_IMAGE = 6,
    GXOS_VEH_VALIDATION_BAD_SECTION = 7,
    GXOS_VEH_VALIDATION_NOT_EXECUTABLE = 8,
    GXOS_VEH_VALIDATION_NOT_READABLE = 9,
    GXOS_VEH_VALIDATION_WRITABLE_SECTION = 10,
    GXOS_VEH_VALIDATION_REGISTRY_ACTIVE = 11,
    GXOS_VEH_VALIDATION_REGISTRY_FULL = 12,
    GXOS_VEH_VALIDATION_SEQUENCE_EXHAUSTED = 13,
    GXOS_VEH_VALIDATION_BAD_REGISTRY = 14
} GXOS_VEH_VALIDATION_RESULT;

typedef struct GXOS_VEH_CALLBACK_DIAGNOSTICS {
    GXOS_VEH_VALIDATION_RESULT validation;
    const GXOS_VEH_IMAGE *image;
    uint32_t section_index;
    uint32_t section_executable;
    uint32_t section_readable;
    uint32_t section_writable;
    uintptr_t callback_address;
    uintptr_t image_base;
    uint64_t callback_rva;
    char section_name[9];
} GXOS_VEH_CALLBACK_DIAGNOSTICS;

typedef struct GXOS_VEH_DISPATCH_REPORT {
    uint32_t snapshot_count;
    uint32_t snapshot_slots[GXOS_VEH_REGISTRY_CAPACITY];
    uint32_t invoked_count;
    uint32_t invoked_slots[GXOS_VEH_REGISTRY_CAPACITY];
    uint64_t invocation_numbers[GXOS_VEH_REGISTRY_CAPACITY];
    int32_t return_values[GXOS_VEH_REGISTRY_CAPACITY];
    uint32_t invalid_return_count;
    uint32_t stopped_on_continue_execution;
    uint32_t final_continue_search;
    uint32_t final_continue_execution;
    uint32_t final_slot;
} GXOS_VEH_DISPATCH_REPORT;

typedef int32_t (*GXOS_VEH_INVOKER)(
    GXOS_VEH_CALLBACK callback,
    GXOS_EXCEPTION_POINTERS_COMPAT *exception_pointers,
    void *context);

void gxos_veh_registry_init(GXOS_VEH_REGISTRY *registry);
int gxos_veh_registry_configure_images(
    GXOS_VEH_REGISTRY *registry,
    const GXOS_VEH_IMAGE *const *images,
    uint32_t image_count);
#if defined(GXOS_VEH_ENABLE_TEST_RESET)
void gxos_veh_registry_reset_for_test(GXOS_VEH_REGISTRY *registry);
#endif
int gxos_veh_registry_valid(const GXOS_VEH_REGISTRY *registry);
void *gxos_veh_registry_add(
    GXOS_VEH_REGISTRY *registry,
    uint32_t first,
    GXOS_VEH_CALLBACK callback,
    GXOS_VEH_CALLBACK_DIAGNOSTICS *diagnostics);
int gxos_veh_registry_handle_is_live(
    const GXOS_VEH_REGISTRY *registry,
    const void *handle);
const GXOS_VEH_RECORD *gxos_veh_registry_record(
    const GXOS_VEH_REGISTRY *registry,
    uint32_t slot);
uint32_t gxos_veh_registry_order_slot(
    const GXOS_VEH_REGISTRY *registry,
    uint32_t position);
uint32_t gxos_veh_registry_live_count(const GXOS_VEH_REGISTRY *registry);
uint32_t gxos_veh_registry_dispatch_active(const GXOS_VEH_REGISTRY *registry);
uint64_t gxos_veh_registry_allocation_count(const GXOS_VEH_REGISTRY *registry);

int gxos_veh_image_parse_pe(
    GXOS_VEH_IMAGE *image,
    const void *identity,
    uintptr_t image_base,
    uint64_t image_size);

int32_t gxos_veh_invoke_direct(
    GXOS_VEH_CALLBACK callback,
    GXOS_EXCEPTION_POINTERS_COMPAT *exception_pointers,
    void *context);

int gxos_veh_dispatch(
    GXOS_VEH_REGISTRY *registry,
    GXOS_EXCEPTION_POINTERS_COMPAT *exception_pointers,
    GXOS_VEH_INVOKER invoker,
    void *invoker_context,
    GXOS_VEH_DISPATCH_REPORT *report);

_Static_assert(sizeof(GXOS_VEH_SECTION) == 0x20, "VEH section record size");
_Static_assert(sizeof(GXOS_VEH_RECORD) >= 0x70, "VEH record retains diagnostics");

#endif

#ifndef GXOS_CREATE_THREAD_H
#define GXOS_CREATE_THREAD_H

#include "scheduler_foundation.h"

#define GXOS_CREATE_THREAD_CREATE_SUSPENDED 0x00000004U
#define GXOS_CREATE_THREAD_ERROR_INVALID_PARAMETER 87U
#define GXOS_CREATE_THREAD_ERROR_NOT_ENOUGH_MEMORY 8U

typedef struct {
    uintptr_t base;
    uintptr_t end;
} GXOS_CREATE_THREAD_EXECUTABLE_REGION;

typedef struct {
    GXOS_SCHEDULER *scheduler;
    uintptr_t payload_base;
    uint64_t payload_size;
    const GXOS_CREATE_THREAD_EXECUTABLE_REGION *executable_regions;
    uint32_t executable_region_count;
} GXOS_CREATE_THREAD_CONTEXT;

/*
 * This is the deliberately narrow payload-facing CreateThread contract.
 * lpParameter is stored and copied as an opaque pointer; it is never
 * dereferenced by this layer.
 */
GXOS_SCHEDULER_HANDLE GXOS_SCHEDULER_MS_ABI gxos_create_thread_contract(
    GXOS_CREATE_THREAD_CONTEXT *context,
    const void *thread_attributes,
    uint64_t stack_size,
    GXOS_SCHEDULER_ENTRY start_routine,
    void *parameter,
    uint64_t creation_flags,
    uintptr_t thread_id,
    GXOS_SCHEDULER_TCB **thread_out);

int gxos_create_thread_start_is_executable(
    const GXOS_CREATE_THREAD_CONTEXT *context,
    GXOS_SCHEDULER_ENTRY start_routine);

#endif

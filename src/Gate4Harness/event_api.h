#ifndef GXOS_EVENT_API_H
#define GXOS_EVENT_API_H

#include "scheduler_foundation.h"

#define GXOS_EVENT_ERROR_INVALID_HANDLE 6U
#define GXOS_EVENT_ERROR_NOT_ENOUGH_MEMORY 8U
#define GXOS_EVENT_ERROR_INVALID_PARAMETER 87U
#define GXOS_WAIT_IO_COMPLETION 0x000000C0U

typedef int (*GXOS_WAIT_READ_HANDLE)(const void *source,
                                     GXOS_SCHEDULER_HANDLE *handle_out);

typedef struct {
    GXOS_SCHEDULER *scheduler;
    GXOS_WAIT_READ_HANDLE read_handle;
} GXOS_EVENT_API_CONTEXT;

int gxos_set_event_contract(const GXOS_EVENT_API_CONTEXT *context,
                            GXOS_SCHEDULER_HANDLE handle);

int gxos_reset_event_contract(const GXOS_EVENT_API_CONTEXT *context,
                              GXOS_SCHEDULER_HANDLE handle);

uint32_t gxos_wait_for_single_object_ex_contract(
    const GXOS_EVENT_API_CONTEXT *context,
    GXOS_SCHEDULER_HANDLE handle,
    uint32_t milliseconds,
    uint32_t alertable);

uint32_t gxos_wait_for_single_object_contract(
    const GXOS_EVENT_API_CONTEXT *context,
    GXOS_SCHEDULER_HANDLE handle,
    uint32_t milliseconds);

uint32_t gxos_wait_for_multiple_objects_ex_contract(
    const GXOS_EVENT_API_CONTEXT *context,
    uint32_t count,
    const void *handles,
    uint32_t wait_all,
    uint32_t milliseconds,
    uint32_t alertable);

#endif

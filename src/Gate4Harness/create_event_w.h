#ifndef GXOS_CREATE_EVENT_W_H
#define GXOS_CREATE_EVENT_W_H

#include "scheduler_foundation.h"

#define GXOS_CREATE_EVENT_W_ERROR_INVALID_PARAMETER 87U
#define GXOS_CREATE_EVENT_W_ERROR_NOT_ENOUGH_MEMORY 8U

typedef struct {
    GXOS_SCHEDULER *scheduler;
} GXOS_CREATE_EVENT_W_CONTEXT;

GXOS_SCHEDULER_HANDLE GXOS_SCHEDULER_MS_ABI gxos_create_event_w_contract(
    GXOS_CREATE_EVENT_W_CONTEXT *context,
    const void *event_attributes,
    int32_t manual_reset,
    int32_t initial_state,
    const uint16_t *name);

#endif

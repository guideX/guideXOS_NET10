#include "create_event_w.h"

static void set_failure_error(GXOS_CREATE_EVENT_W_CONTEXT *context,
                              uint32_t error)
{
    if (context != 0 && context->scheduler != 0 &&
        context->scheduler->active) {
        gxos_scheduler_set_last_error(error);
    }
}

GXOS_SCHEDULER_HANDLE GXOS_SCHEDULER_MS_ABI gxos_create_event_w_contract(
    GXOS_CREATE_EVENT_W_CONTEXT *context,
    const void *event_attributes,
    int32_t manual_reset,
    int32_t initial_state,
    const uint16_t *name)
{
    GXOS_SCHEDULER_HANDLE handle = 0;

    if (context == 0 || context->scheduler == 0 ||
        !context->scheduler->active || event_attributes != 0 || name != 0) {
        set_failure_error(context, GXOS_CREATE_EVENT_W_ERROR_INVALID_PARAMETER);
        return 0;
    }
    if (!gxos_scheduler_create_event(context->scheduler,
                                     manual_reset != 0,
                                     initial_state != 0,
                                     &handle)) {
        set_failure_error(context, GXOS_CREATE_EVENT_W_ERROR_NOT_ENOUGH_MEMORY);
        return 0;
    }
    return handle;
}

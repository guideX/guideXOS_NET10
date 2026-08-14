#include "event_api.h"

static void set_scheduler_error(uint32_t error)
{
    gxos_scheduler_set_last_error(error);
}

static GXOS_SCHEDULER_EVENT *open_event(
    GXOS_SCHEDULER_HANDLE handle)
{
    GXOS_SCHEDULER_OBJECT *object = gxos_scheduler_object_from_handle(handle);
    GXOS_SCHEDULER_EVENT *event;
    if (object == 0 || object->type != GXOS_SCHEDULER_OBJECT_EVENT ||
        object->public_handle_refs == 0 || object->close_state ||
        object->internal_refs == 0 || object->target == 0) {
        return 0;
    }
    event = (GXOS_SCHEDULER_EVENT *)object->target;
    if (!event->live ||
        event->object_slot != object->slot ||
        event->generation != object->generation) {
        return 0;
    }
    return event;
}

int gxos_set_event_contract(const GXOS_EVENT_API_CONTEXT *context,
                            GXOS_SCHEDULER_HANDLE handle)
{
    if (context == 0 || context->scheduler == 0 ||
        !context->scheduler->active || open_event(handle) == 0) {
        set_scheduler_error(GXOS_EVENT_ERROR_INVALID_HANDLE);
        return 0;
    }
    if (!gxos_scheduler_signal_event(handle)) {
        set_scheduler_error(GXOS_EVENT_ERROR_INVALID_HANDLE);
        return 0;
    }
    return 1;
}

int gxos_reset_event_contract(const GXOS_EVENT_API_CONTEXT *context,
                              GXOS_SCHEDULER_HANDLE handle)
{
    if (context == 0 || context->scheduler == 0 ||
        !context->scheduler->active || open_event(handle) == 0) {
        set_scheduler_error(GXOS_EVENT_ERROR_INVALID_HANDLE);
        return 0;
    }
    if (!gxos_scheduler_reset_event(handle)) {
        set_scheduler_error(GXOS_EVENT_ERROR_INVALID_HANDLE);
        return 0;
    }
    return 1;
}

uint32_t gxos_wait_for_multiple_objects_ex_contract(
    const GXOS_EVENT_API_CONTEXT *context,
    uint32_t count,
    const void *handles,
    uint32_t wait_all,
    uint32_t milliseconds,
    uint32_t alertable)
{
    GXOS_SCHEDULER_HANDLE handle = 0;
    GXOS_SCHEDULER_EVENT *event;
    GXOS_SCHEDULER_REGISTER_SNAPSHOT snapshot;
    int32_t wait_result = GXOS_SCHEDULER_WAIT_FAILURE;

    if (context == 0 || context->scheduler == 0 ||
        !context->scheduler->active || count != 1U || handles == 0 ||
        wait_all != 0U || milliseconds != GXOS_INFINITE ||
        alertable != 0U || context->read_handle == 0) {
        set_scheduler_error(GXOS_EVENT_ERROR_INVALID_PARAMETER);
        return GXOS_WAIT_FAILED;
    }
    if (!context->read_handle(handles, &handle)) {
        set_scheduler_error(GXOS_EVENT_ERROR_INVALID_PARAMETER);
        return GXOS_WAIT_FAILED;
    }
    event = open_event(handle);
    if (event == 0) {
        set_scheduler_error(GXOS_EVENT_ERROR_INVALID_HANDLE);
        return GXOS_WAIT_FAILED;
    }
    if (event->signaled) return GXOS_WAIT_OBJECT_0;

    gxos_scheduler_main_block(handle, &snapshot, &wait_result);
    if (wait_result == GXOS_SCHEDULER_WAIT_SIGNALED) {
        return GXOS_WAIT_OBJECT_0;
    }
    set_scheduler_error(GXOS_EVENT_ERROR_NOT_ENOUGH_MEMORY);
    return GXOS_WAIT_FAILED;
}

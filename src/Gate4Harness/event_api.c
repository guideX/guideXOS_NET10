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

static uint32_t wait_failure(uint32_t error)
{
    set_scheduler_error(error);
    return GXOS_WAIT_FAILED;
}

static uint32_t wait_one(const GXOS_EVENT_API_CONTEXT *context,
                         GXOS_SCHEDULER_HANDLE handle,
                         uint32_t milliseconds,
                         uint32_t alertable)
{
    GXOS_SCHEDULER_OBJECT *object;
    GXOS_SCHEDULER_EVENT *event;
    GXOS_SCHEDULER_REGISTER_SNAPSHOT snapshot;
    int32_t wait_result = GXOS_SCHEDULER_WAIT_FAILURE;
    uint64_t now_ms;
    uint64_t deadline_ms;

    if (context == 0 || context->scheduler == 0 ||
        !context->scheduler->active) {
        return wait_failure(GXOS_EVENT_ERROR_INVALID_PARAMETER);
    }
    if (alertable != 0U) {
        /* No APC, completion-routine, or equivalent delivery mechanism exists
           in the cooperative scheduler.  Do not manufacture IO_COMPLETION. */
        return wait_failure(GXOS_EVENT_ERROR_INVALID_PARAMETER);
    }
    object = gxos_scheduler_object_from_handle(handle);
    if (object == 0 || object->public_handle_refs == 0 ||
        object->close_state || object->internal_refs == 0) {
        return wait_failure(GXOS_EVENT_ERROR_INVALID_HANDLE);
    }
    if (object->type != GXOS_SCHEDULER_OBJECT_EVENT) {
        return wait_failure(GXOS_EVENT_ERROR_INVALID_PARAMETER);
    }
    event = open_event(handle);
    if (event == 0) return wait_failure(GXOS_EVENT_ERROR_INVALID_HANDLE);
    if (event->signaled) {
        if (!event->manual_reset) event->signaled = 0;
        return GXOS_WAIT_OBJECT_0;
    }
    if (milliseconds == 0U) return GXOS_WAIT_TIMEOUT;
    if (milliseconds != GXOS_INFINITE) {
        if (context->scheduler->now_ms == 0 ||
            !context->scheduler->now_ms(context->scheduler->clock_context,
                                         &now_ms)) {
            return wait_failure(GXOS_EVENT_ERROR_INVALID_PARAMETER);
        }
        deadline_ms = now_ms > UINT64_MAX - (uint64_t)milliseconds
            ? UINT64_MAX : now_ms + (uint64_t)milliseconds;
        (void)gxos_scheduler_poll_timeouts();
        if (!gxos_scheduler_arm_wait_timeout(deadline_ms)) {
            return wait_failure(GXOS_EVENT_ERROR_INVALID_PARAMETER);
        }
    }
    gxos_scheduler_block_current(handle, &snapshot, &wait_result);
    if (wait_result == GXOS_SCHEDULER_WAIT_SIGNALED) {
        return GXOS_WAIT_OBJECT_0;
    }
    if (wait_result == GXOS_SCHEDULER_WAIT_TIMED_OUT) {
        return GXOS_WAIT_TIMEOUT;
    }
    return wait_failure(GXOS_EVENT_ERROR_NOT_ENOUGH_MEMORY);
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

uint32_t gxos_wait_for_single_object_ex_contract(
    const GXOS_EVENT_API_CONTEXT *context,
    GXOS_SCHEDULER_HANDLE handle,
    uint32_t milliseconds,
    uint32_t alertable)
{
    return wait_one(context, handle, milliseconds, alertable);
}

uint32_t gxos_wait_for_single_object_contract(
    const GXOS_EVENT_API_CONTEXT *context,
    GXOS_SCHEDULER_HANDLE handle,
    uint32_t milliseconds)
{
    return wait_one(context, handle, milliseconds, 0U);
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

    if (context == 0 || context->scheduler == 0 ||
        !context->scheduler->active || count != 1U || handles == 0 ||
        wait_all != 0U || milliseconds != GXOS_INFINITE ||
        alertable != 0U || context->read_handle == 0) {
        return wait_failure(GXOS_EVENT_ERROR_INVALID_PARAMETER);
    }
    if (!context->read_handle(handles, &handle)) {
        return wait_failure(GXOS_EVENT_ERROR_INVALID_PARAMETER);
    }
    return wait_one(context, handle, milliseconds, alertable);
}

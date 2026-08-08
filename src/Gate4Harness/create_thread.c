#include "create_thread.h"

static void set_failure_error(GXOS_CREATE_THREAD_CONTEXT *context,
                              uint32_t error)
{
    if (context != 0 && context->scheduler != 0 &&
        context->scheduler->active) {
        gxos_scheduler_set_last_error(error);
    }
}

int gxos_create_thread_start_is_executable(
    const GXOS_CREATE_THREAD_CONTEXT *context,
    GXOS_SCHEDULER_ENTRY start_routine)
{
    uintptr_t start;
    uint64_t encoded_start;
    uintptr_t payload_end;
    uint32_t index;

    if (context == 0 || start_routine == 0 || context->payload_base == 0 ||
        context->payload_size == 0 ||
        context->payload_base > UINTPTR_MAX - context->payload_size) {
        return 0;
    }
    start = (uintptr_t)start_routine;
    encoded_start = (uint64_t)start;
    if ((uintptr_t)encoded_start != start) return 0;
    payload_end = context->payload_base + (uintptr_t)context->payload_size;
    if (start < context->payload_base || start >= payload_end ||
        context->executable_regions == 0) {
        return 0;
    }
    for (index = 0; index != context->executable_region_count; ++index) {
        uintptr_t region_base = context->executable_regions[index].base;
        uintptr_t region_end = context->executable_regions[index].end;
        if (region_base >= context->payload_base && region_end <= payload_end &&
            region_base < region_end && start >= region_base &&
            start < region_end) {
            return 1;
        }
    }
    return 0;
}

GXOS_SCHEDULER_HANDLE GXOS_SCHEDULER_MS_ABI gxos_create_thread_contract(
    GXOS_CREATE_THREAD_CONTEXT *context,
    const void *thread_attributes,
    uint64_t stack_size,
    GXOS_SCHEDULER_ENTRY start_routine,
    void *parameter,
    uint64_t creation_flags,
    uintptr_t thread_id,
    GXOS_SCHEDULER_TCB **thread_out)
{
    GXOS_SCHEDULER_HANDLE handle = 0;
    GXOS_SCHEDULER_TCB *thread = 0;

    if (thread_out != 0) *thread_out = 0;
    if (context == 0 || context->scheduler == 0 ||
        !context->scheduler->active || thread_attributes != 0 ||
        stack_size != 0 || start_routine == 0 ||
        !gxos_create_thread_start_is_executable(context, start_routine) ||
        creation_flags != GXOS_CREATE_THREAD_CREATE_SUSPENDED ||
        thread_id != 0) {
        set_failure_error(context, GXOS_CREATE_THREAD_ERROR_INVALID_PARAMETER);
        return 0;
    }
    if (!gxos_scheduler_create_suspended_thread(
            context->scheduler, start_routine, parameter, &handle, &thread)) {
        set_failure_error(context, GXOS_CREATE_THREAD_ERROR_NOT_ENOUGH_MEMORY);
        return 0;
    }
    if (thread_out != 0) *thread_out = thread;
    return handle;
}

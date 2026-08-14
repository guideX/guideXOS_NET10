#include "standard_handle.h"

static void set_last_error(const GXOS_STANDARD_HANDLE_CONTEXT *context,
                           uint32_t value)
{
    if (context != 0 && context->last_error != 0) {
        *context->last_error = value;
    }
}

static GXOS_SCHEDULER_HANDLE query_role(
    const GXOS_STANDARD_HANDLE_CONTEXT *context,
    uint8_t role,
    uint8_t available)
{
    GXOS_SCHEDULER_HANDLE handle;
    uint8_t roles;

    if (!available || context == 0 || context->scheduler == 0) return 0;
    handle = gxos_scheduler_standard_handle_for_role(role);
    if (handle != 0) return handle;

    roles = 0;
    if (context->output_available) {
        roles |= GXOS_SCHEDULER_STANDARD_STREAM_ROLE_OUTPUT;
    }
    if (context->error_available) {
        roles |= GXOS_SCHEDULER_STANDARD_STREAM_ROLE_ERROR;
    }
    if (roles == 0 || !gxos_scheduler_install_standard_stream(
                         context->scheduler, roles, context->output_backend,
                         context->output_capabilities)) {
        set_last_error(context, GXOS_STANDARD_HANDLE_ERROR_NOT_ENOUGH_MEMORY);
        return 0;
    }
    return gxos_scheduler_standard_handle_for_role(role);
}

GXOS_SCHEDULER_HANDLE gxos_get_std_handle_contract(
    const GXOS_STANDARD_HANDLE_CONTEXT *context,
    uint32_t selector)
{
    if (selector == GXOS_STANDARD_HANDLE_INPUT) {
        return query_role(context, GXOS_SCHEDULER_STANDARD_STREAM_ROLE_INPUT,
                          context != 0 && context->input_available);
    }
    if (selector == GXOS_STANDARD_HANDLE_OUTPUT) {
        return query_role(context, GXOS_SCHEDULER_STANDARD_STREAM_ROLE_OUTPUT,
                          context != 0 && context->output_available);
    }
    if (selector == GXOS_STANDARD_HANDLE_ERROR) {
        return query_role(context, GXOS_SCHEDULER_STANDARD_STREAM_ROLE_ERROR,
                          context != 0 && context->error_available);
    }
    set_last_error(context, GXOS_STANDARD_HANDLE_ERROR_INVALID_HANDLE);
    return GXOS_STANDARD_HANDLE_INVALID_VALUE;
}

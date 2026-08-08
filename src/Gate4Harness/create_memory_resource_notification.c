#include "create_memory_resource_notification.h"

static void set_failure_error(
    GXOS_CREATE_MEMORY_RESOURCE_NOTIFICATION_CONTEXT *context,
    uint32_t error)
{
    if (context != 0 && context->scheduler != 0 &&
        context->scheduler->active) {
        gxos_scheduler_set_last_error(error);
    }
}

GXOS_SCHEDULER_HANDLE GXOS_SCHEDULER_MS_ABI
gxos_create_memory_resource_notification_contract(
    GXOS_CREATE_MEMORY_RESOURCE_NOTIFICATION_CONTEXT *context,
    uint32_t notification_type)
{
    GXOS_SCHEDULER_HANDLE handle = 0;

    if (context == 0 || context->scheduler == 0 ||
        !context->scheduler->active ||
        notification_type != GXOS_MEMORY_RESOURCE_NOTIFICATION_LOW) {
        set_failure_error(
            context,
            GXOS_CREATE_MEMORY_RESOURCE_NOTIFICATION_ERROR_INVALID_PARAMETER);
        return 0;
    }
    if (!gxos_scheduler_create_memory_resource_notification(
            context->scheduler, notification_type, &handle)) {
        set_failure_error(
            context,
            GXOS_CREATE_MEMORY_RESOURCE_NOTIFICATION_ERROR_NOT_ENOUGH_MEMORY);
        return 0;
    }
    return handle;
}

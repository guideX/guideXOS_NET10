#include "managed_kernel_driver_worker.h"
#include "managed_kernel_driver_service_owner.h"

GXOS_MANAGED_KERNEL_DRIVER_RESTART_RESULT
gxos_managed_kernel_driver_service_get_status(
    const GXOS_MANAGED_KERNEL_DRIVER_WORKER_CONTEXT *context,
    GXOS_MANAGED_KERNEL_DRIVER_SERVICE_STATUS_V1 *status_out)
{
    GXOS_MANAGED_KERNEL_INTERRUPT_ROUTE *route = 0;
    GXOS_MANAGED_KERNEL_DRIVER_RESTART_STATE restart_state;
    uint32_t route_active;
    uint32_t route_enabled;
    uint32_t route_accepting;
    uint32_t budget;
    uint64_t flags;
    uint32_t index;

    if (status_out != 0) {
        *status_out = (GXOS_MANAGED_KERNEL_DRIVER_SERVICE_STATUS_V1){0};
    }
    if (context == 0 || status_out == 0 || context->scheduler == 0 ||
        context->interrupt == 0 || !context->scheduler->active ||
        context->scheduler->current == 0 ||
        context->scheduler->boot_thread == 0 ||
        context->interrupt->route_count == 0U ||
        context->interrupt->route_count >
            GXOS_MANAGED_KERNEL_INTERRUPT_MAX_ROUTES) {
        return GXOS_MANAGED_KERNEL_DRIVER_RESTART_RESULT_INVALID_ARGUMENT;
    }
    for (index = 0; index != context->interrupt->route_count; ++index) {
        GXOS_MANAGED_KERNEL_INTERRUPT_ROUTE *candidate =
            &context->interrupt->routes[index];
        if (candidate->device_id == context->device_identity ||
            (context->device_identity == 0U && index == 0U)) {
            route = candidate;
            break;
        }
    }
    if (route == 0 || context->interrupt->critical_enter == 0 ||
        context->interrupt->critical_leave == 0) {
        return GXOS_MANAGED_KERNEL_DRIVER_RESTART_RESULT_INVALID_ARGUMENT;
    }

    /* The service owner is cooperatively serialized on the scheduler's boot
       thread. Hold the route's existing IRQ critical section while copying its
       flags and the lifecycle/owner values into this one snapshot. */
    flags = context->interrupt->critical_enter(route->hardware_context);
    route_active = __atomic_load_n(&route->subscription_active,
                                   __ATOMIC_ACQUIRE);
    route_enabled = __atomic_load_n(&route->hardware_enabled,
                                    __ATOMIC_ACQUIRE);
    route_accepting = __atomic_load_n(&route->accepting_events,
                                      __ATOMIC_ACQUIRE);

    status_out->structure_size =
        (uint32_t)sizeof(GXOS_MANAGED_KERNEL_DRIVER_SERVICE_STATUS_V1);
    status_out->version = GXOS_MANAGED_KERNEL_DRIVER_SERVICE_STATUS_VERSION;
    status_out->service_slot = 0U;
    status_out->device_identity = context->service_handle.identity != 0U
        ? context->service_handle.device_identity
        : (context->device_identity != 0U
            ? context->device_identity : route->device_id);
    status_out->owner_state = context->service_state;
    status_out->current_service_valid =
        context->service_handle.identity != 0U &&
        gxos_managed_kernel_driver_owner_is_current(
            context, context->service_handle) ? 1U : 0U;
    if (status_out->current_service_valid != 0U) {
        status_out->current_service_identity = context->service_handle.identity;
        status_out->current_generation = context->service_handle.generation;
        status_out->service_slot = context->service_handle.slot;
    }
    status_out->route_enabled = route_active != 0U && route_enabled != 0U &&
        route_accepting != 0U ? 1U : 0U;
    status_out->runtime_attached =
        context->nativeaot_lifecycle.attached != 0U &&
        context->nativeaot_lifecycle.detached == 0U ? 1U : 0U;
    status_out->last_failure_reason = context->last_failure_reason;
    status_out->last_failed_identity = context->last_failed_identity;
    status_out->last_failed_generation = context->last_failed_generation;
    status_out->last_failed_device_identity =
        context->last_failed_device_identity;
    budget = gxos_managed_kernel_driver_owner_restart_budget(context);
    restart_state =
        gxos_managed_kernel_driver_owner_restart_state(context);
    status_out->restart_budget_remaining = budget;
    status_out->automatic_restart_attempts = budget <=
            GXOS_MANAGED_KERNEL_DRIVER_AUTOMATIC_RESTART_BUDGET
        ? GXOS_MANAGED_KERNEL_DRIVER_AUTOMATIC_RESTART_BUDGET - budget : 0U;
    status_out->restart_failed = context->service_state ==
            GXOS_MANAGED_KERNEL_DRIVER_SERVICE_RESTART_FAILED ? 1U : 0U;
    status_out->explicit_restart_in_progress =
        gxos_managed_kernel_driver_owner_manual_restart_in_progress(context)
            ? 1U : 0U;
    status_out->explicit_restart_allowed = 0U;

    if (status_out->last_failure_reason ==
            GXOS_MANAGED_KERNEL_DRIVER_FAILURE_NONE) {
        if (context->service_state ==
                GXOS_MANAGED_KERNEL_DRIVER_SERVICE_QUARANTINED ||
            context->nativeaot_lifecycle.runtime_ownership_state ==
                GXOS_NATIVEAOT_RUNTIME_OWNERSHIP_AMBIGUOUS) {
            status_out->last_failure_reason =
                GXOS_MANAGED_KERNEL_DRIVER_FAILURE_AMBIGUOUS_RUNTIME_OWNERSHIP;
        } else if (context->service_state ==
                       GXOS_MANAGED_KERNEL_DRIVER_SERVICE_RESTART_FAILED &&
                   restart_state == GXOS_MANAGED_KERNEL_DRIVER_RESTART_FAILED) {
            status_out->last_failure_reason =
                GXOS_MANAGED_KERNEL_DRIVER_FAILURE_AUTOMATIC_REPLACEMENT_ADMISSION;
        } else if (context->service_state ==
                       GXOS_MANAGED_KERNEL_DRIVER_SERVICE_FAILED) {
            status_out->last_failure_reason =
                GXOS_MANAGED_KERNEL_DRIVER_FAILURE_RUNNING_DISPATCH;
        } else if (context->service_state ==
                       GXOS_MANAGED_KERNEL_DRIVER_SERVICE_START_FAILED) {
            status_out->last_failure_reason =
                GXOS_MANAGED_KERNEL_DRIVER_FAILURE_START_ATTACH;
        }
    }
    if (context->service_state ==
            GXOS_MANAGED_KERNEL_DRIVER_SERVICE_QUARANTINED &&
        status_out->last_failed_identity == 0U &&
        context->service_handle.identity != 0U) {
        status_out->last_failed_identity = context->service_handle.identity;
        status_out->last_failed_generation = context->service_handle.generation;
        status_out->last_failed_device_identity =
            context->service_handle.device_identity;
    }

    status_out->explicit_restart_allowed =
        context->service_state ==
                GXOS_MANAGED_KERNEL_DRIVER_SERVICE_RESTART_FAILED &&
        (context->state == GXOS_MANAGED_KERNEL_DRIVER_WORKER_DESTROYED ||
         context->state == GXOS_MANAGED_KERNEL_DRIVER_WORKER_FREE) &&
        status_out->current_service_valid == 0U &&
        context->service_handle.identity == 0U && context->thread == 0 &&
        context->worker_handle == 0U && context->wake_event == 0U &&
        context->tcb_owned == 0U && context->thread_handle_owned == 0U &&
        context->wake_event_owned == 0U &&
        context->wake_event_handle_open == 0U && context->route_published == 0U &&
        context->nativeaot_lifecycle.ownership_state ==
            GXOS_NATIVEAOT_WORKER_OWNERSHIP_RECLAIMED &&
        (restart_state == GXOS_MANAGED_KERNEL_DRIVER_RESTART_FAILED ||
         restart_state == GXOS_MANAGED_KERNEL_DRIVER_RESTART_EXHAUSTED) &&
        context->restart_prepare != 0 && budget <=
            GXOS_MANAGED_KERNEL_DRIVER_AUTOMATIC_RESTART_BUDGET &&
        status_out->explicit_restart_in_progress == 0U &&
        route_active == 0U && route_enabled == 0U && route_accepting == 0U &&
        status_out->device_identity != 0U ? 1U : 0U;

    context->interrupt->critical_leave(route->hardware_context, flags);
    return GXOS_MANAGED_KERNEL_DRIVER_RESTART_RESULT_OK;
}

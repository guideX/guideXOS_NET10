#include "../managed_kernel_driver_service_owner.h"

#include <stdio.h>

static uint32_t g_failures;
static uint32_t g_critical_depth;

static void expect(int condition, const char *message)
{
    if (!condition) {
        ++g_failures;
        printf("FAIL: %s\n", message);
    }
}

static uint64_t test_critical_enter(void *context)
{
    (void)context;
    ++g_critical_depth;
    return 0x72U;
}

static void test_critical_leave(void *context, uint64_t flags)
{
    (void)context;
    if (flags == 0x72U && g_critical_depth != 0U) --g_critical_depth;
}

static uint32_t GX_MANAGED_KERNEL_MS_ABI test_restart_prepare(uint32_t stage)
{
    (void)stage;
    return GX_MANAGED_OK;
}

static int query(
    GXOS_MANAGED_KERNEL_DRIVER_WORKER_CONTEXT *worker,
    GXOS_MANAGED_KERNEL_DRIVER_SERVICE_STATUS_V1 *status)
{
    return gxos_managed_kernel_driver_service_get_status(worker, status) ==
        GXOS_MANAGED_KERNEL_DRIVER_RESTART_RESULT_OK;
}

int main(void)
{
    GXOS_MANAGED_KERNEL_DRIVER_WORKER_CONTEXT worker = {0};
    GXOS_SCHEDULER scheduler = {0};
    GXOS_SCHEDULER_TCB boot_thread = {0};
    GXOS_SCHEDULER_TCB service_thread = {0};
    GXOS_MANAGED_KERNEL_INTERRUPT_CONTEXT interrupt = {0};
    GXOS_MANAGED_KERNEL_DRIVER_SERVICE_STATUS_V1 status = {0};
    GXOS_MANAGED_KERNEL_DRIVER_SERVICE_HANDLE first = {0};
    GXOS_MANAGED_KERNEL_DRIVER_SERVICE_HANDLE failed = {0};
    GXOS_MANAGED_KERNEL_DRIVER_SERVICE_HANDLE manual = {0};
    uint32_t budget_before = 0U;
    int other_owner = 0;

    scheduler.active = 1U;
    scheduler.boot_thread = &boot_thread;
    scheduler.current = &boot_thread;
    interrupt.route_count = 1U;
    interrupt.critical_enter = test_critical_enter;
    interrupt.critical_leave = test_critical_leave;
    interrupt.routes[0].device_id = 1U;
    worker.scheduler = &scheduler;
    worker.interrupt = &interrupt;
    worker.restart_prepare = test_restart_prepare;
    worker.device_identity = 1U;
    worker.thread = &service_thread;
    worker.state = GXOS_MANAGED_KERNEL_DRIVER_WORKER_RUNNING;

    expect(gxos_managed_kernel_driver_owner_claim(&worker) ==
               GXOS_MANAGED_KERNEL_DRIVER_OWNER_OK &&
           gxos_managed_kernel_driver_owner_publish(&worker, 1U, &first),
           "running generation published");
    worker.service_handle = first;
    worker.service_identity = first.identity;
    worker.service_generation = first.generation;
    worker.service_state = GXOS_MANAGED_KERNEL_DRIVER_SERVICE_WAITING;
    worker.nativeaot_lifecycle.attached = 1U;
    worker.nativeaot_lifecycle.ownership_state =
        GXOS_NATIVEAOT_WORKER_OWNERSHIP_RUNNING;
    interrupt.routes[0].subscription_active = 1U;
    interrupt.routes[0].hardware_enabled = 1U;
    interrupt.routes[0].accepting_events = 1U;

    expect(query(&worker, &status) &&
               status.structure_size == sizeof(status) && status.version == 1U &&
               status.service_slot == 0U && status.device_identity == 1U &&
               status.owner_state == GXOS_MANAGED_KERNEL_DRIVER_SERVICE_WAITING &&
               status.current_service_valid == 1U &&
               status.current_service_identity == first.identity &&
               status.current_generation == first.generation &&
               status.route_enabled == 1U && status.runtime_attached == 1U &&
               status.restart_budget_remaining == 1U &&
               status.automatic_restart_attempts == 0U &&
               status.last_failure_reason ==
                   GXOS_MANAGED_KERNEL_DRIVER_FAILURE_NONE &&
               status.explicit_restart_allowed == 0U,
           "running snapshot has coherent generation, route, and runtime state");
    expect(gxos_managed_kernel_driver_owner_restart_budget(&worker) == 1U &&
               interrupt.routes[0].hardware_enabled == 1U &&
               g_critical_depth == 0U,
           "status read did not mutate restart budget or route state");

    expect(gxos_managed_kernel_driver_owner_release(&worker),
           "normal running service owner released after stop");
    worker.service_handle = (GXOS_MANAGED_KERNEL_DRIVER_SERVICE_HANDLE){0};
    worker.service_state = GXOS_MANAGED_KERNEL_DRIVER_SERVICE_RECLAIMED;
    worker.state = GXOS_MANAGED_KERNEL_DRIVER_WORKER_DESTROYED;
    worker.thread = 0;
    worker.nativeaot_lifecycle.attached = 1U;
    worker.nativeaot_lifecycle.detached = 1U;
    worker.nativeaot_lifecycle.ownership_state =
        GXOS_NATIVEAOT_WORKER_OWNERSHIP_RECLAIMED;
    interrupt.routes[0].subscription_active = 0U;
    interrupt.routes[0].hardware_enabled = 0U;
    interrupt.routes[0].accepting_events = 0U;
    worker.shutdown_policy = GXOS_MANAGED_KERNEL_DRIVER_SERVICE_DRAIN;
    expect(query(&worker, &status) && status.current_service_valid == 0U &&
               status.route_enabled == 0U && status.runtime_attached == 0U &&
               status.last_failure_reason ==
                   GXOS_MANAGED_KERNEL_DRIVER_FAILURE_NONE &&
               status.explicit_restart_allowed == 0U,
           "normal DRAIN stop is distinct from a service failure");
    worker.shutdown_policy = GXOS_MANAGED_KERNEL_DRIVER_SERVICE_DISCARD;
    expect(query(&worker, &status) && status.owner_state ==
               GXOS_MANAGED_KERNEL_DRIVER_SERVICE_RECLAIMED &&
               status.last_failure_reason ==
                   GXOS_MANAGED_KERNEL_DRIVER_FAILURE_NONE &&
               status.route_enabled == 0U,
           "normal DISCARD stop does not create a failure reason");

    expect(gxos_managed_kernel_driver_owner_claim(&worker) ==
               GXOS_MANAGED_KERNEL_DRIVER_OWNER_OK &&
           gxos_managed_kernel_driver_owner_publish(&worker, 1U, &failed),
           "failed automatic-replacement generation published");
    expect(gxos_managed_kernel_driver_owner_restart_begin(
               &worker, failed,
               GXOS_MANAGED_KERNEL_DRIVER_RESTART_CAUSE_RECOVERABLE_DISPATCH,
               &budget_before) && budget_before == 1U &&
           gxos_managed_kernel_driver_owner_release(&worker) &&
           gxos_managed_kernel_driver_owner_claim(&worker) ==
               GXOS_MANAGED_KERNEL_DRIVER_OWNER_OK &&
           gxos_managed_kernel_driver_owner_release(&worker) &&
           gxos_managed_kernel_driver_owner_restart_complete(&worker, 0),
           "one failed automatic replacement consumes this episode's budget");
    worker.service_handle = (GXOS_MANAGED_KERNEL_DRIVER_SERVICE_HANDLE){0};
    worker.service_state = GXOS_MANAGED_KERNEL_DRIVER_SERVICE_RESTART_FAILED;
    worker.state = GXOS_MANAGED_KERNEL_DRIVER_WORKER_DESTROYED;
    worker.thread = 0;
    worker.nativeaot_lifecycle.attached = 1U;
    worker.nativeaot_lifecycle.detached = 1U;
    worker.nativeaot_lifecycle.ownership_state =
        GXOS_NATIVEAOT_WORKER_OWNERSHIP_RECLAIMED;
    worker.last_failure_reason =
        GXOS_MANAGED_KERNEL_DRIVER_FAILURE_AUTOMATIC_REPLACEMENT_ADMISSION;
    worker.last_failed_identity = failed.identity;
    worker.last_failed_generation = failed.generation;
    worker.last_failed_device_identity = failed.device_identity;
    expect(query(&worker, &status) && status.owner_state ==
               GXOS_MANAGED_KERNEL_DRIVER_SERVICE_RESTART_FAILED &&
               status.current_service_valid == 0U &&
               status.current_service_identity == 0U &&
               status.current_generation == 0U && status.device_identity == 1U &&
               status.route_enabled == 0U && status.runtime_attached == 0U &&
               status.last_failure_reason ==
                   GXOS_MANAGED_KERNEL_DRIVER_FAILURE_AUTOMATIC_REPLACEMENT_ADMISSION &&
               status.last_failed_identity == failed.identity &&
               status.last_failed_generation == failed.generation &&
               status.restart_budget_remaining == 0U &&
               status.automatic_restart_attempts == 1U &&
               status.restart_failed == 1U &&
               status.explicit_restart_allowed == 1U,
           "restart-failed snapshot preserves failure cause and failed generation");
    expect(gxos_managed_kernel_driver_owner_restart_budget(&worker) == 0U &&
               interrupt.routes[0].hardware_enabled == 0U &&
               g_critical_depth == 0U,
           "failed-state status query is read-only");

    expect(gxos_managed_kernel_driver_owner_manual_restart_begin(
               &worker, failed.identity + 1U, failed.generation,
               failed.device_identity) ==
                   GXOS_MANAGED_KERNEL_DRIVER_OWNER_RESTART_STALE &&
           gxos_managed_kernel_driver_owner_restart_budget(&worker) == 0U,
           "stale explicit restart tuple is rejected without mutation");
    expect(gxos_managed_kernel_driver_owner_manual_restart_begin(
               &worker, failed.identity, failed.generation,
               failed.device_identity) ==
                   GXOS_MANAGED_KERNEL_DRIVER_OWNER_RESTART_OK &&
           gxos_managed_kernel_driver_owner_manual_restart_in_progress(&worker) &&
           gxos_managed_kernel_driver_owner_restart_budget(&worker) == 1U &&
           gxos_managed_kernel_driver_owner_manual_restart_begin(
               &worker, failed.identity, failed.generation,
               failed.device_identity) ==
                   GXOS_MANAGED_KERNEL_DRIVER_OWNER_RESTART_BUSY,
           "explicit new episode resets budget once and rejects duplicate request");
    expect(gxos_managed_kernel_driver_owner_claim(&worker) ==
               GXOS_MANAGED_KERNEL_DRIVER_OWNER_OK &&
           gxos_managed_kernel_driver_owner_publish(&worker, 1U, &manual) &&
           manual.identity != failed.identity &&
           manual.generation != failed.generation &&
           manual.device_identity == failed.device_identity &&
           !gxos_managed_kernel_driver_owner_is_current(&worker, failed) &&
           gxos_managed_kernel_driver_owner_manual_restart_complete(&worker, 1),
           "explicit episode gets a new identity and rejects stale generation");
    worker.service_handle = manual;
    worker.service_state = GXOS_MANAGED_KERNEL_DRIVER_SERVICE_WAITING;
    worker.state = GXOS_MANAGED_KERNEL_DRIVER_WORKER_RUNNING;
    worker.thread = &service_thread;
    worker.nativeaot_lifecycle.detached = 0U;
    worker.nativeaot_lifecycle.ownership_state =
        GXOS_NATIVEAOT_WORKER_OWNERSHIP_RUNNING;
    interrupt.routes[0].subscription_active = 1U;
    interrupt.routes[0].hardware_enabled = 1U;
    interrupt.routes[0].accepting_events = 1U;
    expect(query(&worker, &status) && status.current_service_valid == 1U &&
               status.current_service_identity == manual.identity &&
               status.current_generation == manual.generation &&
               status.device_identity == failed.device_identity &&
               status.restart_budget_remaining == 1U &&
               status.automatic_restart_attempts == 0U &&
               status.last_failure_reason ==
                   GXOS_MANAGED_KERNEL_DRIVER_FAILURE_AUTOMATIC_REPLACEMENT_ADMISSION &&
               status.last_failed_identity == failed.identity &&
               status.explicit_restart_allowed == 0U,
           "healthy explicit restart preserves historical failure and new budget");
    expect(gxos_managed_kernel_driver_owner_restart_begin(
               &worker, manual,
               GXOS_MANAGED_KERNEL_DRIVER_RESTART_CAUSE_RECOVERABLE_DISPATCH,
               &budget_before) && budget_before == 1U &&
               gxos_managed_kernel_driver_owner_restart_budget(&worker) == 0U &&
               gxos_managed_kernel_driver_owner_restart_complete(&worker, 1) &&
               gxos_managed_kernel_driver_owner_restart_state(&worker) ==
                   GXOS_MANAGED_KERNEL_DRIVER_RESTART_SUCCEEDED,
           "new explicit episode permits exactly one later automatic restart");
    expect(gxos_managed_kernel_driver_owner_claim(&other_owner) ==
               GXOS_MANAGED_KERNEL_DRIVER_OWNER_CAPACITY,
           "joint service owner capacity remains one");

    worker.service_state = GXOS_MANAGED_KERNEL_DRIVER_SERVICE_QUARANTINED;
    worker.last_failure_reason =
        GXOS_MANAGED_KERNEL_DRIVER_FAILURE_AMBIGUOUS_RUNTIME_OWNERSHIP;
    worker.last_failed_identity = manual.identity;
    worker.last_failed_generation = manual.generation;
    worker.last_failed_device_identity = manual.device_identity;
    worker.nativeaot_lifecycle.runtime_ownership_state =
        GXOS_NATIVEAOT_RUNTIME_OWNERSHIP_AMBIGUOUS;
    expect(query(&worker, &status) && status.owner_state ==
               GXOS_MANAGED_KERNEL_DRIVER_SERVICE_QUARANTINED &&
               status.last_failure_reason ==
                   GXOS_MANAGED_KERNEL_DRIVER_FAILURE_AMBIGUOUS_RUNTIME_OWNERSHIP &&
               status.explicit_restart_allowed == 0U,
           "ambiguous runtime ownership is visible and never restartable");

    if (g_failures != 0U) {
        printf("MANAGED_KERNEL_DRIVER_SERVICE_STATUS_HOST_TESTS=FAILED failures=%u\n",
               g_failures);
        return 1;
    }
    printf("MANAGED_KERNEL_DRIVER_SERVICE_STATUS_HOST_TESTS=PASSED\n");
    return 0;
}

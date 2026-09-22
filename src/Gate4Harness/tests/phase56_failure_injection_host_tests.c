#include "nativeaot_scheduler_thread_lifecycle.h"

#include <stdio.h>

static unsigned g_failures;

#define CHECK(condition) do { \
    if (!(condition)) { \
        fprintf(stderr, "phase56 host test failure: %s:%d: %s\n", \
                __FILE__, __LINE__, #condition); \
        ++g_failures; \
    } \
} while (0)

/* The focused test exercises the lifecycle evidence edge without creating a
   scheduler instance or invoking any runtime callback. */
void gxos_scheduler_start_worker(void) {}
void gxos_scheduler_invalid_thread_return(void) {}
void gxos_scheduler_capture_registers(
    GXOS_SCHEDULER_REGISTER_SNAPSHOT *snapshot)
{
    if (snapshot != 0) {
        *snapshot = (GXOS_SCHEDULER_REGISTER_SNAPSHOT){0};
    }
}
void gxos_scheduler_main_dispatch(GXOS_SCHEDULER_REGISTER_SNAPSHOT *snapshot)
{
    (void)snapshot;
}

int main(void)
{
    GXOS_SCHEDULER_TCB reclaimed_thread = {0};
    GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE lifecycle = {0};
    GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE invalid = {0};
    const GXOS_NATIVEAOT_FAILURE_INJECTION_RECORD *record;

    lifecycle.thread = &reclaimed_thread;
    lifecycle.ownership_state = GXOS_NATIVEAOT_WORKER_OWNERSHIP_ALLOCATED;
    CHECK(gxos_nativeaot_scheduler_worker_note_pre_runtime_reclaimed(
              &lifecycle));
    CHECK(lifecycle.ownership_state ==
              GXOS_NATIVEAOT_WORKER_OWNERSHIP_RECLAIMED);
    CHECK(lifecycle.scheduler_owned == 0 && lifecycle.stack_owned == 0 &&
          lifecycle.environment_owned == 0 && lifecycle.tls_fls_owned == 0 &&
          lifecycle.vm_resources_owned == 0);
    CHECK(!gxos_nativeaot_scheduler_worker_note_pre_runtime_reclaimed(
              &lifecycle));

    invalid.thread = &reclaimed_thread;
    invalid.ownership_state = GXOS_NATIVEAOT_WORKER_OWNERSHIP_ALLOCATED;
    CHECK(!gxos_nativeaot_phase56_failure_arm(
              GXOS_NATIVEAOT_FAILURE_INJECTION_AFTER_WORKER_PREPARE,
              &invalid));
    record = gxos_nativeaot_phase56_failure_record();
    CHECK(record != 0 &&
          record->state == GXOS_NATIVEAOT_FAILURE_INJECTION_DISARMED &&
          record->fire_count == 0);
    CHECK(!gxos_nativeaot_phase56_failure_try_fire(&invalid));

    (void)printf("PHASE56_FAILURE_INJECTION_HOST_TEST=%s\n",
                 g_failures == 0 ? "PASS" : "FAIL");
    return g_failures == 0 ? 0 : 1;
}

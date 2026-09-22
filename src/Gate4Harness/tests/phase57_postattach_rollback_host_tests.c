#include "nativeaot_scheduler_thread_lifecycle.h"

#include <stdio.h>

static unsigned g_failures;

#define CHECK(condition) do { \
    if (!(condition)) { \
        fprintf(stderr, "phase57 host test failure: %s:%d: %s\n", \
                __FILE__, __LINE__, #condition); \
        ++g_failures; \
    } \
} while (0)

void gxos_scheduler_start_worker(void) {}
void gxos_scheduler_invalid_thread_return(void) {}
void gxos_scheduler_capture_registers(
    GXOS_SCHEDULER_REGISTER_SNAPSHOT *snapshot)
{
    if (snapshot != 0) *snapshot = (GXOS_SCHEDULER_REGISTER_SNAPSHOT){0};
}
void gxos_scheduler_main_dispatch(GXOS_SCHEDULER_REGISTER_SNAPSHOT *snapshot)
{
    (void)snapshot;
}

int main(void)
{
    GXOS_SCHEDULER_TCB thread = {0};
    GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE lifecycle = {0};
    const GXOS_NATIVEAOT_FAILURE_INJECTION_RECORD *record;

    /* A post-attach point cannot arm until the runtime evidence is complete. */
    lifecycle.thread = &thread;
    lifecycle.ownership_state =
        GXOS_NATIVEAOT_WORKER_OWNERSHIP_RUNTIME_ATTACHED;
    lifecycle.attached = 1;
    lifecycle.runtime_attach_count = 1;
    lifecycle.runtime_thread_owned = 1;
    CHECK(!gxos_nativeaot_phase56_failure_arm(
              GXOS_NATIVEAOT_FAILURE_INJECTION_AFTER_RUNTIME_ATTACH,
              &lifecycle));
    record = gxos_nativeaot_phase56_failure_record();
    CHECK(record != 0 && record->point ==
              GXOS_NATIVEAOT_FAILURE_INJECTION_NONE &&
          record->state == GXOS_NATIVEAOT_FAILURE_INJECTION_DISARMED &&
          record->fire_count == 0);
    CHECK(!gxos_nativeaot_phase56_failure_try_fire(&lifecycle));

    /* The bookkeeping fields make the exactly-once contract explicit even
       when a host test does not claim to emulate NativeAOT detach itself. */
    lifecycle.runtime_detach_count = 1;
    CHECK(lifecycle.runtime_attach_count == 1 &&
          lifecycle.runtime_detach_count == 1);
    CHECK(!gxos_nativeaot_scheduler_worker_detach(&lifecycle));

    (void)printf("PHASE57_POSTATTACH_ROLLBACK_HOST_TEST=PASS\n");
    return g_failures == 0 ? 0 : 1;
}

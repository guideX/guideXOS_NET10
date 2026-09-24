#include "nativeaot_scheduler_thread_lifecycle.h"

#include <stdio.h>

static unsigned g_failures;

#define CHECK(condition) do { \
    if (!(condition)) { \
        fprintf(stderr, "phase59 host test failure: %s:%d: %s\n", \
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

    CHECK(GXOS_NATIVEAOT_FAILURE_INJECTION_AFTER_GC_ROOT_SURVIVAL == 4);
    lifecycle.thread = &thread;
    lifecycle.ownership_state =
        GXOS_NATIVEAOT_WORKER_OWNERSHIP_RUNTIME_ATTACHED;
    lifecycle.attached = 1;
    lifecycle.runtime_attach_count = 1;
    lifecycle.runtime_thread_owned = 1;
    lifecycle.managed_worker_object_owned = 1;
    lifecycle.managed_root_owned = 1;
    lifecycle.managed_root_survived = 1;
    lifecycle.managed_root_identity = 0x5A01;
    lifecycle.managed_root_publication_count = 1;
    CHECK(!gxos_nativeaot_phase56_failure_arm(
              GXOS_NATIVEAOT_FAILURE_INJECTION_AFTER_GC_ROOT_SURVIVAL,
              &lifecycle));
    record = gxos_nativeaot_phase56_failure_record();
    CHECK(record != 0 &&
          record->point == GXOS_NATIVEAOT_FAILURE_INJECTION_NONE &&
          record->state == GXOS_NATIVEAOT_FAILURE_INJECTION_DISARMED &&
          record->fire_count == 0);

    /* Root ownership cannot be released by a stale token or a stale
       lifecycle context, even when the post-GC bit is present. */
    CHECK(!gxos_nativeaot_scheduler_worker_note_managed_root_released(
              &lifecycle, 0x5B01));
    CHECK(lifecycle.managed_root_owned == 1 &&
          lifecycle.managed_root_release_count == 0);
    CHECK(!gxos_nativeaot_scheduler_worker_note_managed_root_released(
              &lifecycle, 0x5A01));
    CHECK(lifecycle.managed_root_owned == 1);
    CHECK(!gxos_nativeaot_scheduler_worker_detach(&lifecycle));

    /* The generic record is single-shot and cannot be fired without a
       complete generation-matched live worker. */
    CHECK(!gxos_nativeaot_phase56_failure_try_fire(&lifecycle));
    CHECK(!gxos_nativeaot_phase56_failure_arm(
              GXOS_NATIVEAOT_FAILURE_INJECTION_AFTER_MANAGED_ROOT,
              &lifecycle));

    (void)printf("PHASE59_POSTGC_ROLLBACK_HOST_TEST=PASS\n");
    return g_failures == 0 ? 0 : 1;
}

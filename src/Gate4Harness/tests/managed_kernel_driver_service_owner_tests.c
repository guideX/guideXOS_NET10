#include "../managed_kernel_driver_service_owner.h"

#include <stdio.h>

static uint32_t g_failures;

static void expect(int condition, const char *message)
{
    if (!condition) {
        ++g_failures;
        printf("FAIL: %s\n", message);
    }
}

int main(void)
{
    int owner_a = 0;
    int owner_b = 0;
    GXOS_MANAGED_KERNEL_DRIVER_SERVICE_HANDLE first = {0};
    GXOS_MANAGED_KERNEL_DRIVER_SERVICE_HANDLE second = {0};
    GXOS_MANAGED_KERNEL_DRIVER_SERVICE_HANDLE stale;
    GXOS_MANAGED_KERNEL_DRIVER_SERVICE_HANDLE changed;

    expect(gxos_managed_kernel_driver_owner_claim(&owner_a) ==
               GXOS_MANAGED_KERNEL_DRIVER_OWNER_OK,
           "first bounded service slot can be claimed");
    expect(gxos_managed_kernel_driver_owner_claim(&owner_b) ==
               GXOS_MANAGED_KERNEL_DRIVER_OWNER_CAPACITY,
           "a second persistent service is rejected at capacity one");
    expect(!gxos_managed_kernel_driver_owner_publish(
               &owner_b, 1U, &second) && second.identity == 0U,
           "unowned context cannot publish an identity");
    expect(gxos_managed_kernel_driver_owner_publish(
               &owner_a, 1U, &first) && first.slot == 0U &&
               first.identity != 0U && first.generation != 0U &&
               first.device_identity == 1U,
           "owner publishes a value-only slot identity");
    expect(gxos_managed_kernel_driver_owner_is_current(&owner_a, first),
           "current service handle validates");

    changed = first;
    ++changed.slot;
    expect(!gxos_managed_kernel_driver_owner_is_current(&owner_a, changed),
           "wrong owner slot is rejected");
    changed = first;
    ++changed.identity;
    expect(!gxos_managed_kernel_driver_owner_is_current(&owner_a, changed),
           "wrong service identity is rejected");
    changed = first;
    ++changed.generation;
    expect(!gxos_managed_kernel_driver_owner_is_current(&owner_a, changed),
           "wrong service generation is rejected");
    changed = first;
    ++changed.device_identity;
    expect(!gxos_managed_kernel_driver_owner_is_current(&owner_a, changed),
           "wrong device identity is rejected");
    expect(!gxos_managed_kernel_driver_owner_is_current(&owner_b, first),
           "handle cannot be replayed by another owner context");

    stale = first;
    expect(gxos_managed_kernel_driver_owner_release(&owner_a),
           "owner releases its service slot");
    expect(!gxos_managed_kernel_driver_owner_is_current(&owner_a, stale),
           "released handle becomes stale");
    expect(!gxos_managed_kernel_driver_owner_release(&owner_a),
           "duplicate release is rejected");
    expect(gxos_managed_kernel_driver_owner_claim(&owner_b) ==
               GXOS_MANAGED_KERNEL_DRIVER_OWNER_OK &&
               gxos_managed_kernel_driver_owner_publish(
                   &owner_b, 1U, &second),
           "same service slot can be reused after release");
    expect(second.slot == first.slot && second.identity != first.identity &&
               second.generation != first.generation &&
               !gxos_managed_kernel_driver_owner_is_current(&owner_b, stale) &&
               gxos_managed_kernel_driver_owner_is_current(&owner_b, second),
           "restart advances identity and generation and rejects stale handle");
    expect(gxos_managed_kernel_driver_owner_restart_budget(&owner_b) == 1U &&
               gxos_managed_kernel_driver_owner_restart_state(&owner_b) ==
                   GXOS_MANAGED_KERNEL_DRIVER_RESTART_NOT_ATTEMPTED,
           "new manual service episode starts with one automatic restart");
    {
        uint32_t budget_before = 0U;
        GXOS_MANAGED_KERNEL_DRIVER_SERVICE_HANDLE restarted = {0};
        GXOS_MANAGED_KERNEL_DRIVER_SERVICE_HANDLE another_failure;
        expect(!gxos_managed_kernel_driver_owner_restart_begin(
                   &owner_b, second,
                   GXOS_MANAGED_KERNEL_DRIVER_RESTART_CAUSE_NONE,
                   &budget_before) &&
                   gxos_managed_kernel_driver_owner_restart_budget(&owner_b) == 1U,
               "non-recoverable and ordinary stop causes do not consume budget");
        expect(gxos_managed_kernel_driver_owner_restart_begin(
                   &owner_b, second,
                   GXOS_MANAGED_KERNEL_DRIVER_RESTART_CAUSE_RECOVERABLE_DISPATCH,
                   &budget_before) && budget_before == 1U &&
                   gxos_managed_kernel_driver_owner_restart_budget(&owner_b) == 0U &&
                   gxos_managed_kernel_driver_owner_restart_state(&owner_b) ==
                       GXOS_MANAGED_KERNEL_DRIVER_RESTART_ATTEMPTED,
               "recoverable running failure consumes exactly one restart");
        expect(gxos_managed_kernel_driver_owner_release(&owner_b) &&
                   gxos_managed_kernel_driver_owner_claim(&owner_b) ==
                       GXOS_MANAGED_KERNEL_DRIVER_OWNER_OK &&
                   gxos_managed_kernel_driver_owner_publish(
                       &owner_b, second.device_identity, &restarted) &&
                   gxos_managed_kernel_driver_owner_restart_budget(&owner_b) == 0U,
               "automatic replacement preserves device and spent budget");
        expect(restarted.slot == second.slot &&
                   restarted.device_identity == second.device_identity &&
                   restarted.identity != second.identity &&
                   restarted.generation != second.generation &&
                   !gxos_managed_kernel_driver_owner_is_current(&owner_b, second) &&
                   gxos_managed_kernel_driver_owner_restart_complete(&owner_b, 1) &&
                   gxos_managed_kernel_driver_owner_restart_state(&owner_b) ==
                       GXOS_MANAGED_KERNEL_DRIVER_RESTART_SUCCEEDED,
               "replacement succeeds in a new generation and invalidates old handle");
        another_failure = restarted;
        expect(!gxos_managed_kernel_driver_owner_restart_begin(
                   &owner_b, another_failure,
                   GXOS_MANAGED_KERNEL_DRIVER_RESTART_CAUSE_RECOVERABLE_DISPATCH,
                   &budget_before) &&
                   gxos_managed_kernel_driver_owner_restart_exhaust(
                       &owner_b, another_failure) &&
                   gxos_managed_kernel_driver_owner_restart_state(&owner_b) ==
                       GXOS_MANAGED_KERNEL_DRIVER_RESTART_EXHAUSTED,
               "second recoverable failure exhausts budget without retry");
        expect(gxos_managed_kernel_driver_owner_release(&owner_b) &&
                   gxos_managed_kernel_driver_owner_claim(&owner_b) ==
                       GXOS_MANAGED_KERNEL_DRIVER_OWNER_OK &&
                   gxos_managed_kernel_driver_owner_restart_budget(&owner_b) == 1U &&
                   gxos_managed_kernel_driver_owner_restart_state(&owner_b) ==
                       GXOS_MANAGED_KERNEL_DRIVER_RESTART_NOT_ATTEMPTED,
               "later explicit manual owner claim starts a fresh bounded episode");
        expect(gxos_managed_kernel_driver_owner_release(&owner_b),
               "fresh manual owner episode releases its slot");
    }

    {
        GXOS_MANAGED_KERNEL_DRIVER_SERVICE_HANDLE failed_episode = {0};
        GXOS_MANAGED_KERNEL_DRIVER_SERVICE_HANDLE failed_admission_handle = {0};
        GXOS_MANAGED_KERNEL_DRIVER_SERVICE_HANDLE manual_episode = {0};
        uint32_t budget_before = 0U;
        expect(gxos_managed_kernel_driver_owner_claim(&owner_a) ==
                   GXOS_MANAGED_KERNEL_DRIVER_OWNER_OK &&
                   gxos_managed_kernel_driver_owner_publish(
                       &owner_a, 1U, &failed_episode),
               "failed-admission episode publishes its running generation");
        expect(gxos_managed_kernel_driver_owner_restart_begin(
                   &owner_a, failed_episode,
                   GXOS_MANAGED_KERNEL_DRIVER_RESTART_CAUSE_RECOVERABLE_DISPATCH,
                   &budget_before) && budget_before == 1U &&
                   gxos_managed_kernel_driver_owner_restart_budget(&owner_a) == 0U,
               "failed replacement attempt consumes the only restart budget");
        expect(gxos_managed_kernel_driver_owner_release(&owner_a) &&
                   gxos_managed_kernel_driver_owner_claim(&owner_a) ==
                       GXOS_MANAGED_KERNEL_DRIVER_OWNER_OK &&
                   failed_admission_handle.identity == 0U &&
                   gxos_managed_kernel_driver_owner_release(&owner_a) &&
                   gxos_managed_kernel_driver_owner_restart_complete(&owner_a, 0),
               "replacement capacity rejection publishes no replacement identity");
        expect(gxos_managed_kernel_driver_owner_restart_budget(&owner_a) == 0U &&
                   gxos_managed_kernel_driver_owner_restart_state(&owner_a) ==
                       GXOS_MANAGED_KERNEL_DRIVER_RESTART_FAILED &&
                   !gxos_managed_kernel_driver_owner_is_current(
                       &owner_a, failed_episode),
               "failed automatic replacement leaves a stopped failed episode");
        expect(!gxos_managed_kernel_driver_owner_restart_begin(
                   &owner_a, failed_episode,
                   GXOS_MANAGED_KERNEL_DRIVER_RESTART_CAUSE_RECOVERABLE_DISPATCH,
                   &budget_before) && budget_before == 0U &&
                   gxos_managed_kernel_driver_owner_restart_budget(&owner_a) == 0U,
               "a second automatic replacement is rejected after admission failure");
        expect(gxos_managed_kernel_driver_owner_claim(&owner_a) ==
                   GXOS_MANAGED_KERNEL_DRIVER_OWNER_OK &&
                   gxos_managed_kernel_driver_owner_restart_budget(&owner_a) == 1U &&
                   gxos_managed_kernel_driver_owner_restart_state(&owner_a) ==
                       GXOS_MANAGED_KERNEL_DRIVER_RESTART_NOT_ATTEMPTED &&
                   gxos_managed_kernel_driver_owner_publish(
                       &owner_a, 1U, &manual_episode) &&
                   manual_episode.identity != failed_episode.identity &&
                   manual_episode.generation != failed_episode.generation &&
                   manual_episode.device_identity == failed_episode.device_identity,
               "explicit manual start creates a new episode and resets budget");
        expect(gxos_managed_kernel_driver_owner_release(&owner_a),
               "manual episode releases the owner slot");
    }

    if (g_failures != 0) {
        printf("MANAGED_KERNEL_DRIVER_SERVICE_OWNER_HOST_TESTS=FAILED failures=%u\n",
               g_failures);
        return 1;
    }
    printf("MANAGED_KERNEL_DRIVER_SERVICE_OWNER_HOST_TESTS=PASSED\n");
    return 0;
}

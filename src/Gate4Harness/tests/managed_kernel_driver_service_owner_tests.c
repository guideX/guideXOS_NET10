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
    expect(gxos_managed_kernel_driver_owner_release(&owner_b),
           "replacement releases its service slot");

    if (g_failures != 0) {
        printf("MANAGED_KERNEL_DRIVER_SERVICE_OWNER_HOST_TESTS=FAILED failures=%u\n",
               g_failures);
        return 1;
    }
    printf("MANAGED_KERNEL_DRIVER_SERVICE_OWNER_HOST_TESTS=PASSED\n");
    return 0;
}

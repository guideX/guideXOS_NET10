#ifndef GXOS_MANAGED_KERNEL_DRIVER_SERVICE_OWNER_H
#define GXOS_MANAGED_KERNEL_DRIVER_SERVICE_OWNER_H

#include "managed_kernel_driver_worker.h"

enum {
    GXOS_MANAGED_KERNEL_DRIVER_OWNER_OK = 1,
    GXOS_MANAGED_KERNEL_DRIVER_OWNER_CAPACITY = 2,
    GXOS_MANAGED_KERNEL_DRIVER_OWNER_INVALID = 3
};

typedef enum GXOS_MANAGED_KERNEL_DRIVER_RESTART_CAUSE {
    GXOS_MANAGED_KERNEL_DRIVER_RESTART_CAUSE_NONE = 0,
    GXOS_MANAGED_KERNEL_DRIVER_RESTART_CAUSE_RECOVERABLE_DISPATCH = 1
} GXOS_MANAGED_KERNEL_DRIVER_RESTART_CAUSE;

typedef enum GXOS_MANAGED_KERNEL_DRIVER_RESTART_STATE {
    GXOS_MANAGED_KERNEL_DRIVER_RESTART_NOT_ATTEMPTED = 0,
    GXOS_MANAGED_KERNEL_DRIVER_RESTART_ATTEMPTED = 1,
    GXOS_MANAGED_KERNEL_DRIVER_RESTART_SUCCEEDED = 2,
    GXOS_MANAGED_KERNEL_DRIVER_RESTART_EXHAUSTED = 3,
    GXOS_MANAGED_KERNEL_DRIVER_RESTART_FAILED = 4
} GXOS_MANAGED_KERNEL_DRIVER_RESTART_STATE;

#define GXOS_MANAGED_KERNEL_DRIVER_AUTOMATIC_RESTART_BUDGET 1U

/* Internal singleton-slot operations. The context pointer never leaves this
   native subsystem; callers receive only the value-only service handle. */
int gxos_managed_kernel_driver_owner_claim(const void *owner_context);
int gxos_managed_kernel_driver_owner_publish(
    const void *owner_context, uint32_t device_identity,
    GXOS_MANAGED_KERNEL_DRIVER_SERVICE_HANDLE *handle_out);
int gxos_managed_kernel_driver_owner_is_current(
    const void *owner_context,
    GXOS_MANAGED_KERNEL_DRIVER_SERVICE_HANDLE handle);
int gxos_managed_kernel_driver_owner_release(const void *owner_context);
int gxos_managed_kernel_driver_owner_restart_begin(
    const void *owner_context,
    GXOS_MANAGED_KERNEL_DRIVER_SERVICE_HANDLE handle,
    GXOS_MANAGED_KERNEL_DRIVER_RESTART_CAUSE cause,
    uint32_t *budget_before_out);
int gxos_managed_kernel_driver_owner_restart_complete(
    const void *owner_context, int succeeded);
int gxos_managed_kernel_driver_owner_restart_exhaust(
    const void *owner_context,
    GXOS_MANAGED_KERNEL_DRIVER_SERVICE_HANDLE handle);
uint32_t gxos_managed_kernel_driver_owner_restart_budget(
    const void *owner_context);
GXOS_MANAGED_KERNEL_DRIVER_RESTART_STATE
gxos_managed_kernel_driver_owner_restart_state(const void *owner_context);

#endif

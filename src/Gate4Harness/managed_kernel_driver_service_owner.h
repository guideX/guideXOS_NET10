#ifndef GXOS_MANAGED_KERNEL_DRIVER_SERVICE_OWNER_H
#define GXOS_MANAGED_KERNEL_DRIVER_SERVICE_OWNER_H

#include "managed_kernel_driver_worker.h"

enum {
    GXOS_MANAGED_KERNEL_DRIVER_OWNER_OK = 1,
    GXOS_MANAGED_KERNEL_DRIVER_OWNER_CAPACITY = 2,
    GXOS_MANAGED_KERNEL_DRIVER_OWNER_INVALID = 3
};

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

#endif

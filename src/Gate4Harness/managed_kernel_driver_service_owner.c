#include "managed_kernel_driver_service_owner.h"

typedef struct GXOS_MANAGED_KERNEL_DRIVER_OWNER_SLOT {
    const void *owner_context;
    GXOS_MANAGED_KERNEL_DRIVER_SERVICE_HANDLE handle;
    uint32_t last_identity;
    uint16_t last_generation;
} GXOS_MANAGED_KERNEL_DRIVER_OWNER_SLOT;

static GXOS_MANAGED_KERNEL_DRIVER_OWNER_SLOT g_owner_slot;

static uint32_t next_identity(uint32_t previous)
{
    uint32_t next = previous + 1U;
    return next == 0U ? 1U : next;
}

static uint16_t next_generation(uint16_t previous)
{
    uint16_t next = (uint16_t)(previous + 1U);
    return next == 0U ? 1U : next;
}

int gxos_managed_kernel_driver_owner_claim(const void *owner_context)
{
    if (owner_context == 0) return GXOS_MANAGED_KERNEL_DRIVER_OWNER_INVALID;
    if (g_owner_slot.owner_context != 0) {
        return GXOS_MANAGED_KERNEL_DRIVER_OWNER_CAPACITY;
    }
    g_owner_slot.owner_context = owner_context;
    g_owner_slot.handle = (GXOS_MANAGED_KERNEL_DRIVER_SERVICE_HANDLE){0};
    return GXOS_MANAGED_KERNEL_DRIVER_OWNER_OK;
}

int gxos_managed_kernel_driver_owner_publish(
    const void *owner_context, uint32_t device_identity,
    GXOS_MANAGED_KERNEL_DRIVER_SERVICE_HANDLE *handle_out)
{
    GXOS_MANAGED_KERNEL_DRIVER_SERVICE_HANDLE handle = {0};
    if (handle_out != 0) *handle_out = handle;
    if (owner_context == 0 || device_identity == 0 || handle_out == 0 ||
        g_owner_slot.owner_context != owner_context ||
        g_owner_slot.handle.identity != 0U) {
        return 0;
    }
    g_owner_slot.last_identity = next_identity(g_owner_slot.last_identity);
    g_owner_slot.last_generation =
        next_generation(g_owner_slot.last_generation);
    handle.identity = g_owner_slot.last_identity;
    handle.generation = g_owner_slot.last_generation;
    handle.device_identity = device_identity;
    handle.slot = 0U;
    g_owner_slot.handle = handle;
    *handle_out = handle;
    return 1;
}

int gxos_managed_kernel_driver_owner_is_current(
    const void *owner_context,
    GXOS_MANAGED_KERNEL_DRIVER_SERVICE_HANDLE handle)
{
    return owner_context != 0 && g_owner_slot.owner_context == owner_context &&
           handle.identity != 0U && handle.generation != 0U &&
           handle.device_identity != 0U && handle.slot == 0U &&
           handle.identity == g_owner_slot.handle.identity &&
           handle.generation == g_owner_slot.handle.generation &&
           handle.device_identity == g_owner_slot.handle.device_identity &&
           handle.slot == g_owner_slot.handle.slot;
}

int gxos_managed_kernel_driver_owner_release(const void *owner_context)
{
    if (owner_context == 0 || g_owner_slot.owner_context != owner_context) {
        return 0;
    }
    g_owner_slot.owner_context = 0;
    g_owner_slot.handle = (GXOS_MANAGED_KERNEL_DRIVER_SERVICE_HANDLE){0};
    return 1;
}

#include "managed_kernel_driver_service_owner.h"

typedef struct GXOS_MANAGED_KERNEL_DRIVER_OWNER_SLOT {
    const void *owner_context;
    GXOS_MANAGED_KERNEL_DRIVER_SERVICE_HANDLE handle;
    uint32_t last_identity;
    uint16_t last_generation;
    const void *restart_owner_context;
    uint32_t restart_budget_remaining;
    GXOS_MANAGED_KERNEL_DRIVER_RESTART_STATE restart_state;
    uint32_t restart_in_progress;
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
    if (g_owner_slot.restart_in_progress == 0U ||
        g_owner_slot.restart_owner_context != owner_context) {
        g_owner_slot.restart_owner_context = owner_context;
        g_owner_slot.restart_budget_remaining =
            GXOS_MANAGED_KERNEL_DRIVER_AUTOMATIC_RESTART_BUDGET;
        g_owner_slot.restart_state =
            GXOS_MANAGED_KERNEL_DRIVER_RESTART_NOT_ATTEMPTED;
        g_owner_slot.restart_in_progress = 0U;
    }
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

int gxos_managed_kernel_driver_owner_restart_begin(
    const void *owner_context,
    GXOS_MANAGED_KERNEL_DRIVER_SERVICE_HANDLE handle,
    GXOS_MANAGED_KERNEL_DRIVER_RESTART_CAUSE cause,
    uint32_t *budget_before_out)
{
    if (budget_before_out != 0) *budget_before_out = 0U;
    if (cause != GXOS_MANAGED_KERNEL_DRIVER_RESTART_CAUSE_RECOVERABLE_DISPATCH ||
        !gxos_managed_kernel_driver_owner_is_current(owner_context, handle) ||
        g_owner_slot.restart_owner_context != owner_context ||
        g_owner_slot.restart_in_progress != 0U ||
        g_owner_slot.restart_budget_remaining == 0U ||
        g_owner_slot.restart_state !=
            GXOS_MANAGED_KERNEL_DRIVER_RESTART_NOT_ATTEMPTED) {
        return 0;
    }
    if (budget_before_out != 0) {
        *budget_before_out = g_owner_slot.restart_budget_remaining;
    }
    --g_owner_slot.restart_budget_remaining;
    g_owner_slot.restart_state = GXOS_MANAGED_KERNEL_DRIVER_RESTART_ATTEMPTED;
    g_owner_slot.restart_in_progress = 1U;
    return 1;
}

int gxos_managed_kernel_driver_owner_restart_complete(
    const void *owner_context, int succeeded)
{
    if (owner_context == 0 ||
        g_owner_slot.restart_owner_context != owner_context ||
        g_owner_slot.restart_in_progress == 0U ||
        g_owner_slot.restart_state !=
            GXOS_MANAGED_KERNEL_DRIVER_RESTART_ATTEMPTED) {
        return 0;
    }
    g_owner_slot.restart_state = succeeded
        ? GXOS_MANAGED_KERNEL_DRIVER_RESTART_SUCCEEDED
        : GXOS_MANAGED_KERNEL_DRIVER_RESTART_FAILED;
    g_owner_slot.restart_in_progress = 0U;
    return 1;
}

int gxos_managed_kernel_driver_owner_restart_exhaust(
    const void *owner_context,
    GXOS_MANAGED_KERNEL_DRIVER_SERVICE_HANDLE handle)
{
    if (!gxos_managed_kernel_driver_owner_is_current(owner_context, handle) ||
        g_owner_slot.restart_owner_context != owner_context ||
        g_owner_slot.restart_in_progress != 0U ||
        g_owner_slot.restart_budget_remaining != 0U ||
        (g_owner_slot.restart_state !=
             GXOS_MANAGED_KERNEL_DRIVER_RESTART_SUCCEEDED &&
         g_owner_slot.restart_state !=
             GXOS_MANAGED_KERNEL_DRIVER_RESTART_FAILED)) {
        return 0;
    }
    g_owner_slot.restart_state = GXOS_MANAGED_KERNEL_DRIVER_RESTART_EXHAUSTED;
    return 1;
}

uint32_t gxos_managed_kernel_driver_owner_restart_budget(
    const void *owner_context)
{
    return owner_context != 0 &&
           g_owner_slot.restart_owner_context == owner_context
        ? g_owner_slot.restart_budget_remaining : 0U;
}

GXOS_MANAGED_KERNEL_DRIVER_RESTART_STATE
gxos_managed_kernel_driver_owner_restart_state(const void *owner_context)
{
    return owner_context != 0 &&
           g_owner_slot.restart_owner_context == owner_context
        ? g_owner_slot.restart_state
        : GXOS_MANAGED_KERNEL_DRIVER_RESTART_NOT_ATTEMPTED;
}

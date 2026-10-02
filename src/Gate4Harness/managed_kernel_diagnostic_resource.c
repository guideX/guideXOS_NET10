#include "managed_kernel_diagnostic_resource.h"

#include <stddef.h>

#define GXOS_DIAGNOSTIC_UART_OWNER 0x47584449U
#define GXOS_DIAGNOSTIC_UART_KNOWN_OWNERSHIP_FLAGS \
    (GXOS_DIAGNOSTIC_UART_OWNERSHIP_EXCLUSIVE | \
     GXOS_DIAGNOSTIC_UART_OWNERSHIP_DIAGNOSTIC_ONLY)

static int ranges_overlap(uint64_t left_base, uint64_t left_length,
                          uint64_t right_base, uint64_t right_length)
{
    uint64_t left_end;
    uint64_t right_end;
    if (left_length == 0U || right_length == 0U ||
        left_base > UINT64_MAX - left_length ||
        right_base > UINT64_MAX - right_length) return 0;
    left_end = left_base + left_length;
    right_end = right_base + right_length;
    return left_base < right_end && right_base < left_end;
}

GXOS_DIAGNOSTIC_RESOURCE_QUERY_RESULT
gxos_managed_kernel_diagnostic_uart_validate(
    const GXOS_MANAGED_KERNEL_DIAGNOSTIC_UART_RESOURCE *resource)
{
    if (resource == 0) return GXOS_DIAGNOSTIC_RESOURCE_QUERY_INVALID;
    if (resource->present == 0U) {
        return GXOS_DIAGNOSTIC_RESOURCE_QUERY_UNAVAILABLE;
    }
    if (resource->present != 1U || resource->enabled != 1U ||
        resource->source < GXOS_DIAGNOSTIC_UART_SOURCE_QEMU_PLATFORM ||
        resource->source > GXOS_DIAGNOSTIC_UART_SOURCE_FIRMWARE_DESCRIPTOR ||
        resource->backend != GXOS_DIAGNOSTIC_UART_BACKEND_IO_16550 ||
        resource->ownership_flags !=
            GXOS_DIAGNOSTIC_UART_KNOWN_OWNERSHIP_FLAGS ||
        resource->io_base == 0U || (resource->io_base & 7U) != 0U ||
        resource->register_span != GXOS_DIAGNOSTIC_UART_REGISTER_SPAN_16550 ||
        resource->io_base > UINT16_MAX -
            (GXOS_DIAGNOSTIC_UART_REGISTER_SPAN_16550 - 1U) ||
        resource->irq >= GXOS_DIAGNOSTIC_UART_IRQ_COUNT ||
        resource->irq == 2U || resource->reserved[0] != 0U ||
        resource->reserved[1] != 0U || resource->reserved[2] != 0U) {
        return GXOS_DIAGNOSTIC_RESOURCE_QUERY_INVALID;
    }
    return GXOS_DIAGNOSTIC_RESOURCE_QUERY_VALID;
}

static void clear_reservation(
    GXOS_MANAGED_KERNEL_DIAGNOSTIC_UART_RESERVATION *reservation)
{
    *reservation = (GXOS_MANAGED_KERNEL_DIAGNOSTIC_UART_RESERVATION){0};
}

GXOS_DIAGNOSTIC_RESOURCE_ACTIVATION_RESULT
gxos_managed_kernel_diagnostic_uart_activate(
    const GXOS_MANAGED_KERNEL_DIAGNOSTIC_UART_RESOURCE *resource,
    uint32_t diagnostic_policy_allowed,
    const GX_MANAGED_KERNEL_DEVICE_RESOURCE_V1 *registered_resources,
    uint32_t registered_resource_count,
    uint32_t exclusive_irq_mask,
    GXOS_MANAGED_KERNEL_DIAGNOSTIC_UART_RESERVATION *reservation,
    const GXOS_MANAGED_KERNEL_DIAGNOSTIC_UART_OPERATIONS *operations,
    void *operation_context,
    GXOS_DIAGNOSTIC_RESOURCE_DISABLE_REASON *reason_out)
{
    uint32_t index;
    uint32_t irq_bit;
    if (reason_out != 0) *reason_out = GXOS_DIAGNOSTIC_RESOURCE_REASON_NONE;
    if (resource != 0 && resource->present == 0U) {
        if (reason_out != 0) *reason_out =
            GXOS_DIAGNOSTIC_RESOURCE_REASON_NO_RESOURCE;
        return GXOS_DIAGNOSTIC_RESOURCE_ACTIVATION_DISABLED;
    }
    if (gxos_managed_kernel_diagnostic_uart_validate(resource) !=
            GXOS_DIAGNOSTIC_RESOURCE_QUERY_VALID) {
        if (reason_out != 0) *reason_out =
            GXOS_DIAGNOSTIC_RESOURCE_REASON_INVALID_RESOURCE;
        return GXOS_DIAGNOSTIC_RESOURCE_ACTIVATION_DISABLED;
    }
    if (diagnostic_policy_allowed == 0U) {
        if (reason_out != 0) *reason_out =
            GXOS_DIAGNOSTIC_RESOURCE_REASON_POLICY_DISABLED;
        return GXOS_DIAGNOSTIC_RESOURCE_ACTIVATION_DISABLED;
    }
    if (registered_resource_count != 0U && registered_resources == 0) {
        if (reason_out != 0) *reason_out =
            GXOS_DIAGNOSTIC_RESOURCE_REASON_RESERVATION_FAILED;
        return GXOS_DIAGNOSTIC_RESOURCE_ACTIVATION_DISABLED;
    }
    if (reservation == 0 || operations == 0 ||
        operations->initialize_uart == 0 || operations->register_irq == 0 ||
        operations->unregister_irq == 0 || operations->stop_uart == 0 ||
        registered_resource_count >
            GX_MANAGED_KERNEL_DEVICE_RESOURCE_MAX_DESCRIPTORS) {
        if (reason_out != 0) *reason_out =
            GXOS_DIAGNOSTIC_RESOURCE_REASON_RESERVATION_FAILED;
        return GXOS_DIAGNOSTIC_RESOURCE_ACTIVATION_DISABLED;
    }
    for (index = 0U; index != registered_resource_count; ++index) {
        const GX_MANAGED_KERNEL_DEVICE_RESOURCE_V1 *candidate =
            &registered_resources[index];
        if (gxos_managed_kernel_validate_resource(candidate) !=
                GXOS_MANAGED_KERNEL_RESOURCE_OK) {
            if (reason_out != 0) *reason_out =
                GXOS_DIAGNOSTIC_RESOURCE_REASON_RESERVATION_FAILED;
            return GXOS_DIAGNOSTIC_RESOURCE_ACTIVATION_DISABLED;
        }
        if (candidate->ResourceType ==
                GX_MANAGED_DEVICE_RESOURCE_TYPE_IO_PORT &&
            ranges_overlap(resource->io_base, resource->register_span,
                           candidate->PhysicalBase, candidate->Length)) {
            if (reason_out != 0) *reason_out =
                GXOS_DIAGNOSTIC_RESOURCE_REASON_IO_CONFLICT;
            return GXOS_DIAGNOSTIC_RESOURCE_ACTIVATION_DISABLED;
        }
        if (candidate->ResourceType ==
                GX_MANAGED_DEVICE_RESOURCE_TYPE_INTERRUPT &&
            ranges_overlap(resource->irq, 1U, candidate->PhysicalBase,
                           candidate->Length)) {
            if (reason_out != 0) *reason_out =
                GXOS_DIAGNOSTIC_RESOURCE_REASON_IRQ_CONFLICT;
            return GXOS_DIAGNOSTIC_RESOURCE_ACTIVATION_DISABLED;
        }
    }
    irq_bit = 1U << resource->irq;
    if ((exclusive_irq_mask & irq_bit) != 0U) {
        if (reason_out != 0) *reason_out =
            GXOS_DIAGNOSTIC_RESOURCE_REASON_IRQ_CONFLICT;
        return GXOS_DIAGNOSTIC_RESOURCE_ACTIVATION_DISABLED;
    }
    if (reservation->owned != 0U) {
        if (reason_out != 0) *reason_out =
            GXOS_DIAGNOSTIC_RESOURCE_REASON_RESERVATION_FAILED;
        return GXOS_DIAGNOSTIC_RESOURCE_ACTIVATION_DISABLED;
    }

    *reservation = (GXOS_MANAGED_KERNEL_DIAGNOSTIC_UART_RESERVATION){
        .owned = 1U,
        .resource_type = GX_MANAGED_DEVICE_RESOURCE_TYPE_IO_PORT,
        .owner = GXOS_DIAGNOSTIC_UART_OWNER,
        .io_base = resource->io_base,
        .register_span = resource->register_span,
        .irq = resource->irq};
    if (!operations->initialize_uart(operation_context, resource)) {
        operations->stop_uart(operation_context, resource);
        clear_reservation(reservation);
        if (reason_out != 0) *reason_out =
            GXOS_DIAGNOSTIC_RESOURCE_REASON_UART_INIT_FAILED;
        return GXOS_DIAGNOSTIC_RESOURCE_ACTIVATION_DISABLED;
    }
    if (!operations->register_irq(operation_context, resource)) {
        operations->unregister_irq(operation_context, resource);
        operations->stop_uart(operation_context, resource);
        clear_reservation(reservation);
        if (reason_out != 0) *reason_out =
            GXOS_DIAGNOSTIC_RESOURCE_REASON_IRQ_REGISTRATION_FAILED;
        return GXOS_DIAGNOSTIC_RESOURCE_ACTIVATION_DISABLED;
    }
    return GXOS_DIAGNOSTIC_RESOURCE_ACTIVATION_ENABLED;
}

void gxos_managed_kernel_diagnostic_uart_release(
    const GXOS_MANAGED_KERNEL_DIAGNOSTIC_UART_RESOURCE *resource,
    GXOS_MANAGED_KERNEL_DIAGNOSTIC_UART_RESERVATION *reservation,
    const GXOS_MANAGED_KERNEL_DIAGNOSTIC_UART_OPERATIONS *operations,
    void *operation_context)
{
    if (reservation == 0 || reservation->owned == 0U) return;
    if (resource != 0 && operations != 0) {
        if (operations->unregister_irq != 0) {
            operations->unregister_irq(operation_context, resource);
        }
        if (operations->stop_uart != 0) {
            operations->stop_uart(operation_context, resource);
        }
    }
    clear_reservation(reservation);
}

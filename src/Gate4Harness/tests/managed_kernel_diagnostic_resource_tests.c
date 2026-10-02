#include <stdio.h>
#include <string.h>

#include "../managed_kernel_diagnostic_resource.h"

typedef struct {
    uint32_t initialize_calls;
    uint32_t register_calls;
    uint32_t unregister_calls;
    uint32_t stop_calls;
    uint32_t fail_initialize;
    uint32_t fail_register;
} FAKE_HARDWARE;

static unsigned failures;

static void expect(int condition, const char *message)
{
    if (condition) return;
    ++failures;
    printf("FAIL: %s\n", message);
}

static int fake_initialize(
    void *opaque,
    const GXOS_MANAGED_KERNEL_DIAGNOSTIC_UART_RESOURCE *resource)
{
    FAKE_HARDWARE *hardware = (FAKE_HARDWARE *)opaque;
    ++hardware->initialize_calls;
    expect(resource->io_base == 0x2F8U && resource->register_span == 8U,
           "validated resource reaches UART initialization");
    return hardware->fail_initialize == 0U;
}

static int fake_register_irq(
    void *opaque,
    const GXOS_MANAGED_KERNEL_DIAGNOSTIC_UART_RESOURCE *resource)
{
    FAKE_HARDWARE *hardware = (FAKE_HARDWARE *)opaque;
    ++hardware->register_calls;
    expect(resource->irq == 3U, "validated IRQ reaches registration");
    return hardware->fail_register == 0U;
}

static void fake_unregister_irq(
    void *opaque,
    const GXOS_MANAGED_KERNEL_DIAGNOSTIC_UART_RESOURCE *resource)
{
    FAKE_HARDWARE *hardware = (FAKE_HARDWARE *)opaque;
    (void)resource;
    ++hardware->unregister_calls;
}

static void fake_stop_uart(
    void *opaque,
    const GXOS_MANAGED_KERNEL_DIAGNOSTIC_UART_RESOURCE *resource)
{
    FAKE_HARDWARE *hardware = (FAKE_HARDWARE *)opaque;
    (void)resource;
    ++hardware->stop_calls;
}

static const GXOS_MANAGED_KERNEL_DIAGNOSTIC_UART_OPERATIONS operations = {
    .initialize_uart = fake_initialize,
    .register_irq = fake_register_irq,
    .unregister_irq = fake_unregister_irq,
    .stop_uart = fake_stop_uart};

static GXOS_MANAGED_KERNEL_DIAGNOSTIC_UART_RESOURCE valid_resource(void)
{
    return (GXOS_MANAGED_KERNEL_DIAGNOSTIC_UART_RESOURCE){
        .present = 1U,
        .enabled = 1U,
        .source = GXOS_DIAGNOSTIC_UART_SOURCE_QEMU_PLATFORM,
        .backend = GXOS_DIAGNOSTIC_UART_BACKEND_IO_16550,
        .ownership_flags = GXOS_DIAGNOSTIC_UART_OWNERSHIP_EXCLUSIVE |
                           GXOS_DIAGNOSTIC_UART_OWNERSHIP_DIAGNOSTIC_ONLY,
        .io_base = 0x2F8U,
        .register_span = 8U,
        .irq = 3U};
}

static GX_MANAGED_KERNEL_DEVICE_RESOURCE_V1 io_resource(
    uint64_t id, uint32_t kind, uint32_t device, uint64_t base,
    uint64_t length)
{
    return (GX_MANAGED_KERNEL_DEVICE_RESOURCE_V1){
        .Size = GX_MANAGED_KERNEL_DEVICE_RESOURCE_V1_SIZE,
        .AbiVersion = GX_MANAGED_KERNEL_DEVICE_RESOURCES_ABI_V1,
        .ResourceId = id,
        .OwnerDeviceKind = kind,
        .OwnerDeviceId = device,
        .ResourceType = GX_MANAGED_DEVICE_RESOURCE_TYPE_IO_PORT,
        .Flags = GX_MANAGED_DEVICE_RESOURCE_FLAG_READABLE |
                 GX_MANAGED_DEVICE_RESOURCE_FLAG_IO_PORT |
                 GX_MANAGED_DEVICE_RESOURCE_FLAG_PLATFORM,
        .PhysicalBase = base,
        .Length = length,
        .Alignment = 1U};
}

static GX_MANAGED_KERNEL_DEVICE_RESOURCE_V1 irq_resource(
    uint64_t id, uint32_t device, uint32_t irq)
{
    return (GX_MANAGED_KERNEL_DEVICE_RESOURCE_V1){
        .Size = GX_MANAGED_KERNEL_DEVICE_RESOURCE_V1_SIZE,
        .AbiVersion = GX_MANAGED_KERNEL_DEVICE_RESOURCES_ABI_V1,
        .ResourceId = id,
        .OwnerDeviceKind = GX_MANAGED_DEVICE_KIND_PLATFORM_SERIAL,
        .OwnerDeviceId = device,
        .ResourceType = GX_MANAGED_DEVICE_RESOURCE_TYPE_INTERRUPT,
        .Flags = GX_MANAGED_DEVICE_RESOURCE_FLAG_PLATFORM,
        .PhysicalBase = irq,
        .Length = 1U,
        .Alignment = 1U};
}

static GXOS_DIAGNOSTIC_RESOURCE_DISABLE_REASON activate(
    const GXOS_MANAGED_KERNEL_DIAGNOSTIC_UART_RESOURCE *resource,
    uint32_t policy_allowed,
    const GX_MANAGED_KERNEL_DEVICE_RESOURCE_V1 *registered,
    uint32_t registered_count,
    uint32_t exclusive_irq_mask,
    GXOS_MANAGED_KERNEL_DIAGNOSTIC_UART_RESERVATION *reservation,
    FAKE_HARDWARE *hardware,
    GXOS_DIAGNOSTIC_RESOURCE_ACTIVATION_RESULT *result)
{
    GXOS_DIAGNOSTIC_RESOURCE_DISABLE_REASON reason =
        GXOS_DIAGNOSTIC_RESOURCE_REASON_NONE;
    *result = gxos_managed_kernel_diagnostic_uart_activate(
        resource, policy_allowed, registered, registered_count,
        exclusive_irq_mask, reservation, &operations, hardware, &reason);
    return reason;
}

static void test_query_and_policy(void)
{
    GXOS_MANAGED_KERNEL_DIAGNOSTIC_UART_RESOURCE resource = valid_resource();
    GXOS_MANAGED_KERNEL_DIAGNOSTIC_UART_RESERVATION reservation = {0};
    GXOS_DIAGNOSTIC_RESOURCE_ACTIVATION_RESULT result;
    GXOS_DIAGNOSTIC_RESOURCE_DISABLE_REASON reason;
    FAKE_HARDWARE hardware = {0};
    resource.present = 0U;
    expect(gxos_managed_kernel_diagnostic_uart_validate(&resource) ==
               GXOS_DIAGNOSTIC_RESOURCE_QUERY_UNAVAILABLE,
           "no-UART provider reports unavailable");
    reason = activate(&resource, 1U, 0, 0U, 0U, &reservation,
                      &hardware, &result);
    expect(result == GXOS_DIAGNOSTIC_RESOURCE_ACTIVATION_DISABLED &&
           reason == GXOS_DIAGNOSTIC_RESOURCE_REASON_NO_RESOURCE &&
           hardware.initialize_calls == 0U && hardware.register_calls == 0U,
           "no resource disables ingress without hardware touch");

    resource = valid_resource();
    reason = activate(&resource, 0U, 0, 0U, 0U, &reservation,
                      &hardware, &result);
    expect(result == GXOS_DIAGNOSTIC_RESOURCE_ACTIVATION_DISABLED &&
           reason == GXOS_DIAGNOSTIC_RESOURCE_REASON_POLICY_DISABLED &&
           hardware.initialize_calls == 0U && hardware.register_calls == 0U,
           "valid resource remains inactive when policy denies diagnostics");
    reason = activate(&resource, 1U, 0, 0U, 0U, &reservation,
                      &hardware, &result);
    expect(result == GXOS_DIAGNOSTIC_RESOURCE_ACTIVATION_ENABLED &&
           reason == GXOS_DIAGNOSTIC_RESOURCE_REASON_NONE &&
           reservation.owned == 1U && reservation.owner != 0U &&
           reservation.io_base == 0x2F8U && reservation.register_span == 8U &&
           reservation.irq == 3U,
           "valid trusted resource reserves range and IRQ before activation");
    gxos_managed_kernel_diagnostic_uart_release(
        &resource, &reservation, &operations, &hardware);
    expect(reservation.owned == 0U && hardware.unregister_calls == 1U &&
           hardware.stop_calls == 1U,
           "release tears down IRQ and UART then frees the claim");
}

static void test_invalid_resources_are_rejected_before_hardware(void)
{
    GXOS_MANAGED_KERNEL_DIAGNOSTIC_UART_RESOURCE resource = valid_resource();
    GXOS_MANAGED_KERNEL_DIAGNOSTIC_UART_RESERVATION reservation = {0};
    GXOS_DIAGNOSTIC_RESOURCE_ACTIVATION_RESULT result;
    GXOS_DIAGNOSTIC_RESOURCE_DISABLE_REASON reason;
    FAKE_HARDWARE hardware = {0};
    resource.io_base = 0U;
    reason = activate(&resource, 1U, 0, 0U, 0U, &reservation,
                      &hardware, &result);
    expect(result == GXOS_DIAGNOSTIC_RESOURCE_ACTIVATION_DISABLED &&
           reason == GXOS_DIAGNOSTIC_RESOURCE_REASON_INVALID_RESOURCE,
           "zero I/O base is rejected");
    resource = valid_resource();
    resource.io_base = 0x2F9U;
    expect(gxos_managed_kernel_diagnostic_uart_validate(&resource) ==
               GXOS_DIAGNOSTIC_RESOURCE_QUERY_INVALID,
           "unaligned I/O base is rejected");
    resource = valid_resource();
    resource.irq = 16U;
    reason = activate(&resource, 1U, 0, 0U, 0U, &reservation,
                      &hardware, &result);
    expect(reason == GXOS_DIAGNOSTIC_RESOURCE_REASON_INVALID_RESOURCE,
           "IRQ outside the legacy route range is rejected");
    resource = valid_resource();
    resource.backend = GXOS_DIAGNOSTIC_UART_BACKEND_UNSUPPORTED;
    reason = activate(&resource, 1U, 0, 0U, 0U, &reservation,
                      &hardware, &result);
    expect(reason == GXOS_DIAGNOSTIC_RESOURCE_REASON_INVALID_RESOURCE,
           "unsupported UART capability is rejected");
    expect(hardware.initialize_calls == 0U && hardware.register_calls == 0U &&
           hardware.stop_calls == 0U && hardware.unregister_calls == 0U &&
           reservation.owned == 0U,
           "rejected descriptors cause no UART or IRQ hardware operation");
}

static void test_io_and_irq_conflicts(void)
{
    GXOS_MANAGED_KERNEL_DIAGNOSTIC_UART_RESOURCE resource = valid_resource();
    GXOS_MANAGED_KERNEL_DIAGNOSTIC_UART_RESERVATION reservation = {0};
    GX_MANAGED_KERNEL_DEVICE_RESOURCE_V1 collision = io_resource(
        1U, GX_MANAGED_DEVICE_KIND_PLATFORM_SERIAL,
        GX_MANAGED_SERIAL_DEVICE_ID_COM1, 0x3F8U, 8U);
    GXOS_DIAGNOSTIC_RESOURCE_ACTIVATION_RESULT result;
    GXOS_DIAGNOSTIC_RESOURCE_DISABLE_REASON reason;
    FAKE_HARDWARE hardware = {0};
    resource.io_base = 0x3F8U;
    reason = activate(&resource, 1U, &collision, 1U, 0U, &reservation,
                      &hardware, &result);
    expect(reason == GXOS_DIAGNOSTIC_RESOURCE_REASON_IO_CONFLICT,
           "exact COM1 I/O collision rejects diagnostic claim");
    resource = valid_resource();
    collision.PhysicalBase = 0x2FCU;
    collision.Length = 4U;
    reason = activate(&resource, 1U, &collision, 1U, 0U, &reservation,
                      &hardware, &result);
    expect(reason == GXOS_DIAGNOSTIC_RESOURCE_REASON_IO_CONFLICT,
           "partial register-range overlap is rejected");
    collision = irq_resource(2U, GX_MANAGED_SERIAL_DEVICE_ID_COM1, 3U);
    reason = activate(&resource, 1U, &collision, 1U, 0U, &reservation,
                      &hardware, &result);
    expect(reason == GXOS_DIAGNOSTIC_RESOURCE_REASON_IRQ_CONFLICT,
           "registered exclusive IRQ resource rejects diagnostic claim");
    resource.irq = 4U;
    reason = activate(&resource, 1U, 0, 0U, 1U << 4, &reservation,
                      &hardware, &result);
    expect(reason == GXOS_DIAGNOSTIC_RESOURCE_REASON_IRQ_CONFLICT,
           "COM1 IRQ rejects diagnostic claim");
    resource = valid_resource();
    reason = activate(&resource, 1U, 0, 0U, 1U << 3, &reservation,
                      &hardware, &result);
    expect(reason == GXOS_DIAGNOSTIC_RESOURCE_REASON_IRQ_CONFLICT,
           "platform exclusive IRQ route rejects diagnostic claim");
    expect(hardware.initialize_calls == 0U && hardware.register_calls == 0U &&
           reservation.owned == 0U,
           "I/O and IRQ conflicts are rejected before hardware operations");
}

static void test_duplicate_claim_and_rollback(void)
{
    GXOS_MANAGED_KERNEL_DIAGNOSTIC_UART_RESOURCE resource = valid_resource();
    GXOS_MANAGED_KERNEL_DIAGNOSTIC_UART_RESERVATION reservation = {0};
    GXOS_DIAGNOSTIC_RESOURCE_ACTIVATION_RESULT result;
    GXOS_DIAGNOSTIC_RESOURCE_DISABLE_REASON reason;
    FAKE_HARDWARE hardware = {0};
    reason = activate(&resource, 1U, 0, 0U, 0U, &reservation,
                      &hardware, &result);
    expect(result == GXOS_DIAGNOSTIC_RESOURCE_ACTIVATION_ENABLED,
           "first claim enables one diagnostic ingress");
    reason = activate(&resource, 1U, 0, 0U, 0U, &reservation,
                      &hardware, &result);
    expect(result == GXOS_DIAGNOSTIC_RESOURCE_ACTIVATION_DISABLED &&
           reason == GXOS_DIAGNOSTIC_RESOURCE_REASON_RESERVATION_FAILED &&
           hardware.initialize_calls == 1U && hardware.register_calls == 1U,
           "duplicate claim is rejected without reinitialization or IRQ registration");
    gxos_managed_kernel_diagnostic_uart_release(
        &resource, &reservation, &operations, &hardware);

    reservation = (GXOS_MANAGED_KERNEL_DIAGNOSTIC_UART_RESERVATION){0};
    hardware = (FAKE_HARDWARE){.fail_initialize = 1U};
    reason = activate(&resource, 1U, 0, 0U, 0U, &reservation,
                      &hardware, &result);
    expect(reason == GXOS_DIAGNOSTIC_RESOURCE_REASON_UART_INIT_FAILED &&
           reservation.owned == 0U && hardware.register_calls == 0U &&
           hardware.stop_calls == 1U,
           "UART initialization failure rolls back the reservation");

    reservation = (GXOS_MANAGED_KERNEL_DIAGNOSTIC_UART_RESERVATION){0};
    hardware = (FAKE_HARDWARE){.fail_register = 1U};
    reason = activate(&resource, 1U, 0, 0U, 0U, &reservation,
                      &hardware, &result);
    expect(reason == GXOS_DIAGNOSTIC_RESOURCE_REASON_IRQ_REGISTRATION_FAILED &&
           reservation.owned == 0U && hardware.unregister_calls == 1U &&
           hardware.stop_calls == 1U,
           "IRQ registration failure unregisters and releases the UART claim");
}

int main(void)
{
    test_query_and_policy();
    test_invalid_resources_are_rejected_before_hardware();
    test_io_and_irq_conflicts();
    test_duplicate_claim_and_rollback();
    if (failures != 0U) {
        printf("MANAGED_KERNEL_DIAGNOSTIC_RESOURCE_HOST_TESTS=FAILED failures=%u\n",
               failures);
        return 1;
    }
    printf("MANAGED_KERNEL_DIAGNOSTIC_RESOURCE_HOST_TESTS=PASSED\n");
    return 0;
}

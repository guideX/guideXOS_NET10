#include <stdint.h>
#include <stdio.h>
#include <string.h>

#include "../managed_kernel_interrupt.h"

static uint32_t g_failures;
static uint8_t g_source[32];
static uint32_t g_source_length;
static uint32_t g_source_index;
static uint64_t g_token;
static GX_MANAGED_KERNEL_INTERRUPT_EVENT_V1 g_events[4];
static GX_MANAGED_KERNEL_INTERRUPT_STATS_V1 g_stats;
static uint32_t g_drained;
static uint32_t g_enable_calls;
static uint32_t g_disable_calls;
static uint32_t g_eoi_calls;

static void expect(int condition, const char *message)
{
    if (!condition) {
        ++g_failures;
        printf("FAIL: %s\n", message);
    }
}

static int range_is_known(void *context, uintptr_t address, uintptr_t length)
{
    uintptr_t begin;
    uintptr_t end;
    (void)context;
    if (length == 0 || length > UINTPTR_MAX - address) return 0;
    begin = (uintptr_t)g_source;
    end = begin + sizeof(g_source);
    if (address >= begin && address <= end && length <= end - address) {
        return 1;
    }
    begin = (uintptr_t)&g_token;
    end = begin + sizeof(g_token);
    if (address >= begin && address <= end && length <= end - address) {
        return 1;
    }
    begin = (uintptr_t)g_events;
    end = begin + sizeof(g_events);
    if (address >= begin && address <= end && length <= end - address) {
        return 1;
    }
    begin = (uintptr_t)&g_stats;
    end = begin + sizeof(g_stats);
    if (address >= begin && address <= end && length <= end - address) {
        return 1;
    }
    begin = (uintptr_t)&g_drained;
    end = begin + sizeof(g_drained);
    return address >= begin && address <= end && length <= end - address;
}

static uint64_t critical_enter(void *context)
{
    (void)context;
    return 0;
}

static void critical_leave(void *context, uint64_t flags)
{
    (void)context;
    (void)flags;
}

static int enable_hardware(void *context)
{
    (void)context;
    ++g_enable_calls;
    return 1;
}

static int disable_hardware(void *context)
{
    (void)context;
    ++g_disable_calls;
    return 1;
}

static int capture_source(void *context, uint8_t *payload_byte,
                          uint32_t *status)
{
    (void)context;
    if (g_source_index >= g_source_length || payload_byte == 0 ||
        status == 0) return 0;
    *payload_byte = g_source[g_source_index++];
    *status = 0x20U | *payload_byte;
    return 1;
}

static void send_eoi(void *context)
{
    (void)context;
    ++g_eoi_calls;
}

static void set_source(const char *value)
{
    g_source_length = (uint32_t)strlen(value);
    memcpy(g_source, value, g_source_length);
    g_source_index = 0;
}

int main(void)
{
    GXOS_MANAGED_KERNEL_INTERRUPT_CONTEXT context = {0};
    uint32_t result;
    uint64_t initial_irq_count;

    gxos_managed_kernel_interrupt_initialize(
        &context, GX_MANAGED_DEVICE_KIND_PLATFORM_SERIAL,
        GX_MANAGED_SERIAL_DEVICE_ID_COM1,
        GX_MANAGED_INTERRUPT_EVENT_TYPE_SERIAL_RX,
        range_is_known, critical_enter, critical_leave, enable_hardware,
        disable_hardware, capture_source, send_eoi, 0);
    expect(gxos_managed_kernel_interrupt_validate(&context),
           "interrupt context validates with all native hooks");

    set_source("X");
    gxos_managed_kernel_interrupt_capture(&context);
    expect(g_eoi_calls == 1 && context.serial_isr_count == 0 &&
               context.enqueued_count == 0,
           "inactive capture only acknowledges the interrupt");

    result = gxos_managed_kernel_interrupt_subscribe_v1(
        &context, context.event_type, context.device_kind, context.device_id,
        (uintptr_t)&g_token, sizeof(g_token));
    expect(result == GX_MANAGED_OK && g_token != 0 && g_enable_calls == 1 &&
               context.subscription_active != 0 && context.hardware_enabled != 0,
           "subscribe enables hardware and returns a nonzero token");
    expect(gxos_managed_kernel_interrupt_subscribe_v1(
               &context, context.event_type, context.device_kind,
               context.device_id, (uintptr_t)&g_token, sizeof(g_token)) ==
               GX_MANAGED_ALREADY_INITIALIZED,
           "duplicate subscription is rejected");
    expect(gxos_managed_kernel_interrupt_subscribe_v1(
               &context, context.event_type + 1U, context.device_kind,
               context.device_id, (uintptr_t)&g_token, sizeof(g_token)) ==
               GX_MANAGED_INVALID_ARGUMENT,
           "wrong event identity is rejected");

    set_source("RS");
    gxos_managed_kernel_interrupt_capture(&context);
    expect(context.irq_entry_count == 2 && context.serial_isr_count == 2 &&
               context.enqueued_count == 2 && g_eoi_calls == 2,
           "hardware capture accounts for both received bytes");
    result = gxos_managed_kernel_interrupt_drain_v1(
        &context, GX_MANAGED_KERNEL_INTERRUPT_SERVICES_ABI_V1,
        (uintptr_t)g_events, sizeof(g_events), (uintptr_t)&g_drained,
        sizeof(g_drained));
    expect(result == GX_MANAGED_OK && g_drained == 2 &&
               g_events[0].Sequence == 1 && g_events[0].PayloadByte == 'R' &&
               g_events[1].Sequence == 2 && g_events[1].PayloadByte == 'S' &&
               g_events[0].Flags == GX_MANAGED_INTERRUPT_EVENT_FLAG_HARDWARE_CAPTURE,
           "bounded drain preserves ordered hardware event records");

    set_source("123456789012");
    gxos_managed_kernel_interrupt_capture(&context);
    gxos_managed_kernel_interrupt_capture(&context);
    gxos_managed_kernel_interrupt_capture(&context);
    expect(context.enqueued_count == 10 && context.dropped_count == 4,
           "full fixed queue drops without allocation or overwrite");
    memset(g_events, 0, sizeof(g_events));
    g_drained = 0;
    result = gxos_managed_kernel_interrupt_drain_v1(
        &context, GX_MANAGED_KERNEL_INTERRUPT_SERVICES_ABI_V1,
        (uintptr_t)g_events, sizeof(g_events), (uintptr_t)&g_drained,
        sizeof(g_drained));
    expect(result == GX_MANAGED_OK && g_drained == 4 && context.drained_count == 6,
           "drain is capped at the versioned maximum");

    expect(gxos_managed_kernel_interrupt_query_stats_v1(
               &context, GX_MANAGED_KERNEL_INTERRUPT_SERVICES_ABI_V1,
               (uintptr_t)&g_stats, sizeof(g_stats)) == GX_MANAGED_OK &&
               g_stats.QueueCapacity == GX_MANAGED_KERNEL_INTERRUPT_QUEUE_CAPACITY &&
               g_stats.MaxDrain == GX_MANAGED_KERNEL_INTERRUPT_MAX_DRAIN &&
               g_stats.DroppedCount == 4 && g_stats.SubscriptionActive != 0,
           "stats expose bounded queue accounting");
    initial_irq_count = context.irq_entry_count;
    expect(gxos_managed_kernel_interrupt_unsubscribe_v1(&context, g_token) ==
               GX_MANAGED_OK && g_disable_calls == 1 &&
               context.subscription_active == 0 && context.hardware_enabled == 0,
           "unsubscribe disables hardware and clears active state");
    expect(gxos_managed_kernel_interrupt_unsubscribe_v1(&context, g_token) ==
               GX_MANAGED_INVALID_STATE,
           "second unsubscribe is rejected");

    set_source("Z");
    gxos_managed_kernel_interrupt_capture(&context);
    expect(context.irq_entry_count == initial_irq_count + 1 &&
               context.serial_isr_count == 14 && context.enqueued_count == 10 &&
               g_eoi_calls == 6,
           "post-unsubscribe interrupt is acknowledged without delivery");

    if (g_failures != 0) {
        printf("MANAGED_KERNEL_INTERRUPT_NATIVE_HOST_TESTS=FAILED failures=%u\n",
               g_failures);
        return 1;
    }
    printf("MANAGED_KERNEL_INTERRUPT_NATIVE_HOST_TESTS=PASSED\n");
    return 0;
}

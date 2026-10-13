#include <stdint.h>
#include <stdio.h>
#include <string.h>

#include "../managed_kernel_interrupt.h"

static uint32_t g_failures;
static uint8_t g_source[32];
static uint32_t g_source_length;
static uint32_t g_source_index;
static uint64_t g_token;
static GX_MANAGED_KERNEL_INTERRUPT_EVENT_V1 g_events[8];
static GX_MANAGED_KERNEL_INTERRUPT_STATS_V1 g_stats;
static uint32_t g_drained;
static uint32_t g_enable_calls;
static uint32_t g_disable_calls;
static uint32_t g_eoi_calls;
static uint64_t g_secondary_token;

typedef struct {
    const char *bytes;
    uint32_t length;
    uint32_t index;
    uint32_t enable_calls;
    uint32_t disable_calls;
    uint32_t eoi_calls;
} ROUTE_SOURCE_FIXTURE;

static ROUTE_SOURCE_FIXTURE g_secondary_source;

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
    begin = (uintptr_t)&g_secondary_token;
    end = begin + sizeof(g_secondary_token);
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

static int secondary_enable_hardware(void *context)
{
    ROUTE_SOURCE_FIXTURE *fixture = (ROUTE_SOURCE_FIXTURE *)context;
    if (fixture == 0) return 0;
    ++fixture->enable_calls;
    return 1;
}

static int secondary_disable_hardware(void *context)
{
    ROUTE_SOURCE_FIXTURE *fixture = (ROUTE_SOURCE_FIXTURE *)context;
    if (fixture == 0) return 0;
    ++fixture->disable_calls;
    return 1;
}

static int secondary_capture_source(void *context, uint8_t *payload_byte,
                                    uint32_t *status)
{
    ROUTE_SOURCE_FIXTURE *fixture = (ROUTE_SOURCE_FIXTURE *)context;
    uint8_t cause;
    if (fixture == 0 || fixture->bytes == 0 ||
        fixture->index >= fixture->length || payload_byte == 0 ||
        status == 0) return 0;
    cause = (uint8_t)fixture->bytes[fixture->index++];
    if (cause == 'R') cause = 0x80U;
    else if (cause == 'B') cause = 0x81U;
    *payload_byte = cause;
    *status = cause == 0x80U || cause == 0x81U
        ? cause : 0x40U | cause;
    return 1;
}

static void secondary_send_eoi(void *context)
{
    ROUTE_SOURCE_FIXTURE *fixture = (ROUTE_SOURCE_FIXTURE *)context;
    if (fixture != 0) ++fixture->eoi_calls;
}

static void set_secondary_source(const char *bytes)
{
    g_secondary_source.bytes = bytes;
    g_secondary_source.length = (uint32_t)strlen(bytes);
    g_secondary_source.index = 0;
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

    result = gxos_managed_kernel_interrupt_subscribe_v1(
        &context, context.event_type, context.device_kind, context.device_id,
        (uintptr_t)&g_token, sizeof(g_token));
    expect(result == GX_MANAGED_OK, "resubscribe opens a new route lifetime");
    set_source("ABCD");
    gxos_managed_kernel_interrupt_capture(&context);
    {
        uint64_t discarded = UINT64_MAX;
        uint64_t dropped_before = context.dropped_count;
        expect(gxos_managed_kernel_interrupt_begin_service_stop(
                   &context, 0, &discarded) && discarded == 0 &&
                   context.shutdown_discarded_count == 0 &&
                   context.hardware_enabled == 0 &&
                   context.routes[0].accepting_events == 0 &&
                   context.write_index - context.read_index == 4,
               "drain stop closes route and preserves the queued FIFO");
        set_source("Z");
        gxos_managed_kernel_interrupt_capture(&context);
        expect(context.enqueued_count == 14 &&
                   context.dropped_count == dropped_before &&
                   gxos_managed_kernel_interrupt_unsubscribe_v1(
                       &context, g_token) == GX_MANAGED_OK,
               "drain gate rejects later delivery without overflow accounting");
        memset(g_events, 0, sizeof(g_events));
        g_drained = 0;
        result = gxos_managed_kernel_interrupt_drain_v1(
            &context, GX_MANAGED_KERNEL_INTERRUPT_SERVICES_ABI_V1,
            (uintptr_t)g_events, sizeof(g_events), (uintptr_t)&g_drained,
            sizeof(g_drained));
        expect(result == GX_MANAGED_OK && g_drained == 4 &&
                   g_events[0].PayloadByte == 'A' &&
                   g_events[1].PayloadByte == 'B' &&
                   g_events[2].PayloadByte == 'C' &&
                   g_events[3].PayloadByte == 'D' &&
                   context.write_index == context.read_index,
               "drain after route disable preserves all queued records in order");
    }

    result = gxos_managed_kernel_interrupt_subscribe_v1(
        &context, context.event_type, context.device_kind, context.device_id,
        (uintptr_t)&g_token, sizeof(g_token));
    expect(result == GX_MANAGED_OK, "route can be reused after drained shutdown");
    set_source("WXYZ");
    gxos_managed_kernel_interrupt_capture(&context);
    {
        uint64_t discarded = 0;
        uint64_t dropped_before = context.dropped_count;
        expect(gxos_managed_kernel_interrupt_begin_service_stop(
                   &context, 1, &discarded) && discarded == 4 &&
                   context.shutdown_discarded_count == 4 &&
                   context.dropped_count == dropped_before &&
                   context.write_index == context.read_index &&
                   context.work_pending == 0,
               "discard stop clears queued records with separate accounting");
        expect(gxos_managed_kernel_interrupt_unsubscribe_v1(
                   &context, g_token) == GX_MANAGED_OK,
               "managed unsubscribe completes discard shutdown");
        result = gxos_managed_kernel_interrupt_query_stats_v1(
            &context, GX_MANAGED_KERNEL_INTERRUPT_SERVICES_ABI_V1,
            (uintptr_t)&g_stats, sizeof(g_stats));
        expect(result == GX_MANAGED_OK &&
                   g_stats.DroppedCount == dropped_before &&
                   context.shutdown_discarded_count == 4,
               "overflow and shutdown discard counts remain distinct");
    }

    result = gxos_managed_kernel_interrupt_subscribe_v1(
        &context, context.event_type, context.device_kind, context.device_id,
        (uintptr_t)&g_token, sizeof(g_token));
    expect(result == GX_MANAGED_OK, "route can be reused for failure recovery");
    set_source("PQ");
    gxos_managed_kernel_interrupt_capture(&context);
    {
        uint64_t recovery_discarded = UINT64_MAX;
        uint64_t dropped_before = context.dropped_count;
        expect(gxos_managed_kernel_interrupt_begin_service_failure(
                   &context, &recovery_discarded) &&
                   recovery_discarded == 2U &&
                   context.recovery_discarded_count == 2U &&
                   context.shutdown_discarded_count == 4U &&
                   context.dropped_count == dropped_before &&
                   context.read_index == context.write_index &&
                   context.work_pending == 0U &&
                   context.hardware_enabled == 0U &&
                   context.routes[0].accepting_events == 0U,
               "running failure closes route and accounts queue as recovery loss");
        expect(gxos_managed_kernel_interrupt_unsubscribe_v1(
                   &context, g_token) == GX_MANAGED_OK &&
                   context.recovery_discarded_count == 2U &&
                   context.shutdown_discarded_count == 4U,
               "failed generation unsubscribe does not double count shutdown loss");
    }

    {
        uint64_t first_token;
        uint64_t replacement_token;
        uint64_t enqueued_before = context.enqueued_count;
        uint32_t eoi_before = g_secondary_source.eoi_calls;
        expect(gxos_managed_kernel_interrupt_add_route(
                   &context, 1U, GX_MANAGED_DEVICE_KIND_PLATFORM_KEYBOARD,
                   GX_MANAGED_KEYBOARD_DEVICE_ID_I8042,
                   GX_MANAGED_INTERRUPT_EVENT_TYPE_KEYBOARD_SCANCODE,
                   enable_hardware, disable_hardware, capture_source, send_eoi,
                   0), "keyboard route occupies the production route-table slot");
        expect(gxos_managed_kernel_interrupt_add_route(
                   &context, 2U, GX_MANAGED_DEVICE_KIND_PCI,
                   0x808610D3U, GX_MANAGED_INTERRUPT_EVENT_TYPE_E1000_CAUSE,
                   secondary_enable_hardware, secondary_disable_hardware,
                   secondary_capture_source, secondary_send_eoi,
                   &g_secondary_source),
               "E1000 PCI cause route registers independently at route slot 2");
        set_source("G");
        set_secondary_source("G");
        gxos_managed_kernel_interrupt_capture_route(&context, 2U);
        expect(g_secondary_source.eoi_calls == eoi_before + 1U &&
                   g_source_index == 0U &&
                   context.enqueued_count == enqueued_before,
               "unbound E1000 IRQ is acknowledged without source read or callback");

        result = gxos_managed_kernel_interrupt_subscribe_input_v1(
            &context, GX_MANAGED_INTERRUPT_EVENT_TYPE_E1000_CAUSE,
            GX_MANAGED_DEVICE_KIND_PCI, 0x808610D3U,
            (uintptr_t)&g_token, sizeof(g_token));
        first_token = g_token;
        set_secondary_source("E");
        gxos_managed_kernel_interrupt_capture_route(&context, 2U);
        g_drained = 0;
        result = result == GX_MANAGED_OK
            ? gxos_managed_kernel_interrupt_drain_v1(
                  &context, GX_MANAGED_KERNEL_INTERRUPT_SERVICES_ABI_V1,
                  (uintptr_t)g_events, sizeof(g_events),
                  (uintptr_t)&g_drained, sizeof(g_drained))
            : result;
        expect(result == GX_MANAGED_OK && g_drained == 1U &&
                   g_events[0].EventType ==
                       GX_MANAGED_INTERRUPT_EVENT_TYPE_E1000_CAUSE &&
                   g_events[0].DeviceId == 0x808610D3U &&
                   g_events[0].PayloadByte == 'E',
               "bound E1000 PCI subscription receives its own typed record");
        eoi_before = g_secondary_source.eoi_calls;
        set_secondary_source("R");
        gxos_managed_kernel_interrupt_capture_route(&context, 2U);
        g_drained = 0;
        result = gxos_managed_kernel_interrupt_drain_v1(
            &context, GX_MANAGED_KERNEL_INTERRUPT_SERVICES_ABI_V1,
            (uintptr_t)g_events, sizeof(g_events),
            (uintptr_t)&g_drained, sizeof(g_drained));
        expect(result == GX_MANAGED_OK && g_drained == 1U &&
                   g_events[0].PayloadByte == 0x80U &&
                   g_events[0].Status == 0x80U &&
                   g_secondary_source.eoi_calls == eoi_before + 1U,
               "RX-type RXT0 cause is captured, queued, and acknowledged");
        set_secondary_source("B");
        gxos_managed_kernel_interrupt_capture_route(&context, 2U);
        g_drained = 0;
        result = gxos_managed_kernel_interrupt_drain_v1(
            &context, GX_MANAGED_KERNEL_INTERRUPT_SERVICES_ABI_V1,
            (uintptr_t)g_events, sizeof(g_events),
            (uintptr_t)&g_drained, sizeof(g_drained));
        expect(result == GX_MANAGED_OK && g_drained == 1U &&
                   g_events[0].PayloadByte == 0x81U &&
                   g_events[0].Status == 0x81U &&
                   g_secondary_source.eoi_calls == eoi_before + 2U,
               "simultaneous RX/TX cause bits survive native queueing and acknowledgement");
        expect(gxos_managed_kernel_interrupt_unsubscribe_input_v1(
                    &context, first_token) == GX_MANAGED_OK,
                "E1000 generation can be unsubscribed");
        result = gxos_managed_kernel_interrupt_subscribe_input_v1(
            &context, GX_MANAGED_INTERRUPT_EVENT_TYPE_E1000_CAUSE,
            GX_MANAGED_DEVICE_KIND_PCI, 0x808610D3U,
            (uintptr_t)&g_token, sizeof(g_token));
        replacement_token = g_token;
        expect(result == GX_MANAGED_OK && replacement_token != first_token &&
                    gxos_managed_kernel_interrupt_unsubscribe_input_v1(
                        &context, first_token) == GX_MANAGED_NOT_FOUND &&
                    context.routes[2].subscription_active != 0U,
                "stale E1000 generation token cannot unsubscribe its replacement");
        set_secondary_source("F");
        gxos_managed_kernel_interrupt_capture_route(&context, 2U);
        g_drained = 0;
        result = gxos_managed_kernel_interrupt_drain_v1(
            &context, GX_MANAGED_KERNEL_INTERRUPT_SERVICES_ABI_V1,
            (uintptr_t)g_events, sizeof(g_events),
            (uintptr_t)&g_drained, sizeof(g_drained));
        expect(result == GX_MANAGED_OK && g_drained == 1U &&
                   g_events[0].PayloadByte == 'F',
               "replacement E1000 binding receives later event");
        expect(gxos_managed_kernel_interrupt_unsubscribe_input_v1(
                   &context, replacement_token) == GX_MANAGED_OK,
               "replacement E1000 binding unsubscribes cleanly");
    }

    {
        uint64_t serial_token;
        uint64_t e1000_token;
        uint64_t replacement_serial_token;
        uint64_t replacement_e1000_token;
        uint64_t sequence_before;
        uint64_t enqueued_before;
        uint64_t dropped_before;
        uint32_t serial_native_before;
        uint32_t e1000_native_before;
        uint32_t index;

        result = gxos_managed_kernel_interrupt_subscribe_v1(
            &context, GX_MANAGED_INTERRUPT_EVENT_TYPE_SERIAL_RX,
            GX_MANAGED_DEVICE_KIND_PLATFORM_SERIAL,
            GX_MANAGED_SERIAL_DEVICE_ID_COM1,
            (uintptr_t)&g_token, sizeof(g_token));
        serial_token = g_token;
        expect(result == GX_MANAGED_OK && serial_token != 0,
               "COM1 route binds through its exact route-0 identity");
        result = gxos_managed_kernel_interrupt_subscribe_input_v1(
            &context, GX_MANAGED_INTERRUPT_EVENT_TYPE_E1000_CAUSE,
            GX_MANAGED_DEVICE_KIND_PCI, 0x808610D3U,
            (uintptr_t)&g_secondary_token, sizeof(g_secondary_token));
        e1000_token = g_secondary_token;
        expect(result == GX_MANAGED_OK && e1000_token != 0 &&
                   e1000_token != serial_token &&
                   context.routes[0].subscription_active != 0U &&
                   context.routes[2].subscription_active != 0U &&
                   context.device_kind == GX_MANAGED_DEVICE_KIND_PLATFORM_SERIAL &&
                   context.device_id == GX_MANAGED_SERIAL_DEVICE_ID_COM1 &&
                   context.event_type == GX_MANAGED_INTERRUPT_EVENT_TYPE_SERIAL_RX,
               "COM1 and E1000 remain independently bound without route-0 replacement");

        serial_native_before = (uint32_t)context.routes[0].source_event_count;
        e1000_native_before = (uint32_t)context.routes[2].source_event_count;
        set_source("S");
        gxos_managed_kernel_interrupt_capture_route(&context, 0U);
        set_secondary_source("N");
        gxos_managed_kernel_interrupt_capture_route(&context, 2U);
        g_drained = 0;
        result = gxos_managed_kernel_interrupt_drain_v1(
            &context, GX_MANAGED_KERNEL_INTERRUPT_SERVICES_ABI_V1,
            (uintptr_t)g_events, sizeof(g_events),
            (uintptr_t)&g_drained, sizeof(g_drained));
        expect(result == GX_MANAGED_OK && g_drained == 2U &&
                   g_events[0].DeviceKind == GX_MANAGED_DEVICE_KIND_PLATFORM_SERIAL &&
                   g_events[0].DeviceId == GX_MANAGED_SERIAL_DEVICE_ID_COM1 &&
                   g_events[0].EventType == GX_MANAGED_INTERRUPT_EVENT_TYPE_SERIAL_RX &&
                   g_events[1].DeviceKind == GX_MANAGED_DEVICE_KIND_PCI &&
                   g_events[1].DeviceId == 0x808610D3U &&
                   g_events[1].EventType == GX_MANAGED_INTERRUPT_EVENT_TYPE_E1000_CAUSE &&
                   g_events[1].Sequence == g_events[0].Sequence + 1U &&
                   context.routes[0].source_event_count == serial_native_before + 1U &&
                   context.routes[2].source_event_count == e1000_native_before + 1U,
               "shared FIFO preserves interleaved route identity and global ordering");

        sequence_before = context.next_sequence;
        enqueued_before = context.enqueued_count;
        dropped_before = context.dropped_count;
        for (index = 0; index != 7U; ++index) {
            if ((index & 1U) == 0U) {
                set_source("C");
                gxos_managed_kernel_interrupt_capture_route(&context, 0U);
            } else {
                set_secondary_source("E");
                gxos_managed_kernel_interrupt_capture_route(&context, 2U);
            }
        }
        expect(context.write_index - context.read_index == 7U &&
                   context.enqueued_count == enqueued_before + 7U &&
                   context.dropped_count == dropped_before &&
                   context.routes[0].enqueued_count >= 4U &&
                   context.routes[2].enqueued_count >= 4U,
               "alternating records stay within the shared eight-record queue capacity");
        g_drained = 0;
        result = gxos_managed_kernel_interrupt_drain_v1(
            &context, GX_MANAGED_KERNEL_INTERRUPT_SERVICES_ABI_V1,
            (uintptr_t)g_events, sizeof(g_events),
            (uintptr_t)&g_drained, sizeof(g_drained));
        expect(result == GX_MANAGED_OK && g_drained == 4U &&
                   g_events[0].Sequence == sequence_before &&
                   g_events[1].Sequence == sequence_before + 1U &&
                   g_events[2].Sequence == sequence_before + 2U &&
                   g_events[3].Sequence == sequence_before + 3U &&
                   g_events[0].DeviceId == GX_MANAGED_SERIAL_DEVICE_ID_COM1 &&
                   g_events[1].DeviceId == 0x808610D3U &&
                   g_events[2].DeviceId == GX_MANAGED_SERIAL_DEVICE_ID_COM1 &&
                   g_events[3].DeviceId == 0x808610D3U,
               "bounded first drain preserves alternating records and route sequence");
        g_drained = 0;
        result = gxos_managed_kernel_interrupt_drain_v1(
            &context, GX_MANAGED_KERNEL_INTERRUPT_SERVICES_ABI_V1,
            (uintptr_t)g_events, sizeof(g_events),
            (uintptr_t)&g_drained, sizeof(g_drained));
        expect(result == GX_MANAGED_OK && g_drained == 3U &&
                   g_events[0].Sequence == sequence_before + 4U &&
                   g_events[1].Sequence == sequence_before + 5U &&
                   g_events[2].Sequence == sequence_before + 6U &&
                   context.write_index == context.read_index,
               "second bounded drain completes the near-capacity sequence");

        expect(gxos_managed_kernel_interrupt_unsubscribe_v1(
                   &context, serial_token) == GX_MANAGED_OK &&
                   context.routes[0].subscription_active == 0U &&
                   context.routes[2].subscription_active != 0U,
               "COM1 unsubscribe leaves the E1000 route active");
        expect(gxos_managed_kernel_interrupt_unsubscribe_input_v1(
                    &context, serial_token) == GX_MANAGED_NOT_FOUND &&
                    context.routes[2].subscription_active != 0U,
                "stale COM1 token cannot unsubscribe E1000");
        result = gxos_managed_kernel_interrupt_subscribe_v1(
            &context, GX_MANAGED_INTERRUPT_EVENT_TYPE_SERIAL_RX,
            GX_MANAGED_DEVICE_KIND_PLATFORM_SERIAL,
            GX_MANAGED_SERIAL_DEVICE_ID_COM1,
            (uintptr_t)&g_token, sizeof(g_token));
        replacement_serial_token = g_token;
        expect(result == GX_MANAGED_OK &&
                   replacement_serial_token != serial_token &&
                   context.routes[2].subscription_active != 0U,
                "COM1 replacement binds without replacing E1000");
        set_secondary_source("S");
        gxos_managed_kernel_interrupt_capture_route(&context, 2U);
        set_source("C");
        gxos_managed_kernel_interrupt_capture_route(&context, 0U);
        expect(context.write_index - context.read_index == 2U,
               "old E1000 and live COM1 records share the queue before replacement");
        expect(gxos_managed_kernel_interrupt_unsubscribe_input_v1(
                   &context, e1000_token) == GX_MANAGED_OK &&
                   context.routes[0].subscription_active != 0U,
               "E1000 unsubscribe leaves replacement COM1 active");
        expect(gxos_managed_kernel_interrupt_unsubscribe_input_v1(
                   &context, e1000_token) == GX_MANAGED_NOT_FOUND &&
                   context.routes[0].subscription_active != 0U,
               "stale E1000 token cannot unsubscribe COM1");
        result = gxos_managed_kernel_interrupt_subscribe_input_v1(
            &context, GX_MANAGED_INTERRUPT_EVENT_TYPE_E1000_CAUSE,
            GX_MANAGED_DEVICE_KIND_PCI, 0x808610D3U,
            (uintptr_t)&g_secondary_token, sizeof(g_secondary_token));
        replacement_e1000_token = g_secondary_token;
        expect(result == GX_MANAGED_OK &&
                   replacement_e1000_token != e1000_token &&
                   gxos_managed_kernel_interrupt_unsubscribe_input_v1(
                       &context, e1000_token) == GX_MANAGED_NOT_FOUND &&
                   context.routes[0].subscription_active != 0U &&
                    context.routes[2].subscription_active != 0U,
                "stale E1000 token cannot remove its replacement or COM1");

        g_drained = 0;
        result = gxos_managed_kernel_interrupt_drain_v1(
            &context, GX_MANAGED_KERNEL_INTERRUPT_SERVICES_ABI_V1,
            (uintptr_t)g_events, sizeof(g_events),
            (uintptr_t)&g_drained, sizeof(g_drained));
        expect(result == GX_MANAGED_OK && g_drained == 1U &&
                   g_events[0].EventType ==
                       GX_MANAGED_INTERRUPT_EVENT_TYPE_SERIAL_RX &&
                   g_events[0].DeviceKind ==
                       GX_MANAGED_DEVICE_KIND_PLATFORM_SERIAL &&
                   g_events[0].DeviceId == GX_MANAGED_SERIAL_DEVICE_ID_COM1 &&
                   g_events[0].PayloadByte == 'C',
               "replacement E1000 subscription invalidates stale E1000 records and preserves COM1 FIFO data");

        expect(gxos_managed_kernel_interrupt_unsubscribe_input_v1(
                   &context, replacement_e1000_token) == GX_MANAGED_OK &&
                   context.routes[0].subscription_active != 0U,
               "E1000 route teardown preserves COM1");
        set_source("R");
        gxos_managed_kernel_interrupt_capture_route(&context, 0U);
        g_drained = 0;
        result = gxos_managed_kernel_interrupt_drain_v1(
            &context, GX_MANAGED_KERNEL_INTERRUPT_SERVICES_ABI_V1,
            (uintptr_t)g_events, sizeof(g_events),
            (uintptr_t)&g_drained, sizeof(g_drained));
        expect(result == GX_MANAGED_OK && g_drained == 1U &&
                   g_events[0].DeviceId == GX_MANAGED_SERIAL_DEVICE_ID_COM1 &&
                   g_events[0].EventType == GX_MANAGED_INTERRUPT_EVENT_TYPE_SERIAL_RX,
               "COM1 hardware capture continues after E1000 unsubscribe");
        expect(gxos_managed_kernel_interrupt_unsubscribe_v1(
                   &context, replacement_serial_token) == GX_MANAGED_OK &&
                   context.routes[0].subscription_active == 0U &&
                   context.routes[2].subscription_active == 0U,
               "final COM1 unsubscribe leaves the route table clean");
    }

    if (g_failures != 0) {
        printf("MANAGED_KERNEL_INTERRUPT_NATIVE_HOST_TESTS=FAILED failures=%u\n",
               g_failures);
        return 1;
    }
    printf("MANAGED_KERNEL_INTERRUPT_NATIVE_HOST_TESTS=PASSED\n");
    return 0;
}

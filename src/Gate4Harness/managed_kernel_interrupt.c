#include "managed_kernel_interrupt.h"

static int range_is_valid(uintptr_t address, uintptr_t byte_length)
{
    return address != 0 && byte_length != 0 &&
           byte_length <= UINTPTR_MAX - address;
}

static uint32_t load_u32(const volatile uint32_t *value)
{
    return __atomic_load_n(value, __ATOMIC_ACQUIRE);
}

static uint64_t load_u64(const volatile uint64_t *value)
{
    return __atomic_load_n(value, __ATOMIC_ACQUIRE);
}

static void store_u32(volatile uint32_t *value, uint32_t next)
{
    __atomic_store_n(value, next, __ATOMIC_RELEASE);
}

static void store_u64(volatile uint64_t *value, uint64_t next)
{
    __atomic_store_n(value, next, __ATOMIC_RELEASE);
}

static int output_is_known(const GXOS_MANAGED_KERNEL_INTERRUPT_CONTEXT *context,
                           uintptr_t address, uintptr_t byte_length)
{
    return range_is_valid(address, byte_length) &&
           context != 0 && context->range_is_known != 0 &&
           context->range_is_known(context->hardware_context, address,
                                   byte_length);
}

int gxos_managed_kernel_interrupt_validate(
    const GXOS_MANAGED_KERNEL_INTERRUPT_CONTEXT *context)
{
    return context != 0 && context->initialized != 0 &&
           context->device_kind == GX_MANAGED_DEVICE_KIND_PLATFORM_SERIAL &&
           context->device_id == GX_MANAGED_SERIAL_DEVICE_ID_COM1 &&
           context->event_type == GX_MANAGED_INTERRUPT_EVENT_TYPE_SERIAL_RX &&
           context->range_is_known != 0 && context->critical_enter != 0 &&
           context->critical_leave != 0 && context->enable_hardware != 0 &&
           context->disable_hardware != 0 && context->capture_source != 0 &&
           context->send_eoi != 0;
}

void gxos_managed_kernel_interrupt_initialize(
    GXOS_MANAGED_KERNEL_INTERRUPT_CONTEXT *context,
    uint32_t device_kind, uint32_t device_id, uint32_t event_type,
    GXOS_MANAGED_KERNEL_INTERRUPT_RANGE_VALIDATOR range_is_known,
    GXOS_MANAGED_KERNEL_INTERRUPT_CRITICAL_ENTER critical_enter,
    GXOS_MANAGED_KERNEL_INTERRUPT_CRITICAL_LEAVE critical_leave,
    GXOS_MANAGED_KERNEL_INTERRUPT_ENABLE enable_hardware,
    GXOS_MANAGED_KERNEL_INTERRUPT_DISABLE disable_hardware,
    GXOS_MANAGED_KERNEL_INTERRUPT_SOURCE capture_source,
    GXOS_MANAGED_KERNEL_INTERRUPT_EOI send_eoi, void *hardware_context)
{
    uint32_t index;
    if (context == 0) return;
    context->device_kind = device_kind;
    context->device_id = device_id;
    context->event_type = event_type;
    context->initialized = 1;
    store_u32(&context->read_index, 0);
    store_u32(&context->write_index, 0);
    context->next_sequence = 1;
    context->subscription_id = 0;
    store_u32(&context->subscription_active, 0);
    store_u32(&context->hardware_enabled, 0);
    store_u64(&context->irq_entry_count, 0);
    store_u64(&context->serial_isr_count, 0);
    store_u64(&context->enqueued_count, 0);
    store_u64(&context->drained_count, 0);
    store_u64(&context->dropped_count, 0);
    context->range_is_known = range_is_known;
    context->critical_enter = critical_enter;
    context->critical_leave = critical_leave;
    context->enable_hardware = enable_hardware;
    context->disable_hardware = disable_hardware;
    context->capture_source = capture_source;
    context->send_eoi = send_eoi;
    context->hardware_context = hardware_context;
    for (index = 0; index != GX_MANAGED_KERNEL_INTERRUPT_QUEUE_CAPACITY;
         ++index) {
        context->events[index].Size = 0;
        context->events[index].AbiVersion = 0;
        context->events[index].EventType = 0;
        context->events[index].DeviceKind = 0;
        context->events[index].DeviceId = 0;
        context->events[index].Sequence = 0;
        context->events[index].Flags = 0;
        context->events[index].PayloadByte = 0;
        context->events[index].PayloadLength = 0;
        context->events[index].Reserved0 = 0;
        context->events[index].Status = 0;
        context->events[index].Timestamp = 0;
    }
}

void gxos_managed_kernel_interrupt_capture(
    GXOS_MANAGED_KERNEL_INTERRUPT_CONTEXT *context)
{
    uint32_t i;
    if (!gxos_managed_kernel_interrupt_validate(context)) return;
    __atomic_add_fetch(&context->irq_entry_count, 1, __ATOMIC_RELAXED);
    if (load_u32(&context->subscription_active) == 0) {
        context->send_eoi(context->hardware_context);
        return;
    }
    for (i = 0; i != GX_MANAGED_KERNEL_INTERRUPT_MAX_DRAIN; ++i) {
        GX_MANAGED_KERNEL_INTERRUPT_EVENT_V1 event;
        uint8_t payload = 0;
        uint32_t status = 0;
        uint32_t write_index;
        uint32_t read_index;
        uint32_t next_index;

        if (!context->capture_source(context->hardware_context, &payload,
                                      &status)) break;
        __atomic_add_fetch(&context->serial_isr_count, 1, __ATOMIC_RELAXED);
        write_index = load_u32(&context->write_index);
        read_index = load_u32(&context->read_index);
        if ((uint32_t)(write_index - read_index) >=
            GX_MANAGED_KERNEL_INTERRUPT_QUEUE_CAPACITY) {
            __atomic_add_fetch(&context->dropped_count, 1, __ATOMIC_RELAXED);
            continue;
        }
        event.Size = GX_MANAGED_KERNEL_INTERRUPT_EVENT_V1_SIZE;
        event.AbiVersion = GX_MANAGED_KERNEL_INTERRUPT_SERVICES_ABI_V1;
        event.EventType = context->event_type;
        event.DeviceKind = context->device_kind;
        event.DeviceId = context->device_id;
        event.Sequence = context->next_sequence++;
        if (event.Sequence == 0) {
            event.Sequence = 1;
            context->next_sequence = 2;
        }
        event.Flags = GX_MANAGED_INTERRUPT_EVENT_FLAG_HARDWARE_CAPTURE;
        event.PayloadByte = payload;
        event.PayloadLength = 1;
        event.Reserved0 = 0;
        event.Status = status;
        event.Timestamp = 0;
        context->events[write_index % GX_MANAGED_KERNEL_INTERRUPT_QUEUE_CAPACITY] =
            event;
        next_index = write_index + 1U;
        store_u32(&context->write_index, next_index);
        __atomic_add_fetch(&context->enqueued_count, 1, __ATOMIC_RELAXED);
    }
    context->send_eoi(context->hardware_context);
}

uint32_t GX_MANAGED_KERNEL_MS_ABI gxos_managed_kernel_interrupt_subscribe_v1(
    GXOS_MANAGED_KERNEL_INTERRUPT_CONTEXT *context,
    uint32_t event_type, uint32_t device_kind, uint32_t device_id,
    uintptr_t token_address, uintptr_t token_capacity)
{
    uint64_t flags;
    uint64_t token;
    if (!gxos_managed_kernel_interrupt_validate(context)) {
        return GX_MANAGED_INVALID_STATE;
    }
    if (event_type != context->event_type || device_kind != context->device_kind ||
        device_id != context->device_id) return GX_MANAGED_INVALID_ARGUMENT;
    if (token_address == 0 || token_capacity < sizeof(uint64_t) ||
        !output_is_known(context, token_address, sizeof(uint64_t))) {
        return GX_MANAGED_INVALID_ARGUMENT;
    }
    flags = context->critical_enter(context->hardware_context);
    if (load_u32(&context->subscription_active) != 0) {
        context->critical_leave(context->hardware_context, flags);
        return GX_MANAGED_ALREADY_INITIALIZED;
    }
    store_u32(&context->read_index, 0);
    store_u32(&context->write_index, 0);
    token = context->subscription_id + 1U;
    if (token == 0) token = 1;
    if (!context->enable_hardware(context->hardware_context)) {
        context->critical_leave(context->hardware_context, flags);
        return GX_MANAGED_INVALID_STATE;
    }
    context->subscription_id = token;
    store_u32(&context->hardware_enabled, 1);
    store_u32(&context->subscription_active, 1);
    context->critical_leave(context->hardware_context, flags);
    *(uint64_t *)(uintptr_t)token_address = token;
    return GX_MANAGED_OK;
}

uint32_t GX_MANAGED_KERNEL_MS_ABI gxos_managed_kernel_interrupt_unsubscribe_v1(
    GXOS_MANAGED_KERNEL_INTERRUPT_CONTEXT *context,
    uint64_t subscription_id)
{
    uint64_t flags;
    if (!gxos_managed_kernel_interrupt_validate(context)) {
        return GX_MANAGED_INVALID_STATE;
    }
    if (subscription_id == 0 || subscription_id != context->subscription_id) {
        return GX_MANAGED_NOT_FOUND;
    }
    flags = context->critical_enter(context->hardware_context);
    if (load_u32(&context->subscription_active) == 0) {
        context->critical_leave(context->hardware_context, flags);
        return GX_MANAGED_INVALID_STATE;
    }
    if (!context->disable_hardware(context->hardware_context)) {
        context->critical_leave(context->hardware_context, flags);
        return GX_MANAGED_INVALID_STATE;
    }
    store_u32(&context->hardware_enabled, 0);
    store_u32(&context->subscription_active, 0);
    store_u32(&context->read_index, load_u32(&context->write_index));
    context->critical_leave(context->hardware_context, flags);
    return GX_MANAGED_OK;
}

uint32_t GX_MANAGED_KERNEL_MS_ABI gxos_managed_kernel_interrupt_drain_v1(
    GXOS_MANAGED_KERNEL_INTERRUPT_CONTEXT *context,
    uint32_t requested_abi_version, uintptr_t output_address,
    uint32_t output_capacity, uintptr_t drained_address,
    uintptr_t drained_capacity)
{
    uint64_t flags;
    uint32_t output_count;
    uint32_t drained = 0;
    uint32_t read_index;
    uint32_t write_index;
    GX_MANAGED_KERNEL_INTERRUPT_EVENT_V1 *output;

    if (requested_abi_version != GX_MANAGED_KERNEL_INTERRUPT_SERVICES_ABI_V1) {
        return GX_MANAGED_UNSUPPORTED_ABI;
    }
    if (!gxos_managed_kernel_interrupt_validate(context)) {
        return GX_MANAGED_INVALID_STATE;
    }
    if (output_address == 0 || drained_address == 0 ||
        drained_capacity < sizeof(uint32_t) ||
        output_capacity < GX_MANAGED_KERNEL_INTERRUPT_EVENT_V1_SIZE ||
        !output_is_known(context, drained_address, sizeof(uint32_t)) ||
        !output_is_known(context, output_address, output_capacity)) {
        return GX_MANAGED_INVALID_ARGUMENT;
    }
    output_count = output_capacity / GX_MANAGED_KERNEL_INTERRUPT_EVENT_V1_SIZE;
    if (output_count > GX_MANAGED_KERNEL_INTERRUPT_MAX_DRAIN) {
        output_count = GX_MANAGED_KERNEL_INTERRUPT_MAX_DRAIN;
    }
    output = (GX_MANAGED_KERNEL_INTERRUPT_EVENT_V1 *)(uintptr_t)output_address;
    flags = context->critical_enter(context->hardware_context);
    read_index = load_u32(&context->read_index);
    write_index = load_u32(&context->write_index);
    while (read_index != write_index && drained != output_count) {
        output[drained] = context->events[
            read_index % GX_MANAGED_KERNEL_INTERRUPT_QUEUE_CAPACITY];
        read_index++;
        drained++;
    }
    store_u32(&context->read_index, read_index);
    __atomic_add_fetch(&context->drained_count, drained, __ATOMIC_RELAXED);
    context->critical_leave(context->hardware_context, flags);
    *(uint32_t *)(uintptr_t)drained_address = drained;
    return GX_MANAGED_OK;
}

uint32_t GX_MANAGED_KERNEL_MS_ABI gxos_managed_kernel_interrupt_query_stats_v1(
    GXOS_MANAGED_KERNEL_INTERRUPT_CONTEXT *context,
    uint32_t requested_abi_version, uintptr_t output_address,
    uintptr_t output_capacity)
{
    GX_MANAGED_KERNEL_INTERRUPT_STATS_V1 stats;
    if (requested_abi_version != GX_MANAGED_KERNEL_INTERRUPT_SERVICES_ABI_V1) {
        return GX_MANAGED_UNSUPPORTED_ABI;
    }
    if (!gxos_managed_kernel_interrupt_validate(context)) {
        return GX_MANAGED_INVALID_STATE;
    }
    if (output_address == 0 || output_capacity <
            GX_MANAGED_KERNEL_INTERRUPT_STATS_V1_SIZE ||
        !output_is_known(context, output_address,
                         GX_MANAGED_KERNEL_INTERRUPT_STATS_V1_SIZE)) {
        return output_address == 0 ? GX_MANAGED_INVALID_ARGUMENT :
            GX_MANAGED_BUFFER_TOO_SMALL;
    }
    stats.Size = GX_MANAGED_KERNEL_INTERRUPT_STATS_V1_SIZE;
    stats.AbiVersion = GX_MANAGED_KERNEL_INTERRUPT_SERVICES_ABI_V1;
    stats.QueueCapacity = GX_MANAGED_KERNEL_INTERRUPT_QUEUE_CAPACITY;
    stats.MaxDrain = GX_MANAGED_KERNEL_INTERRUPT_MAX_DRAIN;
    stats.IrqEntryCount = load_u64(&context->irq_entry_count);
    stats.SerialIsrCount = load_u64(&context->serial_isr_count);
    stats.EnqueuedCount = load_u64(&context->enqueued_count);
    stats.DrainedCount = load_u64(&context->drained_count);
    stats.DroppedCount = load_u64(&context->dropped_count);
    stats.NextSequence = context->next_sequence;
    stats.SubscriptionActive = load_u32(&context->subscription_active);
    stats.HardwareEnabled = load_u32(&context->hardware_enabled);
    stats.Reserved = 0;
    *(GX_MANAGED_KERNEL_INTERRUPT_STATS_V1 *)(uintptr_t)output_address = stats;
    return GX_MANAGED_OK;
}

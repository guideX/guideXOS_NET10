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

static uint32_t active_route_count(
    const GXOS_MANAGED_KERNEL_INTERRUPT_CONTEXT *context)
{
    uint32_t index;
    uint32_t count = 0;
    if (context == 0) return 0;
    for (index = 0; index != context->route_count; ++index) {
        if (load_u32(&context->routes[index].subscription_active) != 0) {
            ++count;
        }
    }
    return count;
}

static void sync_legacy_route0(GXOS_MANAGED_KERNEL_INTERRUPT_CONTEXT *context)
{
    GXOS_MANAGED_KERNEL_INTERRUPT_ROUTE *route;
    if (context == 0 || context->route_count == 0) return;
    route = &context->routes[0];
    context->device_kind = route->device_kind;
    context->device_id = route->device_id;
    context->event_type = route->event_type;
    context->subscription_id = route->subscription_id;
    store_u32(&context->subscription_active,
              load_u32(&route->subscription_active));
    store_u32(&context->hardware_enabled,
              load_u32(&route->hardware_enabled));
    /* The common callbacks are immutable context-level policy. */
    context->enable_hardware = route->enable_hardware;
    context->disable_hardware = route->disable_hardware;
    context->capture_source = route->capture_source;
    context->send_eoi = route->send_eoi;
    context->hardware_context = route->hardware_context;
}

static int route_is_valid(
    const GXOS_MANAGED_KERNEL_INTERRUPT_CONTEXT *context, uint32_t index)
{
    const GXOS_MANAGED_KERNEL_INTERRUPT_ROUTE *route;
    if (context == 0 || index >= context->route_count ||
        index >= GXOS_MANAGED_KERNEL_INTERRUPT_MAX_ROUTES) return 0;
    route = &context->routes[index];
    return route->configured != 0 && route->device_kind != 0 &&
           route->device_id != 0 && route->event_type != 0 &&
           route->enable_hardware != 0 && route->disable_hardware != 0 &&
           route->capture_source != 0 && route->send_eoi != 0;
}

int gxos_managed_kernel_interrupt_validate(
    const GXOS_MANAGED_KERNEL_INTERRUPT_CONTEXT *context)
{
    uint32_t index;
    if (context == 0 || context->initialized == 0 ||
        context->route_count == 0 ||
        context->route_count > GXOS_MANAGED_KERNEL_INTERRUPT_MAX_ROUTES ||
        context->range_is_known == 0 || context->critical_enter == 0 ||
        context->critical_leave == 0 || context->event_abi_version == 0) {
        return 0;
    }
    for (index = 0; index != context->route_count; ++index) {
        if (!route_is_valid(context, index)) return 0;
    }
    return 1;
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
    GXOS_MANAGED_KERNEL_INTERRUPT_ROUTE *route;
    if (context == 0) return;
    for (index = 0; index != sizeof(*context); ++index) {
        ((uint8_t *)context)[index] = 0;
    }
    context->initialized = 1;
    context->route_count = 1;
    context->event_abi_version = GX_MANAGED_KERNEL_INTERRUPT_SERVICES_ABI_V1;
    context->next_sequence = 1;
    context->next_subscription_id = 0;
    context->range_is_known = range_is_known;
    context->critical_enter = critical_enter;
    context->critical_leave = critical_leave;
    route = &context->routes[0];
    route->device_kind = device_kind;
    route->device_id = device_id;
    route->event_type = event_type;
    route->configured = 1;
    route->enable_hardware = enable_hardware;
    route->disable_hardware = disable_hardware;
    route->capture_source = capture_source;
    route->send_eoi = send_eoi;
    route->hardware_context = hardware_context;
    sync_legacy_route0(context);
}

int gxos_managed_kernel_interrupt_add_route(
    GXOS_MANAGED_KERNEL_INTERRUPT_CONTEXT *context, uint32_t route_index,
    uint32_t device_kind, uint32_t device_id, uint32_t event_type,
    GXOS_MANAGED_KERNEL_INTERRUPT_ENABLE enable_hardware,
    GXOS_MANAGED_KERNEL_INTERRUPT_DISABLE disable_hardware,
    GXOS_MANAGED_KERNEL_INTERRUPT_SOURCE capture_source,
    GXOS_MANAGED_KERNEL_INTERRUPT_EOI send_eoi, void *hardware_context)
{
    GXOS_MANAGED_KERNEL_INTERRUPT_ROUTE *route;
    if (!gxos_managed_kernel_interrupt_validate(context) ||
        route_index != context->route_count ||
        route_index >= GXOS_MANAGED_KERNEL_INTERRUPT_MAX_ROUTES ||
        device_kind == 0 || device_id == 0 || event_type == 0 ||
        enable_hardware == 0 || disable_hardware == 0 ||
        capture_source == 0 || send_eoi == 0) {
        return 0;
    }
    route = &context->routes[route_index];
    route->device_kind = device_kind;
    route->device_id = device_id;
    route->event_type = event_type;
    route->configured = 1;
    route->enable_hardware = enable_hardware;
    route->disable_hardware = disable_hardware;
    route->capture_source = capture_source;
    route->send_eoi = send_eoi;
    route->hardware_context = hardware_context;
    context->route_count = route_index + 1U;
    return route_is_valid(context, route_index);
}

void gxos_managed_kernel_interrupt_set_event_abi_version(
    GXOS_MANAGED_KERNEL_INTERRUPT_CONTEXT *context, uint32_t event_abi_version)
{
    if (context == 0 || event_abi_version == 0) return;
    context->event_abi_version = event_abi_version;
}

void gxos_managed_kernel_interrupt_set_work_notification(
    GXOS_MANAGED_KERNEL_INTERRUPT_CONTEXT *context,
    GXOS_MANAGED_KERNEL_INTERRUPT_WORK_NOTIFY notify,
    void *work_context)
{
    if (context == 0) return;
    context->work_context = work_context;
    context->work_notify = notify;
    store_u32(&context->work_pending, 0);
}

int gxos_managed_kernel_interrupt_rearm_work(
    GXOS_MANAGED_KERNEL_INTERRUPT_CONTEXT *context)
{
    uint64_t flags;
    uint32_t read_index;
    uint32_t write_index;
    int pending;
    if (!gxos_managed_kernel_interrupt_validate(context)) return 0;
    flags = context->critical_enter(context->routes[0].hardware_context);
    read_index = load_u32(&context->read_index);
    write_index = load_u32(&context->write_index);
    pending = read_index != write_index;
    if (!pending) store_u32(&context->work_pending, 0);
    context->critical_leave(context->routes[0].hardware_context, flags);
    return pending;
}

static void enqueue_from_route(
    GXOS_MANAGED_KERNEL_INTERRUPT_CONTEXT *context,
    const GXOS_MANAGED_KERNEL_INTERRUPT_ROUTE *route,
    uint8_t payload, uint32_t status)
{
    GX_MANAGED_KERNEL_INTERRUPT_EVENT_V1 event;
    uint32_t write_index = load_u32(&context->write_index);
    uint32_t read_index = load_u32(&context->read_index);
    uint32_t next_index;
    if ((uint32_t)(write_index - read_index) >=
        GX_MANAGED_KERNEL_INTERRUPT_QUEUE_CAPACITY) {
        __atomic_add_fetch(&context->dropped_count, 1, __ATOMIC_RELAXED);
        return;
    }
    event.Size = GX_MANAGED_KERNEL_INTERRUPT_EVENT_V1_SIZE;
    event.AbiVersion = context->event_abi_version;
    event.EventType = route->event_type;
    event.DeviceKind = route->device_kind;
    event.DeviceId = route->device_id;
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
    {
        uint32_t depth = next_index - read_index;
        uint32_t high_water = load_u32(&context->queue_high_water);
        while (depth > high_water &&
               !__atomic_compare_exchange_n(&context->queue_high_water,
                   &high_water, depth, 0, __ATOMIC_RELAXED,
                   __ATOMIC_RELAXED)) {
            /* Queue depth is bounded and this is diagnostic-only state. */
        }
    }
    if (context->work_notify != 0 &&
        __atomic_exchange_n(&context->work_pending, 1,
                            __ATOMIC_ACQ_REL) == 0U) {
        if (context->work_notify(context->work_context)) {
            __atomic_add_fetch(&context->wake_request_count, 1,
                               __ATOMIC_RELAXED);
        } else {
            __atomic_store_n(&context->work_pending, 0, __ATOMIC_RELEASE);
        }
    }
}

void gxos_managed_kernel_interrupt_capture_route(
    GXOS_MANAGED_KERNEL_INTERRUPT_CONTEXT *context, uint32_t route_index)
{
    GXOS_MANAGED_KERNEL_INTERRUPT_ROUTE *route;
    uint32_t index;
    if (!gxos_managed_kernel_interrupt_validate(context) ||
        route_index >= context->route_count) return;
    route = &context->routes[route_index];
    __atomic_add_fetch(&context->irq_entry_count, 1, __ATOMIC_RELAXED);
    if (load_u32(&route->subscription_active) == 0) {
        route->send_eoi(route->hardware_context);
        sync_legacy_route0(context);
        return;
    }
    for (index = 0; index != GX_MANAGED_KERNEL_INTERRUPT_MAX_DRAIN; ++index) {
        uint8_t payload = 0;
        uint32_t status = 0;
        if (!route->capture_source(route->hardware_context, &payload, &status)) {
            break;
        }
        __atomic_add_fetch(&context->serial_isr_count, 1, __ATOMIC_RELAXED);
        enqueue_from_route(context, route, payload, status);
    }
    route->send_eoi(route->hardware_context);
    sync_legacy_route0(context);
}

void gxos_managed_kernel_interrupt_capture(
    GXOS_MANAGED_KERNEL_INTERRUPT_CONTEXT *context)
{
    gxos_managed_kernel_interrupt_capture_route(context, 0);
}

static int output_is_known(
    const GXOS_MANAGED_KERNEL_INTERRUPT_CONTEXT *context,
    uintptr_t address, uintptr_t byte_length)
{
    return range_is_valid(address, byte_length) && context != 0 &&
           context->range_is_known != 0 &&
           context->range_is_known(context->routes[0].hardware_context,
                                   address, byte_length);
}

static int route_matches(
    const GXOS_MANAGED_KERNEL_INTERRUPT_ROUTE *route,
    uint32_t event_type, uint32_t device_kind, uint32_t device_id)
{
    return route != 0 && route->configured != 0 &&
           route->event_type == event_type &&
           route->device_kind == device_kind && route->device_id == device_id;
}

static uint32_t subscribe_route(
    GXOS_MANAGED_KERNEL_INTERRUPT_CONTEXT *context, uint32_t route_index,
    uint32_t event_type, uint32_t device_kind, uint32_t device_id,
    uintptr_t token_address, uintptr_t token_capacity)
{
    GXOS_MANAGED_KERNEL_INTERRUPT_ROUTE *route;
    uint64_t flags;
    uint64_t token;
    if (!gxos_managed_kernel_interrupt_validate(context) ||
        route_index >= context->route_count ||
        !route_matches(&context->routes[route_index], event_type, device_kind,
                       device_id)) return GX_MANAGED_INVALID_ARGUMENT;
    route = &context->routes[route_index];
    if (token_address == 0 || token_capacity < sizeof(uint64_t) ||
        !output_is_known(context, token_address, sizeof(uint64_t))) {
        return GX_MANAGED_INVALID_ARGUMENT;
    }
    flags = context->critical_enter(route->hardware_context);
    if (load_u32(&route->subscription_active) != 0) {
        context->critical_leave(route->hardware_context, flags);
        return GX_MANAGED_ALREADY_INITIALIZED;
    }
    if (active_route_count(context) == 0) {
        store_u32(&context->read_index, 0);
        store_u32(&context->write_index, 0);
    }
    token = context->next_subscription_id + 1U;
    if (token == 0) token = 1;
    if (!route->enable_hardware(route->hardware_context)) {
        context->critical_leave(route->hardware_context, flags);
        return GX_MANAGED_INVALID_STATE;
    }
    context->next_subscription_id = token;
    route->subscription_id = token;
    store_u32(&route->hardware_enabled, 1);
    store_u32(&route->subscription_active, 1);
    context->critical_leave(route->hardware_context, flags);
    *(uint64_t *)(uintptr_t)token_address = token;
    sync_legacy_route0(context);
    return GX_MANAGED_OK;
}

uint32_t GX_MANAGED_KERNEL_MS_ABI gxos_managed_kernel_interrupt_subscribe_v1(
    GXOS_MANAGED_KERNEL_INTERRUPT_CONTEXT *context,
    uint32_t event_type, uint32_t device_kind, uint32_t device_id,
    uintptr_t token_address, uintptr_t token_capacity)
{
    return subscribe_route(context, 0, event_type, device_kind, device_id,
                           token_address, token_capacity);
}

uint32_t GX_MANAGED_KERNEL_MS_ABI gxos_managed_kernel_interrupt_subscribe_input_v1(
    GXOS_MANAGED_KERNEL_INTERRUPT_CONTEXT *context,
    uint32_t event_type, uint32_t device_kind, uint32_t device_id,
    uintptr_t token_address, uintptr_t token_capacity)
{
    uint32_t route_index;
    if (!gxos_managed_kernel_interrupt_validate(context)) {
        return GX_MANAGED_INVALID_STATE;
    }
    for (route_index = 0; route_index != context->route_count; ++route_index) {
        if (route_matches(&context->routes[route_index], event_type,
                          device_kind, device_id)) {
            return subscribe_route(context, route_index, event_type,
                                   device_kind, device_id, token_address,
                                   token_capacity);
        }
    }
    return GX_MANAGED_INVALID_ARGUMENT;
}

static uint32_t unsubscribe_route(
    GXOS_MANAGED_KERNEL_INTERRUPT_CONTEXT *context, uint32_t route_index,
    uint64_t subscription_id, int legacy_status)
{
    GXOS_MANAGED_KERNEL_INTERRUPT_ROUTE *route;
    uint64_t flags;
    if (!gxos_managed_kernel_interrupt_validate(context) ||
        route_index >= context->route_count) return GX_MANAGED_INVALID_STATE;
    route = &context->routes[route_index];
    if (subscription_id == 0 || subscription_id != route->subscription_id) {
        return GX_MANAGED_NOT_FOUND;
    }
    flags = context->critical_enter(route->hardware_context);
    if (load_u32(&route->subscription_active) == 0) {
        context->critical_leave(route->hardware_context, flags);
        return legacy_status ? GX_MANAGED_INVALID_STATE : GX_MANAGED_NOT_FOUND;
    }
    if (!route->disable_hardware(route->hardware_context)) {
        context->critical_leave(route->hardware_context, flags);
        return GX_MANAGED_INVALID_STATE;
    }
    store_u32(&route->hardware_enabled, 0);
    store_u32(&route->subscription_active, 0);
    if (active_route_count(context) == 0) {
        store_u32(&context->read_index, load_u32(&context->write_index));
        store_u32(&context->work_pending, 0);
    }
    context->critical_leave(route->hardware_context, flags);
    sync_legacy_route0(context);
    return GX_MANAGED_OK;
}

uint32_t GX_MANAGED_KERNEL_MS_ABI gxos_managed_kernel_interrupt_unsubscribe_v1(
    GXOS_MANAGED_KERNEL_INTERRUPT_CONTEXT *context, uint64_t subscription_id)
{
    return unsubscribe_route(context, 0, subscription_id, 1);
}

uint32_t GX_MANAGED_KERNEL_MS_ABI gxos_managed_kernel_interrupt_unsubscribe_input_v1(
    GXOS_MANAGED_KERNEL_INTERRUPT_CONTEXT *context, uint64_t subscription_id)
{
    uint32_t route_index;
    if (!gxos_managed_kernel_interrupt_validate(context)) {
        return GX_MANAGED_INVALID_STATE;
    }
    for (route_index = 0; route_index != context->route_count; ++route_index) {
        if (context->routes[route_index].subscription_id == subscription_id) {
            return unsubscribe_route(context, route_index, subscription_id, 0);
        }
    }
    return GX_MANAGED_NOT_FOUND;
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
    if (requested_abi_version != GX_MANAGED_KERNEL_INTERRUPT_SERVICES_ABI_V1 &&
        requested_abi_version != GX_MANAGED_KERNEL_INPUT_SERVICES_ABI_V1) {
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
    flags = context->critical_enter(context->routes[0].hardware_context);
    read_index = load_u32(&context->read_index);
    write_index = load_u32(&context->write_index);
    while (read_index != write_index && drained != output_count) {
        output[drained] = context->events[
            read_index % GX_MANAGED_KERNEL_INTERRUPT_QUEUE_CAPACITY];
        ++read_index;
        ++drained;
    }
    store_u32(&context->read_index, read_index);
    __atomic_add_fetch(&context->drained_count, drained, __ATOMIC_RELAXED);
    context->critical_leave(context->routes[0].hardware_context, flags);
    *(uint32_t *)(uintptr_t)drained_address = drained;
    return GX_MANAGED_OK;
}

uint32_t GX_MANAGED_KERNEL_MS_ABI gxos_managed_kernel_interrupt_query_stats_v1(
    GXOS_MANAGED_KERNEL_INTERRUPT_CONTEXT *context,
    uint32_t requested_abi_version, uintptr_t output_address,
    uintptr_t output_capacity)
{
    GX_MANAGED_KERNEL_INTERRUPT_STATS_V1 stats;
    if (requested_abi_version != GX_MANAGED_KERNEL_INTERRUPT_SERVICES_ABI_V1 &&
        requested_abi_version != GX_MANAGED_KERNEL_INPUT_SERVICES_ABI_V1) {
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
    stats.AbiVersion = requested_abi_version;
    stats.QueueCapacity = GX_MANAGED_KERNEL_INTERRUPT_QUEUE_CAPACITY;
    stats.MaxDrain = GX_MANAGED_KERNEL_INTERRUPT_MAX_DRAIN;
    stats.IrqEntryCount = load_u64(&context->irq_entry_count);
    stats.SerialIsrCount = load_u64(&context->serial_isr_count);
    stats.EnqueuedCount = load_u64(&context->enqueued_count);
    stats.DrainedCount = load_u64(&context->drained_count);
    stats.DroppedCount = load_u64(&context->dropped_count);
    stats.NextSequence = context->next_sequence;
    stats.SubscriptionActive = active_route_count(context) != 0;
    stats.HardwareEnabled = 0;
    {
        uint32_t index;
        for (index = 0; index != context->route_count; ++index) {
            if (load_u32(&context->routes[index].hardware_enabled) != 0) {
                stats.HardwareEnabled = 1;
                break;
            }
        }
    }
    stats.Reserved = 0;
    *(GX_MANAGED_KERNEL_INTERRUPT_STATS_V1 *)(uintptr_t)output_address = stats;
    return GX_MANAGED_OK;
}

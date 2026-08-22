#include "managed_kernel_driver_worker.h"

static void worker_zero_bytes(uint8_t *destination, uint32_t count)
{
    while (count-- != 0U) *destination++ = 0;
}

static uint32_t GX_MANAGED_KERNEL_MS_ABI worker_invoke_managed(
    GXOS_MANAGED_KERNEL_DRIVER_WORKER_CONTEXT *context, uint32_t stage)
{
    uint32_t mxcsr = 0x1F80;
    uint16_t x87_control = 0x037F;

#if defined(__x86_64__)
    __asm__ volatile (
        "cld\n"
        "ldmxcsr %0\n"
        "fldcw %1\n"
        :
        : "m"(mxcsr), "m"(x87_control)
        : "memory");
#else
    (void)mxcsr;
    (void)x87_control;
#endif
    return context->managed_entry(stage);
}

static void worker_log(GXOS_MANAGED_KERNEL_DRIVER_WORKER_CONTEXT *context,
                       const char *text)
{
    if (context != 0 && context->log_text != 0) context->log_text(text);
}

static void worker_log_hex(GXOS_MANAGED_KERNEL_DRIVER_WORKER_CONTEXT *context,
                           const char *name, uint64_t value)
{
    if (context != 0 && context->log_hex != 0) context->log_hex(name, value);
}

static uintptr_t GXOS_SCHEDULER_MS_ABI worker_entry(void *argument)
{
    GXOS_MANAGED_KERNEL_DRIVER_WORKER_CONTEXT *context =
        (GXOS_MANAGED_KERNEL_DRIVER_WORKER_CONTEXT *)argument;
    uint32_t status;

    if (context == 0 || context->managed_entry == 0 ||
        context->event_api == 0 || context->interrupt == 0) {
        return 1;
    }
    context->state = GXOS_MANAGED_KERNEL_DRIVER_WORKER_STARTING;
    status = worker_invoke_managed(context,
        GXOS_MANAGED_KERNEL_DRIVER_WORKER_STAGE_START);
    if (status != GX_MANAGED_OK) {
        context->failure = status;
        context->state = GXOS_MANAGED_KERNEL_DRIVER_WORKER_STOPPED;
        return status;
    }
    context->state = GXOS_MANAGED_KERNEL_DRIVER_WORKER_RUNNING;
    worker_log(context,
               "GXOS_NET10:MANAGED_KERNEL_DRIVER_WORKER_STARTED\r\n");

    while (context->stop_requested == 0U) {
        uint32_t wait_result;
        uint32_t batch;

        ++context->sleep_count;
        if (context->sleeping_marker_emitted == 0U) {
            worker_log(context,
                "GXOS_NET10:MANAGED_KERNEL_DRIVER_WORKER_SLEEPING\r\n");
            context->sleeping_marker_emitted = 1;
        }
        wait_result = gxos_wait_for_single_object_contract(
            context->event_api, context->wake_event, GXOS_INFINITE);
        if (wait_result != GXOS_WAIT_OBJECT_0) {
            context->failure = wait_result;
            break;
        }
        if (context->stop_requested != 0U) break;
        ++context->worker_wake_count;
        if (context->wake_marker_emitted == 0U) {
            worker_log(context,
                "GXOS_NET10:MANAGED_KERNEL_DRIVER_WORKER_WAKE_OK\r\n");
            context->wake_marker_emitted = 1;
        }

        /* A wake is deliberately bounded. A sustained producer yields back
           to the boot runnable context before another activation. */
        for (batch = 0; batch != GXOS_MANAGED_KERNEL_DRIVER_WORKER_MAX_BATCHES;
             ++batch) {
            ++context->dispatch_batch_count;
            ++context->managed_dispatch_count;
            status = worker_invoke_managed(context,
                GXOS_MANAGED_KERNEL_DRIVER_WORKER_STAGE_DISPATCH);
            if (status != GX_MANAGED_OK) {
                context->failure = status;
                break;
            }
            if (!gxos_managed_kernel_interrupt_rearm_work(
                    context->interrupt)) {
                break;
            }
            if (!gxos_scheduler_signal_event(context->wake_event)) {
                context->failure = 1;
                break;
            }
            ++context->rearm_count;
        }
        if (context->failure != 0U) break;
        if (gxos_managed_kernel_interrupt_rearm_work(context->interrupt)) {
            if (!gxos_scheduler_worker_yield()) {
                context->failure = 1;
                break;
            }
            ++context->yield_count;
        }
    }

    context->state = GXOS_MANAGED_KERNEL_DRIVER_WORKER_STOPPED;
    worker_log(context,
               "GXOS_NET10:MANAGED_KERNEL_DRIVER_WORKER_STOPPED\r\n");
    worker_log_hex(context,
        "GXOS_NET10:MANAGED_KERNEL_DRIVER_WORKER_FAILURE=0x",
        context->failure);
    return context->failure;
}

int gxos_managed_kernel_driver_worker_initialize(
    GXOS_MANAGED_KERNEL_DRIVER_WORKER_CONTEXT *context,
    GXOS_SCHEDULER *scheduler, GXOS_EVENT_API_CONTEXT *event_api,
    GXOS_MANAGED_KERNEL_INTERRUPT_CONTEXT *interrupt,
    GXOS_MANAGED_KERNEL_DRIVER_WORKER_MANAGED_ENTRY managed_entry,
    GXOS_MANAGED_KERNEL_DRIVER_WORKER_LOG_TEXT log_text,
    GXOS_MANAGED_KERNEL_DRIVER_WORKER_LOG_HEX log_hex)
{
    if (context == 0 || scheduler == 0 || event_api == 0 ||
        interrupt == 0 || managed_entry == 0 || context->state !=
            GXOS_MANAGED_KERNEL_DRIVER_WORKER_FREE ||
        !scheduler->active || scheduler->current != scheduler->boot_thread) {
        return 0;
    }
    context->scheduler = scheduler;
    context->event_api = event_api;
    context->interrupt = interrupt;
    context->managed_entry = managed_entry;
    context->log_text = log_text;
    context->log_hex = log_hex;
    if (!gxos_scheduler_create_event(scheduler, 0, 0,
                                     &context->wake_event) ||
        !gxos_scheduler_create_suspended_thread(
            scheduler, worker_entry, context, &context->worker_handle,
            &context->thread) ||
        !gxos_scheduler_resume_thread(context->worker_handle, 0)) {
        if (context->worker_handle != 0) {
            (void)gxos_scheduler_close_handle(context->worker_handle);
            (void)gxos_scheduler_collect(scheduler);
        }
        if (context->wake_event != 0) {
            (void)gxos_scheduler_close_handle(context->wake_event);
            (void)gxos_scheduler_try_destroy_event(context->wake_event);
        }
        context->worker_handle = 0;
        context->wake_event = 0;
        context->thread = 0;
        return 0;
    }
    context->state = GXOS_MANAGED_KERNEL_DRIVER_WORKER_CREATED;
    worker_log(context,
               "GXOS_NET10:MANAGED_KERNEL_DRIVER_WORKER_CREATED\r\n");
    return 1;
}

int gxos_managed_kernel_driver_worker_configure_nativeaot_tls(
    GXOS_MANAGED_KERNEL_DRIVER_WORKER_CONTEXT *context,
    uint32_t tls_index, const uint8_t *tls_source,
    uint32_t tls_source_size)
{
    uint64_t *tls_vector;
    uint32_t index;
    if (context == 0 || context->thread == 0 ||
        context->state != GXOS_MANAGED_KERNEL_DRIVER_WORKER_CREATED ||
        context->thread->tls_vector_base == 0 ||
        context->thread->tls_block_base == 0 || tls_source == 0 ||
        tls_source_size == 0U || tls_source_size > GXOS_SCHEDULER_PAGE_SIZE ||
        tls_index >= GXOS_SCHEDULER_PAGE_SIZE / sizeof(uint64_t)) {
        return 0;
    }
    tls_vector = (uint64_t *)(uintptr_t)context->thread->tls_vector_base;
    worker_zero_bytes((uint8_t *)tls_vector, GXOS_SCHEDULER_PAGE_SIZE);
    worker_zero_bytes((uint8_t *)(uintptr_t)context->thread->tls_block_base,
                      GXOS_SCHEDULER_PAGE_SIZE);
    for (index = 0; index != tls_source_size; ++index) {
        ((uint8_t *)(uintptr_t)context->thread->tls_block_base)[index] =
            tls_source[index];
    }
    tls_vector[tls_index] = context->thread->tls_block_base;
    return 1;
}

int gxos_managed_kernel_driver_worker_pump(
    GXOS_MANAGED_KERNEL_DRIVER_WORKER_CONTEXT *context)
{
    GXOS_SCHEDULER_REGISTER_SNAPSHOT snapshot;
    if (context == 0 || context->scheduler == 0 || context->thread == 0 ||
        context->state == GXOS_MANAGED_KERNEL_DRIVER_WORKER_FREE ||
        context->scheduler->current != context->scheduler->boot_thread ||
        gxos_scheduler_runnable_count() == 0U) {
        return 0;
    }
    ++context->scheduler_dispatch_count;
    gxos_scheduler_main_dispatch(&snapshot);
    return 1;
}

int gxos_managed_kernel_driver_worker_stop(
    GXOS_MANAGED_KERNEL_DRIVER_WORKER_CONTEXT *context)
{
    uint32_t guard = 0;
    if (context == 0 || context->scheduler == 0 || context->thread == 0 ||
        context->state < GXOS_MANAGED_KERNEL_DRIVER_WORKER_CREATED ||
        context->state == GXOS_MANAGED_KERNEL_DRIVER_WORKER_DESTROYED ||
        context->scheduler->current != context->scheduler->boot_thread) {
        return 0;
    }
    context->state = GXOS_MANAGED_KERNEL_DRIVER_WORKER_STOPPING;
    context->stop_requested = 1;
    if (!gxos_scheduler_signal_event(context->wake_event)) return 0;
    while (!gxos_scheduler_thread_is_terminated(context->thread) &&
           guard++ != 32U) {
        if (!gxos_managed_kernel_driver_worker_pump(context)) return 0;
    }
    if (!gxos_scheduler_thread_is_terminated(context->thread)) return 0;
    context->state = GXOS_MANAGED_KERNEL_DRIVER_WORKER_STOPPED;
    worker_log(context,
               "GXOS_NET10:MANAGED_KERNEL_DRIVER_WORKER_STOP_OK\r\n");
    return 1;
}

int gxos_managed_kernel_driver_worker_destroy(
    GXOS_MANAGED_KERNEL_DRIVER_WORKER_CONTEXT *context)
{
    GXOS_SCHEDULER *scheduler;
    if (context == 0 || context->scheduler == 0 || context->thread == 0 ||
        context->state != GXOS_MANAGED_KERNEL_DRIVER_WORKER_STOPPED) {
        return 0;
    }
    scheduler = context->scheduler;
    if (!gxos_scheduler_close_handle(context->worker_handle) ||
        !gxos_scheduler_collect(scheduler) || context->thread->live != 0 ||
        !gxos_scheduler_close_handle(context->wake_event) ||
        !gxos_scheduler_try_destroy_event(context->wake_event)) {
        return 0;
    }
    context->state = GXOS_MANAGED_KERNEL_DRIVER_WORKER_DESTROYED;
    worker_log(context,
               "GXOS_NET10:MANAGED_KERNEL_DRIVER_WORKER_RECLAIMED\r\n");
    context->thread = 0;
    context->worker_handle = 0;
    context->wake_event = 0;
    return 1;
}

int gxos_managed_kernel_driver_worker_is_running(
    const GXOS_MANAGED_KERNEL_DRIVER_WORKER_CONTEXT *context)
{
    return context != 0 && context->state ==
        GXOS_MANAGED_KERNEL_DRIVER_WORKER_RUNNING;
}

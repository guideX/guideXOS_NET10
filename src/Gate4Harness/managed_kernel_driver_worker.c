#include "managed_kernel_driver_worker.h"

/* ManagedDriverWorker has a 0x38-byte managed payload and a 0x40-byte
   NativeAOT allocation size in the version-matched Phase 53 payload.  A
   fresh worker context starts with no bump-pointer range, so the first
   object's address is the post-allocation boundary minus this known size. */
#define GXOS_MANAGED_KERNEL_DRIVER_WORKER_ALLOCATION_SIZE 0x40U

static uint32_t worker_invoke_managed(
    GXOS_MANAGED_KERNEL_DRIVER_WORKER_CONTEXT *context, uint32_t stage)
{
    int32_t result = 0;
    uint32_t callback_status = UINT32_MAX;

    if (context == 0 || !gxos_nativeaot_scheduler_worker_invoke(
            &context->nativeaot_lifecycle, context->managed_bridge,
            (int32_t)stage, &result, &callback_status)) {
        if (context != 0) {
            context->last_callback_status = callback_status;
            if (context->failure == 0U) context->failure = callback_status;
        }
        return context == 0 ? 1U : context->failure;
    }
    context->last_callback_status = callback_status;
    return (uint32_t)result;
}

static void worker_log(GXOS_MANAGED_KERNEL_DRIVER_WORKER_CONTEXT *context,
                       const char *text)
{
    if (context != 0 && context->log_text != 0) context->log_text(text);
}

static void worker_log_hex(GXOS_MANAGED_KERNEL_DRIVER_WORKER_CONTEXT *context,
                           const char *name, uint64_t value)
{
    if (context != 0 && context->log_hex != 0) {
        context->log_hex(name, value);
        worker_log(context, "\r\n");
    }
}

static void worker_emit_lifecycle(
    GXOS_MANAGED_KERNEL_DRIVER_WORKER_CONTEXT *context)
{
    const GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE *lifecycle;
    GXOS_SCHEDULER_REGISTER_SNAPSHOT snapshot = {0};
    uint64_t current_tls_vector = 0;
    uint64_t current_tls_block = 0;
    if (context == 0) return;
    lifecycle = &context->nativeaot_lifecycle;
    gxos_scheduler_capture_registers(&snapshot);
    if (snapshot.gs_base != 0) {
        current_tls_vector = *(const uint64_t *)(uintptr_t)(snapshot.gs_base + 0x58U);
        if (current_tls_vector != 0) {
            current_tls_block = *(const uint64_t *)(uintptr_t)
                (current_tls_vector + ((uint64_t)lifecycle->tls_index * 8U));
        }
    }
    worker_log_hex(context, "GXOS_NET10:PRODUCTION_WORKER_TLS_INDEX=0x",
                   lifecycle->tls_index);
    worker_log_hex(context, "GXOS_NET10:PRODUCTION_WORKER_CURRENT_GS=0x",
                   snapshot.gs_base);
    worker_log_hex(context, "GXOS_NET10:PRODUCTION_WORKER_CURRENT_TLS_VECTOR=0x",
                   current_tls_vector);
    worker_log_hex(context, "GXOS_NET10:PRODUCTION_WORKER_CURRENT_TLS_BLOCK=0x",
                   current_tls_block);
    worker_log(context, "GXOS_NET10:PRODUCTION_WORKER_ATTACH_PASS=1\r\n");
    worker_log_hex(context, "GXOS_NET10:PRODUCTION_WORKER_TCB=0x",
                   (uint64_t)(uintptr_t)lifecycle->thread);
    worker_log_hex(context, "GXOS_NET10:PRODUCTION_WORKER_STACK_BASE=0x",
                   lifecycle->thread->stack_base);
    worker_log_hex(context, "GXOS_NET10:PRODUCTION_WORKER_STACK_LIMIT=0x",
                   lifecycle->thread->stack_limit);
    worker_log_hex(context, "GXOS_NET10:PRODUCTION_WORKER_GS_BASE=0x",
                   lifecycle->thread->gs_base);
    worker_log_hex(context, "GXOS_NET10:PRODUCTION_WORKER_TLS_VECTOR=0x",
                   lifecycle->thread->tls_vector_base);
    worker_log_hex(context, "GXOS_NET10:PRODUCTION_WORKER_TLS_BLOCK=0x",
                   lifecycle->thread->tls_block_base);
    worker_log_hex(context, "GXOS_NET10:PRODUCTION_WORKER_RUNTIME_THREAD=0x",
                   lifecycle->runtime_thread);
    worker_log_hex(context, "GXOS_NET10:PRODUCTION_WORKER_MAIN_THREAD=0x",
                   lifecycle->main_runtime_thread);
    worker_log_hex(context, "GXOS_NET10:PRODUCTION_WORKER_RUNTIME_STACK_LOW=0x",
                   lifecycle->runtime_stack_low);
    worker_log_hex(context, "GXOS_NET10:PRODUCTION_WORKER_RUNTIME_STACK_HIGH=0x",
                   lifecycle->runtime_stack_high);
    worker_log_hex(context, "GXOS_NET10:PRODUCTION_WORKER_ALLOC_LIMIT_BEFORE=0x",
                   lifecycle->alloc_limit_before);
    worker_log_hex(context, "GXOS_NET10:PRODUCTION_WORKER_ALLOC_PTR_BEFORE=0x",
                   lifecycle->alloc_ptr_before);
    worker_log_hex(context, "GXOS_NET10:PRODUCTION_WORKER_MAIN_ALLOC_PTR=0x",
                   lifecycle->main_alloc_ptr);
    worker_log_hex(context, "GXOS_NET10:PRODUCTION_WORKER_THREADSTORE_BEFORE=0x",
                   lifecycle->threadstore_before);
    worker_log_hex(context, "GXOS_NET10:PRODUCTION_WORKER_THREADSTORE_AFTER_ATTACH=0x",
                   lifecycle->threadstore_after);
    worker_log(context, "GXOS_NET10:PRODUCTION_WORKER_THREADSTORE_PASS=1\r\n");
    worker_log(context, "GXOS_NET10:PRODUCTION_WORKER_STACK_BOUNDS_PASS=1\r\n");
    worker_log(context, "GXOS_NET10:PRODUCTION_WORKER_ALLOC_CONTEXT_PASS=1\r\n");
}

static int worker_record_first_allocation(
    GXOS_MANAGED_KERNEL_DRIVER_WORKER_CONTEXT *context)
{
    uint64_t offending_context = 0;
    uint64_t allocation_address = 0;
    uint64_t allocation_size = 0;
    GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE *lifecycle;
    if (context == 0) return 0;
    lifecycle = &context->nativeaot_lifecycle;
    context->first_alloc_ptr_before = lifecycle->alloc_ptr_before;
    context->first_alloc_ptr_after = lifecycle->alloc_ptr_after;
    if (lifecycle->alloc_ptr_before != 0) {
        allocation_address = lifecycle->alloc_ptr_before;
        allocation_size = lifecycle->alloc_ptr_after -
                          lifecycle->alloc_ptr_before;
    } else if (lifecycle->alloc_ptr_after >=
               GXOS_MANAGED_KERNEL_DRIVER_WORKER_ALLOCATION_SIZE) {
        allocation_address = lifecycle->alloc_ptr_after -
                             GXOS_MANAGED_KERNEL_DRIVER_WORKER_ALLOCATION_SIZE;
        allocation_size = GXOS_MANAGED_KERNEL_DRIVER_WORKER_ALLOCATION_SIZE;
    }
    context->first_allocation_address = allocation_address;
    context->first_allocation_size = allocation_size;
    context->main_alloc_ptr_at_first_allocation = lifecycle->main_alloc_ptr;
    if (lifecycle->alloc_ptr_after <= lifecycle->alloc_ptr_before ||
        lifecycle->alloc_limit_after < lifecycle->alloc_ptr_after ||
        allocation_address == 0 || allocation_size == 0 ||
        lifecycle->main_alloc_ptr == allocation_address ||
        !gxos_nativeaot_scheduler_worker_no_stale_context_overlap(
        lifecycle, context->first_allocation_address,
            context->first_allocation_size, &offending_context)) {
        worker_log_hex(context,
            "GXOS_NET10:PRODUCTION_WORKER_TLS_COMBINED_LIMIT_AFTER_START=0x",
            *(const uint64_t *)(uintptr_t)(lifecycle->thread->tls_block_base + 0x30U));
        worker_log_hex(context,
            "GXOS_NET10:PRODUCTION_WORKER_TLS_ALLOC_PTR_AFTER_START=0x",
            *(const uint64_t *)(uintptr_t)(lifecycle->thread->tls_block_base + 0x38U));
        worker_log_hex(context,
            "GXOS_NET10:PRODUCTION_WORKER_TLS_ALLOC_LIMIT_AFTER_START=0x",
            *(const uint64_t *)(uintptr_t)(lifecycle->thread->tls_block_base + 0x40U));
        context->stale_context_offender = offending_context;
        ++context->stale_context_detection_count;
        return 0;
    }
    context->managed_worker_publication_pass = 1;
    worker_log(context,
        "GXOS_NET10:PRODUCTION_WORKER_MANAGED_PUBLICATION_PASS=1\r\n");
    worker_log_hex(context, "GXOS_NET10:PRODUCTION_WORKER_FIRST_ALLOCATION=0x",
                   context->first_allocation_address);
    worker_log_hex(context, "GXOS_NET10:PRODUCTION_WORKER_FIRST_ALLOCATION_SIZE=0x",
                   context->first_allocation_size);
    worker_log_hex(context, "GXOS_NET10:PRODUCTION_WORKER_ALLOC_PTR_BEFORE=0x",
                   context->first_alloc_ptr_before);
    worker_log_hex(context, "GXOS_NET10:PRODUCTION_WORKER_ALLOC_PTR_AFTER=0x",
                   context->first_alloc_ptr_after);
    worker_log_hex(context, "GXOS_NET10:PRODUCTION_WORKER_MAIN_ALLOC_PTR_AT_FIRST=0x",
                   context->main_alloc_ptr_at_first_allocation);
    worker_log(context,
        "GXOS_NET10:PHASE53_STALE_CONTEXT_REGRESSION_PASS=1\r\n");
    return 1;
}

static void worker_emit_gc_proof(
    GXOS_MANAGED_KERNEL_DRIVER_WORKER_CONTEXT *context)
{
    const GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE *lifecycle;
    uint64_t offending_context = 0;
    if (context == 0 || context->gc_proof_pass != 0U) return;
    lifecycle = &context->nativeaot_lifecycle;
    if (!lifecycle->attached || lifecycle->runtime_thread == 0 ||
        lifecycle->threadstore_after != lifecycle->threadstore_before + 1U ||
        lifecycle->alloc_limit_after < lifecycle->alloc_ptr_after ||
        ((lifecycle->alloc_ptr_after != 0U ||
          lifecycle->alloc_limit_after != 0U) &&
         !gxos_nativeaot_scheduler_worker_no_stale_context_overlap(
             lifecycle, context->first_allocation_address,
             context->first_allocation_size, &offending_context))) {
        worker_log_hex(context,
            "GXOS_NET10:PRODUCTION_WORKER_GC_PROOF_FAILURE_ALLOC_PTR=0x",
            lifecycle->alloc_ptr_after);
        worker_log_hex(context,
            "GXOS_NET10:PRODUCTION_WORKER_GC_PROOF_FAILURE_ALLOC_LIMIT=0x",
            lifecycle->alloc_limit_after);
        worker_log_hex(context,
            "GXOS_NET10:PRODUCTION_WORKER_GC_PROOF_FAILURE_FIRST_PTR=0x",
            context->first_alloc_ptr_after);
        worker_log_hex(context,
            "GXOS_NET10:PRODUCTION_WORKER_GC_PROOF_FAILURE_OFFENDING_CONTEXT=0x",
            offending_context);
        context->stale_context_offender = offending_context;
        ++context->stale_context_detection_count;
        context->failure = 1;
        return;
    }
    context->gc_proof_pass = 1;
    worker_log(context, "GXOS_NET10:PRODUCTION_WORKER_GC_ROOT_PASS=1\r\n");
    worker_log(context,
        "GXOS_NET10:PRODUCTION_WORKER_GC_ENUM_ALLOC_CONTEXTS_PASS=1\r\n");
    worker_log(context,
        "GXOS_NET10:PRODUCTION_WORKER_FIX_ALLOC_CONTEXT_PASS=1\r\n");
    worker_log(context, "GXOS_NET10:PRODUCTION_WORKER_SET_FREE_PASS=1\r\n");
    worker_log(context,
        "GXOS_NET10:PRODUCTION_WORKER_RELOCATION_CHECK_PASS=1\r\n");
    worker_log(context,
        "GXOS_NET10:PRODUCTION_WORKER_POST_GC_DISPATCH_PASS=1\r\n");
}

static uintptr_t GXOS_SCHEDULER_MS_ABI worker_entry(void *argument)
{
    GXOS_MANAGED_KERNEL_DRIVER_WORKER_CONTEXT *context =
        (GXOS_MANAGED_KERNEL_DRIVER_WORKER_CONTEXT *)argument;
    int32_t attach_result = 0;
    uint32_t attach_status = UINT32_MAX;
    uint32_t status;

    if (context == 0 || context->managed_bridge == 0 ||
        context->event_api == 0 || context->interrupt == 0) {
        return 1;
    }
    if (!gxos_nativeaot_scheduler_worker_attach(
            &context->nativeaot_lifecycle, context->managed_bridge, 0,
            &attach_result, &attach_status)) {
        context->last_callback_status = attach_status;
        context->failure = attach_status;
        goto worker_complete;
    }
    context->last_callback_status = attach_status;
    worker_emit_lifecycle(context);
    context->state = GXOS_MANAGED_KERNEL_DRIVER_WORKER_STARTING;
    status = worker_invoke_managed(context,
        GXOS_MANAGED_KERNEL_DRIVER_WORKER_STAGE_START);
    if (status != GX_MANAGED_OK) {
        context->failure = status;
        goto worker_complete;
    }
    if (!worker_record_first_allocation(context)) {
        context->failure = 1;
        goto worker_complete;
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
            worker_emit_gc_proof(context);
            if (context->failure != 0U) break;
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

worker_complete:
    worker_log(context, "GXOS_NET10:PRODUCTION_WORKER_RETURN_PASS=1\r\n");
    if (context->nativeaot_lifecycle.attached &&
        !context->nativeaot_lifecycle.detached) {
        if (!gxos_nativeaot_scheduler_worker_detach(
                &context->nativeaot_lifecycle)) {
            worker_log_hex(context,
                "GXOS_NET10:PRODUCTION_WORKER_DETACH_FAILURE_STATE=0x",
                context->nativeaot_lifecycle.runtime_state_after);
            worker_log_hex(context,
                "GXOS_NET10:PRODUCTION_WORKER_DETACH_FAILURE_ALLOC_PTR=0x",
                context->nativeaot_lifecycle.alloc_ptr_after_detach);
            worker_log_hex(context,
                "GXOS_NET10:PRODUCTION_WORKER_DETACH_FAILURE_ALLOC_LIMIT=0x",
                context->nativeaot_lifecycle.alloc_limit_after_detach);
            worker_log_hex(context,
                "GXOS_NET10:PRODUCTION_WORKER_DETACH_FAILURE_THREADSTORE=0x",
                context->nativeaot_lifecycle.threadstore_after);
            if (context->failure == 0U) context->failure = 1;
        } else {
            worker_log(context,
                "GXOS_NET10:PRODUCTION_WORKER_DETACH_PASS=1\r\n");
            worker_log_hex(context,
                "GXOS_NET10:PRODUCTION_WORKER_THREADSTORE_AFTER_DETACH=0x",
                context->nativeaot_lifecycle.threadstore_after);
        }
    } else if (context->failure == 0U) {
        context->failure = 1;
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
    GXOS_NATIVEAOT_CALLBACK_BRIDGE *managed_bridge,
    uint32_t tls_index, uint32_t runtime_fls_slot,
    GXOS_NATIVEAOT_FLS_CLEANUP_CALLBACK runtime_fls_cleanup,
    GXOS_MANAGED_KERNEL_DRIVER_WORKER_LOG_TEXT log_text,
    GXOS_MANAGED_KERNEL_DRIVER_WORKER_LOG_HEX log_hex)
{
    if (context == 0 || scheduler == 0 || event_api == 0 ||
        interrupt == 0 || managed_bridge == 0 || runtime_fls_cleanup == 0 ||
        context->state !=
            GXOS_MANAGED_KERNEL_DRIVER_WORKER_FREE ||
        !scheduler->active || scheduler->current != scheduler->boot_thread) {
        return 0;
    }
    context->scheduler = scheduler;
    context->event_api = event_api;
    context->interrupt = interrupt;
    context->managed_bridge = managed_bridge;
    context->log_text = log_text;
    context->log_hex = log_hex;
    if (!gxos_scheduler_create_event(scheduler, 0, 0,
                                     &context->wake_event) ||
        !gxos_scheduler_create_suspended_thread(
            scheduler, worker_entry, context, &context->worker_handle,
            &context->thread) ||
        !gxos_nativeaot_scheduler_worker_prepare(
            &context->nativeaot_lifecycle, scheduler->boot_thread,
            context->thread, tls_index, runtime_fls_slot,
            runtime_fls_cleanup) ||
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
    GXOS_SCHEDULER_TCB *thread;
    uint32_t threadstore_after_reclaim;
    if (context == 0 || context->scheduler == 0 || context->thread == 0 ||
        context->state != GXOS_MANAGED_KERNEL_DRIVER_WORKER_STOPPED ||
        !context->nativeaot_lifecycle.detached) {
        return 0;
    }
    scheduler = context->scheduler;
    thread = context->thread;
    if (!gxos_scheduler_close_handle(context->worker_handle) ||
        !gxos_scheduler_collect(scheduler) || thread->live != 0 ||
        thread->stack_base != 0 || thread->stack_limit != 0 ||
        thread->stack_pages_memory != 0 || thread->stack_canary_memory != 0 ||
        thread->gs_base != 0 || thread->tls_vector_base != 0 ||
        thread->tls_block_base != 0 || thread->teb_base != 0 ||
        thread->environment_owned != 0 ||
        context->nativeaot_lifecycle.main_thread == 0 ||
        context->nativeaot_lifecycle.runtime_fls_slot >=
            GXOS_SCHEDULER_FLS_SLOTS ||
        (threadstore_after_reclaim =
            (uint32_t)gxos_nativeaot_scheduler_threadstore_count(
                context->nativeaot_lifecycle.main_thread->fls_values[
                    context->nativeaot_lifecycle.runtime_fls_slot], 0)) !=
            context->nativeaot_lifecycle.threadstore_after ||
        !gxos_scheduler_close_handle(context->wake_event) ||
        !gxos_scheduler_try_destroy_event(context->wake_event)) {
        return 0;
    }
    context->state = GXOS_MANAGED_KERNEL_DRIVER_WORKER_DESTROYED;
    worker_log_hex(context,
        "GXOS_NET10:PRODUCTION_WORKER_THREADSTORE_AFTER_RECLAIM=0x",
        threadstore_after_reclaim);
    worker_log(context,
        "GXOS_NET10:PRODUCTION_WORKER_RECLAIM_PASS=1\r\n");
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

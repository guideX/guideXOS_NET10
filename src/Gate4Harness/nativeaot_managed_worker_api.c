#include "nativeaot_managed_worker_api.h"

#include <stddef.h>

#define GXOS_PHASE61_PHASE_IN_MANAGED 8U
#define GXOS_PHASE61_PHASE_AFTER_MANAGED_RETURN 9U
#define GXOS_PHASE61_CYCLE_COUNT 12U

typedef struct {
    GXOS_PHASE53O_PROBE *probe;
    GXOS_NATIVEAOT_CALLBACK_BRIDGE *callback_bridge;
    GXOS_NATIVEAOT_CALLBACK_BRIDGE *gc_bridge;
    GXOS_NATIVEAOT_MANAGED_WORKER_API_RECORD record;
    GXOS_NATIVEAOT_MANAGED_WORKER_RESULT last_result;
    GXOS_NATIVEAOT_MANAGED_WORKER_HANDLE last_closed;
    uint8_t has_last_closed;
    uint8_t reserved[7];
} GXOS_NATIVEAOT_MANAGED_WORKER_API_CONTEXT;

_Static_assert(sizeof(GXOS_NATIVEAOT_MANAGED_WORKER_API_CONTEXT) <=
                   GXOS_NATIVEAOT_MANAGED_WORKER_API_STORAGE_SIZE,
               "Phase 61 opaque API storage is too small");

static GXOS_NATIVEAOT_MANAGED_WORKER_API_CONTEXT *phase61_context(
    GXOS_NATIVEAOT_MANAGED_WORKER_API *api)
{
    return (GXOS_NATIVEAOT_MANAGED_WORKER_API_CONTEXT *)(void *)api;
}

static void phase61_zero(uint8_t *bytes, size_t count)
{
    size_t index;
    for (index = 0; index != count; ++index) bytes[index] = 0;
}

static int phase61_handle_valid(GXOS_NATIVEAOT_MANAGED_WORKER_HANDLE handle)
{
    return handle.scheduler_slot != UINT32_MAX &&
           handle.worker_identity != 0 && handle.worker_generation != 0;
}

static int phase61_state_transition_allowed(
    GXOS_NATIVEAOT_MANAGED_WORKER_STATE from,
    GXOS_NATIVEAOT_MANAGED_WORKER_STATE to)
{
    return (from == GXOS_NATIVEAOT_MANAGED_WORKER_STATE_CREATED &&
                to == GXOS_NATIVEAOT_MANAGED_WORKER_STATE_SUBMITTED) ||
           (from == GXOS_NATIVEAOT_MANAGED_WORKER_STATE_SUBMITTED &&
                to == GXOS_NATIVEAOT_MANAGED_WORKER_STATE_RUNNING) ||
           (from == GXOS_NATIVEAOT_MANAGED_WORKER_STATE_RUNNING &&
                to == GXOS_NATIVEAOT_MANAGED_WORKER_STATE_COMPLETED) ||
           (from == GXOS_NATIVEAOT_MANAGED_WORKER_STATE_RUNNING &&
                to == GXOS_NATIVEAOT_MANAGED_WORKER_STATE_FAILED) ||
           (from == GXOS_NATIVEAOT_MANAGED_WORKER_STATE_COMPLETED &&
                to == GXOS_NATIVEAOT_MANAGED_WORKER_STATE_CLOSED) ||
           (from == GXOS_NATIVEAOT_MANAGED_WORKER_STATE_FAILED &&
                to == GXOS_NATIVEAOT_MANAGED_WORKER_STATE_CLOSED);
}

int gxos_nativeaot_managed_worker_api_validate_request(
    const GXOS_NATIVEAOT_MANAGED_WORKER_REQUEST *request)
{
    if (request == 0) {
        return GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_INVALID_ARGUMENT;
    }
    if (request->version != GXOS_NATIVEAOT_MANAGED_WORKER_API_VERSION) {
        return GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_INVALID_REQUEST_VERSION;
    }
    if (request->size != sizeof(*request)) {
        return GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_INVALID_REQUEST_SIZE;
    }
    if (request->payload_size > GXOS_NATIVEAOT_MANAGED_WORKER_API_PAYLOAD_MAX) {
        return GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_INVALID_ARGUMENT;
    }
    switch (request->operation_id) {
        case GXOS_NATIVEAOT_MANAGED_WORKER_OPERATION_ADD_ONE:
            return request->payload_size == 0 && request->argument0 <= 0xFFFEU &&
                       request->argument1 == 0
                ? GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK
                : GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_INVALID_ARGUMENT;
        case GXOS_NATIVEAOT_MANAGED_WORKER_OPERATION_GC_CHECK:
            return request->payload_size == 0 && request->argument0 <= 0xFFFFU &&
                       request->argument1 == 0
                ? GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK
                : GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_INVALID_ARGUMENT;
        default:
            return GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_UNSUPPORTED_OPERATION;
    }
}

int gxos_nativeaot_managed_worker_api_handle_equal(
    GXOS_NATIVEAOT_MANAGED_WORKER_HANDLE left,
    GXOS_NATIVEAOT_MANAGED_WORKER_HANDLE right)
{
    return left.scheduler_slot == right.scheduler_slot &&
           left.worker_identity == right.worker_identity &&
           left.worker_generation == right.worker_generation;
}

int gxos_nativeaot_managed_worker_api_state_transition(
    GXOS_NATIVEAOT_MANAGED_WORKER_STATE from,
    GXOS_NATIVEAOT_MANAGED_WORKER_STATE to)
{
    return phase61_state_transition_allowed(from, to);
}

static GXOS_NATIVEAOT_MANAGED_WORKER_STATUS phase61_validate_active_handle(
    GXOS_NATIVEAOT_MANAGED_WORKER_API *api,
    GXOS_NATIVEAOT_MANAGED_WORKER_HANDLE handle)
{
    GXOS_NATIVEAOT_MANAGED_WORKER_API_CONTEXT *context = phase61_context(api);
    if (context == 0 || !phase61_handle_valid(handle)) {
        return GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_INVALID_HANDLE;
    }
    if (context->record.active &&
        gxos_nativeaot_managed_worker_api_handle_equal(
            context->record.handle, handle)) {
        return GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK;
    }
    if (context->has_last_closed &&
        gxos_nativeaot_managed_worker_api_handle_equal(
            context->last_closed, handle)) {
        return GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_STALE_HANDLE;
    }
    return GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_INVALID_HANDLE;
}

static void phase61_result_init(
    GXOS_NATIVEAOT_MANAGED_WORKER_RESULT *result,
    GXOS_NATIVEAOT_MANAGED_WORKER_HANDLE handle)
{
    phase61_zero((uint8_t *)result, sizeof(*result));
    result->version = GXOS_NATIVEAOT_MANAGED_WORKER_API_VERSION;
    result->size = sizeof(*result);
    result->state = GXOS_NATIVEAOT_MANAGED_WORKER_STATE_CREATED;
    result->status = GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK;
    result->worker = handle;
}

static void phase61_mark_failed(
    GXOS_NATIVEAOT_MANAGED_WORKER_API_RECORD *record)
{
    record->result.status = GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_INTERNAL_FAILURE;
    record->result.result_code = -1;
    record->result.state = GXOS_NATIVEAOT_MANAGED_WORKER_STATE_FAILED;
    record->state = GXOS_NATIVEAOT_MANAGED_WORKER_STATE_FAILED;
}

static uintptr_t GXOS_PHASE53O_MS_ABI phase61_worker_entry(void *argument)
{
    GXOS_NATIVEAOT_MANAGED_WORKER_API_RECORD *record =
        (GXOS_NATIVEAOT_MANAGED_WORKER_API_RECORD *)argument;
    GXOS_PHASE53O_PROBE *probe;
    GXOS_NATIVEAOT_CALLBACK_BRIDGE *bridge;
    int32_t managed_result = 0;
    uint32_t callback_status = UINT32_MAX;
    uint32_t gc_delta = 0;
    uint32_t gc_generation = 0;
    uint32_t gc_checksum = 0;
    GXOS_SCHEDULER_REGISTER_SNAPSHOT snapshot = {0};
    int invoked = 0;
    int good = 1;

    if (record == 0 || record->state !=
            GXOS_NATIVEAOT_MANAGED_WORKER_STATE_SUBMITTED ||
        record->thread == 0 ||
        (gxos_scheduler_capture_registers(&snapshot),
         !gxos_nativeaot_scheduler_worker_mark_running(&record->lifecycle)) ||
        !gxos_scheduler_validate_worker_snapshot(record->thread, &snapshot) ||
        !gxos_nativeaot_managed_worker_api_state_transition(
            GXOS_NATIVEAOT_MANAGED_WORKER_STATE_SUBMITTED,
            GXOS_NATIVEAOT_MANAGED_WORKER_STATE_RUNNING)) {
        if (record != 0) phase61_mark_failed(record);
        return 0;
    }
    record->state = GXOS_NATIVEAOT_MANAGED_WORKER_STATE_RUNNING;
    record->result.state = GXOS_NATIVEAOT_MANAGED_WORKER_STATE_RUNNING;
    probe = record->probe;
    if (probe == 0 || probe->phase_in_managed == 0 ||
        probe->phase_after_managed == 0) {
        good = 0;
    } else {
        bridge = record->request.operation_id ==
            GXOS_NATIVEAOT_MANAGED_WORKER_OPERATION_GC_CHECK
            ? probe->gc_bridge : probe->callback_bridge;
        probe->phase_in_managed(GXOS_PHASE61_PHASE_IN_MANAGED);
        if (bridge == 0 ||
            !gxos_nativeaot_scheduler_worker_invoke(
                &record->lifecycle, bridge,
                (int32_t)record->request.argument0, &managed_result,
                &callback_status)) {
            good = 0;
        }
        probe = record->probe;
        probe->phase_after_managed(GXOS_PHASE61_PHASE_AFTER_MANAGED_RETURN);
        invoked = callback_status == GXOS_NATIVEAOT_CALLBACK_OK;
        record->result.managed_result = managed_result;
        if (record->request.operation_id ==
                GXOS_NATIVEAOT_MANAGED_WORKER_OPERATION_ADD_ONE) {
            if (!invoked || ((uint32_t)managed_result & 0xFFFFU) !=
                    record->request.argument0 + 1U) {
                good = 0;
            } else {
                record->result.output0 =
                    (uint32_t)managed_result & 0xFFFFU;
                record->result.output1 =
                    ((uint32_t)managed_result >> 16) & 0xFFFFU;
            }
        } else if (record->request.operation_id ==
                       GXOS_NATIVEAOT_MANAGED_WORKER_OPERATION_GC_CHECK) {
            if (!invoked || !gxos_nativeaot_gc_result_valid(
                    managed_result, record->request.argument0, &gc_delta,
                    &gc_generation, &gc_checksum) || gc_delta == 0U) {
                good = 0;
            } else {
                record->lifecycle.managed_root_survived = 1;
                record->result.output0 = gc_delta;
                record->result.output1 = gc_checksum;
            }
        } else {
            good = 0;
        }
    }

    /* A managed callback failure may still leave the runtime attached.  The
       proven detach guard remains the only authority for cleanup. */
    if (record->lifecycle.attached && !record->lifecycle.detached) {
        if (!gxos_nativeaot_scheduler_worker_detach(&record->lifecycle)) {
            good = 0;
        }
    }
    if (!good || callback_status != GXOS_NATIVEAOT_CALLBACK_OK ||
        record->lifecycle.ownership_state !=
            GXOS_NATIVEAOT_WORKER_OWNERSHIP_RUNTIME_DETACHED ||
        gxos_scheduler_get_fls(probe->runtime_fls_slot) != 0) {
        phase61_mark_failed(record);
        return (uintptr_t)record->result.result_code;
    }
    record->result.result_code = 0;
    record->result.status = GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK;
    record->result.state = GXOS_NATIVEAOT_MANAGED_WORKER_STATE_COMPLETED;
    record->state = GXOS_NATIVEAOT_MANAGED_WORKER_STATE_COMPLETED;
    return 0;
}

int gxos_nativeaot_managed_worker_api_initialize(
    GXOS_NATIVEAOT_MANAGED_WORKER_API *api,
    GXOS_PHASE53O_PROBE *probe)
{
    GXOS_NATIVEAOT_MANAGED_WORKER_API_CONTEXT *context = phase61_context(api);
    if (api == 0 || probe == 0 || probe->scheduler == 0 ||
        probe->main_thread == 0 || probe->callback_bridge == 0 ||
        probe->gc_bridge == 0 || probe->runtime_fls_cleanup == 0 ||
        probe->vm_region_count == 0 ||
        probe->main_thread != gxos_scheduler_current_thread()) {
        return 0;
    }
    phase61_zero((uint8_t *)context, sizeof(*context));
    context->probe = probe;
    context->callback_bridge = probe->callback_bridge;
    context->gc_bridge = probe->gc_bridge;
    return 1;
}

static void phase61_discard_unstarted(
    GXOS_NATIVEAOT_MANAGED_WORKER_API *api)
{
    GXOS_NATIVEAOT_MANAGED_WORKER_API_CONTEXT *context = phase61_context(api);
    GXOS_NATIVEAOT_MANAGED_WORKER_API_RECORD *record = &context->record;
    if (record->scheduler_handle != 0) {
        (void)gxos_scheduler_close_handle(record->scheduler_handle);
    }
    if (record->thread != 0 && record->thread->live) {
        (void)gxos_scheduler_discard_created_thread(record->thread);
    }
    (void)gxos_scheduler_collect(context->probe->scheduler);
}

GXOS_NATIVEAOT_MANAGED_WORKER_STATUS
gxos_nativeaot_managed_worker_api_create(
    GXOS_NATIVEAOT_MANAGED_WORKER_API *api,
    GXOS_NATIVEAOT_MANAGED_WORKER_HANDLE *handle_out)
{
    GXOS_NATIVEAOT_MANAGED_WORKER_API_CONTEXT *context = phase61_context(api);
    GXOS_NATIVEAOT_MANAGED_WORKER_API_RECORD *record;
    GXOS_SCHEDULER_TCB *thread = 0;
    GXOS_SCHEDULER_HANDLE scheduler_handle = 0;
    if (handle_out != 0) *handle_out = (GXOS_NATIVEAOT_MANAGED_WORKER_HANDLE){0};
    if (context == 0 || context->probe == 0 || handle_out == 0) {
        return GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_INVALID_ARGUMENT;
    }
    if (context->record.active) {
        return GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_INVALID_STATE;
    }
    phase61_zero((uint8_t *)&context->record, sizeof(context->record));
    record = &context->record;
    record->probe = context->probe;
    record->state = GXOS_NATIVEAOT_MANAGED_WORKER_STATE_FREE;
    if (!gxos_scheduler_create_suspended_thread(
            context->probe->scheduler, phase61_worker_entry, record,
            &scheduler_handle, &thread) || thread == 0 ||
        !gxos_nativeaot_scheduler_worker_prepare(
            &record->lifecycle, context->probe->main_thread, thread,
            context->probe->tls_index, context->probe->runtime_fls_slot,
            context->probe->runtime_fls_cleanup)) {
        record->scheduler_handle = scheduler_handle;
        record->thread = thread;
        if (thread != 0 && thread->live) phase61_discard_unstarted(api);
        phase61_zero((uint8_t *)&context->record, sizeof(context->record));
        return GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_INTERNAL_FAILURE;
    }
    record->scheduler_handle = scheduler_handle;
    record->thread = thread;
    record->handle.scheduler_slot = gxos_scheduler_thread_slot(thread);
    record->handle.worker_identity = thread->identity;
    record->handle.worker_generation = thread->generation;
    record->result.worker = record->handle;
    phase61_result_init(&record->result, record->handle);
    record->result.state = GXOS_NATIVEAOT_MANAGED_WORKER_STATE_CREATED;
    record->state = GXOS_NATIVEAOT_MANAGED_WORKER_STATE_CREATED;
    record->active = 1;
    *handle_out = record->handle;
    return GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK;
}

GXOS_NATIVEAOT_MANAGED_WORKER_STATUS
gxos_nativeaot_managed_worker_api_submit(
    GXOS_NATIVEAOT_MANAGED_WORKER_API *api,
    GXOS_NATIVEAOT_MANAGED_WORKER_HANDLE handle,
    const GXOS_NATIVEAOT_MANAGED_WORKER_REQUEST *request)
{
    GXOS_NATIVEAOT_MANAGED_WORKER_API_CONTEXT *context = phase61_context(api);
    GXOS_NATIVEAOT_MANAGED_WORKER_STATUS status;
    uint32_t previous_suspend_count = 0;
    if (request == 0) {
        return GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_INVALID_ARGUMENT;
    }
    status = (GXOS_NATIVEAOT_MANAGED_WORKER_STATUS)
        gxos_nativeaot_managed_worker_api_validate_request(request);
    if (status != GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK) return status;
    status = phase61_validate_active_handle(api, handle);
    if (status != GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK) return status;
    if (context->record.state != GXOS_NATIVEAOT_MANAGED_WORKER_STATE_CREATED) {
        return GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_DUPLICATE_SUBMIT;
    }
    context->record.request = *request;
    context->record.result.operation_id = request->operation_id;
    if (!gxos_scheduler_resume_thread(
            context->record.scheduler_handle, &previous_suspend_count) ||
        previous_suspend_count != 1U ||
        !gxos_nativeaot_scheduler_worker_mark_runnable(
            &context->record.lifecycle) ||
        !gxos_nativeaot_managed_worker_api_state_transition(
            GXOS_NATIVEAOT_MANAGED_WORKER_STATE_CREATED,
            GXOS_NATIVEAOT_MANAGED_WORKER_STATE_SUBMITTED)) {
        phase61_mark_failed(&context->record);
        return GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_INTERNAL_FAILURE;
    }
    context->record.state = GXOS_NATIVEAOT_MANAGED_WORKER_STATE_SUBMITTED;
    context->record.result.state = GXOS_NATIVEAOT_MANAGED_WORKER_STATE_SUBMITTED;
    return GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK;
}

GXOS_NATIVEAOT_MANAGED_WORKER_STATUS
gxos_nativeaot_managed_worker_api_drive(
    GXOS_NATIVEAOT_MANAGED_WORKER_API *api,
    GXOS_NATIVEAOT_MANAGED_WORKER_HANDLE handle)
{
    GXOS_NATIVEAOT_MANAGED_WORKER_API_CONTEXT *context = phase61_context(api);
    GXOS_SCHEDULER_REGISTER_SNAPSHOT snapshot = {0};
    GXOS_NATIVEAOT_MANAGED_WORKER_STATUS status =
        phase61_validate_active_handle(api, handle);
    if (status != GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK) return status;
    if (context->record.state != GXOS_NATIVEAOT_MANAGED_WORKER_STATE_SUBMITTED ||
        gxos_scheduler_current_thread() != context->probe->main_thread) {
        return GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_INVALID_STATE;
    }
    gxos_scheduler_main_dispatch(&snapshot);
    if (context->record.state == GXOS_NATIVEAOT_MANAGED_WORKER_STATE_SUBMITTED) {
        phase61_mark_failed(&context->record);
    }
    return GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK;
}

GXOS_NATIVEAOT_MANAGED_WORKER_STATUS
gxos_nativeaot_managed_worker_api_poll(
    GXOS_NATIVEAOT_MANAGED_WORKER_API *api,
    GXOS_NATIVEAOT_MANAGED_WORKER_HANDLE handle,
    GXOS_NATIVEAOT_MANAGED_WORKER_RESULT *result_out)
{
    GXOS_NATIVEAOT_MANAGED_WORKER_API_CONTEXT *context = phase61_context(api);
    GXOS_NATIVEAOT_MANAGED_WORKER_STATUS status =
        phase61_validate_active_handle(api, handle);
    if (status != GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK) return status;
    if (result_out != 0) *result_out = context->record.result;
    if (context->record.state != GXOS_NATIVEAOT_MANAGED_WORKER_STATE_COMPLETED &&
        context->record.state != GXOS_NATIVEAOT_MANAGED_WORKER_STATE_FAILED) {
        return GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_NOT_COMPLETE;
    }
    return (GXOS_NATIVEAOT_MANAGED_WORKER_STATUS)
        context->record.result.status;
}

GXOS_NATIVEAOT_MANAGED_WORKER_STATUS
gxos_nativeaot_managed_worker_api_close(
    GXOS_NATIVEAOT_MANAGED_WORKER_API *api,
    GXOS_NATIVEAOT_MANAGED_WORKER_HANDLE handle,
    GXOS_NATIVEAOT_MANAGED_WORKER_RESULT *result_out)
{
    GXOS_NATIVEAOT_MANAGED_WORKER_API_CONTEXT *context = phase61_context(api);
    GXOS_NATIVEAOT_MANAGED_WORKER_STATUS status;
    GXOS_NATIVEAOT_MANAGED_WORKER_API_RECORD *record;
    int reclaimed;
    if (api == 0 || !phase61_handle_valid(handle)) {
        return GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_INVALID_HANDLE;
    }
    if (!context->record.active && context->has_last_closed &&
        gxos_nativeaot_managed_worker_api_handle_equal(
            context->last_closed, handle)) {
        return GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_DUPLICATE_CLOSE;
    }
    status = phase61_validate_active_handle(api, handle);
    if (status != GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK) return status;
    record = &context->record;
    if (record->state != GXOS_NATIVEAOT_MANAGED_WORKER_STATE_COMPLETED &&
        record->state != GXOS_NATIVEAOT_MANAGED_WORKER_STATE_FAILED) {
        return GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_CLOSE_BEFORE_COMPLETE;
    }
    if (record->thread == 0 ||
        !gxos_scheduler_thread_is_terminated(record->thread) ||
        record->lifecycle.ownership_state !=
            GXOS_NATIVEAOT_WORKER_OWNERSHIP_RUNTIME_DETACHED) {
        return GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_INTERNAL_FAILURE;
    }
    if (!gxos_nativeaot_scheduler_worker_note_reclaimable(
            &record->lifecycle) ||
        !gxos_scheduler_close_handle(record->scheduler_handle) ||
        !gxos_scheduler_collect(context->probe->scheduler) ||
        !gxos_nativeaot_scheduler_worker_note_reclaimed(
            &record->lifecycle)) {
        return GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_INTERNAL_FAILURE;
    }
    reclaimed = record->lifecycle.ownership_state ==
        GXOS_NATIVEAOT_WORKER_OWNERSHIP_RECLAIMED &&
        record->thread->live == 0;
    if (!reclaimed) return GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_INTERNAL_FAILURE;
    if (result_out != 0) *result_out = record->result;
    context->last_result = record->result;
    context->last_closed = handle;
    context->has_last_closed = 1;
    record->state = GXOS_NATIVEAOT_MANAGED_WORKER_STATE_CLOSED;
    record->result.state = GXOS_NATIVEAOT_MANAGED_WORKER_STATE_CLOSED;
    record->active = 0;
    return GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK;
}

static uint32_t phase61_live_threads(const GXOS_SCHEDULER *scheduler)
{
    uint32_t index;
    uint32_t count = 0;
    volatile const GXOS_SCHEDULER_TCB *threads = scheduler->threads;
    for (index = 0; index != GXOS_SCHEDULER_MAX_THREADS; ++index) {
        if (threads[index].live) ++count;
    }
    return count;
}

static uint32_t phase61_live_objects(const GXOS_SCHEDULER *scheduler)
{
    uint32_t index;
    uint32_t count = 0;
    volatile const GXOS_SCHEDULER_OBJECT *objects = scheduler->objects;
    for (index = 0; index != GXOS_SCHEDULER_MAX_OBJECTS; ++index) {
        if (objects[index].live) ++count;
    }
    return count;
}

static void phase61_text(GXOS_PHASE53O_PROBE *probe, const char *text)
{
    probe->log_text(text);
}

static void phase61_hex(GXOS_PHASE53O_PROBE *probe, const char *name,
                        uint64_t value)
{
    probe->log_hex(name, value);
    probe->log_text("\r\n");
}

int gxos_nativeaot_managed_worker_api_probe(
    GXOS_NATIVEAOT_MANAGED_WORKER_API *api)
{
    GXOS_NATIVEAOT_MANAGED_WORKER_API_CONTEXT *context = phase61_context(api);
    GXOS_NATIVEAOT_MANAGED_WORKER_HANDLE previous = {0};
    uint32_t baseline_vm;
    uint32_t baseline_threads;
    uint32_t baseline_objects;
    uint32_t previous_generation = 0;
    uint32_t cycle;
    if (context == 0 || context->probe == 0 || context->probe->log_text == 0 ||
        context->probe->log_hex == 0) return 0;
    baseline_vm = *context->probe->vm_region_count;
    baseline_threads = phase61_live_threads(context->probe->scheduler);
    baseline_objects = phase61_live_objects(context->probe->scheduler);
    phase61_text(context->probe, "GXOS_NET10:PHASE61_BEGIN\r\n");
    phase61_text(context->probe,
                 "GXOS_NET10:PHASE61_API=BOUNDED_MANAGED_WORKER\r\n");
    phase61_hex(context->probe, "GXOS_NET10:PHASE61_REQUEST_VERSION=0x",
                GXOS_NATIVEAOT_MANAGED_WORKER_API_VERSION);
    phase61_hex(context->probe, "GXOS_NET10:PHASE61_REQUEST_SIZE=0x",
                sizeof(GXOS_NATIVEAOT_MANAGED_WORKER_REQUEST));
    phase61_text(context->probe,
                 "GXOS_NET10:PHASE61_HANDLE=SLOT_IDENTITY_GENERATION\r\n");
    phase61_text(context->probe,
                 "GXOS_NET10:PHASE61_STATE_SEQUENCE=CREATED>SUBMITTED>RUNNING>COMPLETED>CLOSED\r\n");
    phase61_text(context->probe,
                 "GXOS_NET10:PHASE61_OPERATIONS=ADD_ONE,GC_CHECK\r\n");
    phase61_hex(context->probe, "GXOS_NET10:PHASE61_RESOURCE_BASELINE_VM=0x",
                baseline_vm);
    phase61_hex(context->probe, "GXOS_NET10:PHASE61_RESOURCE_BASELINE_THREADS=0x",
                baseline_threads);
    phase61_hex(context->probe, "GXOS_NET10:PHASE61_RESOURCE_BASELINE_OBJECTS=0x",
                baseline_objects);

    for (cycle = 0; cycle != GXOS_PHASE61_CYCLE_COUNT; ++cycle) {
        GXOS_NATIVEAOT_MANAGED_WORKER_HANDLE handle;
        GXOS_NATIVEAOT_MANAGED_WORKER_REQUEST request = {0};
        GXOS_NATIVEAOT_MANAGED_WORKER_RESULT result = {0};
        GXOS_NATIVEAOT_MANAGED_WORKER_RESULT closed_result = {0};
        GXOS_NATIVEAOT_MANAGED_WORKER_STATUS status;
        uint32_t seed = 0x61U + cycle;
        int is_gc = (cycle & 1U) != 0;
        request.version = GXOS_NATIVEAOT_MANAGED_WORKER_API_VERSION;
        request.size = sizeof(request);
        request.operation_id = (uint16_t)(is_gc
            ? GXOS_NATIVEAOT_MANAGED_WORKER_OPERATION_GC_CHECK
            : GXOS_NATIVEAOT_MANAGED_WORKER_OPERATION_ADD_ONE);
        request.argument0 = is_gc ? seed : 0x20U + cycle;
        status = gxos_nativeaot_managed_worker_api_create(api, &handle);
        if (status != GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK) return 0;
        if (cycle != 0U &&
            gxos_nativeaot_managed_worker_api_poll(api, previous, &result) !=
                GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_STALE_HANDLE) return 0;
        if (cycle == 0U) {
            GXOS_NATIVEAOT_MANAGED_WORKER_REQUEST invalid = request;
            invalid.version = 99U;
            if (gxos_nativeaot_managed_worker_api_submit(api, handle, &invalid) !=
                    GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_INVALID_REQUEST_VERSION) return 0;
            invalid = request;
            invalid.operation_id = 99U;
            if (gxos_nativeaot_managed_worker_api_submit(api, handle, &invalid) !=
                    GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_UNSUPPORTED_OPERATION) return 0;
        }
        if (gxos_nativeaot_managed_worker_api_submit(api, handle, &request) !=
                GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK ||
            gxos_nativeaot_managed_worker_api_submit(api, handle, &request) !=
                GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_DUPLICATE_SUBMIT ||
            gxos_nativeaot_managed_worker_api_close(api, handle, 0) !=
                GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_CLOSE_BEFORE_COMPLETE ||
            gxos_nativeaot_managed_worker_api_drive(api, handle) !=
                GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK ||
            gxos_nativeaot_managed_worker_api_poll(api, handle, &result) !=
                GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK) return 0;
        if (result.worker.worker_identity != handle.worker_identity ||
            result.worker.worker_generation != handle.worker_generation ||
            result.operation_id != request.operation_id || result.result_code != 0 ||
            (is_gc && (result.output0 == 0 || result.output1 !=
                       (gxos_nativeaot_gc_expected_checksum(seed) & 0x0FFFU))) ||
            (!is_gc && result.output0 != request.argument0 + 1U)) return 0;
        if (previous_generation != 0U &&
            handle.worker_generation == previous_generation) return 0;
        if (gxos_nativeaot_managed_worker_api_close(api, handle, &closed_result) !=
                GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK ||
            closed_result.worker.worker_identity != handle.worker_identity ||
            gxos_nativeaot_managed_worker_api_poll(api, handle, &result) !=
                GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_STALE_HANDLE ||
            gxos_nativeaot_managed_worker_api_close(api, handle, 0) !=
                GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_DUPLICATE_CLOSE ||
            *context->probe->vm_region_count != baseline_vm ||
            phase61_live_threads(context->probe->scheduler) != baseline_threads ||
            phase61_live_objects(context->probe->scheduler) != baseline_objects) return 0;
        previous = handle;
        previous_generation = handle.worker_generation;
        phase61_hex(context->probe, "GXOS_NET10:PHASE61_CYCLE=0x", cycle + 1U);
        phase61_hex(context->probe, "GXOS_NET10:PHASE61_RESULT=0x", result.output0);
    }
    phase61_text(context->probe,
                 "GXOS_NET10:PHASE61_REQUEST_VALIDATION_OK=1\r\n");
    phase61_text(context->probe,
                 "GXOS_NET10:PHASE61_DUPLICATE_SUBMIT_REJECTED=1\r\n");
    phase61_text(context->probe,
                 "GXOS_NET10:PHASE61_CLOSE_BEFORE_COMPLETE_REJECTED=1\r\n");
    phase61_text(context->probe,
                 "GXOS_NET10:PHASE61_DUPLICATE_CLOSE_REJECTED=1\r\n");
    phase61_text(context->probe,
                 "GXOS_NET10:PHASE61_STALE_HANDLE_REJECTED=1\r\n");
    phase61_text(context->probe,
                 "GXOS_NET10:PHASE61_GENERATION_ADVANCEMENT_OK=1\r\n");
    phase61_text(context->probe,
                 "GXOS_NET10:PHASE61_SEQUENTIAL_CALLER_ISOLATION_OK=1\r\n");
    phase61_hex(context->probe, "GXOS_NET10:PHASE61_RESOURCE_AFTER_VM=0x",
                *context->probe->vm_region_count);
    phase61_hex(context->probe, "GXOS_NET10:PHASE61_RESOURCE_AFTER_THREADS=0x",
                phase61_live_threads(context->probe->scheduler));
    phase61_hex(context->probe, "GXOS_NET10:PHASE61_RESOURCE_AFTER_OBJECTS=0x",
                phase61_live_objects(context->probe->scheduler));
    phase61_text(context->probe, "GXOS_NET10:PHASE61_CYCLES=12\r\n");
    phase61_text(context->probe, "GXOS_NET10:PHASE61_COMPLETE=1\r\n");
    phase61_text(context->probe, "GXOS_NET10:PHASE61_PASS=1\r\n");
    return 1;
}

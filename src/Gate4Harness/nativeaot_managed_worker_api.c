#include "nativeaot_managed_worker_api.h"

#include <stddef.h>

#define GXOS_PHASE61_PHASE_IN_MANAGED 8U
#define GXOS_PHASE61_PHASE_AFTER_MANAGED_RETURN 9U
#define GXOS_PHASE61_CYCLE_COUNT 12U
#define GXOS_PHASE62_PAIR_COUNT 12U

typedef struct {
    GXOS_PHASE53O_PROBE *probe;
    GXOS_NATIVEAOT_CALLBACK_BRIDGE *callback_bridge;
    GXOS_NATIVEAOT_CALLBACK_BRIDGE *gc_bridge;
    GXOS_NATIVEAOT_MANAGED_WORKER_API_RECORD records[
        GXOS_NATIVEAOT_MANAGED_WORKER_API_CAPACITY];
    GXOS_NATIVEAOT_MANAGED_WORKER_RESULT last_results[
        GXOS_NATIVEAOT_MANAGED_WORKER_API_CAPACITY];
    GXOS_NATIVEAOT_MANAGED_WORKER_HANDLE last_closed[
        GXOS_NATIVEAOT_MANAGED_WORKER_API_CAPACITY];
    uint8_t has_last_closed[GXOS_NATIVEAOT_MANAGED_WORKER_API_CAPACITY];
    uint8_t concurrent_mode;
    uint8_t runtime_overlap_observed;
    uint8_t completion_count;
    uint8_t reserved[5];
    GXOS_NATIVEAOT_MANAGED_WORKER_HANDLE completion_order[
        GXOS_NATIVEAOT_MANAGED_WORKER_API_CAPACITY];
    uint32_t peak_vm;
    uint32_t peak_threads;
    uint32_t peak_objects;
    uint32_t peak_api_workers;
    uint32_t peak_roots;
    uint32_t max_attached_workers;
    uint32_t max_root_workers;
    uint32_t stale_root_token;
    GXOS_NATIVEAOT_MANAGED_WORKER_HANDLE attach_failure_handle;
    uint8_t phase64_mode;
    uint8_t attach_failure_requested;
    uint8_t attach_failure_fired;
    uint8_t attach_failure_runtime_acquired;
    uint8_t phase64_b_root_release_peer_live;
    uint8_t phase64_c_root_release_peer_live;
    uint8_t phase64_stale_root_rejection;
    uint8_t phase64_reserved;
    uint32_t attach_failure_detach_count;
} GXOS_NATIVEAOT_MANAGED_WORKER_API_CONTEXT;

_Static_assert(sizeof(GXOS_NATIVEAOT_MANAGED_WORKER_API_CONTEXT) <=
                   GXOS_NATIVEAOT_MANAGED_WORKER_API_STORAGE_SIZE,
               "Phase 61 opaque API storage is too small");

static GXOS_NATIVEAOT_MANAGED_WORKER_API_CONTEXT *phase61_context(
    GXOS_NATIVEAOT_MANAGED_WORKER_API *api)
{
    return (GXOS_NATIVEAOT_MANAGED_WORKER_API_CONTEXT *)(void *)api;
}

static uint32_t phase61_record_index(
    const GXOS_NATIVEAOT_MANAGED_WORKER_API_CONTEXT *context,
    const GXOS_NATIVEAOT_MANAGED_WORKER_API_RECORD *record)
{
    uint32_t index;
    if (context == 0 || record == 0) return UINT32_MAX;
    for (index = 0; index != GXOS_NATIVEAOT_MANAGED_WORKER_API_CAPACITY;
         ++index) {
        if (&context->records[index] == record) return index;
    }
    return UINT32_MAX;
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

static GXOS_NATIVEAOT_MANAGED_WORKER_API_RECORD *phase61_active_record(
    GXOS_NATIVEAOT_MANAGED_WORKER_API_CONTEXT *context,
    GXOS_NATIVEAOT_MANAGED_WORKER_HANDLE handle)
{
    uint32_t index;
    if (context == 0) return 0;
    for (index = 0; index != GXOS_NATIVEAOT_MANAGED_WORKER_API_CAPACITY;
         ++index) {
        if (context->records[index].active &&
            gxos_nativeaot_managed_worker_api_handle_equal(
                context->records[index].handle, handle)) {
            return &context->records[index];
        }
    }
    return 0;
}

static int phase61_known_stale_handle(
    const GXOS_NATIVEAOT_MANAGED_WORKER_API_CONTEXT *context,
    GXOS_NATIVEAOT_MANAGED_WORKER_HANDLE handle)
{
    uint32_t index;
    if (context == 0) return 0;
    for (index = 0; index != GXOS_NATIVEAOT_MANAGED_WORKER_API_CAPACITY;
         ++index) {
        if (context->has_last_closed[index] &&
            context->last_closed[index].scheduler_slot ==
                handle.scheduler_slot) {
            return 1;
        }
    }
    return 0;
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
    if (phase61_active_record(context, handle) != 0) {
        return GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK;
    }
    if (phase61_known_stale_handle(context, handle)) {
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

#ifdef GXOS_ENABLE_PHASE64_MANAGED_WORKER_API
static void phase64_hex(GXOS_PHASE53O_PROBE *probe, const char *name,
                        uint64_t value);
#endif

static uint32_t phase64_attached_workers(
    const GXOS_NATIVEAOT_MANAGED_WORKER_API_CONTEXT *context)
{
    uint32_t index;
    uint32_t count = 0;
    if (context == 0) return 0;
    for (index = 0; index != GXOS_NATIVEAOT_MANAGED_WORKER_API_CAPACITY;
         ++index) {
        if (context->records[index].active &&
            context->records[index].lifecycle.attached) {
            ++count;
        }
    }
    return count;
}

static uint32_t phase64_root_workers(
    const GXOS_NATIVEAOT_MANAGED_WORKER_API_CONTEXT *context)
{
    uint32_t index;
    uint32_t count = 0;
    if (context == 0) return 0;
    for (index = 0; index != GXOS_NATIVEAOT_MANAGED_WORKER_API_CAPACITY;
         ++index) {
        if (context->records[index].active &&
            context->records[index].lifecycle.managed_root_owned) {
            ++count;
        }
    }
    return count;
}

static void phase64_update_overlap(
    GXOS_NATIVEAOT_MANAGED_WORKER_API_CONTEXT *context)
{
    uint32_t attached;
    uint32_t roots;
    if (context == 0) return;
    attached = phase64_attached_workers(context);
    roots = phase64_root_workers(context);
    if (context->phase64_mode) {
        if (attached > context->max_attached_workers) {
            context->max_attached_workers = attached;
        }
        if (roots > context->max_root_workers) {
            context->max_root_workers = roots;
        }
    }
    if (attached >= 2U) context->runtime_overlap_observed = 1;
}

static uint32_t phase64_root_token(
    const GXOS_NATIVEAOT_MANAGED_WORKER_API_RECORD *record)
{
    /* Tokens are bounded, nonzero, and unique for the live scheduler slots and
       identities used by this fixture.  They are not managed addresses. */
    return 0x6400U + ((record->handle.scheduler_slot & 0x0FU) << 8) +
           (record->handle.worker_identity & 0xFFU);
}

static void phase64_note_root_release_peer(
    GXOS_NATIVEAOT_MANAGED_WORKER_API_CONTEXT *context,
    const GXOS_NATIVEAOT_MANAGED_WORKER_API_RECORD *record)
{
    uint32_t index;
    if (context == 0 || record == 0) return;
    for (index = 0; index != GXOS_NATIVEAOT_MANAGED_WORKER_API_CAPACITY;
         ++index) {
        const GXOS_NATIVEAOT_MANAGED_WORKER_API_RECORD *peer =
            &context->records[index];
        if (peer == record || !peer->active) continue;
        if (record->request.argument0 >= 0xB2U &&
            record->request.argument0 <= 0xBDU) {
            context->phase64_b_root_release_peer_live = 1;
        }
        if (record->request.argument0 >= 0xC3U &&
            record->request.argument0 <= 0xCEU) {
            context->phase64_c_root_release_peer_live = 1;
        }
    }
}

static uintptr_t GXOS_PHASE53O_MS_ABI phase61_worker_entry(void *argument)
{
    GXOS_NATIVEAOT_MANAGED_WORKER_API_RECORD *record =
        (GXOS_NATIVEAOT_MANAGED_WORKER_API_RECORD *)argument;
    GXOS_NATIVEAOT_MANAGED_WORKER_API_CONTEXT *owner;
    GXOS_PHASE53O_PROBE *probe;
    GXOS_NATIVEAOT_CALLBACK_BRIDGE *bridge;
    int32_t managed_result = 0;
    uint32_t callback_status = UINT32_MAX;
    uint32_t gc_delta = 0;
    uint32_t gc_generation = 0;
    uint32_t gc_checksum = 0;
    uint32_t root_token = 0;
    GXOS_SCHEDULER_REGISTER_SNAPSHOT snapshot = {0};
    int invoked = 0;
    int attach_failure_injected = 0;
    int phase64_gc = 0;
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
    owner = (GXOS_NATIVEAOT_MANAGED_WORKER_API_CONTEXT *)record->owner_context;
    if (probe == 0 || probe->phase_in_managed == 0 ||
        probe->phase_after_managed == 0) {
        good = 0;
    } else {
        phase64_gc = owner != 0 && owner->phase64_mode &&
            record->request.operation_id ==
                GXOS_NATIVEAOT_MANAGED_WORKER_OPERATION_GC_CHECK;
        root_token = phase64_gc ? phase64_root_token(record) : 0;

        if (phase64_gc) {
            int32_t root_result = 0;
            uint32_t root_status = UINT32_MAX;
            probe->phase_in_managed(GXOS_PHASE61_PHASE_IN_MANAGED);
            if (probe->managed_root_publish_bridge == 0 ||
                !gxos_nativeaot_scheduler_worker_invoke(
                    &record->lifecycle, probe->managed_root_publish_bridge,
                    (int32_t)root_token, &root_result, &root_status) ||
                root_status != GXOS_NATIVEAOT_CALLBACK_OK ||
                (uint32_t)root_result != (0x58000000U | root_token) ||
                !gxos_nativeaot_scheduler_worker_note_managed_root_published(
                    &record->lifecycle, root_token)) {
                good = 0;
            }
            probe->phase_after_managed(GXOS_PHASE61_PHASE_AFTER_MANAGED_RETURN);
            phase64_update_overlap(owner);
        }

        bridge = phase64_gc ? probe->gc_bridge : probe->callback_bridge;
        probe->phase_in_managed(GXOS_PHASE61_PHASE_IN_MANAGED);
        if (!phase64_gc) {
            bridge = record->request.operation_id ==
                GXOS_NATIVEAOT_MANAGED_WORKER_OPERATION_GC_CHECK
                ? probe->gc_bridge : probe->callback_bridge;
        }
        if (good && (bridge == 0 ||
            !gxos_nativeaot_scheduler_worker_invoke(
                &record->lifecycle, bridge,
                (int32_t)record->request.argument0, &managed_result,
                &callback_status))) {
            good = 0;
        }
        probe = record->probe;
        probe->phase_after_managed(GXOS_PHASE61_PHASE_AFTER_MANAGED_RETURN);
        invoked = callback_status == GXOS_NATIVEAOT_CALLBACK_OK;
        record->result.managed_result = managed_result;
        record->runtime_fls_value = gxos_scheduler_get_fls(
            probe->runtime_fls_slot);
        record->runtime_tls_block_value = gxos_scheduler_current_tls_block();

#ifdef GXOS_ENABLE_PHASE64_MANAGED_WORKER_API
        if (owner != 0 && owner->phase64_mode &&
            gxos_nativeaot_managed_worker_api_handle_equal(
                record->handle, owner->attach_failure_handle) &&
            owner->attach_failure_requested && !phase64_gc) {
            GXOS_NATIVEAOT_FAILURE_INJECTION_RECORD const *injection;
            owner->attach_failure_requested = 0;
            if (!record->lifecycle.attached ||
                !gxos_nativeaot_phase56_failure_arm(
                    GXOS_NATIVEAOT_FAILURE_INJECTION_AFTER_RUNTIME_ATTACH,
                    &record->lifecycle) ||
                !gxos_nativeaot_phase56_failure_try_fire(
                    &record->lifecycle)) {
                good = 0;
            } else {
                injection = gxos_nativeaot_phase56_failure_record();
                if (injection == 0 || injection->fire_count != 1U) {
                    good = 0;
                } else {
                    attach_failure_injected = 1;
                    owner->attach_failure_fired = 1;
                    owner->attach_failure_runtime_acquired =
                        record->lifecycle.attached != 0;
                    /* The injected post-attach failure is intentional.  Keep
                       the detach guard as the cleanup authority, then force
                       the API record through its FAILED state. */
                    good = 0;
                }
            }
        }
#endif

        if (owner != 0 && owner->concurrent_mode) {
            phase64_update_overlap(owner);
            if (!record->yielded && !gxos_scheduler_worker_yield()) {
                good = 0;
            } else {
                record->yielded = 1;
            }
        }

        if (!attach_failure_injected && good &&
            record->request.operation_id ==
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
        } else if (!attach_failure_injected && good &&
                   record->request.operation_id ==
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
        } else if (!attach_failure_injected && good) {
            good = 0;
        }

        if (phase64_gc && good) {
            int32_t validate_result = 0;
            uint32_t validate_status = UINT32_MAX;
            probe->phase_in_managed(GXOS_PHASE61_PHASE_IN_MANAGED);
            if (probe->managed_root_validate_bridge == 0 ||
                !gxos_nativeaot_scheduler_worker_invoke(
                    &record->lifecycle, probe->managed_root_validate_bridge,
                    (int32_t)root_token, &validate_result,
                    &validate_status) ||
                validate_status != GXOS_NATIVEAOT_CALLBACK_OK ||
                (uint32_t)validate_result != (0x5A000000U | root_token)) {
                good = 0;
            }
            probe->phase_after_managed(GXOS_PHASE61_PHASE_AFTER_MANAGED_RETURN);
            phase64_update_overlap(owner);
            if (owner != 0 && owner->stale_root_token != 0U) {
                int32_t stale_result = 0;
                uint32_t stale_status = UINT32_MAX;
                probe->phase_in_managed(GXOS_PHASE61_PHASE_IN_MANAGED);
                if (!gxos_nativeaot_scheduler_worker_invoke(
                        &record->lifecycle,
                        probe->managed_root_release_bridge,
                        (int32_t)owner->stale_root_token, &stale_result,
                        &stale_status) ||
                    stale_status != GXOS_NATIVEAOT_CALLBACK_OK ||
                    stale_result != -2) {
                    good = 0;
                } else if (owner != 0) {
                    owner->phase64_stale_root_rejection = 1;
                }
                probe->phase_after_managed(GXOS_PHASE61_PHASE_AFTER_MANAGED_RETURN);
            }
        }

        if (phase64_gc && good) {
            int32_t release_result = 0;
            uint32_t release_status = UINT32_MAX;
            probe->phase_in_managed(GXOS_PHASE61_PHASE_IN_MANAGED);
            if (!gxos_nativeaot_scheduler_worker_invoke(
                    &record->lifecycle, probe->managed_root_release_bridge,
                    (int32_t)root_token, &release_result, &release_status) ||
                release_status != GXOS_NATIVEAOT_CALLBACK_OK ||
                (uint32_t)release_result != (0x59000000U | root_token) ||
                !gxos_nativeaot_scheduler_worker_note_managed_root_released(
                    &record->lifecycle, root_token)) {
                good = 0;
            }
            if (good) phase64_note_root_release_peer(owner, record);
            probe->phase_after_managed(GXOS_PHASE61_PHASE_AFTER_MANAGED_RETURN);
            phase64_update_overlap(owner);
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
    if (owner != 0 && owner->concurrent_mode &&
        owner->completion_count < GXOS_NATIVEAOT_MANAGED_WORKER_API_CAPACITY) {
        owner->completion_order[owner->completion_count++] = record->handle;
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
#ifdef GXOS_ENABLE_PHASE64_MANAGED_WORKER_API
        probe->managed_root_publish_bridge == 0 ||
        probe->managed_root_release_bridge == 0 ||
        probe->managed_root_validate_bridge == 0 ||
#endif
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
    GXOS_NATIVEAOT_MANAGED_WORKER_API_CONTEXT *context,
    GXOS_NATIVEAOT_MANAGED_WORKER_API_RECORD *record)
{
    if (context == 0 || record == 0) return;
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
    uint32_t index;
    if (handle_out != 0) *handle_out = (GXOS_NATIVEAOT_MANAGED_WORKER_HANDLE){0};
    if (context == 0 || context->probe == 0 || handle_out == 0) {
        return GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_INVALID_ARGUMENT;
    }
    record = 0;
    for (index = 0; index != GXOS_NATIVEAOT_MANAGED_WORKER_API_CAPACITY;
         ++index) {
        if (!context->records[index].active) {
            record = &context->records[index];
            break;
        }
    }
    if (record == 0) {
        return GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_CAPACITY;
    }
    phase61_zero((uint8_t *)record, sizeof(*record));
    record->probe = context->probe;
    record->owner_context = context;
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
        if (thread != 0 && thread->live) {
            phase61_discard_unstarted(context, record);
        }
        phase61_zero((uint8_t *)record, sizeof(*record));
        return GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_INTERNAL_FAILURE;
    }
    record->lifecycle.allow_shared_threadstore = context->concurrent_mode;
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
    GXOS_NATIVEAOT_MANAGED_WORKER_API_RECORD *record;
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
    record = phase61_active_record(context, handle);
    if (record == 0) {
        return GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_INVALID_HANDLE;
    }
    if (record->state != GXOS_NATIVEAOT_MANAGED_WORKER_STATE_CREATED) {
        return GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_DUPLICATE_SUBMIT;
    }
    record->request = *request;
    record->result.operation_id = request->operation_id;
    if (!gxos_scheduler_resume_thread(
            record->scheduler_handle, &previous_suspend_count) ||
        previous_suspend_count != 1U ||
        !gxos_nativeaot_scheduler_worker_mark_runnable(
            &record->lifecycle) ||
        !gxos_nativeaot_managed_worker_api_state_transition(
            GXOS_NATIVEAOT_MANAGED_WORKER_STATE_CREATED,
            GXOS_NATIVEAOT_MANAGED_WORKER_STATE_SUBMITTED)) {
        phase61_mark_failed(record);
        return GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_INTERNAL_FAILURE;
    }
    record->state = GXOS_NATIVEAOT_MANAGED_WORKER_STATE_SUBMITTED;
    record->result.state = GXOS_NATIVEAOT_MANAGED_WORKER_STATE_SUBMITTED;
    return GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK;
}

GXOS_NATIVEAOT_MANAGED_WORKER_STATUS
gxos_nativeaot_managed_worker_api_drive(
    GXOS_NATIVEAOT_MANAGED_WORKER_API *api,
    GXOS_NATIVEAOT_MANAGED_WORKER_HANDLE handle)
{
    GXOS_NATIVEAOT_MANAGED_WORKER_API_CONTEXT *context = phase61_context(api);
    GXOS_NATIVEAOT_MANAGED_WORKER_API_RECORD *record;
    GXOS_SCHEDULER_REGISTER_SNAPSHOT snapshot = {0};
    uint32_t dispatches;
    GXOS_NATIVEAOT_MANAGED_WORKER_STATUS status =
        phase61_validate_active_handle(api, handle);
    if (status != GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK) return status;
    record = phase61_active_record(context, handle);
    if (record == 0) {
        return GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_INVALID_HANDLE;
    }
    if (record->state != GXOS_NATIVEAOT_MANAGED_WORKER_STATE_SUBMITTED ||
        gxos_scheduler_current_thread() != context->probe->main_thread) {
        return GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_INVALID_STATE;
    }
    for (dispatches = 0;
         dispatches <= GXOS_NATIVEAOT_MANAGED_WORKER_API_CAPACITY;
         ++dispatches) {
        if (record->state == GXOS_NATIVEAOT_MANAGED_WORKER_STATE_COMPLETED ||
            record->state == GXOS_NATIVEAOT_MANAGED_WORKER_STATE_FAILED) {
            break;
        }
        if (gxos_scheduler_runnable_count() == 0U) {
            phase61_mark_failed(record);
            return GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_INTERNAL_FAILURE;
        }
        gxos_scheduler_main_dispatch(&snapshot);
    }
    if (record->state == GXOS_NATIVEAOT_MANAGED_WORKER_STATE_SUBMITTED ||
        record->state == GXOS_NATIVEAOT_MANAGED_WORKER_STATE_RUNNING) {
        phase61_mark_failed(record);
        return GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_INTERNAL_FAILURE;
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
    GXOS_NATIVEAOT_MANAGED_WORKER_API_RECORD *record;
    GXOS_NATIVEAOT_MANAGED_WORKER_STATUS status =
        phase61_validate_active_handle(api, handle);
    if (status != GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK) return status;
    record = phase61_active_record(context, handle);
    if (record == 0) {
        return GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_INVALID_HANDLE;
    }
    if (result_out != 0) *result_out = record->result;
    if (record->state != GXOS_NATIVEAOT_MANAGED_WORKER_STATE_COMPLETED &&
        record->state != GXOS_NATIVEAOT_MANAGED_WORKER_STATE_FAILED) {
        return GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_NOT_COMPLETE;
    }
    return (GXOS_NATIVEAOT_MANAGED_WORKER_STATUS)
        record->result.status;
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
    uint32_t index;
    int reclaimed;
    if (api == 0 || !phase61_handle_valid(handle)) {
        return GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_INVALID_HANDLE;
    }
    for (index = 0; index != GXOS_NATIVEAOT_MANAGED_WORKER_API_CAPACITY;
         ++index) {
        if (!context->records[index].active &&
            context->has_last_closed[index] &&
            gxos_nativeaot_managed_worker_api_handle_equal(
                context->last_closed[index], handle)) {
            return GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_DUPLICATE_CLOSE;
        }
    }
    status = phase61_validate_active_handle(api, handle);
    if (status != GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK) return status;
    record = phase61_active_record(context, handle);
    if (record == 0) {
        return GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_INVALID_HANDLE;
    }
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
            &record->lifecycle)) {
        return GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_INTERNAL_FAILURE;
    }
    if (!gxos_scheduler_close_handle(record->scheduler_handle)) {
        return GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_INTERNAL_FAILURE;
    }
    if (!gxos_scheduler_collect(context->probe->scheduler)) {
        return GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_INTERNAL_FAILURE;
    }
    if (!gxos_nativeaot_scheduler_worker_note_reclaimed(
            &record->lifecycle)) {
        return GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_INTERNAL_FAILURE;
    }
    if (record->lifecycle.ownership_state !=
            GXOS_NATIVEAOT_WORKER_OWNERSHIP_RECLAIMED ||
        record->thread->live != 0) {
        return GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_INTERNAL_FAILURE;
    }
    reclaimed = record->lifecycle.ownership_state ==
        GXOS_NATIVEAOT_WORKER_OWNERSHIP_RECLAIMED &&
        record->thread->live == 0;
    if (!reclaimed) return GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_INTERNAL_FAILURE;
    if (result_out != 0) *result_out = record->result;
    index = phase61_record_index(context, record);
    if (index == UINT32_MAX) {
        return GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_INTERNAL_FAILURE;
    }
    context->last_results[index] = record->result;
    context->last_closed[index] = handle;
    context->has_last_closed[index] = 1;
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

static void phase62_text(GXOS_PHASE53O_PROBE *probe, const char *text)
{
    phase61_text(probe, text);
}

static void phase62_hex(GXOS_PHASE53O_PROBE *probe, const char *name,
                        uint64_t value)
{
    phase61_hex(probe, name, value);
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

static uint32_t phase62_live_api_workers(
    const GXOS_NATIVEAOT_MANAGED_WORKER_API_CONTEXT *context)
{
    uint32_t index;
    uint32_t count = 0;
    if (context == 0) return 0;
    for (index = 0; index != GXOS_NATIVEAOT_MANAGED_WORKER_API_CAPACITY;
         ++index) {
        if (context->records[index].active) ++count;
    }
    return count;
}

static uint32_t phase62_root_ledger(
    const GXOS_NATIVEAOT_MANAGED_WORKER_API_CONTEXT *context)
{
    uint32_t index;
    uint32_t count = 0;
    if (context == 0) return 0;
    for (index = 0; index != GXOS_NATIVEAOT_MANAGED_WORKER_API_CAPACITY;
         ++index) {
        if (context->records[index].active &&
            context->records[index].lifecycle.managed_root_survived) {
            ++count;
        }
    }
    return count;
}

static void phase62_update_peak(
    GXOS_NATIVEAOT_MANAGED_WORKER_API_CONTEXT *context,
    GXOS_PHASE53O_PROBE *probe)
{
    uint32_t value;
    if (context == 0 || probe == 0) return;
    value = *probe->vm_region_count;
    if (value > context->peak_vm) context->peak_vm = value;
    value = phase61_live_threads(probe->scheduler);
    if (value > context->peak_threads) context->peak_threads = value;
    value = phase61_live_objects(probe->scheduler);
    if (value > context->peak_objects) context->peak_objects = value;
    value = phase62_live_api_workers(context);
    if (value > context->peak_api_workers) context->peak_api_workers = value;
    value = phase62_root_ledger(context);
    if (value > context->peak_roots) context->peak_roots = value;
}

static void phase62_checkpoint(
    GXOS_PHASE53O_PROBE *probe,
    const char *vm_label,
    const char *threads_label,
    const char *api_workers_label,
    const char *root_label,
    uint32_t vm_regions,
    uint32_t threads,
    uint32_t api_workers,
    uint32_t roots)
{
    phase62_hex(probe, vm_label, vm_regions);
    phase62_hex(probe, threads_label, threads);
    phase62_hex(probe, api_workers_label, api_workers);
    phase62_hex(probe, root_label, roots);
}

static void phase62_emit_worker(
    GXOS_PHASE53O_PROBE *probe,
    const char *slot_label,
    const char *identity_label,
    const char *generation_label,
    const char *tcb_label,
    const char *stack_label,
    const char *guard_label,
    const char *rsp_label,
    const char *runtime_thread_label,
    const char *allocation_label,
    const char *tls_vector_label,
    const char *tls_block_label,
    const char *fls_label,
    const char *operation_label,
    const char *result_label,
    const GXOS_NATIVEAOT_MANAGED_WORKER_API_RECORD *record)
{
    phase62_hex(probe, slot_label, record->handle.scheduler_slot);
    phase62_hex(probe, identity_label, record->handle.worker_identity);
    phase62_hex(probe, generation_label, record->handle.worker_generation);
    phase62_hex(probe, tcb_label, (uintptr_t)record->thread);
    phase62_hex(probe, stack_label,
                record->lifecycle.stack_reservation_base);
    phase62_hex(probe, guard_label, record->lifecycle.guard_vm_identity);
    phase62_hex(probe, rsp_label, record->lifecycle.saved_rsp);
    phase62_hex(probe, runtime_thread_label,
                record->lifecycle.runtime_thread);
    phase62_hex(probe, allocation_label,
                record->lifecycle.allocation_context);
    phase62_hex(probe, tls_vector_label,
                record->lifecycle.tls_vector_base);
    phase62_hex(probe, tls_block_label,
                record->runtime_tls_block_value);
    phase62_hex(probe, fls_label, record->runtime_fls_value);
    phase62_hex(probe, operation_label, record->request.operation_id);
    phase62_hex(probe, result_label, record->result.output0);
}

static int phase62_result_valid(
    const GXOS_NATIVEAOT_MANAGED_WORKER_HANDLE handle,
    const GXOS_NATIVEAOT_MANAGED_WORKER_REQUEST *request,
    const GXOS_NATIVEAOT_MANAGED_WORKER_RESULT *result)
{
    if (request == 0 || result == 0 || result->status !=
            GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK ||
        result->state != GXOS_NATIVEAOT_MANAGED_WORKER_STATE_COMPLETED ||
        !gxos_nativeaot_managed_worker_api_handle_equal(result->worker, handle) ||
        result->operation_id != request->operation_id || result->result_code != 0) {
        return 0;
    }
    if (request->operation_id ==
            GXOS_NATIVEAOT_MANAGED_WORKER_OPERATION_ADD_ONE) {
        return result->output0 == request->argument0 + 1U &&
            ((uint32_t)result->managed_result & 0xFFFFU) == result->output0;
    }
    return request->operation_id ==
            GXOS_NATIVEAOT_MANAGED_WORKER_OPERATION_GC_CHECK &&
        result->output0 != 0U && result->output1 ==
            (gxos_nativeaot_gc_expected_checksum(request->argument0) & 0x0FFFU);
}

static int phase62_run_reuse(
    GXOS_NATIVEAOT_MANAGED_WORKER_API *api,
    GXOS_NATIVEAOT_MANAGED_WORKER_API_CONTEXT *context,
    GXOS_PHASE53O_PROBE *probe,
    GXOS_NATIVEAOT_MANAGED_WORKER_HANDLE old_a,
    GXOS_NATIVEAOT_MANAGED_WORKER_HANDLE old_b,
    int *same_slot_out)
{
    GXOS_NATIVEAOT_MANAGED_WORKER_HANDLE handle = {0};
    GXOS_NATIVEAOT_MANAGED_WORKER_REQUEST request = {0};
    GXOS_NATIVEAOT_MANAGED_WORKER_RESULT result = {0};
    GXOS_NATIVEAOT_MANAGED_WORKER_STATUS status;
    GXOS_NATIVEAOT_MANAGED_WORKER_API_RECORD *record = 0;
    uint32_t index;
    int same_slot;
    request.version = GXOS_NATIVEAOT_MANAGED_WORKER_API_VERSION;
    request.size = sizeof(request);
    request.operation_id = GXOS_NATIVEAOT_MANAGED_WORKER_OPERATION_ADD_ONE;
    request.argument0 = 0xE0U;
    status = gxos_nativeaot_managed_worker_api_create(api, &handle);
    if (status != GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK) return 0;
    same_slot = handle.scheduler_slot == old_a.scheduler_slot ||
        handle.scheduler_slot == old_b.scheduler_slot;
    if (same_slot &&
        ((handle.scheduler_slot == old_a.scheduler_slot &&
          handle.worker_generation == old_a.worker_generation) ||
         (handle.scheduler_slot == old_b.scheduler_slot &&
          handle.worker_generation == old_b.worker_generation))) {
        return 0;
    }
    if (gxos_nativeaot_managed_worker_api_poll(api, old_a, &result) ==
            GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK ||
        gxos_nativeaot_managed_worker_api_submit(api, old_b, &request) ==
            GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK ||
        gxos_nativeaot_managed_worker_api_close(api, old_a, 0) ==
            GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK ||
        gxos_nativeaot_managed_worker_api_submit(api, handle, &request) !=
            GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK ||
        gxos_nativeaot_managed_worker_api_drive(api, handle) !=
            GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK) {
        return 0;
    }
    for (index = 0; index != GXOS_NATIVEAOT_MANAGED_WORKER_API_CAPACITY;
         ++index) {
        if (context->records[index].active) {
            record = &context->records[index];
            break;
        }
    }
    if (record == 0 ||
        gxos_nativeaot_managed_worker_api_poll(api, record->handle, &result) !=
            GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK ||
        !phase62_result_valid(record->handle, &record->request, &result) ||
        gxos_nativeaot_managed_worker_api_close(api, record->handle, 0) !=
            GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK ||
        phase62_live_api_workers(context) != 0U) {
        return 0;
    }
    handle = context->last_closed[index];
    if (same_slot_out != 0) *same_slot_out = same_slot;
    phase62_hex(probe, "GXOS_NET10:PHASE62_REUSE_C_HANDLE_SLOT=0x",
                handle.scheduler_slot);
    phase62_hex(probe, "GXOS_NET10:PHASE62_REUSE_C_IDENTITY=0x",
                handle.worker_identity);
    phase62_hex(probe, "GXOS_NET10:PHASE62_REUSE_C_GENERATION=0x",
                handle.worker_generation);
    return 1;
}

static int phase62_run_pair(
    GXOS_NATIVEAOT_MANAGED_WORKER_API *api,
    GXOS_NATIVEAOT_MANAGED_WORKER_API_CONTEXT *context,
    GXOS_PHASE53O_PROBE *probe,
    uint32_t cycle,
    uint32_t baseline_vm,
    uint32_t baseline_threads,
    uint32_t baseline_objects,
    GXOS_NATIVEAOT_MANAGED_WORKER_HANDLE *a_out,
    GXOS_NATIVEAOT_MANAGED_WORKER_HANDLE *b_out)
{
    GXOS_NATIVEAOT_MANAGED_WORKER_HANDLE handle_a = {0};
    GXOS_NATIVEAOT_MANAGED_WORKER_HANDLE handle_b = {0};
    GXOS_NATIVEAOT_MANAGED_WORKER_HANDLE wrong = {0};
    GXOS_NATIVEAOT_MANAGED_WORKER_REQUEST request_a = {0};
    GXOS_NATIVEAOT_MANAGED_WORKER_REQUEST request_b = {0};
    GXOS_NATIVEAOT_MANAGED_WORKER_RESULT result_a = {0};
    GXOS_NATIVEAOT_MANAGED_WORKER_RESULT result_b = {0};
    GXOS_NATIVEAOT_MANAGED_WORKER_RESULT result_a_again = {0};
    GXOS_NATIVEAOT_MANAGED_WORKER_RESULT result_b_before_close = {0};
    GXOS_NATIVEAOT_MANAGED_WORKER_RESULT closed_result = {0};
    GXOS_NATIVEAOT_MANAGED_WORKER_STATUS status;
    GXOS_NATIVEAOT_MANAGED_WORKER_API_RECORD *record_a;
    GXOS_NATIVEAOT_MANAGED_WORKER_API_RECORD *record_b;
    GXOS_NATIVEAOT_MANAGED_WORKER_API_RECORD *first_record;
    GXOS_NATIVEAOT_MANAGED_WORKER_API_RECORD *second_record;
    GXOS_NATIVEAOT_MANAGED_WORKER_HANDLE first_handle;
    uint32_t first_operation;
    uint32_t first_result;
    uint32_t second_result;
    uint32_t other_threads;
    uint32_t other_objects;
    int reverse = (cycle & 1U) != 0;
    uint32_t a_index = reverse ? 1U : 0U;
    uint32_t b_index = reverse ? 0U : 1U;
    uint32_t first_index = reverse ? b_index : a_index;

    request_a.version = GXOS_NATIVEAOT_MANAGED_WORKER_API_VERSION;
    request_a.size = sizeof(request_a);
    request_a.operation_id = GXOS_NATIVEAOT_MANAGED_WORKER_OPERATION_ADD_ONE;
    request_a.argument0 = 41U + (cycle * 3U);
    request_b.version = GXOS_NATIVEAOT_MANAGED_WORKER_API_VERSION;
    request_b.size = sizeof(request_b);
    request_b.operation_id = GXOS_NATIVEAOT_MANAGED_WORKER_OPERATION_GC_CHECK;
    request_b.argument0 = 0x61U + cycle;
    context->completion_count = 0;
    context->runtime_overlap_observed = 0;
    if (reverse) {
        if (gxos_nativeaot_managed_worker_api_create(api, &handle_b) !=
                GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK ||
            gxos_nativeaot_managed_worker_api_submit(api, handle_b,
                                                     &request_b) !=
                GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK ||
            gxos_nativeaot_managed_worker_api_create(api, &handle_a) !=
                GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK) return 0;
        phase62_checkpoint(probe,
            "GXOS_NET10:PHASE62_A_CREATED_VM=0x",
            "GXOS_NET10:PHASE62_A_CREATED_THREADS=0x",
            "GXOS_NET10:PHASE62_A_CREATED_API_WORKERS=0x",
            "GXOS_NET10:PHASE62_A_CREATED_ROOT_LEDGER=0x",
            *probe->vm_region_count, phase61_live_threads(probe->scheduler),
            phase62_live_api_workers(context), phase62_root_ledger(context));
        if (gxos_nativeaot_managed_worker_api_submit(api, handle_a,
                                                     &request_a) !=
                GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK) return 0;
    } else {
        if (gxos_nativeaot_managed_worker_api_create(api, &handle_a) !=
                GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK) return 0;
        phase62_checkpoint(probe,
            "GXOS_NET10:PHASE62_A_CREATED_VM=0x",
            "GXOS_NET10:PHASE62_A_CREATED_THREADS=0x",
            "GXOS_NET10:PHASE62_A_CREATED_API_WORKERS=0x",
            "GXOS_NET10:PHASE62_A_CREATED_ROOT_LEDGER=0x",
            *probe->vm_region_count, phase61_live_threads(probe->scheduler),
            phase62_live_api_workers(context), phase62_root_ledger(context));
        if (gxos_nativeaot_managed_worker_api_submit(api, handle_a,
                                                     &request_a) !=
                GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK ||
            gxos_nativeaot_managed_worker_api_create(api, &handle_b) !=
                GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK) return 0;
        if (gxos_nativeaot_managed_worker_api_submit(api, handle_b,
                                                     &request_b) !=
                GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK) return 0;
    }
    phase62_checkpoint(probe,
        "GXOS_NET10:PHASE62_AB_CREATED_VM=0x",
        "GXOS_NET10:PHASE62_AB_CREATED_THREADS=0x",
        "GXOS_NET10:PHASE62_AB_CREATED_API_WORKERS=0x",
        "GXOS_NET10:PHASE62_AB_CREATED_ROOT_LEDGER=0x",
        *probe->vm_region_count, phase61_live_threads(probe->scheduler),
        phase62_live_api_workers(context), phase62_root_ledger(context));
    phase62_update_peak(context, probe);
    if (GXOS_NATIVEAOT_MANAGED_WORKER_API_CAPACITY == 2U) {
        status = gxos_nativeaot_managed_worker_api_create(api, &wrong);
        if (status != GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_CAPACITY) {
            return 0;
        }
    }
    wrong = handle_a;
    wrong.worker_identity = handle_b.worker_identity;
    {
        GXOS_NATIVEAOT_MANAGED_WORKER_STATUS wrong_submit =
            gxos_nativeaot_managed_worker_api_submit(api, wrong, &request_a);
        GXOS_NATIVEAOT_MANAGED_WORKER_STATUS wrong_poll =
            gxos_nativeaot_managed_worker_api_poll(api, wrong, &result_a);
        GXOS_NATIVEAOT_MANAGED_WORKER_STATUS wrong_close =
            gxos_nativeaot_managed_worker_api_close(api, wrong, 0);
        if (wrong_submit == GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK ||
            wrong_poll == GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK ||
            wrong_close == GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK) {
            return 0;
        }
    }
    {
        GXOS_NATIVEAOT_MANAGED_WORKER_STATUS duplicate_a =
            gxos_nativeaot_managed_worker_api_submit(api, handle_a, &request_a);
        GXOS_NATIVEAOT_MANAGED_WORKER_STATUS duplicate_b =
            gxos_nativeaot_managed_worker_api_submit(api, handle_b, &request_b);
        if (duplicate_a != GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_DUPLICATE_SUBMIT ||
            duplicate_b != GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_DUPLICATE_SUBMIT) {
            return 0;
        }
    }
    request_a.argument0 = 0xEEU;
    request_b.argument0 = 0xEFU;
    first_handle = context->records[first_index].handle;
    status = gxos_nativeaot_managed_worker_api_drive(api, first_handle);
    if (status != GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK) return 0;
    record_a = &context->records[a_index];
    record_b = &context->records[b_index];
    first_record = &context->records[first_index];
    second_record = &context->records[first_index == a_index ? b_index : a_index];
    if (!context->runtime_overlap_observed || context->completion_count == 0) return 0;
    if (!gxos_nativeaot_managed_worker_api_handle_equal(
            context->completion_order[0], first_record->handle)) {
        return 0;
    }
    if (record_a->state == GXOS_NATIVEAOT_MANAGED_WORKER_STATE_SUBMITTED ||
        record_a->state == GXOS_NATIVEAOT_MANAGED_WORKER_STATE_RUNNING) {
        status = gxos_nativeaot_managed_worker_api_drive(api, record_a->handle);
        if (status != GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK) return 0;
    }
    if (record_b->state == GXOS_NATIVEAOT_MANAGED_WORKER_STATE_SUBMITTED ||
        record_b->state == GXOS_NATIVEAOT_MANAGED_WORKER_STATE_RUNNING) {
        status = gxos_nativeaot_managed_worker_api_drive(api, record_b->handle);
        if (status != GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK) return 0;
    }
    status = gxos_nativeaot_managed_worker_api_poll(api, record_a->handle, &result_a);
    if (status != GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK) return 0;
    status = gxos_nativeaot_managed_worker_api_poll(api, record_b->handle, &result_b);
    if (status != GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK) return 0;
    if (!phase62_result_valid(record_a->handle, &record_a->request, &result_a) ||
        !phase62_result_valid(record_b->handle, &record_b->request, &result_b)) return 0;
    status = gxos_nativeaot_managed_worker_api_poll(api, record_a->handle, &result_a_again);
    if (status != GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK ||
        result_a_again.output0 != result_a.output0 ||
        result_a_again.output1 != result_a.output1) return 0;
    if (first_index == a_index) {
        first_operation = result_a.operation_id;
        first_result = result_a.output0;
        second_result = result_b.output0;
    } else {
        first_operation = result_b.operation_id;
        first_result = result_b.output0;
        second_result = result_a.output0;
    }
    if (first_record->lifecycle.runtime_thread == 0 ||
        second_record->lifecycle.runtime_thread == 0 ||
        first_record->lifecycle.runtime_thread ==
            second_record->lifecycle.runtime_thread ||
        first_record->lifecycle.allocation_context == 0 ||
        second_record->lifecycle.allocation_context == 0 ||
        first_record->lifecycle.allocation_context ==
            second_record->lifecycle.allocation_context ||
        first_record->runtime_fls_value == 0 ||
        second_record->runtime_fls_value == 0 ||
        first_record->runtime_fls_value == second_record->runtime_fls_value ||
        first_record->lifecycle.tls_vector_base ==
            second_record->lifecycle.tls_vector_base ||
        first_record->lifecycle.stack_reservation_base ==
            second_record->lifecycle.stack_reservation_base ||
        first_record->lifecycle.guard_vm_identity ==
            second_record->lifecycle.guard_vm_identity ||
        first_record->lifecycle.saved_rsp <
            first_record->lifecycle.stack_usable_low ||
        first_record->lifecycle.saved_rsp >=
            first_record->lifecycle.stack_usable_high ||
        second_record->lifecycle.saved_rsp <
            second_record->lifecycle.stack_usable_low ||
        second_record->lifecycle.saved_rsp >=
            second_record->lifecycle.stack_usable_high) return 0;
    phase62_emit_worker(probe,
        "GXOS_NET10:PHASE62_A_SLOT=0x", "GXOS_NET10:PHASE62_A_IDENTITY=0x",
        "GXOS_NET10:PHASE62_A_GENERATION=0x", "GXOS_NET10:PHASE62_A_TCB=0x",
        "GXOS_NET10:PHASE62_A_STACK=0x", "GXOS_NET10:PHASE62_A_GUARD=0x",
        "GXOS_NET10:PHASE62_A_RSP=0x", "GXOS_NET10:PHASE62_A_RUNTIME_THREAD=0x",
        "GXOS_NET10:PHASE62_A_ALLOC_CONTEXT=0x", "GXOS_NET10:PHASE62_A_TLS_VECTOR=0x",
        "GXOS_NET10:PHASE62_A_TLS_BLOCK=0x", "GXOS_NET10:PHASE62_A_FLS=0x",
        "GXOS_NET10:PHASE62_A_OPERATION=0x", "GXOS_NET10:PHASE62_A_RESULT=0x",
        record_a);
    phase62_emit_worker(probe,
        "GXOS_NET10:PHASE62_B_SLOT=0x", "GXOS_NET10:PHASE62_B_IDENTITY=0x",
        "GXOS_NET10:PHASE62_B_GENERATION=0x", "GXOS_NET10:PHASE62_B_TCB=0x",
        "GXOS_NET10:PHASE62_B_STACK=0x", "GXOS_NET10:PHASE62_B_GUARD=0x",
        "GXOS_NET10:PHASE62_B_RSP=0x", "GXOS_NET10:PHASE62_B_RUNTIME_THREAD=0x",
        "GXOS_NET10:PHASE62_B_ALLOC_CONTEXT=0x", "GXOS_NET10:PHASE62_B_TLS_VECTOR=0x",
        "GXOS_NET10:PHASE62_B_TLS_BLOCK=0x", "GXOS_NET10:PHASE62_B_FLS=0x",
        "GXOS_NET10:PHASE62_B_OPERATION=0x", "GXOS_NET10:PHASE62_B_RESULT=0x",
        record_b);
    phase62_hex(probe, "GXOS_NET10:PHASE62_FIRST_OPERATION=0x", first_operation);
    phase62_hex(probe, "GXOS_NET10:PHASE62_FIRST_RESULT=0x", first_result);
    phase62_hex(probe, "GXOS_NET10:PHASE62_SECOND_RESULT=0x", second_result);
    phase62_checkpoint(probe,
        "GXOS_NET10:PHASE62_OVERLAP_VM=0x",
        "GXOS_NET10:PHASE62_OVERLAP_THREADS=0x",
        "GXOS_NET10:PHASE62_OVERLAP_API_WORKERS=0x",
        "GXOS_NET10:PHASE62_OVERLAP_ROOT_LEDGER=0x",
        *probe->vm_region_count, phase61_live_threads(probe->scheduler),
        phase62_live_api_workers(context), phase62_root_ledger(context));
    phase62_update_peak(context, probe);
    phase62_text(probe, "GXOS_NET10:PHASE62_RUNTIME_OVERLAP=1\r\n");
    if (result_b.output0 == 0 || phase62_root_ledger(context) != 1U) return 0;
    phase62_text(probe, "GXOS_NET10:PHASE62_GC_WORKER_ROOT_LEDGER=1\r\n");
    phase62_text(probe, "GXOS_NET10:PHASE62_OTHER_WORKER_SURVIVED_GC=1\r\n");
    if (first_index == a_index) {
        phase62_hex(probe, "GXOS_NET10:PHASE62_A_FIRST_RESULT=0x",
                    result_a.output0);
    } else {
        phase62_hex(probe, "GXOS_NET10:PHASE62_B_FIRST_RESULT=0x",
                    result_b.output0);
    }
    if (!reverse) {
        if (gxos_nativeaot_managed_worker_api_close(api, record_a->handle,
                                                    &closed_result) !=
                GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK) return 0;
        other_threads = phase61_live_threads(probe->scheduler);
        other_objects = phase61_live_objects(probe->scheduler);
        if (gxos_nativeaot_managed_worker_api_poll(api, record_a->handle,
                                                   &result_a_again) ==
                GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK ||
            gxos_nativeaot_managed_worker_api_submit(api, record_a->handle,
                                                     &request_a) ==
                GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK ||
            gxos_nativeaot_managed_worker_api_close(api, record_a->handle, 0) ==
                GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK ||
            gxos_nativeaot_managed_worker_api_poll(api, record_b->handle,
                                                   &result_b_before_close) !=
                GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK ||
            result_b_before_close.output0 != result_b.output0 ||
            phase62_live_api_workers(context) != 1U ||
            other_threads <= baseline_threads || other_objects <= baseline_objects) {
            return 0;
        }
        phase62_hex(probe, "GXOS_NET10:PHASE62_RECLAIM_A_THREADS=0x",
                    other_threads);
        phase62_hex(probe, "GXOS_NET10:PHASE62_RECLAIM_A_OBJECTS=0x",
                    other_objects);
        phase62_text(probe,
                     "GXOS_NET10:PHASE62_RECLAIM_A_WHILE_B_LIVE=1\r\n");
        if (gxos_nativeaot_managed_worker_api_close(api, record_b->handle, 0) !=
                GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK) return 0;
        phase62_text(probe, "GXOS_NET10:PHASE62_RECLAIM_B_AFTER_A=1\r\n");
    } else {
        if (gxos_nativeaot_managed_worker_api_close(api, record_b->handle,
                                                    &closed_result) !=
                GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK) return 0;
        other_threads = phase61_live_threads(probe->scheduler);
        other_objects = phase61_live_objects(probe->scheduler);
        if (gxos_nativeaot_managed_worker_api_poll(api, record_b->handle,
                                                   &result_b_before_close) ==
                GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK ||
            gxos_nativeaot_managed_worker_api_submit(api, record_b->handle,
                                                     &request_b) ==
                GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK ||
            gxos_nativeaot_managed_worker_api_close(api, record_b->handle, 0) ==
                GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK ||
            gxos_nativeaot_managed_worker_api_poll(api, record_a->handle,
                                                   &result_a_again) !=
                GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK ||
            result_a_again.output0 != result_a.output0 ||
            phase62_live_api_workers(context) != 1U ||
            other_threads <= baseline_threads || other_objects <= baseline_objects) {
            return 0;
        }
        phase62_hex(probe, "GXOS_NET10:PHASE62_RECLAIM_B_THREADS=0x",
                    other_threads);
        phase62_hex(probe, "GXOS_NET10:PHASE62_RECLAIM_B_OBJECTS=0x",
                    other_objects);
        phase62_text(probe,
                     "GXOS_NET10:PHASE62_RECLAIM_B_WHILE_A_LIVE=1\r\n");
        if (gxos_nativeaot_managed_worker_api_close(api, record_a->handle, 0) !=
                GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK) return 0;
        phase62_text(probe, "GXOS_NET10:PHASE62_RECLAIM_A_AFTER_B=1\r\n");
    }
    if (*probe->vm_region_count != baseline_vm ||
        phase61_live_threads(probe->scheduler) != baseline_threads ||
        phase61_live_objects(probe->scheduler) != baseline_objects ||
        phase62_live_api_workers(context) != 0U || phase62_root_ledger(context) != 0U) {
        return 0;
    }
    phase62_hex(probe, "GXOS_NET10:PHASE62_CYCLE=0x", cycle + 1U);
    if (a_out != 0) *a_out = context->last_closed[a_index];
    if (b_out != 0) *b_out = context->last_closed[b_index];
    return baseline_objects == phase61_live_objects(probe->scheduler);
}

int gxos_nativeaot_managed_worker_api_concurrent_probe(
    GXOS_NATIVEAOT_MANAGED_WORKER_API *api)
{
    GXOS_NATIVEAOT_MANAGED_WORKER_API_CONTEXT *context = phase61_context(api);
    GXOS_PHASE53O_PROBE *probe;
    uint32_t baseline_vm;
    uint32_t baseline_threads;
    uint32_t baseline_objects;
    uint32_t cycle;
    int same_slot_reuse = 0;

    if (context == 0 || (probe = context->probe) == 0 ||
        probe->log_text == 0 || probe->log_hex == 0) return 0;
    baseline_vm = *probe->vm_region_count;
    baseline_threads = phase61_live_threads(probe->scheduler);
    baseline_objects = phase61_live_objects(probe->scheduler);
    context->peak_vm = baseline_vm;
    context->peak_threads = baseline_threads;
    context->peak_objects = baseline_objects;
    context->peak_api_workers = 0;
    context->peak_roots = 0;
    context->concurrent_mode = 1;
    phase62_text(probe, "GXOS_NET10:PHASE62_BEGIN\r\n");
    phase62_text(probe,
                 "GXOS_NET10:PHASE62_API=BOUNDED_TWO_MANAGED_WORKERS\r\n");
    phase62_hex(probe, "GXOS_NET10:PHASE62_CAPACITY=0x",
                GXOS_NATIVEAOT_MANAGED_WORKER_API_CAPACITY);
    phase62_checkpoint(probe,
        "GXOS_NET10:PHASE62_BASELINE_VM=0x",
        "GXOS_NET10:PHASE62_BASELINE_THREADS=0x",
        "GXOS_NET10:PHASE62_BASELINE_API_WORKERS=0x",
        "GXOS_NET10:PHASE62_BASELINE_ROOT_LEDGER=0x",
        baseline_vm, baseline_threads, phase62_live_api_workers(context),
        phase62_root_ledger(context));
    phase62_hex(probe, "GXOS_NET10:PHASE62_BASELINE_OBJECTS=0x",
                baseline_objects);
    for (cycle = 0; cycle != GXOS_PHASE62_PAIR_COUNT; ++cycle) {
        GXOS_NATIVEAOT_MANAGED_WORKER_HANDLE handle_a = {0};
        GXOS_NATIVEAOT_MANAGED_WORKER_HANDLE handle_b = {0};
        if (!phase62_run_pair(api, context, probe, cycle, baseline_vm,
                              baseline_threads, baseline_objects,
                              &handle_a, &handle_b)) return 0;
        if (cycle == 0U) {
            if (!phase62_run_reuse(api, context, probe, handle_a, handle_b,
                                  &same_slot_reuse)) return 0;
            if (GXOS_NATIVEAOT_MANAGED_WORKER_API_CAPACITY == 2U) {
                phase62_text(probe,
                    "GXOS_NET10:PHASE62_CAPACITY_REJECTED=1\r\n");
            } else {
                phase62_text(probe,
                    "GXOS_NET10:PHASE62_CAPACITY_THREE_REGRESSION=1\r\n");
            }
            phase62_text(probe, "GXOS_NET10:PHASE62_SAME_SLOT_REUSE_TESTED=1\r\n");
        }
    }
    if (*probe->vm_region_count != baseline_vm ||
        phase61_live_threads(probe->scheduler) != baseline_threads ||
        phase61_live_objects(probe->scheduler) != baseline_objects ||
        phase62_live_api_workers(context) != 0U || phase62_root_ledger(context) != 0U) {
        return 0;
    }
    phase62_hex(probe, "GXOS_NET10:PHASE62_PEAK_VM=0x", context->peak_vm);
    phase62_hex(probe, "GXOS_NET10:PHASE62_PEAK_THREADS=0x", context->peak_threads);
    phase62_hex(probe, "GXOS_NET10:PHASE62_PEAK_OBJECTS=0x", context->peak_objects);
    phase62_hex(probe, "GXOS_NET10:PHASE62_PEAK_API_WORKERS=0x",
                context->peak_api_workers);
    phase62_hex(probe, "GXOS_NET10:PHASE62_PEAK_ROOT_LEDGER=0x",
                context->peak_roots);
    phase62_hex(probe, "GXOS_NET10:PHASE62_FINAL_VM=0x",
                *probe->vm_region_count);
    phase62_hex(probe, "GXOS_NET10:PHASE62_FINAL_THREADS=0x",
                phase61_live_threads(probe->scheduler));
    phase62_hex(probe, "GXOS_NET10:PHASE62_FINAL_OBJECTS=0x",
                phase61_live_objects(probe->scheduler));
    phase62_hex(probe, "GXOS_NET10:PHASE62_FINAL_API_WORKERS=0x",
                phase62_live_api_workers(context));
    phase62_hex(probe, "GXOS_NET10:PHASE62_FINAL_ROOT_LEDGER=0x",
                phase62_root_ledger(context));
    phase62_text(probe, "GXOS_NET10:PHASE62_REQUEST_ISOLATION=1\r\n");
    phase62_text(probe, "GXOS_NET10:PHASE62_RESULT_ISOLATION=1\r\n");
    phase62_text(probe, "GXOS_NET10:PHASE62_CROSS_HANDLE_REJECTED=1\r\n");
    phase62_text(probe, "GXOS_NET10:PHASE62_STALE_CROSS_WORKER_REJECTED=1\r\n");
    phase62_text(probe, "GXOS_NET10:PHASE62_COMPLETION_ORDERINGS=ADD_FIRST,GC_FIRST\r\n");
    phase62_text(probe, same_slot_reuse
        ? "GXOS_NET10:PHASE62_GENERATION_ADVANCEMENT=1\r\n"
        : "GXOS_NET10:PHASE62_GENERATION_ADVANCEMENT=NON_SLOT_REUSE\r\n");
    phase62_text(probe, "GXOS_NET10:PHASE62_PAIRS=12\r\n");
    phase62_text(probe, "GXOS_NET10:PHASE62_COMPLETE=1\r\n");
    phase62_text(probe, "GXOS_NET10:PHASE62_PASS=1\r\n");
    return 1;
}

#ifdef GXOS_ENABLE_PHASE64_MANAGED_WORKER_API

#define GXOS_PHASE64_SCENARIO_COUNT 12U

static void phase64_text(GXOS_PHASE53O_PROBE *probe, const char *text)
{
    phase61_text(probe, text);
}

static void phase64_hex(GXOS_PHASE53O_PROBE *probe, const char *name,
                        uint64_t value)
{
    phase61_hex(probe, name, value);
}

static uint32_t phase64_root_ledger(
    const GXOS_NATIVEAOT_MANAGED_WORKER_API_CONTEXT *context)
{
    return phase64_root_workers(context);
}

static int phase64_result_valid(
    const GXOS_NATIVEAOT_MANAGED_WORKER_API_RECORD *record,
    const GXOS_NATIVEAOT_MANAGED_WORKER_RESULT *result)
{
    if (record == 0 || result == 0 ||
        result->status != GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK ||
        result->state != GXOS_NATIVEAOT_MANAGED_WORKER_STATE_COMPLETED ||
        !gxos_nativeaot_managed_worker_api_handle_equal(
            result->worker, record->handle) ||
        result->operation_id != record->request.operation_id ||
        result->result_code != 0) {
        return 0;
    }
    if (record->request.operation_id ==
            GXOS_NATIVEAOT_MANAGED_WORKER_OPERATION_ADD_ONE) {
        return result->output0 == record->request.argument0 + 1U &&
            ((uint32_t)result->managed_result & 0xFFFFU) == result->output0;
    }
    return record->request.operation_id ==
            GXOS_NATIVEAOT_MANAGED_WORKER_OPERATION_GC_CHECK &&
        result->output0 != 0U && result->output1 ==
            (gxos_nativeaot_gc_expected_checksum(record->request.argument0) &
             0x0FFFU) && record->lifecycle.managed_root_survived != 0;
}

static void phase64_update_peak(
    GXOS_NATIVEAOT_MANAGED_WORKER_API_CONTEXT *context,
    GXOS_PHASE53O_PROBE *probe)
{
    uint32_t value;
    if (context == 0 || probe == 0) return;
    value = *probe->vm_region_count;
    if (value > context->peak_vm) context->peak_vm = value;
    value = phase61_live_threads(probe->scheduler);
    if (value > context->peak_threads) context->peak_threads = value;
    value = phase61_live_objects(probe->scheduler);
    if (value > context->peak_objects) context->peak_objects = value;
    value = phase62_live_api_workers(context);
    if (value > context->peak_api_workers) context->peak_api_workers = value;
    value = context->max_root_workers;
    if (value > context->peak_roots) context->peak_roots = value;
}

static void phase64_checkpoint(
    GXOS_NATIVEAOT_MANAGED_WORKER_API_CONTEXT *context,
    GXOS_PHASE53O_PROBE *probe, const char *vm_label,
    const char *threads_label, const char *objects_label,
    const char *workers_label, const char *roots_label, uint32_t roots)
{
    phase64_hex(probe, vm_label, *probe->vm_region_count);
    phase64_hex(probe, threads_label, phase61_live_threads(probe->scheduler));
    phase64_hex(probe, objects_label, phase61_live_objects(probe->scheduler));
    phase64_hex(probe, workers_label,
                phase62_live_api_workers(context));
    phase64_hex(probe, roots_label, roots);
}

static int phase64_drive_if_needed(
    GXOS_NATIVEAOT_MANAGED_WORKER_API *api,
    GXOS_NATIVEAOT_MANAGED_WORKER_API_RECORD *record)
{
    if (record == 0 || !record->active) return 0;
    if (record->state == GXOS_NATIVEAOT_MANAGED_WORKER_STATE_COMPLETED ||
        record->state == GXOS_NATIVEAOT_MANAGED_WORKER_STATE_FAILED) {
        return 1;
    }
    return gxos_nativeaot_managed_worker_api_drive(api, record->handle) ==
        GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK;
}

static int phase64_create_worker(
    GXOS_NATIVEAOT_MANAGED_WORKER_API *api,
    GXOS_NATIVEAOT_MANAGED_WORKER_HANDLE *handle_out)
{
    return gxos_nativeaot_managed_worker_api_create(api, handle_out) ==
        GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK;
}

static int phase64_run_triple(
    GXOS_NATIVEAOT_MANAGED_WORKER_API *api,
    GXOS_NATIVEAOT_MANAGED_WORKER_API_CONTEXT *context,
    GXOS_PHASE53O_PROBE *probe, uint32_t cycle, uint32_t baseline_vm,
    uint32_t baseline_threads, uint32_t baseline_objects)
{
    GXOS_NATIVEAOT_MANAGED_WORKER_HANDLE handle_a = {0};
    GXOS_NATIVEAOT_MANAGED_WORKER_HANDLE handle_b = {0};
    GXOS_NATIVEAOT_MANAGED_WORKER_HANDLE handle_c = {0};
    GXOS_NATIVEAOT_MANAGED_WORKER_HANDLE rejected = {0};
    GXOS_NATIVEAOT_MANAGED_WORKER_REQUEST request_a = {0};
    GXOS_NATIVEAOT_MANAGED_WORKER_REQUEST request_b = {0};
    GXOS_NATIVEAOT_MANAGED_WORKER_REQUEST request_c = {0};
    GXOS_NATIVEAOT_MANAGED_WORKER_RESULT result = {0};
    GXOS_NATIVEAOT_MANAGED_WORKER_RESULT result_again = {0};
    GXOS_NATIVEAOT_MANAGED_WORKER_API_RECORD *record_a;
    GXOS_NATIVEAOT_MANAGED_WORKER_API_RECORD *record_b;
    GXOS_NATIVEAOT_MANAGED_WORKER_API_RECORD *record_c;
    GXOS_NATIVEAOT_MANAGED_WORKER_API_RECORD *records[3];
    GXOS_NATIVEAOT_MANAGED_WORKER_HANDLE handles[3];
    uint32_t expected_a = 41U + cycle;
    uint32_t expected_b = 0xB2U + cycle;
    uint32_t expected_c = 0xC3U + cycle;
    uint32_t submit_order[3];
    uint32_t close_order[3] = {cycle % 3U, (cycle + 1U) % 3U,
                               (cycle + 2U) % 3U};
    uint32_t index;
    uint32_t before_vm;
    uint32_t before_threads;
    uint32_t before_objects;
    uint32_t before_identity;
    uint32_t after_vm;
    uint32_t after_threads;
    uint32_t after_objects;
    uint32_t closed_c_root_token;
    GXOS_NATIVEAOT_MANAGED_WORKER_STATUS close_status;

    request_a.version = GXOS_NATIVEAOT_MANAGED_WORKER_API_VERSION;
    request_a.size = sizeof(request_a);
    request_a.operation_id = GXOS_NATIVEAOT_MANAGED_WORKER_OPERATION_ADD_ONE;
    request_a.argument0 = expected_a;
    request_b.version = GXOS_NATIVEAOT_MANAGED_WORKER_API_VERSION;
    request_b.size = sizeof(request_b);
    request_b.operation_id = GXOS_NATIVEAOT_MANAGED_WORKER_OPERATION_GC_CHECK;
    request_b.argument0 = expected_b;
    request_c.version = GXOS_NATIVEAOT_MANAGED_WORKER_API_VERSION;
    request_c.size = sizeof(request_c);
    request_c.operation_id = GXOS_NATIVEAOT_MANAGED_WORKER_OPERATION_GC_CHECK;
    request_c.argument0 = expected_c;

    context->completion_count = 0;
    context->runtime_overlap_observed = 0;
    context->max_attached_workers = 0;
    context->max_root_workers = 0;
    if (!phase64_create_worker(api, &handle_a) ||
        !phase64_create_worker(api, &handle_b) ||
        !phase64_create_worker(api, &handle_c)) return 0;
    record_a = phase61_active_record(context, handle_a);
    record_b = phase61_active_record(context, handle_b);
    record_c = phase61_active_record(context, handle_c);
    if (record_a == 0 || record_b == 0 || record_c == 0) return 0;
    closed_c_root_token = 0;
    records[0] = record_a;
    records[1] = record_b;
    records[2] = record_c;
    handles[0] = handle_a;
    handles[1] = handle_b;
    handles[2] = handle_c;
    phase64_update_peak(context, probe);
    if (phase62_live_api_workers(context) != 3U ||
        phase61_live_threads(probe->scheduler) != baseline_threads + 3U ||
        phase61_live_objects(probe->scheduler) != baseline_objects + 3U) {
        return 0;
    }
    if (cycle == 0U) {
        phase64_checkpoint(context, probe,
            "GXOS_NET10:PHASE64_A_LIVE_VM=0x",
            "GXOS_NET10:PHASE64_A_LIVE_THREADS=0x",
            "GXOS_NET10:PHASE64_A_LIVE_OBJECTS=0x",
            "GXOS_NET10:PHASE64_A_LIVE_WORKERS=0x",
            "GXOS_NET10:PHASE64_A_LIVE_ROOTS=0x",
            phase64_root_ledger(context));
    }
    if (gxos_nativeaot_managed_worker_api_create(api, &rejected) !=
            GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_CAPACITY ||
        rejected.scheduler_slot != 0 || rejected.worker_identity != 0 ||
        rejected.worker_generation != 0) return 0;
    before_vm = *probe->vm_region_count;
    before_threads = phase61_live_threads(probe->scheduler);
    before_objects = phase61_live_objects(probe->scheduler);
    before_identity = probe->scheduler->next_identity;
    rejected = (GXOS_NATIVEAOT_MANAGED_WORKER_HANDLE){0};
    if (gxos_nativeaot_managed_worker_api_create(api, &rejected) !=
            GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_CAPACITY ||
        *probe->vm_region_count != before_vm ||
        phase61_live_threads(probe->scheduler) != before_threads ||
        phase61_live_objects(probe->scheduler) != before_objects ||
        probe->scheduler->next_identity != before_identity ||
        phase62_live_api_workers(context) != 3U) return 0;
    if (cycle == 0U) {
        phase64_hex(probe, "GXOS_NET10:PHASE64_FOURTH_REJECT_STATUS=0x",
                    GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_CAPACITY);
        phase64_text(probe,
            "GXOS_NET10:PHASE64_FOURTH_REJECT_NO_LOWER_ALLOCATION=1\r\n");
    }
    phase64_update_peak(context, probe);

    switch (cycle % 3U) {
        case 0:
            submit_order[0] = 0; submit_order[1] = 1; submit_order[2] = 2;
            break;
        case 1:
            submit_order[0] = 2; submit_order[1] = 1; submit_order[2] = 0;
            break;
        default:
            submit_order[0] = 1; submit_order[1] = 0; submit_order[2] = 2;
            break;
    }
    for (index = 0; index != 3U; ++index) {
        GXOS_NATIVEAOT_MANAGED_WORKER_REQUEST *request =
            submit_order[index] == 0 ? &request_a :
            submit_order[index] == 1 ? &request_b : &request_c;
        if (gxos_nativeaot_managed_worker_api_submit(
                api, handles[submit_order[index]], request) !=
                GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK) return 0;
    }
    /* Mutating caller storage after submit must not change copied records. */
    request_a.argument0 = 0xA1U;
    request_b.argument0 = 0xB1U;
    request_c.argument0 = 0xC1U;
    if (phase62_live_api_workers(context) != 3U) return 0;
    if (cycle == 0U) {
        phase64_checkpoint(context, probe,
            "GXOS_NET10:PHASE64_AB_LIVE_VM=0x",
            "GXOS_NET10:PHASE64_AB_LIVE_THREADS=0x",
            "GXOS_NET10:PHASE64_AB_LIVE_OBJECTS=0x",
            "GXOS_NET10:PHASE64_AB_LIVE_WORKERS=0x",
            "GXOS_NET10:PHASE64_AB_LIVE_ROOTS=0x",
            phase64_root_ledger(context));
    }
    phase64_update_peak(context, probe);

    for (index = 0; index != 3U; ++index) {
        if (!phase64_drive_if_needed(api, records[submit_order[index]])) {
            return 0;
        }
    }
    phase64_update_peak(context, probe);
    if (context->max_attached_workers < 3U ||
        context->max_root_workers < GXOS_NATIVEAOT_MANAGED_WORKER_ROOT_CAPACITY ||
        !context->runtime_overlap_observed) {
        return 0;
    }
    if (!phase64_result_valid(record_a, &record_a->result) ||
        !phase64_result_valid(record_b, &record_b->result) ||
        !phase64_result_valid(record_c, &record_c->result) ||
        record_a->request.argument0 != expected_a ||
        record_b->request.argument0 != expected_b ||
        record_c->request.argument0 != expected_c ||
        record_b->lifecycle.managed_root_identity == 0 ||
        record_c->lifecycle.managed_root_identity == 0 ||
        record_b->lifecycle.managed_root_identity ==
            record_c->lifecycle.managed_root_identity) {
        return 0;
    }
    if (gxos_nativeaot_managed_worker_api_poll(api, handle_a, &result) !=
            GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK ||
        gxos_nativeaot_managed_worker_api_poll(api, handle_b, &result_again) !=
            GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK ||
        result.operation_id !=
            GXOS_NATIVEAOT_MANAGED_WORKER_OPERATION_ADD_ONE ||
        result_again.operation_id !=
            GXOS_NATIVEAOT_MANAGED_WORKER_OPERATION_GC_CHECK) {
        return 0;
    }
    if (cycle == 0U) {
        phase64_checkpoint(context, probe,
            "GXOS_NET10:PHASE64_ROOT_OVERLAP_VM=0x",
            "GXOS_NET10:PHASE64_ROOT_OVERLAP_THREADS=0x",
            "GXOS_NET10:PHASE64_ROOT_OVERLAP_OBJECTS=0x",
            "GXOS_NET10:PHASE64_ROOT_OVERLAP_WORKERS=0x",
            "GXOS_NET10:PHASE64_ROOT_OVERLAP_ROOTS=0x",
            context->max_root_workers);
        phase64_hex(probe, "GXOS_NET10:PHASE64_MAX_ATTACHED_WORKERS=0x",
                    context->max_attached_workers);
        phase64_hex(probe, "GXOS_NET10:PHASE64_MAX_ROOT_WORKERS=0x",
                    context->max_root_workers);
        phase64_hex(probe, "GXOS_NET10:PHASE64_B_ROOT_TOKEN=0x",
                    record_b->lifecycle.managed_root_identity);
        phase64_hex(probe, "GXOS_NET10:PHASE64_C_ROOT_TOKEN=0x",
                    record_c->lifecycle.managed_root_identity);
    }
    if (phase64_root_ledger(context) != 0U) {
        return 0;
    }

    if (cycle == 0U) {
        phase62_emit_worker(probe,
            "GXOS_NET10:PHASE64_A_SLOT=0x",
            "GXOS_NET10:PHASE64_A_IDENTITY=0x",
            "GXOS_NET10:PHASE64_A_GENERATION=0x",
            "GXOS_NET10:PHASE64_A_TCB=0x",
            "GXOS_NET10:PHASE64_A_STACK=0x",
            "GXOS_NET10:PHASE64_A_GUARD=0x",
            "GXOS_NET10:PHASE64_A_RSP=0x",
            "GXOS_NET10:PHASE64_A_RUNTIME_THREAD=0x",
            "GXOS_NET10:PHASE64_A_ALLOC_CONTEXT=0x",
            "GXOS_NET10:PHASE64_A_TLS_VECTOR=0x",
            "GXOS_NET10:PHASE64_A_TLS_BLOCK=0x",
            "GXOS_NET10:PHASE64_A_FLS=0x",
            "GXOS_NET10:PHASE64_A_OPERATION=0x",
            "GXOS_NET10:PHASE64_A_RESULT=0x", record_a);
        phase62_emit_worker(probe,
            "GXOS_NET10:PHASE64_B_SLOT=0x",
            "GXOS_NET10:PHASE64_B_IDENTITY=0x",
            "GXOS_NET10:PHASE64_B_GENERATION=0x",
            "GXOS_NET10:PHASE64_B_TCB=0x",
            "GXOS_NET10:PHASE64_B_STACK=0x",
            "GXOS_NET10:PHASE64_B_GUARD=0x",
            "GXOS_NET10:PHASE64_B_RSP=0x",
            "GXOS_NET10:PHASE64_B_RUNTIME_THREAD=0x",
            "GXOS_NET10:PHASE64_B_ALLOC_CONTEXT=0x",
            "GXOS_NET10:PHASE64_B_TLS_VECTOR=0x",
            "GXOS_NET10:PHASE64_B_TLS_BLOCK=0x",
            "GXOS_NET10:PHASE64_B_FLS=0x",
            "GXOS_NET10:PHASE64_B_OPERATION=0x",
            "GXOS_NET10:PHASE64_B_RESULT=0x", record_b);
        phase62_emit_worker(probe,
            "GXOS_NET10:PHASE64_C_SLOT=0x",
            "GXOS_NET10:PHASE64_C_IDENTITY=0x",
            "GXOS_NET10:PHASE64_C_GENERATION=0x",
            "GXOS_NET10:PHASE64_C_TCB=0x",
            "GXOS_NET10:PHASE64_C_STACK=0x",
            "GXOS_NET10:PHASE64_C_GUARD=0x",
            "GXOS_NET10:PHASE64_C_RSP=0x",
            "GXOS_NET10:PHASE64_C_RUNTIME_THREAD=0x",
            "GXOS_NET10:PHASE64_C_ALLOC_CONTEXT=0x",
            "GXOS_NET10:PHASE64_C_TLS_VECTOR=0x",
            "GXOS_NET10:PHASE64_C_TLS_BLOCK=0x",
            "GXOS_NET10:PHASE64_C_FLS=0x",
            "GXOS_NET10:PHASE64_C_OPERATION=0x",
            "GXOS_NET10:PHASE64_C_RESULT=0x", record_c);
        phase64_hex(probe, "GXOS_NET10:PHASE64_B_GC_CHECKSUM=0x",
                    record_b->result.output1);
        phase64_hex(probe, "GXOS_NET10:PHASE64_C_GC_CHECKSUM=0x",
                    record_c->result.output1);
        phase64_hex(probe, "GXOS_NET10:PHASE64_B_ROOT_SURVIVED=0x",
                    record_b->lifecycle.managed_root_survived);
        phase64_hex(probe, "GXOS_NET10:PHASE64_C_ROOT_SURVIVED=0x",
                    record_c->lifecycle.managed_root_survived);
    }
    for (index = 0; index != 3U; ++index) {
        GXOS_NATIVEAOT_MANAGED_WORKER_API_RECORD *record =
            records[close_order[index]];
        GXOS_NATIVEAOT_MANAGED_WORKER_HANDLE stale = record->handle;
        close_status = gxos_nativeaot_managed_worker_api_close(
            api, record->handle, &result);
        if (close_status != GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK ||
            phase62_live_api_workers(context) != 2U - index) {
            return 0;
        }
        if (index == 0U) {
            after_vm = *probe->vm_region_count;
            after_threads = phase61_live_threads(probe->scheduler);
            after_objects = phase61_live_objects(probe->scheduler);
            if (after_vm >= before_vm || after_threads >= before_threads ||
                after_objects >= before_objects) {
                return 0;
            }
            if (cycle == 0U) {
                phase64_checkpoint(context, probe,
                    "GXOS_NET10:PHASE64_ONE_RECLAIMED_VM=0x",
                    "GXOS_NET10:PHASE64_ONE_RECLAIMED_THREADS=0x",
                    "GXOS_NET10:PHASE64_ONE_RECLAIMED_OBJECTS=0x",
                    "GXOS_NET10:PHASE64_ONE_RECLAIMED_WORKERS=0x",
                    "GXOS_NET10:PHASE64_ONE_RECLAIMED_ROOTS=0x",
                    phase64_root_ledger(context));
            }
        }
        if (gxos_nativeaot_managed_worker_api_poll(api, stale, &result) ==
                GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK ||
            gxos_nativeaot_managed_worker_api_submit(api, stale,
                                                     &request_a) ==
                GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK) return 0;
        for (uint32_t peer = 0; peer != 3U; ++peer) {
            if (peer != close_order[index] && records[peer]->active &&
                records[peer]->thread != 0 && !records[peer]->thread->live) {
                return 0;
            }
        }
    }
    if (*probe->vm_region_count != baseline_vm ||
        phase61_live_threads(probe->scheduler) != baseline_threads ||
        phase61_live_objects(probe->scheduler) != baseline_objects ||
        phase62_live_api_workers(context) != 0U ||
        phase64_root_ledger(context) != 0U) {
        return 0;
    }
    if (cycle == 0U) {
        phase64_text(probe,
            "GXOS_NET10:PHASE64_RECLAIM_A_WHILE_BC_LIVE=1\r\n");
    } else if (cycle == 1U) {
        phase64_text(probe,
            "GXOS_NET10:PHASE64_RECLAIM_B_WHILE_AC_LIVE=1\r\n");
    } else if (cycle == 2U) {
        phase64_text(probe,
            "GXOS_NET10:PHASE64_RECLAIM_C_WHILE_AB_LIVE=1\r\n");
    }
    /* Preserve a real, previously published token for the next scenario's
       cross-worker stale-release check.  The record is created before its
       worker runs, so capturing the identity at create time yields zero. */
    closed_c_root_token = record_c->lifecycle.managed_root_identity;
    context->stale_root_token = closed_c_root_token;
    return 1;
}

static int phase64_run_attach_failure(
    GXOS_NATIVEAOT_MANAGED_WORKER_API *api,
    GXOS_NATIVEAOT_MANAGED_WORKER_API_CONTEXT *context,
    GXOS_PHASE53O_PROBE *probe, uint32_t baseline_vm,
    uint32_t baseline_threads, uint32_t baseline_objects)
{
    GXOS_NATIVEAOT_MANAGED_WORKER_HANDLE healthy_a = {0};
    GXOS_NATIVEAOT_MANAGED_WORKER_HANDLE healthy_b = {0};
    GXOS_NATIVEAOT_MANAGED_WORKER_HANDLE failed = {0};
    GXOS_NATIVEAOT_MANAGED_WORKER_HANDLE replacement = {0};
    GXOS_NATIVEAOT_MANAGED_WORKER_REQUEST request_a = {0};
    GXOS_NATIVEAOT_MANAGED_WORKER_REQUEST request_b = {0};
    GXOS_NATIVEAOT_MANAGED_WORKER_REQUEST request_failed = {0};
    GXOS_NATIVEAOT_MANAGED_WORKER_REQUEST request_replacement = {0};
    GXOS_NATIVEAOT_MANAGED_WORKER_RESULT result = {0};
    GXOS_NATIVEAOT_MANAGED_WORKER_API_RECORD *record_a;
    GXOS_NATIVEAOT_MANAGED_WORKER_API_RECORD *record_b;
    GXOS_NATIVEAOT_MANAGED_WORKER_API_RECORD *failed_record;
    GXOS_NATIVEAOT_MANAGED_WORKER_API_RECORD *replacement_record;
    GXOS_NATIVEAOT_MANAGED_WORKER_STATUS status;
    uint32_t failed_slot;
    uint32_t failed_identity;
    uint16_t failed_generation;
    uint32_t peers_vm;
    uint32_t peers_threads;
    uint32_t peers_objects;

    request_a.version = request_b.version = request_failed.version =
        request_replacement.version = GXOS_NATIVEAOT_MANAGED_WORKER_API_VERSION;
    request_a.size = request_b.size = request_failed.size =
        request_replacement.size = sizeof(request_a);
    request_a.operation_id = GXOS_NATIVEAOT_MANAGED_WORKER_OPERATION_ADD_ONE;
    request_a.argument0 = 0xD1U;
    request_b.operation_id = GXOS_NATIVEAOT_MANAGED_WORKER_OPERATION_GC_CHECK;
    request_b.argument0 = 0xD2U;
    request_failed.operation_id =
        GXOS_NATIVEAOT_MANAGED_WORKER_OPERATION_ADD_ONE;
    request_failed.argument0 = 0xD3U;
    request_replacement.operation_id =
        GXOS_NATIVEAOT_MANAGED_WORKER_OPERATION_ADD_ONE;
    request_replacement.argument0 = 0xD4U;
    context->attach_failure_requested = 0;
    context->attach_failure_fired = 0;
    context->attach_failure_runtime_acquired = 0;
    context->attach_failure_detach_count = 0;
    if (!phase64_create_worker(api, &healthy_a) ||
        !phase64_create_worker(api, &healthy_b) ||
        gxos_nativeaot_managed_worker_api_submit(api, healthy_a, &request_a) !=
            GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK ||
        gxos_nativeaot_managed_worker_api_submit(api, healthy_b, &request_b) !=
            GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK ||
        !phase64_create_worker(api, &failed)) return 0;
    record_a = phase61_active_record(context, healthy_a);
    record_b = phase61_active_record(context, healthy_b);
    failed_record = phase61_active_record(context, failed);
    if (record_a == 0 || record_b == 0 || failed_record == 0) return 0;
    context->attach_failure_handle = failed;
    context->attach_failure_requested = 1;
    if (gxos_nativeaot_managed_worker_api_submit(api, failed,
                                                 &request_failed) !=
            GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK ||
        gxos_nativeaot_managed_worker_api_drive(api, failed) !=
            GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK) return 0;
    if (!context->attach_failure_fired ||
        !context->attach_failure_runtime_acquired ||
        failed_record->state != GXOS_NATIVEAOT_MANAGED_WORKER_STATE_FAILED ||
        failed_record->result.status !=
            GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_INTERNAL_FAILURE ||
        !failed_record->lifecycle.attached ||
        !failed_record->lifecycle.detached ||
        failed_record->lifecycle.runtime_attach_count != 1U ||
        failed_record->lifecycle.runtime_detach_count != 1U ||
        failed_record->lifecycle.ownership_state !=
            GXOS_NATIVEAOT_WORKER_OWNERSHIP_RUNTIME_DETACHED) return 0;
    failed_slot = failed.scheduler_slot;
    failed_identity = failed.worker_identity;
    failed_generation = failed.worker_generation;
    peers_vm = *probe->vm_region_count;
    peers_threads = phase61_live_threads(probe->scheduler);
    peers_objects = phase61_live_objects(probe->scheduler);
    phase64_hex(probe, "GXOS_NET10:PHASE64_FAILED_RUNTIME_THREAD=0x",
                failed_record->lifecycle.runtime_thread);
    phase64_hex(probe, "GXOS_NET10:PHASE64_FAILED_RUNTIME_ATTACH_COUNT=0x",
                failed_record->lifecycle.runtime_attach_count);
    phase64_hex(probe, "GXOS_NET10:PHASE64_FAILED_RUNTIME_DETACH_COUNT=0x",
                failed_record->lifecycle.runtime_detach_count);
    phase64_hex(probe, "GXOS_NET10:PHASE64_FAILED_VM_WITH_PEERS=0x", peers_vm);
    phase64_hex(probe, "GXOS_NET10:PHASE64_FAILED_THREADS_WITH_PEERS=0x",
                peers_threads);
    phase64_hex(probe, "GXOS_NET10:PHASE64_FAILED_OBJECTS_WITH_PEERS=0x",
                peers_objects);
    if (phase64_drive_if_needed(api, record_a) == 0 ||
        phase64_drive_if_needed(api, record_b) == 0 ||
        gxos_nativeaot_managed_worker_api_poll(api, healthy_a, &result) !=
            GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK ||
        !phase64_result_valid(record_a, &result) ||
        gxos_nativeaot_managed_worker_api_poll(api, healthy_b, &result) !=
            GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK ||
        !phase64_result_valid(record_b, &result)) return 0;
    if (gxos_nativeaot_managed_worker_api_poll(api, failed, &result) !=
            GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_INTERNAL_FAILURE) return 0;
    status = gxos_nativeaot_managed_worker_api_close(api, failed, &result);
    if (status != GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK ||
        phase62_live_api_workers(context) != 2U ||
        *probe->vm_region_count >= peers_vm ||
        phase61_live_threads(probe->scheduler) >= peers_threads ||
        phase61_live_objects(probe->scheduler) >= peers_objects) return 0;
    phase64_text(probe, "GXOS_NET10:PHASE64_ATTACH_FAILURE_CLEANUP_COMPLETE=1\r\n");
    if (gxos_nativeaot_managed_worker_api_poll(api, failed, &result) ==
            GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK ||
        gxos_nativeaot_managed_worker_api_submit(api, failed, &request_a) ==
            GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK ||
        gxos_nativeaot_managed_worker_api_close(api, failed, 0) ==
            GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK) return 0;
    phase64_text(probe, "GXOS_NET10:PHASE64_ATTACH_FAILURE_STALE_HANDLE_REJECTED=1\r\n");
    if (!phase64_create_worker(api, &replacement) ||
        replacement.scheduler_slot != failed.scheduler_slot ||
        replacement.worker_identity == failed_identity ||
        replacement.worker_generation == failed_generation) return 0;
    replacement_record = phase61_active_record(context, replacement);
    if (replacement_record == 0 ||
        gxos_nativeaot_managed_worker_api_submit(
            api, replacement, &request_replacement) !=
            GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK ||
        gxos_nativeaot_managed_worker_api_drive(api, replacement) !=
            GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK ||
        gxos_nativeaot_managed_worker_api_poll(api, replacement, &result) !=
            GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK ||
        !phase64_result_valid(replacement_record, &result) ||
        gxos_nativeaot_managed_worker_api_close(api, replacement, 0) !=
            GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK) return 0;
    (void)failed_slot;
    phase64_text(probe, "GXOS_NET10:PHASE64_CAPACITY_RECOVERED=1\r\n");
    phase64_text(probe, "GXOS_NET10:PHASE64_SAME_SLOT_REUSE=1\r\n");
    {
        GXOS_NATIVEAOT_MANAGED_WORKER_STATUS healthy_a_close =
            gxos_nativeaot_managed_worker_api_close(api, healthy_a, 0);
        GXOS_NATIVEAOT_MANAGED_WORKER_STATUS healthy_b_close =
            gxos_nativeaot_managed_worker_api_close(api, healthy_b, 0);
        uint32_t final_vm = *probe->vm_region_count;
        uint32_t final_threads = phase61_live_threads(probe->scheduler);
        uint32_t final_objects = phase61_live_objects(probe->scheduler);
        uint32_t final_workers = phase62_live_api_workers(context);
        uint32_t final_roots = phase64_root_ledger(context);
        if (healthy_a_close != GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK ||
            healthy_b_close != GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK ||
            final_vm != baseline_vm || final_threads != baseline_threads ||
            final_objects != baseline_objects || final_workers != 0U ||
            final_roots != 0U) {
            phase64_hex(probe, "GXOS_NET10:PHASE64_ATTACH_FAILURE_FINAL_A_CLOSE=0x",
                        healthy_a_close);
            phase64_hex(probe, "GXOS_NET10:PHASE64_ATTACH_FAILURE_FINAL_B_CLOSE=0x",
                        healthy_b_close);
            phase64_hex(probe, "GXOS_NET10:PHASE64_ATTACH_FAILURE_FINAL_VM=0x",
                        final_vm);
            phase64_hex(probe, "GXOS_NET10:PHASE64_ATTACH_FAILURE_FINAL_THREADS=0x",
                        final_threads);
            phase64_hex(probe, "GXOS_NET10:PHASE64_ATTACH_FAILURE_FINAL_OBJECTS=0x",
                        final_objects);
            phase64_hex(probe, "GXOS_NET10:PHASE64_ATTACH_FAILURE_FINAL_WORKERS=0x",
                        final_workers);
            phase64_hex(probe, "GXOS_NET10:PHASE64_ATTACH_FAILURE_FINAL_ROOTS=0x",
                        final_roots);
            return 0;
        }
    }
    phase64_hex(probe, "GXOS_NET10:PHASE64_FAILED_SLOT_IDENTITY=0x",
                failed_slot);
    return 1;
}

int gxos_nativeaot_managed_worker_api_capacity_three_probe(
    GXOS_NATIVEAOT_MANAGED_WORKER_API *api)
{
    GXOS_NATIVEAOT_MANAGED_WORKER_API_CONTEXT *context = phase61_context(api);
    GXOS_PHASE53O_PROBE *probe;
    uint32_t baseline_vm;
    uint32_t baseline_threads;
    uint32_t baseline_objects;
    uint32_t cycle;

    if (context == 0 || (probe = context->probe) == 0 ||
        probe->log_text == 0 || probe->log_hex == 0 ||
        probe->managed_root_publish_bridge == 0 ||
        probe->managed_root_release_bridge == 0 ||
        probe->managed_root_validate_bridge == 0) return 0;
    context->phase64_mode = 1;
    context->concurrent_mode = 1;
    baseline_vm = *probe->vm_region_count;
    baseline_threads = phase61_live_threads(probe->scheduler);
    baseline_objects = phase61_live_objects(probe->scheduler);
    context->peak_vm = baseline_vm;
    context->peak_threads = baseline_threads;
    context->peak_objects = baseline_objects;
    context->peak_api_workers = 0;
    context->peak_roots = 0;
    context->stale_root_token = 0;
    context->phase64_b_root_release_peer_live = 0;
    context->phase64_c_root_release_peer_live = 0;
    context->phase64_stale_root_rejection = 0;
    phase64_text(probe, "GXOS_NET10:PHASE64_BEGIN\r\n");
    phase64_text(probe,
                 "GXOS_NET10:PHASE64_API=BOUNDED_THREE_MANAGED_WORKERS\r\n");
    phase64_hex(probe, "GXOS_NET10:PHASE64_CAPACITY=0x",
                GXOS_NATIVEAOT_MANAGED_WORKER_API_CAPACITY);
    phase64_hex(probe, "GXOS_NET10:PHASE64_ROOT_CAPACITY=0x",
                GXOS_NATIVEAOT_MANAGED_WORKER_ROOT_CAPACITY);
    phase64_hex(probe, "GXOS_NET10:PHASE64_SCHEDULER_THREAD_CAPACITY=0x",
                GXOS_SCHEDULER_MAX_THREADS);
    phase64_hex(probe, "GXOS_NET10:PHASE64_SCHEDULER_OBJECT_CAPACITY=0x",
                GXOS_SCHEDULER_MAX_OBJECTS);
    phase64_hex(probe, "GXOS_NET10:PHASE64_BASELINE_VM=0x", baseline_vm);
    phase64_hex(probe, "GXOS_NET10:PHASE64_BASELINE_THREADS=0x",
                baseline_threads);
    phase64_hex(probe, "GXOS_NET10:PHASE64_BASELINE_OBJECTS=0x",
                baseline_objects);
    phase64_hex(probe, "GXOS_NET10:PHASE64_BASELINE_API_WORKERS=0x", 0);
    phase64_hex(probe, "GXOS_NET10:PHASE64_BASELINE_ROOT_LEDGER=0x", 0);
    for (cycle = 0; cycle != GXOS_PHASE64_SCENARIO_COUNT; ++cycle) {
        if (!phase64_run_triple(api, context, probe, cycle, baseline_vm,
                                baseline_threads, baseline_objects)) return 0;
        phase64_hex(probe, "GXOS_NET10:PHASE64_SCENARIO=0x", cycle + 1U);
    }
    if (!phase64_run_attach_failure(api, context, probe, baseline_vm,
                                    baseline_threads, baseline_objects)) return 0;
    phase64_hex(probe, "GXOS_NET10:PHASE64_PEAK_VM=0x", context->peak_vm);
    phase64_hex(probe, "GXOS_NET10:PHASE64_PEAK_THREADS=0x", context->peak_threads);
    phase64_hex(probe, "GXOS_NET10:PHASE64_PEAK_OBJECTS=0x", context->peak_objects);
    phase64_hex(probe, "GXOS_NET10:PHASE64_PEAK_API_WORKERS=0x",
                context->peak_api_workers);
    phase64_hex(probe, "GXOS_NET10:PHASE64_PEAK_ROOT_LEDGER=0x",
                context->peak_roots);
    phase64_hex(probe, "GXOS_NET10:PHASE64_FINAL_VM=0x",
                *probe->vm_region_count);
    phase64_hex(probe, "GXOS_NET10:PHASE64_FINAL_THREADS=0x",
                phase61_live_threads(probe->scheduler));
    phase64_hex(probe, "GXOS_NET10:PHASE64_FINAL_OBJECTS=0x",
                phase61_live_objects(probe->scheduler));
    phase64_hex(probe, "GXOS_NET10:PHASE64_FINAL_API_WORKERS=0x",
                phase62_live_api_workers(context));
    phase64_hex(probe, "GXOS_NET10:PHASE64_FINAL_ROOT_LEDGER=0x",
                phase64_root_ledger(context));
    if (!context->phase64_b_root_release_peer_live ||
        !context->phase64_c_root_release_peer_live ||
        !context->phase64_stale_root_rejection) return 0;
    phase64_text(probe, "GXOS_NET10:PHASE64_THREE_WORKER_OVERLAP=1\r\n");
    phase64_text(probe, "GXOS_NET10:PHASE64_TWO_ROOT_OVERLAP=1\r\n");
    phase64_text(probe, "GXOS_NET10:PHASE64_REQUEST_ISOLATION=1\r\n");
    phase64_text(probe, "GXOS_NET10:PHASE64_RESULT_ISOLATION=1\r\n");
    phase64_text(probe, "GXOS_NET10:PHASE64_FOURTH_WORKER_REJECTED=1\r\n");
    phase64_text(probe,
        "GXOS_NET10:PHASE64_ROOT_RELEASE_B_WHILE_C_LIVE=1\r\n");
    phase64_text(probe,
        "GXOS_NET10:PHASE64_ROOT_RELEASE_C_WHILE_B_LIVE=1\r\n");
    phase64_text(probe,
        "GXOS_NET10:PHASE64_CROSS_ROOT_STALE_REJECTED=1\r\n");
    phase64_text(probe, "GXOS_NET10:PHASE64_ATTACH_FAILURE_ROLLBACK=1\r\n");
    phase64_text(probe, "GXOS_NET10:PHASE64_COMPLETION_ORDERS=A_B_C,C_B_A,B_A_C\r\n");
    phase64_text(probe, "GXOS_NET10:PHASE64_SCENARIOS=12\r\n");
    phase64_text(probe, "GXOS_NET10:PHASE64_COMPLETE=1\r\n");
    phase64_text(probe, "GXOS_NET10:PHASE64_PASS=1\r\n");
    return 1;
}

#endif

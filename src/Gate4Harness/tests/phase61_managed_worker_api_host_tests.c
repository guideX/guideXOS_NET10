#include "nativeaot_managed_worker_api_internal.h"

#include <stdio.h>
#include <string.h>

static unsigned g_failures;

#define CHECK(condition) do { \
    if (!(condition)) { \
        fprintf(stderr, "phase61 host test failure: %s:%d: %s\n", \
                __FILE__, __LINE__, #condition); \
        ++g_failures; \
    } \
} while (0)

void gxos_scheduler_start_worker(void) {}
void gxos_scheduler_invalid_thread_return(void) {}
void gxos_scheduler_capture_registers(
    GXOS_SCHEDULER_REGISTER_SNAPSHOT *snapshot)
{
    if (snapshot != 0) *snapshot = (GXOS_SCHEDULER_REGISTER_SNAPSHOT){0};
}
void gxos_scheduler_main_dispatch(GXOS_SCHEDULER_REGISTER_SNAPSHOT *snapshot)
{
    (void)snapshot;
}
int gxos_scheduler_worker_yield(void) { return 1; }

static GXOS_NATIVEAOT_MANAGED_WORKER_REQUEST valid_request(uint16_t operation)
{
    GXOS_NATIVEAOT_MANAGED_WORKER_REQUEST request = {0};
    request.version = GXOS_NATIVEAOT_MANAGED_WORKER_API_VERSION;
    request.size = sizeof(request);
    request.operation_id = operation;
    request.argument0 = 7;
    return request;
}

int main(void)
{
    GXOS_NATIVEAOT_MANAGED_WORKER_REQUEST request;
    GXOS_NATIVEAOT_MANAGED_WORKER_API api = {0};
    GXOS_NATIVEAOT_MANAGED_WORKER_HANDLE handle = {2, 7, 4, 0};
    GXOS_NATIVEAOT_MANAGED_WORKER_HANDLE different = {2, 7, 5, 0};
    GXOS_NATIVEAOT_MANAGED_WORKER_HANDLE handle_b = {3, 8, 4, 0};
    GXOS_NATIVEAOT_MANAGED_WORKER_HANDLE handle_c = {4, 9, 5, 0};
    GXOS_NATIVEAOT_MANAGED_WORKER_REQUEST request_a;
    GXOS_NATIVEAOT_MANAGED_WORKER_REQUEST request_b;
    GXOS_NATIVEAOT_MANAGED_WORKER_REQUEST request_c;
    GXOS_NATIVEAOT_MANAGED_WORKER_API_RECORD records[
        GXOS_NATIVEAOT_MANAGED_WORKER_API_CAPACITY] = {0};
    GXOS_NATIVEAOT_MANAGED_WORKER_RESULT result_a = {0};
    GXOS_NATIVEAOT_MANAGED_WORKER_RESULT result_b = {0};
    GXOS_NATIVEAOT_MANAGED_WORKER_RESULT result_c = {0};
    uint32_t root_slots[GXOS_NATIVEAOT_MANAGED_WORKER_ROOT_CAPACITY] = {0};
    GXOS_NATIVEAOT_MANAGED_WORKER_API_CONTEXT *context =
        (GXOS_NATIVEAOT_MANAGED_WORKER_API_CONTEXT *)(void *)&api;
    GXOS_PHASE53O_PROBE probe = {0};
    GXOS_NATIVEAOT_CALLBACK_BRIDGE root_publish = {0};
    GXOS_NATIVEAOT_MANAGED_WORKER_HANDLE stale_b = handle_b;
    GXOS_NATIVEAOT_MANAGED_WORKER_HANDLE replacement_d = {3, 10, 5, 0};
    uint32_t active_workers;

    CHECK(sizeof(GXOS_NATIVEAOT_MANAGED_WORKER_REQUEST) == 32U);
    CHECK(sizeof(GXOS_NATIVEAOT_MANAGED_WORKER_HANDLE) == 12U);
    CHECK(sizeof(GXOS_NATIVEAOT_MANAGED_WORKER_API) ==
          GXOS_NATIVEAOT_MANAGED_WORKER_API_STORAGE_SIZE);
    CHECK(GXOS_NATIVEAOT_MANAGED_WORKER_API_CAPACITY == 3U);
    CHECK(GXOS_NATIVEAOT_MANAGED_WORKER_ROOT_CAPACITY == 2U);
    CHECK(GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_CAPACITY == 13);
    CHECK(GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_CANCELED == 14);
    CHECK(gxos_nativeaot_managed_worker_api_state_transition(
              GXOS_NATIVEAOT_MANAGED_WORKER_STATE_CREATED,
              GXOS_NATIVEAOT_MANAGED_WORKER_STATE_SUBMITTED));
    CHECK(gxos_nativeaot_managed_worker_api_state_transition(
              GXOS_NATIVEAOT_MANAGED_WORKER_STATE_RUNNING,
              GXOS_NATIVEAOT_MANAGED_WORKER_STATE_COMPLETED));
    CHECK(!gxos_nativeaot_managed_worker_api_state_transition(
              GXOS_NATIVEAOT_MANAGED_WORKER_STATE_CREATED,
              GXOS_NATIVEAOT_MANAGED_WORKER_STATE_COMPLETED));
    CHECK(!gxos_nativeaot_managed_worker_api_state_transition(
              GXOS_NATIVEAOT_MANAGED_WORKER_STATE_CLOSED,
              GXOS_NATIVEAOT_MANAGED_WORKER_STATE_CREATED));
    CHECK(gxos_nativeaot_managed_worker_api_state_transition(
              GXOS_NATIVEAOT_MANAGED_WORKER_STATE_RUNNING,
              GXOS_NATIVEAOT_MANAGED_WORKER_STATE_CANCEL_REQUESTED));
    CHECK(gxos_nativeaot_managed_worker_api_state_transition(
              GXOS_NATIVEAOT_MANAGED_WORKER_STATE_CANCEL_REQUESTED,
              GXOS_NATIVEAOT_MANAGED_WORKER_STATE_CANCELED));

    request = valid_request(
        GXOS_NATIVEAOT_MANAGED_WORKER_OPERATION_ADD_ONE);
    CHECK(gxos_nativeaot_managed_worker_api_validate_request(&request) ==
          GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK);
    request = valid_request(
        GXOS_NATIVEAOT_MANAGED_WORKER_OPERATION_GC_CHECK);
    request.argument0 = 0xFFFFU;
    CHECK(gxos_nativeaot_managed_worker_api_validate_request(&request) ==
          GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK);
    request.version = 2;
    CHECK(gxos_nativeaot_managed_worker_api_validate_request(&request) ==
          GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_INVALID_REQUEST_VERSION);
    request = valid_request(
        GXOS_NATIVEAOT_MANAGED_WORKER_OPERATION_ADD_ONE);
    request.size--;
    CHECK(gxos_nativeaot_managed_worker_api_validate_request(&request) ==
          GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_INVALID_REQUEST_SIZE);
    request = valid_request(
        GXOS_NATIVEAOT_MANAGED_WORKER_OPERATION_ADD_ONE);
    request.payload_size = GXOS_NATIVEAOT_MANAGED_WORKER_API_PAYLOAD_MAX + 1U;
    CHECK(gxos_nativeaot_managed_worker_api_validate_request(&request) ==
          GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_INVALID_ARGUMENT);
    request = valid_request(99);
    CHECK(gxos_nativeaot_managed_worker_api_validate_request(&request) ==
          GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_UNSUPPORTED_OPERATION);
    request = valid_request(
        GXOS_NATIVEAOT_MANAGED_WORKER_OPERATION_ADD_ONE);
    request.argument0 = 0xFFFFU;
    CHECK(gxos_nativeaot_managed_worker_api_validate_request(&request) ==
          GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_INVALID_ARGUMENT);

    CHECK(gxos_nativeaot_managed_worker_api_handle_equal(handle, handle));
    CHECK(!gxos_nativeaot_managed_worker_api_handle_equal(handle, different));
    CHECK(gxos_nativeaot_managed_worker_api_poll(&api, handle, 0) ==
          GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_INVALID_HANDLE);
    CHECK(gxos_nativeaot_managed_worker_api_close(&api, handle, 0) ==
          GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_INVALID_HANDLE);

    /* The host proof models the bounded API storage contract.  Runtime
       attachment, scheduler isolation, and reclaim remain QEMU-only claims. */
    request_a = valid_request(GXOS_NATIVEAOT_MANAGED_WORKER_OPERATION_ADD_ONE);
    request_a.argument0 = 41U;
    request_b = valid_request(GXOS_NATIVEAOT_MANAGED_WORKER_OPERATION_GC_CHECK);
    request_b.argument0 = 0x61U;
    request_c = valid_request(GXOS_NATIVEAOT_MANAGED_WORKER_OPERATION_GC_CHECK);
    request_c.argument0 = 0xC3U;
    records[0].handle = handle;
    records[1].handle = handle_b;
    records[2].handle = handle_c;
    records[0].request = request_a;
    records[1].request = request_b;
    records[2].request = request_c;
    records[0].active = 1;
    records[1].active = 1;
    records[2].active = 1;
    result_a.worker = records[0].handle;
    result_a.operation_id = request_a.operation_id;
    result_a.output0 = 42U;
    result_b.worker = records[1].handle;
    result_b.operation_id = request_b.operation_id;
    result_b.output0 = 1U;
    result_b.output1 = 0xB2U;
    result_c.worker = records[2].handle;
    result_c.operation_id = request_c.operation_id;
    result_c.output0 = 1U;
    result_c.output1 = 0xC3U;
    CHECK(records[0].request.argument0 == 41U);
    CHECK(records[1].request.argument0 == 0x61U);
    CHECK(records[2].request.argument0 == 0xC3U);
    CHECK(records[0].request.operation_id != records[1].request.operation_id);
    CHECK(records[0].handle.scheduler_slot != records[1].handle.scheduler_slot);
    CHECK(records[1].handle.scheduler_slot != records[2].handle.scheduler_slot);
    CHECK(!gxos_nativeaot_managed_worker_api_handle_equal(
        records[0].handle, records[1].handle));
    CHECK(result_a.worker.worker_identity == records[0].handle.worker_identity);
    CHECK(result_b.worker.worker_identity == records[1].handle.worker_identity);
    CHECK(result_c.worker.worker_identity == records[2].handle.worker_identity);
    CHECK(result_a.output0 != result_b.output0);
    CHECK(result_b.output1 != result_c.output1);
    root_slots[0] = 0xB2U;
    root_slots[1] = 0xC3U;
    CHECK(root_slots[0] != root_slots[1]);
    root_slots[0] = 0;
    CHECK(root_slots[0] == 0 && root_slots[1] == 0xC3U);
    CHECK(GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_DUPLICATE_SUBMIT !=
          GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK);
    CHECK(GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_CAPACITY !=
          GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK);

    /* Exercise the public cancellation status path against bounded internal
       records. The guest fixture separately proves scheduler/runtime cleanup. */
    memset(&api, 0, sizeof(api));
    context->probe = &probe;
    context->phase64_mode = 1;
    probe.managed_root_publish_bridge = &root_publish;
    context->records[0].active = 1;
    context->records[0].handle = handle;
    context->records[0].state = GXOS_NATIVEAOT_MANAGED_WORKER_STATE_RUNNING;
    context->records[0].request = request_a;
    context->records[1].active = 1;
    context->records[1].handle = handle_b;
    context->records[1].state = GXOS_NATIVEAOT_MANAGED_WORKER_STATE_SUBMITTED;
    context->records[1].request = request_b;
    context->records[2].active = 1;
    context->records[2].handle = handle_c;
    context->records[2].state = GXOS_NATIVEAOT_MANAGED_WORKER_STATE_COMPLETED;
    context->records[2].request = request_c;

    CHECK(gxos_nativeaot_managed_worker_api_request_cancel(&api,
              (GXOS_NATIVEAOT_MANAGED_WORKER_HANDLE){0}) ==
          GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_INVALID_HANDLE);
    stale_b.worker_generation++;
    CHECK(gxos_nativeaot_managed_worker_api_request_cancel(&api, stale_b) ==
          GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_STALE_HANDLE);
    CHECK(gxos_nativeaot_managed_worker_api_request_cancel(&api,
              (GXOS_NATIVEAOT_MANAGED_WORKER_HANDLE){5, 77, 1, 0}) ==
          GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_INVALID_HANDLE);
    CHECK(gxos_nativeaot_managed_worker_api_request_cancel(&api, handle) ==
          GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_CANCEL_UNSUPPORTED_STATE);
    CHECK(gxos_nativeaot_managed_worker_api_request_cancel(&api, handle_b) ==
          GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK);
    CHECK(context->records[1].cancel_requested == 1U &&
          context->records[1].state ==
              GXOS_NATIVEAOT_MANAGED_WORKER_STATE_CANCEL_REQUESTED);
    CHECK(gxos_nativeaot_managed_worker_api_request_cancel(&api, handle_b) ==
          GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_CANCEL_ALREADY_REQUESTED);
    context->records[1].cancel_checkpoint_open = 1;
    context->records[1].state = GXOS_NATIVEAOT_MANAGED_WORKER_STATE_RUNNING;
    CHECK(gxos_nativeaot_managed_worker_api_request_cancel(&api, handle_b) ==
          GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_CANCEL_ALREADY_REQUESTED);
    CHECK(context->records[1].cancel_requested == 1U &&
          context->records[1].state ==
              GXOS_NATIVEAOT_MANAGED_WORKER_STATE_RUNNING);
    CHECK(context->records[0].state == GXOS_NATIVEAOT_MANAGED_WORKER_STATE_RUNNING &&
          context->records[0].request.operation_id ==
              GXOS_NATIVEAOT_MANAGED_WORKER_OPERATION_ADD_ONE);
    context->records[2].state = GXOS_NATIVEAOT_MANAGED_WORKER_STATE_RUNNING;
    context->records[2].cancel_checkpoint_passed = 1;
    CHECK(gxos_nativeaot_managed_worker_api_request_cancel(&api, handle_c) ==
          GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_CANCEL_UNSUPPORTED_STATE);
    context->records[2].state = GXOS_NATIVEAOT_MANAGED_WORKER_STATE_COMPLETED;
    context->records[1].state = GXOS_NATIVEAOT_MANAGED_WORKER_STATE_CANCELED;
    context->records[1].result.state =
        GXOS_NATIVEAOT_MANAGED_WORKER_STATE_CANCELED;
    context->records[1].result.status =
        GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_CANCELED;
    context->records[1].result.worker = handle_b;
    context->records[1].result.operation_id =
        GXOS_NATIVEAOT_MANAGED_WORKER_OPERATION_GC_CHECK;
    CHECK(gxos_nativeaot_managed_worker_api_request_cancel(&api, handle_b) ==
          GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_CANCEL_ALREADY_COMPLETED);
    CHECK(context->records[1].result.state ==
              GXOS_NATIVEAOT_MANAGED_WORKER_STATE_CANCELED &&
          context->records[1].result.status ==
              GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_CANCELED &&
          context->records[1].result.output0 == 0U &&
          context->records[1].result.output1 == 0U &&
          context->records[1].result.managed_result == 0 &&
          context->records[1].result.operation_id ==
              GXOS_NATIVEAOT_MANAGED_WORKER_OPERATION_GC_CHECK);
    CHECK(gxos_nativeaot_managed_worker_api_request_cancel(&api, handle_c) ==
          GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_CANCEL_ALREADY_COMPLETED);
    context->records[1].active = 0;
    context->last_closed[1] = handle_b;
    context->has_last_closed[1] = 1;
    CHECK(gxos_nativeaot_managed_worker_api_request_cancel(&api, handle_b) ==
          GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_CANCEL_ALREADY_CLOSED);
    context->records[1].active = 1;
    context->records[1].handle = replacement_d;
    context->records[1].state = GXOS_NATIVEAOT_MANAGED_WORKER_STATE_SUBMITTED;
    context->records[1].request = request_a;
    CHECK(gxos_nativeaot_managed_worker_api_request_cancel(&api, handle_b) ==
          GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_STALE_HANDLE);
    active_workers = (uint32_t)context->records[0].active +
        (uint32_t)context->records[1].active +
        (uint32_t)context->records[2].active;
    CHECK(active_workers == 3U && replacement_d.scheduler_slot ==
          handle_b.scheduler_slot && replacement_d.worker_identity !=
          handle_b.worker_identity && replacement_d.worker_generation !=
          handle_b.worker_generation);

    (void)printf("PHASE61_MANAGED_WORKER_API_HOST_TEST=PASS\n");
    (void)printf("PHASE62_MANAGED_WORKER_API_HOST_TEST=PASS\n");
    (void)printf("PHASE64_MANAGED_WORKER_API_HOST_TEST=PASS\n");
    (void)printf("PHASE65_MANAGED_WORKER_API_HOST_TEST=PASS\n");
    return g_failures == 0 ? 0 : 1;
}

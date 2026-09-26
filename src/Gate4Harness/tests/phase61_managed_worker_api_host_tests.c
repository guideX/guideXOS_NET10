#include "nativeaot_managed_worker_api.h"

#include <stdio.h>

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
    GXOS_NATIVEAOT_MANAGED_WORKER_REQUEST request_a;
    GXOS_NATIVEAOT_MANAGED_WORKER_REQUEST request_b;
    GXOS_NATIVEAOT_MANAGED_WORKER_API_RECORD records[
        GXOS_NATIVEAOT_MANAGED_WORKER_API_CAPACITY] = {0};
    GXOS_NATIVEAOT_MANAGED_WORKER_RESULT result_a = {0};
    GXOS_NATIVEAOT_MANAGED_WORKER_RESULT result_b = {0};

    CHECK(sizeof(GXOS_NATIVEAOT_MANAGED_WORKER_REQUEST) == 32U);
    CHECK(sizeof(GXOS_NATIVEAOT_MANAGED_WORKER_HANDLE) == 12U);
    CHECK(sizeof(GXOS_NATIVEAOT_MANAGED_WORKER_API) ==
          GXOS_NATIVEAOT_MANAGED_WORKER_API_STORAGE_SIZE);
    CHECK(GXOS_NATIVEAOT_MANAGED_WORKER_API_CAPACITY == 2U);
    CHECK(GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_CAPACITY == 13);
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
    records[0].handle = handle;
    records[1].handle = handle_b;
    records[0].request = request_a;
    records[1].request = request_b;
    records[0].active = 1;
    records[1].active = 1;
    result_a.worker = records[0].handle;
    result_a.operation_id = request_a.operation_id;
    result_a.output0 = 42U;
    result_b.worker = records[1].handle;
    result_b.operation_id = request_b.operation_id;
    result_b.output0 = 1U;
    CHECK(records[0].request.argument0 == 41U);
    CHECK(records[1].request.argument0 == 0x61U);
    CHECK(records[0].request.operation_id != records[1].request.operation_id);
    CHECK(records[0].handle.scheduler_slot != records[1].handle.scheduler_slot);
    CHECK(!gxos_nativeaot_managed_worker_api_handle_equal(
        records[0].handle, records[1].handle));
    CHECK(result_a.worker.worker_identity == records[0].handle.worker_identity);
    CHECK(result_b.worker.worker_identity == records[1].handle.worker_identity);
    CHECK(result_a.output0 != result_b.output0);
    CHECK(GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_DUPLICATE_SUBMIT !=
          GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK);
    CHECK(GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_CAPACITY !=
          GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK);

    (void)printf("PHASE61_MANAGED_WORKER_API_HOST_TEST=PASS\n");
    (void)printf("PHASE62_MANAGED_WORKER_API_HOST_TEST=PASS\n");
    return g_failures == 0 ? 0 : 1;
}

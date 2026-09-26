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

    CHECK(sizeof(GXOS_NATIVEAOT_MANAGED_WORKER_REQUEST) == 32U);
    CHECK(sizeof(GXOS_NATIVEAOT_MANAGED_WORKER_HANDLE) == 12U);
    CHECK(sizeof(GXOS_NATIVEAOT_MANAGED_WORKER_API) ==
          GXOS_NATIVEAOT_MANAGED_WORKER_API_STORAGE_SIZE);
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

    (void)printf("PHASE61_MANAGED_WORKER_API_HOST_TEST=PASS\n");
    return g_failures == 0 ? 0 : 1;
}

#ifndef GXOS_NATIVEAOT_MANAGED_WORKER_API_H
#define GXOS_NATIVEAOT_MANAGED_WORKER_API_H

#include <stdint.h>

#include "nativeaot_scheduler_thread_lifecycle.h"

#define GXOS_NATIVEAOT_MANAGED_WORKER_API_VERSION 1U
#define GXOS_NATIVEAOT_MANAGED_WORKER_API_PAYLOAD_MAX 16U
#define GXOS_NATIVEAOT_MANAGED_WORKER_API_CAPACITY 2U

typedef enum {
    GXOS_NATIVEAOT_MANAGED_WORKER_OPERATION_ADD_ONE = 1,
    GXOS_NATIVEAOT_MANAGED_WORKER_OPERATION_GC_CHECK = 2
} GXOS_NATIVEAOT_MANAGED_WORKER_OPERATION;

typedef enum {
    GXOS_NATIVEAOT_MANAGED_WORKER_STATE_FREE = 0,
    GXOS_NATIVEAOT_MANAGED_WORKER_STATE_CREATED = 1,
    GXOS_NATIVEAOT_MANAGED_WORKER_STATE_SUBMITTED = 2,
    GXOS_NATIVEAOT_MANAGED_WORKER_STATE_RUNNING = 3,
    GXOS_NATIVEAOT_MANAGED_WORKER_STATE_COMPLETED = 4,
    GXOS_NATIVEAOT_MANAGED_WORKER_STATE_FAILED = 5,
    GXOS_NATIVEAOT_MANAGED_WORKER_STATE_CLOSED = 6
} GXOS_NATIVEAOT_MANAGED_WORKER_STATE;

typedef enum {
    GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_OK = 0,
    GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_INVALID_ARGUMENT = 1,
    GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_INVALID_REQUEST_VERSION = 2,
    GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_INVALID_REQUEST_SIZE = 3,
    GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_UNSUPPORTED_OPERATION = 4,
    GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_INVALID_HANDLE = 5,
    GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_STALE_HANDLE = 6,
    GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_INVALID_STATE = 7,
    GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_DUPLICATE_SUBMIT = 8,
    GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_NOT_COMPLETE = 9,
    GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_CLOSE_BEFORE_COMPLETE = 10,
    GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_DUPLICATE_CLOSE = 11,
    GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_INTERNAL_FAILURE = 12,
    GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_CAPACITY = 13
} GXOS_NATIVEAOT_MANAGED_WORKER_STATUS;

/* Fixed-size, caller-owned request.  The payload is reserved for future
   bounded operations; Phase 61 deliberately supports no pointer-bearing
   payload and no callback registration. */
typedef struct {
    uint16_t version;
    uint16_t size;
    uint16_t operation_id;
    uint16_t payload_size;
    uint32_t argument0;
    uint32_t argument1;
    uint8_t payload[GXOS_NATIVEAOT_MANAGED_WORKER_API_PAYLOAD_MAX];
} GXOS_NATIVEAOT_MANAGED_WORKER_REQUEST;

/* This value is the only worker identity accepted by API operations.  It is
   intentionally independent of scheduler handles and TCB pointers. */
typedef struct {
    uint32_t scheduler_slot;
    uint32_t worker_identity;
    uint16_t worker_generation;
    uint16_t reserved;
} GXOS_NATIVEAOT_MANAGED_WORKER_HANDLE;

typedef struct {
    uint16_t version;
    uint16_t size;
    uint16_t state;
    uint16_t status;
    uint16_t operation_id;
    uint16_t reserved;
    int32_t result_code;
    uint32_t output0;
    uint32_t output1;
    int32_t managed_result;
    GXOS_NATIVEAOT_MANAGED_WORKER_HANDLE worker;
} GXOS_NATIVEAOT_MANAGED_WORKER_RESULT;

typedef struct GXOS_NATIVEAOT_MANAGED_WORKER_API_RECORD {
    GXOS_PHASE53O_PROBE *probe;
    GXOS_NATIVEAOT_MANAGED_WORKER_REQUEST request;
    GXOS_NATIVEAOT_MANAGED_WORKER_RESULT result;
    GXOS_NATIVEAOT_MANAGED_WORKER_HANDLE handle;
    GXOS_SCHEDULER_HANDLE scheduler_handle;
    GXOS_SCHEDULER_TCB *thread;
    GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE lifecycle;
    void *owner_context;
    uint64_t runtime_fls_value;
    uint64_t runtime_tls_block_value;
    GXOS_NATIVEAOT_MANAGED_WORKER_STATE state;
    uint8_t active;
    uint8_t yielded;
    uint8_t reserved[3];
} GXOS_NATIVEAOT_MANAGED_WORKER_API_RECORD;

/* The service context is private to the implementation.  Callers receive
   only aligned opaque storage; raw probes, scheduler handles, and TCB
   pointers never cross this public boundary. */
#define GXOS_NATIVEAOT_MANAGED_WORKER_API_STORAGE_SIZE 1536U
typedef union {
    uintptr_t alignment;
    uint8_t bytes[GXOS_NATIVEAOT_MANAGED_WORKER_API_STORAGE_SIZE];
} GXOS_NATIVEAOT_MANAGED_WORKER_API;

int gxos_nativeaot_managed_worker_api_validate_request(
    const GXOS_NATIVEAOT_MANAGED_WORKER_REQUEST *request);

int gxos_nativeaot_managed_worker_api_handle_equal(
    GXOS_NATIVEAOT_MANAGED_WORKER_HANDLE left,
    GXOS_NATIVEAOT_MANAGED_WORKER_HANDLE right);

int gxos_nativeaot_managed_worker_api_state_transition(
    GXOS_NATIVEAOT_MANAGED_WORKER_STATE from,
    GXOS_NATIVEAOT_MANAGED_WORKER_STATE to);

int gxos_nativeaot_managed_worker_api_initialize(
    GXOS_NATIVEAOT_MANAGED_WORKER_API *api,
    GXOS_PHASE53O_PROBE *probe);

GXOS_NATIVEAOT_MANAGED_WORKER_STATUS
gxos_nativeaot_managed_worker_api_create(
    GXOS_NATIVEAOT_MANAGED_WORKER_API *api,
    GXOS_NATIVEAOT_MANAGED_WORKER_HANDLE *handle_out);

GXOS_NATIVEAOT_MANAGED_WORKER_STATUS
gxos_nativeaot_managed_worker_api_submit(
    GXOS_NATIVEAOT_MANAGED_WORKER_API *api,
    GXOS_NATIVEAOT_MANAGED_WORKER_HANDLE handle,
    const GXOS_NATIVEAOT_MANAGED_WORKER_REQUEST *request);

GXOS_NATIVEAOT_MANAGED_WORKER_STATUS
gxos_nativeaot_managed_worker_api_drive(
    GXOS_NATIVEAOT_MANAGED_WORKER_API *api,
    GXOS_NATIVEAOT_MANAGED_WORKER_HANDLE handle);

GXOS_NATIVEAOT_MANAGED_WORKER_STATUS
gxos_nativeaot_managed_worker_api_poll(
    GXOS_NATIVEAOT_MANAGED_WORKER_API *api,
    GXOS_NATIVEAOT_MANAGED_WORKER_HANDLE handle,
    GXOS_NATIVEAOT_MANAGED_WORKER_RESULT *result_out);

GXOS_NATIVEAOT_MANAGED_WORKER_STATUS
gxos_nativeaot_managed_worker_api_close(
    GXOS_NATIVEAOT_MANAGED_WORKER_API *api,
    GXOS_NATIVEAOT_MANAGED_WORKER_HANDLE handle,
    GXOS_NATIVEAOT_MANAGED_WORKER_RESULT *result_out);

/* Diagnostic fixture only: drives twelve sequential API calls while using
   the same production lifecycle and proves stale-generation rejection. */
int gxos_nativeaot_managed_worker_api_probe(
    GXOS_NATIVEAOT_MANAGED_WORKER_API *api);

/* Diagnostic fixture only: proves two concurrently live API workers and
   preserves the Phase 61 sequential probe as a separate configuration. */
int gxos_nativeaot_managed_worker_api_concurrent_probe(
    GXOS_NATIVEAOT_MANAGED_WORKER_API *api);

#endif

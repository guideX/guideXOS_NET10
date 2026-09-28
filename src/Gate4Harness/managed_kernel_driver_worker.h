#ifndef GXOS_MANAGED_KERNEL_DRIVER_WORKER_H
#define GXOS_MANAGED_KERNEL_DRIVER_WORKER_H

#include <stdint.h>

#include "event_api.h"
#include "managed_kernel_interrupt.h"
#include "nativeaot_scheduler_thread_lifecycle.h"

#define GXOS_MANAGED_KERNEL_DRIVER_WORKER_MAX_BATCHES 4U
#define GXOS_MANAGED_KERNEL_DRIVER_WORKER_STAGE_START 1U
#define GXOS_MANAGED_KERNEL_DRIVER_WORKER_STAGE_DISPATCH 2U
#define GXOS_MANAGED_KERNEL_DRIVER_WORKER_STAGE_STOP 3U
#define GXOS_MANAGED_KERNEL_DRIVER_WORKER_STAGE_FAILURE_CLEANUP 4U
#define GXOS_MANAGED_KERNEL_DRIVER_RESTART_PREPARE_STAGE 5U
#define GXOS_MANAGED_KERNEL_DRIVER_RESTART_ABORT_STAGE 6U
#define GXOS_MANAGED_KERNEL_DRIVER_SERVICE_CAPACITY 1U
#define GXOS_MANAGED_KERNEL_DRIVER_RECOVERABLE_DISPATCH_FAILURE 0xF0700001U

typedef struct {
    uint32_t identity;
    uint32_t device_identity;
    uint16_t generation;
    uint16_t slot;
} GXOS_MANAGED_KERNEL_DRIVER_SERVICE_HANDLE;

typedef enum {
    GXOS_MANAGED_KERNEL_DRIVER_SERVICE_FREE = 0,
    GXOS_MANAGED_KERNEL_DRIVER_SERVICE_ALLOCATED = 1,
    GXOS_MANAGED_KERNEL_DRIVER_SERVICE_STARTING = 2,
    GXOS_MANAGED_KERNEL_DRIVER_SERVICE_RUNTIME_ATTACHED = 3,
    GXOS_MANAGED_KERNEL_DRIVER_SERVICE_RUNNING = 4,
    GXOS_MANAGED_KERNEL_DRIVER_SERVICE_WAITING = 5,
    GXOS_MANAGED_KERNEL_DRIVER_SERVICE_STOP_REQUESTED = 6,
    GXOS_MANAGED_KERNEL_DRIVER_SERVICE_STOPPING = 7,
    GXOS_MANAGED_KERNEL_DRIVER_SERVICE_RUNTIME_DETACHED = 8,
    GXOS_MANAGED_KERNEL_DRIVER_SERVICE_RECLAIMABLE = 9,
    GXOS_MANAGED_KERNEL_DRIVER_SERVICE_RECLAIMED = 10,
    GXOS_MANAGED_KERNEL_DRIVER_SERVICE_START_FAILED = 11,
    GXOS_MANAGED_KERNEL_DRIVER_SERVICE_QUARANTINED = 12,
    GXOS_MANAGED_KERNEL_DRIVER_SERVICE_FAILED = 13,
    GXOS_MANAGED_KERNEL_DRIVER_SERVICE_RESTART_FAILED = 14
} GXOS_MANAGED_KERNEL_DRIVER_SERVICE_STATE;

typedef uint32_t (GX_MANAGED_KERNEL_MS_ABI
                 *GXOS_MANAGED_KERNEL_DRIVER_RESTART_PREPARE)(uint32_t stage);

typedef enum {
    GXOS_MANAGED_KERNEL_DRIVER_SERVICE_DRAIN = 1,
    GXOS_MANAGED_KERNEL_DRIVER_SERVICE_DISCARD = 2
} GXOS_MANAGED_KERNEL_DRIVER_SERVICE_SHUTDOWN_POLICY;

typedef enum {
    GXOS_MANAGED_KERNEL_DRIVER_SERVICE_RESULT_OK = 0,
    GXOS_MANAGED_KERNEL_DRIVER_SERVICE_RESULT_INVALID = 1,
    GXOS_MANAGED_KERNEL_DRIVER_SERVICE_RESULT_CAPACITY = 2,
    GXOS_MANAGED_KERNEL_DRIVER_SERVICE_RESULT_RESOURCE_FAILURE = 3
} GXOS_MANAGED_KERNEL_DRIVER_SERVICE_RESULT;

typedef void (*GXOS_MANAGED_KERNEL_DRIVER_WORKER_LOG_TEXT)(const char *text);
typedef void (*GXOS_MANAGED_KERNEL_DRIVER_WORKER_LOG_HEX)(const char *name,
                                                           uint64_t value);

typedef enum {
    GXOS_MANAGED_KERNEL_DRIVER_WORKER_FREE = 0,
    GXOS_MANAGED_KERNEL_DRIVER_WORKER_CREATED = 1,
    GXOS_MANAGED_KERNEL_DRIVER_WORKER_STARTING = 2,
    GXOS_MANAGED_KERNEL_DRIVER_WORKER_RUNNING = 3,
    GXOS_MANAGED_KERNEL_DRIVER_WORKER_STOPPING = 4,
    GXOS_MANAGED_KERNEL_DRIVER_WORKER_STOPPED = 5,
    GXOS_MANAGED_KERNEL_DRIVER_WORKER_DESTROYED = 6
} GXOS_MANAGED_KERNEL_DRIVER_WORKER_STATE;

typedef struct {
    GXOS_SCHEDULER *scheduler;
    GXOS_EVENT_API_CONTEXT *event_api;
    GXOS_MANAGED_KERNEL_INTERRUPT_CONTEXT *interrupt;
    GXOS_NATIVEAOT_CALLBACK_BRIDGE *managed_bridge;
    GXOS_MANAGED_KERNEL_DRIVER_WORKER_LOG_TEXT log_text;
    GXOS_MANAGED_KERNEL_DRIVER_WORKER_LOG_HEX log_hex;
    GXOS_SCHEDULER_HANDLE worker_handle;
    GXOS_SCHEDULER_HANDLE wake_event;
    GXOS_SCHEDULER_TCB *thread;
    volatile uint32_t state;
    volatile uint32_t stop_requested;
    volatile uint32_t failure;
    GXOS_MANAGED_KERNEL_DRIVER_SERVICE_STATE service_state;
    GXOS_MANAGED_KERNEL_DRIVER_SERVICE_HANDLE service_handle;
    GXOS_MANAGED_KERNEL_DRIVER_RESTART_PREPARE restart_prepare;
    uint32_t service_identity;
    uint32_t device_identity;
    uint16_t service_generation;
    uint32_t route_published;
    uint32_t tcb_owned;
    uint32_t thread_handle_owned;
    uint32_t wake_event_owned;
    uint32_t wake_event_handle_open;
    uint32_t runtime_attach_attempted;
    uint32_t managed_loop_entered;
    GXOS_MANAGED_KERNEL_DRIVER_SERVICE_SHUTDOWN_POLICY shutdown_policy;
    uint64_t shutdown_discarded_count;
    uint64_t failure_discarded_count;
    uint8_t sleeping_marker_emitted;
    uint8_t wake_marker_emitted;
    uint16_t reserved;
    uint64_t scheduler_dispatch_count;
    uint64_t worker_wake_count;
    uint64_t dispatch_batch_count;
    uint64_t managed_dispatch_count;
    uint64_t sleep_count;
    uint64_t yield_count;
    uint64_t rearm_count;
    GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE nativeaot_lifecycle;
    uint32_t last_callback_status;
    uint32_t managed_worker_publication_pass;
    uint32_t gc_proof_pass;
    uint32_t stale_context_detection_count;
    uint64_t first_allocation_address;
    uint64_t first_allocation_size;
    uint64_t first_alloc_ptr_before;
    uint64_t first_alloc_ptr_after;
    uint64_t main_alloc_ptr_at_first_allocation;
    uint64_t stale_context_offender;
#ifdef GXOS_ENABLE_PHASE70_RESTART_FIXTURE
    volatile uint32_t restart_failure_armed;
    uint32_t restart_failure_after_dispatch;
    uint32_t restart_failure_fired;
#endif
#ifdef GXOS_ENABLE_PHASE71_RESTART_ADMISSION_FAILURE_FIXTURE
    volatile uint32_t restart_admission_failure_armed;
    uint32_t restart_admission_failure_attempts;
    uint32_t restart_admission_failure_fired;
    uint32_t restart_admission_natural_result;
    uint32_t restart_admission_failure_result;
#endif
} GXOS_MANAGED_KERNEL_DRIVER_WORKER_CONTEXT;

GXOS_MANAGED_KERNEL_DRIVER_SERVICE_RESULT
gxos_managed_kernel_driver_worker_initialize(
    GXOS_MANAGED_KERNEL_DRIVER_WORKER_CONTEXT *context,
    GXOS_SCHEDULER *scheduler, GXOS_EVENT_API_CONTEXT *event_api,
    GXOS_MANAGED_KERNEL_INTERRUPT_CONTEXT *interrupt,
    GXOS_NATIVEAOT_CALLBACK_BRIDGE *managed_bridge,
    GXOS_MANAGED_KERNEL_DRIVER_RESTART_PREPARE restart_prepare,
    uint32_t tls_index, uint32_t runtime_fls_slot,
    GXOS_NATIVEAOT_FLS_CLEANUP_CALLBACK runtime_fls_cleanup,
    GXOS_MANAGED_KERNEL_DRIVER_WORKER_LOG_TEXT log_text,
    GXOS_MANAGED_KERNEL_DRIVER_WORKER_LOG_HEX log_hex);

int gxos_managed_kernel_driver_worker_publish_route(
    GXOS_MANAGED_KERNEL_DRIVER_WORKER_CONTEXT *context,
    GXOS_MANAGED_KERNEL_DRIVER_SERVICE_HANDLE *handle_out);

int gxos_managed_kernel_driver_worker_start(
    GXOS_MANAGED_KERNEL_DRIVER_WORKER_CONTEXT *context,
    GXOS_MANAGED_KERNEL_DRIVER_SERVICE_HANDLE handle);

int gxos_managed_kernel_driver_worker_request_stop(
    GXOS_MANAGED_KERNEL_DRIVER_WORKER_CONTEXT *context,
    GXOS_MANAGED_KERNEL_DRIVER_SERVICE_HANDLE handle,
    GXOS_MANAGED_KERNEL_DRIVER_SERVICE_SHUTDOWN_POLICY policy);

int gxos_managed_kernel_driver_worker_pump(
    GXOS_MANAGED_KERNEL_DRIVER_WORKER_CONTEXT *context,
    GXOS_MANAGED_KERNEL_DRIVER_SERVICE_HANDLE handle);

/* Owner-facing pump updates the caller's opaque handle after an automatic
   generation replacement. A stale handle remains rejected on later calls. */
int gxos_managed_kernel_driver_worker_pump_current(
    GXOS_MANAGED_KERNEL_DRIVER_WORKER_CONTEXT *context,
    GXOS_MANAGED_KERNEL_DRIVER_SERVICE_HANDLE *handle_inout);

int gxos_managed_kernel_driver_worker_stop(
    GXOS_MANAGED_KERNEL_DRIVER_WORKER_CONTEXT *context,
    GXOS_MANAGED_KERNEL_DRIVER_SERVICE_HANDLE handle,
    GXOS_MANAGED_KERNEL_DRIVER_SERVICE_SHUTDOWN_POLICY policy);

int gxos_managed_kernel_driver_worker_destroy(
    GXOS_MANAGED_KERNEL_DRIVER_WORKER_CONTEXT *context,
    GXOS_MANAGED_KERNEL_DRIVER_SERVICE_HANDLE handle);

int gxos_managed_kernel_driver_worker_is_running(
    const GXOS_MANAGED_KERNEL_DRIVER_WORKER_CONTEXT *context,
    GXOS_MANAGED_KERNEL_DRIVER_SERVICE_HANDLE handle);

#endif

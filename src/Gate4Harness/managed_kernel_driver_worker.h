#ifndef GXOS_MANAGED_KERNEL_DRIVER_WORKER_H
#define GXOS_MANAGED_KERNEL_DRIVER_WORKER_H

#include <stdint.h>

#include "event_api.h"
#include "managed_kernel_interrupt.h"
#include "nativeaot_scheduler_thread_lifecycle.h"

#define GXOS_MANAGED_KERNEL_DRIVER_WORKER_MAX_BATCHES 4U
#define GXOS_MANAGED_KERNEL_DRIVER_WORKER_STAGE_START 1U
#define GXOS_MANAGED_KERNEL_DRIVER_WORKER_STAGE_DISPATCH 2U

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
} GXOS_MANAGED_KERNEL_DRIVER_WORKER_CONTEXT;

int gxos_managed_kernel_driver_worker_initialize(
    GXOS_MANAGED_KERNEL_DRIVER_WORKER_CONTEXT *context,
    GXOS_SCHEDULER *scheduler, GXOS_EVENT_API_CONTEXT *event_api,
    GXOS_MANAGED_KERNEL_INTERRUPT_CONTEXT *interrupt,
    GXOS_NATIVEAOT_CALLBACK_BRIDGE *managed_bridge,
    uint32_t tls_index, uint32_t runtime_fls_slot,
    GXOS_NATIVEAOT_FLS_CLEANUP_CALLBACK runtime_fls_cleanup,
    GXOS_MANAGED_KERNEL_DRIVER_WORKER_LOG_TEXT log_text,
    GXOS_MANAGED_KERNEL_DRIVER_WORKER_LOG_HEX log_hex);

int gxos_managed_kernel_driver_worker_pump(
    GXOS_MANAGED_KERNEL_DRIVER_WORKER_CONTEXT *context);

int gxos_managed_kernel_driver_worker_stop(
    GXOS_MANAGED_KERNEL_DRIVER_WORKER_CONTEXT *context);

int gxos_managed_kernel_driver_worker_destroy(
    GXOS_MANAGED_KERNEL_DRIVER_WORKER_CONTEXT *context);

int gxos_managed_kernel_driver_worker_is_running(
    const GXOS_MANAGED_KERNEL_DRIVER_WORKER_CONTEXT *context);

#endif

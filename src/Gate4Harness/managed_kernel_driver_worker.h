#ifndef GXOS_MANAGED_KERNEL_DRIVER_WORKER_H
#define GXOS_MANAGED_KERNEL_DRIVER_WORKER_H

#include <stdint.h>

#include "event_api.h"
#include "managed_kernel_interrupt.h"

#define GXOS_MANAGED_KERNEL_DRIVER_WORKER_MAX_BATCHES 4U
#define GXOS_MANAGED_KERNEL_DRIVER_WORKER_STAGE_START 1U
#define GXOS_MANAGED_KERNEL_DRIVER_WORKER_STAGE_DISPATCH 2U

typedef uint32_t (GX_MANAGED_KERNEL_MS_ABI
    *GXOS_MANAGED_KERNEL_DRIVER_WORKER_MANAGED_ENTRY)(uint32_t stage);
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
    GXOS_MANAGED_KERNEL_DRIVER_WORKER_MANAGED_ENTRY managed_entry;
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
} GXOS_MANAGED_KERNEL_DRIVER_WORKER_CONTEXT;

int gxos_managed_kernel_driver_worker_initialize(
    GXOS_MANAGED_KERNEL_DRIVER_WORKER_CONTEXT *context,
    GXOS_SCHEDULER *scheduler, GXOS_EVENT_API_CONTEXT *event_api,
    GXOS_MANAGED_KERNEL_INTERRUPT_CONTEXT *interrupt,
    GXOS_MANAGED_KERNEL_DRIVER_WORKER_MANAGED_ENTRY managed_entry,
    GXOS_MANAGED_KERNEL_DRIVER_WORKER_LOG_TEXT log_text,
    GXOS_MANAGED_KERNEL_DRIVER_WORKER_LOG_HEX log_hex);

int gxos_managed_kernel_driver_worker_configure_nativeaot_tls(
    GXOS_MANAGED_KERNEL_DRIVER_WORKER_CONTEXT *context,
    uint32_t tls_index, const uint8_t *tls_source,
    uint32_t tls_source_size);

int gxos_managed_kernel_driver_worker_pump(
    GXOS_MANAGED_KERNEL_DRIVER_WORKER_CONTEXT *context);

int gxos_managed_kernel_driver_worker_stop(
    GXOS_MANAGED_KERNEL_DRIVER_WORKER_CONTEXT *context);

int gxos_managed_kernel_driver_worker_destroy(
    GXOS_MANAGED_KERNEL_DRIVER_WORKER_CONTEXT *context);

int gxos_managed_kernel_driver_worker_is_running(
    const GXOS_MANAGED_KERNEL_DRIVER_WORKER_CONTEXT *context);

#endif

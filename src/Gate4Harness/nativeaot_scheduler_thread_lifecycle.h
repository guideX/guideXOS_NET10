#ifndef GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE_H
#define GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE_H

#include <stdint.h>
#include "nativeaot_callback_bridge.h"
#include "nativeaot_gc_probe_contract.h"
#include "scheduler_foundation.h"

#if defined(__x86_64__)
#define GXOS_PHASE53O_MS_ABI __attribute__((ms_abi))
#else
#define GXOS_PHASE53O_MS_ABI
#endif

typedef void (GXOS_PHASE53O_MS_ABI *GXOS_NATIVEAOT_FLS_CLEANUP_CALLBACK)(
    void *value);

/* Version-matched RuntimeThreadLocals offsets used by the native lifecycle
   bridge. Managed code never observes or manipulates these fields. */
#define GXOS_NATIVEAOT_THREAD_ALLOC_PTR_OFFSET 0x08U
#define GXOS_NATIVEAOT_THREAD_ALLOC_LIMIT_OFFSET 0x10U
#define GXOS_NATIVEAOT_TLS_STATE_FLAGS_OFFSET 0x40U
#define GXOS_NATIVEAOT_TLS_TRANSITION_FRAME_OFFSET 0x48U
#define GXOS_NATIVEAOT_TLS_THREADSTORE_NEXT_OFFSET 0x60U
#define GXOS_NATIVEAOT_TLS_STACK_LOW_OFFSET 0xA8U
#define GXOS_NATIVEAOT_TLS_STACK_HIGH_OFFSET 0xB0U
#define GXOS_NATIVEAOT_RUNTIME_THREAD_ATTACHED 1U
#define GXOS_NATIVEAOT_RUNTIME_THREAD_DETACHED 2U

typedef struct {
    GXOS_SCHEDULER_TCB *main_thread;
    GXOS_SCHEDULER_TCB *thread;
    uint32_t tls_index;
    uint32_t runtime_fls_slot;
    GXOS_NATIVEAOT_FLS_CLEANUP_CALLBACK runtime_fls_cleanup;
    uint32_t attached;
    uint32_t detached;
    uint64_t main_runtime_thread;
    uint64_t runtime_thread;
    uint64_t runtime_state_before;
    uint64_t runtime_state_after;
    uint64_t runtime_transition_frame;
    uint64_t runtime_stack_low;
    uint64_t runtime_stack_high;
    uint64_t allocation_context;
    uint64_t main_allocation_context;
    uint64_t main_alloc_limit;
    uint64_t main_alloc_ptr;
    uint64_t alloc_limit_before;
    uint64_t alloc_ptr_before;
    uint64_t alloc_limit_after;
    uint64_t alloc_ptr_after;
    uint64_t alloc_limit_after_detach;
    uint64_t alloc_ptr_after_detach;
    uint64_t threadstore_head_after;
    uint32_t threadstore_before;
    uint32_t threadstore_after;
} GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE;

int gxos_nativeaot_scheduler_threadstore_count(uint64_t head,
                                                uint64_t *last_out);

int gxos_nativeaot_scheduler_worker_prepare(
    GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE *lifecycle,
    GXOS_SCHEDULER_TCB *main_thread, GXOS_SCHEDULER_TCB *thread,
    uint32_t tls_index, uint32_t runtime_fls_slot,
    GXOS_NATIVEAOT_FLS_CLEANUP_CALLBACK runtime_fls_cleanup);

int gxos_nativeaot_scheduler_worker_attach(
    GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE *lifecycle,
    GXOS_NATIVEAOT_CALLBACK_BRIDGE *managed_bridge, int32_t input,
    int32_t *result, uint32_t *callback_status_out);

int gxos_nativeaot_scheduler_worker_invoke(
    GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE *lifecycle,
    GXOS_NATIVEAOT_CALLBACK_BRIDGE *managed_bridge, int32_t input,
    int32_t *result, uint32_t *callback_status_out);

int gxos_nativeaot_scheduler_worker_detach(
    GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE *lifecycle);

int gxos_nativeaot_scheduler_worker_no_stale_context_overlap(
    const GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE *lifecycle,
    uint64_t object_address, uint64_t object_size,
    uint64_t *offending_context_out);

typedef void (GXOS_PHASE53O_MS_ABI *GXOS_PHASE53O_LOG_TEXT)(
    const char *text);
typedef void (GXOS_PHASE53O_MS_ABI *GXOS_PHASE53O_LOG_HEX)(
    const char *name, uint64_t value);
typedef void (GXOS_PHASE53O_MS_ABI *GXOS_PHASE53O_SET_PHASE)(
    uint32_t phase);
typedef GXOS_NATIVEAOT_FLS_CLEANUP_CALLBACK
    GXOS_PHASE53O_FLS_CLEANUP_CALLBACK;

typedef struct {
    GXOS_SCHEDULER *scheduler;
    GXOS_SCHEDULER_TCB *main_thread;
    GXOS_NATIVEAOT_CALLBACK_BRIDGE *callback_bridge;
    GXOS_NATIVEAOT_CALLBACK_BRIDGE *gc_bridge;
    uint32_t runtime_fls_slot;
    GXOS_PHASE53O_FLS_CLEANUP_CALLBACK runtime_fls_cleanup;
    uint64_t main_tls_block;
    uint32_t *vm_region_count;
    GXOS_PHASE53O_LOG_TEXT log_text;
    GXOS_PHASE53O_LOG_HEX log_hex;
    GXOS_PHASE53O_SET_PHASE phase_in_managed;
    GXOS_PHASE53O_SET_PHASE phase_after_managed;
    uint32_t tls_index;
} GXOS_PHASE53O_PROBE;

int gxos_nativeaot_scheduler_thread_lifecycle_probe(
    GXOS_PHASE53O_PROBE *probe);

#endif

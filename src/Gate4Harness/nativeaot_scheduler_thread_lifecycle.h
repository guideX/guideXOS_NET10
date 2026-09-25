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
#define GXOS_NATIVEAOT_WORKER_OWNERSHIP_HISTORY_MAX 12U

#ifdef GXOS_ENABLE_PHASE56_FAILURE_INJECTION
typedef enum {
    GXOS_NATIVEAOT_FAILURE_INJECTION_NONE = 0,
    GXOS_NATIVEAOT_FAILURE_INJECTION_AFTER_WORKER_PREPARE = 1,
    GXOS_NATIVEAOT_FAILURE_INJECTION_AFTER_RUNTIME_ATTACH = 2,
    GXOS_NATIVEAOT_FAILURE_INJECTION_AFTER_MANAGED_ROOT = 3,
    GXOS_NATIVEAOT_FAILURE_INJECTION_AFTER_GC_ROOT_SURVIVAL = 4,
    GXOS_NATIVEAOT_FAILURE_INJECTION_AFTER_MANAGED_ROOT_RELEASE = 5
} GXOS_NATIVEAOT_FAILURE_INJECTION_POINT;

typedef enum {
    GXOS_NATIVEAOT_FAILURE_INJECTION_DISARMED = 0,
    GXOS_NATIVEAOT_FAILURE_INJECTION_ARMED = 1,
    GXOS_NATIVEAOT_FAILURE_INJECTION_FIRED = 2
} GXOS_NATIVEAOT_FAILURE_INJECTION_STATE;

typedef struct {
    GXOS_NATIVEAOT_FAILURE_INJECTION_POINT point;
    GXOS_NATIVEAOT_FAILURE_INJECTION_STATE state;
    uint32_t arm_count;
    uint32_t fire_count;
    uint32_t mismatch_count;
    uint32_t scheduler_slot;
    uint32_t worker_identity;
    uint16_t worker_generation;
    uint16_t reserved;
} GXOS_NATIVEAOT_FAILURE_INJECTION_RECORD;
#endif

typedef enum {
    GXOS_NATIVEAOT_WORKER_OWNERSHIP_FREE = 0,
    GXOS_NATIVEAOT_WORKER_OWNERSHIP_ALLOCATED = 1,
    GXOS_NATIVEAOT_WORKER_OWNERSHIP_RUNNABLE = 2,
    GXOS_NATIVEAOT_WORKER_OWNERSHIP_RUNNING = 3,
    GXOS_NATIVEAOT_WORKER_OWNERSHIP_RUNTIME_ATTACHED = 4,
    GXOS_NATIVEAOT_WORKER_OWNERSHIP_DETACH_PENDING = 5,
    GXOS_NATIVEAOT_WORKER_OWNERSHIP_RUNTIME_DETACHED = 6,
    GXOS_NATIVEAOT_WORKER_OWNERSHIP_RECLAIMABLE = 7,
    GXOS_NATIVEAOT_WORKER_OWNERSHIP_RECLAIMED = 8
} GXOS_NATIVEAOT_WORKER_OWNERSHIP_STATE;

typedef struct {
    GXOS_NATIVEAOT_WORKER_OWNERSHIP_STATE ownership_state;
    uint32_t ownership_transition_count;
    uint32_t ownership_transition_failures;
    uint32_t scheduler_slot;
    uint32_t worker_identity;
    uint16_t worker_generation;
    uint16_t ownership_history_count;
    uint8_t ownership_history[GXOS_NATIVEAOT_WORKER_OWNERSHIP_HISTORY_MAX];
    uint8_t scheduler_owned;
    uint8_t stack_owned;
    uint8_t environment_owned;
    uint8_t tls_fls_owned;
    uint8_t runtime_thread_owned;
    /* Existing Phase 57 callback-scope evidence; this is not a GC-root bit. */
    uint8_t managed_worker_object_owned;
    /* Phase 58 actual managed static-root ownership ledger. */
    uint8_t managed_root_owned;
    uint8_t managed_root_survived;
    uint8_t callback_registration_observed;
    uint8_t vm_resources_owned;
    uint8_t reserved_ownership[2];
    uint64_t managed_root_identity;
    uint32_t managed_root_publication_count;
    uint32_t managed_root_release_count;
    uint64_t stack_reservation_base;
    uint64_t stack_guard_base;
    uint64_t stack_usable_low;
    uint64_t stack_usable_high;
    uint64_t saved_rsp;
    uint64_t gs_base;
    uint64_t teb_base;
    uint64_t tls_vector_base;
    uint64_t tls_block_base;
    uint64_t guard_vm_identity;
    uint64_t usable_vm_identity;
    GXOS_SCHEDULER_TCB *main_thread;
    GXOS_SCHEDULER_TCB *thread;
    uint32_t tls_index;
    uint32_t runtime_fls_slot;
    GXOS_NATIVEAOT_FLS_CLEANUP_CALLBACK runtime_fls_cleanup;
    uint32_t attached;
    uint32_t detached;
    uint32_t runtime_attach_count;
    uint32_t runtime_detach_count;
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

int gxos_nativeaot_scheduler_worker_mark_runnable(
    GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE *lifecycle);

int gxos_nativeaot_scheduler_worker_mark_running(
    GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE *lifecycle);

int gxos_nativeaot_scheduler_worker_attach(
    GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE *lifecycle,
    GXOS_NATIVEAOT_CALLBACK_BRIDGE *managed_bridge, int32_t input,
    int32_t *result, uint32_t *callback_status_out);

int gxos_nativeaot_scheduler_worker_invoke(
    GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE *lifecycle,
    GXOS_NATIVEAOT_CALLBACK_BRIDGE *managed_bridge, int32_t input,
    int32_t *result, uint32_t *callback_status_out);

int gxos_nativeaot_scheduler_worker_note_managed_root_published(
    GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE *lifecycle,
    uint64_t root_identity);

int gxos_nativeaot_scheduler_worker_note_managed_root_released(
    GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE *lifecycle,
    uint64_t root_identity);

int gxos_nativeaot_scheduler_worker_detach(
    GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE *lifecycle);

int gxos_nativeaot_scheduler_worker_note_reclaimable(
    GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE *lifecycle);

int gxos_nativeaot_scheduler_worker_note_reclaimed(
    GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE *lifecycle);

/* A prepared worker has no runtime ownership and therefore cannot traverse
   the normal RuntimeDetached -> Reclaimable edge.  This records the result
   of the scheduler's close -> discard -> collect rollback only after the TCB
   has been fully zeroed. */
int gxos_nativeaot_scheduler_worker_note_pre_runtime_reclaimed(
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
    GXOS_NATIVEAOT_CALLBACK_BRIDGE *managed_root_publish_bridge;
    GXOS_NATIVEAOT_CALLBACK_BRIDGE *managed_root_release_bridge;
    GXOS_NATIVEAOT_CALLBACK_BRIDGE *managed_root_validate_bridge;
} GXOS_PHASE53O_PROBE;

int gxos_nativeaot_scheduler_thread_lifecycle_probe(
    GXOS_PHASE53O_PROBE *probe);

int gxos_nativeaot_managed_worker_ownership_probe(
    GXOS_PHASE53O_PROBE *probe);

#ifdef GXOS_ENABLE_PHASE56_FAILURE_INJECTION
int gxos_nativeaot_phase56_failure_arm(
    GXOS_NATIVEAOT_FAILURE_INJECTION_POINT point,
    const GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE *lifecycle);
int gxos_nativeaot_phase56_failure_try_fire(
    const GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE *lifecycle);
const GXOS_NATIVEAOT_FAILURE_INJECTION_RECORD *
gxos_nativeaot_phase56_failure_record(void);
#endif

#ifdef GXOS_ENABLE_PHASE56_PREATTACH_ROLLBACK
int gxos_nativeaot_phase56_preattach_rollback_probe(
    GXOS_PHASE53O_PROBE *probe);
#endif

#ifdef GXOS_ENABLE_PHASE57_POSTATTACH_ROLLBACK
int gxos_nativeaot_phase57_postattach_rollback_probe(
    GXOS_PHASE53O_PROBE *probe);
#endif

#ifdef GXOS_ENABLE_PHASE58_POSTROOT_ROLLBACK
int gxos_nativeaot_phase58_postroot_rollback_probe(
    GXOS_PHASE53O_PROBE *probe);
#endif

#ifdef GXOS_ENABLE_PHASE59_POSTGC_ROLLBACK
int gxos_nativeaot_phase59_postgc_rollback_probe(
    GXOS_PHASE53O_PROBE *probe);
#endif

#ifdef GXOS_ENABLE_PHASE60_POSTROOTRELEASE_ROLLBACK
int gxos_nativeaot_phase60_postrootrelease_rollback_probe(
    GXOS_PHASE53O_PROBE *probe);
#endif

#endif

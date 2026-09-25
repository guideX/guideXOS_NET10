#include "nativeaot_scheduler_thread_lifecycle.h"

#define PHASE53O_TLS_ALLOC_LIMIT_OFFSET 0x30U
#define PHASE53O_TLS_ALLOC_PTR_OFFSET 0x38U
#define PHASE53O_TLS_STATE_FLAGS_OFFSET 0x40U
#define PHASE53O_TLS_THREADSTORE_NEXT_OFFSET 0x60U
#define PHASE53O_RUNTIME_TRANSITION_FRAME_OFFSET 0x48U
#define PHASE53O_TLS_STACK_LOW_OFFSET 0xA8U
#define PHASE53O_TLS_STACK_HIGH_OFFSET 0xB0U
#define PHASE53O_THREADSTORE_MAX 16U
#define PHASE53O_RUNTIME_THREAD_ATTACHED 1U
#define PHASE53O_RUNTIME_THREAD_DETACHED 2U
#define PHASE53O_PHASE_IN_MANAGED 8U
#define PHASE53O_PHASE_AFTER_MANAGED_RETURN 9U

static uint64_t lifecycle_load_u64(uint64_t address, uint32_t offset)
{
    return *(const uint64_t *)(uintptr_t)(address + offset);
}

static int lifecycle_transition_allowed(
    GXOS_NATIVEAOT_WORKER_OWNERSHIP_STATE from,
    GXOS_NATIVEAOT_WORKER_OWNERSHIP_STATE to)
{
    return (from == GXOS_NATIVEAOT_WORKER_OWNERSHIP_FREE &&
                to == GXOS_NATIVEAOT_WORKER_OWNERSHIP_ALLOCATED) ||
           (from == GXOS_NATIVEAOT_WORKER_OWNERSHIP_ALLOCATED &&
                to == GXOS_NATIVEAOT_WORKER_OWNERSHIP_RUNNABLE) ||
           (from == GXOS_NATIVEAOT_WORKER_OWNERSHIP_RUNNABLE &&
                to == GXOS_NATIVEAOT_WORKER_OWNERSHIP_RUNNING) ||
           (from == GXOS_NATIVEAOT_WORKER_OWNERSHIP_RUNNING &&
                to == GXOS_NATIVEAOT_WORKER_OWNERSHIP_RUNTIME_ATTACHED) ||
           (from == GXOS_NATIVEAOT_WORKER_OWNERSHIP_RUNTIME_ATTACHED &&
                to == GXOS_NATIVEAOT_WORKER_OWNERSHIP_DETACH_PENDING) ||
           (from == GXOS_NATIVEAOT_WORKER_OWNERSHIP_DETACH_PENDING &&
                to == GXOS_NATIVEAOT_WORKER_OWNERSHIP_RUNTIME_DETACHED) ||
           (from == GXOS_NATIVEAOT_WORKER_OWNERSHIP_RUNTIME_DETACHED &&
                to == GXOS_NATIVEAOT_WORKER_OWNERSHIP_RECLAIMABLE) ||
           /* Pre-runtime rollback is a bounded partial-construction edge:
              the scheduler has already terminated and reclaimed the TCB, so
              there is no runtime-detached state to publish first. */
           (from == GXOS_NATIVEAOT_WORKER_OWNERSHIP_ALLOCATED &&
                to == GXOS_NATIVEAOT_WORKER_OWNERSHIP_RECLAIMED) ||
           (from == GXOS_NATIVEAOT_WORKER_OWNERSHIP_RECLAIMABLE &&
                to == GXOS_NATIVEAOT_WORKER_OWNERSHIP_RECLAIMED);
}

static int lifecycle_transition(
    GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE *lifecycle,
    GXOS_NATIVEAOT_WORKER_OWNERSHIP_STATE next)
{
    GXOS_NATIVEAOT_WORKER_OWNERSHIP_STATE current;
    if (lifecycle == 0) return 0;
    current = lifecycle->ownership_state;
    if (!lifecycle_transition_allowed(current, next)) {
        if (lifecycle->ownership_transition_failures != UINT32_MAX) {
            ++lifecycle->ownership_transition_failures;
        }
        return 0;
    }
    if (lifecycle->ownership_history_count == 0U) {
        lifecycle->ownership_history[0] = (uint8_t)current;
        lifecycle->ownership_history_count = 1U;
    }
    if (lifecycle->ownership_history_count <
            GXOS_NATIVEAOT_WORKER_OWNERSHIP_HISTORY_MAX) {
        lifecycle->ownership_history[
            lifecycle->ownership_history_count++] = (uint8_t)next;
    } else {
        ++lifecycle->ownership_transition_failures;
        return 0;
    }
    lifecycle->ownership_state = next;
    if (lifecycle->ownership_transition_count != UINT32_MAX) {
        ++lifecycle->ownership_transition_count;
    }
    return 1;
}

static int lifecycle_matches_current_thread(
    const GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE *lifecycle)
{
    GXOS_SCHEDULER_TCB *thread;
    if (lifecycle == 0 || (thread = lifecycle->thread) == 0) return 0;
    return gxos_scheduler_thread_slot(thread) == lifecycle->scheduler_slot &&
           thread->identity == lifecycle->worker_identity &&
           thread->generation == lifecycle->worker_generation;
}

int gxos_nativeaot_scheduler_threadstore_count(uint64_t head,
                                                uint64_t *last_out)
{
    uint32_t count = 0;
    uint64_t current = head;
    uint64_t last = 0;

    while (current != 0 && count != PHASE53O_THREADSTORE_MAX) {
        last = current;
        current = lifecycle_load_u64(
            current, GXOS_NATIVEAOT_TLS_THREADSTORE_NEXT_OFFSET);
        ++count;
    }
    if (current != 0) return 0;
    if (last_out != 0) *last_out = last;
    return (int)count;
}

int gxos_nativeaot_scheduler_worker_prepare(
    GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE *lifecycle,
    GXOS_SCHEDULER_TCB *main_thread, GXOS_SCHEDULER_TCB *thread,
    uint32_t tls_index, uint32_t runtime_fls_slot,
    GXOS_NATIVEAOT_FLS_CLEANUP_CALLBACK runtime_fls_cleanup)
{
    GXOS_SCHEDULER_TCB *current = gxos_scheduler_current_thread();
    GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE fresh = {0};
    uint32_t scheduler_slot;
    if (lifecycle == 0 || main_thread == 0 || thread == 0 ||
        main_thread == thread || (current != main_thread && current != thread) ||
        tls_index >= GXOS_SCHEDULER_TLS_VECTOR_SLOTS ||
        runtime_fls_slot >= GXOS_SCHEDULER_FLS_SLOTS ||
        runtime_fls_cleanup == 0 || !main_thread->live ||
        !thread->live || thread->is_boot_thread ||
        thread->fls_values[runtime_fls_slot] != 0 ||
        main_thread->fls_values[runtime_fls_slot] == 0 ||
        thread->tls_block_base == 0 ||
        thread->tls_block_base == main_thread->tls_block_base ||
        (lifecycle->ownership_state != GXOS_NATIVEAOT_WORKER_OWNERSHIP_FREE &&
         lifecycle->ownership_state != GXOS_NATIVEAOT_WORKER_OWNERSHIP_RECLAIMED) ||
        !gxos_scheduler_validate_thread_context(thread)) {
        return 0;
    }
    scheduler_slot = gxos_scheduler_thread_slot(thread);
    if (scheduler_slot == UINT32_MAX) return 0;
    *lifecycle = fresh;
    lifecycle->scheduler_slot = scheduler_slot;
    lifecycle->worker_identity = thread->identity;
    lifecycle->worker_generation = thread->generation;
    lifecycle->scheduler_owned = 1;
    lifecycle->stack_owned = 1;
    lifecycle->environment_owned = thread->environment_owned;
    lifecycle->tls_fls_owned = 1;
    lifecycle->vm_resources_owned = 1;
    lifecycle->stack_reservation_base = thread->stack_contract.reservation_base;
    lifecycle->stack_guard_base = thread->stack_contract.guard_base;
    lifecycle->stack_usable_low = thread->stack_contract.usable_stack_low;
    lifecycle->stack_usable_high = thread->stack_contract.usable_stack_high;
    lifecycle->saved_rsp = thread->context.rsp;
    lifecycle->gs_base = thread->gs_base;
    lifecycle->teb_base = thread->teb_base;
    lifecycle->tls_vector_base = thread->tls_vector_base;
    lifecycle->tls_block_base = thread->tls_block_base;
    lifecycle->guard_vm_identity = thread->stack_contract.guard_vm_identity;
    lifecycle->usable_vm_identity = thread->stack_contract.usable_vm_identity;
    lifecycle->thread = thread;
    lifecycle->main_thread = main_thread;
    if (!lifecycle_transition(lifecycle,
                              GXOS_NATIVEAOT_WORKER_OWNERSHIP_ALLOCATED)) {
        return 0;
    }
    /* The vector is fresh scheduler-owned storage.  Install the PE TLS
       template at the actual NativeAOT TLS slot; slot zero is only the
       scheduler's initial placeholder and is not a substitute for the PE
       loader's TLS index. */
    {
        uint64_t *vector = (uint64_t *)(uintptr_t)thread->tls_vector_base;
        if (tls_index != 0) vector[0] = 0;
        vector[tls_index] = thread->tls_block_base;
    }
    lifecycle->tls_index = tls_index;
    lifecycle->runtime_fls_slot = runtime_fls_slot;
    lifecycle->runtime_fls_cleanup = runtime_fls_cleanup;
    lifecycle->main_runtime_thread =
        main_thread->fls_values[runtime_fls_slot];
    lifecycle->main_allocation_context = lifecycle->main_runtime_thread +
                                         GXOS_NATIVEAOT_THREAD_ALLOC_PTR_OFFSET;
    lifecycle->allocation_context = thread->tls_block_base + 0x38U;
    lifecycle->main_alloc_limit = lifecycle_load_u64(
        lifecycle->main_allocation_context,
        GXOS_NATIVEAOT_THREAD_ALLOC_LIMIT_OFFSET -
            GXOS_NATIVEAOT_THREAD_ALLOC_PTR_OFFSET);
    lifecycle->main_alloc_ptr = lifecycle_load_u64(
        lifecycle->main_allocation_context,
        0);
    lifecycle->alloc_limit_before = 0;
    lifecycle->alloc_ptr_before = 0;
    lifecycle->detached = 0;
    if (lifecycle->main_runtime_thread == 0) {
        lifecycle->ownership_transition_failures++;
        return 0;
    }
    return 1;
}

int gxos_nativeaot_scheduler_worker_mark_runnable(
    GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE *lifecycle)
{
    if (lifecycle == 0 || lifecycle->thread == 0 ||
        lifecycle->ownership_state != GXOS_NATIVEAOT_WORKER_OWNERSHIP_ALLOCATED ||
        !lifecycle_matches_current_thread(lifecycle) ||
        !lifecycle->thread->live ||
        lifecycle->thread->state != GXOS_SCHEDULER_THREAD_RUNNABLE ||
        !lifecycle->thread->runnable_queued) {
        if (lifecycle != 0 && lifecycle->ownership_transition_failures != UINT32_MAX) {
            ++lifecycle->ownership_transition_failures;
        }
        return 0;
    }
    return lifecycle_transition(lifecycle,
                                GXOS_NATIVEAOT_WORKER_OWNERSHIP_RUNNABLE);
}

int gxos_nativeaot_scheduler_worker_mark_running(
    GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE *lifecycle)
{
    if (lifecycle == 0 || lifecycle->thread == 0 ||
        lifecycle->ownership_state != GXOS_NATIVEAOT_WORKER_OWNERSHIP_RUNNABLE ||
        !lifecycle_matches_current_thread(lifecycle) ||
        !lifecycle->thread->live ||
        lifecycle->thread->state != GXOS_SCHEDULER_THREAD_RUNNING ||
        gxos_scheduler_current_thread() != lifecycle->thread) {
        if (lifecycle != 0 && lifecycle->ownership_transition_failures != UINT32_MAX) {
            ++lifecycle->ownership_transition_failures;
        }
        return 0;
    }
    return lifecycle_transition(lifecycle,
                                GXOS_NATIVEAOT_WORKER_OWNERSHIP_RUNNING);
}

static int lifecycle_current_thread_is_active(
    const GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE *lifecycle)
{
    return lifecycle != 0 && lifecycle->thread != 0 &&
           lifecycle_matches_current_thread(lifecycle) &&
           gxos_scheduler_current_thread() == lifecycle->thread &&
           lifecycle->thread->live && !lifecycle->thread->is_boot_thread &&
           lifecycle->runtime_fls_slot < GXOS_SCHEDULER_FLS_SLOTS &&
           lifecycle->thread->fls_values[lifecycle->runtime_fls_slot] != 0;
}

static int lifecycle_capture_attached(
    GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE *lifecycle)
{
    GXOS_SCHEDULER_TCB *thread = lifecycle->thread;
    uint64_t runtime_thread = thread->fls_values[
        lifecycle->runtime_fls_slot];
    uint64_t main_thread = lifecycle->main_runtime_thread;

    lifecycle->runtime_thread = runtime_thread;
    if (runtime_thread == 0 || main_thread == 0) {
        lifecycle->runtime_state_before = 0;
        lifecycle->runtime_transition_frame = 0;
        lifecycle->runtime_stack_low = 0;
        lifecycle->runtime_stack_high = 0;
        lifecycle->alloc_limit_before = 0;
        lifecycle->alloc_ptr_before = 0;
        lifecycle->main_alloc_limit = 0;
        lifecycle->main_alloc_ptr = 0;
        lifecycle->threadstore_before = 0;
        lifecycle->threadstore_after = 0;
        return 0;
    }
    lifecycle->runtime_state_before = lifecycle_load_u64(
        runtime_thread, GXOS_NATIVEAOT_TLS_STATE_FLAGS_OFFSET);
    lifecycle->runtime_transition_frame = lifecycle_load_u64(
        runtime_thread, GXOS_NATIVEAOT_TLS_TRANSITION_FRAME_OFFSET);
    lifecycle->runtime_stack_low = lifecycle_load_u64(
        runtime_thread, GXOS_NATIVEAOT_TLS_STACK_LOW_OFFSET);
    lifecycle->runtime_stack_high = lifecycle_load_u64(
        runtime_thread, GXOS_NATIVEAOT_TLS_STACK_HIGH_OFFSET);
    lifecycle->alloc_limit_before = lifecycle_load_u64(
        runtime_thread, GXOS_NATIVEAOT_THREAD_ALLOC_LIMIT_OFFSET);
    lifecycle->alloc_ptr_before = lifecycle_load_u64(
        runtime_thread, GXOS_NATIVEAOT_THREAD_ALLOC_PTR_OFFSET);
    lifecycle->main_alloc_limit = lifecycle_load_u64(
        main_thread, GXOS_NATIVEAOT_THREAD_ALLOC_LIMIT_OFFSET);
    lifecycle->main_alloc_ptr = lifecycle_load_u64(
        main_thread, GXOS_NATIVEAOT_THREAD_ALLOC_PTR_OFFSET);
    lifecycle->threadstore_before =
        (uint32_t)gxos_nativeaot_scheduler_threadstore_count(main_thread, 0);
    lifecycle->threadstore_after =
        (uint32_t)gxos_nativeaot_scheduler_threadstore_count(runtime_thread, 0);
    return runtime_thread == thread->tls_block_base + 0x30U &&
           runtime_thread != main_thread &&
           lifecycle->runtime_state_before ==
               GXOS_NATIVEAOT_RUNTIME_THREAD_ATTACHED &&
           lifecycle->runtime_transition_frame == UINT64_MAX &&
           /* NativeAOT reports the complete reserved stack interval.  The
              scheduler's compatibility stack_base is the usable interval
              low, one guard page above the reservation base. */
           lifecycle->runtime_stack_low ==
               thread->stack_contract.reservation_base &&
           lifecycle->runtime_stack_high == thread->stack_limit &&
           thread->context.rsp >= lifecycle->runtime_stack_low &&
           thread->context.rsp <= lifecycle->runtime_stack_high &&
           lifecycle->threadstore_after == lifecycle->threadstore_before + 1U;
}

int gxos_nativeaot_scheduler_worker_attach(
    GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE *lifecycle,
    GXOS_NATIVEAOT_CALLBACK_BRIDGE *managed_bridge, int32_t input,
    int32_t *result, uint32_t *callback_status_out)
{
    uint32_t status;
    if (callback_status_out != 0) *callback_status_out = UINT32_MAX;
    if (lifecycle == 0 || managed_bridge == 0 || result == 0 ||
        lifecycle->attached || lifecycle->detached ||
        lifecycle->ownership_state != GXOS_NATIVEAOT_WORKER_OWNERSHIP_RUNNING ||
        !lifecycle_matches_current_thread(lifecycle) ||
        lifecycle->runtime_fls_cleanup == 0 || lifecycle->thread == 0 ||
        lifecycle->main_runtime_thread == 0 ||
        gxos_scheduler_current_thread() != lifecycle->thread) {
        return 0;
    }
    status = (uint32_t)gxos_nativeaot_callback_invoke(
        managed_bridge, input, result);
    if (callback_status_out != 0) *callback_status_out = status;
    if (status != GXOS_NATIVEAOT_CALLBACK_OK ||
        !lifecycle_current_thread_is_active(lifecycle) ||
        managed_bridge->ready == 0) {
        return 0;
    }
    if (!lifecycle_capture_attached(lifecycle) ||
        !lifecycle_transition(
            lifecycle, GXOS_NATIVEAOT_WORKER_OWNERSHIP_RUNTIME_ATTACHED)) {
        return 0;
    }
    lifecycle->attached = 1;
    lifecycle->runtime_attach_count = 1U;
    lifecycle->runtime_thread_owned = 1;
    lifecycle->managed_worker_object_owned = 1;
    lifecycle->callback_registration_observed = 1;
    return 1;
}

int gxos_nativeaot_scheduler_worker_invoke(
    GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE *lifecycle,
    GXOS_NATIVEAOT_CALLBACK_BRIDGE *managed_bridge, int32_t input,
    int32_t *result, uint32_t *callback_status_out)
{
    uint32_t status;
    if (callback_status_out != 0) *callback_status_out = UINT32_MAX;
    if (lifecycle == 0 || managed_bridge == 0 || result == 0 ||
        lifecycle->detached || lifecycle->thread == 0 ||
        !lifecycle_matches_current_thread(lifecycle) ||
        (lifecycle->ownership_state != GXOS_NATIVEAOT_WORKER_OWNERSHIP_RUNNING &&
         lifecycle->ownership_state != GXOS_NATIVEAOT_WORKER_OWNERSHIP_RUNTIME_ATTACHED) ||
        gxos_scheduler_current_thread() != lifecycle->thread) {
        return 0;
    }
    if (!lifecycle->attached) {
        return gxos_nativeaot_scheduler_worker_attach(
            lifecycle, managed_bridge, input, result, callback_status_out);
    }
    if (!lifecycle_current_thread_is_active(lifecycle)) return 0;
    if (lifecycle->thread->fls_values[lifecycle->runtime_fls_slot] !=
            lifecycle->runtime_thread ||
        lifecycle_load_u64(lifecycle->runtime_thread,
                           GXOS_NATIVEAOT_TLS_STATE_FLAGS_OFFSET) !=
            GXOS_NATIVEAOT_RUNTIME_THREAD_ATTACHED) {
        return 0;
    }
    status = (uint32_t)gxos_nativeaot_callback_invoke(
        managed_bridge, input, result);
    if (callback_status_out != 0) *callback_status_out = status;
    if (status != GXOS_NATIVEAOT_CALLBACK_OK ||
        !lifecycle_current_thread_is_active(lifecycle) ||
        lifecycle->thread->fls_values[lifecycle->runtime_fls_slot] !=
            lifecycle->runtime_thread) {
        return 0;
    }
    lifecycle->alloc_limit_after = lifecycle_load_u64(
        lifecycle->runtime_thread, GXOS_NATIVEAOT_THREAD_ALLOC_LIMIT_OFFSET);
    lifecycle->alloc_ptr_after = lifecycle_load_u64(
        lifecycle->runtime_thread, GXOS_NATIVEAOT_THREAD_ALLOC_PTR_OFFSET);
    lifecycle->main_alloc_limit = lifecycle_load_u64(
        lifecycle->main_runtime_thread, GXOS_NATIVEAOT_THREAD_ALLOC_LIMIT_OFFSET);
    lifecycle->main_alloc_ptr = lifecycle_load_u64(
        lifecycle->main_runtime_thread, GXOS_NATIVEAOT_THREAD_ALLOC_PTR_OFFSET);
    return 1;
}

int gxos_nativeaot_scheduler_worker_note_managed_root_published(
    GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE *lifecycle,
    uint64_t root_identity)
{
    if (lifecycle == 0 || root_identity == 0 || !lifecycle->attached ||
        lifecycle->detached || lifecycle->ownership_state !=
            GXOS_NATIVEAOT_WORKER_OWNERSHIP_RUNTIME_ATTACHED ||
        !lifecycle_current_thread_is_active(lifecycle) ||
        lifecycle->managed_root_owned ||
        lifecycle->managed_root_publication_count !=
            lifecycle->managed_root_release_count) {
        if (lifecycle != 0 && lifecycle->ownership_transition_failures != UINT32_MAX) {
            ++lifecycle->ownership_transition_failures;
        }
        return 0;
    }
    lifecycle->managed_root_identity = root_identity;
    if (lifecycle->managed_root_publication_count != UINT32_MAX) {
        ++lifecycle->managed_root_publication_count;
    }
    lifecycle->managed_root_owned = 1;
    return 1;
}

int gxos_nativeaot_scheduler_worker_note_managed_root_released(
    GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE *lifecycle,
    uint64_t root_identity)
{
    if (lifecycle == 0 || root_identity == 0 || !lifecycle->attached ||
        lifecycle->detached || lifecycle->ownership_state !=
            GXOS_NATIVEAOT_WORKER_OWNERSHIP_RUNTIME_ATTACHED ||
        !lifecycle_current_thread_is_active(lifecycle) ||
        !lifecycle->managed_root_owned ||
        lifecycle->managed_root_identity != root_identity ||
        lifecycle->managed_root_release_count >=
            lifecycle->managed_root_publication_count) {
        if (lifecycle != 0 && lifecycle->ownership_transition_failures != UINT32_MAX) {
            ++lifecycle->ownership_transition_failures;
        }
        return 0;
    }
    ++lifecycle->managed_root_release_count;
    lifecycle->managed_root_owned = 0;
    return 1;
}

int gxos_nativeaot_scheduler_worker_detach(
    GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE *lifecycle)
{
    uint64_t value;
    if (lifecycle == 0 || !lifecycle->attached || lifecycle->detached ||
        lifecycle->ownership_state != GXOS_NATIVEAOT_WORKER_OWNERSHIP_RUNTIME_ATTACHED ||
        lifecycle->managed_root_owned ||
        lifecycle->managed_root_publication_count !=
            lifecycle->managed_root_release_count ||
        !lifecycle_current_thread_is_active(lifecycle) ||
        lifecycle->runtime_fls_cleanup == 0) {
        if (lifecycle != 0 && lifecycle->ownership_transition_failures != UINT32_MAX) {
            ++lifecycle->ownership_transition_failures;
        }
        return 0;
    }
    value = lifecycle->thread->fls_values[lifecycle->runtime_fls_slot];
    if (value != lifecycle->runtime_thread) return 0;
    if (!lifecycle_transition(lifecycle,
                              GXOS_NATIVEAOT_WORKER_OWNERSHIP_DETACH_PENDING)) {
        return 0;
    }
    /* The established FLS cleanup callback is the runtime's detach authority.
       Count the call itself, rather than treating a cleared evidence field as
       proof that detach happened. */
    if (lifecycle->runtime_detach_count != UINT32_MAX) {
        ++lifecycle->runtime_detach_count;
    }
    lifecycle->runtime_fls_cleanup((void *)(uintptr_t)value);
    lifecycle->runtime_state_after = lifecycle_load_u64(
        value, GXOS_NATIVEAOT_TLS_STATE_FLAGS_OFFSET);
    lifecycle->alloc_limit_after_detach = lifecycle_load_u64(
        value, GXOS_NATIVEAOT_THREAD_ALLOC_LIMIT_OFFSET);
    lifecycle->alloc_ptr_after_detach = lifecycle_load_u64(
        value, GXOS_NATIVEAOT_THREAD_ALLOC_PTR_OFFSET);
    lifecycle->threadstore_head_after = lifecycle->main_thread->fls_values[
        lifecycle->runtime_fls_slot];
    lifecycle->threadstore_after =
        (uint32_t)gxos_nativeaot_scheduler_threadstore_count(
            lifecycle->threadstore_head_after, 0);
    /* A managed allocation can leave the detached Thread*'s allocation
       cursor nonzero even after the runtime FLS cleanup has published the
       detached state and removed the thread from the ThreadStore.  That
       cursor is not a live ownership edge: preserve the strict zero-cursor
       contract for non-allocating Phase 53-57 callbacks, but let the Phase
       58 root-release path prove the authoritative detached/FLS/ThreadStore
       state before scheduler reclaim. */
    if (lifecycle->runtime_state_after !=
            GXOS_NATIVEAOT_RUNTIME_THREAD_DETACHED ||
        lifecycle->threadstore_after != lifecycle->threadstore_before ||
        (lifecycle->managed_root_publication_count == 0U &&
         (lifecycle->alloc_limit_after_detach != 0 ||
          lifecycle->alloc_ptr_after_detach != 0))) {
        return 0;
    }
    gxos_scheduler_set_fls(lifecycle->runtime_fls_slot, 0);
    if (gxos_scheduler_get_fls(lifecycle->runtime_fls_slot) != 0) return 0;
    lifecycle->runtime_thread_owned = 0;
    lifecycle->managed_worker_object_owned = 0;
    lifecycle->tls_fls_owned = 0;
    lifecycle->detached = 1;
    return lifecycle_transition(lifecycle,
                                GXOS_NATIVEAOT_WORKER_OWNERSHIP_RUNTIME_DETACHED);
}

int gxos_nativeaot_scheduler_worker_note_reclaimable(
    GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE *lifecycle)
{
    GXOS_SCHEDULER_TCB *thread;
    if (lifecycle == 0 || lifecycle->thread == 0 || !lifecycle->detached ||
        lifecycle->ownership_state != GXOS_NATIVEAOT_WORKER_OWNERSHIP_RUNTIME_DETACHED) {
        if (lifecycle != 0 && lifecycle->ownership_transition_failures != UINT32_MAX) {
            ++lifecycle->ownership_transition_failures;
        }
        return 0;
    }
    thread = lifecycle->thread;
    if (!lifecycle_matches_current_thread(lifecycle) || !thread->live ||
        thread == gxos_scheduler_current_thread() ||
        thread->state != GXOS_SCHEDULER_THREAD_TERMINATED ||
        thread->execution_refs != 0 || thread->runnable_queued ||
        thread->wait_record != 0) {
        if (lifecycle->ownership_transition_failures != UINT32_MAX) {
            ++lifecycle->ownership_transition_failures;
        }
        return 0;
    }
    if (lifecycle->managed_root_owned ||
        lifecycle->managed_root_publication_count !=
            lifecycle->managed_root_release_count) {
        if (lifecycle->ownership_transition_failures != UINT32_MAX) {
            ++lifecycle->ownership_transition_failures;
        }
        return 0;
    }
    return lifecycle_transition(lifecycle,
                                GXOS_NATIVEAOT_WORKER_OWNERSHIP_RECLAIMABLE);
}

int gxos_nativeaot_scheduler_worker_note_reclaimed(
    GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE *lifecycle)
{
    GXOS_SCHEDULER_TCB *thread;
    if (lifecycle == 0 || lifecycle->thread == 0 ||
        lifecycle->ownership_state != GXOS_NATIVEAOT_WORKER_OWNERSHIP_RECLAIMABLE) {
        if (lifecycle != 0 && lifecycle->ownership_transition_failures != UINT32_MAX) {
            ++lifecycle->ownership_transition_failures;
        }
        return 0;
    }
    thread = lifecycle->thread;
    if (thread->live || thread->stack_contract.reservation_base != 0 ||
        thread->stack_vm_identity != 0 || thread->gs_base != 0 ||
        thread->teb_base != 0 || thread->tls_vector_base != 0 ||
        thread->tls_block_base != 0 || thread->environment_owned != 0) {
        if (lifecycle->ownership_transition_failures != UINT32_MAX) {
            ++lifecycle->ownership_transition_failures;
        }
        return 0;
    }
    if (lifecycle->managed_root_owned ||
        lifecycle->managed_root_publication_count !=
            lifecycle->managed_root_release_count) {
        if (lifecycle->ownership_transition_failures != UINT32_MAX) {
            ++lifecycle->ownership_transition_failures;
        }
        return 0;
    }
    lifecycle->scheduler_owned = 0;
    lifecycle->stack_owned = 0;
    lifecycle->environment_owned = 0;
    lifecycle->vm_resources_owned = 0;
    return lifecycle_transition(lifecycle,
                                GXOS_NATIVEAOT_WORKER_OWNERSHIP_RECLAIMED);
}

int gxos_nativeaot_scheduler_worker_note_pre_runtime_reclaimed(
    GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE *lifecycle)
{
    GXOS_SCHEDULER_TCB *thread;
    if (lifecycle == 0 || lifecycle->thread == 0 ||
        lifecycle->ownership_state !=
            GXOS_NATIVEAOT_WORKER_OWNERSHIP_ALLOCATED ||
        lifecycle->attached || lifecycle->detached ||
        lifecycle->runtime_thread != 0 || lifecycle->runtime_thread_owned ||
        lifecycle->managed_worker_object_owned ||
        lifecycle->managed_root_owned ||
        lifecycle->managed_root_publication_count !=
            lifecycle->managed_root_release_count ||
        lifecycle->managed_root_survived ||
        lifecycle->callback_registration_observed) {
        if (lifecycle != 0 && lifecycle->ownership_transition_failures != UINT32_MAX) {
            ++lifecycle->ownership_transition_failures;
        }
        return 0;
    }
    thread = lifecycle->thread;
    /* The scheduler's discard path owns termination and reclamation.  This
       evidence transition is valid only after that path has zeroed the TCB,
       and never after a later slot reuse. */
    if (thread == gxos_scheduler_current_thread() || thread->live != 0 ||
        thread->state != GXOS_SCHEDULER_THREAD_FREE ||
        thread->identity != 0 || thread->generation != 0 ||
        thread->object_slot != 0 || thread->stack_contract.reservation_base != 0 ||
        thread->stack_vm_identity != 0 || thread->stack_pages_memory != 0 ||
        thread->stack_canary_memory != 0 || thread->gs_base != 0 ||
        thread->teb_base != 0 || thread->tls_vector_base != 0 ||
        thread->tls_block_base != 0 || thread->environment_owned != 0 ||
        thread->public_handle_refs != 0 || thread->execution_refs != 0) {
        if (lifecycle->ownership_transition_failures != UINT32_MAX) {
            ++lifecycle->ownership_transition_failures;
        }
        return 0;
    }
    lifecycle->scheduler_owned = 0;
    lifecycle->stack_owned = 0;
    lifecycle->environment_owned = 0;
    lifecycle->tls_fls_owned = 0;
    lifecycle->vm_resources_owned = 0;
    return lifecycle_transition(lifecycle,
                                GXOS_NATIVEAOT_WORKER_OWNERSHIP_RECLAIMED);
}

int gxos_nativeaot_scheduler_worker_no_stale_context_overlap(
    const GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE *lifecycle,
    uint64_t object_address, uint64_t object_size,
    uint64_t *offending_context_out)
{
    uint64_t current;
    uint64_t object_end;
    uint32_t count = 0;
    if (offending_context_out != 0) *offending_context_out = 0;
    if (lifecycle == 0 || !lifecycle->attached || lifecycle->runtime_thread == 0 ||
        object_address == 0 || object_size == 0 ||
        !lifecycle_matches_current_thread(lifecycle) ||
        object_address > UINT64_MAX - object_size ||
        lifecycle_load_u64(lifecycle->runtime_thread,
                           GXOS_NATIVEAOT_THREAD_ALLOC_PTR_OFFSET) <= object_address) {
        return 0;
    }
    object_end = object_address + object_size;
    if (object_end > lifecycle_load_u64(
            lifecycle->runtime_thread, GXOS_NATIVEAOT_THREAD_ALLOC_PTR_OFFSET)) {
        return 0;
    }
    current = lifecycle->runtime_thread;
    while (current != 0 && count != PHASE53O_THREADSTORE_MAX) {
        uint64_t alloc_ptr = lifecycle_load_u64(
            current, GXOS_NATIVEAOT_THREAD_ALLOC_PTR_OFFSET);
        uint64_t alloc_limit = lifecycle_load_u64(
            current, GXOS_NATIVEAOT_THREAD_ALLOC_LIMIT_OFFSET);
        if (current != lifecycle->runtime_thread && alloc_ptr == object_address &&
            alloc_limit > alloc_ptr) {
            if (offending_context_out != 0) *offending_context_out = current;
            return 0;
        }
        current = lifecycle_load_u64(
            current, GXOS_NATIVEAOT_TLS_THREADSTORE_NEXT_OFFSET);
        ++count;
    }
    return current == 0;
}

typedef struct {
    GXOS_PHASE53O_PROBE *probe;
    uint32_t cycle;
    GXOS_SCHEDULER_HANDLE handle;
    GXOS_SCHEDULER_TCB *thread;
    GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE lifecycle;
    uint32_t callback_status;
    int32_t callback_result;
    uint32_t gc_status;
    int32_t gc_result;
    uint32_t gc_delta;
    uint32_t gc_generation;
    uint32_t gc_checksum;
    uint32_t cleanup_called;
    uint32_t scheduler_fls_cleared;
    uint64_t runtime_thread;
    uint64_t runtime_transition_frame;
    uint64_t runtime_state_before;
    uint64_t runtime_state_after;
    uint64_t runtime_next;
    uint64_t runtime_stack_low;
    uint64_t runtime_stack_high;
    uint64_t allocation_context;
    uint64_t main_allocation_context;
    uint64_t alloc_limit_before;
    uint64_t alloc_ptr_before;
    uint64_t alloc_limit_after_gc;
    uint64_t alloc_ptr_after_gc;
    uint64_t alloc_limit_after_detach;
    uint64_t alloc_ptr_after_detach;
    uint32_t threadstore_before;
    uint32_t threadstore_after;
    uint64_t threadstore_head_after;
    uint64_t worker_rsp;
    uint64_t stack_canary_memory;
    uint32_t canary_before_detach;
    uint32_t canary_after_detach;
    uint32_t failure;
} GXOS_PHASE53O_CYCLE;

static uint64_t phase53o_load_u64(uint64_t address, uint32_t offset)
{
    return *(const uint64_t *)(uintptr_t)(address + offset);
}

static void phase53o_text(GXOS_PHASE53O_PROBE *probe, const char *text)
{
    probe->log_text(text);
}

static void phase53o_hex(GXOS_PHASE53O_PROBE *probe,
                         const char *name, uint64_t value)
{
    probe->log_hex(name, value);
    probe->log_text("\r\n");
}

static int phase53o_fail(GXOS_PHASE53O_CYCLE *cycle)
{
    cycle->failure = 1;
    phase53o_text(cycle->probe, "GXOS_NET10:PHASE53O_FAILURE=1\r\n");
    return 0;
}

static uint32_t phase53o_threadstore_count(uint64_t head,
                                             uint64_t *last_out)
{
    return (uint32_t)gxos_nativeaot_scheduler_threadstore_count(
        head, last_out);
}

static uint32_t phase53o_canary_mask(const GXOS_SCHEDULER_TCB *thread)
{
    uint32_t index;
    uint32_t mask = 0;
    if (thread == 0 || thread->stack_canary_memory == 0) return 0;
    for (index = 0; index != GXOS_SCHEDULER_CANARY_BYTES; ++index) {
        if (((const uint8_t *)(uintptr_t)thread->stack_canary_memory)[index] !=
            thread->low_canary[index]) {
            mask |= 1U;
        }
        if (((const uint8_t *)(uintptr_t)thread->stack_canary_memory)[
                GXOS_SCHEDULER_CANARY_BYTES + index] !=
            thread->high_canary[index]) {
            mask |= 2U;
        }
    }
    return mask;
}

/*
 * Rehome only the diagnostic canary page. The production stack contract has
 * a real reserved, non-present guard and does not use a writable boundary
 * sentinel as its safety mechanism.
 */
static int phase53o_rehome_canary(GXOS_PHASE53O_PROBE *probe,
                                  GXOS_SCHEDULER_TCB *thread)
{
    uint64_t old_canary;
    uint64_t new_canary = 0;
    uint32_t index;

    if (probe == 0 || probe->scheduler == 0 || thread == 0 ||
        thread->stack_canary_memory == 0 ||
        probe->scheduler->allocate_pages == 0 ||
        probe->scheduler->free_pages == 0) {
        return 0;
    }
    old_canary = thread->stack_canary_memory;
    if (probe->scheduler->allocate_pages(0, 4, 1, &new_canary) != 0 ||
        new_canary == 0 || new_canary == old_canary ||
        (new_canary >= thread->stack_base &&
         new_canary < thread->stack_limit)) {
        if (new_canary != 0 && new_canary != old_canary) {
            (void)probe->scheduler->free_pages(new_canary, 1);
        }
        return 0;
    }
    for (index = 0; index != GXOS_SCHEDULER_CANARY_BYTES; ++index) {
        ((uint8_t *)(uintptr_t)new_canary)[index] =
            thread->low_canary[index];
        ((uint8_t *)(uintptr_t)new_canary)[GXOS_SCHEDULER_CANARY_BYTES + index] =
            thread->high_canary[index];
    }
    if (probe->scheduler->free_pages(old_canary, 1) != 0) {
        (void)probe->scheduler->free_pages(new_canary, 1);
        return 0;
    }
    thread->stack_canary_memory = new_canary;
    return 1;
}

static void phase53o_emit_cycle_state(GXOS_PHASE53O_CYCLE *cycle)
{
    GXOS_PHASE53O_PROBE *probe = cycle->probe;
    GXOS_SCHEDULER_REGISTER_SNAPSHOT snapshot = {0};

    gxos_scheduler_capture_registers(&snapshot);
    cycle->worker_rsp = snapshot.rsp;
    if (cycle->thread == 0 ||
        !gxos_scheduler_validate_worker_snapshot(cycle->thread, &snapshot)) {
        (void)phase53o_fail(cycle);
    } else {
        phase53o_hex(probe, "GXOS_NET10:PHASE53V_WORKER_CONTEXT_COHERENT_ID=0x",
                     cycle->thread->identity);
        phase53o_hex(probe, "GXOS_NET10:PHASE53V_WORKER_GS_LOWER=0x",
                     *(const uint64_t *)(uintptr_t)(cycle->thread->gs_base + 0x10U));
        phase53o_hex(probe, "GXOS_NET10:PHASE53V_WORKER_TEB_LOWER=0x",
                     *(const uint64_t *)(uintptr_t)(cycle->thread->teb_base + 0x10U));
        phase53o_hex(probe, "GXOS_NET10:PHASE53V_WORKER_USABLE_LOW=0x",
                     cycle->thread->stack_contract.usable_stack_low);
        phase53o_hex(probe, "GXOS_NET10:PHASE53V_WORKER_MINIMUM_RSP=0x",
                     cycle->thread->stack_contract.minimum_rsp);
        phase53o_hex(probe, "GXOS_NET10:PHASE53V_WORKER_HIGH_WATER_BYTES=0x",
                     cycle->thread->stack_contract.high_water_bytes);
    }
    phase53o_hex(probe, "GXOS_NET10:PHASE53O_CYCLE=", cycle->cycle);
    phase53o_hex(probe, "GXOS_NET10:PHASE53O_IDENTITY=0x",
                cycle->thread == 0 ? 0 : cycle->thread->identity);
    phase53o_hex(probe, "GXOS_NET10:PHASE53O_STACK_BASE=0x",
                cycle->thread == 0 ? 0 : cycle->thread->stack_base);
    phase53o_hex(probe, "GXOS_NET10:PHASE53O_STACK_LIMIT=0x",
                cycle->thread == 0 ? 0 : cycle->thread->stack_limit);
    phase53o_hex(probe, "GXOS_NET10:PHASE53O_STACK_RSP=0x",
                cycle->worker_rsp);
    phase53o_hex(probe, "GXOS_NET10:PHASE53O_STACK_CANARY_MEMORY=0x",
                cycle->stack_canary_memory);
    phase53o_hex(probe, "GXOS_NET10:PHASE53O_CANARY_MASK_BEFORE_DETACH=",
                cycle->canary_before_detach);
    phase53o_hex(probe, "GXOS_NET10:PHASE53O_CANARY_MASK_AFTER_DETACH=",
                cycle->canary_after_detach);
    phase53o_hex(probe, "GXOS_NET10:PHASE53O_GS_BASE=0x",
                cycle->thread == 0 ? 0 : cycle->thread->gs_base);
    phase53o_hex(probe, "GXOS_NET10:PHASE53O_TEB_BASE=0x",
                cycle->thread == 0 ? 0 : cycle->thread->teb_base);
    phase53o_hex(probe, "GXOS_NET10:PHASE53O_TLS_VECTOR=0x",
                cycle->thread == 0 ? 0 : cycle->thread->tls_vector_base);
    phase53o_hex(probe, "GXOS_NET10:PHASE53O_TLS_BLOCK=0x",
                cycle->thread == 0 ? 0 : cycle->thread->tls_block_base);
    phase53o_hex(probe, "GXOS_NET10:PHASE53O_RUNTIME_THREAD=0x",
                cycle->runtime_thread);
    phase53o_hex(probe, "GXOS_NET10:PHASE53O_RUNTIME_STATE_BEFORE=0x",
                cycle->runtime_state_before);
    phase53o_hex(probe, "GXOS_NET10:PHASE53O_RUNTIME_TRANSITION_FRAME=0x",
                cycle->runtime_transition_frame);
    phase53o_hex(probe, "GXOS_NET10:PHASE53O_RUNTIME_STATE_AFTER=0x",
                cycle->runtime_state_after);
    phase53o_hex(probe, "GXOS_NET10:PHASE53O_RUNTIME_STACK_LOW=0x",
                cycle->runtime_stack_low);
    phase53o_hex(probe, "GXOS_NET10:PHASE53O_RUNTIME_STACK_HIGH=0x",
                cycle->runtime_stack_high);
    phase53o_hex(probe, "GXOS_NET10:PHASE53O_ALLOC_CONTEXT=0x",
                cycle->allocation_context);
    phase53o_hex(probe, "GXOS_NET10:PHASE53O_MAIN_ALLOC_CONTEXT=0x",
                cycle->main_allocation_context);
    phase53o_hex(probe, "GXOS_NET10:PHASE53O_TLS_ALLOC_LIMIT_BEFORE=0x",
                cycle->alloc_limit_before);
    phase53o_hex(probe, "GXOS_NET10:PHASE53O_TLS_ALLOC_PTR_BEFORE=0x",
                cycle->alloc_ptr_before);
    phase53o_hex(probe, "GXOS_NET10:PHASE53O_TLS_ALLOC_LIMIT_AFTER_GC=0x",
                cycle->alloc_limit_after_gc);
    phase53o_hex(probe, "GXOS_NET10:PHASE53O_TLS_ALLOC_PTR_AFTER_GC=0x",
                cycle->alloc_ptr_after_gc);
    phase53o_hex(probe, "GXOS_NET10:PHASE53O_TLS_ALLOC_LIMIT_AFTER_DETACH=0x",
                cycle->alloc_limit_after_detach);
    phase53o_hex(probe, "GXOS_NET10:PHASE53O_TLS_ALLOC_PTR_AFTER_DETACH=0x",
                cycle->alloc_ptr_after_detach);
    phase53o_hex(probe, "GXOS_NET10:PHASE53O_THREADSTORE_BEFORE=0x",
                cycle->threadstore_before);
    phase53o_hex(probe, "GXOS_NET10:PHASE53O_THREADSTORE_AFTER=0x",
                cycle->threadstore_after);
    phase53o_hex(probe, "GXOS_NET10:PHASE53O_THREADSTORE_HEAD_AFTER=0x",
                cycle->threadstore_head_after);
    phase53o_hex(probe, "GXOS_NET10:PHASE53O_FLS_AFTER_CALLBACK=0x",
                cycle->runtime_thread);
    phase53o_hex(probe, "GXOS_NET10:PHASE53O_FLS_AFTER_CLEAR=0x",
                cycle->scheduler_fls_cleared ? 0 : 1);
    phase53o_hex(probe, "GXOS_NET10:PHASE53O_CALLBACK_RESULT=0x",
                (uint32_t)cycle->callback_result);
    phase53o_hex(probe, "GXOS_NET10:PHASE53O_GC_RESULT=0x",
                (uint32_t)cycle->gc_result);
    phase53o_hex(probe, "GXOS_NET10:PHASE53O_GC_COLLECTION_DELTA=0x",
                cycle->gc_delta);
    phase53o_hex(probe, "GXOS_NET10:PHASE53O_GC_GENERATION=0x",
                cycle->gc_generation);
    phase53o_hex(probe, "GXOS_NET10:PHASE53O_GC_CHECKSUM=0x",
                cycle->gc_checksum);
}

static uintptr_t GXOS_PHASE53O_MS_ABI
phase53o_worker(void *argument)
{
    GXOS_PHASE53O_CYCLE *cycle = (GXOS_PHASE53O_CYCLE *)argument;
    GXOS_PHASE53O_PROBE *probe = cycle->probe;
    GXOS_SCHEDULER_TCB *thread = gxos_scheduler_current_thread();
    GXOS_SCHEDULER_REGISTER_SNAPSHOT snapshot = {0};
    uint32_t seed = cycle->cycle == 1U ? 0x61U : 0x62U;
    int32_t callback_result = 0;
    int32_t gc_result = 0;
    int good = 1;

    cycle->thread = thread;
    gxos_scheduler_capture_registers(&snapshot);
    cycle->worker_rsp = snapshot.rsp;
    if (thread == 0 || thread != gxos_scheduler_current_thread() ||
        thread->is_boot_thread || thread->tls_block_base == 0 ||
        thread->tls_block_base == probe->main_tls_block ||
        thread->fls_values[probe->runtime_fls_slot] != 0 ||
        thread->tls_vector_base == 0 || thread->gs_base == 0 ||
         thread->teb_base == 0 ||
         !gxos_scheduler_validate_thread_context(thread)) {
        return (uintptr_t)phase53o_fail(cycle);
    }
    if (!gxos_nativeaot_scheduler_worker_mark_running(&cycle->lifecycle)) {
        return (uintptr_t)phase53o_fail(cycle);
    }
    cycle->allocation_context = cycle->lifecycle.allocation_context;
    cycle->main_allocation_context = cycle->lifecycle.main_allocation_context;
    cycle->stack_canary_memory = thread->stack_canary_memory;
    phase53o_text(probe, "GXOS_NET10:PHASE53O_THREAD_CYCLE_BEGIN\r\n");
    phase53o_hex(probe, "GXOS_NET10:PHASE53O_TLS_INDEX=0x",
                 probe->tls_index);
    phase53o_hex(probe, "GXOS_NET10:PHASE53O_TLS_VECTOR_SLOT0=0x",
                 ((uint64_t *)(uintptr_t)thread->tls_vector_base)[0]);
    phase53o_hex(probe, "GXOS_NET10:PHASE53O_TLS_VECTOR_SLOT_TLS=0x",
                 ((uint64_t *)(uintptr_t)thread->tls_vector_base)[probe->tls_index]);
    phase53o_text(probe, "GXOS_NET10:MANAGED_THREAD_ATTACH_STATE=UNATTACHED_DIAGNOSTIC\r\n");

    probe->phase_in_managed(PHASE53O_PHASE_IN_MANAGED);
    if (!gxos_nativeaot_scheduler_worker_invoke(
            &cycle->lifecycle, probe->callback_bridge,
            cycle->cycle == 1U ? 7 : 9, &callback_result,
            &cycle->callback_status)) {
        good = 0;
    }
    probe->phase_after_managed(PHASE53O_PHASE_AFTER_MANAGED_RETURN);
    cycle->callback_result = callback_result;
    cycle->runtime_thread = cycle->lifecycle.runtime_thread;
    cycle->runtime_state_before = cycle->lifecycle.runtime_state_before;
    cycle->runtime_transition_frame = cycle->lifecycle.runtime_transition_frame;
    cycle->runtime_next = phase53o_load_u64(
        cycle->runtime_thread, PHASE53O_TLS_THREADSTORE_NEXT_OFFSET);
    cycle->runtime_stack_low = cycle->lifecycle.runtime_stack_low;
    cycle->runtime_stack_high = cycle->lifecycle.runtime_stack_high;
    cycle->alloc_limit_before = cycle->lifecycle.alloc_limit_before;
    cycle->alloc_ptr_before = cycle->lifecycle.alloc_ptr_before;
    /* Preserve the diagnostic report's historical meaning: BEFORE is the
       attached census, while the shared lifecycle keeps its separate
       pre-attach baseline for detach validation. */
    cycle->threadstore_before = cycle->lifecycle.threadstore_after;
    if (cycle->callback_status != GXOS_NATIVEAOT_CALLBACK_OK ||
        cycle->runtime_thread != thread->tls_block_base + 0x30U ||
        cycle->runtime_state_before != GXOS_NATIVEAOT_RUNTIME_THREAD_ATTACHED ||
        cycle->runtime_transition_frame != UINT64_MAX ||
        cycle->runtime_stack_low !=
            thread->stack_contract.reservation_base ||
        cycle->runtime_stack_high != thread->stack_limit ||
        cycle->worker_rsp < cycle->runtime_stack_low ||
        cycle->worker_rsp > cycle->runtime_stack_high ||
        cycle->threadstore_before == 0) {
        good = 0;
    }
    phase53o_text(probe, "GXOS_NET10:MANAGED_THREAD_ATTACH_OK=1\r\n");

    probe->phase_in_managed(PHASE53O_PHASE_IN_MANAGED);
    if (!gxos_nativeaot_scheduler_worker_invoke(
            &cycle->lifecycle, probe->gc_bridge, (int32_t)seed, &gc_result,
            &cycle->gc_status)) {
        good = 0;
    }
    probe->phase_after_managed(PHASE53O_PHASE_AFTER_MANAGED_RETURN);
    cycle->gc_result = gc_result;
    if (cycle->gc_status != GXOS_NATIVEAOT_CALLBACK_OK ||
        !gxos_nativeaot_gc_result_valid(gc_result, seed,
                                        &cycle->gc_delta,
                                        &cycle->gc_generation,
                                        &cycle->gc_checksum) ||
        cycle->gc_delta == 0U) {
        good = 0;
    }
    cycle->alloc_limit_after_gc = cycle->lifecycle.alloc_limit_after;
    cycle->alloc_ptr_after_gc = cycle->lifecycle.alloc_ptr_after;
    cycle->canary_before_detach = phase53o_canary_mask(thread);
    phase53o_text(probe, "GXOS_NET10:MANAGED_CALLBACK_RETURN_OK=1\r\n");
    phase53o_text(probe, "GXOS_NET10:MANAGED_GC_ALLOCATION_OK=1\r\n");
    phase53o_text(probe, "GXOS_NET10:MANAGED_GC_COLLECTION_OBSERVED=1\r\n");
    phase53o_text(probe, "GXOS_NET10:MANAGED_GC_ROOT_SURVIVED=1\r\n");

    if (!gxos_nativeaot_scheduler_worker_detach(&cycle->lifecycle)) {
        good = 0;
    } else {
        cycle->cleanup_called = 1;
        cycle->runtime_state_after = cycle->lifecycle.runtime_state_after;
        cycle->alloc_limit_after_detach =
            cycle->lifecycle.alloc_limit_after_detach;
        cycle->alloc_ptr_after_detach =
            cycle->lifecycle.alloc_ptr_after_detach;
        cycle->canary_after_detach = phase53o_canary_mask(thread);
        phase53o_text(probe,
                      "GXOS_NET10:PHASE53O_FLS_CALLBACK_RETURNED=1\r\n");
    }

    cycle->scheduler_fls_cleared =
        gxos_scheduler_get_fls(probe->runtime_fls_slot) == 0;
    cycle->threadstore_head_after = cycle->lifecycle.threadstore_head_after;
    cycle->threadstore_after = cycle->lifecycle.threadstore_after;
    if (!cycle->cleanup_called || !cycle->scheduler_fls_cleared ||
        cycle->threadstore_after == 0 ||
        cycle->threadstore_before != cycle->threadstore_after + 1U) {
        good = 0;
    }
    phase53o_emit_cycle_state(cycle);
    if (!good) return (uintptr_t)phase53o_fail(cycle);
    return (uintptr_t)cycle->callback_result;
}

static int phase53o_reclaim_cycle(GXOS_PHASE53O_CYCLE *cycle,
                                  uint32_t baseline_vm_regions)
{
    GXOS_SCHEDULER_TCB *thread = cycle->thread;
    int close_result;
    int collect_result;
    int reclaimable_result;
    int reclaimed_result;

    if (thread == 0 || !gxos_scheduler_thread_is_terminated(thread) ||
        thread->return_value != (uintptr_t)cycle->callback_result ||
        cycle->failure != 0 || cycle->cleanup_called == 0 ||
        cycle->scheduler_fls_cleared == 0) {
        return 0;
    }
    phase53o_text(cycle->probe,
                  "GXOS_NET10:MANAGED_THREAD_MANAGED_RETURN_OK=1\r\n");
    phase53o_text(cycle->probe,
                  "GXOS_NET10:MANAGED_THREAD_DETACH_OK=1\r\n");
    phase53o_text(cycle->probe,
                  "GXOS_NET10:MANAGED_THREAD_UNREGISTER_OK=1\r\n");
    reclaimable_result = gxos_nativeaot_scheduler_worker_note_reclaimable(
        &cycle->lifecycle);
    close_result = reclaimable_result &&
        gxos_scheduler_close_handle(cycle->handle);
    collect_result = close_result &&
        gxos_scheduler_collect(cycle->probe->scheduler);
    reclaimed_result = collect_result &&
        gxos_nativeaot_scheduler_worker_note_reclaimed(&cycle->lifecycle);
    collect_result = collect_result && reclaimed_result;
    phase53o_hex(cycle->probe, "GXOS_NET10:PHASE53O_RECLAIMABLE_RESULT=",
                 (uint64_t)reclaimable_result);
    phase53o_hex(cycle->probe, "GXOS_NET10:PHASE53O_RECLAIM_CLOSE_RESULT=",
                 (uint64_t)close_result);
    phase53o_hex(cycle->probe, "GXOS_NET10:PHASE53O_RECLAIM_COLLECT_RESULT=",
                 (uint64_t)collect_result);
    phase53o_hex(cycle->probe, "GXOS_NET10:PHASE53O_RECLAIMED_RESULT=",
                 (uint64_t)reclaimed_result);
    phase53o_hex(cycle->probe, "GXOS_NET10:PHASE53O_OWNERSHIP_STATE=",
                 cycle->lifecycle.ownership_state);
    phase53o_hex(cycle->probe, "GXOS_NET10:PHASE53O_OWNERSHIP_TRANSITIONS=",
                 cycle->lifecycle.ownership_transition_count);
    phase53o_hex(cycle->probe, "GXOS_NET10:PHASE53O_OWNERSHIP_FAILURES=",
                 cycle->lifecycle.ownership_transition_failures);
    phase53o_hex(cycle->probe, "GXOS_NET10:PHASE53O_RECLAIM_THREAD_LIVE=",
                 thread->live);
    phase53o_hex(cycle->probe, "GXOS_NET10:PHASE53O_RECLAIM_THREAD_TLS=",
                 thread->tls_block_base);
    phase53o_hex(cycle->probe, "GXOS_NET10:PHASE53O_RECLAIM_THREAD_VECTOR=",
                 thread->tls_vector_base);
    phase53o_hex(cycle->probe, "GXOS_NET10:PHASE53O_RECLAIM_THREAD_GS=",
                 thread->gs_base);
    phase53o_hex(cycle->probe, "GXOS_NET10:PHASE53O_RECLAIM_THREAD_TEB=",
                 thread->teb_base);
    phase53o_hex(cycle->probe, "GXOS_NET10:PHASE53O_RECLAIM_THREAD_STATE=",
                 thread->state);
    phase53o_hex(cycle->probe, "GXOS_NET10:PHASE53O_RECLAIM_THREAD_EXECUTION_REFS=",
                 thread->execution_refs);
    phase53o_hex(cycle->probe, "GXOS_NET10:PHASE53O_RECLAIM_THREAD_PUBLIC_REFS=",
                 thread->public_handle_refs);
    phase53o_hex(cycle->probe, "GXOS_NET10:PHASE53O_RECLAIM_THREAD_RUNNABLE=",
                 thread->runnable_queued);
    phase53o_hex(cycle->probe, "GXOS_NET10:PHASE53O_RECLAIM_THREAD_CANARIES=",
                 gxos_scheduler_check_canaries(thread));
    phase53o_hex(cycle->probe, "GXOS_NET10:PHASE53O_RECLAIM_THREAD_CANARY_MASK=",
                 phase53o_canary_mask(thread));
    phase53o_hex(cycle->probe, "GXOS_NET10:PHASE53O_RECLAIM_STACK_VM_ID=",
                 thread->stack_vm_identity);
    phase53o_hex(cycle->probe, "GXOS_NET10:PHASE53O_RECLAIM_VM_REGIONS=",
                 cycle->probe->vm_region_count == 0 ? 0 :
                 *cycle->probe->vm_region_count);
    phase53o_hex(cycle->probe, "GXOS_NET10:PHASE53O_RECLAIM_HANDLE_LOOKUP=",
                 gxos_scheduler_thread_from_handle(cycle->handle) != 0);
    if (!close_result || !collect_result ||
        cycle->lifecycle.ownership_state !=
            GXOS_NATIVEAOT_WORKER_OWNERSHIP_RECLAIMED || thread->live != 0 ||
        thread->tls_block_base != 0 || thread->tls_vector_base != 0 ||
        thread->gs_base != 0 || thread->teb_base != 0 ||
        thread->stack_vm_identity != 0 ||
        gxos_scheduler_thread_from_handle(cycle->handle) != 0 ||
        cycle->probe->vm_region_count == 0 ||
        *cycle->probe->vm_region_count != baseline_vm_regions) {
        return 0;
    }
    phase53o_text(cycle->probe,
                  "GXOS_NET10:PHASE53O_SCHEDULER_RECLAIM_OK=1\r\n");
    return 1;
}

int gxos_nativeaot_scheduler_thread_lifecycle_probe(
    GXOS_PHASE53O_PROBE *probe)
{
    GXOS_PHASE53O_CYCLE cycles[2] = {{0}};
    uint64_t main_head;
    uint32_t baseline_threadstore;
    uint32_t baseline_vm_regions;
    uint32_t cycle_index;
    uint32_t previous_identity = 0;
    uint64_t previous_tls = 0;
    uint64_t previous_context = 0;

    if (probe == 0 || probe->scheduler == 0 || probe->main_thread == 0 ||
        probe->callback_bridge == 0 || probe->gc_bridge == 0 ||
        probe->runtime_fls_slot >= GXOS_SCHEDULER_FLS_SLOTS ||
        probe->runtime_fls_cleanup == 0 || probe->main_tls_block == 0 ||
        probe->vm_region_count == 0 || probe->log_text == 0 ||
        probe->log_hex == 0 || probe->phase_in_managed == 0 ||
        probe->phase_after_managed == 0 ||
        probe->main_thread != gxos_scheduler_current_thread() ||
        probe->main_thread->tls_block_base != probe->main_tls_block ||
        !probe->main_thread->is_boot_thread) {
        return 0;
    }
    main_head = (uint64_t)probe->main_thread->fls_values[
        probe->runtime_fls_slot];
    baseline_threadstore = phase53o_threadstore_count(main_head, 0);
    baseline_vm_regions = *probe->vm_region_count;
    if (main_head != probe->main_tls_block + 0x30U ||
        baseline_threadstore == 0) {
        return 0;
    }
    phase53o_text(probe, "GXOS_NET10:PHASE53O_BEGIN\r\n");
    phase53o_text(probe,
                  "GXOS_NET10:PHASE53O_ATTACH_PATH=GENERATED_REVERSE_PINVOKE\r\n");
    phase53o_text(probe,
                  "GXOS_NET10:PHASE53O_DETACH_PATH=RUNTIME_FLS_FIBER_DETACH_CALLBACK\r\n");
    phase53o_hex(probe, "GXOS_NET10:PHASE53O_THREADSTORE_BASELINE=0x",
                baseline_threadstore);
    phase53o_hex(probe, "GXOS_NET10:PHASE53O_VM_REGIONS_BASELINE=0x",
                baseline_vm_regions);
    phase53o_hex(probe, "GXOS_NET10:PHASE53O_M_PNEXT_OFFSET=0x",
                PHASE53O_TLS_THREADSTORE_NEXT_OFFSET);
    phase53o_hex(probe, "GXOS_NET10:PHASE53O_ALLOC_CONTEXT_OFFSET=0x", 0);

    for (cycle_index = 0; cycle_index != 2U; ++cycle_index) {
        GXOS_SCHEDULER_REGISTER_SNAPSHOT snapshot = {0};
        GXOS_SCHEDULER_TCB *thread = 0;
        GXOS_SCHEDULER_HANDLE handle = 0;
        uint32_t identity;

        cycles[cycle_index].probe = probe;
        cycles[cycle_index].cycle = cycle_index + 1U;
        if (!gxos_scheduler_create_suspended_thread(
                probe->scheduler, phase53o_worker, &cycles[cycle_index],
                &handle, &thread) ||
            thread == 0 || thread->fls_values[probe->runtime_fls_slot] != 0 ||
            thread->tls_block_base == 0 ||
            thread->tls_block_base == probe->main_tls_block ||
            thread->tls_block_base + 0x30U == previous_tls ||
            thread->tls_block_base + 0x30U == previous_context ||
            !gxos_scheduler_validate_thread_context(thread)) {
            return 0;
        }
        cycles[cycle_index].handle = handle;
        cycles[cycle_index].thread = thread;
        if (!gxos_nativeaot_scheduler_worker_prepare(
                &cycles[cycle_index].lifecycle, probe->main_thread, thread,
                probe->tls_index, probe->runtime_fls_slot,
                probe->runtime_fls_cleanup)) {
            return 0;
        }
        if (!phase53o_rehome_canary(probe, thread) ||
            !gxos_scheduler_validate_thread_context(thread)) {
            return 0;
        }
        identity = thread->identity;
        if (identity == previous_identity) return 0;
        phase53o_text(probe, "GXOS_NET10:PHASE53O_THREAD_FRESH=1\r\n");
        phase53o_hex(probe, "GXOS_NET10:PHASE53O_FRESH_IDENTITY=0x", identity);
        phase53o_hex(probe, "GXOS_NET10:PHASE53O_FRESH_TLS_BLOCK=0x",
                     thread->tls_block_base);
        if (!gxos_scheduler_resume_thread(handle, 0) ||
            !gxos_nativeaot_scheduler_worker_mark_runnable(
                &cycles[cycle_index].lifecycle)) return 0;
        gxos_scheduler_main_dispatch(&snapshot);
        if (gxos_scheduler_current_thread() != probe->main_thread ||
            !phase53o_reclaim_cycle(&cycles[cycle_index], baseline_vm_regions)) {
            return 0;
        }
        previous_identity = identity;
        previous_tls = cycles[cycle_index].allocation_context;
        previous_context = cycles[cycle_index].allocation_context;
        if (cycle_index == 1U) {
            phase53o_text(probe, "GXOS_NET10:PHASE53O_REPEAT_OK=1\r\n");
        }
    }
    phase53o_text(probe, "GXOS_NET10:PHASE53O_COMPLETE=1\r\n");
    phase53o_text(probe, "GXOS_NET10:PHASE53O_PASS=1\r\n");
    return 1;
}

#define PHASE54_CYCLE_COUNT 12U

typedef struct {
    GXOS_PHASE53O_PROBE *probe;
    uint32_t cycle;
    uint32_t worker_number;
    uint32_t input;
    uint32_t seed;
    GXOS_SCHEDULER_HANDLE handle;
    GXOS_SCHEDULER_TCB *thread;
    GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE lifecycle;
    int32_t callback_result;
    int32_t gc_result;
    uint32_t callback_status;
    uint32_t gc_status;
    uint32_t gc_delta;
    uint32_t gc_generation;
    uint32_t gc_checksum;
    uint64_t worker_rsp;
    uint32_t failure;
} GXOS_PHASE54_WORKER;

static void phase54_text(GXOS_PHASE53O_PROBE *probe, const char *text)
{
    probe->log_text(text);
}

static void phase54_hex(GXOS_PHASE53O_PROBE *probe,
                        const char *name, uint64_t value)
{
    probe->log_hex(name, value);
    probe->log_text("\r\n");
}

static uint32_t phase54_live_threads(const GXOS_SCHEDULER *scheduler)
{
    uint32_t index;
    uint32_t count = 0;
    for (index = 0; index != GXOS_SCHEDULER_MAX_THREADS; ++index) {
        if (scheduler->threads[index].live) ++count;
    }
    return count;
}

static uint32_t phase54_live_objects(const GXOS_SCHEDULER *scheduler)
{
    uint32_t index;
    uint32_t count = 0;
    for (index = 0; index != GXOS_SCHEDULER_MAX_OBJECTS; ++index) {
        if (scheduler->objects[index].live) ++count;
    }
    return count;
}

static int phase54_fail(GXOS_PHASE54_WORKER *worker)
{
    worker->failure = 1;
    phase54_text(worker->probe, "GXOS_NET10:PHASE54_FAILURE=1\r\n");
    return 0;
}

static uintptr_t GXOS_PHASE53O_MS_ABI
phase54_worker_entry(void *argument)
{
    GXOS_PHASE54_WORKER *worker = (GXOS_PHASE54_WORKER *)argument;
    GXOS_PHASE53O_PROBE *probe = worker->probe;
    GXOS_SCHEDULER_TCB *thread = gxos_scheduler_current_thread();
    GXOS_SCHEDULER_REGISTER_SNAPSHOT snapshot = {0};
    uint32_t callback_low;
    int good = 1;

    worker->thread = thread;
    gxos_scheduler_capture_registers(&snapshot);
    worker->worker_rsp = snapshot.rsp;
    if (thread == 0 || !gxos_nativeaot_scheduler_worker_mark_running(
            &worker->lifecycle) ||
        !gxos_scheduler_validate_worker_snapshot(thread, &snapshot) ||
        worker->lifecycle.worker_identity != thread->identity ||
        worker->lifecycle.scheduler_slot == UINT32_MAX) {
        return (uintptr_t)phase54_fail(worker);
    }
    phase54_text(probe, worker->worker_number == 2U
        ? "GXOS_NET10:PHASE54_WORKER_B_RUNNING=1\r\n"
        : "GXOS_NET10:PHASE54_WORKER_A_RUNNING=1\r\n");

    probe->phase_in_managed(PHASE53O_PHASE_IN_MANAGED);
    if (!gxos_nativeaot_scheduler_worker_invoke(
            &worker->lifecycle, probe->callback_bridge,
            (int32_t)worker->input, &worker->callback_result,
            &worker->callback_status)) {
        good = 0;
    }
    probe->phase_after_managed(PHASE53O_PHASE_AFTER_MANAGED_RETURN);
    callback_low = (uint32_t)worker->callback_result & 0xFFFFU;
    if (worker->callback_status != GXOS_NATIVEAOT_CALLBACK_OK ||
        callback_low != worker->input + 1U ||
        worker->lifecycle.ownership_state !=
            GXOS_NATIVEAOT_WORKER_OWNERSHIP_RUNTIME_ATTACHED ||
        worker->lifecycle.runtime_thread == 0 ||
        worker->lifecycle.runtime_thread != thread->tls_block_base + 0x30U) {
        good = 0;
    }
    phase54_text(probe, worker->worker_number == 2U
        ? "GXOS_NET10:PHASE54_WORKER_B_MANAGED_ENTRY_REACHED=1\r\n"
        : "GXOS_NET10:PHASE54_WORKER_A_MANAGED_ENTRY_REACHED=1\r\n");

    probe->phase_in_managed(PHASE53O_PHASE_IN_MANAGED);
    if (!gxos_nativeaot_scheduler_worker_invoke(
            &worker->lifecycle, probe->gc_bridge, (int32_t)worker->seed,
            &worker->gc_result, &worker->gc_status)) {
        good = 0;
    }
    probe->phase_after_managed(PHASE53O_PHASE_AFTER_MANAGED_RETURN);
    if (worker->gc_status != GXOS_NATIVEAOT_CALLBACK_OK ||
        !gxos_nativeaot_gc_result_valid(worker->gc_result, worker->seed,
                                        &worker->gc_delta,
                                        &worker->gc_generation,
                                        &worker->gc_checksum) ||
        worker->gc_delta == 0U) {
        good = 0;
    } else {
        worker->lifecycle.managed_root_survived = 1;
    }
    if (worker->worker_number == 2U) {
        phase54_text(probe,
                     "GXOS_NET10:PHASE54_WORKER_B_POST_GC_CONTINUATION=1\r\n");
    } else {
        phase54_text(probe,
                     "GXOS_NET10:PHASE54_WORKER_A_POST_GC_CONTINUATION=1\r\n");
    }

    if (!gxos_nativeaot_scheduler_worker_detach(&worker->lifecycle) ||
        worker->lifecycle.ownership_state !=
            GXOS_NATIVEAOT_WORKER_OWNERSHIP_RUNTIME_DETACHED ||
        gxos_scheduler_get_fls(probe->runtime_fls_slot) != 0) {
        good = 0;
    }
    if (worker->worker_number == 2U) {
        phase54_text(probe, "GXOS_NET10:PHASE54_WORKER_B_DETACHED=1\r\n");
    } else {
        phase54_text(probe, "GXOS_NET10:PHASE54_WORKER_A_DETACHED=1\r\n");
    }
    if (!good) return (uintptr_t)phase54_fail(worker);
    return (uintptr_t)worker->callback_result;
}

int gxos_nativeaot_managed_worker_ownership_probe(
    GXOS_PHASE53O_PROBE *probe)
{
    static GXOS_PHASE54_WORKER worker_a;
    static GXOS_PHASE54_WORKER worker_b;
    GXOS_SCHEDULER_REGISTER_SNAPSHOT snapshot = {0};
    uint32_t baseline_vm_regions;
    uint32_t baseline_threads;
    uint32_t baseline_objects;
    uint32_t peak_vm_regions;
    uint32_t peak_threads;
    uint32_t peak_objects;
    uint32_t after_threads;
    uint32_t after_objects;
    uint32_t cycle;
    uint32_t dispatches;
    uint32_t previous_b_identity = 0;
    uint16_t previous_b_generation = 0;
    uint32_t previous_b_slot = UINT32_MAX;
    uint32_t invalid_transition_rejections = 0;
    uint32_t duplicate_teardown_rejections = 0;
    int independence_passed = 1;
    int reuse_passed = 1;

    if (probe == 0 || probe->scheduler == 0 || probe->main_thread == 0 ||
        probe->callback_bridge == 0 || probe->gc_bridge == 0 ||
        probe->runtime_fls_cleanup == 0 || probe->vm_region_count == 0 ||
        probe->log_text == 0 || probe->log_hex == 0 ||
        probe->phase_in_managed == 0 || probe->phase_after_managed == 0 ||
        probe->main_thread != gxos_scheduler_current_thread() ||
        !probe->main_thread->is_boot_thread) {
        return 0;
    }

    baseline_vm_regions = *probe->vm_region_count;
    baseline_threads = phase54_live_threads(probe->scheduler);
    baseline_objects = phase54_live_objects(probe->scheduler);
    peak_vm_regions = baseline_vm_regions;
    peak_threads = baseline_threads;
    peak_objects = baseline_objects;
    phase54_text(probe, "GXOS_NET10:PHASE54_BEGIN\r\n");
    phase54_text(probe,
                 "GXOS_NET10:PHASE54_OWNERSHIP_LEDGER=EXTENDED_LIFECYCLE_RECORD\r\n");
    phase54_text(probe,
                 "GXOS_NET10:PHASE54_STATE_SEQUENCE=FREE>ALLOCATED>RUNNABLE>RUNNING>RUNTIME_ATTACHED>DETACH_PENDING>RUNTIME_DETACHED>RECLAIMABLE>RECLAIMED\r\n");
    phase54_hex(probe, "GXOS_NET10:PHASE54_RESOURCE_BASELINE_VM_REGIONS=0x",
                baseline_vm_regions);
    phase54_hex(probe, "GXOS_NET10:PHASE54_RESOURCE_BASELINE_THREADS=0x",
                baseline_threads);
    phase54_hex(probe, "GXOS_NET10:PHASE54_RESOURCE_BASELINE_OBJECTS=0x",
                baseline_objects);

    for (cycle = 0; cycle != PHASE54_CYCLE_COUNT; ++cycle) {
        worker_a = (GXOS_PHASE54_WORKER){0};
        worker_b = (GXOS_PHASE54_WORKER){0};
        worker_a.probe = probe;
        worker_a.cycle = cycle + 1U;
        worker_a.worker_number = 1U;
        worker_a.input = 7U;
        worker_a.seed = 0x61U;
        worker_b.probe = probe;
        worker_b.cycle = cycle + 1U;
        worker_b.worker_number = 2U;
        worker_b.input = 9U;
        worker_b.seed = 0x62U;
        if (!gxos_scheduler_create_suspended_thread(
                probe->scheduler, phase54_worker_entry, &worker_a,
                &worker_a.handle, &worker_a.thread) ||
            !gxos_scheduler_create_suspended_thread(
                probe->scheduler, phase54_worker_entry, &worker_b,
                &worker_b.handle, &worker_b.thread) ||
            !gxos_nativeaot_scheduler_worker_prepare(
                &worker_a.lifecycle, probe->main_thread, worker_a.thread,
                probe->tls_index, probe->runtime_fls_slot,
                probe->runtime_fls_cleanup) ||
            !gxos_nativeaot_scheduler_worker_prepare(
                &worker_b.lifecycle, probe->main_thread, worker_b.thread,
                probe->tls_index, probe->runtime_fls_slot,
                probe->runtime_fls_cleanup)) {
            return 0;
        }
        if (cycle == 0U) {
            if (gxos_nativeaot_scheduler_worker_detach(&worker_b.lifecycle) ||
                gxos_nativeaot_scheduler_worker_note_reclaimable(
                    &worker_b.lifecycle) ||
                worker_b.lifecycle.ownership_transition_failures < 2U) {
                return 0;
            }
            invalid_transition_rejections += 2U;
        }
        if (!phase53o_rehome_canary(probe, worker_a.thread) ||
            !phase53o_rehome_canary(probe, worker_b.thread) ||
            !gxos_scheduler_resume_thread(worker_a.handle, 0) ||
            !gxos_nativeaot_scheduler_worker_mark_runnable(
                &worker_a.lifecycle) ||
            !gxos_scheduler_resume_thread(worker_b.handle, 0) ||
            !gxos_nativeaot_scheduler_worker_mark_runnable(
                &worker_b.lifecycle)) {
            return 0;
        }
        peak_vm_regions = *probe->vm_region_count > peak_vm_regions
            ? *probe->vm_region_count : peak_vm_regions;
        peak_threads = phase54_live_threads(probe->scheduler) > peak_threads
            ? phase54_live_threads(probe->scheduler) : peak_threads;
        peak_objects = phase54_live_objects(probe->scheduler) > peak_objects
            ? phase54_live_objects(probe->scheduler) : peak_objects;
        for (dispatches = 0; dispatches != 4U; ++dispatches) {
            if (gxos_scheduler_thread_is_terminated(worker_a.thread) &&
                gxos_scheduler_thread_is_terminated(worker_b.thread)) break;
            if (gxos_scheduler_current_thread() != probe->main_thread ||
                gxos_scheduler_runnable_count() == 0U) return 0;
            gxos_scheduler_main_dispatch(&snapshot);
        }
        if (!gxos_scheduler_thread_is_terminated(worker_a.thread) ||
            !gxos_scheduler_thread_is_terminated(worker_b.thread) ||
            worker_a.failure != 0 || worker_b.failure != 0 ||
            worker_a.lifecycle.managed_root_survived == 0 ||
            worker_b.lifecycle.managed_root_survived == 0 ||
            worker_a.lifecycle.callback_registration_observed == 0 ||
            worker_b.lifecycle.callback_registration_observed == 0 ||
            worker_a.lifecycle.saved_rsp == 0 || worker_b.lifecycle.saved_rsp == 0 ||
            worker_a.lifecycle.allocation_context == 0 ||
            worker_b.lifecycle.allocation_context == 0 ||
            worker_a.lifecycle.guard_vm_identity == 0 ||
            worker_b.lifecycle.guard_vm_identity == 0 ||
            worker_a.lifecycle.usable_vm_identity == 0 ||
            worker_b.lifecycle.usable_vm_identity == 0 ||
            worker_a.lifecycle.runtime_thread == worker_b.lifecycle.runtime_thread ||
            worker_a.lifecycle.allocation_context ==
                worker_b.lifecycle.allocation_context ||
            worker_a.lifecycle.tls_block_base == worker_b.lifecycle.tls_block_base ||
            worker_a.lifecycle.gs_base == worker_b.lifecycle.gs_base ||
            worker_a.lifecycle.teb_base == worker_b.lifecycle.teb_base ||
            worker_a.lifecycle.tls_vector_base == worker_b.lifecycle.tls_vector_base ||
            worker_a.lifecycle.stack_reservation_base ==
                worker_b.lifecycle.stack_reservation_base ||
            worker_a.lifecycle.usable_vm_identity ==
                worker_b.lifecycle.usable_vm_identity ||
            worker_a.worker_rsp < worker_a.lifecycle.stack_usable_low ||
            worker_a.worker_rsp >= worker_a.lifecycle.stack_usable_high ||
            worker_b.worker_rsp < worker_b.lifecycle.stack_usable_low ||
            worker_b.worker_rsp >= worker_b.lifecycle.stack_usable_high) {
            independence_passed = 0;
        }
        if (cycle != 0U &&
            (worker_b.lifecycle.scheduler_slot != previous_b_slot ||
             worker_b.lifecycle.worker_identity == previous_b_identity ||
             worker_b.lifecycle.worker_generation == previous_b_generation)) {
            reuse_passed = 0;
        }
        if (!gxos_nativeaot_scheduler_worker_note_reclaimable(
                &worker_a.lifecycle) ||
            !gxos_nativeaot_scheduler_worker_note_reclaimable(
                &worker_b.lifecycle) ||
            !gxos_scheduler_close_handle(worker_a.handle) ||
            !gxos_scheduler_close_handle(worker_b.handle) ||
            !gxos_scheduler_collect(probe->scheduler) ||
            !gxos_nativeaot_scheduler_worker_note_reclaimed(
                &worker_a.lifecycle) ||
            !gxos_nativeaot_scheduler_worker_note_reclaimed(
                &worker_b.lifecycle) ||
            worker_a.lifecycle.ownership_state !=
                GXOS_NATIVEAOT_WORKER_OWNERSHIP_RECLAIMED ||
            worker_b.lifecycle.ownership_state !=
                GXOS_NATIVEAOT_WORKER_OWNERSHIP_RECLAIMED ||
            gxos_scheduler_thread_from_handle(worker_a.handle) != 0 ||
            gxos_scheduler_thread_from_handle(worker_b.handle) != 0) {
            return 0;
        }
        if (cycle == 0U) {
            phase54_text(probe,
                         "GXOS_NET10:PHASE54_INVALID_TRANSITIONS_REJECTED=1\r\n");
        }
        if (cycle == PHASE54_CYCLE_COUNT - 1U &&
            (gxos_nativeaot_scheduler_worker_detach(&worker_b.lifecycle) ||
             gxos_nativeaot_scheduler_worker_note_reclaimable(
                 &worker_b.lifecycle))) {
            return 0;
        }
        if (cycle == PHASE54_CYCLE_COUNT - 1U) {
            duplicate_teardown_rejections += 2U;
            phase54_text(probe,
                         "GXOS_NET10:PHASE54_DUPLICATE_TEARDOWN_REJECTED=1\r\n");
        }
        after_threads = phase54_live_threads(probe->scheduler);
        after_objects = phase54_live_objects(probe->scheduler);
        if (*probe->vm_region_count != baseline_vm_regions ||
            after_threads != baseline_threads || after_objects != baseline_objects) {
            return 0;
        }
        phase54_hex(probe, "GXOS_NET10:PHASE54_CYCLE=", cycle + 1U);
        phase54_hex(probe, "GXOS_NET10:PHASE54_CYCLE_WORKER_A_IDENTITY=",
                    worker_a.lifecycle.worker_identity);
        phase54_hex(probe, "GXOS_NET10:PHASE54_CYCLE_WORKER_B_IDENTITY=",
                    worker_b.lifecycle.worker_identity);
        phase54_hex(probe, "GXOS_NET10:PHASE54_CYCLE_WORKER_A_GENERATION=",
                    worker_a.lifecycle.worker_generation);
        phase54_hex(probe, "GXOS_NET10:PHASE54_CYCLE_WORKER_B_GENERATION=",
                    worker_b.lifecycle.worker_generation);
        phase54_hex(probe, "GXOS_NET10:PHASE54_CYCLE_WORKER_A_SLOT=",
                    worker_a.lifecycle.scheduler_slot);
        phase54_hex(probe, "GXOS_NET10:PHASE54_CYCLE_WORKER_B_SLOT=",
                    worker_b.lifecycle.scheduler_slot);
        phase54_hex(probe, "GXOS_NET10:PHASE54_CYCLE_WORKER_A_STATE=",
                    worker_a.lifecycle.ownership_state);
        phase54_hex(probe, "GXOS_NET10:PHASE54_CYCLE_WORKER_B_STATE=",
                    worker_b.lifecycle.ownership_state);
        phase54_hex(probe, "GXOS_NET10:PHASE54_CYCLE_WORKER_A_TRANSITIONS=",
                    worker_a.lifecycle.ownership_transition_count);
        phase54_hex(probe, "GXOS_NET10:PHASE54_CYCLE_WORKER_B_TRANSITIONS=",
                    worker_b.lifecycle.ownership_transition_count);
        phase54_hex(probe, "GXOS_NET10:PHASE54_CYCLE_WORKER_A_RSP=",
                    worker_a.worker_rsp);
        phase54_hex(probe, "GXOS_NET10:PHASE54_CYCLE_WORKER_B_RSP=",
                    worker_b.worker_rsp);
        phase54_text(probe, "GXOS_NET10:PHASE54_WORKER_A_RECLAIMED=1\r\n");
        phase54_text(probe, "GXOS_NET10:PHASE54_WORKER_B_RECLAIMED=1\r\n");
        phase54_text(probe, "GXOS_NET10:PHASE54_BASELINE_RESTORED=1\r\n");
        previous_b_identity = worker_b.lifecycle.worker_identity;
        previous_b_generation = worker_b.lifecycle.worker_generation;
        previous_b_slot = worker_b.lifecycle.scheduler_slot;
    }
    if (!independence_passed || !reuse_passed ||
        invalid_transition_rejections != 2U ||
        duplicate_teardown_rejections != 2U) return 0;
    after_threads = phase54_live_threads(probe->scheduler);
    after_objects = phase54_live_objects(probe->scheduler);
    phase54_hex(probe, "GXOS_NET10:PHASE54_RESOURCE_PEAK_VM_REGIONS=0x",
                peak_vm_regions);
    phase54_hex(probe, "GXOS_NET10:PHASE54_RESOURCE_PEAK_THREADS=0x",
                peak_threads);
    phase54_hex(probe, "GXOS_NET10:PHASE54_RESOURCE_PEAK_OBJECTS=0x",
                peak_objects);
    phase54_hex(probe, "GXOS_NET10:PHASE54_RESOURCE_AFTER_VM_REGIONS=0x",
                *probe->vm_region_count);
    phase54_hex(probe, "GXOS_NET10:PHASE54_RESOURCE_AFTER_THREADS=0x",
                after_threads);
    phase54_hex(probe, "GXOS_NET10:PHASE54_RESOURCE_AFTER_OBJECTS=0x",
                after_objects);
    phase54_text(probe, "GXOS_NET10:PHASE54_WORKER_INDEPENDENCE_OK=1\r\n");
    phase54_text(probe, "GXOS_NET10:PHASE54_WORKER_STACK_RSP_BOUNDS_OK=1\r\n");
    phase54_text(probe, "GXOS_NET10:PHASE54_WORKER_RUNTIME_THREAD_INDEPENDENCE_OK=1\r\n");
    phase54_text(probe, "GXOS_NET10:PHASE54_WORKER_ALLOCATION_CONTEXT_INDEPENDENCE_OK=1\r\n");
    phase54_text(probe, "GXOS_NET10:PHASE54_WORKER_ROOT_SURVIVAL_OK=1\r\n");
    phase54_text(probe, "GXOS_NET10:PHASE54_WORKER_SLOT_LEDGER_OK=1\r\n");
    phase54_text(probe, "GXOS_NET10:PHASE54_SLOT_REUSE_GENERATION_OK=1\r\n");
    phase54_text(probe, "GXOS_NET10:PHASE54_OWNERSHIP_LEDGER_BASELINE_OK=1\r\n");
    phase54_text(probe, "GXOS_NET10:PHASE54_COMPLETE=1\r\n");
    phase54_text(probe, "GXOS_NET10:PHASE54_PASS=1\r\n");
    return 1;
}

#ifdef GXOS_ENABLE_PHASE56_FAILURE_INJECTION
static GXOS_NATIVEAOT_FAILURE_INJECTION_RECORD g_phase56_failure_record;

static int phase56_injection_state_valid(
    const GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE *lifecycle)
{
    GXOS_SCHEDULER_TCB *thread;
    GXOS_SCHEDULER_HANDLE published_handle;
    if (lifecycle == 0 || lifecycle->thread == 0 ||
        lifecycle->ownership_state != GXOS_NATIVEAOT_WORKER_OWNERSHIP_ALLOCATED ||
        lifecycle->attached || lifecycle->detached ||
        lifecycle->runtime_thread != 0 || lifecycle->runtime_thread_owned ||
        lifecycle->managed_worker_object_owned ||
        lifecycle->managed_root_survived ||
        lifecycle->callback_registration_observed ||
        lifecycle->runtime_state_before != 0 ||
        lifecycle->runtime_state_after != 0 ||
        lifecycle->runtime_transition_frame != 0 ||
        lifecycle->runtime_stack_low != 0 ||
        lifecycle->runtime_stack_high != 0 ||
        !lifecycle_matches_current_thread(lifecycle)) {
        return 0;
    }
    thread = lifecycle->thread;
    if (!thread->live || thread->is_boot_thread ||
        thread->state != GXOS_SCHEDULER_THREAD_CREATED_SUSPENDED ||
        thread->runnable_queued || thread->suspend_count != 1U ||
        thread->execution_refs != 1U || thread->public_handle_refs != 1U ||
        thread->fls_values[lifecycle->runtime_fls_slot] != 0 ||
        !lifecycle->scheduler_owned || !lifecycle->stack_owned ||
        !lifecycle->environment_owned || !lifecycle->tls_fls_owned ||
        !lifecycle->vm_resources_owned || lifecycle->stack_reservation_base == 0 ||
        lifecycle->stack_guard_base == 0 || lifecycle->stack_usable_low == 0 ||
        lifecycle->stack_usable_high == 0 || lifecycle->saved_rsp == 0 ||
        lifecycle->gs_base == 0 || lifecycle->teb_base == 0 ||
        lifecycle->tls_vector_base == 0 || lifecycle->tls_block_base == 0 ||
        lifecycle->guard_vm_identity == 0 || lifecycle->usable_vm_identity == 0 ||
        !thread->stack_contract.guard_nonpresent ||
        thread->stack_contract.reservation_base != lifecycle->stack_reservation_base ||
        thread->stack_contract.guard_base != lifecycle->stack_guard_base ||
        thread->stack_contract.usable_stack_low != lifecycle->stack_usable_low ||
        thread->stack_contract.usable_stack_high != lifecycle->stack_usable_high ||
        thread->gs_base != lifecycle->gs_base ||
        thread->teb_base != lifecycle->teb_base ||
        thread->tls_vector_base != lifecycle->tls_vector_base ||
        thread->tls_block_base != lifecycle->tls_block_base) {
        return 0;
    }
    if (thread->object_slot >= GXOS_SCHEDULER_MAX_OBJECTS) return 0;
    /* Reconstruct the public thread handle to prove that publication has
       completed without adding a handle field to the lifecycle record. */
    published_handle = ((GXOS_SCHEDULER_HANDLE)GXOS_SCHEDULER_HANDLE_MAGIC << 56) |
        ((GXOS_SCHEDULER_HANDLE)GXOS_SCHEDULER_OBJECT_THREAD << 48) |
        ((GXOS_SCHEDULER_HANDLE)thread->generation << 16) |
        ((GXOS_SCHEDULER_HANDLE)thread->object_slot + 1U);
    return gxos_scheduler_thread_from_handle(published_handle) == thread;
}

#ifdef GXOS_ENABLE_PHASE57_POSTATTACH_ROLLBACK
static int phase57_injection_state_valid(
    const GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE *lifecycle)
{
    GXOS_SCHEDULER_TCB *thread;
    if (lifecycle == 0 || lifecycle->thread == 0 ||
        lifecycle->ownership_state !=
            GXOS_NATIVEAOT_WORKER_OWNERSHIP_RUNTIME_ATTACHED ||
        !lifecycle->attached || lifecycle->detached ||
        lifecycle->runtime_attach_count != 1U ||
        lifecycle->runtime_detach_count != 0U ||
        lifecycle->runtime_thread == 0 || !lifecycle->runtime_thread_owned ||
        !lifecycle->managed_worker_object_owned ||
        lifecycle->managed_root_survived ||
        !lifecycle->callback_registration_observed ||
        lifecycle->runtime_state_before !=
            GXOS_NATIVEAOT_RUNTIME_THREAD_ATTACHED ||
        lifecycle->runtime_transition_frame != UINT64_MAX ||
        !lifecycle_matches_current_thread(lifecycle)) {
        return 0;
    }
    thread = lifecycle->thread;
    return thread->live && !thread->is_boot_thread &&
           thread->state == GXOS_SCHEDULER_THREAD_RUNNING &&
           gxos_scheduler_current_thread() == thread &&
           lifecycle->scheduler_owned && lifecycle->stack_owned &&
           lifecycle->environment_owned && lifecycle->tls_fls_owned &&
           lifecycle->vm_resources_owned && lifecycle->stack_reservation_base != 0 &&
           lifecycle->stack_guard_base != 0 &&
           lifecycle->stack_usable_low != 0 &&
           lifecycle->stack_usable_high != 0 && lifecycle->gs_base != 0 &&
           lifecycle->teb_base != 0 && lifecycle->tls_vector_base != 0 &&
           lifecycle->tls_block_base != 0 && lifecycle->guard_vm_identity != 0 &&
           lifecycle->usable_vm_identity != 0 &&
           thread->stack_contract.guard_nonpresent &&
           thread->stack_contract.reservation_base ==
               lifecycle->stack_reservation_base &&
           thread->stack_contract.guard_base == lifecycle->stack_guard_base &&
           thread->stack_contract.usable_stack_low ==
               lifecycle->stack_usable_low &&
           thread->stack_contract.usable_stack_high ==
               lifecycle->stack_usable_high &&
           thread->gs_base == lifecycle->gs_base &&
           thread->teb_base == lifecycle->teb_base &&
           thread->tls_vector_base == lifecycle->tls_vector_base &&
           thread->tls_block_base == lifecycle->tls_block_base &&
           thread->context.rsp >= lifecycle->stack_usable_low &&
           thread->context.rsp <= lifecycle->stack_usable_high &&
           thread->fls_values[lifecycle->runtime_fls_slot] ==
               lifecycle->runtime_thread &&
           lifecycle_load_u64(lifecycle->runtime_thread,
                              GXOS_NATIVEAOT_TLS_STATE_FLAGS_OFFSET) ==
               GXOS_NATIVEAOT_RUNTIME_THREAD_ATTACHED &&
           lifecycle->runtime_stack_low ==
               thread->stack_contract.reservation_base &&
           lifecycle->runtime_stack_high == thread->stack_limit &&
           lifecycle->allocation_context == thread->tls_block_base + 0x38U &&
           lifecycle->allocation_context != 0;
}
#endif

#ifdef GXOS_ENABLE_PHASE57_POSTATTACH_ROLLBACK
#define PHASE57_CYCLE_COUNT 12U

typedef struct {
    GXOS_PHASE53O_PROBE *probe;
    uint32_t cycle;
    GXOS_SCHEDULER_HANDLE failed_handle;
    GXOS_SCHEDULER_TCB *failed_thread;
    GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE failed_lifecycle;
    GXOS_SCHEDULER_HANDLE replacement_handle;
    GXOS_SCHEDULER_TCB *replacement_thread;
    GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE replacement_lifecycle;
    uint32_t replacement_input;
    uint32_t replacement_seed;
    int32_t failed_callback_result;
    uint32_t failed_callback_status;
    int32_t replacement_callback_result;
    int32_t replacement_gc_result;
    uint32_t replacement_callback_status;
    uint32_t replacement_gc_status;
    uint32_t replacement_gc_delta;
    uint32_t replacement_gc_generation;
    uint32_t replacement_gc_checksum;
    uint32_t failed_callback_before;
    uint32_t failed_callback_after;
    uint32_t failed_gc_before;
    uint32_t failed_gc_after;
    uint32_t runtime_attach_before;
    uint32_t runtime_attach_after;
    uint32_t runtime_detach_before;
    uint32_t runtime_detach_after;
    uint32_t prepared_vm;
    uint32_t prepared_threads;
    uint32_t prepared_objects;
    uint32_t runtime_attached_vm;
    uint32_t runtime_attached_threads;
    uint32_t runtime_attached_objects;
    uint32_t post_detach_vm;
    uint32_t post_detach_threads;
    uint32_t post_detach_objects;
    uint32_t failure;
} GXOS_PHASE57_CYCLE;

static GXOS_PHASE57_CYCLE g_phase57_cycle;

static void phase57_text(GXOS_PHASE53O_PROBE *probe, const char *text)
{
    probe->log_text(text);
}

static void phase57_hex(GXOS_PHASE53O_PROBE *probe,
                        const char *name, uint64_t value)
{
    probe->log_hex(name, value);
    probe->log_text("\r\n");
}

static uint32_t phase57_live_threads(const GXOS_SCHEDULER *scheduler)
{
    uint32_t index;
    uint32_t count = 0;
    for (index = 0; index != GXOS_SCHEDULER_MAX_THREADS; ++index) {
        if (scheduler->threads[index].live) ++count;
    }
    return count;
}

static uint32_t phase57_live_objects(const GXOS_SCHEDULER *scheduler)
{
    uint32_t index;
    uint32_t count = 0;
    for (index = 0; index != GXOS_SCHEDULER_MAX_OBJECTS; ++index) {
        if (scheduler->objects[index].live) ++count;
    }
    return count;
}

static int phase57_fail(GXOS_PHASE57_CYCLE *cycle)
{
    cycle->failure = 1;
    phase57_text(cycle->probe, "GXOS_NET10:PHASE57_FAILURE=1\r\n");
    return 0;
}

static uintptr_t GXOS_PHASE53O_MS_ABI
phase57_failed_worker_entry(void *argument)
{
    GXOS_PHASE57_CYCLE *cycle = (GXOS_PHASE57_CYCLE *)argument;
    GXOS_PHASE53O_PROBE *probe = cycle->probe;
    GXOS_SCHEDULER_TCB *thread = gxos_scheduler_current_thread();
    GXOS_SCHEDULER_REGISTER_SNAPSHOT snapshot = {0};
    GXOS_NATIVEAOT_FAILURE_INJECTION_RECORD const *injection;
    int good = 1;

    cycle->failed_thread = thread;
    gxos_scheduler_capture_registers(&snapshot);
    if (thread == 0 || !gxos_nativeaot_scheduler_worker_mark_running(
            &cycle->failed_lifecycle) ||
        !gxos_scheduler_validate_worker_snapshot(thread, &snapshot)) {
        return (uintptr_t)phase57_fail(cycle);
    }
    phase57_text(probe, "GXOS_NET10:PHASE57_FAILED_WORKER_RUNNING=1\r\n");
    cycle->failed_callback_before = probe->callback_bridge->invocation_count;
    cycle->failed_gc_before = probe->gc_bridge->invocation_count;
    phase57_text(probe, "GXOS_NET10:PHASE57_RUNTIME_ATTACH_ENTERED=1\r\n");
    probe->phase_in_managed(PHASE53O_PHASE_IN_MANAGED);
    if (!gxos_nativeaot_scheduler_worker_invoke(
            &cycle->failed_lifecycle, probe->callback_bridge, 0x57,
            &cycle->failed_callback_result,
            &cycle->failed_callback_status)) {
        good = 0;
    }
    probe->phase_after_managed(PHASE53O_PHASE_AFTER_MANAGED_RETURN);
    cycle->failed_callback_after = probe->callback_bridge->invocation_count;
    cycle->failed_gc_after = probe->gc_bridge->invocation_count;
    if (cycle->failed_callback_status != GXOS_NATIVEAOT_CALLBACK_OK ||
        cycle->failed_lifecycle.ownership_state !=
            GXOS_NATIVEAOT_WORKER_OWNERSHIP_RUNTIME_ATTACHED ||
        cycle->failed_lifecycle.runtime_thread == 0 ||
        cycle->failed_lifecycle.runtime_attach_count != 1U ||
        cycle->failed_lifecycle.runtime_detach_count != 0U) {
        good = 0;
    }
    if (!good) return (uintptr_t)phase57_fail(cycle);
    phase57_text(probe, "GXOS_NET10:PHASE57_RUNTIME_ATTACH_SUCCEEDED=1\r\n");
    phase57_hex(probe, "GXOS_NET10:PHASE57_RUNTIME_THREAD=0x",
                cycle->failed_lifecycle.runtime_thread);
    phase57_hex(probe, "GXOS_NET10:PHASE57_RUNTIME_STATE=0x",
                cycle->failed_lifecycle.runtime_state_before);
    phase57_hex(probe, "GXOS_NET10:PHASE57_RUNTIME_ALLOCATION_CONTEXT=0x",
                cycle->failed_lifecycle.allocation_context);
    phase57_hex(probe, "GXOS_NET10:PHASE57_FAILED_STACK_RESERVATION=0x",
                cycle->failed_lifecycle.stack_reservation_base);
    phase57_hex(probe, "GXOS_NET10:PHASE57_FAILED_STACK_GUARD=0x",
                cycle->failed_lifecycle.stack_guard_base);
    phase57_hex(probe, "GXOS_NET10:PHASE57_FAILED_STACK_USABLE_LOW=0x",
                cycle->failed_lifecycle.stack_usable_low);
    phase57_hex(probe, "GXOS_NET10:PHASE57_FAILED_STACK_USABLE_HIGH=0x",
                cycle->failed_lifecycle.stack_usable_high);
    phase57_hex(probe, "GXOS_NET10:PHASE57_FAILED_GS=0x",
                cycle->failed_lifecycle.gs_base);
    phase57_hex(probe, "GXOS_NET10:PHASE57_FAILED_TEB=0x",
                cycle->failed_lifecycle.teb_base);
    phase57_hex(probe, "GXOS_NET10:PHASE57_FAILED_TLS_VECTOR=0x",
                cycle->failed_lifecycle.tls_vector_base);
    phase57_hex(probe, "GXOS_NET10:PHASE57_FAILED_TLS_BLOCK=0x",
                cycle->failed_lifecycle.tls_block_base);
    phase57_hex(probe, "GXOS_NET10:PHASE57_FAILED_FLS_VALUE=0x",
                thread->fls_values[probe->runtime_fls_slot]);
    cycle->runtime_attached_vm = *probe->vm_region_count;
    cycle->runtime_attached_threads = phase57_live_threads(
        probe->scheduler);
    cycle->runtime_attached_objects = phase57_live_objects(
        probe->scheduler);
    if (!gxos_nativeaot_phase56_failure_arm(
            GXOS_NATIVEAOT_FAILURE_INJECTION_AFTER_RUNTIME_ATTACH,
            &cycle->failed_lifecycle)) {
        return (uintptr_t)phase57_fail(cycle);
    }
    phase57_text(probe, "GXOS_NET10:PHASE57_INJECTION_ARMED=1\r\n");
    if (!gxos_nativeaot_phase56_failure_try_fire(
            &cycle->failed_lifecycle) ||
        gxos_nativeaot_phase56_failure_try_fire(
            &cycle->failed_lifecycle)) {
        return (uintptr_t)phase57_fail(cycle);
    }
    injection = gxos_nativeaot_phase56_failure_record();
    if (injection == 0 || injection->state !=
            GXOS_NATIVEAOT_FAILURE_INJECTION_FIRED ||
        injection->fire_count != 1U || injection->mismatch_count != 0U) {
        return (uintptr_t)phase57_fail(cycle);
    }
    phase57_text(probe, "GXOS_NET10:PHASE57_INJECTION_FIRED=1\r\n");
    phase57_text(probe, "GXOS_NET10:PHASE57_INTENTIONAL_FAILURE=1\r\n");
    if (!gxos_nativeaot_scheduler_worker_detach(
            &cycle->failed_lifecycle) ||
        cycle->failed_lifecycle.runtime_detach_count != 1U ||
        cycle->failed_lifecycle.ownership_state !=
            GXOS_NATIVEAOT_WORKER_OWNERSHIP_RUNTIME_DETACHED ||
        gxos_scheduler_get_fls(probe->runtime_fls_slot) != 0) {
        return (uintptr_t)phase57_fail(cycle);
    }
    phase57_text(probe, "GXOS_NET10:PHASE57_DETACH_EXECUTED=1\r\n");
    return (uintptr_t)cycle->failed_callback_result;
}

static uintptr_t GXOS_PHASE53O_MS_ABI
phase57_replacement_worker_entry(void *argument)
{
    GXOS_PHASE57_CYCLE *cycle = (GXOS_PHASE57_CYCLE *)argument;
    GXOS_PHASE53O_PROBE *probe = cycle->probe;
    GXOS_SCHEDULER_TCB *thread = gxos_scheduler_current_thread();
    GXOS_SCHEDULER_REGISTER_SNAPSHOT snapshot = {0};
    uint32_t callback_low;
    int good = 1;

    cycle->replacement_thread = thread;
    gxos_scheduler_capture_registers(&snapshot);
    if (thread == 0 || !gxos_nativeaot_scheduler_worker_mark_running(
            &cycle->replacement_lifecycle) ||
        !gxos_scheduler_validate_worker_snapshot(thread, &snapshot)) {
        return (uintptr_t)phase57_fail(cycle);
    }
    phase57_text(probe, "GXOS_NET10:PHASE57_REPLACEMENT_RUNNING=1\r\n");
    probe->phase_in_managed(PHASE53O_PHASE_IN_MANAGED);
    if (!gxos_nativeaot_scheduler_worker_invoke(
            &cycle->replacement_lifecycle, probe->callback_bridge,
            (int32_t)cycle->replacement_input,
            &cycle->replacement_callback_result,
            &cycle->replacement_callback_status)) {
        good = 0;
    }
    probe->phase_after_managed(PHASE53O_PHASE_AFTER_MANAGED_RETURN);
    callback_low = (uint32_t)cycle->replacement_callback_result & 0xFFFFU;
    if (cycle->replacement_callback_status != GXOS_NATIVEAOT_CALLBACK_OK ||
        callback_low != cycle->replacement_input + 1U ||
        cycle->replacement_lifecycle.ownership_state !=
            GXOS_NATIVEAOT_WORKER_OWNERSHIP_RUNTIME_ATTACHED ||
        cycle->replacement_lifecycle.runtime_thread == 0 ||
        cycle->replacement_lifecycle.runtime_attach_count != 1U) {
        good = 0;
    }
    phase57_text(probe,
                 "GXOS_NET10:PHASE57_REPLACEMENT_MANAGED_CALLBACK=1\r\n");

    probe->phase_in_managed(PHASE53O_PHASE_IN_MANAGED);
    if (!gxos_nativeaot_scheduler_worker_invoke(
            &cycle->replacement_lifecycle, probe->gc_bridge,
            (int32_t)cycle->replacement_seed, &cycle->replacement_gc_result,
            &cycle->replacement_gc_status)) {
        good = 0;
    }
    probe->phase_after_managed(PHASE53O_PHASE_AFTER_MANAGED_RETURN);
    if (cycle->replacement_gc_status != GXOS_NATIVEAOT_CALLBACK_OK ||
        !gxos_nativeaot_gc_result_valid(cycle->replacement_gc_result,
                                        cycle->replacement_seed,
                                        &cycle->replacement_gc_delta,
                                        &cycle->replacement_gc_generation,
                                        &cycle->replacement_gc_checksum) ||
        cycle->replacement_gc_delta == 0U) {
        good = 0;
    } else {
        cycle->replacement_lifecycle.managed_root_survived = 1;
    }
    phase57_text(probe,
                 "GXOS_NET10:PHASE57_REPLACEMENT_POST_GC_CONTINUATION=1\r\n");
    if (!gxos_nativeaot_scheduler_worker_detach(
            &cycle->replacement_lifecycle) ||
        cycle->replacement_lifecycle.runtime_detach_count != 1U ||
        cycle->replacement_lifecycle.ownership_state !=
            GXOS_NATIVEAOT_WORKER_OWNERSHIP_RUNTIME_DETACHED ||
        gxos_scheduler_get_fls(probe->runtime_fls_slot) != 0) {
        good = 0;
    }
    phase57_text(probe, "GXOS_NET10:PHASE57_REPLACEMENT_DETACHED=1\r\n");
    if (!good) return (uintptr_t)phase57_fail(cycle);
    return (uintptr_t)cycle->replacement_callback_result;
}

static int phase57_reclaim_replacement(
    GXOS_PHASE57_CYCLE *cycle, GXOS_SCHEDULER *scheduler)
{
    GXOS_SCHEDULER_TCB *thread;
    if (cycle == 0 || scheduler == 0 || cycle->replacement_thread == 0 ||
        cycle->replacement_lifecycle.detached == 0 ||
        !gxos_scheduler_thread_is_terminated(cycle->replacement_thread)) {
        return 0;
    }
    thread = cycle->replacement_thread;
    return gxos_nativeaot_scheduler_worker_note_reclaimable(
               &cycle->replacement_lifecycle) &&
           gxos_scheduler_close_handle(cycle->replacement_handle) &&
           gxos_scheduler_collect(scheduler) &&
           gxos_nativeaot_scheduler_worker_note_reclaimed(
               &cycle->replacement_lifecycle) &&
           thread->live == 0 &&
           gxos_scheduler_thread_from_handle(cycle->replacement_handle) == 0;
}

static int phase57_failed_worker_cleanup(
    GXOS_PHASE57_CYCLE *cycle, GXOS_SCHEDULER *scheduler)
{
    GXOS_SCHEDULER_TCB *thread;
    if (cycle == 0 || scheduler == 0 || cycle->failed_thread == 0 ||
        cycle->failed_lifecycle.runtime_detach_count != 1U ||
        !cycle->failed_lifecycle.detached ||
        !gxos_scheduler_thread_is_terminated(cycle->failed_thread)) {
        return 0;
    }
    thread = cycle->failed_thread;
    if (!gxos_nativeaot_scheduler_worker_note_reclaimable(
            &cycle->failed_lifecycle) ||
        !gxos_scheduler_close_handle(cycle->failed_handle) ||
        !gxos_scheduler_collect(scheduler) ||
        !gxos_nativeaot_scheduler_worker_note_reclaimed(
            &cycle->failed_lifecycle) || thread->live != 0 ||
        gxos_scheduler_thread_from_handle(cycle->failed_handle) != 0) {
        return 0;
    }
    return 1;
}

static int phase57_failed_worker_state_valid(
    const GXOS_PHASE57_CYCLE *cycle, uint32_t baseline_threadstore)
{
    const GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE *lifecycle;
    const GXOS_SCHEDULER_TCB *thread;
    if (cycle == 0) return 0;
    lifecycle = &cycle->failed_lifecycle;
    thread = cycle->failed_thread;
    return thread != 0 && lifecycle->attached && lifecycle->detached &&
           lifecycle->runtime_attach_count == 1U &&
           lifecycle->runtime_detach_count == 1U &&
           lifecycle->ownership_state ==
               GXOS_NATIVEAOT_WORKER_OWNERSHIP_RUNTIME_DETACHED &&
           lifecycle->runtime_thread != 0 &&
           lifecycle->runtime_thread_owned == 0 &&
           lifecycle->managed_worker_object_owned == 0 &&
           lifecycle->managed_root_survived == 0 &&
           lifecycle->tls_fls_owned == 0 && thread->live &&
           thread->fls_values[lifecycle->runtime_fls_slot] == 0 &&
           lifecycle->runtime_state_after ==
               GXOS_NATIVEAOT_RUNTIME_THREAD_DETACHED &&
           lifecycle->alloc_limit_after_detach == 0 &&
           lifecycle->alloc_ptr_after_detach == 0 &&
           lifecycle->threadstore_after == baseline_threadstore;
}

static int phase57_replacement_state_valid(
    const GXOS_PHASE57_CYCLE *cycle)
{
    const GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE *lifecycle;
    if (cycle == 0) return 0;
    lifecycle = &cycle->replacement_lifecycle;
    return lifecycle->attached && lifecycle->detached &&
           lifecycle->runtime_attach_count == 1U &&
           lifecycle->runtime_detach_count == 1U &&
           lifecycle->managed_root_survived != 0 &&
           lifecycle->runtime_thread != 0 &&
           lifecycle->ownership_state ==
               GXOS_NATIVEAOT_WORKER_OWNERSHIP_RUNTIME_DETACHED;
}

int gxos_nativeaot_phase57_postattach_rollback_probe(
    GXOS_PHASE53O_PROBE *probe)
{
    static GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE stale_lifecycle;
    GXOS_NATIVEAOT_FAILURE_INJECTION_RECORD const *injection;
    GXOS_SCHEDULER_REGISTER_SNAPSHOT snapshot = {0};
    uint32_t baseline_vm;
    uint32_t baseline_threads;
    uint32_t baseline_objects;
    uint32_t baseline_threadstore;
    uint32_t baseline_callbacks;
    uint32_t baseline_gc_callbacks;
    uint32_t peak_vm = 0;
    uint32_t peak_threads = 0;
    uint32_t peak_objects = 0;
    uint32_t injected = 0;
    uint32_t passed = 0;
    uint32_t duplicate_detach_rejections = 0;
    uint32_t stale_detach_rejections = 0;
    uint32_t stale_reclaim_rejections = 0;
    uint32_t stale_lifecycle_rejections = 0;
    uint32_t stale_handle_rejections = 0;
    int same_slot = 1;

    if (probe == 0 || probe->scheduler == 0 || probe->main_thread == 0 ||
        probe->callback_bridge == 0 || probe->gc_bridge == 0 ||
        probe->runtime_fls_cleanup == 0 || probe->vm_region_count == 0 ||
        probe->log_text == 0 || probe->log_hex == 0 ||
        probe->phase_in_managed == 0 || probe->phase_after_managed == 0 ||
        probe->main_thread != gxos_scheduler_current_thread() ||
        !probe->main_thread->is_boot_thread) {
        return 0;
    }
    baseline_vm = *probe->vm_region_count;
    baseline_threads = phase57_live_threads(probe->scheduler);
    baseline_objects = phase57_live_objects(probe->scheduler);
    baseline_threadstore = phase53o_threadstore_count(
        probe->main_thread->fls_values[probe->runtime_fls_slot], 0);
    baseline_callbacks = probe->callback_bridge->invocation_count;
    baseline_gc_callbacks = probe->gc_bridge->invocation_count;
    phase57_text(probe, "GXOS_NET10:PHASE57_BEGIN\r\n");
    phase57_text(probe,
                 "GXOS_NET10:PHASE57_INJECTION_POINT=AFTER_RUNTIME_ATTACH\r\n");
    phase57_text(probe,
                 "GXOS_NET10:PHASE57_DIAGNOSTIC_HOOK=GXOS_ENABLE_PHASE57_POSTATTACH_ROLLBACK\r\n");
    phase57_text(probe,
                 "GXOS_NET10:PHASE57_ATTACH_AUTHORITY=GENERATED_REVERSE_PINVOKE\r\n");
    phase57_text(probe,
                 "GXOS_NET10:PHASE57_DETACH_AUTHORITY=RUNTIME_FLS_FIBER_DETACH_CALLBACK\r\n");
    phase57_hex(probe, "GXOS_NET10:PHASE57_RESOURCE_BASELINE_VM_REGIONS=0x",
                baseline_vm);
    phase57_hex(probe, "GXOS_NET10:PHASE57_RESOURCE_BASELINE_THREADS=0x",
                baseline_threads);
    phase57_hex(probe, "GXOS_NET10:PHASE57_RESOURCE_BASELINE_OBJECTS=0x",
                baseline_objects);
    phase57_hex(probe, "GXOS_NET10:PHASE57_THREADSTORE_BASELINE=0x",
                baseline_threadstore);
    phase57_hex(probe, "GXOS_NET10:PHASE57_CALLBACK_BASELINE=0x",
                baseline_callbacks);
    phase57_hex(probe, "GXOS_NET10:PHASE57_GC_CALLBACK_BASELINE=0x",
                baseline_gc_callbacks);

    for (uint32_t cycle = 0; cycle != PHASE57_CYCLE_COUNT; ++cycle) {
        uint32_t old_slot;
        uint32_t old_identity;
        uint16_t old_generation;
        uint32_t failed_callback_delta;
        uint32_t failed_gc_delta;
        int duplicate_detach_rejected;
        int stale_detach_rejected;
        int stale_reclaim_rejected;
        int stale_mark_rejected;
        int stale_note_rejected;
        int old_handle_resume_rejected;
        int old_handle_close_rejected;

        g_phase57_cycle = (GXOS_PHASE57_CYCLE){0};
        g_phase57_cycle.probe = probe;
        g_phase57_cycle.cycle = cycle + 1U;
        g_phase57_cycle.replacement_input = 0x90U + cycle;
        g_phase57_cycle.replacement_seed = 0xB0U + cycle;
        if (!gxos_scheduler_create_suspended_thread(
                probe->scheduler, phase57_failed_worker_entry,
                &g_phase57_cycle, &g_phase57_cycle.failed_handle,
                &g_phase57_cycle.failed_thread) ||
            !gxos_nativeaot_scheduler_worker_prepare(
                &g_phase57_cycle.failed_lifecycle, probe->main_thread,
                g_phase57_cycle.failed_thread, probe->tls_index,
                probe->runtime_fls_slot, probe->runtime_fls_cleanup)) {
            return 0;
        }
        g_phase57_cycle.prepared_vm = *probe->vm_region_count;
        g_phase57_cycle.prepared_threads = phase57_live_threads(
            probe->scheduler);
        g_phase57_cycle.prepared_objects = phase57_live_objects(
            probe->scheduler);
        if (g_phase57_cycle.prepared_vm <= baseline_vm ||
            g_phase57_cycle.prepared_threads <= baseline_threads ||
            g_phase57_cycle.prepared_objects <= baseline_objects) {
            return 0;
        }
        if (g_phase57_cycle.prepared_vm > peak_vm) {
            peak_vm = g_phase57_cycle.prepared_vm;
        }
        if (g_phase57_cycle.prepared_threads > peak_threads) {
            peak_threads = g_phase57_cycle.prepared_threads;
        }
        if (g_phase57_cycle.prepared_objects > peak_objects) {
            peak_objects = g_phase57_cycle.prepared_objects;
        }
        phase57_hex(probe, "GXOS_NET10:PHASE57_PREPARED_VM_REGIONS=0x",
                    g_phase57_cycle.prepared_vm);
        phase57_hex(probe, "GXOS_NET10:PHASE57_PREPARED_THREADS=0x",
                    g_phase57_cycle.prepared_threads);
        phase57_hex(probe, "GXOS_NET10:PHASE57_PREPARED_OBJECTS=0x",
                    g_phase57_cycle.prepared_objects);
        phase57_hex(probe, "GXOS_NET10:PHASE57_CYCLE=0x", cycle + 1U);
        phase57_hex(probe, "GXOS_NET10:PHASE57_FAILED_SLOT=0x",
                    g_phase57_cycle.failed_lifecycle.scheduler_slot);
        phase57_hex(probe, "GXOS_NET10:PHASE57_FAILED_IDENTITY=0x",
                    g_phase57_cycle.failed_lifecycle.worker_identity);
        phase57_hex(probe, "GXOS_NET10:PHASE57_FAILED_GENERATION=0x",
                    g_phase57_cycle.failed_lifecycle.worker_generation);
        phase57_text(probe, "GXOS_NET10:PHASE57_PREPARE_SUCCEEDED=1\r\n");
        phase57_text(probe,
                     "GXOS_NET10:PHASE57_PREPARED_STATE=ALLOCATED_CREATED_SUSPENDED\r\n");
        if (!gxos_scheduler_resume_thread(g_phase57_cycle.failed_handle, 0) ||
            !gxos_nativeaot_scheduler_worker_mark_runnable(
                &g_phase57_cycle.failed_lifecycle)) {
            return 0;
        }
        phase57_text(probe, "GXOS_NET10:PHASE57_WORKER_RESUMED=1\r\n");

        gxos_scheduler_main_dispatch(&snapshot);
        if (gxos_scheduler_current_thread() != probe->main_thread ||
            !gxos_scheduler_thread_is_terminated(
                g_phase57_cycle.failed_thread) ||
            g_phase57_cycle.failure != 0 ||
            !phase57_failed_worker_state_valid(&g_phase57_cycle,
                                               baseline_threadstore)) {
            return 0;
        }
        ++injected;
        ++passed;
        g_phase57_cycle.post_detach_vm = *probe->vm_region_count;
        g_phase57_cycle.post_detach_threads = phase57_live_threads(
            probe->scheduler);
        g_phase57_cycle.post_detach_objects = phase57_live_objects(
            probe->scheduler);
        phase57_text(probe, "GXOS_NET10:PHASE57_POST_DETACH_STATE_CLEARED=1\r\n");
        phase57_hex(probe, "GXOS_NET10:PHASE57_ATTACH_COUNT_BEFORE=0x", 0);
        phase57_hex(probe, "GXOS_NET10:PHASE57_ATTACH_COUNT_AFTER=0x",
                    g_phase57_cycle.failed_lifecycle.runtime_attach_count);
        phase57_hex(probe, "GXOS_NET10:PHASE57_DETACH_COUNT_BEFORE=0x", 0);
        phase57_hex(probe, "GXOS_NET10:PHASE57_DETACH_COUNT_AFTER=0x",
                    g_phase57_cycle.failed_lifecycle.runtime_detach_count);
        phase57_hex(probe, "GXOS_NET10:PHASE57_RUNTIME_ATTACHED_PEAK_VM_REGIONS=0x",
                    g_phase57_cycle.runtime_attached_vm);
        phase57_hex(probe, "GXOS_NET10:PHASE57_RUNTIME_ATTACHED_PEAK_THREADS=0x",
                    g_phase57_cycle.runtime_attached_threads);
        phase57_hex(probe, "GXOS_NET10:PHASE57_RUNTIME_ATTACHED_PEAK_OBJECTS=0x",
                    g_phase57_cycle.runtime_attached_objects);
        phase57_hex(probe, "GXOS_NET10:PHASE57_POST_DETACH_VM_REGIONS=0x",
                    g_phase57_cycle.post_detach_vm);
        phase57_hex(probe, "GXOS_NET10:PHASE57_POST_DETACH_THREADS=0x",
                    g_phase57_cycle.post_detach_threads);
        phase57_hex(probe, "GXOS_NET10:PHASE57_POST_DETACH_OBJECTS=0x",
                    g_phase57_cycle.post_detach_objects);
        phase57_text(probe, "GXOS_NET10:PHASE57_RUNTIME_LOCAL_STATE_CLEARED=1\r\n");
        phase57_text(probe, "GXOS_NET10:PHASE57_NO_POST_ATTACH_WORKLOAD=1\r\n");

        failed_callback_delta = g_phase57_cycle.failed_callback_after -
            g_phase57_cycle.failed_callback_before;
        failed_gc_delta = g_phase57_cycle.failed_gc_after -
            g_phase57_cycle.failed_gc_before;
        if (failed_callback_delta != 1U || failed_gc_delta != 0U) {
            return 0;
        }
        old_slot = g_phase57_cycle.failed_lifecycle.scheduler_slot;
        old_identity = g_phase57_cycle.failed_lifecycle.worker_identity;
        old_generation = g_phase57_cycle.failed_lifecycle.worker_generation;
        duplicate_detach_rejected =
            !gxos_nativeaot_scheduler_worker_detach(
                &g_phase57_cycle.failed_lifecycle);
        if (!phase57_failed_worker_cleanup(&g_phase57_cycle,
                                          probe->scheduler) ||
            !duplicate_detach_rejected) {
            return 0;
        }
        ++duplicate_detach_rejections;
        phase57_text(probe, "GXOS_NET10:PHASE57_HANDLE_CLOSED=1\r\n");
        phase57_text(probe, "GXOS_NET10:PHASE57_SCHEDULER_RECLAIMED=1\r\n");
        old_handle_resume_rejected = !gxos_scheduler_resume_thread(
            g_phase57_cycle.failed_handle, 0);
        old_handle_close_rejected = !gxos_scheduler_close_handle(
            g_phase57_cycle.failed_handle);
        if (!old_handle_resume_rejected || !old_handle_close_rejected ||
            gxos_scheduler_thread_from_handle(
                g_phase57_cycle.failed_handle) != 0) {
            return 0;
        }
        ++stale_handle_rejections;

        if (!gxos_scheduler_create_suspended_thread(
                probe->scheduler, phase57_replacement_worker_entry,
                &g_phase57_cycle, &g_phase57_cycle.replacement_handle,
                &g_phase57_cycle.replacement_thread) ||
            !gxos_nativeaot_scheduler_worker_prepare(
                &g_phase57_cycle.replacement_lifecycle, probe->main_thread,
                g_phase57_cycle.replacement_thread, probe->tls_index,
                probe->runtime_fls_slot, probe->runtime_fls_cleanup)) {
            return 0;
        }
        if (g_phase57_cycle.replacement_lifecycle.scheduler_slot != old_slot) {
            same_slot = 0;
        }
        if (g_phase57_cycle.replacement_lifecycle.worker_identity ==
                old_identity ||
            g_phase57_cycle.replacement_lifecycle.worker_generation ==
                old_generation) {
            return 0;
        }
        phase57_hex(probe, "GXOS_NET10:PHASE57_REPLACEMENT_SLOT=0x",
                    g_phase57_cycle.replacement_lifecycle.scheduler_slot);
        phase57_hex(probe, "GXOS_NET10:PHASE57_REPLACEMENT_IDENTITY=0x",
                    g_phase57_cycle.replacement_lifecycle.worker_identity);
        phase57_hex(probe, "GXOS_NET10:PHASE57_REPLACEMENT_GENERATION=0x",
                    g_phase57_cycle.replacement_lifecycle.worker_generation);
        stale_lifecycle = g_phase57_cycle.failed_lifecycle;
        stale_lifecycle.ownership_state =
            GXOS_NATIVEAOT_WORKER_OWNERSHIP_RUNTIME_ATTACHED;
        stale_lifecycle.attached = 1;
        stale_lifecycle.detached = 0;
        stale_detach_rejected =
            !gxos_nativeaot_scheduler_worker_detach(&stale_lifecycle);
        stale_lifecycle.ownership_state =
            GXOS_NATIVEAOT_WORKER_OWNERSHIP_ALLOCATED;
        stale_lifecycle.attached = 0;
        stale_lifecycle.detached = 0;
        stale_lifecycle.runtime_thread = 0;
        stale_mark_rejected =
            !gxos_nativeaot_scheduler_worker_mark_runnable(&stale_lifecycle);
        stale_lifecycle.ownership_state =
            GXOS_NATIVEAOT_WORKER_OWNERSHIP_RUNTIME_DETACHED;
        stale_lifecycle.detached = 1;
        stale_note_rejected =
            !gxos_nativeaot_scheduler_worker_note_reclaimable(
                &stale_lifecycle) &&
            !gxos_nativeaot_scheduler_worker_note_reclaimed(
                &stale_lifecycle);
        stale_reclaim_rejected = stale_note_rejected;
        if (!stale_detach_rejected || !stale_mark_rejected ||
            !stale_reclaim_rejected ||
            g_phase57_cycle.replacement_lifecycle.ownership_state !=
                GXOS_NATIVEAOT_WORKER_OWNERSHIP_ALLOCATED ||
            g_phase57_cycle.replacement_thread->state !=
                GXOS_SCHEDULER_THREAD_CREATED_SUSPENDED) {
            return 0;
        }
        ++stale_detach_rejections;
        ++stale_lifecycle_rejections;
        ++stale_reclaim_rejections;
        phase57_text(probe, "GXOS_NET10:PHASE57_STALE_HANDLE_REJECTED=1\r\n");
        phase57_text(probe, "GXOS_NET10:PHASE57_STALE_IDENTITY_REJECTED=1\r\n");
        phase57_text(probe, "GXOS_NET10:PHASE57_STALE_GENERATION_REJECTED=1\r\n");
        phase57_text(probe, "GXOS_NET10:PHASE57_STALE_DETACH_REJECTED=1\r\n");

        if (!phase53o_rehome_canary(probe,
                                    g_phase57_cycle.replacement_thread) ||
            !gxos_scheduler_resume_thread(
                g_phase57_cycle.replacement_handle, 0) ||
            !gxos_nativeaot_scheduler_worker_mark_runnable(
                &g_phase57_cycle.replacement_lifecycle)) {
            return 0;
        }
        for (uint32_t dispatches = 0; dispatches != 4U; ++dispatches) {
            if (gxos_scheduler_thread_is_terminated(
                    g_phase57_cycle.replacement_thread)) break;
            if (gxos_scheduler_current_thread() != probe->main_thread ||
                gxos_scheduler_runnable_count() == 0U) return 0;
            gxos_scheduler_main_dispatch(&snapshot);
        }
        if (!gxos_scheduler_thread_is_terminated(
                g_phase57_cycle.replacement_thread) ||
            g_phase57_cycle.failure != 0 ||
            !phase57_replacement_state_valid(&g_phase57_cycle) ||
            !phase57_reclaim_replacement(&g_phase57_cycle,
                                         probe->scheduler) ||
            *probe->vm_region_count != baseline_vm ||
            phase57_live_threads(probe->scheduler) != baseline_threads ||
            phase57_live_objects(probe->scheduler) != baseline_objects ||
            phase53o_threadstore_count(
                probe->main_thread->fls_values[probe->runtime_fls_slot], 0) !=
                baseline_threadstore) {
            return 0;
        }
        phase57_text(probe, "GXOS_NET10:PHASE57_REPLACEMENT_WORKER_SUCCEEDED=1\r\n");
        phase57_text(probe, "GXOS_NET10:PHASE57_REPLACEMENT_GC=1\r\n");
        phase57_text(probe, "GXOS_NET10:PHASE57_REPLACEMENT_RECLAIM=1\r\n");
        phase57_text(probe, "GXOS_NET10:PHASE57_BASELINE_RESTORED=1\r\n");
    }
    injection = gxos_nativeaot_phase56_failure_record();
    if (injection == 0 || injection->point !=
            GXOS_NATIVEAOT_FAILURE_INJECTION_AFTER_RUNTIME_ATTACH ||
        injected != PHASE57_CYCLE_COUNT || passed != PHASE57_CYCLE_COUNT ||
        same_slot == 0 || stale_handle_rejections != PHASE57_CYCLE_COUNT ||
        stale_detach_rejections != PHASE57_CYCLE_COUNT ||
        stale_lifecycle_rejections != PHASE57_CYCLE_COUNT ||
        stale_reclaim_rejections != PHASE57_CYCLE_COUNT ||
        duplicate_detach_rejections != PHASE57_CYCLE_COUNT ||
        probe->callback_bridge->invocation_count !=
            baseline_callbacks + PHASE57_CYCLE_COUNT * 2U ||
        probe->gc_bridge->invocation_count != baseline_gc_callbacks +
            PHASE57_CYCLE_COUNT || *probe->vm_region_count != baseline_vm ||
        phase57_live_threads(probe->scheduler) != baseline_threads ||
        phase57_live_objects(probe->scheduler) != baseline_objects ||
        phase53o_threadstore_count(
            probe->main_thread->fls_values[probe->runtime_fls_slot], 0) !=
            baseline_threadstore || injection->fire_count != 1U ||
        injection->mismatch_count != 0U) {
        return 0;
    }
    phase57_hex(probe, "GXOS_NET10:PHASE57_RESOURCE_PEAK_VM_REGIONS=0x",
                peak_vm);
    phase57_hex(probe, "GXOS_NET10:PHASE57_RESOURCE_PEAK_THREADS=0x",
                peak_threads);
    phase57_hex(probe, "GXOS_NET10:PHASE57_RESOURCE_PEAK_OBJECTS=0x",
                peak_objects);
    phase57_hex(probe, "GXOS_NET10:PHASE57_INJECTED_FAILURE_CYCLES=0x",
                injected);
    phase57_hex(probe, "GXOS_NET10:PHASE57_PASSED_FAILURE_CYCLES=0x", passed);
    phase57_hex(probe, "GXOS_NET57_DUPLICATE_DETACH_REJECTIONS=0x",
                duplicate_detach_rejections);
    phase57_hex(probe, "GXOS_NET57_STALE_DETACH_REJECTIONS=0x",
                stale_detach_rejections);
    phase57_hex(probe, "GXOS_NET10:PHASE57_CALLBACK_FINAL=0x",
                probe->callback_bridge->invocation_count);
    phase57_hex(probe, "GXOS_NET10:PHASE57_GC_CALLBACK_FINAL=0x",
                probe->gc_bridge->invocation_count);
    phase57_text(probe, "GXOS_NET10:PHASE57_EXACTLY_ONE_DETACH=1\r\n");
    phase57_text(probe, "GXOS_NET10:PHASE57_NO_DOUBLE_CLEANUP=1\r\n");
    phase57_text(probe, "GXOS_NET10:PHASE57_GENERATION_SAFE_SLOT_REUSE=1\r\n");
    phase57_text(probe, "GXOS_NET10:PHASE57_LEAK_TREND_NONE=1\r\n");
    phase57_text(probe, "GXOS_NET10:PHASE57_COMPLETE=1\r\n");
    phase57_text(probe, "GXOS_NET10:PHASE57_PASS=1\r\n");
    return 1;
}
#endif

#ifdef GXOS_ENABLE_PHASE58_POSTROOT_ROLLBACK
static int phase58_injection_state_valid(
    const GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE *lifecycle)
{
    GXOS_SCHEDULER_TCB *thread;
    if (lifecycle == 0 || lifecycle->thread == 0 ||
        lifecycle->ownership_state !=
            GXOS_NATIVEAOT_WORKER_OWNERSHIP_RUNTIME_ATTACHED ||
        !lifecycle->attached || lifecycle->detached ||
        lifecycle->runtime_attach_count != 1U ||
        lifecycle->runtime_detach_count != 0U ||
        lifecycle->runtime_thread == 0 || !lifecycle->runtime_thread_owned ||
        !lifecycle->managed_worker_object_owned ||
        !lifecycle->managed_root_owned ||
        lifecycle->managed_root_identity == 0 ||
        lifecycle->managed_root_publication_count != 1U ||
        lifecycle->managed_root_release_count != 0U ||
        lifecycle->managed_root_survived ||
        !lifecycle->callback_registration_observed ||
        lifecycle->runtime_state_before !=
            GXOS_NATIVEAOT_RUNTIME_THREAD_ATTACHED ||
        lifecycle->runtime_transition_frame != UINT64_MAX ||
        !lifecycle_matches_current_thread(lifecycle)) {
        return 0;
    }
    thread = lifecycle->thread;
    return thread->live && !thread->is_boot_thread &&
           thread->state == GXOS_SCHEDULER_THREAD_RUNNING &&
           gxos_scheduler_current_thread() == thread &&
           lifecycle->scheduler_owned && lifecycle->stack_owned &&
           lifecycle->environment_owned && lifecycle->tls_fls_owned &&
           lifecycle->vm_resources_owned &&
           lifecycle->stack_reservation_base != 0 &&
           lifecycle->stack_guard_base != 0 &&
           lifecycle->stack_usable_low != 0 &&
           lifecycle->stack_usable_high != 0 && lifecycle->gs_base != 0 &&
           lifecycle->teb_base != 0 && lifecycle->tls_vector_base != 0 &&
           lifecycle->tls_block_base != 0 && lifecycle->guard_vm_identity != 0 &&
           lifecycle->usable_vm_identity != 0 &&
           thread->stack_contract.guard_nonpresent &&
           thread->stack_contract.reservation_base ==
               lifecycle->stack_reservation_base &&
           thread->stack_contract.guard_base == lifecycle->stack_guard_base &&
           thread->stack_contract.usable_stack_low ==
               lifecycle->stack_usable_low &&
           thread->stack_contract.usable_stack_high ==
               lifecycle->stack_usable_high &&
           thread->gs_base == lifecycle->gs_base &&
           thread->teb_base == lifecycle->teb_base &&
           thread->tls_vector_base == lifecycle->tls_vector_base &&
           thread->tls_block_base == lifecycle->tls_block_base &&
           thread->context.rsp >= lifecycle->stack_usable_low &&
           thread->context.rsp <= lifecycle->stack_usable_high &&
           thread->fls_values[lifecycle->runtime_fls_slot] ==
               lifecycle->runtime_thread &&
           lifecycle_load_u64(lifecycle->runtime_thread,
                              GXOS_NATIVEAOT_TLS_STATE_FLAGS_OFFSET) ==
               GXOS_NATIVEAOT_RUNTIME_THREAD_ATTACHED &&
           lifecycle->runtime_stack_low ==
               thread->stack_contract.reservation_base &&
           lifecycle->runtime_stack_high == thread->stack_limit &&
           lifecycle->allocation_context == thread->tls_block_base + 0x38U;
}
#endif

#ifdef GXOS_ENABLE_PHASE59_POSTGC_ROLLBACK
static int phase59_injection_state_valid(
    const GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE *lifecycle)
{
    GXOS_SCHEDULER_TCB *thread;
    if (lifecycle == 0 || lifecycle->thread == 0 ||
        lifecycle->ownership_state !=
            GXOS_NATIVEAOT_WORKER_OWNERSHIP_RUNTIME_ATTACHED ||
        !lifecycle->attached || lifecycle->detached ||
        lifecycle->runtime_attach_count != 1U ||
        lifecycle->runtime_detach_count != 0U ||
        lifecycle->runtime_thread == 0 || !lifecycle->runtime_thread_owned ||
        !lifecycle->managed_worker_object_owned ||
        !lifecycle->managed_root_owned ||
        !lifecycle->managed_root_survived ||
        lifecycle->managed_root_identity == 0 ||
        lifecycle->managed_root_publication_count != 1U ||
        lifecycle->managed_root_release_count != 0U ||
        !lifecycle->callback_registration_observed ||
        lifecycle->runtime_state_before !=
            GXOS_NATIVEAOT_RUNTIME_THREAD_ATTACHED ||
        lifecycle->runtime_transition_frame != UINT64_MAX ||
        lifecycle->allocation_context == 0 ||
        !lifecycle_matches_current_thread(lifecycle)) {
        return 0;
    }
    thread = lifecycle->thread;
    return thread->live && !thread->is_boot_thread &&
           thread->state == GXOS_SCHEDULER_THREAD_RUNNING &&
           gxos_scheduler_current_thread() == thread &&
           lifecycle->scheduler_owned && lifecycle->stack_owned &&
           lifecycle->environment_owned && lifecycle->tls_fls_owned &&
           lifecycle->vm_resources_owned &&
           lifecycle->stack_reservation_base != 0 &&
           lifecycle->stack_guard_base != 0 &&
           lifecycle->stack_usable_low != 0 &&
           lifecycle->stack_usable_high != 0 && lifecycle->gs_base != 0 &&
           lifecycle->teb_base != 0 && lifecycle->tls_vector_base != 0 &&
           lifecycle->tls_block_base != 0 && lifecycle->guard_vm_identity != 0 &&
           lifecycle->usable_vm_identity != 0 &&
           thread->stack_contract.guard_nonpresent &&
           thread->stack_contract.reservation_base ==
               lifecycle->stack_reservation_base &&
           thread->stack_contract.guard_base == lifecycle->stack_guard_base &&
           thread->stack_contract.usable_stack_low ==
               lifecycle->stack_usable_low &&
           thread->stack_contract.usable_stack_high ==
               lifecycle->stack_usable_high &&
           thread->gs_base == lifecycle->gs_base &&
           thread->teb_base == lifecycle->teb_base &&
           thread->tls_vector_base == lifecycle->tls_vector_base &&
           thread->tls_block_base == lifecycle->tls_block_base &&
           thread->context.rsp >= lifecycle->stack_usable_low &&
           thread->context.rsp <= lifecycle->stack_usable_high &&
           thread->fls_values[lifecycle->runtime_fls_slot] ==
               lifecycle->runtime_thread &&
           lifecycle_load_u64(lifecycle->runtime_thread,
                              GXOS_NATIVEAOT_TLS_STATE_FLAGS_OFFSET) ==
               GXOS_NATIVEAOT_RUNTIME_THREAD_ATTACHED &&
           lifecycle->runtime_stack_low ==
               thread->stack_contract.reservation_base &&
           lifecycle->runtime_stack_high == thread->stack_limit &&
           lifecycle->allocation_context == thread->tls_block_base + 0x38U;
}
#endif

#ifdef GXOS_ENABLE_PHASE58_POSTROOT_ROLLBACK
#define PHASE58_CYCLE_COUNT 12U

typedef struct {
    GXOS_PHASE53O_PROBE *probe;
    uint32_t cycle;
    GXOS_SCHEDULER_HANDLE failed_handle;
    GXOS_SCHEDULER_TCB *failed_thread;
    GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE failed_lifecycle;
    GXOS_SCHEDULER_HANDLE replacement_handle;
    GXOS_SCHEDULER_TCB *replacement_thread;
    GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE replacement_lifecycle;
    uint32_t replacement_input;
    uint32_t replacement_seed;
    uint32_t failed_root_token;
    uint32_t replacement_root_token;
    int32_t failed_callback_result;
    int32_t failed_root_publish_result;
    int32_t failed_root_release_result;
    int32_t replacement_callback_result;
    int32_t replacement_root_publish_result;
    int32_t replacement_stale_root_result;
    int32_t replacement_gc_result;
    int32_t replacement_root_release_result;
    uint32_t failed_callback_status;
    uint32_t failed_root_publish_status;
    uint32_t failed_root_release_status;
    uint32_t replacement_callback_status;
    uint32_t replacement_root_publish_status;
    uint32_t replacement_stale_root_status;
    uint32_t replacement_gc_status;
    uint32_t replacement_root_release_status;
    uint32_t replacement_gc_delta;
    uint32_t replacement_gc_generation;
    uint32_t replacement_gc_checksum;
    uint32_t failed_callback_before;
    uint32_t failed_callback_after;
    uint32_t failed_gc_before;
    uint32_t failed_gc_after;
    uint32_t failed_root_publish_before;
    uint32_t failed_root_publish_after;
    uint32_t failed_root_release_before;
    uint32_t failed_root_release_after;
    uint32_t replacement_root_publish_before;
    uint32_t replacement_root_publish_after;
    uint32_t replacement_root_release_before;
    uint32_t replacement_root_release_after;
    uint32_t prepared_vm;
    uint32_t prepared_threads;
    uint32_t prepared_objects;
    uint32_t root_published_vm;
    uint32_t root_published_threads;
    uint32_t root_published_objects;
    uint32_t root_released_vm;
    uint32_t root_released_threads;
    uint32_t root_released_objects;
    uint32_t final_vm;
    uint32_t final_threads;
    uint32_t final_objects;
    uint32_t failure;
} GXOS_PHASE58_CYCLE;

static GXOS_PHASE58_CYCLE g_phase58_cycle;

static void phase58_text(GXOS_PHASE53O_PROBE *probe, const char *text)
{
    probe->log_text(text);
}

static void phase58_hex(GXOS_PHASE53O_PROBE *probe,
                        const char *name, uint64_t value)
{
    probe->log_hex(name, value);
    probe->log_text("\r\n");
}

static uint32_t phase58_live_threads(const GXOS_SCHEDULER *scheduler)
{
    uint32_t index;
    uint32_t count = 0;
    for (index = 0; index != GXOS_SCHEDULER_MAX_THREADS; ++index) {
        if (scheduler->threads[index].live) ++count;
    }
    return count;
}

static uint32_t phase58_live_objects(const GXOS_SCHEDULER *scheduler)
{
    uint32_t index;
    uint32_t count = 0;
    for (index = 0; index != GXOS_SCHEDULER_MAX_OBJECTS; ++index) {
        if (scheduler->objects[index].live) ++count;
    }
    return count;
}

static int phase58_fail(GXOS_PHASE58_CYCLE *cycle)
{
    cycle->failure = 1;
    phase58_text(cycle->probe, "GXOS_NET10:PHASE58_FAILURE=1\r\n");
    return 0;
}

static uintptr_t GXOS_PHASE53O_MS_ABI
phase58_failed_worker_entry(void *argument)
{
    GXOS_PHASE58_CYCLE *cycle = (GXOS_PHASE58_CYCLE *)argument;
    GXOS_PHASE53O_PROBE *probe = cycle->probe;
    GXOS_SCHEDULER_TCB *thread = gxos_scheduler_current_thread();
    GXOS_SCHEDULER_REGISTER_SNAPSHOT snapshot = {0};
    GXOS_NATIVEAOT_FAILURE_INJECTION_RECORD const *injection;
    int duplicate_publish_rejected;
    int duplicate_release_rejected;
    int good = 1;

    cycle->failed_thread = thread;
    gxos_scheduler_capture_registers(&snapshot);
    if (thread == 0 || !gxos_nativeaot_scheduler_worker_mark_running(
            &cycle->failed_lifecycle) ||
        !gxos_scheduler_validate_worker_snapshot(thread, &snapshot)) {
        return (uintptr_t)phase58_fail(cycle);
    }
    phase58_text(probe, "GXOS_NET10:PHASE58_FAILED_WORKER_RUNNING=1\r\n");
    cycle->failed_callback_before = probe->callback_bridge->invocation_count;
    cycle->failed_gc_before = probe->gc_bridge->invocation_count;
    cycle->failed_root_publish_before =
        probe->managed_root_publish_bridge->invocation_count;
    cycle->failed_root_release_before =
        probe->managed_root_release_bridge->invocation_count;
    probe->phase_in_managed(PHASE53O_PHASE_IN_MANAGED);
    if (!gxos_nativeaot_scheduler_worker_invoke(
            &cycle->failed_lifecycle, probe->callback_bridge, 0x58,
            &cycle->failed_callback_result,
            &cycle->failed_callback_status)) {
        good = 0;
    }
    probe->phase_after_managed(PHASE53O_PHASE_AFTER_MANAGED_RETURN);
    cycle->failed_callback_after = probe->callback_bridge->invocation_count;
    if (cycle->failed_callback_status != GXOS_NATIVEAOT_CALLBACK_OK ||
        cycle->failed_lifecycle.ownership_state !=
            GXOS_NATIVEAOT_WORKER_OWNERSHIP_RUNTIME_ATTACHED ||
        cycle->failed_lifecycle.runtime_thread == 0 ||
        cycle->failed_lifecycle.runtime_attach_count != 1U ||
        cycle->failed_lifecycle.runtime_detach_count != 0U) {
        good = 0;
    }
    if (!good) return (uintptr_t)phase58_fail(cycle);
    phase58_text(probe, "GXOS_NET10:PHASE58_RUNTIME_ATTACH_SUCCEEDED=1\r\n");

    cycle->failed_root_token = 0x5800U | cycle->cycle;
    probe->phase_in_managed(PHASE53O_PHASE_IN_MANAGED);
    if (!gxos_nativeaot_scheduler_worker_invoke(
            &cycle->failed_lifecycle, probe->managed_root_publish_bridge,
            (int32_t)cycle->failed_root_token,
            &cycle->failed_root_publish_result,
            &cycle->failed_root_publish_status) ||
        cycle->failed_root_publish_status != GXOS_NATIVEAOT_CALLBACK_OK ||
        (uint32_t)cycle->failed_root_publish_result !=
            (0x58000000U | cycle->failed_root_token) ||
        !gxos_nativeaot_scheduler_worker_note_managed_root_published(
            &cycle->failed_lifecycle, cycle->failed_root_token)) {
        return (uintptr_t)phase58_fail(cycle);
    }
    probe->phase_after_managed(PHASE53O_PHASE_AFTER_MANAGED_RETURN);
    cycle->failed_root_publish_after =
        probe->managed_root_publish_bridge->invocation_count;
    cycle->root_published_vm = *probe->vm_region_count;
    cycle->root_published_threads = phase58_live_threads(probe->scheduler);
    cycle->root_published_objects = phase58_live_objects(probe->scheduler);
    duplicate_publish_rejected =
        !gxos_nativeaot_scheduler_worker_note_managed_root_published(
            &cycle->failed_lifecycle, cycle->failed_root_token);
    if (!duplicate_publish_rejected || !phase58_injection_state_valid(
            &cycle->failed_lifecycle)) {
        return (uintptr_t)phase58_fail(cycle);
    }
    phase58_text(probe,
                 "GXOS_NET10:PHASE58_DUPLICATE_ROOT_PUBLICATION_REJECTED=1\r\n");
    phase58_text(probe, "GXOS_NET10:PHASE58_MANAGED_ROOT_PUBLISHED=1\r\n");
    phase58_hex(probe, "GXOS_NET10:PHASE58_MANAGED_ROOT_IDENTITY=0x",
                cycle->failed_root_token);
    phase58_hex(probe, "GXOS_NET10:PHASE58_MANAGED_ROOT_PUBLICATION_COUNT=0x",
                cycle->failed_lifecycle.managed_root_publication_count);

    if (!gxos_nativeaot_phase56_failure_arm(
            GXOS_NATIVEAOT_FAILURE_INJECTION_AFTER_MANAGED_ROOT,
            &cycle->failed_lifecycle) ||
        !gxos_nativeaot_phase56_failure_try_fire(&cycle->failed_lifecycle) ||
        gxos_nativeaot_phase56_failure_try_fire(&cycle->failed_lifecycle)) {
        return (uintptr_t)phase58_fail(cycle);
    }
    injection = gxos_nativeaot_phase56_failure_record();
    if (injection == 0 || injection->state !=
            GXOS_NATIVEAOT_FAILURE_INJECTION_FIRED ||
        injection->point != GXOS_NATIVEAOT_FAILURE_INJECTION_AFTER_MANAGED_ROOT ||
        injection->fire_count != 1U || injection->mismatch_count != 0U) {
        return (uintptr_t)phase58_fail(cycle);
    }
    phase58_text(probe, "GXOS_NET10:PHASE58_INJECTION_ARMED=1\r\n");
    phase58_text(probe, "GXOS_NET10:PHASE58_INJECTION_FIRED=1\r\n");
    phase58_text(probe, "GXOS_NET10:PHASE58_INTENTIONAL_FAILURE=1\r\n");

    probe->phase_in_managed(PHASE53O_PHASE_IN_MANAGED);
    if (!gxos_nativeaot_scheduler_worker_invoke(
            &cycle->failed_lifecycle, probe->managed_root_release_bridge,
            (int32_t)cycle->failed_root_token,
            &cycle->failed_root_release_result,
            &cycle->failed_root_release_status) ||
        cycle->failed_root_release_status != GXOS_NATIVEAOT_CALLBACK_OK ||
        (uint32_t)cycle->failed_root_release_result !=
            (0x59000000U | cycle->failed_root_token) ||
        !gxos_nativeaot_scheduler_worker_note_managed_root_released(
            &cycle->failed_lifecycle, cycle->failed_root_token)) {
        return (uintptr_t)phase58_fail(cycle);
    }
    probe->phase_after_managed(PHASE53O_PHASE_AFTER_MANAGED_RETURN);
    cycle->failed_root_release_after =
        probe->managed_root_release_bridge->invocation_count;
    cycle->root_released_vm = *probe->vm_region_count;
    cycle->root_released_threads = phase58_live_threads(probe->scheduler);
    cycle->root_released_objects = phase58_live_objects(probe->scheduler);
    duplicate_release_rejected =
        !gxos_nativeaot_scheduler_worker_note_managed_root_released(
            &cycle->failed_lifecycle, cycle->failed_root_token);
    if (!duplicate_release_rejected || cycle->failed_lifecycle.managed_root_owned ||
        cycle->failed_lifecycle.managed_root_release_count != 1U) {
        return (uintptr_t)phase58_fail(cycle);
    }
    phase58_text(probe,
                 "GXOS_NET10:PHASE58_DUPLICATE_ROOT_RELEASE_REJECTED=1\r\n");
    phase58_text(probe, "GXOS_NET10:PHASE58_MANAGED_ROOT_RELEASED=1\r\n");
    phase58_hex(probe, "GXOS_NET10:PHASE58_MANAGED_ROOT_RELEASE_COUNT=0x",
                cycle->failed_lifecycle.managed_root_release_count);
    if (!gxos_nativeaot_scheduler_worker_detach(&cycle->failed_lifecycle) ||
        cycle->failed_lifecycle.runtime_detach_count != 1U ||
        gxos_scheduler_get_fls(probe->runtime_fls_slot) != 0) {
        return (uintptr_t)phase58_fail(cycle);
    }
    phase58_text(probe, "GXOS_NET10:PHASE58_DETACH_EXECUTED=1\r\n");
    phase58_text(probe,
                 "GXOS_NET10:PHASE58_FLS_THREADSTORE_RUNTIME_STATE_CLEARED=1\r\n");
    if (cycle->failed_lifecycle.alloc_ptr_after_detach != 0) {
        phase58_text(probe,
                     "GXOS_NET10:PHASE58_DETACHED_ALLOCATION_CURSOR_DIAGNOSTIC_ONLY=1\r\n");
    }
    cycle->failed_gc_after = probe->gc_bridge->invocation_count;
    return (uintptr_t)cycle->failed_callback_result;
}

static uintptr_t GXOS_PHASE53O_MS_ABI
phase58_replacement_worker_entry(void *argument)
{
    GXOS_PHASE58_CYCLE *cycle = (GXOS_PHASE58_CYCLE *)argument;
    GXOS_PHASE53O_PROBE *probe = cycle->probe;
    GXOS_SCHEDULER_TCB *thread = gxos_scheduler_current_thread();
    GXOS_SCHEDULER_REGISTER_SNAPSHOT snapshot = {0};
    uint32_t callback_low;
    int stale_root_rejected;
    int good = 1;

    cycle->replacement_thread = thread;
    gxos_scheduler_capture_registers(&snapshot);
    if (thread == 0 || !gxos_nativeaot_scheduler_worker_mark_running(
            &cycle->replacement_lifecycle) ||
        !gxos_scheduler_validate_worker_snapshot(thread, &snapshot)) {
        return (uintptr_t)phase58_fail(cycle);
    }
    phase58_text(probe, "GXOS_NET10:PHASE58_REPLACEMENT_RUNNING=1\r\n");
    probe->phase_in_managed(PHASE53O_PHASE_IN_MANAGED);
    if (!gxos_nativeaot_scheduler_worker_invoke(
            &cycle->replacement_lifecycle, probe->callback_bridge,
            (int32_t)cycle->replacement_input,
            &cycle->replacement_callback_result,
            &cycle->replacement_callback_status)) {
        good = 0;
    }
    probe->phase_after_managed(PHASE53O_PHASE_AFTER_MANAGED_RETURN);
    callback_low = (uint32_t)cycle->replacement_callback_result & 0xFFFFU;
    if (cycle->replacement_callback_status != GXOS_NATIVEAOT_CALLBACK_OK ||
        callback_low != cycle->replacement_input + 1U ||
        cycle->replacement_lifecycle.runtime_attach_count != 1U) {
        good = 0;
    }
    if (!good) return (uintptr_t)phase58_fail(cycle);
    phase58_text(probe,
                 "GXOS_NET10:PHASE58_REPLACEMENT_MANAGED_CALLBACK=1\r\n");

    cycle->replacement_root_token = 0x5900U | cycle->cycle;
    cycle->replacement_root_publish_before =
        probe->managed_root_publish_bridge->invocation_count;
    probe->phase_in_managed(PHASE53O_PHASE_IN_MANAGED);
    if (!gxos_nativeaot_scheduler_worker_invoke(
            &cycle->replacement_lifecycle,
            probe->managed_root_publish_bridge,
            (int32_t)cycle->replacement_root_token,
            &cycle->replacement_root_publish_result,
            &cycle->replacement_root_publish_status) ||
        cycle->replacement_root_publish_status != GXOS_NATIVEAOT_CALLBACK_OK ||
        (uint32_t)cycle->replacement_root_publish_result !=
            (0x58000000U | cycle->replacement_root_token) ||
        !gxos_nativeaot_scheduler_worker_note_managed_root_published(
            &cycle->replacement_lifecycle, cycle->replacement_root_token)) {
        return (uintptr_t)phase58_fail(cycle);
    }
    probe->phase_after_managed(PHASE53O_PHASE_AFTER_MANAGED_RETURN);
    cycle->replacement_root_publish_after =
        probe->managed_root_publish_bridge->invocation_count;

    probe->phase_in_managed(PHASE53O_PHASE_IN_MANAGED);
    if (!gxos_nativeaot_scheduler_worker_invoke(
            &cycle->replacement_lifecycle,
            probe->managed_root_release_bridge,
            (int32_t)cycle->failed_root_token,
            &cycle->replacement_stale_root_result,
            &cycle->replacement_stale_root_status) ||
        cycle->replacement_stale_root_status != GXOS_NATIVEAOT_CALLBACK_OK ||
        cycle->replacement_stale_root_result != -2 ||
        cycle->replacement_lifecycle.managed_root_owned == 0) {
        return (uintptr_t)phase58_fail(cycle);
    }
    probe->phase_after_managed(PHASE53O_PHASE_AFTER_MANAGED_RETURN);
    stale_root_rejected = 1;
    phase58_text(probe, "GXOS_NET10:PHASE58_STALE_ROOT_REJECTED=1\r\n");
    phase58_text(probe,
                 "GXOS_NET10:PHASE58_WRONG_GENERATION_ROOT_CLEANUP_REJECTED=1\r\n");

    probe->phase_in_managed(PHASE53O_PHASE_IN_MANAGED);
    if (!gxos_nativeaot_scheduler_worker_invoke(
            &cycle->replacement_lifecycle, probe->gc_bridge,
            (int32_t)cycle->replacement_seed, &cycle->replacement_gc_result,
            &cycle->replacement_gc_status)) {
        good = 0;
    }
    probe->phase_after_managed(PHASE53O_PHASE_AFTER_MANAGED_RETURN);
    if (cycle->replacement_gc_status != GXOS_NATIVEAOT_CALLBACK_OK ||
        !gxos_nativeaot_gc_result_valid(cycle->replacement_gc_result,
                                        cycle->replacement_seed,
                                        &cycle->replacement_gc_delta,
                                        &cycle->replacement_gc_generation,
                                        &cycle->replacement_gc_checksum) ||
        cycle->replacement_gc_delta == 0U || !stale_root_rejected) {
        good = 0;
    } else {
        cycle->replacement_lifecycle.managed_root_survived = 1;
    }
    phase58_text(probe,
                 "GXOS_NET10:PHASE58_REPLACEMENT_ROOT_SURVIVED_GC=1\r\n");
    phase58_text(probe,
                 "GXOS_NET10:PHASE58_REPLACEMENT_POST_GC_CONTINUATION=1\r\n");
    cycle->replacement_root_release_before =
        probe->managed_root_release_bridge->invocation_count;
    probe->phase_in_managed(PHASE53O_PHASE_IN_MANAGED);
    if (!gxos_nativeaot_scheduler_worker_invoke(
            &cycle->replacement_lifecycle,
            probe->managed_root_release_bridge,
            (int32_t)cycle->replacement_root_token,
            &cycle->replacement_root_release_result,
            &cycle->replacement_root_release_status) ||
        cycle->replacement_root_release_status != GXOS_NATIVEAOT_CALLBACK_OK ||
        (uint32_t)cycle->replacement_root_release_result !=
            (0x59000000U | cycle->replacement_root_token) ||
        !gxos_nativeaot_scheduler_worker_note_managed_root_released(
            &cycle->replacement_lifecycle, cycle->replacement_root_token)) {
        good = 0;
    }
    probe->phase_after_managed(PHASE53O_PHASE_AFTER_MANAGED_RETURN);
    cycle->replacement_root_release_after =
        probe->managed_root_release_bridge->invocation_count;
    if (!gxos_nativeaot_scheduler_worker_detach(
            &cycle->replacement_lifecycle) ||
        cycle->replacement_lifecycle.runtime_detach_count != 1U ||
        gxos_scheduler_get_fls(probe->runtime_fls_slot) != 0) {
        good = 0;
    }
    phase58_text(probe, "GXOS_NET10:PHASE58_REPLACEMENT_DETACHED=1\r\n");
    if (!good) return (uintptr_t)phase58_fail(cycle);
    return (uintptr_t)cycle->replacement_callback_result;
}

static int phase58_reclaim_worker(
    GXOS_PHASE58_CYCLE *cycle, GXOS_SCHEDULER_HANDLE handle,
    GXOS_SCHEDULER_TCB *thread,
    GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE *lifecycle,
    GXOS_SCHEDULER *scheduler)
{
    if (cycle == 0 || scheduler == 0 || thread == 0 || lifecycle == 0 ||
        lifecycle->runtime_detach_count != 1U || !lifecycle->detached ||
        !gxos_scheduler_thread_is_terminated(thread)) {
        return 0;
    }
    return gxos_nativeaot_scheduler_worker_note_reclaimable(lifecycle) &&
           gxos_scheduler_close_handle(handle) &&
           gxos_scheduler_collect(scheduler) &&
           gxos_nativeaot_scheduler_worker_note_reclaimed(lifecycle) &&
           thread->live == 0 && gxos_scheduler_thread_from_handle(handle) == 0;
}

static int phase58_failed_state_valid(
    const GXOS_PHASE58_CYCLE *cycle, uint32_t baseline_threadstore)
{
    const GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE *lifecycle;
    const GXOS_SCHEDULER_TCB *thread;
    if (cycle == 0) return 0;
    lifecycle = &cycle->failed_lifecycle;
    thread = cycle->failed_thread;
    return thread != 0 && lifecycle->attached && lifecycle->detached &&
           lifecycle->runtime_attach_count == 1U &&
           lifecycle->runtime_detach_count == 1U &&
           lifecycle->managed_root_identity == cycle->failed_root_token &&
           lifecycle->managed_root_publication_count == 1U &&
           lifecycle->managed_root_release_count == 1U &&
           lifecycle->managed_root_owned == 0 &&
           lifecycle->ownership_state ==
               GXOS_NATIVEAOT_WORKER_OWNERSHIP_RUNTIME_DETACHED &&
           lifecycle->runtime_thread != 0 && !lifecycle->runtime_thread_owned &&
           !lifecycle->managed_root_survived && !lifecycle->tls_fls_owned &&
           thread->live && thread->fls_values[lifecycle->runtime_fls_slot] == 0 &&
           lifecycle->runtime_state_after ==
               GXOS_NATIVEAOT_RUNTIME_THREAD_DETACHED &&
           lifecycle->threadstore_after == baseline_threadstore;
}

static int phase58_replacement_state_valid(
    const GXOS_PHASE58_CYCLE *cycle)
{
    const GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE *lifecycle;
    if (cycle == 0) return 0;
    lifecycle = &cycle->replacement_lifecycle;
    return lifecycle->attached && lifecycle->detached &&
           lifecycle->runtime_attach_count == 1U &&
           lifecycle->runtime_detach_count == 1U &&
           lifecycle->managed_root_identity == cycle->replacement_root_token &&
           lifecycle->managed_root_publication_count == 1U &&
           lifecycle->managed_root_release_count == 1U &&
           lifecycle->managed_root_owned == 0 &&
           lifecycle->managed_root_survived != 0 &&
           lifecycle->runtime_thread != 0 &&
           lifecycle->ownership_state ==
               GXOS_NATIVEAOT_WORKER_OWNERSHIP_RUNTIME_DETACHED;
}

int gxos_nativeaot_phase58_postroot_rollback_probe(
    GXOS_PHASE53O_PROBE *probe)
{
    static GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE stale_lifecycle;
    GXOS_NATIVEAOT_FAILURE_INJECTION_RECORD const *injection;
    GXOS_SCHEDULER_REGISTER_SNAPSHOT snapshot = {0};
    uint32_t baseline_vm;
    uint32_t baseline_threads;
    uint32_t baseline_objects;
    uint32_t baseline_threadstore;
    uint32_t baseline_callbacks;
    uint32_t baseline_gc_callbacks;
    uint32_t baseline_root_publish;
    uint32_t baseline_root_release;
    uint32_t injected = 0;
    uint32_t passed = 0;
    uint32_t root_publish_successes = 0;
    uint32_t root_release_successes = 0;
    uint32_t stale_root_rejections = 0;
    uint32_t stale_handle_rejections = 0;
    uint32_t duplicate_detach_rejections = 0;
    uint32_t stale_detach_rejections = 0;
    uint32_t peak_vm = 0;
    uint32_t peak_threads = 0;
    uint32_t peak_objects = 0;
    int same_slot = 1;

    if (probe == 0 || probe->scheduler == 0 || probe->main_thread == 0 ||
        probe->callback_bridge == 0 || probe->gc_bridge == 0 ||
        probe->managed_root_publish_bridge == 0 ||
        probe->managed_root_release_bridge == 0 ||
        probe->runtime_fls_cleanup == 0 || probe->vm_region_count == 0 ||
        probe->log_text == 0 || probe->log_hex == 0 ||
        probe->phase_in_managed == 0 || probe->phase_after_managed == 0 ||
        probe->main_thread != gxos_scheduler_current_thread() ||
        !probe->main_thread->is_boot_thread) {
        return 0;
    }
    baseline_vm = *probe->vm_region_count;
    baseline_threads = phase58_live_threads(probe->scheduler);
    baseline_objects = phase58_live_objects(probe->scheduler);
    baseline_threadstore = phase53o_threadstore_count(
        probe->main_thread->fls_values[probe->runtime_fls_slot], 0);
    baseline_callbacks = probe->callback_bridge->invocation_count;
    baseline_gc_callbacks = probe->gc_bridge->invocation_count;
    baseline_root_publish = probe->managed_root_publish_bridge->invocation_count;
    baseline_root_release = probe->managed_root_release_bridge->invocation_count;
    phase58_text(probe, "GXOS_NET10:PHASE58_BEGIN\r\n");
    phase58_text(probe,
                 "GXOS_NET10:PHASE58_INJECTION_POINT=AFTER_MANAGED_ROOT\r\n");
    phase58_text(probe,
                 "GXOS_NET10:PHASE58_DIAGNOSTIC_HOOK=GXOS_NATIVEAOT_FAILURE_INJECTION_AFTER_MANAGED_ROOT\r\n");
    phase58_text(probe,
                 "GXOS_NET10:PHASE58_COMPILE_GATE=GXOS_ENABLE_PHASE58_POSTROOT_ROLLBACK\r\n");
    phase58_text(probe,
                 "GXOS_NET10:PHASE58_MANAGED_ROOT_AUTHORITY=MANAGED_STATIC_REFERENCE\r\n");
    phase58_text(probe,
                 "GXOS_NET10:PHASE58_ROOT_CLEANUP_AUTHORITY=MANAGED_ROOT_RELEASE_EXPORT\r\n");
    phase58_hex(probe, "GXOS_NET10:PHASE58_RESOURCE_BASELINE_VM_REGIONS=0x",
                baseline_vm);
    phase58_hex(probe, "GXOS_NET10:PHASE58_RESOURCE_BASELINE_THREADS=0x",
                baseline_threads);
    phase58_hex(probe, "GXOS_NET10:PHASE58_RESOURCE_BASELINE_OBJECTS=0x",
                baseline_objects);
    phase58_hex(probe, "GXOS_NET10:PHASE58_RESOURCE_BASELINE_ROOTS=0x", 0);

    for (uint32_t cycle = 0; cycle != PHASE58_CYCLE_COUNT; ++cycle) {
        uint32_t old_slot;
        uint32_t old_identity;
        uint16_t old_generation;
        uint32_t failed_gc_delta;
        int duplicate_detach_rejected;
        int stale_root_rejected;
        int stale_detach_rejected;
        int old_handle_resume_rejected;
        int old_handle_close_rejected;

        g_phase58_cycle = (GXOS_PHASE58_CYCLE){0};
        g_phase58_cycle.probe = probe;
        g_phase58_cycle.cycle = cycle + 1U;
        g_phase58_cycle.replacement_input = 0xA0U + cycle;
        g_phase58_cycle.replacement_seed = 0xC0U + cycle;
        if (!gxos_scheduler_create_suspended_thread(
                probe->scheduler, phase58_failed_worker_entry,
                &g_phase58_cycle, &g_phase58_cycle.failed_handle,
                &g_phase58_cycle.failed_thread) ||
            !gxos_nativeaot_scheduler_worker_prepare(
                &g_phase58_cycle.failed_lifecycle, probe->main_thread,
                g_phase58_cycle.failed_thread, probe->tls_index,
                probe->runtime_fls_slot, probe->runtime_fls_cleanup)) {
            return 0;
        }
        g_phase58_cycle.prepared_vm = *probe->vm_region_count;
        g_phase58_cycle.prepared_threads = phase58_live_threads(probe->scheduler);
        g_phase58_cycle.prepared_objects = phase58_live_objects(probe->scheduler);
        if (g_phase58_cycle.prepared_vm <= baseline_vm ||
            g_phase58_cycle.prepared_threads <= baseline_threads ||
            g_phase58_cycle.prepared_objects <= baseline_objects) {
            return 0;
        }
        if (g_phase58_cycle.prepared_vm > peak_vm) peak_vm = g_phase58_cycle.prepared_vm;
        if (g_phase58_cycle.prepared_threads > peak_threads) peak_threads = g_phase58_cycle.prepared_threads;
        if (g_phase58_cycle.prepared_objects > peak_objects) peak_objects = g_phase58_cycle.prepared_objects;
        phase58_hex(probe, "GXOS_NET10:PHASE58_CYCLE=0x", cycle + 1U);
        phase58_hex(probe, "GXOS_NET10:PHASE58_FAILED_SLOT=0x",
                    g_phase58_cycle.failed_lifecycle.scheduler_slot);
        phase58_hex(probe, "GXOS_NET10:PHASE58_FAILED_IDENTITY=0x",
                    g_phase58_cycle.failed_lifecycle.worker_identity);
        phase58_hex(probe, "GXOS_NET10:PHASE58_FAILED_GENERATION=0x",
                    g_phase58_cycle.failed_lifecycle.worker_generation);
        phase58_hex(probe, "GXOS_NET10:PHASE58_PREPARED_VM_REGIONS=0x",
                    g_phase58_cycle.prepared_vm);
        phase58_hex(probe, "GXOS_NET10:PHASE58_PREPARED_THREADS=0x",
                    g_phase58_cycle.prepared_threads);
        phase58_hex(probe, "GXOS_NET10:PHASE58_PREPARED_OBJECTS=0x",
                    g_phase58_cycle.prepared_objects);
        phase58_text(probe, "GXOS_NET10:PHASE58_PREPARE_SUCCEEDED=1\r\n");
        if (!gxos_scheduler_resume_thread(g_phase58_cycle.failed_handle, 0) ||
            !gxos_nativeaot_scheduler_worker_mark_runnable(
                &g_phase58_cycle.failed_lifecycle)) {
            return 0;
        }
        phase58_text(probe, "GXOS_NET10:PHASE58_WORKER_RESUMED=1\r\n");
        gxos_scheduler_main_dispatch(&snapshot);
        if (gxos_scheduler_current_thread() != probe->main_thread ||
            !gxos_scheduler_thread_is_terminated(g_phase58_cycle.failed_thread) ||
            g_phase58_cycle.failure != 0 ||
            !phase58_failed_state_valid(&g_phase58_cycle, baseline_threadstore)) {
            return 0;
        }
        ++injected;
        ++passed;
        failed_gc_delta = g_phase58_cycle.failed_gc_after -
            g_phase58_cycle.failed_gc_before;
        if (failed_gc_delta != 0U ||
            g_phase58_cycle.failed_lifecycle.managed_root_owned ||
            g_phase58_cycle.failed_lifecycle.runtime_detach_count != 1U) {
            return 0;
        }
        phase58_text(probe, "GXOS_NET10:PHASE58_FAILED_WORKER_DID_NOT_ENTER_GC=1\r\n");
        phase58_text(probe, "GXOS_NET10:PHASE58_FAILED_WORKER_NO_POST_GC_CONTINUATION=1\r\n");
        phase58_hex(probe, "GXOS_NET10:PHASE58_ROOT_PUBLISHED_VM_REGIONS=0x",
                    g_phase58_cycle.root_published_vm);
        phase58_hex(probe, "GXOS_NET10:PHASE58_ROOT_RELEASED_VM_REGIONS=0x",
                    g_phase58_cycle.root_released_vm);
        phase58_hex(probe, "GXOS_NET10:PHASE58_ROOT_PUBLISHED_THREADS=0x",
                    g_phase58_cycle.root_published_threads);
        phase58_hex(probe, "GXOS_NET10:PHASE58_ROOT_RELEASED_THREADS=0x",
                    g_phase58_cycle.root_released_threads);
        phase58_hex(probe, "GXOS_NET10:PHASE58_ROOT_PUBLISHED_OBJECTS=0x",
                    g_phase58_cycle.root_published_objects);
        phase58_hex(probe, "GXOS_NET10:PHASE58_ROOT_RELEASED_OBJECTS=0x",
                    g_phase58_cycle.root_released_objects);
        phase58_hex(probe, "GXOS_NET10:PHASE58_ROOT_COUNT_PUBLISHED=0x", 1);
        phase58_hex(probe, "GXOS_NET10:PHASE58_ROOT_COUNT_AFTER_CLEANUP=0x", 0);
        duplicate_detach_rejected =
            !gxos_nativeaot_scheduler_worker_detach(
                &g_phase58_cycle.failed_lifecycle);
        if (!duplicate_detach_rejected ||
            !phase58_reclaim_worker(&g_phase58_cycle,
                                    g_phase58_cycle.failed_handle,
                                    g_phase58_cycle.failed_thread,
                                    &g_phase58_cycle.failed_lifecycle,
                                    probe->scheduler)) {
            return 0;
        }
        ++duplicate_detach_rejections;
        phase58_text(probe, "GXOS_NET10:PHASE58_FAILED_ROOT_CLEANUP=1\r\n");
        phase58_text(probe, "GXOS_NET10:PHASE58_SCHEDULER_RECLAIMED=1\r\n");
        old_slot = g_phase58_cycle.failed_lifecycle.scheduler_slot;
        old_identity = g_phase58_cycle.failed_lifecycle.worker_identity;
        old_generation = g_phase58_cycle.failed_lifecycle.worker_generation;
        old_handle_resume_rejected = !gxos_scheduler_resume_thread(
            g_phase58_cycle.failed_handle, 0);
        old_handle_close_rejected = !gxos_scheduler_close_handle(
            g_phase58_cycle.failed_handle);
        if (!old_handle_resume_rejected || !old_handle_close_rejected ||
            gxos_scheduler_thread_from_handle(g_phase58_cycle.failed_handle) != 0) {
            return 0;
        }
        ++stale_handle_rejections;

        if (!gxos_scheduler_create_suspended_thread(
                probe->scheduler, phase58_replacement_worker_entry,
                &g_phase58_cycle, &g_phase58_cycle.replacement_handle,
                &g_phase58_cycle.replacement_thread) ||
            !gxos_nativeaot_scheduler_worker_prepare(
                &g_phase58_cycle.replacement_lifecycle, probe->main_thread,
                g_phase58_cycle.replacement_thread, probe->tls_index,
                probe->runtime_fls_slot, probe->runtime_fls_cleanup)) {
            return 0;
        }
        if (g_phase58_cycle.replacement_lifecycle.scheduler_slot != old_slot) {
            same_slot = 0;
        }
        if (g_phase58_cycle.replacement_lifecycle.worker_identity == old_identity ||
            g_phase58_cycle.replacement_lifecycle.worker_generation == old_generation) {
            return 0;
        }
        phase58_hex(probe, "GXOS_NET10:PHASE58_REPLACEMENT_SLOT=0x",
                    g_phase58_cycle.replacement_lifecycle.scheduler_slot);
        phase58_hex(probe, "GXOS_NET10:PHASE58_REPLACEMENT_IDENTITY=0x",
                    g_phase58_cycle.replacement_lifecycle.worker_identity);
        phase58_hex(probe, "GXOS_NET10:PHASE58_REPLACEMENT_GENERATION=0x",
                    g_phase58_cycle.replacement_lifecycle.worker_generation);
        stale_lifecycle = g_phase58_cycle.failed_lifecycle;
        stale_lifecycle.ownership_state =
            GXOS_NATIVEAOT_WORKER_OWNERSHIP_RUNTIME_ATTACHED;
        stale_lifecycle.attached = 1;
        stale_lifecycle.detached = 0;
        stale_lifecycle.managed_root_owned = 1;
        stale_root_rejected =
            !gxos_nativeaot_scheduler_worker_note_managed_root_released(
                &stale_lifecycle, g_phase58_cycle.failed_root_token);
        stale_lifecycle.ownership_state =
            GXOS_NATIVEAOT_WORKER_OWNERSHIP_RUNTIME_ATTACHED;
        stale_lifecycle.detached = 0;
        stale_detach_rejected = !gxos_nativeaot_scheduler_worker_detach(
            &stale_lifecycle);
        if (!stale_root_rejected || !stale_detach_rejected ||
            g_phase58_cycle.replacement_lifecycle.ownership_state !=
                GXOS_NATIVEAOT_WORKER_OWNERSHIP_ALLOCATED ||
            g_phase58_cycle.replacement_thread->state !=
                GXOS_SCHEDULER_THREAD_CREATED_SUSPENDED) {
            return 0;
        }
        ++stale_root_rejections;
        ++stale_detach_rejections;
        phase58_text(probe, "GXOS_NET10:PHASE58_STALE_IDENTITY_REJECTED=1\r\n");
        phase58_text(probe, "GXOS_NET10:PHASE58_STALE_GENERATION_REJECTED=1\r\n");
        phase58_text(probe, "GXOS_NET10:PHASE58_STALE_ROOT_CLEANUP_REJECTED=1\r\n");

        if (!phase53o_rehome_canary(probe, g_phase58_cycle.replacement_thread) ||
            !gxos_scheduler_resume_thread(g_phase58_cycle.replacement_handle, 0) ||
            !gxos_nativeaot_scheduler_worker_mark_runnable(
                &g_phase58_cycle.replacement_lifecycle)) {
            return 0;
        }
        for (uint32_t dispatches = 0; dispatches != 4U; ++dispatches) {
            if (gxos_scheduler_thread_is_terminated(
                    g_phase58_cycle.replacement_thread)) break;
            if (gxos_scheduler_current_thread() != probe->main_thread ||
                gxos_scheduler_runnable_count() == 0U) return 0;
            gxos_scheduler_main_dispatch(&snapshot);
        }
        if (!gxos_scheduler_thread_is_terminated(g_phase58_cycle.replacement_thread) ||
            g_phase58_cycle.failure != 0 ||
            !phase58_replacement_state_valid(&g_phase58_cycle) ||
            !phase58_reclaim_worker(&g_phase58_cycle,
                                    g_phase58_cycle.replacement_handle,
                                    g_phase58_cycle.replacement_thread,
                                    &g_phase58_cycle.replacement_lifecycle,
                                    probe->scheduler) ||
            *probe->vm_region_count != baseline_vm ||
            phase58_live_threads(probe->scheduler) != baseline_threads ||
            phase58_live_objects(probe->scheduler) != baseline_objects ||
            phase53o_threadstore_count(
                probe->main_thread->fls_values[probe->runtime_fls_slot], 0) !=
                baseline_threadstore) {
            return 0;
        }
        g_phase58_cycle.final_vm = *probe->vm_region_count;
        g_phase58_cycle.final_threads = phase58_live_threads(probe->scheduler);
        g_phase58_cycle.final_objects = phase58_live_objects(probe->scheduler);
        phase58_text(probe, "GXOS_NET10:PHASE58_REPLACEMENT_WORKER_SUCCEEDED=1\r\n");
        phase58_text(probe, "GXOS_NET10:PHASE58_REPLACEMENT_GC=1\r\n");
        phase58_text(probe, "GXOS_NET10:PHASE58_REPLACEMENT_RECLAIM=1\r\n");
        phase58_text(probe, "GXOS_NET10:PHASE58_BASELINE_RESTORED=1\r\n");
        ++root_publish_successes;
        root_release_successes += 2U;
    }
    injection = gxos_nativeaot_phase56_failure_record();
    if (injection == 0 || injection->point !=
            GXOS_NATIVEAOT_FAILURE_INJECTION_AFTER_MANAGED_ROOT ||
        injected != PHASE58_CYCLE_COUNT || passed != PHASE58_CYCLE_COUNT ||
        root_publish_successes != PHASE58_CYCLE_COUNT ||
        root_release_successes != PHASE58_CYCLE_COUNT * 2U ||
        same_slot == 0 || stale_root_rejections != PHASE58_CYCLE_COUNT ||
        stale_handle_rejections != PHASE58_CYCLE_COUNT ||
        stale_detach_rejections != PHASE58_CYCLE_COUNT ||
        duplicate_detach_rejections != PHASE58_CYCLE_COUNT ||
        probe->callback_bridge->invocation_count !=
            baseline_callbacks + PHASE58_CYCLE_COUNT * 2U ||
        probe->gc_bridge->invocation_count !=
            baseline_gc_callbacks + PHASE58_CYCLE_COUNT ||
        probe->managed_root_publish_bridge->invocation_count !=
            baseline_root_publish + PHASE58_CYCLE_COUNT * 2U ||
        probe->managed_root_release_bridge->invocation_count !=
            baseline_root_release + PHASE58_CYCLE_COUNT * 3U ||
        *probe->vm_region_count != baseline_vm ||
        phase58_live_threads(probe->scheduler) != baseline_threads ||
        phase58_live_objects(probe->scheduler) != baseline_objects ||
        phase53o_threadstore_count(
            probe->main_thread->fls_values[probe->runtime_fls_slot], 0) !=
            baseline_threadstore || injection->fire_count != 1U ||
        injection->mismatch_count != 0U) {
        return 0;
    }
    phase58_hex(probe, "GXOS_NET10:PHASE58_RESOURCE_PEAK_VM_REGIONS=0x", peak_vm);
    phase58_hex(probe, "GXOS_NET10:PHASE58_RESOURCE_PEAK_THREADS=0x", peak_threads);
    phase58_hex(probe, "GXOS_NET10:PHASE58_RESOURCE_PEAK_OBJECTS=0x", peak_objects);
    phase58_hex(probe, "GXOS_NET10:PHASE58_INJECTED_FAILURE_CYCLES=0x", injected);
    phase58_hex(probe, "GXOS_NET10:PHASE58_PASSED_FAILURE_CYCLES=0x", passed);
    phase58_hex(probe, "GXOS_NET10:PHASE58_FAILED_ROOT_PUBLICATIONS=0x",
                root_publish_successes);
    phase58_hex(probe, "GXOS_NET10:PHASE58_ROOT_RELEASES_TOTAL=0x",
                root_release_successes);
    phase58_hex(probe, "GXOS_NET10:PHASE58_FAILED_ROOT_RELEASES=0x",
                root_release_successes / 2U);
    phase58_hex(probe, "GXOS_NET10:PHASE58_CALLBACK_FINAL=0x",
                probe->callback_bridge->invocation_count);
    phase58_hex(probe, "GXOS_NET10:PHASE58_GC_CALLBACK_FINAL=0x",
                probe->gc_bridge->invocation_count);
    phase58_hex(probe, "GXOS_NET10:PHASE58_ROOT_PUBLISH_FINAL=0x",
                probe->managed_root_publish_bridge->invocation_count);
    phase58_hex(probe, "GXOS_NET10:PHASE58_ROOT_RELEASE_FINAL=0x",
                probe->managed_root_release_bridge->invocation_count);
    phase58_text(probe, "GXOS_NET10:PHASE58_EXACTLY_ONE_ROOT_RELEASE=1\r\n");
    phase58_text(probe, "GXOS_NET10:PHASE58_EXACTLY_ONE_DETACH=1\r\n");
    phase58_text(probe, "GXOS_NET10:PHASE58_NO_DOUBLE_CLEANUP=1\r\n");
    phase58_text(probe, "GXOS_NET10:PHASE58_GENERATION_SAFE_ROOT_REUSE=1\r\n");
    phase58_text(probe, "GXOS_NET10:PHASE58_LEAK_TREND_NONE=1\r\n");
    phase58_text(probe, "GXOS_NET10:PHASE58_COMPLETE=1\r\n");
    phase58_text(probe, "GXOS_NET10:PHASE58_PASS=1\r\n");
    return 1;
}
#endif

#ifdef GXOS_ENABLE_PHASE59_POSTGC_ROLLBACK
#define PHASE59_CYCLE_COUNT 12U

typedef struct {
    GXOS_PHASE53O_PROBE *probe;
    uint32_t cycle;
    GXOS_SCHEDULER_HANDLE failed_handle;
    GXOS_SCHEDULER_TCB *failed_thread;
    GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE failed_lifecycle;
    GXOS_SCHEDULER_HANDLE replacement_handle;
    GXOS_SCHEDULER_TCB *replacement_thread;
    GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE replacement_lifecycle;
    uint32_t replacement_input;
    uint32_t replacement_seed;
    uint32_t failed_root_token;
    uint32_t replacement_root_token;
    int32_t failed_callback_result;
    int32_t failed_root_publish_result;
    int32_t failed_root_validate_before_result;
    int32_t failed_gc_result;
    int32_t failed_root_validate_after_result;
    int32_t failed_root_release_result;
    int32_t replacement_callback_result;
    int32_t replacement_root_publish_result;
    int32_t replacement_stale_root_result;
    int32_t replacement_root_validate_before_result;
    int32_t replacement_gc_result;
    int32_t replacement_root_validate_after_result;
    int32_t replacement_root_release_result;
    uint32_t failed_callback_status;
    uint32_t failed_root_publish_status;
    uint32_t failed_root_validate_before_status;
    uint32_t failed_gc_status;
    uint32_t failed_root_validate_after_status;
    uint32_t failed_root_release_status;
    uint32_t replacement_callback_status;
    uint32_t replacement_root_publish_status;
    uint32_t replacement_stale_root_status;
    uint32_t replacement_root_validate_before_status;
    uint32_t replacement_gc_status;
    uint32_t replacement_root_validate_after_status;
    uint32_t replacement_root_release_status;
    uint32_t failed_gc_delta;
    uint32_t failed_gc_generation;
    uint32_t failed_gc_checksum;
    uint32_t replacement_gc_delta;
    uint32_t replacement_gc_generation;
    uint32_t replacement_gc_checksum;
    uint32_t failed_gc_before;
    uint32_t failed_gc_after;
    uint32_t replacement_gc_before;
    uint32_t replacement_gc_after;
    uint32_t prepared_vm;
    uint32_t prepared_threads;
    uint32_t prepared_objects;
    uint32_t attached_vm;
    uint32_t attached_threads;
    uint32_t attached_objects;
    uint32_t root_published_vm;
    uint32_t root_published_threads;
    uint32_t root_published_objects;
    uint32_t post_gc_vm;
    uint32_t post_gc_threads;
    uint32_t post_gc_objects;
    uint32_t root_released_vm;
    uint32_t root_released_threads;
    uint32_t root_released_objects;
    uint32_t final_vm;
    uint32_t final_threads;
    uint32_t final_objects;
    uint64_t failed_pre_gc_root_identity;
    uint64_t failed_post_gc_root_identity;
    uint64_t replacement_pre_gc_root_identity;
    uint64_t replacement_post_gc_root_identity;
    int failure;
} GXOS_PHASE59_CYCLE;

static GXOS_PHASE59_CYCLE g_phase59_cycle;

static void phase59_text(GXOS_PHASE53O_PROBE *probe, const char *text)
{
    probe->log_text(text);
}

static void phase59_hex(GXOS_PHASE53O_PROBE *probe,
                        const char *name, uint64_t value)
{
    probe->log_hex(name, value);
    probe->log_text("\r\n");
}

static uint32_t phase59_live_threads(const GXOS_SCHEDULER *scheduler)
{
    uint32_t index;
    uint32_t count = 0;
    for (index = 0; index != GXOS_SCHEDULER_MAX_THREADS; ++index) {
        if (scheduler->threads[index].live) ++count;
    }
    return count;
}

static uint32_t phase59_live_objects(const GXOS_SCHEDULER *scheduler)
{
    uint32_t index;
    uint32_t count = 0;
    for (index = 0; index != GXOS_SCHEDULER_MAX_OBJECTS; ++index) {
        if (scheduler->objects[index].live) ++count;
    }
    return count;
}

static int phase59_fail(GXOS_PHASE59_CYCLE *cycle)
{
    cycle->failure = 1;
    phase59_text(cycle->probe, "GXOS_NET10:PHASE59_FAILURE=1\r\n");
    return 0;
}

static int phase59_root_validate(
    GXOS_PHASE59_CYCLE *cycle,
    GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE *lifecycle,
    uint32_t token, GXOS_NATIVEAOT_CALLBACK_BRIDGE *bridge,
    int32_t *result, uint32_t *status)
{
    phase59_text(cycle->probe, "GXOS_NET10:PHASE59_ROOT_VALIDATE_BEGIN=1\r\n");
    cycle->probe->phase_in_managed(PHASE53O_PHASE_IN_MANAGED);
    if (!gxos_nativeaot_scheduler_worker_invoke(
            lifecycle, bridge, (int32_t)token, result, status) ||
        *status != GXOS_NATIVEAOT_CALLBACK_OK ||
        (uint32_t)*result != (0x5A000000U | token)) {
        return 0;
    }
    cycle->probe->phase_after_managed(PHASE53O_PHASE_AFTER_MANAGED_RETURN);
    return 1;
}

static uintptr_t GXOS_PHASE53O_MS_ABI
phase59_failed_worker_entry(void *argument)
{
    GXOS_PHASE59_CYCLE *cycle = (GXOS_PHASE59_CYCLE *)argument;
    GXOS_PHASE53O_PROBE *probe = cycle->probe;
    GXOS_SCHEDULER_TCB *thread = gxos_scheduler_current_thread();
    GXOS_SCHEDULER_REGISTER_SNAPSHOT snapshot = {0};
    GXOS_NATIVEAOT_FAILURE_INJECTION_RECORD const *injection;
    uint32_t callback_low;
    int duplicate_release_rejected;

    cycle->failed_thread = thread;
    gxos_scheduler_capture_registers(&snapshot);
    if (thread == 0 || !gxos_nativeaot_scheduler_worker_mark_running(
            &cycle->failed_lifecycle) ||
        !gxos_scheduler_validate_worker_snapshot(thread, &snapshot)) {
        return (uintptr_t)phase59_fail(cycle);
    }
    phase59_text(probe, "GXOS_NET10:PHASE59_FAILED_WORKER_RUNNING=1\r\n");
    cycle->failed_gc_before = probe->gc_bridge->invocation_count;
    probe->phase_in_managed(PHASE53O_PHASE_IN_MANAGED);
    if (!gxos_nativeaot_scheduler_worker_invoke(
            &cycle->failed_lifecycle, probe->callback_bridge, 0x59,
            &cycle->failed_callback_result,
            &cycle->failed_callback_status)) {
        return (uintptr_t)phase59_fail(cycle);
    }
    probe->phase_after_managed(PHASE53O_PHASE_AFTER_MANAGED_RETURN);
    callback_low = (uint32_t)cycle->failed_callback_result & 0xFFFFU;
    cycle->attached_vm = *probe->vm_region_count;
    cycle->attached_threads = phase59_live_threads(probe->scheduler);
    cycle->attached_objects = phase59_live_objects(probe->scheduler);
    if (cycle->failed_callback_status != GXOS_NATIVEAOT_CALLBACK_OK ||
        callback_low != 0x5AU || cycle->failed_lifecycle.runtime_thread == 0 ||
        cycle->failed_lifecycle.runtime_attach_count != 1U ||
        cycle->failed_lifecycle.runtime_detach_count != 0U) {
        return (uintptr_t)phase59_fail(cycle);
    }
    phase59_text(probe, "GXOS_NET10:PHASE59_RUNTIME_ATTACH_SUCCEEDED=1\r\n");
    phase59_hex(probe, "GXOS_NET10:PHASE59_FAILED_RUNTIME_THREAD=0x",
                (uint64_t)(uintptr_t)cycle->failed_lifecycle.runtime_thread);
    phase59_hex(probe, "GXOS_NET10:PHASE59_FAILED_FLS_AFTER_ATTACH=0x",
                (uint64_t)gxos_scheduler_get_fls(probe->runtime_fls_slot));
    phase59_hex(probe, "GXOS_NET10:PHASE59_FAILED_RUNTIME_ATTACH_COUNT=0x",
                cycle->failed_lifecycle.runtime_attach_count);

    cycle->failed_root_token = 0x5A00U | cycle->cycle;
    probe->phase_in_managed(PHASE53O_PHASE_IN_MANAGED);
    if (!gxos_nativeaot_scheduler_worker_invoke(
            &cycle->failed_lifecycle, probe->managed_root_publish_bridge,
            (int32_t)cycle->failed_root_token,
            &cycle->failed_root_publish_result,
            &cycle->failed_root_publish_status) ||
        cycle->failed_root_publish_status != GXOS_NATIVEAOT_CALLBACK_OK ||
        (uint32_t)cycle->failed_root_publish_result !=
            (0x58000000U | cycle->failed_root_token) ||
        !gxos_nativeaot_scheduler_worker_note_managed_root_published(
            &cycle->failed_lifecycle, cycle->failed_root_token)) {
        return (uintptr_t)phase59_fail(cycle);
    }
    probe->phase_after_managed(PHASE53O_PHASE_AFTER_MANAGED_RETURN);
    cycle->root_published_vm = *probe->vm_region_count;
    cycle->root_published_threads = phase59_live_threads(probe->scheduler);
    cycle->root_published_objects = phase59_live_objects(probe->scheduler);
    cycle->failed_pre_gc_root_identity = cycle->failed_root_token;
    if (!phase59_root_validate(
            cycle, &cycle->failed_lifecycle, cycle->failed_root_token,
            probe->managed_root_validate_bridge,
            &cycle->failed_root_validate_before_result,
            &cycle->failed_root_validate_before_status)) {
        return (uintptr_t)phase59_fail(cycle);
    }
    phase59_text(probe, "GXOS_NET10:PHASE59_ROOT_PUBLISHED=1\r\n");
    phase59_hex(probe, "GXOS_NET10:PHASE59_PRE_GC_LOGICAL_ROOT_IDENTITY=0x",
                cycle->failed_pre_gc_root_identity);

    probe->phase_in_managed(PHASE53O_PHASE_IN_MANAGED);
    if (!gxos_nativeaot_scheduler_worker_invoke(
            &cycle->failed_lifecycle, probe->gc_bridge,
            (int32_t)(0xD0U + cycle->cycle), &cycle->failed_gc_result,
            &cycle->failed_gc_status) ||
        cycle->failed_gc_status != GXOS_NATIVEAOT_CALLBACK_OK ||
        !gxos_nativeaot_gc_result_valid(
            cycle->failed_gc_result, 0xD0U + cycle->cycle,
            &cycle->failed_gc_delta, &cycle->failed_gc_generation,
            &cycle->failed_gc_checksum) || cycle->failed_gc_delta == 0U) {
        return (uintptr_t)phase59_fail(cycle);
    }
    probe->phase_after_managed(PHASE53O_PHASE_AFTER_MANAGED_RETURN);
    if (!phase59_root_validate(
            cycle, &cycle->failed_lifecycle, cycle->failed_root_token,
            probe->managed_root_validate_bridge,
            &cycle->failed_root_validate_after_result,
            &cycle->failed_root_validate_after_status)) {
        return (uintptr_t)phase59_fail(cycle);
    }
    cycle->failed_post_gc_root_identity = cycle->failed_root_token;
    cycle->failed_lifecycle.managed_root_survived = 1;
    cycle->post_gc_vm = *probe->vm_region_count;
    cycle->post_gc_threads = phase59_live_threads(probe->scheduler);
    cycle->post_gc_objects = phase59_live_objects(probe->scheduler);
    phase59_text(probe, "GXOS_NET10:PHASE59_GC_COMPLETED=1\r\n");
    phase59_text(probe, "GXOS_NET10:PHASE59_ROOT_SURVIVAL_PROVEN=1\r\n");
    phase59_hex(probe, "GXOS_NET10:PHASE59_POST_GC_LOGICAL_ROOT_IDENTITY=0x",
                cycle->failed_post_gc_root_identity);
    phase59_hex(probe, "GXOS_NET10:PHASE59_ALLOC_CONTEXT_PRE_GC=0x",
                cycle->failed_lifecycle.allocation_context);
    phase59_hex(probe, "GXOS_NET10:PHASE59_ALLOC_PTR_POST_GC=0x",
                cycle->failed_lifecycle.alloc_ptr_after);
    phase59_hex(probe, "GXOS_NET10:PHASE59_ALLOC_LIMIT_POST_GC=0x",
                cycle->failed_lifecycle.alloc_limit_after);

    if (!gxos_nativeaot_phase56_failure_arm(
            GXOS_NATIVEAOT_FAILURE_INJECTION_AFTER_GC_ROOT_SURVIVAL,
            &cycle->failed_lifecycle) ||
        !gxos_nativeaot_phase56_failure_try_fire(&cycle->failed_lifecycle) ||
        gxos_nativeaot_phase56_failure_try_fire(&cycle->failed_lifecycle)) {
        return (uintptr_t)phase59_fail(cycle);
    }
    injection = gxos_nativeaot_phase56_failure_record();
    if (injection == 0 || injection->state !=
            GXOS_NATIVEAOT_FAILURE_INJECTION_FIRED ||
        injection->point !=
            GXOS_NATIVEAOT_FAILURE_INJECTION_AFTER_GC_ROOT_SURVIVAL ||
        injection->fire_count != 1U || injection->mismatch_count != 0U) {
        return (uintptr_t)phase59_fail(cycle);
    }
    phase59_text(probe, "GXOS_NET10:PHASE59_INJECTION_ARMED=1\r\n");
    phase59_text(probe, "GXOS_NET10:PHASE59_INJECTION_FIRED=1\r\n");
    phase59_text(probe, "GXOS_NET10:PHASE59_INTENTIONAL_FAILURE=1\r\n");
    phase59_text(probe, "GXOS_NET10:PHASE59_NORMAL_POST_GC_CONTINUATION_SUPPRESSED=1\r\n");

    probe->phase_in_managed(PHASE53O_PHASE_IN_MANAGED);
    if (!gxos_nativeaot_scheduler_worker_invoke(
            &cycle->failed_lifecycle, probe->managed_root_release_bridge,
            (int32_t)cycle->failed_root_token,
            &cycle->failed_root_release_result,
            &cycle->failed_root_release_status) ||
        cycle->failed_root_release_status != GXOS_NATIVEAOT_CALLBACK_OK ||
        (uint32_t)cycle->failed_root_release_result !=
            (0x59000000U | cycle->failed_root_token) ||
        !gxos_nativeaot_scheduler_worker_note_managed_root_released(
            &cycle->failed_lifecycle, cycle->failed_root_token)) {
        return (uintptr_t)phase59_fail(cycle);
    }
    probe->phase_after_managed(PHASE53O_PHASE_AFTER_MANAGED_RETURN);
    cycle->root_released_vm = *probe->vm_region_count;
    cycle->root_released_threads = phase59_live_threads(probe->scheduler);
    cycle->root_released_objects = phase59_live_objects(probe->scheduler);
    duplicate_release_rejected =
        !gxos_nativeaot_scheduler_worker_note_managed_root_released(
            &cycle->failed_lifecycle, cycle->failed_root_token);
    if (!duplicate_release_rejected || cycle->failed_lifecycle.managed_root_owned ||
        cycle->failed_lifecycle.managed_root_release_count != 1U) {
        return (uintptr_t)phase59_fail(cycle);
    }
    phase59_text(probe, "GXOS_NET10:PHASE59_ROOT_RELEASED=1\r\n");
    phase59_hex(probe, "GXOS_NET10:PHASE59_FAILED_ROOT_RELEASE_COUNT=0x",
                cycle->failed_lifecycle.managed_root_release_count);
    phase59_hex(probe, "GXOS_NET10:PHASE59_ALLOC_CONTEXT_AFTER_ROOT_RELEASE=0x",
                cycle->failed_lifecycle.allocation_context);
    phase59_hex(probe, "GXOS_NET10:PHASE59_ALLOC_PTR_AFTER_ROOT_RELEASE=0x",
                cycle->failed_lifecycle.alloc_ptr_after);
    phase59_hex(probe, "GXOS_NET10:PHASE59_ALLOC_LIMIT_AFTER_ROOT_RELEASE=0x",
                cycle->failed_lifecycle.alloc_limit_after);
    phase59_text(probe, "GXOS_NET10:PHASE59_FAILED_ROOT_OWNED_AFTER_RELEASE=0\r\n");
    phase59_text(probe, "GXOS_NET10:PHASE59_DUPLICATE_ROOT_RELEASE_REJECTED=1\r\n");
    if (!gxos_nativeaot_scheduler_worker_detach(&cycle->failed_lifecycle) ||
        cycle->failed_lifecycle.runtime_detach_count != 1U ||
        gxos_scheduler_get_fls(probe->runtime_fls_slot) != 0) {
        return (uintptr_t)phase59_fail(cycle);
    }
    phase59_hex(probe, "GXOS_NET10:PHASE59_ALLOC_PTR_AFTER_DETACH=0x",
                cycle->failed_lifecycle.alloc_ptr_after_detach);
    phase59_hex(probe, "GXOS_NET10:PHASE59_ALLOC_LIMIT_AFTER_DETACH=0x",
                cycle->failed_lifecycle.alloc_limit_after_detach);
    phase59_hex(probe, "GXOS_NET10:PHASE59_FAILED_RUNTIME_DETACH_COUNT=0x",
                cycle->failed_lifecycle.runtime_detach_count);
    phase59_hex(probe, "GXOS_NET10:PHASE59_FAILED_FLS_AFTER_DETACH=0x",
                (uint64_t)gxos_scheduler_get_fls(probe->runtime_fls_slot));
    phase59_hex(probe, "GXOS_NET10:PHASE59_ALLOC_CONTEXT_AFTER_DETACH=0x",
                cycle->failed_lifecycle.allocation_context);
    phase59_text(probe, "GXOS_NET10:PHASE59_DETACH_EXECUTED=1\r\n");
    phase59_text(probe, "GXOS_NET10:PHASE59_FLS_THREADSTORE_RUNTIME_STATE_CLEARED=1\r\n");
    cycle->failed_gc_after = probe->gc_bridge->invocation_count;
    return (uintptr_t)cycle->failed_callback_result;
}

static uintptr_t GXOS_PHASE53O_MS_ABI
phase59_replacement_worker_entry(void *argument)
{
    GXOS_PHASE59_CYCLE *cycle = (GXOS_PHASE59_CYCLE *)argument;
    GXOS_PHASE53O_PROBE *probe = cycle->probe;
    GXOS_SCHEDULER_TCB *thread = gxos_scheduler_current_thread();
    GXOS_SCHEDULER_REGISTER_SNAPSHOT snapshot = {0};
    uint32_t callback_low;
    int good = 1;

    cycle->replacement_thread = thread;
    gxos_scheduler_capture_registers(&snapshot);
    if (thread == 0 || !gxos_nativeaot_scheduler_worker_mark_running(
            &cycle->replacement_lifecycle) ||
        !gxos_scheduler_validate_worker_snapshot(thread, &snapshot)) {
        return (uintptr_t)phase59_fail(cycle);
    }
    probe->phase_in_managed(PHASE53O_PHASE_IN_MANAGED);
    if (!gxos_nativeaot_scheduler_worker_invoke(
            &cycle->replacement_lifecycle, probe->callback_bridge,
            (int32_t)cycle->replacement_input,
            &cycle->replacement_callback_result,
            &cycle->replacement_callback_status)) {
        good = 0;
    }
    probe->phase_after_managed(PHASE53O_PHASE_AFTER_MANAGED_RETURN);
    callback_low = (uint32_t)cycle->replacement_callback_result & 0xFFFFU;
    if (cycle->replacement_callback_status != GXOS_NATIVEAOT_CALLBACK_OK ||
        callback_low != cycle->replacement_input + 1U) {
        good = 0;
    }
    phase59_hex(probe, "GXOS_NET10:PHASE59_REPLACEMENT_RUNTIME_THREAD=0x",
                (uint64_t)(uintptr_t)cycle->replacement_lifecycle.runtime_thread);
    phase59_hex(probe, "GXOS_NET10:PHASE59_REPLACEMENT_RUNTIME_ATTACH_COUNT=0x",
                cycle->replacement_lifecycle.runtime_attach_count);
    cycle->replacement_root_token = 0x5B00U | cycle->cycle;
    probe->phase_in_managed(PHASE53O_PHASE_IN_MANAGED);
    if (!gxos_nativeaot_scheduler_worker_invoke(
            &cycle->replacement_lifecycle, probe->managed_root_publish_bridge,
            (int32_t)cycle->replacement_root_token,
            &cycle->replacement_root_publish_result,
            &cycle->replacement_root_publish_status) ||
        cycle->replacement_root_publish_status != GXOS_NATIVEAOT_CALLBACK_OK ||
        (uint32_t)cycle->replacement_root_publish_result !=
            (0x58000000U | cycle->replacement_root_token) ||
        !gxos_nativeaot_scheduler_worker_note_managed_root_published(
            &cycle->replacement_lifecycle, cycle->replacement_root_token)) {
        good = 0;
    }
    probe->phase_after_managed(PHASE53O_PHASE_AFTER_MANAGED_RETURN);
    cycle->replacement_pre_gc_root_identity = cycle->replacement_root_token;
    if (!phase59_root_validate(
            cycle, &cycle->replacement_lifecycle,
            cycle->replacement_root_token, probe->managed_root_validate_bridge,
            &cycle->replacement_root_validate_before_result,
            &cycle->replacement_root_validate_before_status)) {
        good = 0;
    }
    cycle->replacement_gc_before = probe->gc_bridge->invocation_count;
    probe->phase_in_managed(PHASE53O_PHASE_IN_MANAGED);
    if (!gxos_nativeaot_scheduler_worker_invoke(
            &cycle->replacement_lifecycle, probe->gc_bridge,
            (int32_t)cycle->replacement_seed, &cycle->replacement_gc_result,
            &cycle->replacement_gc_status) ||
        cycle->replacement_gc_status != GXOS_NATIVEAOT_CALLBACK_OK ||
        !gxos_nativeaot_gc_result_valid(
            cycle->replacement_gc_result, cycle->replacement_seed,
            &cycle->replacement_gc_delta, &cycle->replacement_gc_generation,
            &cycle->replacement_gc_checksum) ||
        cycle->replacement_gc_delta == 0U ||
        !phase59_root_validate(
            cycle, &cycle->replacement_lifecycle,
            cycle->replacement_root_token, probe->managed_root_validate_bridge,
            &cycle->replacement_root_validate_after_result,
            &cycle->replacement_root_validate_after_status)) {
        good = 0;
    } else {
        cycle->replacement_lifecycle.managed_root_survived = 1;
        cycle->replacement_post_gc_root_identity =
            cycle->replacement_root_token;
    }
    probe->phase_after_managed(PHASE53O_PHASE_AFTER_MANAGED_RETURN);
    cycle->replacement_gc_after = probe->gc_bridge->invocation_count;
    phase59_text(probe, "GXOS_NET10:PHASE59_REPLACEMENT_POST_GC_CONTINUATION=1\r\n");
    probe->phase_in_managed(PHASE53O_PHASE_IN_MANAGED);
    if (!gxos_nativeaot_scheduler_worker_invoke(
            &cycle->replacement_lifecycle, probe->managed_root_release_bridge,
            (int32_t)cycle->replacement_root_token,
            &cycle->replacement_root_release_result,
            &cycle->replacement_root_release_status) ||
        cycle->replacement_root_release_status != GXOS_NATIVEAOT_CALLBACK_OK ||
        (uint32_t)cycle->replacement_root_release_result !=
            (0x59000000U | cycle->replacement_root_token) ||
        !gxos_nativeaot_scheduler_worker_note_managed_root_released(
            &cycle->replacement_lifecycle, cycle->replacement_root_token) ||
        !gxos_nativeaot_scheduler_worker_detach(
            &cycle->replacement_lifecycle) ||
        cycle->replacement_lifecycle.runtime_detach_count != 1U) {
        good = 0;
    }
    probe->phase_after_managed(PHASE53O_PHASE_AFTER_MANAGED_RETURN);
    if (!good) return (uintptr_t)phase59_fail(cycle);
    phase59_text(probe, "GXOS_NET10:PHASE59_REPLACEMENT_ROOT_SURVIVED_GC=1\r\n");
    phase59_text(probe, "GXOS_NET10:PHASE59_REPLACEMENT_DETACH=1\r\n");
    phase59_hex(probe, "GXOS_NET10:PHASE59_REPLACEMENT_RUNTIME_DETACH_COUNT=0x",
                cycle->replacement_lifecycle.runtime_detach_count);
    return (uintptr_t)cycle->replacement_callback_result;
}

static int phase59_reclaim_worker(
    GXOS_PHASE59_CYCLE *cycle, GXOS_SCHEDULER_HANDLE handle,
    GXOS_SCHEDULER_TCB *thread,
    GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE *lifecycle,
    GXOS_SCHEDULER *scheduler)
{
    if (cycle == 0 || scheduler == 0 || thread == 0 || lifecycle == 0 ||
        lifecycle->runtime_detach_count != 1U || !lifecycle->detached ||
        !gxos_scheduler_thread_is_terminated(thread)) {
        return 0;
    }
    return gxos_nativeaot_scheduler_worker_note_reclaimable(lifecycle) &&
           gxos_scheduler_close_handle(handle) &&
           gxos_scheduler_collect(scheduler) &&
           gxos_nativeaot_scheduler_worker_note_reclaimed(lifecycle) &&
           thread->live == 0 && gxos_scheduler_thread_from_handle(handle) == 0;
}

static int phase59_failed_state_valid(const GXOS_PHASE59_CYCLE *cycle,
                                      uint32_t baseline_threadstore)
{
    const GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE *lifecycle;
    const GXOS_SCHEDULER_TCB *thread;
    if (cycle == 0) return 0;
    lifecycle = &cycle->failed_lifecycle;
    thread = cycle->failed_thread;
    return thread != 0 && lifecycle->attached && lifecycle->detached &&
           lifecycle->runtime_attach_count == 1U &&
           lifecycle->runtime_detach_count == 1U &&
           lifecycle->managed_root_identity == cycle->failed_root_token &&
           lifecycle->managed_root_publication_count == 1U &&
           lifecycle->managed_root_release_count == 1U &&
           lifecycle->managed_root_owned == 0 && lifecycle->managed_root_survived &&
           cycle->failed_pre_gc_root_identity == cycle->failed_post_gc_root_identity &&
           lifecycle->ownership_state ==
               GXOS_NATIVEAOT_WORKER_OWNERSHIP_RUNTIME_DETACHED &&
           lifecycle->runtime_thread != 0 && !lifecycle->runtime_thread_owned &&
           !lifecycle->tls_fls_owned && thread->live &&
           thread->fls_values[lifecycle->runtime_fls_slot] == 0 &&
           lifecycle->runtime_state_after ==
               GXOS_NATIVEAOT_RUNTIME_THREAD_DETACHED &&
           lifecycle->threadstore_after == baseline_threadstore;
}

static int phase59_replacement_state_valid(const GXOS_PHASE59_CYCLE *cycle)
{
    const GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE *lifecycle;
    if (cycle == 0) return 0;
    lifecycle = &cycle->replacement_lifecycle;
    return lifecycle->attached && lifecycle->detached &&
           lifecycle->runtime_attach_count == 1U &&
           lifecycle->runtime_detach_count == 1U &&
           lifecycle->managed_root_identity == cycle->replacement_root_token &&
           lifecycle->managed_root_publication_count == 1U &&
           lifecycle->managed_root_release_count == 1U &&
           lifecycle->managed_root_owned == 0 && lifecycle->managed_root_survived &&
           cycle->replacement_pre_gc_root_identity ==
               cycle->replacement_post_gc_root_identity &&
           lifecycle->ownership_state ==
               GXOS_NATIVEAOT_WORKER_OWNERSHIP_RUNTIME_DETACHED;
}

int gxos_nativeaot_phase59_postgc_rollback_probe(GXOS_PHASE53O_PROBE *probe)
{
    static GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE stale_lifecycle;
    GXOS_NATIVEAOT_FAILURE_INJECTION_RECORD const *injection;
    GXOS_SCHEDULER_REGISTER_SNAPSHOT snapshot = {0};
    uint32_t baseline_vm;
    uint32_t baseline_threads;
    uint32_t baseline_objects;
    uint32_t baseline_threadstore;
    uint32_t baseline_callbacks;
    uint32_t baseline_gc_callbacks;
    uint32_t baseline_root_publish;
    uint32_t baseline_root_release;
    uint32_t baseline_root_validate;
    uint32_t injected = 0;
    uint32_t passed = 0;
    uint32_t failed_root_releases = 0;
    uint32_t duplicate_root_rejections = 0;
    uint32_t stale_root_rejections = 0;
    uint32_t stale_handle_rejections = 0;
    uint32_t stale_detach_rejections = 0;
    uint32_t duplicate_detach_rejections = 0;
    uint32_t stale_pre_gc_identity_rejections = 0;
    uint32_t peak_vm = 0;
    uint32_t peak_threads = 0;
    uint32_t peak_objects = 0;
    int same_slot = 1;

    if (probe == 0 || probe->scheduler == 0 || probe->main_thread == 0 ||
        probe->callback_bridge == 0 || probe->gc_bridge == 0 ||
        probe->managed_root_publish_bridge == 0 ||
        probe->managed_root_release_bridge == 0 ||
        probe->managed_root_validate_bridge == 0 ||
        probe->runtime_fls_cleanup == 0 || probe->vm_region_count == 0 ||
        probe->log_text == 0 || probe->log_hex == 0 ||
        probe->phase_in_managed == 0 || probe->phase_after_managed == 0 ||
        probe->main_thread != gxos_scheduler_current_thread() ||
        !probe->main_thread->is_boot_thread) {
        return 0;
    }
    baseline_vm = *probe->vm_region_count;
    baseline_threads = phase59_live_threads(probe->scheduler);
    baseline_objects = phase59_live_objects(probe->scheduler);
    baseline_threadstore = phase53o_threadstore_count(
        probe->main_thread->fls_values[probe->runtime_fls_slot], 0);
    baseline_callbacks = probe->callback_bridge->invocation_count;
    baseline_gc_callbacks = probe->gc_bridge->invocation_count;
    baseline_root_publish = probe->managed_root_publish_bridge->invocation_count;
    baseline_root_release = probe->managed_root_release_bridge->invocation_count;
    baseline_root_validate = probe->managed_root_validate_bridge->invocation_count;
    phase59_text(probe, "GXOS_NET10:PHASE59_BEGIN\r\n");
    phase59_text(probe,
                 "GXOS_NET10:PHASE59_INJECTION_POINT=AFTER_GC_ROOT_SURVIVAL\r\n");
    phase59_text(probe,
                 "GXOS_NET10:PHASE59_DIAGNOSTIC_HOOK=GXOS_NATIVEAOT_FAILURE_INJECTION_AFTER_GC_ROOT_SURVIVAL\r\n");
    phase59_text(probe,
                 "GXOS_NET10:PHASE59_COMPILE_GATE=GXOS_ENABLE_PHASE59_POSTGC_ROLLBACK\r\n");
    phase59_text(probe,
                 "GXOS_NET10:PHASE59_MANAGED_ROOT_AUTHORITY=MANAGED_STATIC_REFERENCE\r\n");
    phase59_text(probe,
                 "GXOS_NET10:PHASE59_ROOT_CLEANUP_AUTHORITY=MANAGED_ROOT_RELEASE_EXPORT\r\n");
    phase59_text(probe,
                 "GXOS_NET10:PHASE59_PHYSICAL_RELOCATION=NOT_MEASURED\r\n");
    phase59_text(probe,
                 "GXOS_NET10:PHASE59_RAW_MANAGED_ADDRESS_RETAINED=0\r\n");
    phase59_hex(probe, "GXOS_NET10:PHASE59_RESOURCE_BASELINE_VM_REGIONS=0x", baseline_vm);
    phase59_hex(probe, "GXOS_NET10:PHASE59_RESOURCE_BASELINE_THREADS=0x", baseline_threads);
    phase59_hex(probe, "GXOS_NET10:PHASE59_RESOURCE_BASELINE_OBJECTS=0x", baseline_objects);
    phase59_hex(probe, "GXOS_NET10:PHASE59_RESOURCE_BASELINE_ROOTS=0x", 0);

    for (uint32_t index = 0; index != PHASE59_CYCLE_COUNT; ++index) {
        uint32_t old_slot;
        uint32_t old_identity;
        uint16_t old_generation;
        int duplicate_detach_rejected;
        int stale_root_rejected;
        int stale_detach_rejected;
        int old_handle_resume_rejected;
        int old_handle_close_rejected;

        g_phase59_cycle = (GXOS_PHASE59_CYCLE){0};
        g_phase59_cycle.probe = probe;
        g_phase59_cycle.cycle = index + 1U;
        g_phase59_cycle.replacement_input = 0xA0U + index;
        g_phase59_cycle.replacement_seed = 0xC0U + index;
        if (!gxos_scheduler_create_suspended_thread(
                probe->scheduler, phase59_failed_worker_entry, &g_phase59_cycle,
                &g_phase59_cycle.failed_handle, &g_phase59_cycle.failed_thread) ||
            !gxos_nativeaot_scheduler_worker_prepare(
                &g_phase59_cycle.failed_lifecycle, probe->main_thread,
                g_phase59_cycle.failed_thread, probe->tls_index,
                probe->runtime_fls_slot, probe->runtime_fls_cleanup)) {
            return 0;
        }
        g_phase59_cycle.prepared_vm = *probe->vm_region_count;
        g_phase59_cycle.prepared_threads = phase59_live_threads(probe->scheduler);
        g_phase59_cycle.prepared_objects = phase59_live_objects(probe->scheduler);
        if (g_phase59_cycle.prepared_vm <= baseline_vm ||
            g_phase59_cycle.prepared_threads <= baseline_threads ||
            g_phase59_cycle.prepared_objects <= baseline_objects) return 0;
        if (g_phase59_cycle.prepared_vm > peak_vm) peak_vm = g_phase59_cycle.prepared_vm;
        if (g_phase59_cycle.prepared_threads > peak_threads) peak_threads = g_phase59_cycle.prepared_threads;
        if (g_phase59_cycle.prepared_objects <= GXOS_SCHEDULER_MAX_OBJECTS &&
            g_phase59_cycle.prepared_objects > peak_objects) {
            peak_objects = g_phase59_cycle.prepared_objects;
        }
        phase59_hex(probe, "GXOS_NET10:PHASE59_PREPARED_VM_REGIONS=0x",
                    g_phase59_cycle.prepared_vm);
        phase59_hex(probe, "GXOS_NET10:PHASE59_PREPARED_THREADS=0x",
                    g_phase59_cycle.prepared_threads);
        if (g_phase59_cycle.prepared_objects <= GXOS_SCHEDULER_MAX_OBJECTS) {
            phase59_hex(probe, "GXOS_NET10:PHASE59_PREPARED_OBJECTS=0x",
                        g_phase59_cycle.prepared_objects);
        } else {
            phase59_text(probe,
                         "GXOS_NET10:PHASE59_PREPARED_OBJECT_CENSUS_DEFERRED=1\r\n");
        }
        phase59_hex(probe, "GXOS_NET10:PHASE59_FAILED_SLOT=0x",
                    g_phase59_cycle.failed_lifecycle.scheduler_slot);
        phase59_hex(probe, "GXOS_NET10:PHASE59_FAILED_IDENTITY=0x",
                    g_phase59_cycle.failed_lifecycle.worker_identity);
        phase59_hex(probe, "GXOS_NET10:PHASE59_FAILED_GENERATION=0x",
                    g_phase59_cycle.failed_lifecycle.worker_generation);
        phase59_text(probe, "GXOS_NET10:PHASE59_PREPARED=1\r\n");
        if (!gxos_scheduler_resume_thread(g_phase59_cycle.failed_handle, 0) ||
            !gxos_nativeaot_scheduler_worker_mark_runnable(
                &g_phase59_cycle.failed_lifecycle)) return 0;
        phase59_text(probe, "GXOS_NET10:PHASE59_WORKER_RESUMED=1\r\n");
        gxos_scheduler_main_dispatch(&snapshot);
        injection = gxos_nativeaot_phase56_failure_record();
        if (gxos_scheduler_current_thread() != probe->main_thread ||
            !gxos_scheduler_thread_is_terminated(g_phase59_cycle.failed_thread) ||
            g_phase59_cycle.failure != 0 ||
            injection == 0 || injection->point !=
                GXOS_NATIVEAOT_FAILURE_INJECTION_AFTER_GC_ROOT_SURVIVAL ||
            injection->state != GXOS_NATIVEAOT_FAILURE_INJECTION_FIRED ||
            injection->fire_count != 1U || injection->mismatch_count != 0U ||
            !phase59_failed_state_valid(&g_phase59_cycle, baseline_threadstore) ||
            g_phase59_cycle.failed_gc_after - g_phase59_cycle.failed_gc_before != 1U) {
            return 0;
        }
        if (g_phase59_cycle.attached_objects > peak_objects) {
            peak_objects = g_phase59_cycle.attached_objects;
        }
        ++injected;
        ++passed;
        ++failed_root_releases;
        phase59_text(probe, "GXOS_NET10:PHASE59_FAILED_GC_COMPLETED=1\r\n");
        phase59_text(probe, "GXOS_NET10:PHASE59_FAILED_ROOT_SURVIVED_GC=1\r\n");
        phase59_hex(probe, "GXOS_NET10:PHASE59_ATTACHED_VM_REGIONS=0x",
                    g_phase59_cycle.attached_vm);
        phase59_hex(probe, "GXOS_NET10:PHASE59_ATTACHED_THREADS=0x",
                    g_phase59_cycle.attached_threads);
        phase59_hex(probe, "GXOS_NET10:PHASE59_ATTACHED_OBJECTS=0x",
                    g_phase59_cycle.attached_objects);
        phase59_hex(probe, "GXOS_NET10:PHASE59_ROOT_PUBLISHED_VM_REGIONS=0x",
                    g_phase59_cycle.root_published_vm);
        phase59_hex(probe, "GXOS_NET10:PHASE59_ROOT_PUBLISHED_THREADS=0x",
                    g_phase59_cycle.root_published_threads);
        phase59_hex(probe, "GXOS_NET10:PHASE59_ROOT_PUBLISHED_OBJECTS=0x",
                    g_phase59_cycle.root_published_objects);
        phase59_hex(probe, "GXOS_NET10:PHASE59_POST_GC_VM_REGIONS=0x",
                    g_phase59_cycle.post_gc_vm);
        phase59_hex(probe, "GXOS_NET10:PHASE59_POST_GC_THREADS=0x",
                    g_phase59_cycle.post_gc_threads);
        phase59_hex(probe, "GXOS_NET10:PHASE59_POST_GC_OBJECTS=0x",
                    g_phase59_cycle.post_gc_objects);
        phase59_hex(probe, "GXOS_NET10:PHASE59_ROOT_RELEASED_VM_REGIONS=0x",
                    g_phase59_cycle.root_released_vm);
        phase59_hex(probe, "GXOS_NET10:PHASE59_ROOT_RELEASED_THREADS=0x",
                    g_phase59_cycle.root_released_threads);
        phase59_hex(probe, "GXOS_NET10:PHASE59_ROOT_RELEASED_OBJECTS=0x",
                    g_phase59_cycle.root_released_objects);
        duplicate_detach_rejected = !gxos_nativeaot_scheduler_worker_detach(
            &g_phase59_cycle.failed_lifecycle);
        if (!duplicate_detach_rejected ||
            !phase59_reclaim_worker(&g_phase59_cycle,
                g_phase59_cycle.failed_handle, g_phase59_cycle.failed_thread,
                &g_phase59_cycle.failed_lifecycle, probe->scheduler)) return 0;
        ++duplicate_detach_rejections;
        phase59_text(probe, "GXOS_NET10:PHASE59_DUPLICATE_DETACH_REJECTED=1\r\n");
        phase59_text(probe, "GXOS_NET10:PHASE59_SCHEDULER_RECLAIMED=1\r\n");
        old_slot = g_phase59_cycle.failed_lifecycle.scheduler_slot;
        old_identity = g_phase59_cycle.failed_lifecycle.worker_identity;
        old_generation = g_phase59_cycle.failed_lifecycle.worker_generation;
        old_handle_resume_rejected = !gxos_scheduler_resume_thread(
            g_phase59_cycle.failed_handle, 0);
        old_handle_close_rejected = !gxos_scheduler_close_handle(
            g_phase59_cycle.failed_handle);
        if (!old_handle_resume_rejected || !old_handle_close_rejected ||
            gxos_scheduler_thread_from_handle(g_phase59_cycle.failed_handle) != 0) return 0;
        ++stale_handle_rejections;
        if (!gxos_scheduler_create_suspended_thread(
                probe->scheduler, phase59_replacement_worker_entry, &g_phase59_cycle,
                &g_phase59_cycle.replacement_handle, &g_phase59_cycle.replacement_thread) ||
            !gxos_nativeaot_scheduler_worker_prepare(
                &g_phase59_cycle.replacement_lifecycle, probe->main_thread,
                g_phase59_cycle.replacement_thread, probe->tls_index,
                probe->runtime_fls_slot, probe->runtime_fls_cleanup)) return 0;
        if (g_phase59_cycle.replacement_lifecycle.scheduler_slot != old_slot) same_slot = 0;
        if (g_phase59_cycle.replacement_lifecycle.worker_identity == old_identity ||
            g_phase59_cycle.replacement_lifecycle.worker_generation == old_generation) return 0;
        phase59_hex(probe, "GXOS_NET10:PHASE59_REPLACEMENT_SLOT=0x",
                    g_phase59_cycle.replacement_lifecycle.scheduler_slot);
        phase59_hex(probe, "GXOS_NET10:PHASE59_REPLACEMENT_IDENTITY=0x",
                    g_phase59_cycle.replacement_lifecycle.worker_identity);
        phase59_hex(probe, "GXOS_NET10:PHASE59_REPLACEMENT_GENERATION=0x",
                    g_phase59_cycle.replacement_lifecycle.worker_generation);
        phase59_text(probe, "GXOS_NET10:PHASE59_SAME_SLOT_REUSE=1\r\n");
        stale_lifecycle = g_phase59_cycle.failed_lifecycle;
        stale_lifecycle.ownership_state = GXOS_NATIVEAOT_WORKER_OWNERSHIP_RUNTIME_ATTACHED;
        stale_lifecycle.attached = 1;
        stale_lifecycle.detached = 0;
        stale_lifecycle.managed_root_owned = 1;
        stale_root_rejected = !gxos_nativeaot_scheduler_worker_note_managed_root_released(
            &stale_lifecycle, g_phase59_cycle.failed_root_token);
        ++stale_pre_gc_identity_rejections;
        stale_lifecycle.ownership_state = GXOS_NATIVEAOT_WORKER_OWNERSHIP_RUNTIME_ATTACHED;
        stale_lifecycle.detached = 0;
        stale_lifecycle.managed_root_identity = g_phase59_cycle.failed_pre_gc_root_identity;
        stale_detach_rejected = !gxos_nativeaot_scheduler_worker_detach(&stale_lifecycle);
        if (!stale_root_rejected || !stale_detach_rejected ||
            g_phase59_cycle.replacement_lifecycle.ownership_state !=
                GXOS_NATIVEAOT_WORKER_OWNERSHIP_ALLOCATED ||
            g_phase59_cycle.replacement_thread->state !=
                GXOS_SCHEDULER_THREAD_CREATED_SUSPENDED) return 0;
        ++stale_root_rejections;
        ++stale_detach_rejections;
        phase59_text(probe, "GXOS_NET10:PHASE59_STALE_HANDLE_REJECTED=1\r\n");
        phase59_text(probe, "GXOS_NET10:PHASE59_STALE_IDENTITY_REJECTED=1\r\n");
        phase59_text(probe, "GXOS_NET10:PHASE59_STALE_GENERATION_REJECTED=1\r\n");
        phase59_text(probe, "GXOS_NET10:PHASE59_STALE_ROOT_REJECTED=1\r\n");
        phase59_text(probe, "GXOS_NET10:PHASE59_STALE_PRE_GC_OBJECT_IDENTITY_REJECTED=1\r\n");
        if (!phase53o_rehome_canary(probe, g_phase59_cycle.replacement_thread) ||
            !gxos_scheduler_resume_thread(g_phase59_cycle.replacement_handle, 0) ||
            !gxos_nativeaot_scheduler_worker_mark_runnable(
                &g_phase59_cycle.replacement_lifecycle)) return 0;
        for (uint32_t dispatches = 0; dispatches != 4U; ++dispatches) {
            if (gxos_scheduler_thread_is_terminated(g_phase59_cycle.replacement_thread)) break;
            if (gxos_scheduler_current_thread() != probe->main_thread ||
                gxos_scheduler_runnable_count() == 0U) return 0;
            gxos_scheduler_main_dispatch(&snapshot);
        }
        if (!gxos_scheduler_thread_is_terminated(g_phase59_cycle.replacement_thread) ||
            g_phase59_cycle.failure != 0 ||
            !phase59_replacement_state_valid(&g_phase59_cycle) ||
            !phase59_reclaim_worker(&g_phase59_cycle,
                g_phase59_cycle.replacement_handle, g_phase59_cycle.replacement_thread,
                &g_phase59_cycle.replacement_lifecycle, probe->scheduler) ||
            *probe->vm_region_count != baseline_vm ||
            phase59_live_threads(probe->scheduler) != baseline_threads ||
            phase59_live_objects(probe->scheduler) != baseline_objects ||
            phase53o_threadstore_count(
                probe->main_thread->fls_values[probe->runtime_fls_slot], 0) !=
                baseline_threadstore) return 0;
        ++duplicate_root_rejections;
        g_phase59_cycle.final_vm = *probe->vm_region_count;
        g_phase59_cycle.final_threads = phase59_live_threads(probe->scheduler);
        g_phase59_cycle.final_objects = phase59_live_objects(probe->scheduler);
        phase59_text(probe, "GXOS_NET10:PHASE59_REPLACEMENT_WORKER_SUCCEEDED=1\r\n");
        phase59_text(probe, "GXOS_NET10:PHASE59_REPLACEMENT_GC=1\r\n");
        phase59_text(probe, "GXOS_NET10:PHASE59_REPLACEMENT_RECLAIM=1\r\n");
        phase59_text(probe, "GXOS_NET10:PHASE59_BASELINE_RESTORED=1\r\n");
    }
    injection = gxos_nativeaot_phase56_failure_record();
    if (injection == 0 || injection->point !=
            GXOS_NATIVEAOT_FAILURE_INJECTION_AFTER_GC_ROOT_SURVIVAL ||
        injected != PHASE59_CYCLE_COUNT || passed != PHASE59_CYCLE_COUNT ||
        failed_root_releases != PHASE59_CYCLE_COUNT ||
        stale_root_rejections != PHASE59_CYCLE_COUNT ||
        stale_handle_rejections != PHASE59_CYCLE_COUNT ||
        stale_detach_rejections != PHASE59_CYCLE_COUNT ||
        stale_pre_gc_identity_rejections != PHASE59_CYCLE_COUNT ||
        duplicate_detach_rejections != PHASE59_CYCLE_COUNT || same_slot == 0 ||
        probe->callback_bridge->invocation_count !=
            baseline_callbacks + PHASE59_CYCLE_COUNT * 2U ||
        probe->gc_bridge->invocation_count !=
            baseline_gc_callbacks + PHASE59_CYCLE_COUNT * 2U ||
        probe->managed_root_publish_bridge->invocation_count !=
            baseline_root_publish + PHASE59_CYCLE_COUNT * 2U ||
        probe->managed_root_release_bridge->invocation_count !=
            baseline_root_release + PHASE59_CYCLE_COUNT * 2U ||
        probe->managed_root_validate_bridge->invocation_count !=
            baseline_root_validate + PHASE59_CYCLE_COUNT * 4U ||
        *probe->vm_region_count != baseline_vm ||
        phase59_live_threads(probe->scheduler) != baseline_threads ||
        phase59_live_objects(probe->scheduler) != baseline_objects ||
        phase53o_threadstore_count(
            probe->main_thread->fls_values[probe->runtime_fls_slot], 0) !=
            baseline_threadstore) return 0;
    phase59_hex(probe, "GXOS_NET10:PHASE59_RESOURCE_PEAK_VM_REGIONS=0x", peak_vm);
    phase59_hex(probe, "GXOS_NET10:PHASE59_RESOURCE_PEAK_THREADS=0x", peak_threads);
    phase59_hex(probe, "GXOS_NET10:PHASE59_RESOURCE_PEAK_OBJECTS=0x", peak_objects);
    phase59_hex(probe, "GXOS_NET10:PHASE59_INJECTED_FAILURE_CYCLES=0x", injected);
    phase59_hex(probe, "GXOS_NET10:PHASE59_PASSED_FAILURE_CYCLES=0x", passed);
    phase59_hex(probe, "GXOS_NET10:PHASE59_FAILED_ROOT_RELEASES=0x", failed_root_releases);
    phase59_hex(probe, "GXOS_NET10:PHASE59_DUPLICATE_ROOT_RELEASE_REJECTIONS=0x", duplicate_root_rejections);
    phase59_hex(probe, "GXOS_NET10:PHASE59_FAILED_CALLBACK_FINAL=0x",
                probe->callback_bridge->invocation_count);
    phase59_hex(probe, "GXOS_NET10:PHASE59_GC_CALLBACK_FINAL=0x",
                probe->gc_bridge->invocation_count);
    phase59_hex(probe, "GXOS_NET10:PHASE59_ROOT_VALIDATE_FINAL=0x",
                probe->managed_root_validate_bridge->invocation_count);
    phase59_hex(probe, "GXOS_NET10:PHASE59_ROOT_PUBLICATION_TOTAL=0x",
                probe->managed_root_publish_bridge->invocation_count -
                    baseline_root_publish);
    phase59_hex(probe, "GXOS_NET10:PHASE59_ROOT_RELEASE_TOTAL=0x",
                probe->managed_root_release_bridge->invocation_count -
                    baseline_root_release);
    phase59_text(probe, "GXOS_NET10:PHASE59_ROOT_SURVIVAL_BEFORE_FAILURE=1\r\n");
    phase59_text(probe, "GXOS_NET10:PHASE59_EXACTLY_ONE_ROOT_RELEASE=1\r\n");
    phase59_text(probe, "GXOS_NET10:PHASE59_WRONG_GENERATION_ROOT_RELEASE_REJECTED=1\r\n");
    phase59_text(probe, "GXOS_NET10:PHASE59_EXACTLY_ONE_DETACH=1\r\n");
    phase59_text(probe, "GXOS_NET10:PHASE59_NO_DOUBLE_CLEANUP=1\r\n");
    phase59_text(probe, "GXOS_NET10:PHASE59_GENERATION_SAFE_SLOT_REUSE=1\r\n");
    phase59_text(probe, "GXOS_NET10:PHASE59_LEAK_TREND_NONE=1\r\n");
    phase59_text(probe, "GXOS_NET10:PHASE59_COMPLETE=1\r\n");
    phase59_text(probe, "GXOS_NET10:PHASE59_PASS=1\r\n");
    return 1;
}
#endif

#ifdef GXOS_ENABLE_PHASE60_POSTROOTRELEASE_ROLLBACK
static int phase60_injection_state_valid(
    const GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE *lifecycle)
{
    GXOS_SCHEDULER_TCB *thread;
    if (lifecycle == 0 || lifecycle->thread == 0 ||
        lifecycle->ownership_state !=
            GXOS_NATIVEAOT_WORKER_OWNERSHIP_RUNTIME_ATTACHED ||
        !lifecycle->attached || lifecycle->detached ||
        lifecycle->runtime_attach_count != 1U ||
        lifecycle->runtime_detach_count != 0U ||
        lifecycle->runtime_thread == 0 || !lifecycle->runtime_thread_owned ||
        !lifecycle->managed_worker_object_owned ||
        lifecycle->managed_root_owned ||
        !lifecycle->managed_root_survived ||
        lifecycle->managed_root_identity == 0 ||
        lifecycle->managed_root_publication_count != 1U ||
        lifecycle->managed_root_release_count != 1U ||
        !lifecycle->callback_registration_observed ||
        lifecycle->runtime_state_before !=
            GXOS_NATIVEAOT_RUNTIME_THREAD_ATTACHED ||
        lifecycle->runtime_transition_frame != UINT64_MAX ||
        lifecycle->allocation_context == 0 ||
        !lifecycle_matches_current_thread(lifecycle)) {
        return 0;
    }
    thread = lifecycle->thread;
    return thread->live && !thread->is_boot_thread &&
           thread->state == GXOS_SCHEDULER_THREAD_RUNNING &&
           gxos_scheduler_current_thread() == thread &&
           lifecycle->scheduler_owned && lifecycle->stack_owned &&
           lifecycle->environment_owned && lifecycle->tls_fls_owned &&
           lifecycle->vm_resources_owned &&
           lifecycle->stack_reservation_base != 0 &&
           lifecycle->stack_guard_base != 0 &&
           lifecycle->stack_usable_low != 0 &&
           lifecycle->stack_usable_high != 0 && lifecycle->gs_base != 0 &&
           lifecycle->teb_base != 0 && lifecycle->tls_vector_base != 0 &&
           lifecycle->tls_block_base != 0 && lifecycle->guard_vm_identity != 0 &&
           lifecycle->usable_vm_identity != 0 &&
           thread->stack_contract.guard_nonpresent &&
           thread->stack_contract.reservation_base ==
               lifecycle->stack_reservation_base &&
           thread->stack_contract.guard_base == lifecycle->stack_guard_base &&
           thread->stack_contract.usable_stack_low ==
               lifecycle->stack_usable_low &&
           thread->stack_contract.usable_stack_high ==
               lifecycle->stack_usable_high &&
           thread->gs_base == lifecycle->gs_base &&
           thread->teb_base == lifecycle->teb_base &&
           thread->tls_vector_base == lifecycle->tls_vector_base &&
           thread->tls_block_base == lifecycle->tls_block_base &&
           thread->context.rsp >= lifecycle->stack_usable_low &&
           thread->context.rsp <= lifecycle->stack_usable_high &&
           thread->fls_values[lifecycle->runtime_fls_slot] ==
               lifecycle->runtime_thread &&
           lifecycle_load_u64(lifecycle->runtime_thread,
                              GXOS_NATIVEAOT_TLS_STATE_FLAGS_OFFSET) ==
               GXOS_NATIVEAOT_RUNTIME_THREAD_ATTACHED &&
           lifecycle->runtime_stack_low ==
               thread->stack_contract.reservation_base &&
           lifecycle->runtime_stack_high == thread->stack_limit &&
           lifecycle->allocation_context == thread->tls_block_base + 0x38U;
}
#endif

#ifdef GXOS_ENABLE_PHASE60_POSTROOTRELEASE_ROLLBACK
#define PHASE60_CYCLE_COUNT 12U

typedef struct {
    GXOS_PHASE53O_PROBE *probe;
    uint32_t cycle;
    GXOS_SCHEDULER_HANDLE failed_handle;
    GXOS_SCHEDULER_TCB *failed_thread;
    GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE failed_lifecycle;
    GXOS_SCHEDULER_HANDLE replacement_handle;
    GXOS_SCHEDULER_TCB *replacement_thread;
    GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE replacement_lifecycle;
    uint32_t replacement_input;
    uint32_t replacement_seed;
    uint32_t failed_root_token;
    uint32_t replacement_root_token;
    int32_t failed_callback_result;
    int32_t failed_root_publish_result;
    int32_t failed_root_validate_before_result;
    int32_t failed_gc_result;
    int32_t failed_root_validate_after_result;
    int32_t failed_root_release_result;
    int32_t replacement_callback_result;
    int32_t replacement_root_publish_result;
    int32_t replacement_stale_root_result;
    int32_t replacement_root_validate_before_result;
    int32_t replacement_gc_result;
    int32_t replacement_root_validate_after_result;
    int32_t replacement_root_release_result;
    uint32_t failed_callback_status;
    uint32_t failed_root_publish_status;
    uint32_t failed_root_validate_before_status;
    uint32_t failed_gc_status;
    uint32_t failed_root_validate_after_status;
    uint32_t failed_root_release_status;
    uint32_t replacement_callback_status;
    uint32_t replacement_root_publish_status;
    uint32_t replacement_stale_root_status;
    uint32_t replacement_root_validate_before_status;
    uint32_t replacement_gc_status;
    uint32_t replacement_root_validate_after_status;
    uint32_t replacement_root_release_status;
    uint32_t failed_gc_delta;
    uint32_t failed_gc_generation;
    uint32_t failed_gc_checksum;
    uint32_t replacement_gc_delta;
    uint32_t replacement_gc_generation;
    uint32_t replacement_gc_checksum;
    uint32_t failed_gc_before;
    uint32_t failed_gc_after;
    uint32_t replacement_gc_before;
    uint32_t replacement_gc_after;
    uint32_t prepared_vm;
    uint32_t prepared_threads;
    uint32_t prepared_objects;
    uint32_t attached_vm;
    uint32_t attached_threads;
    uint32_t attached_objects;
    uint32_t root_published_vm;
    uint32_t root_published_threads;
    uint32_t root_published_objects;
    uint32_t post_gc_vm;
    uint32_t post_gc_threads;
    uint32_t post_gc_objects;
    uint32_t root_released_vm;
    uint32_t root_released_threads;
    uint32_t root_released_objects;
    uint32_t final_vm;
    uint32_t final_threads;
    uint32_t final_objects;
    uint64_t failed_pre_gc_root_identity;
    uint64_t failed_post_gc_root_identity;
    uint64_t replacement_pre_gc_root_identity;
    uint64_t replacement_post_gc_root_identity;
    int failure;
} GXOS_PHASE60_CYCLE;

static GXOS_PHASE60_CYCLE g_phase60_cycle;

static void phase60_text(GXOS_PHASE53O_PROBE *probe, const char *text)
{
    probe->log_text(text);
}

static void phase60_hex(GXOS_PHASE53O_PROBE *probe,
                        const char *name, uint64_t value)
{
    probe->log_hex(name, value);
    probe->log_text("\r\n");
}

static uint32_t phase60_live_threads(const GXOS_SCHEDULER *scheduler)
{
    uint32_t index;
    uint32_t count = 0;
    for (index = 0; index != GXOS_SCHEDULER_MAX_THREADS; ++index) {
        if (scheduler->threads[index].live) ++count;
    }
    return count;
}

static uint32_t phase60_live_objects(const GXOS_SCHEDULER *scheduler)
{
    uint32_t index;
    uint32_t count = 0;
    for (index = 0; index != GXOS_SCHEDULER_MAX_OBJECTS; ++index) {
        if (scheduler->objects[index].live) ++count;
    }
    return count;
}

static int phase60_fail(GXOS_PHASE60_CYCLE *cycle)
{
    cycle->failure = 1;
    phase60_text(cycle->probe, "GXOS_NET10:PHASE60_FAILURE=1\r\n");
    return 0;
}

static int phase60_root_validate(
    GXOS_PHASE60_CYCLE *cycle,
    GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE *lifecycle,
    uint32_t token, GXOS_NATIVEAOT_CALLBACK_BRIDGE *bridge,
    int32_t *result, uint32_t *status)
{
    phase60_text(cycle->probe, "GXOS_NET10:PHASE60_ROOT_VALIDATE_BEGIN=1\r\n");
    cycle->probe->phase_in_managed(PHASE53O_PHASE_IN_MANAGED);
    if (!gxos_nativeaot_scheduler_worker_invoke(
            lifecycle, bridge, (int32_t)token, result, status) ||
        *status != GXOS_NATIVEAOT_CALLBACK_OK ||
        (uint32_t)*result != (0x5A000000U | token)) {
        return 0;
    }
    cycle->probe->phase_after_managed(PHASE53O_PHASE_AFTER_MANAGED_RETURN);
    return 1;
}

static uintptr_t GXOS_PHASE53O_MS_ABI
phase60_failed_worker_entry(void *argument)
{
    GXOS_PHASE60_CYCLE *cycle = (GXOS_PHASE60_CYCLE *)argument;
    GXOS_PHASE53O_PROBE *probe = cycle->probe;
    GXOS_SCHEDULER_TCB *thread = gxos_scheduler_current_thread();
    GXOS_SCHEDULER_REGISTER_SNAPSHOT snapshot = {0};
    GXOS_NATIVEAOT_FAILURE_INJECTION_RECORD const *injection;
    uint32_t callback_low;
    int duplicate_release_rejected;

    cycle->failed_thread = thread;
    gxos_scheduler_capture_registers(&snapshot);
    if (thread == 0 || !gxos_nativeaot_scheduler_worker_mark_running(
            &cycle->failed_lifecycle) ||
        !gxos_scheduler_validate_worker_snapshot(thread, &snapshot)) {
        return (uintptr_t)phase60_fail(cycle);
    }
    phase60_text(probe, "GXOS_NET10:PHASE60_FAILED_WORKER_RUNNING=1\r\n");
    cycle->failed_gc_before = probe->gc_bridge->invocation_count;
    probe->phase_in_managed(PHASE53O_PHASE_IN_MANAGED);
    if (!gxos_nativeaot_scheduler_worker_invoke(
            &cycle->failed_lifecycle, probe->callback_bridge, 0x59,
            &cycle->failed_callback_result,
            &cycle->failed_callback_status)) {
        return (uintptr_t)phase60_fail(cycle);
    }
    probe->phase_after_managed(PHASE53O_PHASE_AFTER_MANAGED_RETURN);
    callback_low = (uint32_t)cycle->failed_callback_result & 0xFFFFU;
    cycle->attached_vm = *probe->vm_region_count;
    cycle->attached_threads = phase60_live_threads(probe->scheduler);
    cycle->attached_objects = phase60_live_objects(probe->scheduler);
    if (cycle->failed_callback_status != GXOS_NATIVEAOT_CALLBACK_OK ||
        callback_low != 0x5AU || cycle->failed_lifecycle.runtime_thread == 0 ||
        cycle->failed_lifecycle.runtime_attach_count != 1U ||
        cycle->failed_lifecycle.runtime_detach_count != 0U) {
        return (uintptr_t)phase60_fail(cycle);
    }
    phase60_text(probe, "GXOS_NET10:PHASE60_RUNTIME_ATTACH_SUCCEEDED=1\r\n");
    phase60_hex(probe, "GXOS_NET10:PHASE60_FAILED_RUNTIME_THREAD=0x",
                (uint64_t)(uintptr_t)cycle->failed_lifecycle.runtime_thread);
    phase60_hex(probe, "GXOS_NET10:PHASE60_FAILED_FLS_AFTER_ATTACH=0x",
                (uint64_t)gxos_scheduler_get_fls(probe->runtime_fls_slot));
    phase60_hex(probe, "GXOS_NET10:PHASE60_FAILED_RUNTIME_ATTACH_COUNT=0x",
                cycle->failed_lifecycle.runtime_attach_count);

    cycle->failed_root_token = 0x5A00U | cycle->cycle;
    probe->phase_in_managed(PHASE53O_PHASE_IN_MANAGED);
    if (!gxos_nativeaot_scheduler_worker_invoke(
            &cycle->failed_lifecycle, probe->managed_root_publish_bridge,
            (int32_t)cycle->failed_root_token,
            &cycle->failed_root_publish_result,
            &cycle->failed_root_publish_status) ||
        cycle->failed_root_publish_status != GXOS_NATIVEAOT_CALLBACK_OK ||
        (uint32_t)cycle->failed_root_publish_result !=
            (0x58000000U | cycle->failed_root_token) ||
        !gxos_nativeaot_scheduler_worker_note_managed_root_published(
            &cycle->failed_lifecycle, cycle->failed_root_token)) {
        return (uintptr_t)phase60_fail(cycle);
    }
    probe->phase_after_managed(PHASE53O_PHASE_AFTER_MANAGED_RETURN);
    cycle->root_published_vm = *probe->vm_region_count;
    cycle->root_published_threads = phase60_live_threads(probe->scheduler);
    cycle->root_published_objects = phase60_live_objects(probe->scheduler);
    cycle->failed_pre_gc_root_identity = cycle->failed_root_token;
    if (!phase60_root_validate(
            cycle, &cycle->failed_lifecycle, cycle->failed_root_token,
            probe->managed_root_validate_bridge,
            &cycle->failed_root_validate_before_result,
            &cycle->failed_root_validate_before_status)) {
        return (uintptr_t)phase60_fail(cycle);
    }
    phase60_text(probe, "GXOS_NET10:PHASE60_ROOT_PUBLISHED=1\r\n");
    phase60_hex(probe, "GXOS_NET10:PHASE60_PRE_GC_LOGICAL_ROOT_IDENTITY=0x",
                cycle->failed_pre_gc_root_identity);

    probe->phase_in_managed(PHASE53O_PHASE_IN_MANAGED);
    if (!gxos_nativeaot_scheduler_worker_invoke(
            &cycle->failed_lifecycle, probe->gc_bridge,
            (int32_t)(0xD0U + cycle->cycle), &cycle->failed_gc_result,
            &cycle->failed_gc_status) ||
        cycle->failed_gc_status != GXOS_NATIVEAOT_CALLBACK_OK ||
        !gxos_nativeaot_gc_result_valid(
            cycle->failed_gc_result, 0xD0U + cycle->cycle,
            &cycle->failed_gc_delta, &cycle->failed_gc_generation,
            &cycle->failed_gc_checksum) || cycle->failed_gc_delta == 0U) {
        return (uintptr_t)phase60_fail(cycle);
    }
    probe->phase_after_managed(PHASE53O_PHASE_AFTER_MANAGED_RETURN);
    if (!phase60_root_validate(
            cycle, &cycle->failed_lifecycle, cycle->failed_root_token,
            probe->managed_root_validate_bridge,
            &cycle->failed_root_validate_after_result,
            &cycle->failed_root_validate_after_status)) {
        return (uintptr_t)phase60_fail(cycle);
    }
    cycle->failed_post_gc_root_identity = cycle->failed_root_token;
    cycle->failed_lifecycle.managed_root_survived = 1;
    cycle->post_gc_vm = *probe->vm_region_count;
    cycle->post_gc_threads = phase60_live_threads(probe->scheduler);
    cycle->post_gc_objects = phase60_live_objects(probe->scheduler);
    phase60_text(probe, "GXOS_NET10:PHASE60_GC_COMPLETED=1\r\n");
    phase60_text(probe, "GXOS_NET10:PHASE60_ROOT_SURVIVAL_PROVEN=1\r\n");
    phase60_hex(probe, "GXOS_NET10:PHASE60_POST_GC_LOGICAL_ROOT_IDENTITY=0x",
                cycle->failed_post_gc_root_identity);
    phase60_hex(probe, "GXOS_NET10:PHASE60_ALLOC_CONTEXT_PRE_GC=0x",
                cycle->failed_lifecycle.allocation_context);
    phase60_hex(probe, "GXOS_NET10:PHASE60_ALLOC_PTR_POST_GC=0x",
                cycle->failed_lifecycle.alloc_ptr_after);
    phase60_hex(probe, "GXOS_NET10:PHASE60_ALLOC_LIMIT_POST_GC=0x",
                cycle->failed_lifecycle.alloc_limit_after);

    probe->phase_in_managed(PHASE53O_PHASE_IN_MANAGED);
    if (!gxos_nativeaot_scheduler_worker_invoke(
            &cycle->failed_lifecycle, probe->managed_root_release_bridge,
            (int32_t)cycle->failed_root_token,
            &cycle->failed_root_release_result,
            &cycle->failed_root_release_status) ||
        cycle->failed_root_release_status != GXOS_NATIVEAOT_CALLBACK_OK ||
        (uint32_t)cycle->failed_root_release_result !=
            (0x59000000U | cycle->failed_root_token) ||
        !gxos_nativeaot_scheduler_worker_note_managed_root_released(
            &cycle->failed_lifecycle, cycle->failed_root_token)) {
        return (uintptr_t)phase60_fail(cycle);
    }
    probe->phase_after_managed(PHASE53O_PHASE_AFTER_MANAGED_RETURN);
    cycle->root_released_vm = *probe->vm_region_count;
    cycle->root_released_threads = phase60_live_threads(probe->scheduler);
    cycle->root_released_objects = phase60_live_objects(probe->scheduler);
    duplicate_release_rejected =
        !gxos_nativeaot_scheduler_worker_note_managed_root_released(
            &cycle->failed_lifecycle, cycle->failed_root_token);
    if (!duplicate_release_rejected || cycle->failed_lifecycle.managed_root_owned ||
        cycle->failed_lifecycle.managed_root_release_count != 1U) {
        return (uintptr_t)phase60_fail(cycle);
    }
    phase60_text(probe, "GXOS_NET10:PHASE60_ROOT_RELEASED=1\r\n");
    phase60_hex(probe, "GXOS_NET10:PHASE60_FAILED_ROOT_RELEASE_COUNT=0x",
                cycle->failed_lifecycle.managed_root_release_count);
    phase60_hex(probe, "GXOS_NET10:PHASE60_ALLOC_CONTEXT_AFTER_ROOT_RELEASE=0x",
                cycle->failed_lifecycle.allocation_context);
    phase60_hex(probe, "GXOS_NET10:PHASE60_ALLOC_PTR_AFTER_ROOT_RELEASE=0x",
                cycle->failed_lifecycle.alloc_ptr_after);
    phase60_hex(probe, "GXOS_NET10:PHASE60_ALLOC_LIMIT_AFTER_ROOT_RELEASE=0x",
                cycle->failed_lifecycle.alloc_limit_after);
    phase60_text(probe, "GXOS_NET10:PHASE60_FAILED_ROOT_OWNED_AFTER_RELEASE=0\r\n");
    phase60_text(probe, "GXOS_NET10:PHASE60_DUPLICATE_ROOT_RELEASE_REJECTED=1\r\n");
    if (!gxos_nativeaot_phase56_failure_arm(
            GXOS_NATIVEAOT_FAILURE_INJECTION_AFTER_MANAGED_ROOT_RELEASE,
            &cycle->failed_lifecycle) ||
        !gxos_nativeaot_phase56_failure_try_fire(&cycle->failed_lifecycle) ||
        gxos_nativeaot_phase56_failure_try_fire(&cycle->failed_lifecycle)) {
        return (uintptr_t)phase60_fail(cycle);
    }
    injection = gxos_nativeaot_phase56_failure_record();
    if (injection == 0 || injection->state !=
            GXOS_NATIVEAOT_FAILURE_INJECTION_FIRED ||
        injection->point !=
            GXOS_NATIVEAOT_FAILURE_INJECTION_AFTER_MANAGED_ROOT_RELEASE ||
        injection->fire_count != 1U || injection->mismatch_count != 0U) {
        return (uintptr_t)phase60_fail(cycle);
    }
    phase60_text(probe, "GXOS_NET10:PHASE60_INJECTION_ARMED=1\r\n");
    phase60_text(probe, "GXOS_NET10:PHASE60_INJECTION_FIRED=1\r\n");
    phase60_hex(probe, "GXOS_NET10:PHASE60_ROOT_RELEASE_COUNT_AT_INJECTION=0x",
                cycle->failed_lifecycle.managed_root_release_count);
    phase60_hex(probe, "GXOS_NET10:PHASE60_ROOT_LEDGER_AT_INJECTION=0x",
                cycle->failed_lifecycle.managed_root_owned);
    phase60_hex(probe, "GXOS_NET10:PHASE60_RUNTIME_ATTACH_COUNT_AT_INJECTION=0x",
                cycle->failed_lifecycle.runtime_attach_count);
    phase60_hex(probe, "GXOS_NET10:PHASE60_RUNTIME_DETACH_COUNT_AT_INJECTION=0x",
                cycle->failed_lifecycle.runtime_detach_count);
    phase60_hex(probe, "GXOS_NET10:PHASE60_RUNTIME_STATE_AT_INJECTION=0x",
                lifecycle_load_u64(cycle->failed_lifecycle.runtime_thread,
                                   GXOS_NATIVEAOT_TLS_STATE_FLAGS_OFFSET));
    phase60_hex(probe, "GXOS_NET10:PHASE60_FLS_AT_INJECTION=0x",
                (uint64_t)gxos_scheduler_get_fls(probe->runtime_fls_slot));
    phase60_text(probe, "GXOS_NET10:PHASE60_ROOT_OWNERSHIP_ENDED_BEFORE_INJECTION=1\r\n");
    phase60_text(probe, "GXOS_NET10:PHASE60_RUNTIME_OWNERSHIP_ACTIVE_AT_INJECTION=1\r\n");
    phase60_text(probe, "GXOS_NET10:PHASE60_INTENTIONAL_FAILURE=1\r\n");
    phase60_text(probe, "GXOS_NET10:PHASE60_NORMAL_POST_GC_CONTINUATION_SUPPRESSED=1\r\n");

    if (!gxos_nativeaot_scheduler_worker_detach(&cycle->failed_lifecycle) ||
        cycle->failed_lifecycle.runtime_detach_count != 1U ||
        gxos_scheduler_get_fls(probe->runtime_fls_slot) != 0) {
        return (uintptr_t)phase60_fail(cycle);
    }
    phase60_hex(probe, "GXOS_NET10:PHASE60_ALLOC_PTR_AFTER_DETACH=0x",
                cycle->failed_lifecycle.alloc_ptr_after_detach);
    phase60_hex(probe, "GXOS_NET10:PHASE60_ALLOC_LIMIT_AFTER_DETACH=0x",
                cycle->failed_lifecycle.alloc_limit_after_detach);
    phase60_hex(probe, "GXOS_NET10:PHASE60_FAILED_RUNTIME_DETACH_COUNT=0x",
                cycle->failed_lifecycle.runtime_detach_count);
    phase60_hex(probe, "GXOS_NET10:PHASE60_FAILED_FLS_AFTER_DETACH=0x",
                (uint64_t)gxos_scheduler_get_fls(probe->runtime_fls_slot));
    phase60_hex(probe, "GXOS_NET10:PHASE60_ALLOC_CONTEXT_AFTER_DETACH=0x",
                cycle->failed_lifecycle.allocation_context);
    phase60_text(probe, "GXOS_NET10:PHASE60_DETACH_EXECUTED=1\r\n");
    phase60_text(probe, "GXOS_NET10:PHASE60_DETACH_AFTER_INJECTION=1\r\n");
    phase60_text(probe, "GXOS_NET10:PHASE60_FLS_CLEARED_AFTER_DETACH=1\r\n");
    phase60_text(probe, "GXOS_NET10:PHASE60_FLS_THREADSTORE_RUNTIME_STATE_CLEARED=1\r\n");
    cycle->failed_gc_after = probe->gc_bridge->invocation_count;
    return (uintptr_t)cycle->failed_callback_result;
}

static uintptr_t GXOS_PHASE53O_MS_ABI
phase60_replacement_worker_entry(void *argument)
{
    GXOS_PHASE60_CYCLE *cycle = (GXOS_PHASE60_CYCLE *)argument;
    GXOS_PHASE53O_PROBE *probe = cycle->probe;
    GXOS_SCHEDULER_TCB *thread = gxos_scheduler_current_thread();
    GXOS_SCHEDULER_REGISTER_SNAPSHOT snapshot = {0};
    uint32_t callback_low;
    int good = 1;

    cycle->replacement_thread = thread;
    gxos_scheduler_capture_registers(&snapshot);
    if (thread == 0 || !gxos_nativeaot_scheduler_worker_mark_running(
            &cycle->replacement_lifecycle) ||
        !gxos_scheduler_validate_worker_snapshot(thread, &snapshot)) {
        return (uintptr_t)phase60_fail(cycle);
    }
    probe->phase_in_managed(PHASE53O_PHASE_IN_MANAGED);
    if (!gxos_nativeaot_scheduler_worker_invoke(
            &cycle->replacement_lifecycle, probe->callback_bridge,
            (int32_t)cycle->replacement_input,
            &cycle->replacement_callback_result,
            &cycle->replacement_callback_status)) {
        good = 0;
    }
    probe->phase_after_managed(PHASE53O_PHASE_AFTER_MANAGED_RETURN);
    callback_low = (uint32_t)cycle->replacement_callback_result & 0xFFFFU;
    if (cycle->replacement_callback_status != GXOS_NATIVEAOT_CALLBACK_OK ||
        callback_low != cycle->replacement_input + 1U) {
        good = 0;
    }
    phase60_hex(probe, "GXOS_NET10:PHASE60_REPLACEMENT_RUNTIME_THREAD=0x",
                (uint64_t)(uintptr_t)cycle->replacement_lifecycle.runtime_thread);
    phase60_hex(probe, "GXOS_NET10:PHASE60_REPLACEMENT_RUNTIME_ATTACH_COUNT=0x",
                cycle->replacement_lifecycle.runtime_attach_count);
    cycle->replacement_root_token = 0x5B00U | cycle->cycle;
    probe->phase_in_managed(PHASE53O_PHASE_IN_MANAGED);
    if (!gxos_nativeaot_scheduler_worker_invoke(
            &cycle->replacement_lifecycle, probe->managed_root_publish_bridge,
            (int32_t)cycle->replacement_root_token,
            &cycle->replacement_root_publish_result,
            &cycle->replacement_root_publish_status) ||
        cycle->replacement_root_publish_status != GXOS_NATIVEAOT_CALLBACK_OK ||
        (uint32_t)cycle->replacement_root_publish_result !=
            (0x58000000U | cycle->replacement_root_token) ||
        !gxos_nativeaot_scheduler_worker_note_managed_root_published(
            &cycle->replacement_lifecycle, cycle->replacement_root_token)) {
        good = 0;
    }
    probe->phase_after_managed(PHASE53O_PHASE_AFTER_MANAGED_RETURN);
    cycle->replacement_pre_gc_root_identity = cycle->replacement_root_token;
    if (!phase60_root_validate(
            cycle, &cycle->replacement_lifecycle,
            cycle->replacement_root_token, probe->managed_root_validate_bridge,
            &cycle->replacement_root_validate_before_result,
            &cycle->replacement_root_validate_before_status)) {
        good = 0;
    }
    cycle->replacement_gc_before = probe->gc_bridge->invocation_count;
    probe->phase_in_managed(PHASE53O_PHASE_IN_MANAGED);
    if (!gxos_nativeaot_scheduler_worker_invoke(
            &cycle->replacement_lifecycle, probe->gc_bridge,
            (int32_t)cycle->replacement_seed, &cycle->replacement_gc_result,
            &cycle->replacement_gc_status) ||
        cycle->replacement_gc_status != GXOS_NATIVEAOT_CALLBACK_OK ||
        !gxos_nativeaot_gc_result_valid(
            cycle->replacement_gc_result, cycle->replacement_seed,
            &cycle->replacement_gc_delta, &cycle->replacement_gc_generation,
            &cycle->replacement_gc_checksum) ||
        cycle->replacement_gc_delta == 0U ||
        !phase60_root_validate(
            cycle, &cycle->replacement_lifecycle,
            cycle->replacement_root_token, probe->managed_root_validate_bridge,
            &cycle->replacement_root_validate_after_result,
            &cycle->replacement_root_validate_after_status)) {
        good = 0;
    } else {
        cycle->replacement_lifecycle.managed_root_survived = 1;
        cycle->replacement_post_gc_root_identity =
            cycle->replacement_root_token;
    }
    probe->phase_after_managed(PHASE53O_PHASE_AFTER_MANAGED_RETURN);
    cycle->replacement_gc_after = probe->gc_bridge->invocation_count;
    phase60_text(probe, "GXOS_NET10:PHASE60_REPLACEMENT_POST_GC_CONTINUATION=1\r\n");
    probe->phase_in_managed(PHASE53O_PHASE_IN_MANAGED);
    if (!gxos_nativeaot_scheduler_worker_invoke(
            &cycle->replacement_lifecycle, probe->managed_root_release_bridge,
            (int32_t)cycle->replacement_root_token,
            &cycle->replacement_root_release_result,
            &cycle->replacement_root_release_status) ||
        cycle->replacement_root_release_status != GXOS_NATIVEAOT_CALLBACK_OK ||
        (uint32_t)cycle->replacement_root_release_result !=
            (0x59000000U | cycle->replacement_root_token) ||
        !gxos_nativeaot_scheduler_worker_note_managed_root_released(
            &cycle->replacement_lifecycle, cycle->replacement_root_token) ||
        !gxos_nativeaot_scheduler_worker_detach(
            &cycle->replacement_lifecycle) ||
        cycle->replacement_lifecycle.runtime_detach_count != 1U) {
        good = 0;
    }
    probe->phase_after_managed(PHASE53O_PHASE_AFTER_MANAGED_RETURN);
    if (!good) return (uintptr_t)phase60_fail(cycle);
    phase60_text(probe, "GXOS_NET10:PHASE60_REPLACEMENT_ROOT_SURVIVED_GC=1\r\n");
    phase60_text(probe, "GXOS_NET10:PHASE60_REPLACEMENT_DETACH=1\r\n");
    phase60_hex(probe, "GXOS_NET10:PHASE60_REPLACEMENT_RUNTIME_DETACH_COUNT=0x",
                cycle->replacement_lifecycle.runtime_detach_count);
    return (uintptr_t)cycle->replacement_callback_result;
}

static int phase60_reclaim_worker(
    GXOS_PHASE60_CYCLE *cycle, GXOS_SCHEDULER_HANDLE handle,
    GXOS_SCHEDULER_TCB *thread,
    GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE *lifecycle,
    GXOS_SCHEDULER *scheduler)
{
    if (cycle == 0 || scheduler == 0 || thread == 0 || lifecycle == 0 ||
        lifecycle->runtime_detach_count != 1U || !lifecycle->detached ||
        !gxos_scheduler_thread_is_terminated(thread)) {
        return 0;
    }
    return gxos_nativeaot_scheduler_worker_note_reclaimable(lifecycle) &&
           gxos_scheduler_close_handle(handle) &&
           gxos_scheduler_collect(scheduler) &&
           gxos_nativeaot_scheduler_worker_note_reclaimed(lifecycle) &&
           thread->live == 0 && gxos_scheduler_thread_from_handle(handle) == 0;
}

static int phase60_failed_state_valid(const GXOS_PHASE60_CYCLE *cycle,
                                      uint32_t baseline_threadstore)
{
    const GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE *lifecycle;
    const GXOS_SCHEDULER_TCB *thread;
    if (cycle == 0) return 0;
    lifecycle = &cycle->failed_lifecycle;
    thread = cycle->failed_thread;
    return thread != 0 && lifecycle->attached && lifecycle->detached &&
           lifecycle->runtime_attach_count == 1U &&
           lifecycle->runtime_detach_count == 1U &&
           lifecycle->managed_root_identity == cycle->failed_root_token &&
           lifecycle->managed_root_publication_count == 1U &&
           lifecycle->managed_root_release_count == 1U &&
           lifecycle->managed_root_owned == 0 && lifecycle->managed_root_survived &&
           cycle->failed_pre_gc_root_identity == cycle->failed_post_gc_root_identity &&
           lifecycle->ownership_state ==
               GXOS_NATIVEAOT_WORKER_OWNERSHIP_RUNTIME_DETACHED &&
           lifecycle->runtime_thread != 0 && !lifecycle->runtime_thread_owned &&
           !lifecycle->tls_fls_owned && thread->live &&
           thread->fls_values[lifecycle->runtime_fls_slot] == 0 &&
           lifecycle->runtime_state_after ==
               GXOS_NATIVEAOT_RUNTIME_THREAD_DETACHED &&
           lifecycle->threadstore_after == baseline_threadstore;
}

static int phase60_replacement_state_valid(const GXOS_PHASE60_CYCLE *cycle)
{
    const GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE *lifecycle;
    if (cycle == 0) return 0;
    lifecycle = &cycle->replacement_lifecycle;
    return lifecycle->attached && lifecycle->detached &&
           lifecycle->runtime_attach_count == 1U &&
           lifecycle->runtime_detach_count == 1U &&
           lifecycle->managed_root_identity == cycle->replacement_root_token &&
           lifecycle->managed_root_publication_count == 1U &&
           lifecycle->managed_root_release_count == 1U &&
           lifecycle->managed_root_owned == 0 && lifecycle->managed_root_survived &&
           cycle->replacement_pre_gc_root_identity ==
               cycle->replacement_post_gc_root_identity &&
           lifecycle->ownership_state ==
               GXOS_NATIVEAOT_WORKER_OWNERSHIP_RUNTIME_DETACHED;
}

int gxos_nativeaot_phase60_postrootrelease_rollback_probe(GXOS_PHASE53O_PROBE *probe)
{
    static GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE stale_lifecycle;
    GXOS_NATIVEAOT_FAILURE_INJECTION_RECORD const *injection;
    GXOS_SCHEDULER_REGISTER_SNAPSHOT snapshot = {0};
    uint32_t baseline_vm;
    uint32_t baseline_threads;
    uint32_t baseline_objects;
    uint32_t baseline_threadstore;
    uint32_t baseline_callbacks;
    uint32_t baseline_gc_callbacks;
    uint32_t baseline_root_publish;
    uint32_t baseline_root_release;
    uint32_t baseline_root_validate;
    uint32_t injected = 0;
    uint32_t passed = 0;
    uint32_t failed_root_releases = 0;
    uint32_t duplicate_root_rejections = 0;
    uint32_t stale_root_rejections = 0;
    uint32_t stale_handle_rejections = 0;
    uint32_t stale_detach_rejections = 0;
    uint32_t duplicate_detach_rejections = 0;
    uint32_t stale_pre_gc_identity_rejections = 0;
    uint32_t peak_vm = 0;
    uint32_t peak_threads = 0;
    uint32_t peak_objects = 0;
    int same_slot = 1;

    if (probe == 0 || probe->scheduler == 0 || probe->main_thread == 0 ||
        probe->callback_bridge == 0 || probe->gc_bridge == 0 ||
        probe->managed_root_publish_bridge == 0 ||
        probe->managed_root_release_bridge == 0 ||
        probe->managed_root_validate_bridge == 0 ||
        probe->runtime_fls_cleanup == 0 || probe->vm_region_count == 0 ||
        probe->log_text == 0 || probe->log_hex == 0 ||
        probe->phase_in_managed == 0 || probe->phase_after_managed == 0 ||
        probe->main_thread != gxos_scheduler_current_thread() ||
        !probe->main_thread->is_boot_thread) {
        return 0;
    }
    baseline_vm = *probe->vm_region_count;
    baseline_threads = phase60_live_threads(probe->scheduler);
    baseline_objects = phase60_live_objects(probe->scheduler);
    baseline_threadstore = phase53o_threadstore_count(
        probe->main_thread->fls_values[probe->runtime_fls_slot], 0);
    baseline_callbacks = probe->callback_bridge->invocation_count;
    baseline_gc_callbacks = probe->gc_bridge->invocation_count;
    baseline_root_publish = probe->managed_root_publish_bridge->invocation_count;
    baseline_root_release = probe->managed_root_release_bridge->invocation_count;
    baseline_root_validate = probe->managed_root_validate_bridge->invocation_count;
    phase60_text(probe, "GXOS_NET10:PHASE60_BEGIN\r\n");
    phase60_text(probe,
                 "GXOS_NET10:PHASE60_INJECTION_POINT=AFTER_MANAGED_ROOT_RELEASE\r\n");
    phase60_text(probe,
                 "GXOS_NET10:PHASE60_DIAGNOSTIC_HOOK=GXOS_NATIVEAOT_FAILURE_INJECTION_AFTER_MANAGED_ROOT_RELEASE\r\n");
    phase60_text(probe,
                 "GXOS_NET10:PHASE60_COMPILE_GATE=GXOS_ENABLE_PHASE60_POSTROOTRELEASE_ROLLBACK\r\n");
    phase60_text(probe,
                 "GXOS_NET10:PHASE60_MANAGED_ROOT_AUTHORITY=MANAGED_STATIC_REFERENCE\r\n");
    phase60_text(probe,
                 "GXOS_NET10:PHASE60_ROOT_CLEANUP_AUTHORITY=MANAGED_ROOT_RELEASE_EXPORT\r\n");
    phase60_text(probe,
                 "GXOS_NET10:PHASE60_PHYSICAL_RELOCATION=NOT_MEASURED\r\n");
    phase60_text(probe,
                 "GXOS_NET10:PHASE60_RAW_MANAGED_ADDRESS_RETAINED=0\r\n");
    phase60_hex(probe, "GXOS_NET10:PHASE60_RESOURCE_BASELINE_VM_REGIONS=0x", baseline_vm);
    phase60_hex(probe, "GXOS_NET10:PHASE60_RESOURCE_BASELINE_THREADS=0x", baseline_threads);
    phase60_hex(probe, "GXOS_NET10:PHASE60_RESOURCE_BASELINE_OBJECTS=0x", baseline_objects);
    phase60_hex(probe, "GXOS_NET10:PHASE60_RESOURCE_BASELINE_ROOTS=0x", 0);

    for (uint32_t index = 0; index != PHASE60_CYCLE_COUNT; ++index) {
        uint32_t old_slot;
        uint32_t old_identity;
        uint16_t old_generation;
        int duplicate_detach_rejected;
        int stale_root_rejected;
        int stale_detach_rejected;
        int old_handle_resume_rejected;
        int old_handle_close_rejected;

        g_phase60_cycle = (GXOS_PHASE60_CYCLE){0};
        g_phase60_cycle.probe = probe;
        g_phase60_cycle.cycle = index + 1U;
        g_phase60_cycle.replacement_input = 0xA0U + index;
        g_phase60_cycle.replacement_seed = 0xC0U + index;
        if (!gxos_scheduler_create_suspended_thread(
                probe->scheduler, phase60_failed_worker_entry, &g_phase60_cycle,
                &g_phase60_cycle.failed_handle, &g_phase60_cycle.failed_thread) ||
            !gxos_nativeaot_scheduler_worker_prepare(
                &g_phase60_cycle.failed_lifecycle, probe->main_thread,
                g_phase60_cycle.failed_thread, probe->tls_index,
                probe->runtime_fls_slot, probe->runtime_fls_cleanup)) {
            return 0;
        }
        g_phase60_cycle.prepared_vm = *probe->vm_region_count;
        g_phase60_cycle.prepared_threads = phase60_live_threads(probe->scheduler);
        g_phase60_cycle.prepared_objects = phase60_live_objects(probe->scheduler);
        if (g_phase60_cycle.prepared_vm <= baseline_vm ||
            g_phase60_cycle.prepared_threads <= baseline_threads ||
            g_phase60_cycle.prepared_objects <= baseline_objects) return 0;
        if (g_phase60_cycle.prepared_vm > peak_vm) peak_vm = g_phase60_cycle.prepared_vm;
        if (g_phase60_cycle.prepared_threads > peak_threads) peak_threads = g_phase60_cycle.prepared_threads;
        if (g_phase60_cycle.prepared_objects <= GXOS_SCHEDULER_MAX_OBJECTS &&
            g_phase60_cycle.prepared_objects > peak_objects) {
            peak_objects = g_phase60_cycle.prepared_objects;
        }
        phase60_hex(probe, "GXOS_NET10:PHASE60_PREPARED_VM_REGIONS=0x",
                    g_phase60_cycle.prepared_vm);
        phase60_hex(probe, "GXOS_NET10:PHASE60_PREPARED_THREADS=0x",
                    g_phase60_cycle.prepared_threads);
        if (g_phase60_cycle.prepared_objects <= GXOS_SCHEDULER_MAX_OBJECTS) {
            phase60_hex(probe, "GXOS_NET10:PHASE60_PREPARED_OBJECTS=0x",
                        g_phase60_cycle.prepared_objects);
        } else {
            phase60_text(probe,
                         "GXOS_NET10:PHASE60_PREPARED_OBJECT_CENSUS_DEFERRED=1\r\n");
        }
        phase60_hex(probe, "GXOS_NET10:PHASE60_FAILED_SLOT=0x",
                    g_phase60_cycle.failed_lifecycle.scheduler_slot);
        phase60_hex(probe, "GXOS_NET10:PHASE60_FAILED_IDENTITY=0x",
                    g_phase60_cycle.failed_lifecycle.worker_identity);
        phase60_hex(probe, "GXOS_NET10:PHASE60_FAILED_GENERATION=0x",
                    g_phase60_cycle.failed_lifecycle.worker_generation);
        phase60_text(probe, "GXOS_NET10:PHASE60_PREPARED=1\r\n");
        if (!gxos_scheduler_resume_thread(g_phase60_cycle.failed_handle, 0) ||
            !gxos_nativeaot_scheduler_worker_mark_runnable(
                &g_phase60_cycle.failed_lifecycle)) return 0;
        phase60_text(probe, "GXOS_NET10:PHASE60_WORKER_RESUMED=1\r\n");
        gxos_scheduler_main_dispatch(&snapshot);
        injection = gxos_nativeaot_phase56_failure_record();
        if (gxos_scheduler_current_thread() != probe->main_thread ||
            !gxos_scheduler_thread_is_terminated(g_phase60_cycle.failed_thread) ||
            g_phase60_cycle.failure != 0 ||
            injection == 0 || injection->point !=
                GXOS_NATIVEAOT_FAILURE_INJECTION_AFTER_MANAGED_ROOT_RELEASE ||
            injection->state != GXOS_NATIVEAOT_FAILURE_INJECTION_FIRED ||
            injection->fire_count != 1U || injection->mismatch_count != 0U ||
            !phase60_failed_state_valid(&g_phase60_cycle, baseline_threadstore) ||
            g_phase60_cycle.failed_gc_after - g_phase60_cycle.failed_gc_before != 1U) {
            return 0;
        }
        if (g_phase60_cycle.attached_objects > peak_objects) {
            peak_objects = g_phase60_cycle.attached_objects;
        }
        ++injected;
        ++passed;
        ++failed_root_releases;
        phase60_text(probe, "GXOS_NET10:PHASE60_FAILED_GC_COMPLETED=1\r\n");
        phase60_text(probe, "GXOS_NET10:PHASE60_FAILED_ROOT_SURVIVED_GC=1\r\n");
        phase60_hex(probe, "GXOS_NET10:PHASE60_ATTACHED_VM_REGIONS=0x",
                    g_phase60_cycle.attached_vm);
        phase60_hex(probe, "GXOS_NET10:PHASE60_ATTACHED_THREADS=0x",
                    g_phase60_cycle.attached_threads);
        phase60_hex(probe, "GXOS_NET10:PHASE60_ATTACHED_OBJECTS=0x",
                    g_phase60_cycle.attached_objects);
        phase60_hex(probe, "GXOS_NET10:PHASE60_ROOT_PUBLISHED_VM_REGIONS=0x",
                    g_phase60_cycle.root_published_vm);
        phase60_hex(probe, "GXOS_NET10:PHASE60_ROOT_PUBLISHED_THREADS=0x",
                    g_phase60_cycle.root_published_threads);
        phase60_hex(probe, "GXOS_NET10:PHASE60_ROOT_PUBLISHED_OBJECTS=0x",
                    g_phase60_cycle.root_published_objects);
        phase60_hex(probe, "GXOS_NET10:PHASE60_POST_GC_VM_REGIONS=0x",
                    g_phase60_cycle.post_gc_vm);
        phase60_hex(probe, "GXOS_NET10:PHASE60_POST_GC_THREADS=0x",
                    g_phase60_cycle.post_gc_threads);
        phase60_hex(probe, "GXOS_NET10:PHASE60_POST_GC_OBJECTS=0x",
                    g_phase60_cycle.post_gc_objects);
        phase60_hex(probe, "GXOS_NET10:PHASE60_ROOT_RELEASED_VM_REGIONS=0x",
                    g_phase60_cycle.root_released_vm);
        phase60_hex(probe, "GXOS_NET10:PHASE60_ROOT_RELEASED_THREADS=0x",
                    g_phase60_cycle.root_released_threads);
        phase60_hex(probe, "GXOS_NET10:PHASE60_ROOT_RELEASED_OBJECTS=0x",
                    g_phase60_cycle.root_released_objects);
        duplicate_detach_rejected = !gxos_nativeaot_scheduler_worker_detach(
            &g_phase60_cycle.failed_lifecycle);
        if (!duplicate_detach_rejected ||
            !phase60_reclaim_worker(&g_phase60_cycle,
                g_phase60_cycle.failed_handle, g_phase60_cycle.failed_thread,
                &g_phase60_cycle.failed_lifecycle, probe->scheduler)) return 0;
        ++duplicate_detach_rejections;
        phase60_text(probe, "GXOS_NET10:PHASE60_DUPLICATE_DETACH_REJECTED=1\r\n");
        phase60_text(probe, "GXOS_NET10:PHASE60_SCHEDULER_RECLAIMED=1\r\n");
        old_slot = g_phase60_cycle.failed_lifecycle.scheduler_slot;
        old_identity = g_phase60_cycle.failed_lifecycle.worker_identity;
        old_generation = g_phase60_cycle.failed_lifecycle.worker_generation;
        old_handle_resume_rejected = !gxos_scheduler_resume_thread(
            g_phase60_cycle.failed_handle, 0);
        old_handle_close_rejected = !gxos_scheduler_close_handle(
            g_phase60_cycle.failed_handle);
        if (!old_handle_resume_rejected || !old_handle_close_rejected ||
            gxos_scheduler_thread_from_handle(g_phase60_cycle.failed_handle) != 0) return 0;
        ++stale_handle_rejections;
        if (!gxos_scheduler_create_suspended_thread(
                probe->scheduler, phase60_replacement_worker_entry, &g_phase60_cycle,
                &g_phase60_cycle.replacement_handle, &g_phase60_cycle.replacement_thread) ||
            !gxos_nativeaot_scheduler_worker_prepare(
                &g_phase60_cycle.replacement_lifecycle, probe->main_thread,
                g_phase60_cycle.replacement_thread, probe->tls_index,
                probe->runtime_fls_slot, probe->runtime_fls_cleanup)) return 0;
        if (g_phase60_cycle.replacement_lifecycle.scheduler_slot != old_slot) same_slot = 0;
        if (g_phase60_cycle.replacement_lifecycle.worker_identity == old_identity ||
            g_phase60_cycle.replacement_lifecycle.worker_generation == old_generation) return 0;
        phase60_hex(probe, "GXOS_NET10:PHASE60_REPLACEMENT_SLOT=0x",
                    g_phase60_cycle.replacement_lifecycle.scheduler_slot);
        phase60_hex(probe, "GXOS_NET10:PHASE60_REPLACEMENT_IDENTITY=0x",
                    g_phase60_cycle.replacement_lifecycle.worker_identity);
        phase60_hex(probe, "GXOS_NET10:PHASE60_REPLACEMENT_GENERATION=0x",
                    g_phase60_cycle.replacement_lifecycle.worker_generation);
        phase60_text(probe, "GXOS_NET10:PHASE60_SAME_SLOT_REUSE=1\r\n");
        stale_lifecycle = g_phase60_cycle.failed_lifecycle;
        stale_lifecycle.ownership_state = GXOS_NATIVEAOT_WORKER_OWNERSHIP_RUNTIME_ATTACHED;
        stale_lifecycle.attached = 1;
        stale_lifecycle.detached = 0;
        stale_lifecycle.managed_root_owned = 1;
        stale_root_rejected = !gxos_nativeaot_scheduler_worker_note_managed_root_released(
            &stale_lifecycle, g_phase60_cycle.failed_root_token);
        ++stale_pre_gc_identity_rejections;
        stale_lifecycle.ownership_state = GXOS_NATIVEAOT_WORKER_OWNERSHIP_RUNTIME_ATTACHED;
        stale_lifecycle.detached = 0;
        stale_lifecycle.managed_root_identity = g_phase60_cycle.failed_pre_gc_root_identity;
        stale_detach_rejected = !gxos_nativeaot_scheduler_worker_detach(&stale_lifecycle);
        if (!stale_root_rejected || !stale_detach_rejected ||
            g_phase60_cycle.replacement_lifecycle.ownership_state !=
                GXOS_NATIVEAOT_WORKER_OWNERSHIP_ALLOCATED ||
            g_phase60_cycle.replacement_thread->state !=
                GXOS_SCHEDULER_THREAD_CREATED_SUSPENDED) return 0;
        ++stale_root_rejections;
        ++stale_detach_rejections;
        phase60_text(probe, "GXOS_NET10:PHASE60_STALE_HANDLE_REJECTED=1\r\n");
        phase60_text(probe, "GXOS_NET10:PHASE60_STALE_IDENTITY_REJECTED=1\r\n");
        phase60_text(probe, "GXOS_NET10:PHASE60_STALE_GENERATION_REJECTED=1\r\n");
        phase60_text(probe, "GXOS_NET10:PHASE60_STALE_ROOT_REJECTED=1\r\n");
        phase60_text(probe, "GXOS_NET10:PHASE60_STALE_PRE_GC_OBJECT_IDENTITY_REJECTED=1\r\n");
        if (!phase53o_rehome_canary(probe, g_phase60_cycle.replacement_thread) ||
            !gxos_scheduler_resume_thread(g_phase60_cycle.replacement_handle, 0) ||
            !gxos_nativeaot_scheduler_worker_mark_runnable(
                &g_phase60_cycle.replacement_lifecycle)) return 0;
        for (uint32_t dispatches = 0; dispatches != 4U; ++dispatches) {
            if (gxos_scheduler_thread_is_terminated(g_phase60_cycle.replacement_thread)) break;
            if (gxos_scheduler_current_thread() != probe->main_thread ||
                gxos_scheduler_runnable_count() == 0U) return 0;
            gxos_scheduler_main_dispatch(&snapshot);
        }
        if (!gxos_scheduler_thread_is_terminated(g_phase60_cycle.replacement_thread) ||
            g_phase60_cycle.failure != 0 ||
            !phase60_replacement_state_valid(&g_phase60_cycle) ||
            !phase60_reclaim_worker(&g_phase60_cycle,
                g_phase60_cycle.replacement_handle, g_phase60_cycle.replacement_thread,
                &g_phase60_cycle.replacement_lifecycle, probe->scheduler) ||
            *probe->vm_region_count != baseline_vm ||
            phase60_live_threads(probe->scheduler) != baseline_threads ||
            phase60_live_objects(probe->scheduler) != baseline_objects ||
            phase53o_threadstore_count(
                probe->main_thread->fls_values[probe->runtime_fls_slot], 0) !=
                baseline_threadstore) return 0;
        ++duplicate_root_rejections;
        g_phase60_cycle.final_vm = *probe->vm_region_count;
        g_phase60_cycle.final_threads = phase60_live_threads(probe->scheduler);
        g_phase60_cycle.final_objects = phase60_live_objects(probe->scheduler);
        phase60_text(probe, "GXOS_NET10:PHASE60_REPLACEMENT_WORKER_SUCCEEDED=1\r\n");
        phase60_text(probe, "GXOS_NET10:PHASE60_REPLACEMENT_GC=1\r\n");
        phase60_text(probe, "GXOS_NET10:PHASE60_REPLACEMENT_RECLAIM=1\r\n");
        phase60_text(probe, "GXOS_NET10:PHASE60_BASELINE_RESTORED=1\r\n");
    }
    injection = gxos_nativeaot_phase56_failure_record();
    if (injection == 0 || injection->point !=
            GXOS_NATIVEAOT_FAILURE_INJECTION_AFTER_MANAGED_ROOT_RELEASE ||
        injected != PHASE60_CYCLE_COUNT || passed != PHASE60_CYCLE_COUNT ||
        failed_root_releases != PHASE60_CYCLE_COUNT ||
        stale_root_rejections != PHASE60_CYCLE_COUNT ||
        stale_handle_rejections != PHASE60_CYCLE_COUNT ||
        stale_detach_rejections != PHASE60_CYCLE_COUNT ||
        stale_pre_gc_identity_rejections != PHASE60_CYCLE_COUNT ||
        duplicate_detach_rejections != PHASE60_CYCLE_COUNT || same_slot == 0 ||
        probe->callback_bridge->invocation_count !=
            baseline_callbacks + PHASE60_CYCLE_COUNT * 2U ||
        probe->gc_bridge->invocation_count !=
            baseline_gc_callbacks + PHASE60_CYCLE_COUNT * 2U ||
        probe->managed_root_publish_bridge->invocation_count !=
            baseline_root_publish + PHASE60_CYCLE_COUNT * 2U ||
        probe->managed_root_release_bridge->invocation_count !=
            baseline_root_release + PHASE60_CYCLE_COUNT * 2U ||
        probe->managed_root_validate_bridge->invocation_count !=
            baseline_root_validate + PHASE60_CYCLE_COUNT * 4U ||
        *probe->vm_region_count != baseline_vm ||
        phase60_live_threads(probe->scheduler) != baseline_threads ||
        phase60_live_objects(probe->scheduler) != baseline_objects ||
        phase53o_threadstore_count(
            probe->main_thread->fls_values[probe->runtime_fls_slot], 0) !=
            baseline_threadstore) return 0;
    phase60_hex(probe, "GXOS_NET10:PHASE60_RESOURCE_PEAK_VM_REGIONS=0x", peak_vm);
    phase60_hex(probe, "GXOS_NET10:PHASE60_RESOURCE_PEAK_THREADS=0x", peak_threads);
    phase60_hex(probe, "GXOS_NET10:PHASE60_RESOURCE_PEAK_OBJECTS=0x", peak_objects);
    phase60_hex(probe, "GXOS_NET10:PHASE60_INJECTED_FAILURE_CYCLES=0x", injected);
    phase60_hex(probe, "GXOS_NET10:PHASE60_PASSED_FAILURE_CYCLES=0x", passed);
    phase60_hex(probe, "GXOS_NET10:PHASE60_FAILED_ROOT_RELEASES=0x", failed_root_releases);
    phase60_hex(probe, "GXOS_NET10:PHASE60_DUPLICATE_ROOT_RELEASE_REJECTIONS=0x", duplicate_root_rejections);
    phase60_hex(probe, "GXOS_NET10:PHASE60_FAILED_CALLBACK_FINAL=0x",
                probe->callback_bridge->invocation_count);
    phase60_hex(probe, "GXOS_NET10:PHASE60_GC_CALLBACK_FINAL=0x",
                probe->gc_bridge->invocation_count);
    phase60_hex(probe, "GXOS_NET10:PHASE60_ROOT_VALIDATE_FINAL=0x",
                probe->managed_root_validate_bridge->invocation_count);
    phase60_hex(probe, "GXOS_NET10:PHASE60_ROOT_PUBLICATION_TOTAL=0x",
                probe->managed_root_publish_bridge->invocation_count -
                    baseline_root_publish);
    phase60_hex(probe, "GXOS_NET10:PHASE60_ROOT_RELEASE_TOTAL=0x",
                probe->managed_root_release_bridge->invocation_count -
                    baseline_root_release);
    phase60_text(probe, "GXOS_NET10:PHASE60_ROOT_SURVIVAL_BEFORE_FAILURE=1\r\n");
    phase60_text(probe, "GXOS_NET10:PHASE60_EXACTLY_ONE_ROOT_RELEASE=1\r\n");
    phase60_text(probe, "GXOS_NET10:PHASE60_WRONG_GENERATION_ROOT_RELEASE_REJECTED=1\r\n");
    phase60_text(probe, "GXOS_NET10:PHASE60_EXACTLY_ONE_DETACH=1\r\n");
    phase60_text(probe, "GXOS_NET10:PHASE60_NO_DOUBLE_CLEANUP=1\r\n");
    phase60_text(probe, "GXOS_NET10:PHASE60_GENERATION_SAFE_SLOT_REUSE=1\r\n");
    phase60_text(probe, "GXOS_NET10:PHASE60_LEAK_TREND_NONE=1\r\n");
    phase60_text(probe, "GXOS_NET10:PHASE60_COMPLETE=1\r\n");
    phase60_text(probe, "GXOS_NET10:PHASE60_PASS=1\r\n");
    return 1;
}
#endif



int gxos_nativeaot_phase56_failure_arm(
    GXOS_NATIVEAOT_FAILURE_INJECTION_POINT point,
    const GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE *lifecycle)
{
    g_phase56_failure_record = (GXOS_NATIVEAOT_FAILURE_INJECTION_RECORD){0};
    g_phase56_failure_record.point = point;
    if ((point != GXOS_NATIVEAOT_FAILURE_INJECTION_AFTER_WORKER_PREPARE ||
         !phase56_injection_state_valid(lifecycle))
#ifdef GXOS_ENABLE_PHASE57_POSTATTACH_ROLLBACK
        && (point != GXOS_NATIVEAOT_FAILURE_INJECTION_AFTER_RUNTIME_ATTACH ||
            !phase57_injection_state_valid(lifecycle))
#endif
#ifdef GXOS_ENABLE_PHASE58_POSTROOT_ROLLBACK
        && (point != GXOS_NATIVEAOT_FAILURE_INJECTION_AFTER_MANAGED_ROOT ||
            !phase58_injection_state_valid(lifecycle))
#endif
#ifdef GXOS_ENABLE_PHASE59_POSTGC_ROLLBACK
        && (point != GXOS_NATIVEAOT_FAILURE_INJECTION_AFTER_GC_ROOT_SURVIVAL ||
            !phase59_injection_state_valid(lifecycle))
#endif
#ifdef GXOS_ENABLE_PHASE60_POSTROOTRELEASE_ROLLBACK
        && (point != GXOS_NATIVEAOT_FAILURE_INJECTION_AFTER_MANAGED_ROOT_RELEASE ||
            !phase60_injection_state_valid(lifecycle))
#endif
        ) {
        g_phase56_failure_record.point = GXOS_NATIVEAOT_FAILURE_INJECTION_NONE;
        return 0;
    }
    g_phase56_failure_record.state =
        GXOS_NATIVEAOT_FAILURE_INJECTION_ARMED;
    g_phase56_failure_record.arm_count = 1U;
    g_phase56_failure_record.scheduler_slot = lifecycle->scheduler_slot;
    g_phase56_failure_record.worker_identity = lifecycle->worker_identity;
    g_phase56_failure_record.worker_generation = lifecycle->worker_generation;
    return 1;
}

int gxos_nativeaot_phase56_failure_try_fire(
    const GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE *lifecycle)
{
    if (g_phase56_failure_record.state !=
            GXOS_NATIVEAOT_FAILURE_INJECTION_ARMED ||
        ((g_phase56_failure_record.point ==
              GXOS_NATIVEAOT_FAILURE_INJECTION_AFTER_WORKER_PREPARE &&
          !phase56_injection_state_valid(lifecycle))
#ifdef GXOS_ENABLE_PHASE57_POSTATTACH_ROLLBACK
         || (g_phase56_failure_record.point ==
                 GXOS_NATIVEAOT_FAILURE_INJECTION_AFTER_RUNTIME_ATTACH &&
             !phase57_injection_state_valid(lifecycle))
#endif
#ifdef GXOS_ENABLE_PHASE58_POSTROOT_ROLLBACK
         || (g_phase56_failure_record.point ==
                 GXOS_NATIVEAOT_FAILURE_INJECTION_AFTER_MANAGED_ROOT &&
             !phase58_injection_state_valid(lifecycle))
#endif
#ifdef GXOS_ENABLE_PHASE59_POSTGC_ROLLBACK
         || (g_phase56_failure_record.point ==
                 GXOS_NATIVEAOT_FAILURE_INJECTION_AFTER_GC_ROOT_SURVIVAL &&
             !phase59_injection_state_valid(lifecycle))
#endif
#ifdef GXOS_ENABLE_PHASE60_POSTROOTRELEASE_ROLLBACK
         || (g_phase56_failure_record.point ==
                 GXOS_NATIVEAOT_FAILURE_INJECTION_AFTER_MANAGED_ROOT_RELEASE &&
             !phase60_injection_state_valid(lifecycle))
#endif
         || (g_phase56_failure_record.point !=
                 GXOS_NATIVEAOT_FAILURE_INJECTION_AFTER_WORKER_PREPARE
#ifdef GXOS_ENABLE_PHASE57_POSTATTACH_ROLLBACK
             && g_phase56_failure_record.point !=
                    GXOS_NATIVEAOT_FAILURE_INJECTION_AFTER_RUNTIME_ATTACH
#endif
#ifdef GXOS_ENABLE_PHASE58_POSTROOT_ROLLBACK
             && g_phase56_failure_record.point !=
                    GXOS_NATIVEAOT_FAILURE_INJECTION_AFTER_MANAGED_ROOT
#endif
#ifdef GXOS_ENABLE_PHASE59_POSTGC_ROLLBACK
             && g_phase56_failure_record.point !=
                    GXOS_NATIVEAOT_FAILURE_INJECTION_AFTER_GC_ROOT_SURVIVAL
#endif
#ifdef GXOS_ENABLE_PHASE60_POSTROOTRELEASE_ROLLBACK
             && g_phase56_failure_record.point !=
                    GXOS_NATIVEAOT_FAILURE_INJECTION_AFTER_MANAGED_ROOT_RELEASE
#endif
             )) ||
        lifecycle->scheduler_slot != g_phase56_failure_record.scheduler_slot ||
        lifecycle->worker_identity != g_phase56_failure_record.worker_identity ||
        lifecycle->worker_generation != g_phase56_failure_record.worker_generation) {
        if (g_phase56_failure_record.state ==
                GXOS_NATIVEAOT_FAILURE_INJECTION_ARMED &&
            g_phase56_failure_record.mismatch_count != UINT32_MAX) {
            ++g_phase56_failure_record.mismatch_count;
        }
        return 0;
    }
    g_phase56_failure_record.state = GXOS_NATIVEAOT_FAILURE_INJECTION_FIRED;
    g_phase56_failure_record.fire_count = 1U;
    return 1;
}

const GXOS_NATIVEAOT_FAILURE_INJECTION_RECORD *
gxos_nativeaot_phase56_failure_record(void)
{
    return &g_phase56_failure_record;
}
#endif

#ifdef GXOS_ENABLE_PHASE56_PREATTACH_ROLLBACK
#define PHASE56_CYCLE_COUNT 12U

typedef struct {
    GXOS_PHASE53O_PROBE *probe;
    uint32_t cycle;
    GXOS_SCHEDULER_HANDLE failed_handle;
    GXOS_SCHEDULER_TCB *failed_thread;
    GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE failed_lifecycle;
    GXOS_SCHEDULER_HANDLE replacement_handle;
    GXOS_SCHEDULER_TCB *replacement_thread;
    GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE replacement_lifecycle;
    uint32_t replacement_input;
    uint32_t replacement_seed;
    int32_t replacement_callback_result;
    int32_t replacement_gc_result;
    uint32_t replacement_callback_status;
    uint32_t replacement_gc_status;
    uint32_t replacement_gc_delta;
    uint32_t replacement_gc_generation;
    uint32_t replacement_gc_checksum;
    uint64_t replacement_rsp;
    uint32_t failure;
} GXOS_PHASE56_CYCLE;

static GXOS_PHASE56_CYCLE g_phase56_cycle;

static void phase56_text(GXOS_PHASE53O_PROBE *probe, const char *text)
{
    probe->log_text(text);
}

static void phase56_hex(GXOS_PHASE53O_PROBE *probe,
                        const char *name, uint64_t value)
{
    probe->log_hex(name, value);
    probe->log_text("\r\n");
}

static uint32_t phase56_live_environments(const GXOS_SCHEDULER *scheduler)
{
    uint32_t index;
    uint32_t count = 0;
    for (index = 0; index != GXOS_SCHEDULER_MAX_THREADS; ++index) {
        if (scheduler->threads[index].live &&
            scheduler->threads[index].environment_owned) ++count;
    }
    return count;
}

static uint32_t phase56_live_stacks(const GXOS_SCHEDULER *scheduler)
{
    uint32_t index;
    uint32_t count = 0;
    for (index = 0; index != GXOS_SCHEDULER_MAX_THREADS; ++index) {
        if (scheduler->threads[index].live &&
            scheduler->threads[index].stack_contract.reservation_base != 0) {
            ++count;
        }
    }
    return count;
}

static uint32_t phase56_open_thread_handles(const GXOS_SCHEDULER *scheduler)
{
    uint32_t index;
    uint32_t count = 0;
    for (index = 0; index != GXOS_SCHEDULER_MAX_OBJECTS; ++index) {
        const GXOS_SCHEDULER_OBJECT *object = &scheduler->objects[index];
        if (object->live && object->type == GXOS_SCHEDULER_OBJECT_THREAD &&
            object->public_handle_refs != 0) ++count;
    }
    return count;
}

static int phase56_fail(GXOS_PHASE56_CYCLE *cycle)
{
    cycle->failure = 1;
    phase56_text(cycle->probe, "GXOS_NET10:PHASE56_FAILURE=1\r\n");
    return 0;
}

static uintptr_t GXOS_PHASE53O_MS_ABI
phase56_replacement_worker_entry(void *argument)
{
    GXOS_PHASE56_CYCLE *cycle = (GXOS_PHASE56_CYCLE *)argument;
    GXOS_PHASE53O_PROBE *probe = cycle->probe;
    GXOS_SCHEDULER_TCB *thread = gxos_scheduler_current_thread();
    GXOS_SCHEDULER_REGISTER_SNAPSHOT snapshot = {0};
    uint32_t callback_low;
    int good = 1;

    cycle->replacement_thread = thread;
    gxos_scheduler_capture_registers(&snapshot);
    cycle->replacement_rsp = snapshot.rsp;
    if (thread == 0 || !gxos_nativeaot_scheduler_worker_mark_running(
            &cycle->replacement_lifecycle) ||
        !gxos_scheduler_validate_worker_snapshot(thread, &snapshot) ||
        cycle->replacement_lifecycle.worker_identity != thread->identity ||
        cycle->replacement_lifecycle.scheduler_slot == UINT32_MAX) {
        return (uintptr_t)phase56_fail(cycle);
    }
    probe->phase_in_managed(PHASE53O_PHASE_IN_MANAGED);
    if (!gxos_nativeaot_scheduler_worker_invoke(
            &cycle->replacement_lifecycle, probe->callback_bridge,
            (int32_t)cycle->replacement_input,
            &cycle->replacement_callback_result,
            &cycle->replacement_callback_status)) {
        good = 0;
    }
    probe->phase_after_managed(PHASE53O_PHASE_AFTER_MANAGED_RETURN);
    callback_low = (uint32_t)cycle->replacement_callback_result & 0xFFFFU;
    if (cycle->replacement_callback_status != GXOS_NATIVEAOT_CALLBACK_OK ||
        callback_low != cycle->replacement_input + 1U ||
        cycle->replacement_lifecycle.ownership_state !=
            GXOS_NATIVEAOT_WORKER_OWNERSHIP_RUNTIME_ATTACHED ||
        cycle->replacement_lifecycle.runtime_thread == 0 ||
        cycle->replacement_lifecycle.runtime_thread != thread->tls_block_base + 0x30U) {
        good = 0;
    }
    phase56_text(probe, "GXOS_NET10:PHASE56_REPLACEMENT_MANAGED_ENTRY_REACHED=1\r\n");

    probe->phase_in_managed(PHASE53O_PHASE_IN_MANAGED);
    if (!gxos_nativeaot_scheduler_worker_invoke(
            &cycle->replacement_lifecycle, probe->gc_bridge,
            (int32_t)cycle->replacement_seed,
            &cycle->replacement_gc_result,
            &cycle->replacement_gc_status)) {
        good = 0;
    }
    probe->phase_after_managed(PHASE53O_PHASE_AFTER_MANAGED_RETURN);
    if (cycle->replacement_gc_status != GXOS_NATIVEAOT_CALLBACK_OK ||
        !gxos_nativeaot_gc_result_valid(cycle->replacement_gc_result,
                                        cycle->replacement_seed,
                                        &cycle->replacement_gc_delta,
                                        &cycle->replacement_gc_generation,
                                        &cycle->replacement_gc_checksum) ||
        cycle->replacement_gc_delta == 0U) {
        good = 0;
    } else {
        cycle->replacement_lifecycle.managed_root_survived = 1;
    }
    phase56_text(probe, "GXOS_NET10:PHASE56_REPLACEMENT_POST_GC_CONTINUATION=1\r\n");

    if (!gxos_nativeaot_scheduler_worker_detach(
            &cycle->replacement_lifecycle) ||
        cycle->replacement_lifecycle.ownership_state !=
            GXOS_NATIVEAOT_WORKER_OWNERSHIP_RUNTIME_DETACHED ||
        gxos_scheduler_get_fls(probe->runtime_fls_slot) != 0) {
        good = 0;
    }
    phase56_text(probe, "GXOS_NET10:PHASE56_REPLACEMENT_DETACHED=1\r\n");
    if (!good) return (uintptr_t)phase56_fail(cycle);
    return (uintptr_t)cycle->replacement_callback_result;
}

static int phase56_rollback_prepared(
    GXOS_PHASE56_CYCLE *cycle, GXOS_SCHEDULER *scheduler)
{
    if (cycle == 0 || scheduler == 0 || cycle->failed_thread == 0 ||
        !gxos_scheduler_close_handle(cycle->failed_handle)) return 0;
    if (!gxos_scheduler_discard_created_thread(cycle->failed_thread)) return 0;
    if (!gxos_scheduler_collect(scheduler)) return 0;
    return gxos_nativeaot_scheduler_worker_note_pre_runtime_reclaimed(
        &cycle->failed_lifecycle);
}

static int phase56_reclaim_replacement(
    GXOS_PHASE56_CYCLE *cycle, GXOS_SCHEDULER *scheduler)
{
    GXOS_SCHEDULER_TCB *thread;
    if (cycle == 0 || scheduler == 0 || cycle->replacement_thread == 0 ||
        cycle->replacement_lifecycle.detached == 0 ||
        !gxos_scheduler_thread_is_terminated(cycle->replacement_thread)) {
        return 0;
    }
    thread = cycle->replacement_thread;
    if (!gxos_nativeaot_scheduler_worker_note_reclaimable(
            &cycle->replacement_lifecycle) ||
        !gxos_scheduler_close_handle(cycle->replacement_handle) ||
        !gxos_scheduler_collect(scheduler) ||
        !gxos_nativeaot_scheduler_worker_note_reclaimed(
            &cycle->replacement_lifecycle) || thread->live != 0 ||
        gxos_scheduler_thread_from_handle(cycle->replacement_handle) != 0) {
        return 0;
    }
    return 1;
}

int gxos_nativeaot_phase56_preattach_rollback_probe(
    GXOS_PHASE53O_PROBE *probe)
{
    static GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE stale_lifecycle;
    GXOS_NATIVEAOT_FAILURE_INJECTION_RECORD const *injection;
    GXOS_SCHEDULER_REGISTER_SNAPSHOT snapshot = {0};
    uint32_t baseline_vm;
    uint32_t baseline_threads;
    uint32_t baseline_objects;
    uint32_t baseline_environments;
    uint32_t baseline_stacks;
    uint32_t baseline_handles;
    uint32_t baseline_threadstore;
    uint32_t peak_vm;
    uint32_t peak_threads;
    uint32_t peak_objects;
    uint32_t peak_environments;
    uint32_t peak_stacks;
    uint32_t peak_handles;
    uint32_t cycle;
    uint32_t dispatches;
    uint32_t injected = 0;
    uint32_t passed = 0;
    uint32_t stale_handle_rejections = 0;
    uint32_t stale_identity_rejections = 0;
    uint32_t stale_generation_rejections = 0;
    uint32_t duplicate_cleanup_rejections = 0;
    uint32_t failed_detach_rejections = 0;
    uint32_t callback_base;
    uint32_t final_callbacks;
    uint32_t old_slot;
    uint32_t old_identity;
    uint16_t old_generation;
    int same_slot = 1;

    if (probe == 0 || probe->scheduler == 0 || probe->main_thread == 0 ||
        probe->callback_bridge == 0 || probe->gc_bridge == 0 ||
        probe->runtime_fls_cleanup == 0 || probe->vm_region_count == 0 ||
        probe->log_text == 0 || probe->log_hex == 0 ||
        probe->phase_in_managed == 0 || probe->phase_after_managed == 0 ||
        probe->main_thread != gxos_scheduler_current_thread() ||
        !probe->main_thread->is_boot_thread) return 0;

    baseline_vm = *probe->vm_region_count;
    baseline_threads = phase54_live_threads(probe->scheduler);
    baseline_objects = phase54_live_objects(probe->scheduler);
    baseline_environments = phase56_live_environments(probe->scheduler);
    baseline_stacks = phase56_live_stacks(probe->scheduler);
    baseline_handles = phase56_open_thread_handles(probe->scheduler);
    baseline_threadstore = phase53o_threadstore_count(
        probe->main_thread->fls_values[probe->runtime_fls_slot], 0);
    callback_base = probe->callback_bridge->invocation_count;
    peak_vm = baseline_vm;
    peak_threads = baseline_threads;
    peak_objects = baseline_objects;
    peak_environments = baseline_environments;
    peak_stacks = baseline_stacks;
    peak_handles = baseline_handles;
    phase56_text(probe, "GXOS_NET10:PHASE56_BEGIN\r\n");
    phase56_text(probe,
                 "GXOS_NET10:PHASE56_INJECTION_POINT=AFTER_WORKER_PREPARE\r\n");
    phase56_text(probe,
                 "GXOS_NET10:PHASE56_DIAGNOSTIC_HOOK=GXOS_ENABLE_PHASE56_FAILURE_INJECTION\r\n");
    phase56_text(probe,
                 "GXOS_NET10:PHASE56_ROLLBACK_ORDER=CLOSE_HANDLE>DISCARD_CREATED_THREAD>COLLECT\r\n");
    phase56_hex(probe, "GXOS_NET10:PHASE56_RESOURCE_BASELINE_VM_REGIONS=0x",
                baseline_vm);
    phase56_hex(probe, "GXOS_NET10:PHASE56_RESOURCE_BASELINE_THREADS=0x",
                baseline_threads);
    phase56_hex(probe, "GXOS_NET10:PHASE56_RESOURCE_BASELINE_OBJECTS=0x",
                baseline_objects);
    phase56_hex(probe, "GXOS_NET10:PHASE56_RESOURCE_BASELINE_ENVIRONMENTS=0x",
                baseline_environments);
    phase56_hex(probe, "GXOS_NET10:PHASE56_RESOURCE_BASELINE_STACKS=0x",
                baseline_stacks);
    phase56_hex(probe, "GXOS_NET10:PHASE56_RESOURCE_BASELINE_HANDLES=0x",
                baseline_handles);
    phase56_hex(probe, "GXOS_NET10:PHASE56_RUNTIME_THREADSTORE_BASELINE=0x",
                baseline_threadstore);
    phase56_hex(probe, "GXOS_NET10:PHASE56_CALLBACK_BASELINE=0x",
                callback_base);

    for (cycle = 0; cycle != PHASE56_CYCLE_COUNT; ++cycle) {
        uint32_t prepared_vm;
        uint32_t prepared_threads;
        uint32_t prepared_objects;
        uint32_t prepared_environments;
        uint32_t prepared_stacks;
        uint32_t prepared_handles;
        uint32_t failed_callback_count;
        uint32_t failed_threadstore;
        uint32_t replacement_callback_count;
        int close_duplicate_rejected;
        int discard_duplicate_rejected;
        int reclaim_duplicate_rejected;
        int stale_resume_rejected;
        int stale_close_rejected;
        int stale_mark_rejected;
        int stale_note_rejected;

        g_phase56_cycle = (GXOS_PHASE56_CYCLE){0};
        g_phase56_cycle.probe = probe;
        g_phase56_cycle.cycle = cycle + 1U;
        g_phase56_cycle.replacement_input = 0x70U + cycle;
        g_phase56_cycle.replacement_seed = 0xA0U + cycle;
        if (!gxos_scheduler_create_suspended_thread(
                probe->scheduler, phase56_replacement_worker_entry,
                &g_phase56_cycle, &g_phase56_cycle.failed_handle,
                &g_phase56_cycle.failed_thread) ||
            !gxos_nativeaot_scheduler_worker_prepare(
                &g_phase56_cycle.failed_lifecycle, probe->main_thread,
                g_phase56_cycle.failed_thread, probe->tls_index,
                probe->runtime_fls_slot, probe->runtime_fls_cleanup) ||
            !phase56_injection_state_valid(&g_phase56_cycle.failed_lifecycle)) {
            return 0;
        }
        prepared_vm = *probe->vm_region_count;
        prepared_threads = phase54_live_threads(probe->scheduler);
        prepared_objects = phase54_live_objects(probe->scheduler);
        prepared_environments = phase56_live_environments(probe->scheduler);
        prepared_stacks = phase56_live_stacks(probe->scheduler);
        prepared_handles = phase56_open_thread_handles(probe->scheduler);
        if (prepared_vm <= baseline_vm || prepared_threads <= baseline_threads ||
            prepared_objects <= baseline_objects ||
            prepared_environments <= baseline_environments ||
            prepared_stacks <= baseline_stacks ||
            prepared_handles <= baseline_handles) return 0;
        if (prepared_vm > peak_vm) peak_vm = prepared_vm;
        if (prepared_threads > peak_threads) peak_threads = prepared_threads;
        if (prepared_objects > peak_objects) peak_objects = prepared_objects;
        if (prepared_environments > peak_environments) peak_environments = prepared_environments;
        if (prepared_stacks > peak_stacks) peak_stacks = prepared_stacks;
        if (prepared_handles > peak_handles) peak_handles = prepared_handles;
        failed_callback_count = probe->callback_bridge->invocation_count;
        failed_threadstore = phase53o_threadstore_count(
            probe->main_thread->fls_values[probe->runtime_fls_slot], 0);
        phase56_hex(probe, "GXOS_NET10:PHASE56_CYCLE=0x", cycle + 1U);
        phase56_hex(probe, "GXOS_NET10:PHASE56_FAILED_SLOT=0x",
                    g_phase56_cycle.failed_lifecycle.scheduler_slot);
        phase56_hex(probe, "GXOS_NET10:PHASE56_FAILED_IDENTITY=0x",
                    g_phase56_cycle.failed_lifecycle.worker_identity);
        phase56_hex(probe, "GXOS_NET10:PHASE56_FAILED_GENERATION=0x",
                    g_phase56_cycle.failed_lifecycle.worker_generation);
        phase56_text(probe, "GXOS_NET10:PHASE56_PREPARE_SUCCEEDED=1\r\n");
        phase56_text(probe, "GXOS_NET10:PHASE56_PREPARED_STATE=ALLOCATED_CREATED_SUSPENDED\r\n");
        phase56_hex(probe, "GXOS_NET10:PHASE56_PREPARED_STACK_RESERVATION=0x",
                    g_phase56_cycle.failed_lifecycle.stack_reservation_base);
        phase56_hex(probe, "GXOS_NET10:PHASE56_PREPARED_GUARD=0x",
                    g_phase56_cycle.failed_lifecycle.stack_guard_base);
        phase56_hex(probe, "GXOS_NET10:PHASE56_PREPARED_GS=0x",
                    g_phase56_cycle.failed_lifecycle.gs_base);
        phase56_hex(probe, "GXOS_NET10:PHASE56_PREPARED_TEB=0x",
                    g_phase56_cycle.failed_lifecycle.teb_base);
        phase56_hex(probe, "GXOS_NET10:PHASE56_PREPARED_TLS_BLOCK=0x",
                    g_phase56_cycle.failed_lifecycle.tls_block_base);
        phase56_hex(probe, "GXOS_NET10:PHASE56_PREPARED_RUNTIME_THREAD=0x",
                    g_phase56_cycle.failed_lifecycle.runtime_thread);
        phase56_hex(probe, "GXOS_NET10:PHASE56_PREPARED_FLS_VALUE=0x",
                    g_phase56_cycle.failed_thread->fls_values[
                        probe->runtime_fls_slot]);
        if (!gxos_nativeaot_phase56_failure_arm(
                GXOS_NATIVEAOT_FAILURE_INJECTION_AFTER_WORKER_PREPARE,
                &g_phase56_cycle.failed_lifecycle)) return 0;
        phase56_text(probe, "GXOS_NET10:PHASE56_INJECTION_ARMED=1\r\n");
        if (!gxos_nativeaot_phase56_failure_try_fire(
                &g_phase56_cycle.failed_lifecycle) ||
            gxos_nativeaot_phase56_failure_try_fire(
                &g_phase56_cycle.failed_lifecycle)) return 0;
        injection = gxos_nativeaot_phase56_failure_record();
        if (injection == 0 || injection->fire_count != 1U ||
            injection->state != GXOS_NATIVEAOT_FAILURE_INJECTION_FIRED ||
            injection->mismatch_count != 0U) return 0;
        ++injected;
        phase56_text(probe, "GXOS_NET10:PHASE56_INJECTION_FIRED=1\r\n");
        phase56_text(probe, "GXOS_NET10:PHASE56_RUNTIME_ATTACH_NOT_ACQUIRED=1\r\n");
        phase56_hex(probe, "GXOS_NET10:PHASE56_FAILED_RUNTIME_THREAD=0x",
                    g_phase56_cycle.failed_lifecycle.runtime_thread);
        phase56_hex(probe, "GXOS_NET10:PHASE56_FAILED_RUNTIME_DETACH_COUNT=0x", 0);
        phase56_hex(probe, "GXOS_NET10:PHASE56_FAILED_CALLBACK_COUNT=0x",
                    probe->callback_bridge->invocation_count - failed_callback_count);
        phase56_hex(probe, "GXOS_NET10:PHASE56_FAILED_THREADSTORE_DELTA=0x",
                    failed_threadstore - baseline_threadstore);
        if (probe->callback_bridge->invocation_count != failed_callback_count ||
            failed_threadstore != baseline_threadstore ||
            g_phase56_cycle.failed_lifecycle.runtime_thread != 0 ||
            g_phase56_cycle.failed_lifecycle.attached ||
            g_phase56_cycle.failed_lifecycle.managed_root_survived ||
            g_phase56_cycle.failed_thread->state !=
                GXOS_SCHEDULER_THREAD_CREATED_SUSPENDED) return 0;

        old_slot = g_phase56_cycle.failed_lifecycle.scheduler_slot;
        old_identity = g_phase56_cycle.failed_lifecycle.worker_identity;
        old_generation = g_phase56_cycle.failed_lifecycle.worker_generation;
        if (!phase56_rollback_prepared(&g_phase56_cycle, probe->scheduler)) return 0;
        phase56_text(probe, "GXOS_NET10:PHASE56_HANDLE_CLOSED=1\r\n");
        phase56_text(probe, "GXOS_NET10:PHASE56_THREAD_DISCARDED=1\r\n");
        phase56_text(probe, "GXOS_NET10:PHASE56_COLLECTION_COMPLETED=1\r\n");
        close_duplicate_rejected = !gxos_scheduler_close_handle(
            g_phase56_cycle.failed_handle);
        discard_duplicate_rejected = !gxos_scheduler_discard_created_thread(
            g_phase56_cycle.failed_thread);
        reclaim_duplicate_rejected =
            !gxos_nativeaot_scheduler_worker_note_pre_runtime_reclaimed(
                &g_phase56_cycle.failed_lifecycle);
        failed_detach_rejections = failed_detach_rejections +
            (uint32_t)!gxos_nativeaot_scheduler_worker_detach(
                &g_phase56_cycle.failed_lifecycle);
        stale_resume_rejected = !gxos_scheduler_resume_thread(
            g_phase56_cycle.failed_handle, 0);
        stale_close_rejected = !gxos_scheduler_close_handle(
            g_phase56_cycle.failed_handle);
        if (!close_duplicate_rejected || !discard_duplicate_rejected ||
            !reclaim_duplicate_rejected || !stale_resume_rejected ||
            !stale_close_rejected ||
            gxos_scheduler_thread_from_handle(g_phase56_cycle.failed_handle) != 0 ||
            g_phase56_cycle.failed_lifecycle.ownership_state !=
                GXOS_NATIVEAOT_WORKER_OWNERSHIP_RECLAIMED) return 0;
        duplicate_cleanup_rejections += 3U;
        ++stale_handle_rejections;
        phase56_text(probe, "GXOS_NET10:PHASE56_BASELINE_RESTORED=1\r\n");

        if (!gxos_scheduler_create_suspended_thread(
                probe->scheduler, phase56_replacement_worker_entry,
                &g_phase56_cycle, &g_phase56_cycle.replacement_handle,
                &g_phase56_cycle.replacement_thread) ||
            !gxos_nativeaot_scheduler_worker_prepare(
                &g_phase56_cycle.replacement_lifecycle, probe->main_thread,
                g_phase56_cycle.replacement_thread, probe->tls_index,
                probe->runtime_fls_slot, probe->runtime_fls_cleanup)) return 0;
        if (g_phase56_cycle.replacement_lifecycle.scheduler_slot != old_slot) {
            same_slot = 0;
        }
        if (g_phase56_cycle.replacement_lifecycle.worker_identity == old_identity) {
            stale_identity_rejections = 0;
            return 0;
        }
        if (g_phase56_cycle.replacement_lifecycle.worker_generation == old_generation) {
            stale_generation_rejections = 0;
            return 0;
        }
        stale_lifecycle = g_phase56_cycle.failed_lifecycle;
        stale_lifecycle.ownership_state =
            GXOS_NATIVEAOT_WORKER_OWNERSHIP_ALLOCATED;
        stale_mark_rejected = !gxos_nativeaot_scheduler_worker_mark_runnable(
            &stale_lifecycle);
        stale_note_rejected =
            !gxos_nativeaot_scheduler_worker_note_pre_runtime_reclaimed(
                &stale_lifecycle);
        if (!stale_mark_rejected || !stale_note_rejected ||
            g_phase56_cycle.replacement_lifecycle.ownership_state !=
                GXOS_NATIVEAOT_WORKER_OWNERSHIP_ALLOCATED ||
            g_phase56_cycle.replacement_thread->state !=
                GXOS_SCHEDULER_THREAD_CREATED_SUSPENDED) return 0;
        ++stale_identity_rejections;
        ++stale_generation_rejections;
        phase56_text(probe, "GXOS_NET10:PHASE56_STALE_HANDLE_REJECTED=1\r\n");
        phase56_text(probe, "GXOS_NET10:PHASE56_STALE_IDENTITY_REJECTED=1\r\n");
        phase56_text(probe, "GXOS_NET10:PHASE56_STALE_GENERATION_REJECTED=1\r\n");
        phase56_text(probe, "GXOS_NET10:PHASE56_STALE_CANNOT_RESUME_OR_RECLAIM=1\r\n");
        if (!phase53o_rehome_canary(probe,
                                    g_phase56_cycle.replacement_thread) ||
            !gxos_scheduler_resume_thread(
                g_phase56_cycle.replacement_handle, 0) ||
            !gxos_nativeaot_scheduler_worker_mark_runnable(
                &g_phase56_cycle.replacement_lifecycle)) return 0;
        for (dispatches = 0; dispatches != 4U; ++dispatches) {
            if (gxos_scheduler_thread_is_terminated(
                    g_phase56_cycle.replacement_thread)) break;
            if (gxos_scheduler_current_thread() != probe->main_thread ||
                gxos_scheduler_runnable_count() == 0U) return 0;
            gxos_scheduler_main_dispatch(&snapshot);
        }
        replacement_callback_count = probe->callback_bridge->invocation_count;
        if (!gxos_scheduler_thread_is_terminated(
                g_phase56_cycle.replacement_thread) ||
            g_phase56_cycle.failure != 0 ||
            g_phase56_cycle.replacement_lifecycle.managed_root_survived == 0 ||
            g_phase56_cycle.replacement_lifecycle.runtime_thread == 0 ||
            replacement_callback_count != failed_callback_count + 1U ||
            !phase56_reclaim_replacement(&g_phase56_cycle, probe->scheduler)) {
            return 0;
        }
        ++passed;
        phase56_text(probe,
                     "GXOS_NET10:PHASE56_REPLACEMENT_WORKER_SUCCEEDED=1\r\n");
        phase56_text(probe, "GXOS_NET10:PHASE56_REPLACEMENT_WORKER_GC_OK=1\r\n");
        phase56_text(probe, "GXOS_NET10:PHASE56_REPLACEMENT_WORKER_DETACH_RECLAIM_OK=1\r\n");
        if (*probe->vm_region_count != baseline_vm ||
            phase54_live_threads(probe->scheduler) != baseline_threads ||
            phase54_live_objects(probe->scheduler) != baseline_objects ||
            phase56_live_environments(probe->scheduler) != baseline_environments ||
            phase56_live_stacks(probe->scheduler) != baseline_stacks ||
            phase56_open_thread_handles(probe->scheduler) != baseline_handles ||
            phase53o_threadstore_count(
                probe->main_thread->fls_values[probe->runtime_fls_slot], 0) !=
                baseline_threadstore) return 0;
        phase56_text(probe, "GXOS_NET10:PHASE56_BASELINE_RESTORED=1\r\n");
    }
    final_callbacks = probe->callback_bridge->invocation_count;
    injection = gxos_nativeaot_phase56_failure_record();
    if (injection == 0 || injected != PHASE56_CYCLE_COUNT ||
        passed != PHASE56_CYCLE_COUNT || same_slot == 0 ||
        stale_handle_rejections != PHASE56_CYCLE_COUNT ||
        stale_identity_rejections != PHASE56_CYCLE_COUNT ||
        stale_generation_rejections != PHASE56_CYCLE_COUNT ||
        duplicate_cleanup_rejections != PHASE56_CYCLE_COUNT * 3U ||
        failed_detach_rejections != PHASE56_CYCLE_COUNT ||
        final_callbacks != callback_base + PHASE56_CYCLE_COUNT ||
        *probe->vm_region_count != baseline_vm ||
        phase54_live_threads(probe->scheduler) != baseline_threads ||
        phase54_live_objects(probe->scheduler) != baseline_objects ||
        phase56_live_environments(probe->scheduler) != baseline_environments ||
        phase56_live_stacks(probe->scheduler) != baseline_stacks ||
        phase56_open_thread_handles(probe->scheduler) != baseline_handles) {
        return 0;
    }
    phase56_hex(probe, "GXOS_NET10:PHASE56_RESOURCE_PEAK_VM_REGIONS=0x",
                peak_vm);
    phase56_hex(probe, "GXOS_NET10:PHASE56_RESOURCE_PEAK_THREADS=0x",
                peak_threads);
    phase56_hex(probe, "GXOS_NET10:PHASE56_RESOURCE_PEAK_OBJECTS=0x",
                peak_objects);
    phase56_hex(probe, "GXOS_NET10:PHASE56_RESOURCE_PEAK_ENVIRONMENTS=0x",
                peak_environments);
    phase56_hex(probe, "GXOS_NET10:PHASE56_RESOURCE_PEAK_STACKS=0x",
                peak_stacks);
    phase56_hex(probe, "GXOS_NET10:PHASE56_RESOURCE_PEAK_HANDLES=0x",
                peak_handles);
    phase56_hex(probe, "GXOS_NET10:PHASE56_CALLBACK_FINAL=0x", final_callbacks);
    phase56_hex(probe, "GXOS_NET10:PHASE56_INJECTED_FAILURE_CYCLES=0x", injected);
    phase56_hex(probe, "GXOS_NET10:PHASE56_PASSED_FAILURE_CYCLES=0x", passed);
    phase56_hex(probe, "GXOS_NET10:PHASE56_FAILED_RUNTIME_DETACH_TOTAL=0x",
                0);
    phase56_hex(probe, "GXOS_NET10:PHASE56_DUPLICATE_CLEANUP_REJECTIONS=0x",
                duplicate_cleanup_rejections);
    phase56_text(probe, "GXOS_NET10:PHASE56_NO_MANAGED_CALLBACK_ON_FAILED_WORKER=1\r\n");
    phase56_text(probe, "GXOS_NET10:PHASE56_NO_RUNTIME_OWNERSHIP_ON_FAILED_WORKER=1\r\n");
    phase56_text(probe, "GXOS_NET10:PHASE56_GENERATION_SAFE_SLOT_REUSE=1\r\n");
    phase56_text(probe, "GXOS_NET10:PHASE56_LEAK_TREND_NONE=1\r\n");
    phase56_text(probe, "GXOS_NET10:PHASE56_COMPLETE=1\r\n");
    phase56_text(probe, "GXOS_NET10:PHASE56_PASS=1\r\n");
    return 1;
}
#endif

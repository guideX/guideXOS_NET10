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
        !gxos_scheduler_validate_thread_context(thread)) {
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
    lifecycle->main_thread = main_thread;
    lifecycle->thread = thread;
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
    return lifecycle->main_runtime_thread != 0;
}

static int lifecycle_current_thread_is_active(
    const GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE *lifecycle)
{
    return lifecycle != 0 && lifecycle->thread != 0 &&
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
           lifecycle->runtime_stack_low == thread->stack_base &&
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
        lifecycle->runtime_fls_cleanup == 0 || lifecycle->thread == 0 ||
        lifecycle->main_runtime_thread == 0 ||
        gxos_scheduler_current_thread() != lifecycle->thread) {
        return 0;
    }
    status = (uint32_t)gxos_nativeaot_callback_invoke(
        managed_bridge, input, result);
    if (callback_status_out != 0) *callback_status_out = status;
    if (status != GXOS_NATIVEAOT_CALLBACK_OK ||
        !lifecycle_current_thread_is_active(lifecycle)) {
        return 0;
    }
    lifecycle->attached = 1;
    return lifecycle_capture_attached(lifecycle);
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

int gxos_nativeaot_scheduler_worker_detach(
    GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE *lifecycle)
{
    uint64_t value;
    if (lifecycle == 0 || !lifecycle->attached || lifecycle->detached ||
        !lifecycle_current_thread_is_active(lifecycle) ||
        lifecycle->runtime_fls_cleanup == 0) {
        return 0;
    }
    value = lifecycle->thread->fls_values[lifecycle->runtime_fls_slot];
    if (value != lifecycle->runtime_thread) return 0;
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
    if (lifecycle->runtime_state_after !=
            GXOS_NATIVEAOT_RUNTIME_THREAD_DETACHED ||
        lifecycle->threadstore_after != lifecycle->threadstore_before ||
        lifecycle->alloc_limit_after_detach != 0 ||
        lifecycle->alloc_ptr_after_detach != 0) {
        return 0;
    }
    gxos_scheduler_set_fls(lifecycle->runtime_fls_slot, 0);
    if (gxos_scheduler_get_fls(lifecycle->runtime_fls_slot) != 0) return 0;
    lifecycle->detached = 1;
    return 1;
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
        if (((const uint8_t *)(uintptr_t)thread->stack_limit -
             GXOS_SCHEDULER_CANARY_BYTES)[index] !=
            thread->high_canary[index]) {
            mask |= 2U;
        }
    }
    return mask;
}

/*
 * The scheduler's production stack layout places its low sentinel page
 * immediately below the registered stack.  NativeAOT owns that boundary
 * while executing managed code and may legitimately clear the adjacent page
 * during stack probing.  Rehome only the diagnostic worker's low sentinel so
 * the scheduler's reclaim check still protects a non-owned page; production
 * scheduler allocation remains unchanged.
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
    if (!gxos_nativeaot_scheduler_worker_prepare(
            &cycle->lifecycle, probe->main_thread, thread,
            probe->tls_index, probe->runtime_fls_slot,
            probe->runtime_fls_cleanup)) {
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
        cycle->runtime_stack_low != thread->stack_base ||
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
    close_result = gxos_scheduler_close_handle(cycle->handle);
    collect_result = close_result &&
        gxos_scheduler_collect(cycle->probe->scheduler);
    phase53o_hex(cycle->probe, "GXOS_NET10:PHASE53O_RECLAIM_CLOSE_RESULT=",
                 (uint64_t)close_result);
    phase53o_hex(cycle->probe, "GXOS_NET10:PHASE53O_RECLAIM_COLLECT_RESULT=",
                 (uint64_t)collect_result);
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
    if (!close_result || !collect_result || thread->live != 0 ||
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
        if (!gxos_scheduler_resume_thread(handle, 0)) return 0;
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

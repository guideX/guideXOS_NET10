#include "scheduler_foundation.h"

typedef struct {
    GXOS_SCHEDULER scheduler;
    GXOS_SCHEDULER_HANDLE event_a;
    GXOS_SCHEDULER_HANDLE event_b;
    GXOS_SCHEDULER_HANDLE worker_handle;
    GXOS_SCHEDULER_TCB *main_thread;
    GXOS_SCHEDULER_TCB *worker;
    GXOS_SCHEDULER_REGISTER_SNAPSHOT main_after_b;
    GXOS_SCHEDULER_REGISTER_SNAPSHOT main_after_worker;
    GXOS_SCHEDULER_REGISTER_SNAPSHOT worker_before_a;
    GXOS_SCHEDULER_REGISTER_SNAPSHOT worker_after_a;
    uint32_t worker_executed;
    uint32_t worker_initial_state_valid;
    uint32_t worker_isolated_state_valid;
    uint32_t worker_wait_result;
    uint32_t failure;
    uint32_t failure_count;
    uint64_t worker_initial_gs;
    uint64_t worker_initial_teb;
    uint64_t worker_initial_tls_vector;
    uint64_t worker_initial_tls_block;
    uint64_t worker_initial_rsp;
} GXOS_SCHEDULER_PROOF_STATE;

static GXOS_SCHEDULER_PROOF_STATE *g_proof;
static GXOS_SCHEDULER_PROOF_STATE g_proof_storage;

static int GXOS_SCHEDULER_MS_ABI proof_register_stack_vm(
    void *context, uint64_t base, uint64_t bytes,
    uint64_t *allocation_identity_out)
{
    (void)context;
    if (base == 0 || bytes == 0 || allocation_identity_out == 0) return 0;
    *allocation_identity_out = base;
    return 1;
}

static int GXOS_SCHEDULER_MS_ABI proof_unregister_stack_vm(
    void *context, uint64_t base, uint64_t bytes,
    uint64_t allocation_identity)
{
    (void)context;
    return base != 0 && bytes != 0 && allocation_identity == base;
}

static void proof_zero(void *destination, size_t count)
{
    uint8_t *bytes = (uint8_t *)destination;
    while (count-- != 0) *bytes++ = 0;
}

static void proof_text(const char *text)
{
    if (g_proof != 0 && g_proof->scheduler.log_text != 0) {
        g_proof->scheduler.log_text(text);
    }
}

static void proof_hex(const char *name, uint64_t value)
{
    if (g_proof != 0 && g_proof->scheduler.log_hex != 0) {
        g_proof->scheduler.log_hex(name, value);
    }
}

static void proof_u32(const char *name, uint32_t value)
{
    if (g_proof != 0 && g_proof->scheduler.log_u32 != 0) {
        g_proof->scheduler.log_u32(name, value);
    }
}

static void proof_fail(void)
{
    if (g_proof != 0) {
        g_proof->failure = 1;
        ++g_proof->failure_count;
    }
}

static uint32_t snapshot_gpr_matches(const GXOS_SCHEDULER_REGISTER_SNAPSHOT *snapshot,
                                     uint64_t prefix)
{
    uint32_t index;
    if (snapshot == 0) return 0;
    for (index = 0; index != 8; ++index) {
        if (snapshot->gpr[index] != prefix + index + 1U) return 0;
    }
    return 1;
}

static uint32_t snapshot_simd_matches(
    const GXOS_SCHEDULER_REGISTER_SNAPSHOT *left,
    const GXOS_SCHEDULER_REGISTER_SNAPSHOT *right)
{
    uint32_t lane;
    uint32_t byte;
    if (left == 0 || right == 0) return 0;
    for (lane = 0; lane != 10; ++lane) {
        for (byte = 0; byte != 16; ++byte) {
            if (left->xmm[lane][byte] != right->xmm[lane][byte]) return 0;
        }
    }
    return 1;
}

static uint32_t snapshot_is_distinct(
    const GXOS_SCHEDULER_REGISTER_SNAPSHOT *left,
    const GXOS_SCHEDULER_REGISTER_SNAPSHOT *right)
{
    uint32_t lane;
    uint32_t byte;
    if (left == 0 || right == 0) return 0;
    for (lane = 0; lane != 10; ++lane) {
        for (byte = 0; byte != 16; ++byte) {
            if (left->xmm[lane][byte] != right->xmm[lane][byte]) return 1;
        }
    }
    return 0;
}

static uint32_t snapshot_control_matches(
    const GXOS_SCHEDULER_REGISTER_SNAPSHOT *before,
    const GXOS_SCHEDULER_REGISTER_SNAPSHOT *after,
    uint32_t mxcsr, uint16_t x87)
{
    if (before == 0 || after == 0) return 0;
    return before->mxcsr == mxcsr && after->mxcsr == mxcsr &&
           before->x87_control == x87 && after->x87_control == x87 &&
           (before->rflags & 0x600U) == 0 &&
           (after->rflags & 0x600U) == 0;
}

static uint32_t snapshot_stack_is_valid(
    const GXOS_SCHEDULER_REGISTER_SNAPSHOT *snapshot,
    uint64_t lower, uint64_t upper)
{
    return snapshot != 0 && snapshot->rsp >= lower && snapshot->rsp < upper;
}

static uintptr_t GXOS_SCHEDULER_MS_ABI proof_dummy_entry(void *argument)
{
    (void)argument;
    return 0;
}

static uintptr_t GXOS_SCHEDULER_MS_ABI proof_worker_entry(void *argument)
{
    GXOS_SCHEDULER_PROOF_STATE *proof = (GXOS_SCHEDULER_PROOF_STATE *)argument;
    GXOS_SCHEDULER_TCB *current = gxos_scheduler_current_thread();
    uint64_t main_tls;
    proof->worker_executed = 1;
    if (current != proof->worker || current->identity == proof->main_thread->identity) {
        proof_fail();
    }
    proof->worker_initial_gs = gxos_scheduler_current_gs_base();
    proof->worker_initial_teb = gxos_scheduler_current_teb_base();
    proof->worker_initial_tls_vector = gxos_scheduler_current_tls_vector();
    proof->worker_initial_tls_block = gxos_scheduler_current_tls_block();
    proof->worker_initial_rsp = 0;
    proof_hex("GXOS_NET10:SCHEDULER_WORKER_GS_BASE=0x", proof->worker_initial_gs);
    proof_hex("GXOS_NET10:SCHEDULER_WORKER_TEB_BASE=0x", proof->worker_initial_teb);
    proof_hex("GXOS_NET10:SCHEDULER_WORKER_TLS_VECTOR=0x", proof->worker_initial_tls_vector);
    proof_hex("GXOS_NET10:SCHEDULER_WORKER_TLS_BLOCK=0x", proof->worker_initial_tls_block);
    proof_hex("GXOS_NET10:SCHEDULER_WORKER_ID=0x", current->identity);
    proof_hex("GXOS_NET10:SCHEDULER_MAIN_GS_BASE=0x", proof->main_thread->gs_base);
    proof_hex("GXOS_NET10:SCHEDULER_MAIN_TEB_BASE=0x", proof->main_thread->teb_base);
    proof_hex("GXOS_NET10:SCHEDULER_MAIN_TLS_VECTOR=0x", proof->main_thread->tls_vector_base);
    proof_hex("GXOS_NET10:SCHEDULER_MAIN_TLS_BLOCK=0x", proof->main_thread->tls_block_base);
    proof_hex("GXOS_NET10:SCHEDULER_CURRENT_TCB=0x", (uint64_t)(uintptr_t)current);
    proof_hex("GXOS_NET10:SCHEDULER_WORKER_TCB=0x", (uint64_t)(uintptr_t)proof->worker);
    if (proof->worker_initial_gs == proof->main_thread->gs_base ||
        proof->worker_initial_teb == proof->main_thread->teb_base ||
        proof->worker_initial_tls_vector == proof->main_thread->tls_vector_base ||
        proof->worker_initial_tls_block == proof->main_thread->tls_block_base ||
        *(uint64_t *)(uintptr_t)(proof->worker_initial_teb + 0x100) != current->identity) {
        proof_fail();
    }
    gxos_scheduler_gs_tls_write(0x5757000000000100ULL);
    gxos_scheduler_set_fls(GXOS_SCHEDULER_FLS_PROOF_SLOT,
                           (uintptr_t)0x5757000000000200ULL);
    gxos_scheduler_set_last_error(0x57570003U);
    if (proof->worker->fls_values[GXOS_SCHEDULER_FLS_PROOF_SLOT] !=
            (uint64_t)0x5757000000000200ULL ||
        proof->main_thread == proof->worker ||
        proof->main_thread->fls_values[GXOS_SCHEDULER_FLS_PROOF_SLOT] ==
            (uint64_t)0x5757000000000200ULL) {
        proof_fail();
    }
    proof_text("GXOS_NET10:SCHEDULER_FLS_PER_TCB_ISOLATION=PROVEN\r\n");
    main_tls = *(uint64_t *)(uintptr_t)(proof->main_thread->tls_block_base +
                                        GXOS_SCHEDULER_TLS_OFFSET);
    if (gxos_scheduler_gs_tls_read() != 0x5757000000000100ULL ||
        gxos_scheduler_get_fls(GXOS_SCHEDULER_FLS_PROOF_SLOT) !=
            (uintptr_t)0x5757000000000200ULL ||
        gxos_scheduler_get_last_error() != 0x57570003U ||
        main_tls == gxos_scheduler_gs_tls_read()) {
        proof_fail();
    }
    proof->worker_initial_state_valid = 1;
    proof_text("GXOS_NET10:SCHEDULER_WORKER_PRIVATE_STATE=PROVEN\r\n");
    if (!gxos_scheduler_signal_event(proof->event_b)) proof_fail();
    proof_text("GXOS_NET10:SCHEDULER_EVENT_B_SIGNALLED_BY_WORKER\r\n");
    gxos_scheduler_capture_worker_sentinels(&proof->worker_before_a);
    if (!snapshot_gpr_matches(&proof->worker_before_a,
                              0x5757000000000000ULL) ||
        !snapshot_control_matches(&proof->worker_before_a,
                                  &proof->worker_before_a, 0x3F80U, 0x077FU)) {
        proof_fail();
    }
    gxos_scheduler_worker_wait(proof->event_a, &proof->worker_after_a,
                               (int32_t *)&proof->worker_wait_result);
    proof_text("GXOS_NET10:SCHEDULER_WORKER_WAIT_RETURNED=1\r\n");
    if (proof->worker_wait_result != GXOS_SCHEDULER_WAIT_SIGNALED ||
        gxos_scheduler_event_is_signaled(proof->event_a) ||
        gxos_scheduler_gs_tls_read() != 0x5757000000000100ULL ||
        gxos_scheduler_get_fls(GXOS_SCHEDULER_FLS_PROOF_SLOT) !=
            (uintptr_t)0x5757000000000200ULL ||
        gxos_scheduler_get_last_error() != 0x57570003U ||
        gxos_scheduler_current_gs_base() != proof->worker_initial_gs ||
        gxos_scheduler_current_tls_block() != proof->worker_initial_tls_block ||
        !snapshot_gpr_matches(&proof->worker_after_a,
                              0x5757000000000000ULL) ||
        !snapshot_gpr_matches(&proof->worker_before_a,
                              0x5757000000000000ULL) ||
        !snapshot_simd_matches(&proof->worker_before_a,
                               &proof->worker_after_a) ||
        !snapshot_control_matches(&proof->worker_before_a,
                                   &proof->worker_after_a, 0x3F80U, 0x077FU) ||
        !snapshot_stack_is_valid(&proof->worker_before_a,
                                 proof->worker->stack_base,
                                 proof->worker->stack_limit) ||
        !snapshot_stack_is_valid(&proof->worker_after_a,
                                 proof->worker->stack_base,
                                 proof->worker->stack_limit)) {
        proof_fail();
    }
    proof->worker_isolated_state_valid = 1;
    proof_text("GXOS_NET10:SCHEDULER_WORKER_RESUMED_AFTER_EVENT_A\r\n");
    return 0;
}

static uint32_t run_negative_controls(GXOS_SCHEDULER_PROOF_STATE *proof)
{
    GXOS_SCHEDULER_HANDLE stale_handle = 0;
    GXOS_SCHEDULER_HANDLE temporary_handles[GXOS_SCHEDULER_MAX_THREADS] = {0};
    GXOS_SCHEDULER_TCB *temporary_threads[GXOS_SCHEDULER_MAX_THREADS] = {0};
    GXOS_SCHEDULER_HANDLE temporary_events[GXOS_SCHEDULER_MAX_EVENTS] = {0};
    uint32_t previous_suspend_count = 0;
    uint32_t thread_count = 0;
    uint32_t event_count = 0;
    uint32_t index;
    GXOS_SCHEDULER_HANDLE temp;
    GXOS_SCHEDULER_TCB *temp_thread;
    uint32_t pass = 1;

    if (gxos_scheduler_resume_thread((GXOS_SCHEDULER_HANDLE)0x1234,
                                      &previous_suspend_count)) pass = 0;
    proof_text("GXOS_NET10:SCHED_NEGATIVE_INVALID_HANDLE=1\r\n");
    if (!pass) proof_fail();
    if (!gxos_scheduler_create_event(&proof->scheduler, 0, 0, &temp)) {
        proof_fail();
        return 0;
    }
    if (gxos_scheduler_resume_thread(temp, &previous_suspend_count)) pass = 0;
    if (!gxos_scheduler_close_handle(temp) ||
        gxos_scheduler_close_handle(temp)) pass = 0;
    stale_handle = temp;
    if (!gxos_scheduler_try_destroy_event(stale_handle)) pass = 0;
    if (gxos_scheduler_create_event(&proof->scheduler, 0, 0, &temp) == 0 ||
        !gxos_scheduler_close_handle(temp) ||
        !gxos_scheduler_try_destroy_event(temp)) pass = 0;
    if (gxos_scheduler_event_from_handle(stale_handle) != 0) pass = 0;
    proof_text("GXOS_NET10:SCHED_NEGATIVE_STALE_GENERATION=1\r\n");
    if (!pass) proof_fail();

    while (thread_count != GXOS_SCHEDULER_MAX_THREADS &&
           gxos_scheduler_create_suspended_thread(
               &proof->scheduler, proof_dummy_entry, 0,
               &temporary_handles[thread_count],
               &temporary_threads[thread_count])) {
        ++thread_count;
    }
    if (gxos_scheduler_create_suspended_thread(
            &proof->scheduler, proof_dummy_entry, 0, &temp, &temp_thread)) {
        pass = 0;
    }
    proof_text("GXOS_NET10:SCHED_NEGATIVE_TCB_EXHAUSTION=1\r\n");
    for (index = 0; index != thread_count; ++index) {
        if (!gxos_scheduler_close_handle(temporary_handles[index]) ||
            !gxos_scheduler_discard_created_thread(temporary_threads[index])) pass = 0;
    }
    if (!pass) proof_fail();

    /* Event capacity fails with object slots still available. */
    event_count = 0;
    while (event_count != GXOS_SCHEDULER_MAX_EVENTS &&
           gxos_scheduler_create_event(&proof->scheduler, 0, 0,
                                       &temporary_events[event_count])) {
        ++event_count;
    }
    if (event_count == 0 || gxos_scheduler_create_event(&proof->scheduler, 0, 0, &temp)) pass = 0;
    proof_text("GXOS_NET10:SCHED_NEGATIVE_EVENT_REGISTRY_EXHAUSTION=1\r\n");
    for (index = 0; index != event_count; ++index) {
        if (!gxos_scheduler_close_handle(temporary_events[index]) ||
            !gxos_scheduler_try_destroy_event(temporary_events[index])) pass = 0;
    }

    /* The remaining records fill the object table before event capacity. */
    thread_count = 0;
    while (thread_count != GXOS_SCHEDULER_MAX_THREADS &&
           gxos_scheduler_create_suspended_thread(
               &proof->scheduler, proof_dummy_entry, 0,
               &temporary_handles[thread_count],
               &temporary_threads[thread_count])) {
        ++thread_count;
    }
    event_count = 0;
    while (gxos_scheduler_create_event(&proof->scheduler, 0, 0,
                                       &temporary_events[event_count])) {
        ++event_count;
        if (event_count == GXOS_SCHEDULER_MAX_EVENTS) break;
    }
    if (event_count == 0 || gxos_scheduler_create_event(&proof->scheduler, 0, 0, &temp)) {
        pass = 0;
    }
    proof_text("GXOS_NET10:SCHED_NEGATIVE_OBJECT_REGISTRY_EXHAUSTION=1\r\n");
    for (index = 0; index != thread_count; ++index) {
        if (!gxos_scheduler_close_handle(temporary_handles[index]) ||
            !gxos_scheduler_discard_created_thread(temporary_threads[index])) pass = 0;
    }
    for (index = 0; index != event_count; ++index) {
        if (!gxos_scheduler_close_handle(temporary_events[index]) ||
            !gxos_scheduler_try_destroy_event(temporary_events[index])) pass = 0;
    }

    if (!gxos_scheduler_create_suspended_thread(
            &proof->scheduler, proof_dummy_entry, 0, &temp, &temp_thread)) {
        proof_fail();
        return 0;
    }
    if (!gxos_scheduler_resume_thread(temp, &previous_suspend_count) ||
        previous_suspend_count != 1 ||
        !gxos_scheduler_resume_thread(temp, &previous_suspend_count) ||
        previous_suspend_count != 0 ||
        !gxos_scheduler_close_handle(temp) ||
        !gxos_scheduler_discard_created_thread(temp_thread)) {
        pass = 0;
    }
    proof_text("GXOS_NET10:SCHED_NEGATIVE_SUSPEND_RULES=1\r\n");
    {
        uint32_t wrong_resume = gxos_scheduler_resume_thread(
            proof->event_a, &previous_suspend_count);
        uint32_t wrong_signal = gxos_scheduler_signal_event(
            proof->worker_handle);
        uint32_t wrong_reset = gxos_scheduler_reset_event(
            proof->worker_handle);
        uint32_t wrong_wait = gxos_scheduler_finish_wait(
            proof->worker_handle);
        if (wrong_resume || wrong_signal || wrong_reset ||
            wrong_wait != (uint32_t)GXOS_SCHEDULER_WAIT_FAILURE) {
            pass = 0;
        }
    }
    proof_text("GXOS_NET10:SCHED_NEGATIVE_WRONG_OBJECT_TYPES=1\r\n");
    if (!pass) proof_fail();
    return pass;
}

int gxos_synthetic_scheduler_proof(
    GXOS_SCHEDULER_ALLOCATE_PAGES allocate_pages,
    GXOS_SCHEDULER_FREE_PAGES free_pages,
    GXOS_SCHEDULER_LOG_TEXT log_text,
    GXOS_SCHEDULER_LOG_HEX log_hex,
    GXOS_SCHEDULER_LOG_U32 log_u32)
{
    GXOS_SCHEDULER_PROOF_STATE *proof = &g_proof_storage;
    GXOS_SCHEDULER_TCB *worker;
    uint32_t previous_suspend_count = 0;
    int32_t main_wait_result = GXOS_SCHEDULER_WAIT_FAILURE;
    uint64_t original_gs;
    uint32_t teardown_result = 0;
    uint32_t positive = 1;
    uint8_t saved_low_canary;

    proof_zero(proof, sizeof(*proof));
    proof->scheduler.log_text = log_text;
    proof->scheduler.log_hex = log_hex;
    proof->scheduler.log_u32 = log_u32;
    g_proof = proof;
    proof_text("GXOS_NET10:SCHEDULER_PROOF_BEGIN\r\n");
    proof_u32("GXOS_NET10:SCHEDULER_MAX_THREADS=0x",
              GXOS_SCHEDULER_MAX_THREADS);
    proof_u32("GXOS_NET10:SCHEDULER_STACK_SIZE=0x",
              GXOS_SCHEDULER_STACK_SIZE);
    if (!gxos_scheduler_initialize(&proof->scheduler, allocate_pages, free_pages,
                                   log_text, log_hex, log_u32)) {
        proof_text("GXOS_NET10:SCHEDULER_PROOF=FAILED\r\n");
        g_proof = 0;
        return 0;
    }
    if (!gxos_scheduler_configure_stack_vm(
            &proof->scheduler, proof_register_stack_vm,
            proof_unregister_stack_vm, 0)) {
        proof_text("GXOS_NET10:SCHEDULER_PROOF=FAILED\r\n");
        g_proof = 0;
        return 0;
    }
    proof->main_thread = proof->scheduler.boot_thread;
    original_gs = proof->scheduler.saved_boot_gs_base;
    proof_hex("GXOS_NET10:SCHEDULER_ORIGINAL_BOOT_GS=0x", original_gs);
    if (!gxos_scheduler_create_event(&proof->scheduler, 0, 0, &proof->event_a) ||
        !gxos_scheduler_create_event(&proof->scheduler, 1, 0, &proof->event_b) ||
        !gxos_scheduler_create_suspended_thread(
            &proof->scheduler, proof_worker_entry, proof,
            &proof->worker_handle, &worker)) {
        proof_fail();
        goto cleanup;
    }
    proof->worker = worker;
    proof_text("GXOS_NET10:SCHEDULER_REGISTER_MAIN=M\r\n");
    proof_text("GXOS_NET10:SCHEDULER_EVENT_A=AUTO_RESET_NONSIGNALED\r\n");
    proof_text("GXOS_NET10:SCHEDULER_EVENT_B=MANUAL_RESET_NONSIGNALED\r\n");
    proof_text("GXOS_NET10:SCHEDULER_WORKER_STATE=CreatedSuspended\r\n");
    proof_hex("GXOS_NET10:SCHEDULER_WORKER_STACK_BASE=0x", worker->stack_base);
    proof_hex("GXOS_NET10:SCHEDULER_WORKER_STACK_LIMIT=0x", worker->stack_limit);
    proof_hex("GXOS_NET10:SCHEDULER_WORKER_INITIAL_RSP=0x", worker->initial_rsp);
    proof_hex("GXOS_NET10:SCHEDULER_WORKER_INITIAL_RIP=0x", worker->context.rip);
    proof_hex("GXOS_NET10:SCHEDULER_WORKER_INITIAL_LOW_CANARY=0x",
              *(uint64_t *)(uintptr_t)worker->stack_base);
    proof_hex("GXOS_NET10:SCHEDULER_WORKER_INITIAL_HIGH_CANARY=0x",
              *(uint64_t *)(uintptr_t)(worker->stack_limit - 16U));
    proof_u32("GXOS_NET10:SCHEDULER_WORKER_ENTRY_ALIGNMENT=0x",
              (uint32_t)(worker->initial_rsp & 0xFU));
    if (proof->worker_executed != 0 || worker->state !=
            GXOS_SCHEDULER_THREAD_CREATED_SUSPENDED ||
        !gxos_scheduler_check_canaries(worker)) {
        positive = 0;
    }
    proof_text("GXOS_NET10:SCHEDULER_WORKER_NOT_EXECUTED=1\r\n");
    if (!run_negative_controls(proof)) positive = 0;
    if (!gxos_scheduler_resume_thread(proof->worker_handle,
                                       &previous_suspend_count) ||
        previous_suspend_count != 1 ||
        worker->state != GXOS_SCHEDULER_THREAD_RUNNABLE) {
        positive = 0;
    }
    proof_text("GXOS_NET10:SCHEDULER_TRANSITION=CreatedSuspended->Runnable\r\n");
    proof_u32("GXOS_NET10:SCHEDULER_RESUME_PREVIOUS_SUSPEND_COUNT=0x",
              previous_suspend_count);
    if (!gxos_scheduler_close_handle(proof->worker_handle) ||
        worker->public_handle_refs != 0 || worker->execution_refs != 1 ||
        !worker->live || !gxos_scheduler_check_canaries(worker)) {
        positive = 0;
    }
    proof_text("GXOS_NET10:SCHEDULER_WORKER_HANDLE_CLOSED_LIVE=1\r\n");

    gxos_scheduler_gs_tls_write(0x4D4D000000000100ULL);
    gxos_scheduler_set_fls(GXOS_SCHEDULER_FLS_PROOF_SLOT,
                           (uintptr_t)0x4D4D000000000200ULL);
    gxos_scheduler_set_last_error(0x4D4D03U);
    proof_text("GXOS_NET10:SCHEDULER_MAIN_BLOCK_EVENT_B\r\n");
    gxos_scheduler_main_block(proof->event_b, &proof->main_after_b, &main_wait_result);
    if (main_wait_result != GXOS_SCHEDULER_WAIT_SIGNALED ||
        gxos_scheduler_current_thread() != proof->main_thread ||
        gxos_scheduler_current_gs_base() != proof->main_thread->gs_base ||
        gxos_scheduler_current_teb_base() != proof->main_thread->teb_base ||
        gxos_scheduler_current_tls_vector() != proof->main_thread->tls_vector_base ||
        gxos_scheduler_current_tls_block() != proof->main_thread->tls_block_base ||
        gxos_scheduler_gs_tls_read() != 0x4D4D000000000100ULL ||
        gxos_scheduler_get_fls(GXOS_SCHEDULER_FLS_PROOF_SLOT) !=
            (uintptr_t)0x4D4D000000000200ULL ||
        gxos_scheduler_get_last_error() != 0x4D4D03U ||
        !gxos_scheduler_event_is_signaled(proof->event_b) ||
        !snapshot_gpr_matches(&proof->main_after_b,
                              0x4D4D000000000000ULL) ||
        !snapshot_simd_matches(&proof->main_after_b, &proof->main_after_b) ||
        !snapshot_control_matches(&proof->main_after_b, &proof->main_after_b,
                                   0x1F80U, 0x037FU) ||
        !snapshot_stack_is_valid(&proof->main_after_b,
                                 proof->scheduler.boot_stack_lower,
                                 proof->scheduler.boot_stack_upper)) {
        positive = 0;
    }
    proof_text("GXOS_NET10:SCHEDULER_MAIN_EVENT_B_WAIT_SUCCEEDED=1\r\n");
    proof_text("GXOS_NET10:SCHEDULER_EVENT_B_MANUAL_SIGNAL_PERSISTS=1\r\n");
    if (!gxos_scheduler_reset_event(proof->event_b) ||
        gxos_scheduler_event_is_signaled(proof->event_b)) positive = 0;
    proof_text("GXOS_NET10:SCHEDULER_EVENT_B_RESET_NONSIGNALED=1\r\n");
    if (gxos_scheduler_try_reclaim_thread(worker) ||
        gxos_scheduler_try_destroy_event(proof->event_a)) positive = 0;
    proof_text("GXOS_NET10:SCHED_NEGATIVE_BLOCKED_LIFETIME_GUARDS=1\r\n");
    if (!gxos_scheduler_signal_event(proof->event_a)) positive = 0;
    proof_text("GXOS_NET10:SCHEDULER_EVENT_A_SIGNAL_WAKE=1\r\n");
    proof_text("GXOS_NET10:SCHEDULER_MAIN_DISPATCH_BEGIN=1\r\n");
    gxos_scheduler_main_dispatch(&proof->main_after_worker);
    proof_text("GXOS_NET10:SCHEDULER_MAIN_DISPATCH_RETURNED=1\r\n");
    if (!gxos_scheduler_thread_is_terminated(worker) ||
        gxos_scheduler_current_thread() != proof->main_thread ||
        !snapshot_gpr_matches(&proof->main_after_worker,
                              0x4D4D000000000000ULL) ||
        !snapshot_simd_matches(&proof->main_after_b, &proof->main_after_worker) ||
        !snapshot_is_distinct(&proof->main_after_b, &proof->worker_before_a) ||
        !snapshot_control_matches(&proof->main_after_b, &proof->main_after_worker,
                                   0x1F80U, 0x037FU)) {
        positive = 0;
    }
    proof_text("GXOS_NET10:SCHEDULER_WORKER_TERMINATED=1\r\n");
    saved_low_canary = *(uint8_t *)(uintptr_t)worker->stack_canary_memory;
    *(uint8_t *)(uintptr_t)worker->stack_canary_memory =
        (uint8_t)(saved_low_canary ^ 1U);
    if (gxos_scheduler_check_canaries(worker)) positive = 0;
    *(uint8_t *)(uintptr_t)worker->stack_canary_memory = saved_low_canary;
    proof_text("GXOS_NET10:SCHED_NEGATIVE_STACK_CANARY_REJECTION=1\r\n");
    if (!gxos_scheduler_check_canaries(worker)) positive = 0;
    proof_hex("GXOS_NET10:SCHEDULER_WORKER_FINAL_LOW_CANARY=0x",
              *(uint64_t *)(uintptr_t)worker->stack_base);
    proof_hex("GXOS_NET10:SCHEDULER_WORKER_FINAL_HIGH_CANARY=0x",
              *(uint64_t *)(uintptr_t)(worker->stack_limit - 16U));
    proof_text("GXOS_NET10:SCHEDULER_WORKER_CANARIES_INTACT=1\r\n");
    if (!gxos_scheduler_collect(&proof->scheduler) || worker->live) positive = 0;
    proof_text("GXOS_NET10:SCHEDULER_CLOSED_HANDLE_TERMINATION_OBSERVED=1\r\n");
    if (gxos_scheduler_close_handle(proof->event_a) == 0 ||
        gxos_scheduler_close_handle(proof->event_b) == 0 ||
        !gxos_scheduler_try_destroy_event(proof->event_a) ||
        !gxos_scheduler_try_destroy_event(proof->event_b)) positive = 0;
    proof_text("GXOS_NET10:SCHEDULER_EVENTS_DESTROYED=1\r\n");
    if (!gxos_scheduler_teardown(&proof->scheduler)) positive = 0;
    teardown_result = 1;
cleanup:
    if (proof->scheduler.active) {
        /* A failure before the positive sequence still restores GS safely. */
        if (proof->worker != 0 && proof->worker->live &&
            proof->worker->state == GXOS_SCHEDULER_THREAD_CREATED_SUSPENDED &&
            proof->worker->public_handle_refs != 0) {
            (void)gxos_scheduler_close_handle(proof->worker_handle);
            proof->worker->execution_refs = 0;
            proof->worker->state = GXOS_SCHEDULER_THREAD_TERMINATED;
        }
        if (proof->worker != 0 && proof->worker->live) {
            (void)gxos_scheduler_collect(&proof->scheduler);
        }
        if (proof->event_a != 0 && gxos_scheduler_event_from_handle(proof->event_a) != 0) {
            (void)gxos_scheduler_close_handle(proof->event_a);
            (void)gxos_scheduler_try_destroy_event(proof->event_a);
        }
        if (proof->event_b != 0 && gxos_scheduler_event_from_handle(proof->event_b) != 0) {
            (void)gxos_scheduler_close_handle(proof->event_b);
            (void)gxos_scheduler_try_destroy_event(proof->event_b);
        }
        (void)gxos_scheduler_teardown(&proof->scheduler);
    }
    if (proof->failure != 0) positive = 0;
    if (!teardown_result && !proof->scheduler.active) teardown_result = 1;
    proof_u32("GXOS_NET10:SCHEDULER_TEARDOWN=0x", teardown_result);
    proof_u32("GXOS_NET10:SCHEDULER_FAILURE_COUNT=0x", proof->failure_count);
    proof_hex("GXOS_NET10:SCHEDULER_RESTORED_GS=0x",
              gxos_scheduler_current_gs_base());
    if (!positive || !teardown_result || gxos_scheduler_current_gs_base() != original_gs) {
        proof_text("GXOS_NET10:SCHEDULER_PROOF=FAILED\r\n");
        g_proof = 0;
        return 0;
    }
    proof_text("GXOS_NET10:SCHEDULER_PROOF=PASSED\r\n");
    proof_text("GXOS_NET10:SCHEDULER_NEUTRAL_STATE=1\r\n");
    g_proof = 0;
    return 1;
}

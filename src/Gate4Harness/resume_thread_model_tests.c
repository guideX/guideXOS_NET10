#include "scheduler_foundation.h"
#include "set_thread_priority.h"

#include <stdio.h>
#include <string.h>

static unsigned char g_pages[512U * GXOS_SCHEDULER_PAGE_SIZE]
    __attribute__((aligned(GXOS_SCHEDULER_PAGE_SIZE)));
static unsigned char g_page_used[512U];
static GXOS_SCHEDULER g_scheduler;
static unsigned int g_checks;
static unsigned int g_failures;

#define CHECK(condition) do { \
    ++g_checks; \
    if (!(condition)) { \
        ++g_failures; \
        (void)printf("FAIL:%s:%u\n", #condition, (unsigned)__LINE__); \
    } \
} while (0)

static uint64_t GXOS_SCHEDULER_MS_ABI model_allocate(
    uint32_t type, uint32_t memory_type, uint64_t pages, uint64_t *memory)
{
    uint32_t index;
    uint32_t run;
    (void)type;
    (void)memory_type;
    if (memory == 0 || pages == 0 || pages > 512U) return 1;
    for (index = 0; index + pages <= 512U; ++index) {
        for (run = 0; run != pages && g_page_used[index + run] == 0; ++run) {
        }
        if (run == pages) {
            for (run = 0; run != pages; ++run) g_page_used[index + run] = 1;
            *memory = (uint64_t)(uintptr_t)(g_pages +
                index * GXOS_SCHEDULER_PAGE_SIZE);
            return 0;
        }
    }
    return 1;
}

static uint64_t GXOS_SCHEDULER_MS_ABI model_free(
    uint64_t memory, uint64_t pages)
{
    uint64_t base = (uint64_t)(uintptr_t)g_pages;
    uint64_t end = base + sizeof(g_pages);
    uint32_t index;
    if (memory < base || memory >= end ||
        (memory - base) % GXOS_SCHEDULER_PAGE_SIZE != 0 ||
        pages == 0 || pages > 512U ||
        memory + pages * GXOS_SCHEDULER_PAGE_SIZE > end) return 1;
    index = (uint32_t)((memory - base) / GXOS_SCHEDULER_PAGE_SIZE);
    while (pages-- != 0) g_page_used[index++] = 0;
    return 0;
}

static void GXOS_SCHEDULER_MS_ABI model_log_text(const char *text)
{
    (void)text;
}

static void GXOS_SCHEDULER_MS_ABI model_log_hex(const char *name, uint64_t value)
{
    (void)name;
    (void)value;
}

static void GXOS_SCHEDULER_MS_ABI model_log_u32(const char *name, uint32_t value)
{
    (void)name;
    (void)value;
}

static uintptr_t GXOS_SCHEDULER_MS_ABI model_entry(void *argument)
{
    return (uintptr_t)argument;
}

void gxos_scheduler_start_worker(void)
{
}

void gxos_scheduler_invalid_thread_return(void)
{
}

static int unchanged(const GXOS_SCHEDULER_TCB *thread,
                     GXOS_SCHEDULER_THREAD_STATE state,
                     uint32_t suspend_count,
                     uint64_t execution_count,
                     uint32_t execution_refs,
                     uint32_t priority,
                     uint32_t queue_count,
                     uint64_t stack_base,
                     uint64_t gs_base,
                     uint64_t tls_vector,
                     uint64_t tls_block,
                     const GXOS_SCHEDULER_CONTEXT *context)
{
    return thread != 0 && thread->state == state &&
           thread->suspend_count == suspend_count &&
           thread->execution_count == execution_count &&
           thread->execution_refs == execution_refs &&
           thread->relative_priority == (int32_t)priority &&
           gxos_scheduler_runnable_count() == queue_count &&
           thread->stack_base == stack_base && thread->gs_base == gs_base &&
           thread->tls_vector_base == tls_vector &&
           thread->tls_block_base == tls_block &&
           memcmp(&thread->context, context, sizeof(*context)) == 0;
}

int main(void)
{
    GXOS_SCHEDULER_HANDLE event_handle = 0;
    GXOS_SCHEDULER_HANDLE notification_handle = 0;
    GXOS_SCHEDULER_HANDLE worker_handle = 0;
    GXOS_SCHEDULER_HANDLE stale_handle = 0;
    GXOS_SCHEDULER_HANDLE replacement_handle = 0;
    GXOS_SCHEDULER_HANDLE corrupt_handle = 0;
    GXOS_SCHEDULER_TCB *worker = 0;
    GXOS_SCHEDULER_TCB *stale_thread = 0;
    GXOS_SCHEDULER_TCB *replacement_thread = 0;
    GXOS_SCHEDULER_TCB *corrupt_thread = 0;
    GXOS_SCHEDULER_OBJECT *worker_object;
    GXOS_SCHEDULER_OBJECT *event_object;
    GXOS_SCHEDULER_OBJECT *notification_object;
    GXOS_SCHEDULER_CONTEXT context_before;
    GXOS_SCHEDULER_CONTEXT context_after;
    GXOS_SCHEDULER_THREAD_STATE state_before;
    uint32_t suspend_before;
    uint64_t execution_before;
    uint32_t execution_refs_before;
    uint32_t priority_before;
    uint32_t queue_before;
    uint64_t stack_base_before;
    uint64_t gs_before;
    uint64_t tls_vector_before;
    uint64_t tls_block_before;
    uint32_t previous_suspend_count = 0xA5A5A5A5U;
    uint8_t saved_canary;

    (void)memset(g_page_used, 0, sizeof(g_page_used));
    CHECK(gxos_scheduler_initialize(&g_scheduler, model_allocate, model_free,
                                    model_log_text, model_log_hex, model_log_u32));
    CHECK(gxos_scheduler_create_event(&g_scheduler, 0, 0, &event_handle));
    CHECK(gxos_scheduler_create_memory_resource_notification(
        &g_scheduler, 0, &notification_handle));
    CHECK(gxos_scheduler_create_suspended_thread(
        &g_scheduler, model_entry, (void *)(uintptr_t)0x55,
        &worker_handle, &worker));
    worker_object = gxos_scheduler_object_from_handle(worker_handle);
    event_object = gxos_scheduler_object_from_handle(event_handle);
    notification_object = gxos_scheduler_object_from_handle(notification_handle);
    CHECK(worker != 0 && worker_object != 0 && worker_object->type ==
          GXOS_SCHEDULER_OBJECT_THREAD);
    CHECK(worker->state == GXOS_SCHEDULER_THREAD_CREATED_SUSPENDED &&
          worker->suspend_count == 1 && worker->execution_count == 0 &&
          worker->execution_refs == 1 && worker->public_handle_refs == 1);
    CHECK(gxos_scheduler_set_thread_priority(worker_handle, 2));
    CHECK(worker->relative_priority == 2);
    CHECK(gxos_scheduler_runnable_count() == 0 &&
          !gxos_scheduler_is_runnable_queued(worker));

    context_before = worker->context;
    state_before = worker->state;
    suspend_before = worker->suspend_count;
    execution_before = worker->execution_count;
    execution_refs_before = worker->execution_refs;
    priority_before = (uint32_t)worker->relative_priority;
    queue_before = gxos_scheduler_runnable_count();
    stack_base_before = worker->stack_base;
    gs_before = worker->gs_base;
    tls_vector_before = worker->tls_vector_base;
    tls_block_before = worker->tls_block_base;
    CHECK(gxos_scheduler_current_thread() == g_scheduler.boot_thread);
    CHECK(gxos_scheduler_resume_thread(worker_handle, &previous_suspend_count));
    CHECK(previous_suspend_count == 1);
    CHECK(worker->state == GXOS_SCHEDULER_THREAD_RUNNABLE &&
          worker->suspend_count == 0 && worker->relative_priority == 2);
    CHECK(gxos_scheduler_is_runnable_queued(worker));
    CHECK(gxos_scheduler_runnable_count() == 1 &&
          gxos_scheduler_runnable_position(worker) == 0);
    CHECK(gxos_scheduler_current_thread() == g_scheduler.boot_thread);
    CHECK(worker->execution_count == 0 && worker->execution_refs == 1 &&
          worker_object->public_handle_refs == 1);
    CHECK(worker->stack_base == stack_base_before && worker->gs_base == gs_before &&
          worker->tls_vector_base == tls_vector_before &&
          worker->tls_block_base == tls_block_before &&
          gxos_scheduler_check_canaries(worker));
    CHECK(memcmp(&worker->context, &context_before, sizeof(context_before)) == 0);

    context_after = worker->context;
    previous_suspend_count = 0x5A5A5A5AU;
    CHECK(gxos_scheduler_resume_thread(worker_handle, &previous_suspend_count));
    CHECK(previous_suspend_count == 0 && worker->suspend_count == 0 &&
          worker->state == GXOS_SCHEDULER_THREAD_RUNNABLE &&
          gxos_scheduler_runnable_count() == 1 &&
          gxos_scheduler_runnable_position(worker) == 0 &&
          memcmp(&worker->context, &context_after, sizeof(context_after)) == 0);

    /* Wrong, stale, closed, and wrong-generation values cannot mutate the
       already-runnable worker or its ready-set membership. */
    state_before = worker->state;
    suspend_before = worker->suspend_count;
    execution_before = worker->execution_count;
    execution_refs_before = worker->execution_refs;
    priority_before = (uint32_t)worker->relative_priority;
    queue_before = gxos_scheduler_runnable_count();
    CHECK(!gxos_scheduler_resume_thread(0, &previous_suspend_count));
    CHECK(unchanged(worker, state_before, suspend_before, execution_before,
                    execution_refs_before, priority_before, queue_before,
                    stack_base_before, gs_before, tls_vector_before,
                    tls_block_before, &context_after));
    CHECK(!gxos_scheduler_resume_thread((GXOS_SCHEDULER_HANDLE)0x1234,
                                        &previous_suspend_count));
    CHECK(!gxos_scheduler_resume_thread(event_handle, &previous_suspend_count));
    CHECK(!gxos_scheduler_resume_thread(notification_handle, &previous_suspend_count));
    CHECK(unchanged(worker, state_before, suspend_before, execution_before,
                    execution_refs_before, priority_before, queue_before,
                    stack_base_before, gs_before, tls_vector_before,
                    tls_block_before, &context_after));

    CHECK(gxos_scheduler_create_suspended_thread(
        &g_scheduler, model_entry, 0, &stale_handle, &stale_thread));
    CHECK(stale_thread != 0);
    CHECK(gxos_scheduler_close_handle(stale_handle));
    CHECK(gxos_scheduler_discard_created_thread(stale_thread));
    CHECK(!stale_thread->live);
    CHECK(!gxos_scheduler_resume_thread(stale_handle, &previous_suspend_count));
    CHECK(!gxos_scheduler_thread_from_handle(stale_handle));
    CHECK(gxos_scheduler_create_suspended_thread(
        &g_scheduler, model_entry, 0, &replacement_handle, &replacement_thread));
    CHECK(replacement_handle != stale_handle &&
          !gxos_scheduler_resume_thread(stale_handle, &previous_suspend_count));
    CHECK(gxos_scheduler_close_handle(replacement_handle));
    CHECK(gxos_scheduler_discard_created_thread(replacement_thread));

    /* A prepared but unusable context fails closed before the 1 -> 0 write. */
    CHECK(gxos_scheduler_create_suspended_thread(
        &g_scheduler, model_entry, 0, &corrupt_handle, &corrupt_thread));
    CHECK(corrupt_thread != 0);
    context_before = corrupt_thread->context;
    state_before = corrupt_thread->state;
    suspend_before = corrupt_thread->suspend_count;
    queue_before = gxos_scheduler_runnable_count();
    corrupt_thread->context.rsp = 0;
    CHECK(!gxos_scheduler_resume_thread(corrupt_handle, &previous_suspend_count));
    CHECK(corrupt_thread->state == state_before &&
          corrupt_thread->suspend_count == suspend_before &&
          gxos_scheduler_runnable_count() == queue_before &&
          corrupt_thread->execution_refs == 1 &&
          memcmp(&corrupt_thread->context, &context_before,
                 offsetof(GXOS_SCHEDULER_CONTEXT, rsp)) == 0);
    corrupt_thread->context = context_before;
    saved_canary = corrupt_thread->low_canary[0];
    corrupt_thread->low_canary[0] ^= 1U;
    CHECK(!gxos_scheduler_resume_thread(corrupt_handle, &previous_suspend_count));
    CHECK(corrupt_thread->state == state_before &&
          corrupt_thread->suspend_count == suspend_before &&
          gxos_scheduler_runnable_count() == queue_before);
    corrupt_thread->low_canary[0] = saved_canary;
    CHECK(gxos_scheduler_close_handle(corrupt_handle));
    CHECK(gxos_scheduler_discard_created_thread(corrupt_thread));

    /* Closing the public reference does not make the runnable TCB disappear;
       deterministic discard then removes exactly its one ready-set entry. */
    CHECK(gxos_scheduler_close_handle(worker_handle));
    CHECK(worker->public_handle_refs == 0 && worker->execution_refs == 1 &&
          worker->live && gxos_scheduler_runnable_count() == 1);
    CHECK(gxos_scheduler_discard_created_thread(worker));
    CHECK(!worker->live && gxos_scheduler_runnable_count() == 0 &&
          gxos_scheduler_thread_from_handle(worker_handle) == 0);

    notification_object->public_handle_refs = 0;
    CHECK(gxos_scheduler_try_destroy_memory_resource_notification(
        notification_handle));
    CHECK(gxos_scheduler_close_handle(event_handle));
    CHECK(gxos_scheduler_try_destroy_event(event_handle));
    CHECK(event_object->live == 0);
    CHECK(gxos_scheduler_teardown(&g_scheduler));
    CHECK(gxos_scheduler_current_thread() == 0);
    CHECK(g_failures == 0);
    (void)printf("RESUME_THREAD_MODEL_TESTS=%s checks=%u\n",
                 g_failures == 0 ? "PASSED" : "FAILED", g_checks);
    return g_failures == 0 ? 0 : 1;
}

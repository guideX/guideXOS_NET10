#include "scheduler_foundation.h"
#include "set_thread_priority.h"

#include <limits.h>
#include <stdio.h>
#include <string.h>

static unsigned char g_pages[256U * GXOS_SCHEDULER_PAGE_SIZE]
    __attribute__((aligned(GXOS_SCHEDULER_PAGE_SIZE)));
static unsigned char g_page_used[256U];
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
    if (memory == 0 || pages == 0 || pages > 256U) return 1;
    for (index = 0; index + pages <= 256U; ++index) {
        for (run = 0; run != pages && g_page_used[index + run] == 0; ++run) {
        }
        if (run == pages) {
            for (run = 0; run != pages; ++run) {
                g_page_used[index + run] = 1;
            }
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
        pages == 0 || pages > 256U ||
        memory + pages * GXOS_SCHEDULER_PAGE_SIZE > end) return 1;
    index = (uint32_t)((memory - base) / GXOS_SCHEDULER_PAGE_SIZE);
    while (pages-- != 0) g_page_used[index++] = 0;
    return 0;
}

static void GXOS_SCHEDULER_MS_ABI model_log_text(const char *text)
{
    (void)text;
}

static void GXOS_SCHEDULER_MS_ABI model_log_hex(const char *name,
                                                 uint64_t value)
{
    (void)name;
    (void)value;
}

static void GXOS_SCHEDULER_MS_ABI model_log_u32(const char *name,
                                                uint32_t value)
{
    (void)name;
    (void)value;
}

static uintptr_t model_entry(void *argument)
{
    return (uintptr_t)argument;
}

void gxos_scheduler_start_worker(void)
{
}

void gxos_scheduler_invalid_thread_return(void)
{
}

int main(void)
{
    GXOS_SCHEDULER_HANDLE event_handle = 0;
    GXOS_SCHEDULER_HANDLE notification_handle = 0;
    GXOS_SCHEDULER_HANDLE worker_handle = 0;
    GXOS_SCHEDULER_HANDLE stale_handle = 0;
    GXOS_SCHEDULER_HANDLE reclaimed_handle = 0;
    GXOS_SCHEDULER_TCB *worker = 0;
    GXOS_SCHEDULER_TCB *stale_thread = 0;
    GXOS_SCHEDULER_TCB *reclaimed_thread = 0;
    GXOS_SCHEDULER_EVENT *event;
    GXOS_SCHEDULER_MEMORY_RESOURCE_NOTIFICATION *notification;
    GXOS_SCHEDULER_OBJECT *event_object;
    GXOS_SCHEDULER_OBJECT *notification_object;
    GXOS_SCHEDULER_EVENT event_snapshot;
    GXOS_SCHEDULER_MEMORY_RESOURCE_NOTIFICATION notification_snapshot;
    GXOS_SCHEDULER_OBJECT event_object_snapshot;
    GXOS_SCHEDULER_OBJECT notification_object_snapshot;
    const int32_t unsupported_values[] = {0, 1, -1, -2, 15, -15, INT32_MAX};
    size_t index;
    uint32_t state_before;
    uint32_t suspend_before;
    uint64_t execution_before;
    uint32_t execution_refs_before;
    uint64_t stack_base_before;
    uint64_t gs_before;
    uint64_t tls_vector_before;
    uint64_t tls_block_before;

    CHECK(gxos_scheduler_initialize(&g_scheduler, model_allocate, model_free,
                                    model_log_text, model_log_hex,
                                    model_log_u32));
    CHECK(gxos_scheduler_create_event(&g_scheduler, 0, 0, &event_handle));
    CHECK(gxos_scheduler_create_memory_resource_notification(
        &g_scheduler, 0, &notification_handle));
    CHECK(gxos_scheduler_create_suspended_thread(
        &g_scheduler, model_entry, 0, &worker_handle, &worker));
    CHECK(worker != 0);
    CHECK(worker->relative_priority == GXOS_SCHEDULER_DEFAULT_RELATIVE_PRIORITY);
    CHECK(worker->state == GXOS_SCHEDULER_THREAD_CREATED_SUSPENDED);
    CHECK(worker->suspend_count == 1);
    CHECK(worker->execution_count == 0);
    CHECK(worker->execution_refs == 1);
    CHECK(worker->state != GXOS_SCHEDULER_THREAD_RUNNABLE);

    event = gxos_scheduler_event_from_handle(event_handle);
    notification = gxos_scheduler_memory_resource_notification_from_handle(
        notification_handle);
    event_object = gxos_scheduler_object_from_handle(event_handle);
    notification_object = gxos_scheduler_object_from_handle(notification_handle);
    CHECK(event != 0 && notification != 0);
    CHECK(event_object != 0 && notification_object != 0);
    event_snapshot = *event;
    notification_snapshot = *notification;
    event_object_snapshot = *event_object;
    notification_object_snapshot = *notification_object;

    CHECK(gxos_scheduler_set_thread_priority(worker_handle, 2));
    CHECK(worker->relative_priority == 2);
    CHECK(worker->state == GXOS_SCHEDULER_THREAD_CREATED_SUSPENDED);
    CHECK(worker->suspend_count == 1);
    CHECK(worker->execution_count == 0);
    CHECK(worker->state != GXOS_SCHEDULER_THREAD_RUNNABLE);
    CHECK(worker->execution_refs == 1);
    CHECK(gxos_scheduler_check_canaries(worker));
    CHECK(memcmp(event, &event_snapshot, sizeof(event_snapshot)) == 0);
    CHECK(memcmp(notification, &notification_snapshot,
                 sizeof(notification_snapshot)) == 0);
    CHECK(memcmp(event_object, &event_object_snapshot,
                 sizeof(event_object_snapshot)) == 0);
    CHECK(memcmp(notification_object, &notification_object_snapshot,
                 sizeof(notification_object_snapshot)) == 0);

    state_before = worker->state;
    suspend_before = worker->suspend_count;
    execution_before = worker->execution_count;
    execution_refs_before = worker->execution_refs;
    stack_base_before = worker->stack_base;
    gs_before = worker->gs_base;
    tls_vector_before = worker->tls_vector_base;
    tls_block_before = worker->tls_block_base;
    for (index = 0; index != sizeof(unsupported_values) / sizeof(unsupported_values[0]);
         ++index) {
        CHECK(!gxos_scheduler_set_thread_priority(
            worker_handle, unsupported_values[index]));
        CHECK(worker->relative_priority == 2);
        CHECK(worker->state == state_before);
        CHECK(worker->suspend_count == suspend_before);
        CHECK(worker->execution_count == execution_before);
        CHECK(worker->execution_refs == execution_refs_before);
        CHECK(worker->stack_base == stack_base_before);
        CHECK(worker->gs_base == gs_before);
        CHECK(worker->tls_vector_base == tls_vector_before);
        CHECK(worker->tls_block_base == tls_block_before);
        CHECK(memcmp(event, &event_snapshot, sizeof(event_snapshot)) == 0);
        CHECK(memcmp(notification, &notification_snapshot,
                     sizeof(notification_snapshot)) == 0);
    }

    CHECK(!gxos_scheduler_set_thread_priority(0, 2));
    CHECK(!gxos_scheduler_set_thread_priority(
        (GXOS_SCHEDULER_HANDLE)0x1234, 2));
    CHECK(!gxos_scheduler_set_thread_priority(event_handle, 2));
    CHECK(!gxos_scheduler_set_thread_priority(notification_handle, 2));
    CHECK(worker->relative_priority == 2);
    CHECK(worker->state == state_before);
    CHECK(worker->suspend_count == suspend_before);
    CHECK(worker->execution_count == execution_before);
    CHECK(worker->execution_refs == execution_refs_before);

    CHECK(gxos_scheduler_resume_thread(worker_handle, 0));
    CHECK(worker->state == GXOS_SCHEDULER_THREAD_RUNNABLE);
    CHECK(worker->suspend_count == 0);
    CHECK(worker->relative_priority == 2);
    state_before = worker->state;
    suspend_before = worker->suspend_count;
    execution_before = worker->execution_count;
    CHECK(!gxos_scheduler_set_thread_priority(worker_handle, -15));
    CHECK(worker->relative_priority == 2);
    CHECK(worker->state == state_before);
    CHECK(worker->suspend_count == suspend_before);
    CHECK(worker->execution_count == execution_before);

    CHECK(gxos_scheduler_create_suspended_thread(
        &g_scheduler, model_entry, 0, &stale_handle, &stale_thread));
    CHECK(stale_thread != 0);
    CHECK(gxos_scheduler_close_handle(stale_handle));
    CHECK(gxos_scheduler_discard_created_thread(stale_thread));
    CHECK(!stale_thread->live);
    CHECK(!gxos_scheduler_set_thread_priority(stale_handle, 2));
    CHECK(worker->relative_priority == 2);

    CHECK(gxos_scheduler_create_suspended_thread(
        &g_scheduler, model_entry, 0, &reclaimed_handle, &reclaimed_thread));
    CHECK(reclaimed_thread != 0);
    CHECK(reclaimed_thread->relative_priority ==
          GXOS_SCHEDULER_DEFAULT_RELATIVE_PRIORITY);
    CHECK(!gxos_scheduler_set_thread_priority(stale_handle, 2));
    CHECK(reclaimed_thread->relative_priority ==
          GXOS_SCHEDULER_DEFAULT_RELATIVE_PRIORITY);

    CHECK(gxos_scheduler_close_handle(reclaimed_handle));
    CHECK(gxos_scheduler_discard_created_thread(reclaimed_thread));
    notification_object->public_handle_refs = 0;
    CHECK(gxos_scheduler_try_destroy_memory_resource_notification(
        notification_handle));
    CHECK(gxos_scheduler_close_handle(event_handle));
    CHECK(gxos_scheduler_try_destroy_event(event_handle));
    CHECK(gxos_scheduler_close_handle(worker_handle));
    CHECK(gxos_scheduler_discard_created_thread(worker));
    CHECK(gxos_scheduler_teardown(&g_scheduler));
    CHECK(gxos_scheduler_current_thread() == 0);
    CHECK(g_failures == 0);
    (void)printf("SET_THREAD_PRIORITY_MODEL_TESTS=%s checks=%u\n",
                 g_failures == 0 ? "PASSED" : "FAILED", g_checks);
    return g_failures == 0 ? 0 : 1;
}

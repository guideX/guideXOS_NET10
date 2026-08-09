#include "scheduler_foundation.h"
#include "create_event_w.h"
#include "create_memory_resource_notification.h"

#include <stdio.h>

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

static void destroy_event(GXOS_SCHEDULER_HANDLE handle)
{
    CHECK(gxos_scheduler_close_handle(handle));
    CHECK(gxos_scheduler_try_destroy_event(handle));
}

static uint32_t live_event_count(void)
{
    uint32_t index;
    uint32_t count = 0;
    for (index = 0; index != GXOS_SCHEDULER_MAX_EVENTS; ++index) {
        if (g_scheduler.events[index].live) ++count;
    }
    return count;
}

static uint32_t live_notification_count(void)
{
    uint32_t index;
    uint32_t count = 0;
    for (index = 0;
         index != GXOS_SCHEDULER_MAX_MEMORY_RESOURCE_NOTIFICATIONS;
         ++index) {
        if (g_scheduler.memory_resource_notifications[index].live) ++count;
    }
    return count;
}

static uint32_t live_object_count(void)
{
    uint32_t index;
    uint32_t count = 0;
    for (index = 0; index != GXOS_SCHEDULER_MAX_OBJECTS; ++index) {
        if (g_scheduler.objects[index].live) ++count;
    }
    return count;
}

int main(void)
{
    GXOS_SCHEDULER_HANDLE auto_event;
    GXOS_SCHEDULER_HANDLE manual_event;
    GXOS_SCHEDULER_HANDLE worker_handle;
    GXOS_SCHEDULER_HANDLE stale_handle;
    GXOS_SCHEDULER_HANDLE temporary_handles[GXOS_SCHEDULER_MAX_THREADS] = {0};
    GXOS_SCHEDULER_TCB *temporary_threads[GXOS_SCHEDULER_MAX_THREADS] = {0};
    GXOS_SCHEDULER_HANDLE temporary_events[GXOS_SCHEDULER_MAX_EVENTS] = {0};
    GXOS_SCHEDULER_TCB *worker;
    GXOS_SCHEDULER_SWITCH_PLAN plan;
    GXOS_CREATE_EVENT_W_CONTEXT event_context;
    GXOS_CREATE_MEMORY_RESOURCE_NOTIFICATION_CONTEXT notification_context;
    GXOS_SCHEDULER_HANDLE contract_events[4] = {0};
    GXOS_SCHEDULER_HANDLE notification_handle = 0;
    GXOS_SCHEDULER_HANDLE stale_notification_handle = 0;
    GXOS_SCHEDULER_MEMORY_RESOURCE_NOTIFICATION *notification;
    GXOS_SCHEDULER_OBJECT *notification_object;
    GXOS_SCHEDULER_WAITABLE *notification_waitable;
    uint32_t previous_suspend_count;
    uint32_t thread_count;
    uint32_t event_count;
    uint32_t index;
    uint32_t objects_before_failed_notification_creation;

    CHECK(gxos_scheduler_initialize(&g_scheduler, model_allocate, model_free,
                                    model_log_text, model_log_hex,
                                    model_log_u32));
    CHECK(gxos_scheduler_current_thread() == g_scheduler.boot_thread);
    CHECK(gxos_scheduler_current_gs_base() != 0);

    event_context.scheduler = &g_scheduler;
    CHECK((contract_events[0] = gxos_create_event_w_contract(
               &event_context, 0, 0, 0, 0)) != 0);
    CHECK((contract_events[1] = gxos_create_event_w_contract(
               &event_context, 0, 1, 0, 0)) != 0);
    CHECK((contract_events[2] = gxos_create_event_w_contract(
               &event_context, 0, 0, 1, 0)) != 0);
    CHECK((contract_events[3] = gxos_create_event_w_contract(
               &event_context, 0, 1, 1, 0)) != 0);
    CHECK(gxos_scheduler_event_from_handle(contract_events[0])->manual_reset == 0);
    CHECK(gxos_scheduler_event_from_handle(contract_events[0])->signaled == 0);
    CHECK(gxos_scheduler_event_from_handle(contract_events[1])->manual_reset == 1);
    CHECK(gxos_scheduler_event_from_handle(contract_events[1])->signaled == 0);
    CHECK(gxos_scheduler_event_from_handle(contract_events[2])->manual_reset == 0);
    CHECK(gxos_scheduler_event_from_handle(contract_events[2])->signaled == 1);
    CHECK(gxos_scheduler_event_from_handle(contract_events[3])->manual_reset == 1);
    CHECK(gxos_scheduler_event_from_handle(contract_events[3])->signaled == 1);
    {
        uint32_t before = live_event_count();
        CHECK(gxos_create_event_w_contract(&event_context, (void *)(uintptr_t)1,
                                           0, 0, 0) == 0);
        CHECK(live_event_count() == before);
        CHECK(gxos_scheduler_get_last_error() ==
              GXOS_CREATE_EVENT_W_ERROR_INVALID_PARAMETER);
        CHECK(gxos_create_event_w_contract(&event_context, 0, 0, 0,
                                           (const uint16_t *)(uintptr_t)1) == 0);
        CHECK(live_event_count() == before);
        CHECK(gxos_scheduler_get_last_error() ==
              GXOS_CREATE_EVENT_W_ERROR_INVALID_PARAMETER);
    }
    event_count = 0;
    while (event_count != GXOS_SCHEDULER_MAX_EVENTS &&
           gxos_create_event_w_contract(&event_context, 0, 0, 0, 0) != 0) {
        ++event_count;
    }
    CHECK(gxos_create_event_w_contract(&event_context, 0, 0, 0, 0) == 0);
    CHECK(gxos_scheduler_get_last_error() ==
          GXOS_CREATE_EVENT_W_ERROR_NOT_ENOUGH_MEMORY);
    CHECK(live_event_count() == GXOS_SCHEDULER_MAX_EVENTS);
    for (index = 0; index != GXOS_SCHEDULER_MAX_EVENTS; ++index) {
        if (g_scheduler.events[index].live) {
            GXOS_SCHEDULER_OBJECT *object =
                &g_scheduler.objects[g_scheduler.events[index].object_slot];
            GXOS_SCHEDULER_HANDLE live_handle =
                ((uint64_t)GXOS_SCHEDULER_HANDLE_MAGIC << 56) |
                ((uint64_t)GXOS_SCHEDULER_OBJECT_EVENT << 48) |
                ((uint64_t)g_scheduler.events[index].generation << 16) |
                ((uint64_t)g_scheduler.events[index].object_slot + 1U);
            CHECK(object->type == GXOS_SCHEDULER_OBJECT_EVENT);
            CHECK(gxos_scheduler_event_from_handle(live_handle) ==
                  &g_scheduler.events[index]);
            CHECK(gxos_scheduler_close_handle(live_handle));
            CHECK(gxos_scheduler_try_destroy_event(live_handle));
        }
    }
    CHECK(live_event_count() == 0);
    CHECK(!gxos_scheduler_initialize(&g_scheduler, model_allocate, model_free,
                                     model_log_text, model_log_hex,
                                     model_log_u32));
    CHECK(gxos_scheduler_current_thread() == g_scheduler.boot_thread);
    CHECK(gxos_scheduler_current_gs_base() != 0);

    notification_context.scheduler = &g_scheduler;
    objects_before_failed_notification_creation = live_object_count();
    CHECK(gxos_create_memory_resource_notification_contract(
              &notification_context, GXOS_MEMORY_RESOURCE_NOTIFICATION_HIGH) == 0);
    CHECK(live_notification_count() == 0);
    CHECK(live_object_count() == objects_before_failed_notification_creation);
    CHECK(gxos_scheduler_get_last_error() ==
          GXOS_CREATE_MEMORY_RESOURCE_NOTIFICATION_ERROR_INVALID_PARAMETER);
    CHECK(gxos_create_memory_resource_notification_contract(
              &notification_context, 2U) == 0);
    CHECK(live_notification_count() == 0);
    CHECK(live_object_count() == objects_before_failed_notification_creation);
    CHECK((notification_handle =
               gxos_create_memory_resource_notification_contract(
                   &notification_context,
                   GXOS_MEMORY_RESOURCE_NOTIFICATION_LOW)) != 0);
    CHECK(live_notification_count() == 1);
    notification = gxos_scheduler_memory_resource_notification_from_handle(
        notification_handle);
    notification_object = gxos_scheduler_object_from_handle(notification_handle);
    notification_waitable = gxos_scheduler_waitable_from_handle(notification_handle);
    CHECK(notification != 0);
    CHECK(notification_object != 0);
    CHECK(notification_object->type ==
          GXOS_SCHEDULER_OBJECT_MEMORY_RESOURCE_NOTIFICATION);
    CHECK(notification->notification_type ==
          GXOS_MEMORY_RESOURCE_NOTIFICATION_LOW);
    CHECK(notification->waitable.signaled == 0);
    CHECK(notification->waitable.waiter_count == 0);
    CHECK(notification_object->public_handle_refs == 1);
    CHECK(notification->close_state == 0);
    CHECK(notification_waitable == &notification->waitable);
    CHECK(gxos_scheduler_prepare_wait(notification_handle, &plan) ==
          GXOS_SCHEDULER_WAIT_FAILURE);
    CHECK(notification->waitable.waiter_count == 0);
    notification->waitable.signaled = 1;
    CHECK(gxos_scheduler_prepare_wait(notification_handle, &plan) ==
          GXOS_SCHEDULER_WAIT_SIGNALED);
    CHECK(gxos_scheduler_finish_wait(notification_handle) ==
          GXOS_SCHEDULER_WAIT_SIGNALED);
    CHECK(notification->waitable.signaled == 1);
    notification->waitable.signaled = 0;
    CHECK(gxos_scheduler_event_from_handle(notification_handle) == 0);
    CHECK(gxos_scheduler_thread_from_handle(notification_handle) == 0);
    CHECK(!gxos_scheduler_close_handle(notification_handle));
    CHECK(gxos_create_memory_resource_notification_contract(
              &notification_context, GXOS_MEMORY_RESOURCE_NOTIFICATION_LOW) == 0);
    CHECK(live_notification_count() == 1);
    CHECK(gxos_scheduler_memory_resource_notification_from_handle(
              notification_handle) == notification);
    stale_notification_handle = notification_handle;
    notification_object->public_handle_refs = 0;
    CHECK(gxos_scheduler_try_destroy_memory_resource_notification(
        stale_notification_handle));
    CHECK(gxos_scheduler_memory_resource_notification_from_handle(
              stale_notification_handle) == 0);
    CHECK(notification->live == 0);
    CHECK(notification->close_state == 1);
    CHECK((notification_handle =
               gxos_create_memory_resource_notification_contract(
                   &notification_context,
                   GXOS_MEMORY_RESOURCE_NOTIFICATION_LOW)) != 0);
    CHECK(notification_handle != stale_notification_handle);
    CHECK(gxos_scheduler_memory_resource_notification_from_handle(
              stale_notification_handle) == 0);
    CHECK(gxos_scheduler_memory_resource_notification_from_handle(
              notification_handle) != 0);
    notification_object = gxos_scheduler_object_from_handle(notification_handle);
    CHECK(notification_object != 0);
    notification_object->public_handle_refs = 0;
    CHECK(gxos_scheduler_try_destroy_memory_resource_notification(
        notification_handle));
    CHECK(live_notification_count() == 0);

    /* Fill every object record, then prove notification creation is atomic. */
    event_count = 0;
    while (event_count != GXOS_SCHEDULER_MAX_EVENTS &&
           gxos_scheduler_create_event(&g_scheduler, 0, 0,
                                       &temporary_events[event_count])) {
        ++event_count;
    }
    thread_count = 0;
    while (thread_count != GXOS_SCHEDULER_MAX_THREADS &&
           gxos_scheduler_create_suspended_thread(
               &g_scheduler, model_entry, 0,
               &temporary_handles[thread_count],
               &temporary_threads[thread_count])) {
        ++thread_count;
    }
    CHECK(live_object_count() == GXOS_SCHEDULER_MAX_OBJECTS);
    CHECK(GXOS_SCHEDULER_MAX_OBJECTS - live_object_count() == 0);
    CHECK(gxos_create_memory_resource_notification_contract(
              &notification_context,
              GXOS_MEMORY_RESOURCE_NOTIFICATION_LOW) == 0);
    CHECK(live_notification_count() == 0);
    CHECK(live_object_count() == GXOS_SCHEDULER_MAX_OBJECTS);
    CHECK(gxos_scheduler_event_from_handle(temporary_events[0]) != 0);
    CHECK(gxos_scheduler_thread_from_handle(temporary_handles[0]) != 0);
    for (index = 0; index != thread_count; ++index) {
        CHECK(gxos_scheduler_close_handle(temporary_handles[index]));
        CHECK(gxos_scheduler_discard_created_thread(temporary_threads[index]));
    }
    for (index = 0; index != event_count; ++index) {
        CHECK(gxos_scheduler_close_handle(temporary_events[index]));
        CHECK(gxos_scheduler_try_destroy_event(temporary_events[index]));
    }
    CHECK(live_object_count() == 1);

    CHECK(gxos_scheduler_create_event(&g_scheduler, 0, 0, &auto_event));
    CHECK(gxos_scheduler_create_event(&g_scheduler, 1, 0, &manual_event));
    CHECK(gxos_scheduler_memory_resource_notification_from_handle(auto_event) == 0);
    CHECK(gxos_scheduler_memory_resource_notification_from_handle(manual_event) == 0);
    CHECK(!gxos_scheduler_resume_thread(auto_event, &previous_suspend_count));
    CHECK(!gxos_scheduler_signal_event((GXOS_SCHEDULER_HANDLE)0x1234));
    CHECK(!gxos_scheduler_reset_event((GXOS_SCHEDULER_HANDLE)0x1234));

    CHECK(gxos_scheduler_create_suspended_thread(
        &g_scheduler, model_entry, (void *)(uintptr_t)0x55,
        &worker_handle, &worker));
    CHECK(worker->state == GXOS_SCHEDULER_THREAD_CREATED_SUSPENDED);
    CHECK(worker->execution_refs == 1);
    CHECK(worker->public_handle_refs == 1);
    CHECK(gxos_scheduler_thread_from_handle(worker_handle) == worker);
    CHECK(gxos_scheduler_event_from_handle(worker_handle) == 0);
    CHECK(gxos_scheduler_memory_resource_notification_from_handle(worker_handle) == 0);
    CHECK(gxos_scheduler_prepare_wait(worker_handle, &plan) ==
          GXOS_SCHEDULER_WAIT_FAILURE);
    CHECK(gxos_scheduler_resume_thread(worker_handle, &previous_suspend_count));
    CHECK(previous_suspend_count == 1);
    CHECK(worker->state == GXOS_SCHEDULER_THREAD_RUNNABLE);
    CHECK(gxos_scheduler_resume_thread(worker_handle, &previous_suspend_count));
    CHECK(previous_suspend_count == 0);
    CHECK(worker->state == GXOS_SCHEDULER_THREAD_RUNNABLE);
    CHECK(gxos_scheduler_runnable_count() == 1);

    CHECK(gxos_scheduler_signal_event(auto_event));
    CHECK(gxos_scheduler_event_is_signaled(auto_event));
    CHECK(gxos_scheduler_prepare_wait(auto_event, &plan) ==
          GXOS_SCHEDULER_WAIT_SIGNALED);
    CHECK(gxos_scheduler_finish_wait(auto_event) == GXOS_SCHEDULER_WAIT_SIGNALED);
    CHECK(!gxos_scheduler_event_is_signaled(auto_event));
    CHECK(gxos_scheduler_signal_event(manual_event));
    CHECK(gxos_scheduler_event_is_signaled(manual_event));
    CHECK(gxos_scheduler_prepare_wait(manual_event, &plan) ==
          GXOS_SCHEDULER_WAIT_SIGNALED);
    CHECK(gxos_scheduler_finish_wait(manual_event) == GXOS_SCHEDULER_WAIT_SIGNALED);
    CHECK(gxos_scheduler_event_is_signaled(manual_event));
    CHECK(gxos_scheduler_reset_event(manual_event));
    CHECK(!gxos_scheduler_event_is_signaled(manual_event));

    stale_handle = auto_event;
    destroy_event(auto_event);
    CHECK(gxos_scheduler_event_from_handle(stale_handle) == 0);
    CHECK(gxos_scheduler_create_event(&g_scheduler, 0, 0, &auto_event));
    CHECK(auto_event != stale_handle);
    CHECK(!gxos_scheduler_try_destroy_event(stale_handle));

    CHECK(gxos_scheduler_close_handle(worker_handle));
    CHECK(worker->live && worker->execution_refs == 1);
    CHECK(!gxos_scheduler_close_handle(worker_handle));
    CHECK(!gxos_scheduler_try_reclaim_thread(worker));
    worker->state = GXOS_SCHEDULER_THREAD_CREATED_SUSPENDED;
    CHECK(gxos_scheduler_discard_created_thread(worker));
    CHECK(gxos_scheduler_thread_from_handle(worker_handle) == 0);

    destroy_event(manual_event);

    /* A registered waiter prevents event destruction until it is woken. */
    CHECK(gxos_scheduler_create_suspended_thread(
        &g_scheduler, model_entry, 0, &worker_handle, &worker));
    CHECK(gxos_scheduler_resume_thread(worker_handle, &previous_suspend_count));
    CHECK(gxos_scheduler_create_event(&g_scheduler, 1, 0, &manual_event));
    CHECK(gxos_scheduler_prepare_wait(manual_event, &plan) ==
          GXOS_SCHEDULER_WAIT_BLOCKED);
    stale_handle = manual_event;
    CHECK(gxos_scheduler_close_handle(manual_event));
    CHECK(!gxos_scheduler_try_destroy_event(manual_event));
    CHECK(gxos_scheduler_signal_event(manual_event));
    g_scheduler.current = g_scheduler.boot_thread;
    g_scheduler.boot_thread->state = GXOS_SCHEDULER_THREAD_RUNNING;
    worker->state = GXOS_SCHEDULER_THREAD_RUNNABLE;
    CHECK(gxos_scheduler_try_destroy_event(stale_handle));
    CHECK(gxos_scheduler_close_handle(worker_handle));
    CHECK(gxos_scheduler_discard_created_thread(worker));

    destroy_event(auto_event);

    thread_count = 0;
    while (thread_count != GXOS_SCHEDULER_MAX_THREADS &&
           gxos_scheduler_create_suspended_thread(
               &g_scheduler, model_entry, 0,
               &temporary_handles[thread_count],
               &temporary_threads[thread_count])) {
        ++thread_count;
    }
    CHECK(thread_count != 0);
    CHECK(thread_count < GXOS_SCHEDULER_MAX_THREADS ||
          !gxos_scheduler_create_suspended_thread(
              &g_scheduler, model_entry, 0, &worker_handle, &worker));
    for (index = 0; index != thread_count; ++index) {
        CHECK(gxos_scheduler_close_handle(temporary_handles[index]));
        CHECK(gxos_scheduler_discard_created_thread(temporary_threads[index]));
    }

    event_count = 0;
    while (event_count != GXOS_SCHEDULER_MAX_EVENTS &&
           gxos_scheduler_create_event(&g_scheduler, 0, 0,
                                       &temporary_events[event_count])) {
        ++event_count;
    }
    CHECK(event_count != 0);
    CHECK(event_count < GXOS_SCHEDULER_MAX_EVENTS ||
          !gxos_scheduler_create_event(&g_scheduler, 0, 0, &auto_event));
    for (index = 0; index != event_count; ++index) {
        CHECK(gxos_scheduler_close_handle(temporary_events[index]));
        CHECK(gxos_scheduler_try_destroy_event(temporary_events[index]));
    }

    CHECK(gxos_scheduler_teardown(&g_scheduler));
    CHECK(gxos_scheduler_current_gs_base() == 0);
    CHECK(g_failures == 0);
    (void)printf("SCHEDULER_MODEL_TESTS=PASSED checks=%u\n", g_checks);
    return g_failures == 0 ? 0 : 1;
}

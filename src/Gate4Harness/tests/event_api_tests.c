#include "event_api.h"

#include <stdio.h>

static unsigned char g_pages[256U * GXOS_SCHEDULER_PAGE_SIZE]
    __attribute__((aligned(GXOS_SCHEDULER_PAGE_SIZE)));
static unsigned char g_page_used[256U];
static GXOS_SCHEDULER g_scheduler;
static GXOS_SCHEDULER_TCB *g_worker;
static unsigned int g_checks;
static unsigned int g_failures;
static GXOS_SCHEDULER_HANDLE g_handle_storage;
static uint64_t g_wait_model_clock_ms;
static unsigned int g_wait_model_mode;

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

static int model_now_ms(void *context, uint64_t *now_ms)
{
    (void)context;
    if (now_ms == 0) return 0;
    *now_ms = g_wait_model_clock_ms;
    return 1;
}

void gxos_scheduler_start_worker(void)
{
}

void gxos_scheduler_invalid_thread_return(void)
{
}

static int read_one_handle(const void *source,
                           GXOS_SCHEDULER_HANDLE *handle_out)
{
    if (source != &g_handle_storage || handle_out == 0) return 0;
    *handle_out = g_handle_storage;
    return 1;
}

void gxos_scheduler_main_block(GXOS_SCHEDULER_HANDLE handle,
                               GXOS_SCHEDULER_REGISTER_SNAPSHOT *snapshot,
                               int32_t *wait_result)
{
    GXOS_SCHEDULER_SWITCH_PLAN plan;
    GXOS_SCHEDULER_WAIT_RECORD *record = 0;
    GXOS_SCHEDULER_TCB *main_thread = g_scheduler.boot_thread;
    GXOS_SCHEDULER_TCB *worker_thread;
    int result;
    (void)snapshot;
    result = gxos_scheduler_prepare_wait_record(handle, &plan, &record);
    if (result == GXOS_SCHEDULER_WAIT_SIGNALED) {
        *wait_result = gxos_scheduler_finish_wait(handle);
        return;
    }
    if (result != GXOS_SCHEDULER_WAIT_BLOCKED || record == 0) {
        *wait_result = GXOS_SCHEDULER_WAIT_FAILURE;
        return;
    }
    worker_thread = gxos_scheduler_current_thread();
    CHECK(main_thread->state == GXOS_SCHEDULER_THREAD_BLOCKED);
    CHECK(record->active && record->pin_held && record->waiter_linked);
    if (g_wait_model_mode == 1U) {
        CHECK(record->timeout_armed);
        CHECK(gxos_scheduler_service_timeouts(record->deadline_ms) == 1);
        CHECK(record->completed &&
              record->completion_result == GXOS_WAIT_TIMEOUT);
    } else {
        CHECK(gxos_scheduler_signal_event(handle));
        CHECK(record->completed &&
              record->completion_result == GXOS_WAIT_OBJECT_0);
    }
    CHECK(main_thread->state == GXOS_SCHEDULER_THREAD_RUNNABLE);
    g_scheduler.current = main_thread;
    main_thread->state = GXOS_SCHEDULER_THREAD_RUNNING;
    main_thread->runnable_queued = 0;
    g_scheduler.runnable_count = 0;
    worker_thread->state = GXOS_SCHEDULER_THREAD_RUNNABLE;
    worker_thread->runnable_queued = 1;
    g_scheduler.runnable_queue[0] = worker_thread;
    g_scheduler.runnable_count = 1;
    *wait_result = gxos_scheduler_finish_wait(handle);
}

void gxos_scheduler_worker_wait(GXOS_SCHEDULER_HANDLE handle,
                                GXOS_SCHEDULER_REGISTER_SNAPSHOT *snapshot,
                                int32_t *wait_result)
{
    gxos_scheduler_main_block(handle, snapshot, wait_result);
}

int main(void)
{
    GXOS_EVENT_API_CONTEXT context;
    GXOS_SCHEDULER_HANDLE immediate;
    GXOS_SCHEDULER_HANDLE blocked;
    GXOS_SCHEDULER_HANDLE stale;
    GXOS_SCHEDULER_OBJECT *object;
    GXOS_SCHEDULER_EVENT *event;
    GXOS_SCHEDULER_SWITCH_PLAN plan;
    GXOS_SCHEDULER_WAIT_RECORD *record = 0;
    GXOS_SCHEDULER_WAIT_RECORD *duplicate = 0;
    GXOS_SCHEDULER_HANDLE reset_manual;
    GXOS_SCHEDULER_HANDLE reset_auto;
    GXOS_SCHEDULER_HANDLE reset_pending;
    GXOS_SCHEDULER_HANDLE stale_reset;
    GXOS_SCHEDULER_HANDLE reset_worker_handle;
    GXOS_SCHEDULER_TCB *reset_worker;
    GXOS_SCHEDULER_WAIT_RECORD *reset_record;
    GXOS_SCHEDULER_OBJECT *reset_object;
    GXOS_SCHEDULER_EVENT *reset_event;
    uint32_t reset_internal_refs;
    uint32_t reset_public_handle_refs;
    uint32_t index;
    uint32_t before_refs;
    GXOS_SCHEDULER_HANDLE single_manual;
    GXOS_SCHEDULER_HANDLE single_auto;
    GXOS_SCHEDULER_HANDLE single_wrapper;
    GXOS_SCHEDULER_HANDLE single_stale;
    GXOS_SCHEDULER_HANDLE single_thread_handle;
    GXOS_SCHEDULER_TCB *single_thread;
    GXOS_SCHEDULER_OBJECT *single_object;
    GXOS_SCHEDULER_EVENT *single_event;

    CHECK(gxos_scheduler_initialize(&g_scheduler, model_allocate, model_free,
                                    model_log_text, model_log_hex,
                                    model_log_u32));
    g_wait_model_clock_ms = 1000;
    g_wait_model_mode = 0;
    CHECK(gxos_scheduler_configure_clock(&g_scheduler, model_now_ms, 0));
    context.scheduler = &g_scheduler;
    context.read_handle = read_one_handle;

    CHECK(gxos_scheduler_create_event(&g_scheduler, 1, 1, &immediate));
    g_handle_storage = immediate;
    CHECK(gxos_wait_for_multiple_objects_ex_contract(
              &context, 1, &g_handle_storage, 0, GXOS_INFINITE, 0) ==
          GXOS_WAIT_OBJECT_0);
    CHECK(gxos_scheduler_event_is_signaled(immediate));

    CHECK(gxos_scheduler_create_event(&g_scheduler, 1, 0, &blocked));
    CHECK(gxos_scheduler_create_suspended_thread(
              &g_scheduler, model_entry, 0, &stale, &g_worker));
    CHECK(gxos_scheduler_resume_thread(stale, 0));
    g_handle_storage = blocked;
    object = gxos_scheduler_object_from_handle(blocked);
    event = gxos_scheduler_event_from_handle(blocked);
    CHECK(object != 0 && event != 0);
    before_refs = object->internal_refs;
    CHECK(gxos_wait_for_multiple_objects_ex_contract(
              &context, 1, &g_handle_storage, 0, GXOS_INFINITE, 0) ==
          GXOS_WAIT_OBJECT_0);
    CHECK(event->waiter_count == 0);
    CHECK(object->internal_refs == before_refs);
    CHECK(gxos_scheduler_active_wait_count() == 0);
    CHECK(gxos_scheduler_blocked_count() == 0);
    CHECK(gxos_scheduler_event_is_signaled(blocked));

    CHECK(gxos_scheduler_close_handle(blocked));
    CHECK(gxos_scheduler_try_destroy_event(blocked));
    CHECK(!gxos_set_event_contract(&context, blocked));
    CHECK(gxos_scheduler_get_last_error() == GXOS_EVENT_ERROR_INVALID_HANDLE);
    CHECK(gxos_scheduler_close_handle(stale));
    CHECK(gxos_scheduler_discard_created_thread(g_worker));
    CHECK(gxos_scheduler_create_suspended_thread(
              &g_scheduler, model_entry, 0, &stale, &g_worker));
    CHECK(gxos_scheduler_resume_thread(stale, 0));
    CHECK(gxos_scheduler_create_event(&g_scheduler, 1, 0, &blocked));
    object = gxos_scheduler_object_from_handle(blocked);
    event = gxos_scheduler_event_from_handle(blocked);
    CHECK(gxos_scheduler_prepare_wait_record(blocked, &plan, &record) ==
          GXOS_SCHEDULER_WAIT_BLOCKED);
    CHECK(record != 0 && record->pin_held && object->internal_refs == 2);
    g_scheduler.current = g_scheduler.boot_thread;
    g_scheduler.boot_thread->state = GXOS_SCHEDULER_THREAD_RUNNING;
    CHECK(gxos_scheduler_prepare_wait_record(blocked, &plan, &duplicate) ==
          GXOS_SCHEDULER_WAIT_FAILURE);
    CHECK(duplicate == 0 && event->waiter_count == 1 &&
          gxos_scheduler_active_wait_count() == 1);
    g_scheduler.boot_thread->state = GXOS_SCHEDULER_THREAD_BLOCKED;
    g_scheduler.current = g_worker;
    CHECK(gxos_scheduler_close_handle(blocked));
    CHECK(!gxos_scheduler_try_destroy_event(blocked));
    CHECK(gxos_scheduler_signal_event(blocked));
    g_scheduler.current = g_scheduler.boot_thread;
    g_scheduler.boot_thread->state = GXOS_SCHEDULER_THREAD_RUNNING;
    g_scheduler.boot_thread->runnable_queued = 0;
    g_scheduler.runnable_count = 0;
    g_worker->state = GXOS_SCHEDULER_THREAD_RUNNABLE;
    CHECK(gxos_scheduler_finish_wait(blocked) == GXOS_SCHEDULER_WAIT_SIGNALED);
    CHECK(object->internal_refs == 1 && event->waiter_count == 0);
    CHECK(gxos_scheduler_try_destroy_event(blocked));
    CHECK(gxos_scheduler_close_handle(stale));
    CHECK(gxos_scheduler_discard_created_thread(g_worker));

    CHECK(gxos_scheduler_create_event(&g_scheduler, 1, 0, &blocked));
    object = gxos_scheduler_object_from_handle(blocked);
    event = gxos_scheduler_event_from_handle(blocked);
    before_refs = object->internal_refs;
    CHECK(gxos_scheduler_prepare_wait_record(blocked, &plan, &record) ==
          GXOS_SCHEDULER_WAIT_FAILURE);
    CHECK(g_scheduler.boot_thread->state == GXOS_SCHEDULER_THREAD_RUNNING);
    CHECK(event->waiter_count == 0 && gxos_scheduler_active_wait_count() == 0);
    CHECK(object->internal_refs == before_refs);

    for (index = 0; index != GXOS_SCHEDULER_MAX_WAIT_RECORDS; ++index) {
        g_scheduler.wait_records[index].valid = 1;
    }
    g_handle_storage = blocked;
    CHECK(gxos_wait_for_multiple_objects_ex_contract(
              &context, 1, &g_handle_storage, 0, GXOS_INFINITE, 0) ==
          GXOS_WAIT_FAILED);
    CHECK(gxos_scheduler_get_last_error() == GXOS_EVENT_ERROR_NOT_ENOUGH_MEMORY);
    CHECK(event->waiter_count == 0 && object->internal_refs == before_refs);
    for (index = 0; index != GXOS_SCHEDULER_MAX_WAIT_RECORDS; ++index) {
        g_scheduler.wait_records[index].valid = 0;
    }

    CHECK(gxos_scheduler_close_handle(blocked));
    CHECK(gxos_scheduler_try_destroy_event(blocked));
    CHECK(gxos_scheduler_create_event(&g_scheduler, 1, 0, &blocked));
    stale = blocked;
    CHECK(gxos_scheduler_close_handle(blocked));
    CHECK(gxos_scheduler_try_destroy_event(stale));
    CHECK(gxos_wait_for_multiple_objects_ex_contract(
              &context, 1, &g_handle_storage, 0, GXOS_INFINITE, 0) ==
          GXOS_WAIT_FAILED);
    CHECK(gxos_scheduler_get_last_error() == GXOS_EVENT_ERROR_INVALID_HANDLE);
    CHECK(gxos_scheduler_create_event(&g_scheduler, 1, 0, &blocked));
    CHECK(stale != blocked);
    g_handle_storage = (GXOS_SCHEDULER_HANDLE)0x1234;
    CHECK(gxos_wait_for_multiple_objects_ex_contract(
              &context, 1, &g_handle_storage, 0, GXOS_INFINITE, 0) ==
          GXOS_WAIT_FAILED);
    CHECK(gxos_scheduler_get_last_error() == GXOS_EVENT_ERROR_INVALID_HANDLE);
    g_handle_storage = blocked;
    CHECK(gxos_wait_for_multiple_objects_ex_contract(
              &context, 0, &g_handle_storage, 0, GXOS_INFINITE, 0) ==
          GXOS_WAIT_FAILED);
    CHECK(gxos_scheduler_get_last_error() == GXOS_EVENT_ERROR_INVALID_PARAMETER);
    CHECK(gxos_wait_for_multiple_objects_ex_contract(
              &context, 2, &g_handle_storage, 0, GXOS_INFINITE, 0) ==
          GXOS_WAIT_FAILED);
    CHECK(gxos_wait_for_multiple_objects_ex_contract(
              &context, 1, &g_handle_storage, 1, GXOS_INFINITE, 0) ==
          GXOS_WAIT_FAILED);
    CHECK(gxos_wait_for_multiple_objects_ex_contract(
              &context, 1, &g_handle_storage, 0, 1, 0) == GXOS_WAIT_FAILED);
    CHECK(gxos_scheduler_get_last_error() == GXOS_EVENT_ERROR_INVALID_PARAMETER);
    CHECK(gxos_wait_for_multiple_objects_ex_contract(
              &context, 1, &g_handle_storage, 0, GXOS_INFINITE, 1) ==
          GXOS_WAIT_FAILED);
    CHECK(gxos_wait_for_multiple_objects_ex_contract(
              &context, 1, (const void *)(uintptr_t)0x1234, 0,
              GXOS_INFINITE, 0) == GXOS_WAIT_FAILED);
    CHECK(gxos_scheduler_close_handle(blocked));
    CHECK(gxos_scheduler_try_destroy_event(blocked));

    CHECK(gxos_scheduler_create_event(&g_scheduler, 1, 0, &blocked));
    object = gxos_scheduler_object_from_handle(blocked);
    event = gxos_scheduler_event_from_handle(blocked);
    g_handle_storage = blocked;
    CHECK(gxos_scheduler_signal_event(blocked));
    CHECK(gxos_wait_for_multiple_objects_ex_contract(
              &context, 1, &g_handle_storage, 0, GXOS_INFINITE, 0) ==
          GXOS_WAIT_OBJECT_0);
    CHECK(event->signaled == 1 && event->waiter_count == 0);
    CHECK(object->internal_refs == 1);
    CHECK(gxos_scheduler_close_handle(blocked));
    CHECK(gxos_scheduler_try_destroy_event(blocked));

    /* ResetEvent is a public adapter over both event modes and is idempotent. */
    CHECK(gxos_scheduler_create_event(&g_scheduler, 1, 1, &reset_manual));
    reset_object = gxos_scheduler_object_from_handle(reset_manual);
    reset_event = gxos_scheduler_event_from_handle(reset_manual);
    reset_internal_refs = reset_object->internal_refs;
    reset_public_handle_refs = reset_object->public_handle_refs;
    CHECK(gxos_reset_event_contract(&context, reset_manual));
    CHECK(!reset_event->signaled && reset_event->manual_reset);
    CHECK(reset_event->waiter_count == 0);
    CHECK(reset_object->internal_refs == reset_internal_refs &&
          reset_object->public_handle_refs == reset_public_handle_refs);
    CHECK(gxos_reset_event_contract(&context, reset_manual));
    CHECK(!reset_event->signaled);
    CHECK(gxos_set_event_contract(&context, reset_manual));
    CHECK(reset_event->signaled);
    CHECK(gxos_reset_event_contract(&context, reset_manual));
    CHECK(!reset_event->signaled);

    CHECK(gxos_scheduler_create_event(&g_scheduler, 0, 1, &reset_auto));
    reset_object = gxos_scheduler_object_from_handle(reset_auto);
    reset_event = gxos_scheduler_event_from_handle(reset_auto);
    reset_internal_refs = reset_object->internal_refs;
    reset_public_handle_refs = reset_object->public_handle_refs;
    CHECK(gxos_reset_event_contract(&context, reset_auto));
    CHECK(!reset_event->signaled && !reset_event->manual_reset);
    CHECK(reset_object->internal_refs == reset_internal_refs &&
          reset_object->public_handle_refs == reset_public_handle_refs);
    CHECK(gxos_reset_event_contract(&context, reset_auto));
    CHECK(!reset_event->signaled);
    CHECK(gxos_set_event_contract(&context, reset_auto));
    CHECK(reset_event->signaled);
    CHECK(gxos_reset_event_contract(&context, reset_auto));
    CHECK(!reset_event->signaled);
    for (index = 0; index != 4U; ++index) {
        CHECK(gxos_set_event_contract(&context, reset_auto));
        CHECK(reset_event->signaled);
        CHECK(gxos_reset_event_contract(&context, reset_auto));
        CHECK(!reset_event->signaled);
        CHECK(gxos_reset_event_contract(&context, reset_auto));
        CHECK(!reset_event->signaled);
    }

    /* A pending waiter remains linked and blocked while ResetEvent runs. */
    CHECK(gxos_scheduler_create_event(&g_scheduler, 1, 0, &reset_pending));
    CHECK(gxos_scheduler_create_suspended_thread(
              &g_scheduler, model_entry, 0, &reset_worker_handle,
              &reset_worker));
    CHECK(gxos_scheduler_resume_thread(reset_worker_handle, 0));
    reset_object = gxos_scheduler_object_from_handle(reset_pending);
    reset_event = gxos_scheduler_event_from_handle(reset_pending);
    reset_internal_refs = reset_object->internal_refs;
    reset_public_handle_refs = reset_object->public_handle_refs;
    reset_record = 0;
    CHECK(gxos_scheduler_prepare_wait_record(
              reset_pending, &plan, &reset_record) ==
          GXOS_SCHEDULER_WAIT_BLOCKED);
    CHECK(reset_record != 0 && reset_record->active &&
          reset_record->waiter_linked && !reset_record->completed);
    CHECK(reset_event->waiter_count == 1 &&
          reset_object->internal_refs == reset_internal_refs + 1U &&
          reset_object->public_handle_refs == reset_public_handle_refs);
    CHECK(g_scheduler.boot_thread->state == GXOS_SCHEDULER_THREAD_BLOCKED);
    CHECK(gxos_scheduler_active_wait_count() == 1);
    CHECK(gxos_reset_event_contract(&context, reset_pending));
    CHECK(!reset_event->signaled && reset_event->waiter_count == 1);
    CHECK(reset_record->active && reset_record->waiter_linked &&
          !reset_record->completed);
    CHECK(g_scheduler.boot_thread->state == GXOS_SCHEDULER_THREAD_BLOCKED);
    CHECK(gxos_scheduler_active_wait_count() == 1);
    CHECK(reset_object->internal_refs == reset_internal_refs + 1U &&
          reset_object->public_handle_refs == reset_public_handle_refs);

    /* Completion is not revoked if ResetEvent follows SetEvent. */
    CHECK(gxos_scheduler_signal_event(reset_pending));
    CHECK(reset_record->completed &&
          reset_record->completion_result == GXOS_WAIT_OBJECT_0);
    g_scheduler.current = g_scheduler.boot_thread;
    g_scheduler.boot_thread->state = GXOS_SCHEDULER_THREAD_RUNNING;
    g_scheduler.boot_thread->runnable_queued = 0;
    g_scheduler.runnable_count = 0;
    reset_worker->state = GXOS_SCHEDULER_THREAD_RUNNABLE;
    CHECK(gxos_reset_event_contract(&context, reset_pending));
    CHECK(!reset_event->signaled && reset_event->waiter_count == 0);
    CHECK(reset_record->active && reset_record->completed &&
          reset_record->completion_result == GXOS_WAIT_OBJECT_0);
    CHECK(gxos_scheduler_active_wait_count() == 1);
    CHECK(reset_object->internal_refs == reset_internal_refs + 1U &&
          reset_object->public_handle_refs == reset_public_handle_refs);
    CHECK(gxos_scheduler_finish_wait(reset_pending) ==
          GXOS_SCHEDULER_WAIT_SIGNALED);
    CHECK(reset_event->waiter_count == 0 &&
          gxos_scheduler_active_wait_count() == 0 &&
          reset_object->internal_refs == reset_internal_refs);
    CHECK(gxos_scheduler_close_handle(reset_pending));
    CHECK(gxos_scheduler_try_destroy_event(reset_pending));
    CHECK(gxos_scheduler_close_handle(reset_worker_handle));
    CHECK(gxos_scheduler_discard_created_thread(reset_worker));
    stale_reset = reset_pending;

    /* Invalid, closed, stale, non-event, and NULL handles do not mutate state. */
    CHECK(!gxos_reset_event_contract(&context, (GXOS_SCHEDULER_HANDLE)0));
    CHECK(gxos_scheduler_get_last_error() == GXOS_EVENT_ERROR_INVALID_HANDLE);
    CHECK(!gxos_reset_event_contract(&context,
                                     (GXOS_SCHEDULER_HANDLE)0x1234));
    CHECK(gxos_scheduler_get_last_error() == GXOS_EVENT_ERROR_INVALID_HANDLE);
    CHECK(gxos_scheduler_create_event(&g_scheduler, 1, 1, &reset_pending));
    reset_object = gxos_scheduler_object_from_handle(reset_pending);
    reset_event = gxos_scheduler_event_from_handle(reset_pending);
    reset_internal_refs = reset_object->internal_refs;
    reset_public_handle_refs = reset_object->public_handle_refs;
    CHECK(gxos_scheduler_close_handle(reset_pending));
    CHECK(!gxos_reset_event_contract(&context, reset_pending));
    CHECK(gxos_scheduler_get_last_error() == GXOS_EVENT_ERROR_INVALID_HANDLE);
    CHECK(reset_event->signaled && reset_event->waiter_count == 0 &&
          reset_object->internal_refs == reset_internal_refs &&
          reset_object->public_handle_refs == 0);
    CHECK(gxos_scheduler_try_destroy_event(reset_pending));
    CHECK(!gxos_reset_event_contract(&context, reset_pending));
    CHECK(gxos_scheduler_get_last_error() == GXOS_EVENT_ERROR_INVALID_HANDLE);
    CHECK(gxos_scheduler_create_event(&g_scheduler, 1, 0, &reset_pending));
    CHECK(reset_pending != stale_reset);
    CHECK(!gxos_reset_event_contract(&context, stale_reset));
    CHECK(gxos_scheduler_get_last_error() == GXOS_EVENT_ERROR_INVALID_HANDLE);
    CHECK(gxos_scheduler_create_suspended_thread(
              &g_scheduler, model_entry, 0, &reset_worker_handle,
              &reset_worker));
    reset_object = gxos_scheduler_object_from_handle(reset_worker_handle);
    CHECK(!gxos_reset_event_contract(&context, reset_worker_handle));
    CHECK(gxos_scheduler_get_last_error() == GXOS_EVENT_ERROR_INVALID_HANDLE);
    CHECK(reset_worker->state == GXOS_SCHEDULER_THREAD_CREATED_SUSPENDED &&
          reset_object->public_handle_refs == 1 &&
          reset_object->internal_refs == 1);
    CHECK(gxos_scheduler_close_handle(reset_worker_handle));
    CHECK(gxos_scheduler_discard_created_thread(reset_worker));
    CHECK(gxos_scheduler_close_handle(reset_pending));
    CHECK(gxos_scheduler_try_destroy_event(reset_pending));

    /* WaitForSingleObjectEx shares the canonical event, handle, timeout, and
       scheduler path.  The host shim models the other runnable thread as the
       signaler or as the cooperative timer service. */
    CHECK(gxos_scheduler_close_handle(reset_manual));
    CHECK(gxos_scheduler_try_destroy_event(reset_manual));
    CHECK(gxos_scheduler_close_handle(reset_auto));
    CHECK(gxos_scheduler_try_destroy_event(reset_auto));
    CHECK(gxos_scheduler_close_handle(immediate));
    CHECK(gxos_scheduler_try_destroy_event(immediate));

    CHECK(gxos_scheduler_create_event(&g_scheduler, 1, 1, &single_manual));
    single_object = gxos_scheduler_object_from_handle(single_manual);
    single_event = gxos_scheduler_event_from_handle(single_manual);
    CHECK(single_object != 0 && single_event != 0 &&
          single_object->internal_refs == 1 && single_event->signaled);
    gxos_scheduler_set_last_error(0x1234U);
    CHECK(gxos_wait_for_single_object_ex_contract(
              &context, single_manual, 0, 0) == GXOS_WAIT_OBJECT_0);
    CHECK(single_event->signaled && single_event->waiter_count == 0 &&
          gxos_scheduler_get_last_error() == 0x1234U);
    CHECK(gxos_wait_for_single_object_ex_contract(
              &context, single_manual, GXOS_INFINITE, 0) == GXOS_WAIT_OBJECT_0);
    CHECK(single_event->signaled);
    CHECK(gxos_reset_event_contract(&context, single_manual));
    CHECK(gxos_wait_for_single_object_ex_contract(
              &context, single_manual, 0, 0) == GXOS_WAIT_TIMEOUT);
    CHECK(gxos_scheduler_get_last_error() == 0x1234U);
    gxos_scheduler_set_last_error(0x2468U);
    CHECK(gxos_wait_for_single_object_ex_contract(
              &context, (GXOS_SCHEDULER_HANDLE)0, 0, 0) == GXOS_WAIT_FAILED);
    CHECK(gxos_scheduler_get_last_error() == GXOS_EVENT_ERROR_INVALID_HANDLE &&
          g_scheduler.boot_thread->state == GXOS_SCHEDULER_THREAD_RUNNING);
    gxos_scheduler_set_last_error(0x1234U);

    CHECK(gxos_scheduler_create_suspended_thread(
              &g_scheduler, model_entry, 0, &single_thread_handle,
              &single_thread));
    CHECK(gxos_scheduler_resume_thread(single_thread_handle, 0));

    g_wait_model_clock_ms = 2000;
    g_wait_model_mode = 1U;
    CHECK(gxos_wait_for_single_object_ex_contract(
              &context, single_manual, 25, 0) == GXOS_WAIT_TIMEOUT);
    CHECK(g_scheduler.boot_thread->state == GXOS_SCHEDULER_THREAD_RUNNING &&
          gxos_scheduler_active_wait_count() == 0 &&
          gxos_scheduler_blocked_count() == 0 &&
          single_event->waiter_count == 0 &&
          single_object->internal_refs == 1 &&
          gxos_scheduler_get_last_error() == 0x1234U);

    /* A finite wait signaled before its deadline returns the object result. */
    g_wait_model_mode = 0U;
    CHECK(gxos_wait_for_single_object_ex_contract(
              &context, single_manual, 50, 0) == GXOS_WAIT_OBJECT_0);
    CHECK(single_event->signaled == 1 && single_event->waiter_count == 0 &&
          gxos_scheduler_active_wait_count() == 0 &&
          single_object->internal_refs == 1);

    CHECK(gxos_scheduler_create_event(&g_scheduler, 0, 1, &single_auto));
    single_event = gxos_scheduler_event_from_handle(single_auto);
    CHECK(gxos_wait_for_single_object_ex_contract(
              &context, single_auto, 0, 0) == GXOS_WAIT_OBJECT_0);
    CHECK(!single_event->signaled);
    CHECK(gxos_wait_for_single_object_ex_contract(
              &context, single_auto, 0, 0) == GXOS_WAIT_TIMEOUT);
    CHECK(gxos_set_event_contract(&context, single_auto));
    CHECK(gxos_wait_for_single_object_contract(
              &context, single_auto, GXOS_INFINITE) == GXOS_WAIT_OBJECT_0);
    CHECK(!single_event->signaled);

    CHECK(gxos_scheduler_create_event(&g_scheduler, 1, 1, &single_wrapper));
    single_event = gxos_scheduler_event_from_handle(single_wrapper);
    CHECK(gxos_wait_for_single_object_contract(
              &context, single_wrapper, 0) == GXOS_WAIT_OBJECT_0);
    CHECK(single_event->signaled);
    CHECK(gxos_reset_event_contract(&context, single_wrapper));
    g_wait_model_clock_ms = UINT64_MAX - 2U;
    g_wait_model_mode = 1U;
    CHECK(gxos_wait_for_single_object_ex_contract(
              &context, single_wrapper, 100, 0) == GXOS_WAIT_TIMEOUT);
    CHECK(g_scheduler.boot_thread->state == GXOS_SCHEDULER_THREAD_RUNNING &&
          gxos_scheduler_active_wait_count() == 0 &&
          single_event->waiter_count == 0);

    /* The Ex flag is defined, but no APC or completion source exists. */
    gxos_scheduler_set_last_error(0x5678U);
    CHECK(gxos_wait_for_single_object_ex_contract(
              &context, single_wrapper, GXOS_INFINITE, 1) == GXOS_WAIT_FAILED);
    CHECK(gxos_scheduler_get_last_error() == GXOS_EVENT_ERROR_INVALID_PARAMETER &&
          single_event->waiter_count == 0 &&
          g_scheduler.boot_thread->state == GXOS_SCHEDULER_THREAD_RUNNING);

    gxos_scheduler_set_last_error(0x9ABCU);
    CHECK(gxos_wait_for_single_object_ex_contract(
              &context, single_thread_handle, 0, 0) == GXOS_WAIT_FAILED);
    CHECK(gxos_scheduler_get_last_error() == GXOS_EVENT_ERROR_INVALID_PARAMETER);
    CHECK(gxos_scheduler_close_handle(single_thread_handle));
    CHECK(gxos_scheduler_discard_created_thread(single_thread));

    CHECK(gxos_scheduler_create_event(&g_scheduler, 1, 0, &single_stale));
    CHECK(gxos_scheduler_close_handle(single_stale));
    CHECK(gxos_scheduler_try_destroy_event(single_stale));
    gxos_scheduler_set_last_error(0x1357U);
    CHECK(gxos_wait_for_single_object_ex_contract(
              &context, single_stale, 0, 0) == GXOS_WAIT_FAILED);
    CHECK(gxos_scheduler_get_last_error() == GXOS_EVENT_ERROR_INVALID_HANDLE);

    CHECK(gxos_scheduler_close_handle(single_manual));
    CHECK(gxos_scheduler_try_destroy_event(single_manual));
    CHECK(gxos_scheduler_close_handle(single_auto));
    CHECK(gxos_scheduler_try_destroy_event(single_auto));
    CHECK(gxos_scheduler_close_handle(single_wrapper));
    CHECK(gxos_scheduler_try_destroy_event(single_wrapper));

    CHECK(gxos_scheduler_teardown(&g_scheduler));
    CHECK(g_failures == 0);
    (void)printf("EVENT_API_TESTS=PASSED checks=%u\n", g_checks);
    return g_failures == 0 ? 0 : 1;
}

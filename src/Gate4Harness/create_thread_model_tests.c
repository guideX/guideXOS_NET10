#include "create_thread.h"

#include <stdio.h>

static unsigned char g_pages[512U * GXOS_SCHEDULER_PAGE_SIZE]
    __attribute__((aligned(GXOS_SCHEDULER_PAGE_SIZE)));
static unsigned char g_page_used[512U];
static GXOS_SCHEDULER g_scheduler;
static unsigned int g_checks;
static unsigned int g_failures;
static uint32_t g_force_allocation_failure;

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
    if (g_force_allocation_failure || memory == 0 || pages == 0 ||
        pages > 512U) return 1;
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
        (memory - base) % GXOS_SCHEDULER_PAGE_SIZE != 0 || pages == 0 ||
        pages > 512U || memory + pages * GXOS_SCHEDULER_PAGE_SIZE > end) {
        return 1;
    }
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

static uint32_t live_object_count(void)
{
    uint32_t index;
    uint32_t count = 0;
    for (index = 0; index != GXOS_SCHEDULER_MAX_OBJECTS; ++index) {
        if (g_scheduler.objects[index].live) ++count;
    }
    return count;
}

static uint32_t live_worker_count(void)
{
    uint32_t index;
    uint32_t count = 0;
    for (index = 1; index != GXOS_SCHEDULER_MAX_THREADS; ++index) {
        if (g_scheduler.threads[index].live) ++count;
    }
    return count;
}

static uint32_t allocated_page_count(void)
{
    uint32_t index;
    uint32_t count = 0;
    for (index = 0; index != 512U; ++index) count += g_page_used[index] != 0;
    return count;
}

static void discard_worker(GXOS_SCHEDULER_HANDLE handle,
                           GXOS_SCHEDULER_TCB *thread)
{
    CHECK(handle != 0);
    CHECK(thread != 0);
    CHECK(gxos_scheduler_close_handle(handle));
    CHECK(gxos_scheduler_discard_created_thread(thread));
}

static GXOS_SCHEDULER_HANDLE create_worker(
    GXOS_CREATE_THREAD_CONTEXT *context,
    GXOS_SCHEDULER_ENTRY entry,
    void *parameter,
    uint64_t flags,
    uint64_t stack_size,
    const void *attributes,
    uintptr_t thread_id,
    GXOS_SCHEDULER_TCB **thread_out)
{
    return gxos_create_thread_contract(
        context, attributes, stack_size, entry, parameter, flags, thread_id,
        thread_out);
}

int main(void)
{
    GXOS_CREATE_THREAD_CONTEXT context;
    GXOS_CREATE_THREAD_EXECUTABLE_REGION executable_region;
    GXOS_SCHEDULER_HANDLE handle;
    GXOS_SCHEDULER_TCB *worker;
    GXOS_SCHEDULER_TCB *boot;
    GXOS_SCHEDULER_HANDLE workers[GXOS_SCHEDULER_MAX_THREADS] = {0};
    GXOS_SCHEDULER_TCB *worker_tcbs[GXOS_SCHEDULER_MAX_THREADS] = {0};
    GXOS_SCHEDULER_HANDLE events[GXOS_SCHEDULER_MAX_EVENTS] = {0};
    uint32_t worker_count;
    uint32_t event_count;
    uint32_t before_objects;
    uint32_t before_workers;
    uint32_t before_pages;
    uint32_t untouched_thread_id = 0xA5A5A5A5U;
    uintptr_t model_start = (uintptr_t)model_entry;
    uintptr_t image_base = model_start & ~((uintptr_t)GXOS_SCHEDULER_PAGE_SIZE - 1U);

    context.scheduler = &g_scheduler;
    context.payload_base = image_base;
    context.payload_size = GXOS_SCHEDULER_PAGE_SIZE;
    executable_region.base = image_base;
    executable_region.end = image_base + GXOS_SCHEDULER_PAGE_SIZE;
    context.executable_regions = &executable_region;
    context.executable_region_count = 1;

    CHECK(gxos_scheduler_initialize(&g_scheduler, model_allocate, model_free,
                                    model_log_text, model_log_hex,
                                    model_log_u32));
    boot = g_scheduler.boot_thread;
    CHECK(boot != 0);
    CHECK(gxos_scheduler_current_thread() == boot);

    handle = create_worker(&context, model_entry, (void *)(uintptr_t)0x12345678,
                           GXOS_CREATE_THREAD_CREATE_SUSPENDED, 0, 0, 0,
                           &worker);
    CHECK(handle != 0);
    CHECK(worker != 0);
    CHECK(gxos_scheduler_thread_from_handle(handle) == worker);
    CHECK(gxos_scheduler_object_from_handle(handle)->type ==
          GXOS_SCHEDULER_OBJECT_THREAD);
    CHECK(gxos_scheduler_object_from_handle(handle)->generation ==
          worker->generation);
    CHECK(worker->state == GXOS_SCHEDULER_THREAD_CREATED_SUSPENDED);
    CHECK(worker->suspend_count == 1);
    CHECK(worker->execution_count == 0);
    CHECK(worker->public_handle_refs == 1);
    CHECK(worker->execution_refs == 1);
    CHECK(worker->entry == model_entry);
    CHECK(worker->entry_argument == (void *)(uintptr_t)0x12345678);
    CHECK(worker->context.r12 == (uint64_t)(uintptr_t)model_entry);
    CHECK(worker->context.r13 == (uint64_t)(uintptr_t)0x12345678);
    CHECK(worker->stack_limit - worker->stack_base ==
          GXOS_SCHEDULER_STACK_SIZE);
    CHECK(worker->stack_base != boot->stack_base);
    CHECK(worker->gs_base != boot->gs_base);
    CHECK(worker->tls_vector_base != boot->tls_vector_base);
    CHECK(worker->tls_block_base != boot->tls_block_base);
    CHECK(&worker->fls_values[0] != &boot->fls_values[0]);
    CHECK(&worker->fls_allocated[0] != &boot->fls_allocated[0]);
    CHECK(&worker->last_error != &boot->last_error);
    CHECK(worker->last_error == 0 && boot->last_error == 0);
    CHECK(worker->initial_rsp >= worker->stack_base &&
          worker->initial_rsp < worker->stack_limit);
    CHECK((worker->initial_rsp & 0xFULL) == 8U);
    CHECK(((worker->initial_rsp - 0x30U) & 0xFULL) == 8U);
    CHECK(worker->initial_rsp - 0x30U + 0x28U <=
          worker->stack_limit - GXOS_SCHEDULER_CANARY_BYTES);
    CHECK(gxos_scheduler_current_thread() == boot);
    CHECK(live_worker_count() == 1);
    CHECK(gxos_scheduler_close_handle(handle));
    CHECK(worker->live && worker->execution_refs == 1);
    CHECK(gxos_scheduler_discard_created_thread(worker));
    CHECK(!worker->live);

    before_objects = live_object_count();
    before_workers = live_worker_count();
    CHECK(create_worker(&context, 0, 0, GXOS_CREATE_THREAD_CREATE_SUSPENDED, 0,
                        0, 0, &worker) == 0);
    CHECK(live_object_count() == before_objects);
    CHECK(live_worker_count() == before_workers);
    CHECK(gxos_scheduler_get_last_error() ==
          GXOS_CREATE_THREAD_ERROR_INVALID_PARAMETER);
    CHECK(create_worker(&context, (GXOS_SCHEDULER_ENTRY)(uintptr_t)0x1234,
                        0, GXOS_CREATE_THREAD_CREATE_SUSPENDED, 0, 0, 0,
                        &worker) == 0);
    CHECK(live_object_count() == before_objects);
    CHECK(create_worker(&context, model_entry, 0,
                        GXOS_CREATE_THREAD_CREATE_SUSPENDED | 1U, 0, 0, 0,
                        &worker) == 0);
    CHECK(create_worker(&context, model_entry, 0,
                        GXOS_CREATE_THREAD_CREATE_SUSPENDED, 1, 0, 0,
                        &worker) == 0);
    CHECK(create_worker(&context, model_entry, 0,
                        GXOS_CREATE_THREAD_CREATE_SUSPENDED, 0,
                        (void *)(uintptr_t)1, 0, &worker) == 0);
    CHECK(create_worker(&context, model_entry, 0,
                        GXOS_CREATE_THREAD_CREATE_SUSPENDED, 0, 0,
                        (uintptr_t)&untouched_thread_id, &worker) == 0);
    CHECK(untouched_thread_id == 0xA5A5A5A5U);

    before_objects = live_object_count();
    before_workers = live_worker_count();
    before_pages = allocated_page_count();
    g_force_allocation_failure = 1;
    CHECK(create_worker(&context, model_entry, 0,
                        GXOS_CREATE_THREAD_CREATE_SUSPENDED, 0, 0, 0,
                        &worker) == 0);
    g_force_allocation_failure = 0;
    CHECK(live_object_count() == before_objects);
    CHECK(live_worker_count() == before_workers);
    CHECK(allocated_page_count() == before_pages);
    CHECK(gxos_scheduler_current_thread() == boot);

    handle = create_worker(&context, model_entry, (void *)(uintptr_t)0xDEADBEEF,
                           GXOS_CREATE_THREAD_CREATE_SUSPENDED, 0, 0, 0,
                           &worker);
    CHECK(handle != 0);
    CHECK(worker->entry_argument == (void *)(uintptr_t)0xDEADBEEF);
    discard_worker(handle, worker);

    worker_count = 0;
    while (worker_count != GXOS_SCHEDULER_MAX_THREADS - 1U) {
        workers[worker_count] = create_worker(
            &context, model_entry, 0, GXOS_CREATE_THREAD_CREATE_SUSPENDED,
            0, 0, 0, &worker_tcbs[worker_count]);
        CHECK(workers[worker_count] != 0);
        ++worker_count;
    }
    before_objects = live_object_count();
    before_workers = live_worker_count();
    CHECK(create_worker(&context, model_entry, 0,
                        GXOS_CREATE_THREAD_CREATE_SUSPENDED, 0, 0, 0,
                        &worker) == 0);
    CHECK(live_object_count() == before_objects);
    CHECK(live_worker_count() == before_workers);
    for (uint32_t index = 0; index != worker_count; ++index) {
        discard_worker(workers[index], worker_tcbs[index]);
    }

    event_count = 0;
    while (event_count != GXOS_SCHEDULER_MAX_EVENTS &&
           gxos_scheduler_create_event(&g_scheduler, 0, 0, &events[event_count])) {
        ++event_count;
    }
    CHECK(event_count == GXOS_SCHEDULER_MAX_EVENTS);
    worker_count = 0;
    while (worker_count != 3U) {
        workers[worker_count] = create_worker(
            &context, model_entry, 0, GXOS_CREATE_THREAD_CREATE_SUSPENDED,
            0, 0, 0, &worker_tcbs[worker_count]);
        CHECK(workers[worker_count] != 0);
        ++worker_count;
    }
    before_objects = live_object_count();
    before_workers = live_worker_count();
    CHECK(create_worker(&context, model_entry, 0,
                        GXOS_CREATE_THREAD_CREATE_SUSPENDED, 0, 0, 0,
                        &worker) == 0);
    CHECK(live_object_count() == before_objects);
    CHECK(live_worker_count() == before_workers);
    for (uint32_t index = 0; index != worker_count; ++index) {
        discard_worker(workers[index], worker_tcbs[index]);
    }
    for (uint32_t index = 0; index != event_count; ++index) {
        CHECK(gxos_scheduler_close_handle(events[index]));
        CHECK(gxos_scheduler_try_destroy_event(events[index]));
    }

    handle = create_worker(&context, model_entry, 0,
                           GXOS_CREATE_THREAD_CREATE_SUSPENDED, 0, 0, 0,
                           &worker);
    CHECK(handle != 0);
    CHECK(gxos_scheduler_close_handle(handle));
    CHECK(gxos_scheduler_discard_created_thread(worker));
    CHECK(gxos_scheduler_teardown(&g_scheduler));
    CHECK(g_failures == 0);
    (void)printf("CREATE_THREAD_MODEL_TESTS=%s checks=%u\n",
                 g_failures == 0 ? "PASSED" : "FAILED", g_checks);
    return g_failures == 0 ? 0 : 1;
}

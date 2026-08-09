#include "platform_is_process_in_job.h"
#include "scheduler_foundation.h"

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

static void configure_memory(GXOS_SYSTEM_INFO_MEMORY_CONTEXT *context,
                             GXOS_SYSTEM_INFO_MEMORY_REGION *region,
                             void *base, size_t size, uint32_t writable)
{
    region->base = (uintptr_t)base;
    region->end = region->base + size;
    region->readable = 1;
    region->writable = writable;
    context->region_count = 1;
    context->regions = region;
}

static GXOS_IS_PROCESS_IN_JOB_STATUS call_checked(
    uintptr_t process_handle, uintptr_t job_handle, int32_t *result,
    const GXOS_IS_PROCESS_IN_JOB_FACTS *facts,
    const GXOS_SYSTEM_INFO_MEMORY_CONTEXT *memory,
    GXOS_IS_PROCESS_IN_JOB_REPORT *report)
{
    return gxos_is_process_in_job_checked(
        process_handle, job_handle, result, facts, memory, report);
}

static int unchanged_bytes(const unsigned char *before,
                           const unsigned char *after, size_t size)
{
    return memcmp(before, after, size) == 0;
}

int main(void)
{
    static unsigned char result_storage[16];
    static unsigned char nonwritable_storage[16];
    GXOS_SYSTEM_INFO_MEMORY_REGION region;
    GXOS_SYSTEM_INFO_MEMORY_CONTEXT memory;
    GXOS_IS_PROCESS_IN_JOB_FACTS facts;
    GXOS_IS_PROCESS_IN_JOB_REPORT report;
    GXOS_SCHEDULER scheduler_before;
    GXOS_SCHEDULER_OBJECT objects_before[GXOS_SCHEDULER_MAX_OBJECTS];
    GXOS_SCHEDULER_HANDLE event_one = 0;
    GXOS_SCHEDULER_HANDLE event_two = 0;
    GXOS_SCHEDULER_HANDLE notification = 0;
    GXOS_SCHEDULER_HANDLE worker_handle = 0;
    GXOS_SCHEDULER_TCB *worker = 0;
    unsigned char bytes_before[sizeof(result_storage)];
    int32_t *result = (int32_t *)(void *)(result_storage + 4);

    (void)memset(g_page_used, 0, sizeof(g_page_used));
    CHECK(gxos_scheduler_initialize(
        &g_scheduler, model_allocate, model_free, model_log_text,
        model_log_hex, model_log_u32));
    CHECK(gxos_scheduler_create_event(&g_scheduler, 1, 0, &event_one));
    CHECK(gxos_scheduler_create_event(&g_scheduler, 0, 0, &event_two));
    CHECK(gxos_scheduler_create_memory_resource_notification(
        &g_scheduler, 0, &notification));
    CHECK(gxos_scheduler_create_suspended_thread(
        &g_scheduler, model_entry, 0, &worker_handle, &worker));
    CHECK(worker != 0);
    worker->relative_priority = GXOS_SCHEDULER_SUPPORTED_RELATIVE_PRIORITY;
    CHECK(gxos_scheduler_resume_thread(worker_handle, 0) == 1);
    CHECK(worker->state == GXOS_SCHEDULER_THREAD_RUNNABLE &&
          worker->suspend_count == 0 && worker->execution_count == 0 &&
          worker->runnable_queued != 0);
    facts.current_process_handle = GXOS_IS_PROCESS_IN_JOB_CURRENT_PROCESS;
    configure_memory(&memory, &region, result_storage, sizeof(result_storage), 1);

    (void)memset(result_storage, 0xA5, sizeof(result_storage));
    *(uint32_t *)(void *)result = 0xAABBCCDDU;
    (void)memcpy(bytes_before, result_storage, sizeof(result_storage));
    CHECK(gxos_is_process_in_job_abi_probe(
              GXOS_IS_PROCESS_IN_JOB_CURRENT_PROCESS,
              GXOS_IS_PROCESS_IN_JOB_NULL_JOB, result, &facts, &memory,
              &report) == GXOS_IS_PROCESS_IN_JOB_TRUE);
    CHECK(report.process_handle_valid == 1 && report.job_handle_valid == 1);
    CHECK(report.result_pointer_canonical == 1 &&
          report.result_pointer_writable == 1 && report.result_range_valid == 1);
    CHECK(report.result_value_before == 0xAABBCCDDU &&
          report.result_value_after == 0 && *result == 0);
    CHECK(report.result_written == 1 && report.result_bytes_written == 4);
    CHECK(result_storage[0] == bytes_before[0] &&
          result_storage[1] == bytes_before[1] &&
          result_storage[2] == bytes_before[2] &&
          result_storage[3] == bytes_before[3] &&
          unchanged_bytes(result_storage + 8, bytes_before + 8, 8));

    (void)memcpy(bytes_before, result_storage, sizeof(result_storage));
    CHECK(call_checked(0, 0, result, &facts, &memory, &report) ==
          GXOS_IS_PROCESS_IN_JOB_STATUS_INVALID_PROCESS_HANDLE);
    CHECK(unchanged_bytes(bytes_before, result_storage, sizeof(result_storage)));
    CHECK(call_checked((uintptr_t)0x1234, 0, result, &facts, &memory, &report) ==
          GXOS_IS_PROCESS_IN_JOB_STATUS_INVALID_PROCESS_HANDLE);
    CHECK(unchanged_bytes(bytes_before, result_storage, sizeof(result_storage)));
    CHECK(call_checked((uintptr_t)worker_handle, 0, result, &facts, &memory,
                       &report) ==
          GXOS_IS_PROCESS_IN_JOB_STATUS_INVALID_PROCESS_HANDLE);
    CHECK(call_checked((uintptr_t)event_one, 0, result, &facts, &memory,
                       &report) ==
          GXOS_IS_PROCESS_IN_JOB_STATUS_INVALID_PROCESS_HANDLE);
    CHECK(call_checked((uintptr_t)notification, 0, result, &facts, &memory,
                       &report) ==
          GXOS_IS_PROCESS_IN_JOB_STATUS_INVALID_PROCESS_HANDLE);
    CHECK(call_checked(GXOS_IS_PROCESS_IN_JOB_CURRENT_PROCESS,
                       (uintptr_t)0x1234, result, &facts, &memory, &report) ==
          GXOS_IS_PROCESS_IN_JOB_STATUS_NON_NULL_JOB_HANDLE);
    CHECK(call_checked(GXOS_IS_PROCESS_IN_JOB_CURRENT_PROCESS, 0, 0, &facts,
                       &memory, &report) ==
          GXOS_IS_PROCESS_IN_JOB_STATUS_NULL_RESULT);

    (void)memset(nonwritable_storage, 0x5A, sizeof(nonwritable_storage));
    configure_memory(&memory, &region, nonwritable_storage,
                     sizeof(nonwritable_storage), 0);
    (void)memcpy(bytes_before, nonwritable_storage, sizeof(nonwritable_storage));
    CHECK(call_checked(GXOS_IS_PROCESS_IN_JOB_CURRENT_PROCESS, 0,
                       (int32_t *)(void *)(nonwritable_storage + 4), &facts,
                       &memory, &report) ==
          GXOS_IS_PROCESS_IN_JOB_STATUS_UNWRITABLE_RESULT);
    CHECK(unchanged_bytes(bytes_before, nonwritable_storage,
                          sizeof(nonwritable_storage)));

    configure_memory(&memory, &region, result_storage, 6, 1);
    (void)memset(result_storage, 0xC3, sizeof(result_storage));
    (void)memcpy(bytes_before, result_storage, sizeof(result_storage));
    CHECK(call_checked(GXOS_IS_PROCESS_IN_JOB_CURRENT_PROCESS, 0,
                       (int32_t *)(void *)(result_storage + 4), &facts,
                       &memory, &report) ==
          GXOS_IS_PROCESS_IN_JOB_STATUS_UNWRITABLE_RESULT);
    CHECK(unchanged_bytes(bytes_before, result_storage, sizeof(result_storage)));

    configure_memory(&memory, &region, result_storage, sizeof(result_storage), 1);
    CHECK(call_checked(GXOS_IS_PROCESS_IN_JOB_CURRENT_PROCESS, 0,
                       (int32_t *)(uintptr_t)(UINTPTR_MAX - 1U), &facts,
                       &memory, &report) ==
          GXOS_IS_PROCESS_IN_JOB_STATUS_RANGE_OVERFLOW);
    CHECK(unchanged_bytes(bytes_before, result_storage, sizeof(result_storage)));

    (void)memcpy(&scheduler_before, &g_scheduler, sizeof(g_scheduler));
    (void)memcpy(objects_before, g_scheduler.objects, sizeof(objects_before));
    (void)memset(result_storage, 0xD7, sizeof(result_storage));
    (void)memcpy(bytes_before, result_storage, sizeof(result_storage));
    CHECK(call_checked((uintptr_t)worker_handle, (uintptr_t)event_two,
                       result, &facts, &memory, &report) ==
          GXOS_IS_PROCESS_IN_JOB_STATUS_INVALID_PROCESS_HANDLE);
    CHECK(unchanged_bytes(bytes_before, result_storage, sizeof(result_storage)));
    CHECK(memcmp(&scheduler_before, &g_scheduler, sizeof(g_scheduler)) == 0);
    CHECK(memcmp(objects_before, g_scheduler.objects, sizeof(objects_before)) == 0);
    CHECK(g_scheduler.boot_thread->state == GXOS_SCHEDULER_THREAD_RUNNING &&
          worker->state == GXOS_SCHEDULER_THREAD_RUNNABLE &&
          worker->suspend_count == 0 && worker->relative_priority == 2 &&
          worker->execution_count == 0 &&
          gxos_scheduler_runnable_count() == 1);

    (void)printf("ISPROCESSINJOB_MODEL_CHECKS=%u\n", g_checks);
    (void)printf("ISPROCESSINJOB_MODEL_FAILURES=%u\n", g_failures);
    return g_failures == 0 ? 0 : 1;
}

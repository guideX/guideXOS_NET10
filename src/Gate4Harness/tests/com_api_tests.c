#include "scheduler_foundation.h"
#include "com_api.h"

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

static uint64_t GXOS_SCHEDULER_MS_ABI test_allocate(
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

static uint64_t GXOS_SCHEDULER_MS_ABI test_free(
    uint64_t memory, uint64_t pages)
{
    uint64_t base = (uint64_t)(uintptr_t)g_pages;
    uint64_t end = base + sizeof(g_pages);
    uint32_t index;
    if (memory < base || memory >= end ||
        (memory - base) % GXOS_SCHEDULER_PAGE_SIZE != 0 || pages == 0 ||
        pages > 256U || memory + pages * GXOS_SCHEDULER_PAGE_SIZE > end) {
        return 1;
    }
    index = (uint32_t)((memory - base) / GXOS_SCHEDULER_PAGE_SIZE);
    while (pages-- != 0) g_page_used[index++] = 0;
    return 0;
}

static void GXOS_SCHEDULER_MS_ABI test_log_text(const char *text)
{
    (void)text;
}

static void GXOS_SCHEDULER_MS_ABI test_log_hex(const char *name,
                                                uint64_t value)
{
    (void)name;
    (void)value;
}

static void GXOS_SCHEDULER_MS_ABI test_log_u32(const char *name,
                                                uint32_t value)
{
    (void)name;
    (void)value;
}

static uintptr_t GXOS_SCHEDULER_MS_ABI test_entry(void *argument)
{
    return (uintptr_t)argument;
}

/* The host test exercises scheduler metadata transitions without performing
   an assembly context switch. */
void gxos_scheduler_start_worker(void)
{
}

void gxos_scheduler_invalid_thread_return(void)
{
}

int main(void)
{
    GXOS_SCHEDULER_TCB *main_thread;
    GXOS_SCHEDULER_TCB *worker;
    GXOS_SCHEDULER_TCB *recreated;
    GXOS_SCHEDULER_HANDLE worker_handle;
    GXOS_SCHEDULER_HANDLE recreated_handle;
    GXOS_SCHEDULER_SWITCH_PLAN plan;

    CHECK(gxos_scheduler_initialize(&g_scheduler, test_allocate, test_free,
                                    test_log_text, test_log_hex, test_log_u32));
    main_thread = gxos_scheduler_current_thread();
    CHECK(main_thread == g_scheduler.boot_thread);
    CHECK(gxos_com_is_initialized(main_thread) == 0U);
    CHECK(gxos_com_model(main_thread) == GXOS_COM_MODEL_NONE);
    CHECK(gxos_com_nesting_count(main_thread) == 0U);
    CHECK(gxos_com_state_generation(main_thread) == 0U);

    CHECK(gxos_com_initialize_ex(0, GXOS_COM_COINIT_MULTITHREADED) ==
          GXOS_COM_S_OK);
    CHECK(gxos_com_is_initialized(main_thread) == 1U);
    CHECK(gxos_com_model(main_thread) == GXOS_COM_MODEL_MTA);
    CHECK(gxos_com_ancillary_flags(main_thread) == 0U);
    CHECK(gxos_com_nesting_count(main_thread) == 1U);
    CHECK(gxos_com_state_generation(main_thread) == main_thread->generation);
    CHECK(gxos_com_initialize_ex(0, GXOS_COM_COINIT_MULTITHREADED) ==
          GXOS_COM_S_FALSE);
    CHECK(gxos_com_nesting_count(main_thread) == 2U);
    CHECK(gxos_com_initialize_ex(0, GXOS_COM_COINIT_DISABLE_OLE1DDE) ==
          GXOS_COM_S_FALSE);
    CHECK(gxos_com_nesting_count(main_thread) == 3U);
    CHECK(gxos_com_ancillary_flags(main_thread) == 0U);
    CHECK(gxos_com_initialize_ex(0, GXOS_COM_COINIT_APARTMENTTHREADED) ==
          GXOS_COM_RPC_E_CHANGED_MODE);
    CHECK(gxos_com_nesting_count(main_thread) == 3U);
    CHECK(gxos_com_initialize_ex((void *)(uintptr_t)1, 0) ==
          GXOS_COM_E_INVALIDARG);
    CHECK(gxos_com_nesting_count(main_thread) == 3U);
    CHECK(gxos_com_initialize_ex(0, 0x10U) == GXOS_COM_E_INVALIDARG);
    CHECK(gxos_com_nesting_count(main_thread) == 3U);
    CHECK(gxos_com_initialize_ex(0, 0x1U) == GXOS_COM_E_INVALIDARG);
    CHECK(gxos_com_nesting_count(main_thread) == 3U);
    gxos_com_uninitialize();
    CHECK(gxos_com_is_initialized(main_thread) == 1U);
    CHECK(gxos_com_nesting_count(main_thread) == 2U);
    gxos_com_uninitialize();
    gxos_com_uninitialize();
    CHECK(gxos_com_is_initialized(main_thread) == 0U);
    CHECK(gxos_com_model(main_thread) == GXOS_COM_MODEL_NONE);
    CHECK(gxos_com_nesting_count(main_thread) == 0U);
    gxos_com_uninitialize();
    CHECK(gxos_com_nesting_count(main_thread) == 0U);
    CHECK(gxos_com_initialize_ex(0, GXOS_COM_COINIT_APARTMENTTHREADED) ==
          GXOS_COM_E_NOTIMPL);
    CHECK(gxos_com_is_initialized(main_thread) == 0U);

    CHECK(gxos_com_initialize_ex(0, GXOS_COM_COINIT_DISABLE_OLE1DDE |
                                    GXOS_COM_COINIT_SPEED_OVER_MEMORY) ==
          GXOS_COM_S_OK);
    CHECK(gxos_com_model(main_thread) == GXOS_COM_MODEL_MTA);
    CHECK(gxos_com_ancillary_flags(main_thread) ==
          (GXOS_COM_COINIT_DISABLE_OLE1DDE |
           GXOS_COM_COINIT_SPEED_OVER_MEMORY));
    CHECK(gxos_com_initialize_ex(0, GXOS_COM_COINIT_DISABLE_OLE1DDE) ==
          GXOS_COM_S_FALSE);
    CHECK(gxos_com_nesting_count(main_thread) == 2U);
    CHECK(gxos_com_ancillary_flags(main_thread) ==
          (GXOS_COM_COINIT_DISABLE_OLE1DDE |
           GXOS_COM_COINIT_SPEED_OVER_MEMORY));
    gxos_com_uninitialize();
    gxos_com_uninitialize();
    CHECK(gxos_com_is_initialized(main_thread) == 0U);

    CHECK(gxos_scheduler_create_suspended_thread(
        &g_scheduler, test_entry, (void *)(uintptr_t)0x55,
        &worker_handle, &worker));
    CHECK(worker->com_initialized == 0U);
    CHECK(worker->com_initialization_count == 0U);
    CHECK(gxos_com_is_initialized(worker) == 0U);
    CHECK(gxos_com_model(worker) == GXOS_COM_MODEL_NONE);
    CHECK(gxos_com_state_generation(worker) == 0U);
    CHECK(gxos_scheduler_resume_thread(worker_handle, 0));
    CHECK(gxos_scheduler_prepare_yield(&plan));
    CHECK(gxos_scheduler_current_thread() == worker);
    CHECK(gxos_com_is_initialized(main_thread) == 0U);
    CHECK(gxos_com_initialize_ex(0, GXOS_COM_COINIT_MULTITHREADED) ==
          GXOS_COM_S_OK);
    CHECK(gxos_com_is_initialized(worker) == 1U);
    CHECK(gxos_com_model(worker) == GXOS_COM_MODEL_MTA);
    CHECK(gxos_com_nesting_count(worker) == 1U);
    CHECK(gxos_com_initialize_ex(0, GXOS_COM_COINIT_APARTMENTTHREADED) ==
          GXOS_COM_RPC_E_CHANGED_MODE);
    CHECK(gxos_com_nesting_count(worker) == 1U);
    CHECK(gxos_scheduler_prepare_yield(&plan));
    CHECK(gxos_scheduler_current_thread() == main_thread);
    CHECK(gxos_com_is_initialized(main_thread) == 0U);
    CHECK(gxos_com_is_initialized(worker) == 1U);
    CHECK(gxos_com_initialize_ex(0, GXOS_COM_COINIT_MULTITHREADED) ==
          GXOS_COM_S_OK);
    CHECK(gxos_com_nesting_count(main_thread) == 1U);
    CHECK(gxos_scheduler_prepare_yield(&plan));
    CHECK(gxos_scheduler_current_thread() == worker);
    CHECK(gxos_com_nesting_count(worker) == 1U);
    CHECK(gxos_com_is_initialized(worker) == 1U);
    CHECK(gxos_scheduler_prepare_yield(&plan));
    CHECK(gxos_scheduler_current_thread() == main_thread);
    CHECK(gxos_com_is_initialized(main_thread) == 1U);
    CHECK(gxos_com_nesting_count(main_thread) == 1U);

    CHECK(gxos_scheduler_prepare_yield(&plan));
    CHECK(gxos_scheduler_current_thread() == worker);
    CHECK(gxos_scheduler_prepare_terminate(0, &plan));
    CHECK(gxos_scheduler_current_thread() == main_thread);
    CHECK(gxos_scheduler_thread_is_terminated(worker));
    CHECK(gxos_scheduler_close_handle(worker_handle));
    CHECK(worker->live == 0U);
    CHECK(gxos_com_is_initialized(worker) == 0U);
    CHECK(gxos_com_nesting_count(worker) == 0U);
    CHECK(gxos_scheduler_thread_from_handle(worker_handle) == 0);

    CHECK(gxos_scheduler_create_suspended_thread(
        &g_scheduler, test_entry, 0, &recreated_handle, &recreated));
    CHECK(recreated == worker);
    CHECK(recreated->generation != 0U);
    CHECK(gxos_com_is_initialized(recreated) == 0U);
    CHECK(gxos_com_model(recreated) == GXOS_COM_MODEL_NONE);
    CHECK(gxos_com_nesting_count(recreated) == 0U);
    CHECK(gxos_com_state_generation(recreated) == 0U);
    CHECK(gxos_scheduler_close_handle(recreated_handle));
    CHECK(gxos_scheduler_discard_created_thread(recreated));

    gxos_com_uninitialize();
    CHECK(gxos_com_is_initialized(main_thread) == 0U);
    CHECK(gxos_scheduler_teardown(&g_scheduler));
    CHECK(g_failures == 0);
    (void)printf("COM_API_TESTS=PASSED checks=%u\n", g_checks);
    return g_failures == 0 ? 0 : 1;
}

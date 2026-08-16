#include "scheduler_foundation.h"
#include "com_api.h"
#include "vm_substrate.h"

#include <stdio.h>
#include <string.h>

#define TEST_PAGE_COUNT 256U
#define TEST_FLS_SLOT GXOS_SCHEDULER_FLS_PROOF_SLOT

static unsigned char g_pages[TEST_PAGE_COUNT * GXOS_SCHEDULER_PAGE_SIZE]
    __attribute__((aligned(GXOS_SCHEDULER_PAGE_SIZE)));
static unsigned char g_page_used[TEST_PAGE_COUNT];
static unsigned g_page_allocations;
static unsigned g_fail_after = UINT32_MAX;
static unsigned g_failures;
static GXOS_SCHEDULER g_scheduler;
static GXOS_VM_REGION_LEDGER g_regions;

#define REQUIRE(condition) do { \
    if (!(condition)) { \
        fprintf(stderr, "scheduler durability test failure: %s:%d: %s\n", \
                __FILE__, __LINE__, #condition); \
        ++g_failures; \
    } \
} while (0)

static uint64_t GXOS_SCHEDULER_MS_ABI test_allocate(
    uint32_t type, uint32_t memory_type, uint64_t pages, uint64_t *memory)
{
    uint32_t index;
    uint32_t run;
    (void)type;
    (void)memory_type;
    if (memory == 0 || pages == 0 || pages > TEST_PAGE_COUNT ||
        g_page_allocations >= g_fail_after) return 1;
    for (index = 0; index + pages <= TEST_PAGE_COUNT; ++index) {
        for (run = 0; run != pages && g_page_used[index + run] == 0; ++run) {
        }
        if (run == pages) {
            for (run = 0; run != pages; ++run) {
                g_page_used[index + run] = 1;
            }
            *memory = (uint64_t)(uintptr_t)(g_pages +
                index * GXOS_SCHEDULER_PAGE_SIZE);
            ++g_page_allocations;
            return 0;
        }
    }
    return 1;
}

static uint64_t GXOS_SCHEDULER_MS_ABI test_free(
    uint64_t memory, uint64_t pages)
{
    uintptr_t base = (uintptr_t)g_pages;
    uintptr_t value = (uintptr_t)memory;
    uint32_t index;
    if (memory == 0 || pages == 0 || value < base ||
        value >= base + sizeof(g_pages) ||
        (value - base) % GXOS_SCHEDULER_PAGE_SIZE != 0 ||
        pages > TEST_PAGE_COUNT - (uint32_t)((value - base) /
                                             GXOS_SCHEDULER_PAGE_SIZE)) {
        return 1;
    }
    index = (uint32_t)((value - base) / GXOS_SCHEDULER_PAGE_SIZE);
    while (pages-- != 0) {
        if (g_page_used[index] == 0) return 1;
        g_page_used[index++] = 0;
    }
    return 0;
}

static int GXOS_SCHEDULER_MS_ABI register_stack(
    void *context, uint64_t base, uint64_t bytes,
    uint64_t *allocation_identity_out)
{
    GXOS_VM_STATUS status = gxos_vm_region_register(
        (GXOS_VM_REGION_LEDGER *)context, base, bytes, base,
        GXOS_VM_REGION_PAGE_READWRITE, GXOS_VM_REGION_STATE_COMMIT,
        GXOS_VM_REGION_PAGE_READWRITE, GXOS_VM_REGION_TYPE_PRIVATE,
        allocation_identity_out);
    return status == GXOS_VM_STATUS_OK;
}

static int GXOS_SCHEDULER_MS_ABI unregister_stack(
    void *context, uint64_t base, uint64_t bytes, uint64_t identity)
{
    return gxos_vm_region_unregister(
               (GXOS_VM_REGION_LEDGER *)context, base, bytes, identity) ==
        GXOS_VM_STATUS_OK;
}

static uintptr_t GXOS_SCHEDULER_MS_ABI test_entry(void *argument)
{
    return (uintptr_t)argument;
}

void gxos_scheduler_start_worker(void) {}
void gxos_scheduler_invalid_thread_return(void) {}

static void require_fresh_thread(const GXOS_SCHEDULER_TCB *thread)
{
    uint32_t slot;
    REQUIRE(thread != 0 && thread->live);
    REQUIRE(gxos_com_is_initialized(thread) == 0U);
    REQUIRE(gxos_com_model(thread) == GXOS_COM_MODEL_NONE);
    REQUIRE(gxos_com_nesting_count(thread) == 0U);
    for (slot = 0; slot != GXOS_SCHEDULER_FLS_SLOTS; ++slot) {
        REQUIRE(thread->fls_allocated[slot] == 0U);
        REQUIRE(thread->fls_values[slot] == 0U);
    }
}

static void require_cleared_thread(const GXOS_SCHEDULER_TCB *thread)
{
    uint32_t slot;
    REQUIRE(thread != 0 && thread->live == 0U);
    REQUIRE(thread->com_initialized == 0U);
    REQUIRE(thread->com_model == GXOS_COM_MODEL_NONE);
    REQUIRE(thread->com_initialization_count == 0U);
    for (slot = 0; slot != GXOS_SCHEDULER_FLS_SLOTS; ++slot) {
        REQUIRE(thread->fls_allocated[slot] == 0U);
        REQUIRE(thread->fls_values[slot] == 0U);
    }
}

static void require_stack(const GXOS_SCHEDULER_TCB *thread)
{
    GXOS_VM_MEMORY_BASIC_INFORMATION information;
    REQUIRE(thread != 0 && thread->live);
    REQUIRE(gxos_vm_region_virtual_query(
                &g_regions, thread->stack_base, &information,
                sizeof(information)) == sizeof(information));
    REQUIRE(information.BaseAddress == thread->stack_base);
    REQUIRE(information.AllocationBase == thread->stack_base);
    REQUIRE(information.RegionSize == GXOS_SCHEDULER_STACK_SIZE);
    REQUIRE(information.State == GXOS_VM_REGION_STATE_COMMIT);
    REQUIRE(information.Type == GXOS_VM_REGION_TYPE_PRIVATE);
}

static void require_current_fls(GXOS_SCHEDULER_TCB *thread,
                                uintptr_t expected)
{
    REQUIRE(gxos_scheduler_current_thread() == thread);
    REQUIRE(gxos_scheduler_get_fls(TEST_FLS_SLOT) == expected);
}

int main(void)
{
    GXOS_SCHEDULER_TCB *main_thread;
    GXOS_SCHEDULER_TCB *first;
    GXOS_SCHEDULER_TCB *second;
    GXOS_SCHEDULER_TCB *recycled = 0;
    GXOS_SCHEDULER_TCB *failed = 0;
    GXOS_SCHEDULER_HANDLE first_handle = 0;
    GXOS_SCHEDULER_HANDLE second_handle = 0;
    GXOS_SCHEDULER_HANDLE recycled_handle = 0;
    GXOS_SCHEDULER_HANDLE failed_handle = 0;
    GXOS_SCHEDULER_HANDLE event_handle = 0;
    GXOS_SCHEDULER_SWITCH_PLAN plan;
    GXOS_VM_MEMORY_BASIC_INFORMATION information;
    uint32_t first_identity;
    uint16_t first_generation;
    uint64_t first_stack_base;
    uint64_t first_stack_identity;
    uint32_t previous_suspend_count;
    uintptr_t main_fls = (uintptr_t)0x11111111U;
    uintptr_t first_fls = (uintptr_t)0x22222222U;
    uintptr_t second_fls = (uintptr_t)0x33333333U;
    unsigned round;

    memset(g_page_used, 0, sizeof(g_page_used));
    g_page_allocations = 0;
    gxos_vm_region_ledger_init(&g_regions);
    REQUIRE(gxos_scheduler_initialize(
                &g_scheduler, test_allocate, test_free, 0, 0, 0));
    REQUIRE(gxos_scheduler_configure_stack_vm(
                &g_scheduler, register_stack, unregister_stack, &g_regions));
    main_thread = gxos_scheduler_current_thread();
    require_fresh_thread(main_thread);
    REQUIRE(gxos_scheduler_runnable_count() == 0U);
    REQUIRE(gxos_scheduler_blocked_count() == 0U);

    REQUIRE(gxos_scheduler_create_suspended_thread(
                &g_scheduler, test_entry, 0, &first_handle, &first));
    REQUIRE(gxos_scheduler_create_suspended_thread(
                &g_scheduler, test_entry, 0, &second_handle, &second));
    require_fresh_thread(first);
    require_fresh_thread(second);
    require_stack(first);
    require_stack(second);
    REQUIRE(first->stack_vm_identity != second->stack_vm_identity);
    REQUIRE(g_regions.live_count == 2U);
    first_identity = first->identity;
    first_generation = first->generation;
    first_stack_base = first->stack_base;
    first_stack_identity = first->stack_vm_identity;

    REQUIRE(gxos_scheduler_resume_thread(first_handle,
                                         &previous_suspend_count));
    REQUIRE(previous_suspend_count == 1U);
    REQUIRE(gxos_scheduler_resume_thread(second_handle,
                                         &previous_suspend_count));
    REQUIRE(previous_suspend_count == 1U);
    gxos_scheduler_set_fls(TEST_FLS_SLOT, main_fls);

    /* Round-robin through all three logical scheduler identities repeatedly. */
    for (round = 0; round != 18U; ++round) {
        GXOS_SCHEDULER_TCB *current = gxos_scheduler_current_thread();
        if (current == main_thread) {
            require_current_fls(main_thread, main_fls);
        } else if (current == first) {
            if (round == 1U) gxos_scheduler_set_fls(TEST_FLS_SLOT, first_fls);
            require_current_fls(first, first_fls);
        } else if (current == second) {
            if (round == 2U) gxos_scheduler_set_fls(TEST_FLS_SLOT, second_fls);
            require_current_fls(second, second_fls);
        } else {
            REQUIRE(0);
        }
        REQUIRE(gxos_scheduler_prepare_yield(&plan));
        REQUIRE(gxos_scheduler_blocked_count() == 0U);
        REQUIRE(gxos_scheduler_active_wait_count() == 0U);
        REQUIRE(gxos_scheduler_get_fls(TEST_FLS_SLOT) == 0U ||
                gxos_scheduler_current_thread() == main_thread ||
                gxos_scheduler_current_thread() == first ||
                gxos_scheduler_current_thread() == second);
    }
    REQUIRE(first->fls_values[TEST_FLS_SLOT] == (uint64_t)first_fls);
    REQUIRE(second->fls_values[TEST_FLS_SLOT] == (uint64_t)second_fls);
    REQUIRE(main_thread->fls_values[TEST_FLS_SLOT] == (uint64_t)main_fls);

    /* COM state is local to the selected scheduler thread. */
    while (gxos_scheduler_current_thread() != main_thread) {
        REQUIRE(gxos_scheduler_prepare_yield(&plan));
    }
    REQUIRE(gxos_com_initialize_ex(0, GXOS_COM_COINIT_MULTITHREADED) ==
            GXOS_COM_S_OK);
    REQUIRE(gxos_com_is_initialized(main_thread) == 1U);
    gxos_com_uninitialize();
    REQUIRE(gxos_scheduler_prepare_yield(&plan));
    REQUIRE(gxos_scheduler_current_thread() == first);
    REQUIRE(gxos_com_initialize_ex(0, GXOS_COM_COINIT_MULTITHREADED) ==
            GXOS_COM_S_OK);
    REQUIRE(gxos_scheduler_prepare_yield(&plan));
    REQUIRE(gxos_scheduler_current_thread() == second);
    REQUIRE(gxos_com_initialize_ex(0, GXOS_COM_COINIT_MULTITHREADED) ==
            GXOS_COM_S_OK);
    REQUIRE(gxos_com_is_initialized(first) == 1U);
    REQUIRE(gxos_com_is_initialized(second) == 1U);
    REQUIRE(gxos_com_is_initialized(main_thread) == 0U);
    REQUIRE(gxos_scheduler_prepare_yield(&plan));
    REQUIRE(gxos_scheduler_current_thread() == main_thread);
    REQUIRE(gxos_com_is_initialized(main_thread) == 0U);
    REQUIRE(gxos_com_is_initialized(first) == 1U);
    REQUIRE(gxos_com_is_initialized(second) == 1U);
    REQUIRE(gxos_scheduler_prepare_yield(&plan));
    REQUIRE(gxos_scheduler_current_thread() == first);
    gxos_com_uninitialize();
    REQUIRE(gxos_scheduler_prepare_yield(&plan));
    REQUIRE(gxos_scheduler_current_thread() == second);
    gxos_com_uninitialize();
    REQUIRE(gxos_com_is_initialized(first) == 0U);
    REQUIRE(gxos_com_is_initialized(second) == 0U);
    REQUIRE(gxos_scheduler_prepare_yield(&plan));
    REQUIRE(gxos_scheduler_current_thread() == main_thread);

    /* A blocked worker has one stable wait record and does not disturb peers. */
    REQUIRE(gxos_scheduler_create_event(&g_scheduler, 0, 0, &event_handle));
    REQUIRE(gxos_scheduler_prepare_yield(&plan));
    REQUIRE(gxos_scheduler_current_thread() == first);
    REQUIRE(gxos_scheduler_prepare_wait(event_handle, &plan) ==
            GXOS_SCHEDULER_WAIT_BLOCKED);
    REQUIRE(first->state == GXOS_SCHEDULER_THREAD_BLOCKED);
    REQUIRE(first->wait_record != 0 && first->wait_record->valid &&
            first->wait_record->active && first->wait_record->waiter_linked);
    REQUIRE(g_scheduler.events[0].waiter_count == 1U);
    REQUIRE(gxos_scheduler_active_wait_count() == 1U);
    REQUIRE(gxos_scheduler_current_thread() == second);
    REQUIRE(gxos_scheduler_signal_event(event_handle));
    REQUIRE(g_scheduler.events[0].waiter_count == 0U);
    REQUIRE(first->state == GXOS_SCHEDULER_THREAD_RUNNABLE);
    REQUIRE(gxos_scheduler_active_wait_count() == 1U);
    REQUIRE(gxos_scheduler_prepare_yield(&plan));
    REQUIRE(gxos_scheduler_current_thread() == main_thread);
    REQUIRE(gxos_scheduler_prepare_yield(&plan));
    REQUIRE(gxos_scheduler_current_thread() == first);
    REQUIRE(gxos_scheduler_finish_wait(event_handle) ==
            GXOS_SCHEDULER_WAIT_SIGNALED);
    REQUIRE(first->wait_record == 0 && first->blocked_object == 0 &&
            gxos_scheduler_active_wait_count() == 0U);
    REQUIRE(first->fls_values[TEST_FLS_SLOT] == (uint64_t)first_fls);
    REQUIRE(gxos_scheduler_close_handle(event_handle));
    REQUIRE(gxos_scheduler_try_destroy_event(event_handle));
    REQUIRE(gxos_scheduler_event_from_handle(event_handle) == 0);

    /* Termination clears COM/FLS state and releases stack VM registrations. */
    REQUIRE(gxos_scheduler_prepare_yield(&plan));
    REQUIRE(gxos_scheduler_current_thread() == second);
    REQUIRE(gxos_scheduler_prepare_terminate(0, &plan));
    REQUIRE(gxos_scheduler_current_thread() == main_thread);
    REQUIRE(gxos_scheduler_close_handle(second_handle));
    REQUIRE(gxos_scheduler_collect(&g_scheduler));
    REQUIRE(second->live == 0U);
    require_cleared_thread(second);
    REQUIRE(gxos_scheduler_thread_from_handle(second_handle) == 0);

    REQUIRE(gxos_scheduler_prepare_yield(&plan));
    REQUIRE(gxos_scheduler_current_thread() == first);
    REQUIRE(gxos_scheduler_prepare_terminate(0, &plan));
    REQUIRE(gxos_scheduler_current_thread() == main_thread);
    REQUIRE(gxos_scheduler_close_handle(first_handle));
    REQUIRE(gxos_scheduler_collect(&g_scheduler));
    REQUIRE(first->live == 0U);
    require_cleared_thread(first);
    REQUIRE(gxos_vm_region_virtual_query(
                &g_regions, first_stack_base, &information,
                sizeof(information)) == 0);
    REQUIRE(g_regions.live_count == 0U);
    REQUIRE(!gxos_scheduler_close_handle(first_handle));

    /* Reuse the TCB/stack resources without inheriting generation state. */
    REQUIRE(gxos_scheduler_create_suspended_thread(
                &g_scheduler, test_entry, 0, &recycled_handle, &recycled));
    require_fresh_thread(recycled);
    require_stack(recycled);
    REQUIRE(recycled == first);
    REQUIRE(recycled->identity != first_identity);
    REQUIRE(recycled->generation != first_generation);
    REQUIRE(recycled->stack_vm_identity != first_stack_identity);
    REQUIRE(gxos_scheduler_thread_from_handle(first_handle) == 0);
    REQUIRE(recycled_handle != first_handle);
    REQUIRE(gxos_scheduler_close_handle(recycled_handle));
    REQUIRE(gxos_scheduler_discard_created_thread(recycled));
    REQUIRE(g_regions.live_count == 0U);
    REQUIRE(gxos_vm_region_virtual_query(
                &g_regions, recycled->stack_base, &information,
                sizeof(information)) == 0);

    /* A failed creation leaves no VM ledger entry or stale TCB state. */
    g_fail_after = g_page_allocations + 1U;
    REQUIRE(!gxos_scheduler_create_suspended_thread(
                &g_scheduler, test_entry, 0, &failed_handle, &failed));
    g_fail_after = UINT32_MAX;
    REQUIRE(failed == 0 && failed_handle == 0);
    REQUIRE(g_regions.live_count == 0U);
    REQUIRE(gxos_vm_region_ledger_validate(&g_regions));
    REQUIRE(gxos_scheduler_runnable_count() == 0U);
    REQUIRE(gxos_scheduler_blocked_count() == 0U);
    REQUIRE(gxos_scheduler_active_wait_count() == 0U);

    REQUIRE(gxos_scheduler_teardown(&g_scheduler));
    REQUIRE(g_regions.live_count == 0U);
    REQUIRE(gxos_vm_region_ledger_validate(&g_regions));
    if (g_failures != 0U) {
        fprintf(stderr, "SCHEDULER_DURABILITY_HOST_TEST=FAIL failures=%u\n",
                g_failures);
        return 1;
    }
    printf("SCHEDULER_DURABILITY_HOST_TEST=PASS\n");
    return 0;
}

#include "scheduler_foundation.h"
#include "vm_substrate.h"

#include <stdio.h>
#include <string.h>

#define TEST_PAGE_COUNT 256U

static unsigned g_failures;
static unsigned char g_pages[TEST_PAGE_COUNT * GXOS_SCHEDULER_PAGE_SIZE]
    __attribute__((aligned(GXOS_SCHEDULER_PAGE_SIZE)));
static unsigned char g_page_used[TEST_PAGE_COUNT];
static unsigned g_page_allocations;
static unsigned g_fail_after = UINT32_MAX;
static GXOS_SCHEDULER g_scheduler;
static GXOS_VM_REGION_LEDGER g_regions;

/* Context switching is outside this focused host test; the scheduler still
   records these entry points in each suspended context. */
void gxos_scheduler_start_worker(void) {}
void gxos_scheduler_invalid_thread_return(void) {}

#define REQUIRE(condition) do { \
    if (!(condition)) { \
        fprintf(stderr, "scheduler stack VM test failure: %s:%d: %s\n", \
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

static int GXOS_SCHEDULER_MS_ABI allocate_stack(
    void *context, uint64_t usable_bytes,
    GXOS_SCHEDULER_STACK_CONTRACT *contract)
{
    GXOS_VM_REGION_LEDGER *ledger = (GXOS_VM_REGION_LEDGER *)context;
    uint64_t reservation = 0;
    GXOS_VM_STATUS status;

    if (usable_bytes != GXOS_SCHEDULER_STACK_USABLE_SIZE ||
        contract == 0 || test_allocate(0, 0,
                                        GXOS_SCHEDULER_STACK_RESERVATION_PAGES,
                                        &reservation) != 0) return 0;
    memset(contract, 0, sizeof(*contract));
    contract->reservation_base = reservation;
    contract->reservation_bytes = GXOS_SCHEDULER_STACK_RESERVATION_SIZE;
    contract->guard_base = reservation;
    contract->guard_bytes = GXOS_SCHEDULER_STACK_GUARD_SIZE;
    contract->usable_stack_low = reservation + GXOS_SCHEDULER_STACK_GUARD_SIZE;
    contract->usable_stack_high = reservation +
        GXOS_SCHEDULER_STACK_RESERVATION_SIZE;
    contract->usable_stack_bytes = GXOS_SCHEDULER_STACK_USABLE_SIZE;
    contract->backing_memory = reservation;
    contract->backing_pages = GXOS_SCHEDULER_STACK_RESERVATION_PAGES;
    contract->guard_nonpresent = 1;
    status = gxos_vm_region_register(
        ledger, contract->guard_base, contract->guard_bytes,
        contract->reservation_base, 0, GXOS_VM_REGION_STATE_RESERVE, 0,
        GXOS_VM_REGION_TYPE_PRIVATE, &contract->guard_vm_identity);
    if (status != GXOS_VM_STATUS_OK) {
        (void)test_free(reservation, GXOS_SCHEDULER_STACK_RESERVATION_PAGES);
        return 0;
    }
    status = gxos_vm_region_register(
        ledger, contract->usable_stack_low, contract->usable_stack_bytes,
        contract->reservation_base, GXOS_VM_REGION_PAGE_READWRITE,
        GXOS_VM_REGION_STATE_COMMIT, GXOS_VM_REGION_PAGE_READWRITE,
        GXOS_VM_REGION_TYPE_PRIVATE, &contract->usable_vm_identity);
    if (status != GXOS_VM_STATUS_OK) {
        (void)gxos_vm_region_unregister(
            ledger, contract->guard_base, contract->guard_bytes,
            contract->guard_vm_identity);
        (void)test_free(reservation, GXOS_SCHEDULER_STACK_RESERVATION_PAGES);
        memset(contract, 0, sizeof(*contract));
        return 0;
    }
    return 1;
}

static int GXOS_SCHEDULER_MS_ABI free_stack(
    void *context, const GXOS_SCHEDULER_STACK_CONTRACT *contract)
{
    GXOS_VM_REGION_LEDGER *ledger = (GXOS_VM_REGION_LEDGER *)context;
    if (contract == 0 || contract->backing_memory == 0 ||
        contract->backing_pages != GXOS_SCHEDULER_STACK_RESERVATION_PAGES ||
        gxos_vm_region_unregister(ledger, contract->usable_stack_low,
                                  contract->usable_stack_bytes,
                                  contract->usable_vm_identity) !=
            GXOS_VM_STATUS_OK ||
        gxos_vm_region_unregister(ledger, contract->guard_base,
                                  contract->guard_bytes,
                                  contract->guard_vm_identity) !=
            GXOS_VM_STATUS_OK ||
        test_free(contract->backing_memory, contract->backing_pages) != 0) {
        return 0;
    }
    return 1;
}

static uintptr_t GXOS_SCHEDULER_MS_ABI test_entry(void *argument)
{
    return (uintptr_t)argument;
}

static void test_region_api_edges(void)
{
    GXOS_VM_MEMORY_BASIC_INFORMATION information;
    GXOS_VM_REGION_LEDGER ledger;
    GXOS_PHYSICAL_LEDGER physical;
    GXOS_PHYSICAL_ALLOCATION allocation;
    uint64_t identity_one = 0;
    uint64_t identity_two = 0;
    uint64_t physical_before;
    uint64_t commit_before;
    uint64_t virtual_before;
    uint32_t physical_slot;

    gxos_vm_region_ledger_init(&ledger);
    REQUIRE(gxos_vm_region_register(
                &ledger, 0x100000, 0x4000, 0x100000,
                GXOS_VM_REGION_PAGE_READWRITE, GXOS_VM_REGION_STATE_COMMIT,
                GXOS_VM_REGION_PAGE_READWRITE, GXOS_VM_REGION_TYPE_PRIVATE,
                &identity_one) == GXOS_VM_STATUS_OK);
    REQUIRE(identity_one != 0);
    REQUIRE(gxos_vm_region_register(
                &ledger, 0x200000, 0x4000, 0x200000,
                GXOS_VM_REGION_PAGE_READWRITE, GXOS_VM_REGION_STATE_COMMIT,
                GXOS_VM_REGION_PAGE_READWRITE, GXOS_VM_REGION_TYPE_PRIVATE,
                &identity_two) == GXOS_VM_STATUS_OK);
    REQUIRE(identity_two != 0 && identity_two != identity_one);
    REQUIRE(gxos_vm_region_ledger_validate(&ledger));

    memset(&information, 0xA5, sizeof(information));
    REQUIRE(gxos_vm_region_virtual_query(
                &ledger, 0x100000, &information, sizeof(information)) ==
            sizeof(information));
    REQUIRE(information.BaseAddress == 0x100000);
    REQUIRE(information.AllocationBase == 0x100000);
    REQUIRE(information.AllocationProtect == GXOS_VM_REGION_PAGE_READWRITE);
    REQUIRE(information.RegionSize == 0x4000);
    REQUIRE(information.State == GXOS_VM_REGION_STATE_COMMIT);
    REQUIRE(information.Protect == GXOS_VM_REGION_PAGE_READWRITE);
    REQUIRE(information.Type == GXOS_VM_REGION_TYPE_PRIVATE);
    REQUIRE(gxos_vm_region_virtual_query(
                &ledger, 0x103FFF, &information, sizeof(information)) ==
            sizeof(information));
    REQUIRE(information.BaseAddress == 0x100000 &&
            information.RegionSize == 0x4000);
    REQUIRE(gxos_vm_region_virtual_query(
                &ledger, 0x0FFFFF, &information, sizeof(information)) == 0);
    REQUIRE(gxos_vm_region_virtual_query(
                &ledger, 0x104000, &information, sizeof(information)) == 0);
    REQUIRE(gxos_vm_region_virtual_query(
                &ledger, 0x900000, &information, sizeof(information)) == 0);
    REQUIRE(gxos_vm_region_virtual_query(
                &ledger, 0x100000, &information,
                sizeof(information) - 1U) == 0);
    REQUIRE(gxos_vm_region_virtual_query(
                &ledger, 0x100000, 0, sizeof(information)) == 0);

    gxos_physical_ledger_init(&physical, 1);
    memset(&allocation, 0, sizeof(allocation));
    allocation.live = 1;
    allocation.base = 0x100000;
    allocation.bytes = 0x4000;
    allocation.pages = 4;
    allocation.allocation_class = GXOS_MEMORY_ALLOCATION_SCHEDULER_STACK;
    allocation.owner = GXOS_MEMORY_OWNER_SCHEDULER;
    allocation.physical_impact_bytes = 0x4000;
    allocation.commit_impact_bytes = 0x4000;
    allocation.virtual_reservation_impact_bytes = 0x4000;
    allocation.generation = 1;
    REQUIRE(gxos_physical_ledger_insert(&physical, &allocation,
                                        &physical_slot) ==
            GXOS_LEDGER_STATUS_OK);
    physical_before = physical.physical_bytes;
    commit_before = physical.commit_bytes;
    virtual_before = physical.virtual_reservation_bytes;
    REQUIRE(gxos_vm_region_unregister(&ledger, 0x100000, 0x4000,
                                      identity_one) == GXOS_VM_STATUS_OK);
    REQUIRE(physical.physical_bytes == physical_before &&
            physical.commit_bytes == commit_before &&
            physical.virtual_reservation_bytes == virtual_before);
    REQUIRE(gxos_vm_region_unregister(&ledger, 0x200000, 0x4000,
                                      identity_two) == GXOS_VM_STATUS_OK);
    REQUIRE(ledger.live_count == 0 && gxos_vm_region_ledger_validate(&ledger));
}

static void test_scheduler_lifecycle(void)
{
    GXOS_SCHEDULER_TCB *first = 0;
    GXOS_SCHEDULER_TCB *second = 0;
    GXOS_SCHEDULER_TCB *failed = 0;
    GXOS_SCHEDULER_HANDLE first_handle = 0;
    GXOS_SCHEDULER_HANDLE second_handle = 0;
    GXOS_SCHEDULER_HANDLE failed_handle = 0;
    GXOS_VM_MEMORY_BASIC_INFORMATION information;
    uint32_t previous_suspend_count;
    uint32_t live_before_failed_create;
    unsigned cycle;

    memset(g_page_used, 0, sizeof(g_page_used));
    g_page_allocations = 0;
    g_fail_after = UINT32_MAX;
    gxos_vm_region_ledger_init(&g_regions);
    REQUIRE(gxos_scheduler_initialize(
                &g_scheduler, test_allocate, test_free, 0, 0, 0));
    REQUIRE(gxos_scheduler_configure_stack_vm(
            &g_scheduler, allocate_stack, free_stack, &g_regions));
    REQUIRE(gxos_scheduler_create_suspended_thread(
                &g_scheduler, test_entry, (void *)(uintptr_t)1,
                &first_handle, &first));
    REQUIRE(gxos_scheduler_create_suspended_thread(
                &g_scheduler, test_entry, (void *)(uintptr_t)2,
                &second_handle, &second));
    REQUIRE(first != 0 && second != 0 && first->state ==
            GXOS_SCHEDULER_THREAD_CREATED_SUSPENDED &&
            second->state == GXOS_SCHEDULER_THREAD_CREATED_SUSPENDED);
    REQUIRE(g_regions.live_count == 4);
    REQUIRE(first->stack_contract.usable_vm_identity != 0 &&
            second->stack_contract.usable_vm_identity !=
                first->stack_contract.usable_vm_identity);
    REQUIRE(first->stack_contract.reservation_bytes ==
                GXOS_SCHEDULER_STACK_RESERVATION_SIZE &&
            first->stack_contract.guard_bytes ==
                GXOS_SCHEDULER_STACK_GUARD_SIZE &&
            first->stack_contract.usable_stack_bytes ==
                GXOS_SCHEDULER_STACK_USABLE_SIZE);
    REQUIRE(first->stack_contract.usable_stack_low ==
                first->stack_contract.guard_base +
                    GXOS_SCHEDULER_STACK_GUARD_SIZE &&
            first->stack_contract.usable_stack_high ==
                first->stack_contract.usable_stack_low +
                    GXOS_SCHEDULER_STACK_USABLE_SIZE &&
            first->stack_contract.usable_stack_low !=
                first->stack_contract.reservation_base);
    REQUIRE(first->stack_contract.initial_rsp >=
                first->stack_contract.usable_stack_low &&
            first->stack_contract.initial_rsp <
                first->stack_contract.usable_stack_high);
    REQUIRE(*(const uint64_t *)(uintptr_t)(first->gs_base + 0x08) ==
                first->stack_contract.usable_stack_high &&
            *(const uint64_t *)(uintptr_t)(first->gs_base + 0x10) ==
                first->stack_contract.usable_stack_low &&
            *(const uint64_t *)(uintptr_t)(first->teb_base + 0x08) ==
                first->stack_contract.usable_stack_high &&
            *(const uint64_t *)(uintptr_t)(first->teb_base + 0x10) ==
                first->stack_contract.usable_stack_low);
    REQUIRE(gxos_scheduler_validate_thread_context(first));
    {
        GXOS_SCHEDULER_REGISTER_SNAPSHOT snapshot;
        memset(&snapshot, 0, sizeof(snapshot));
        snapshot.rsp = first->stack_contract.initial_rsp;
        snapshot.gs_base = first->gs_base;
        REQUIRE(gxos_scheduler_validate_worker_snapshot(first, &snapshot));
        REQUIRE(gxos_scheduler_note_worker_snapshot(first, &snapshot));
        snapshot.gs_base = second->gs_base;
        REQUIRE(!gxos_scheduler_validate_worker_snapshot(first, &snapshot));
        snapshot.gs_base = 0;
        REQUIRE(!gxos_scheduler_validate_worker_snapshot(first, &snapshot));
        snapshot.gs_base = first->gs_base;
        snapshot.rsp = first->stack_contract.usable_stack_high - 0x200U;
        REQUIRE(gxos_scheduler_note_worker_snapshot(first, &snapshot));
        REQUIRE(first->stack_contract.minimum_rsp == snapshot.rsp &&
                first->stack_contract.high_water_bytes == 0x200U);
        snapshot.rsp = first->stack_contract.guard_base;
        REQUIRE(!gxos_scheduler_note_worker_snapshot(first, &snapshot));
    }
    REQUIRE(gxos_vm_region_virtual_query(
                &g_regions, first->stack_base, &information,
                sizeof(information)) == sizeof(information));
    REQUIRE(information.BaseAddress == first->stack_base &&
            information.AllocationBase == first->stack_contract.reservation_base &&
            information.RegionSize == GXOS_SCHEDULER_STACK_SIZE &&
            information.State == GXOS_VM_REGION_STATE_COMMIT &&
            information.AllocationProtect == GXOS_VM_REGION_PAGE_READWRITE &&
            information.Protect == GXOS_VM_REGION_PAGE_READWRITE &&
            information.Type == GXOS_VM_REGION_TYPE_PRIVATE);
    REQUIRE(gxos_vm_region_virtual_query(
                &g_regions, first->stack_limit - 1U, &information,
                sizeof(information)) == sizeof(information));
    REQUIRE(gxos_vm_region_virtual_query(
                &g_regions, first->stack_contract.guard_base, &information,
                sizeof(information)) == sizeof(information));
    REQUIRE(information.BaseAddress == first->stack_contract.guard_base &&
            information.AllocationBase == first->stack_contract.reservation_base &&
            information.RegionSize == GXOS_SCHEDULER_STACK_GUARD_SIZE &&
            information.State == GXOS_VM_REGION_STATE_RESERVE &&
            information.Protect == 0);
    REQUIRE(gxos_vm_region_virtual_query(
                &g_regions, first->stack_limit, &information,
                sizeof(information)) == 0);
    REQUIRE(gxos_scheduler_runnable_count() == 0);
    REQUIRE(gxos_scheduler_resume_thread(first_handle,
                                          &previous_suspend_count));
    REQUIRE(previous_suspend_count == 1 && gxos_scheduler_runnable_count() == 1);
    REQUIRE(gxos_scheduler_resume_thread(second_handle,
                                          &previous_suspend_count));
    REQUIRE(previous_suspend_count == 1 && gxos_scheduler_runnable_count() == 2);

    live_before_failed_create = g_regions.live_count;
    g_fail_after = g_page_allocations + 1U;
    REQUIRE(!gxos_scheduler_create_suspended_thread(
                &g_scheduler, test_entry, (void *)(uintptr_t)3,
                &failed_handle, &failed));
    REQUIRE(failed == 0 && failed_handle == 0 &&
            g_regions.live_count == live_before_failed_create &&
            gxos_vm_region_ledger_validate(&g_regions));
    g_fail_after = UINT32_MAX;

    REQUIRE(gxos_scheduler_close_handle(first_handle));
    REQUIRE(gxos_scheduler_discard_created_thread(first));
    REQUIRE(g_regions.live_count == 2);
    REQUIRE(gxos_scheduler_close_handle(second_handle));
    REQUIRE(gxos_scheduler_discard_created_thread(second));
    REQUIRE(g_regions.live_count == 0 && gxos_scheduler_runnable_count() == 0);

    for (cycle = 0; cycle != 12; ++cycle) {
        GXOS_SCHEDULER_TCB *thread = 0;
        GXOS_SCHEDULER_HANDLE handle = 0;
        REQUIRE(gxos_scheduler_create_suspended_thread(
                    &g_scheduler, test_entry, (void *)(uintptr_t)cycle,
                    &handle, &thread));
        REQUIRE(thread != 0 && g_regions.live_count == 2);
        REQUIRE(gxos_scheduler_close_handle(handle));
        REQUIRE(gxos_scheduler_discard_created_thread(thread));
        REQUIRE(g_regions.live_count == 0 &&
                gxos_vm_region_ledger_validate(&g_regions));
    }
    REQUIRE(gxos_scheduler_teardown(&g_scheduler));
    REQUIRE(g_regions.live_count == 0 && g_page_allocations != 0);
}

int main(void)
{
    test_region_api_edges();
    test_scheduler_lifecycle();
    if (g_failures != 0) {
        fprintf(stderr, "SCHEDULER_STACK_VM_HOST_TEST=FAIL failures=%u\n",
                g_failures);
        return 1;
    }
    printf("SCHEDULER_STACK_VM_HOST_TEST=PASS\n");
    return 0;
}

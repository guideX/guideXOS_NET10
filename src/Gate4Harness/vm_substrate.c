#include "vm_substrate.h"

typedef struct {
    uint64_t virtual_page;
    uint64_t physical_page;
    void *alias;
} GXOS_VM_CREATED_PAGE;

/* The loader is single-threaded during this milestone; keep rollback state
   out of the firmware stack so freestanding builds need no stack-probe ABI. */
static GXOS_VM_CREATED_PAGE
    g_vm_created_pages[GXOS_VM_MAX_COMMITMENTS];

static void zero_bytes(void *memory, uint64_t bytes)
{
    uint8_t *cursor = (uint8_t *)memory;
    while (bytes-- != 0) *cursor++ = 0;
}

static int add_u64(uint64_t left, uint64_t right, uint64_t *result)
{
    if (left > UINT64_MAX - right) return 0;
    *result = left + right;
    return 1;
}

static int range_end(uint64_t base, uint64_t bytes, uint64_t *end)
{
    if (bytes == 0 || base > UINT64_MAX - bytes) return 0;
    *end = base + bytes;
    return 1;
}

static int vm_region_contains(const GXOS_VM_REGION *region,
                              uint64_t address)
{
    uint64_t end;
    return region != 0 && region->live &&
        range_end(region->base, region->bytes, &end) &&
        address >= region->base && address < end;
}

void gxos_vm_region_ledger_init(GXOS_VM_REGION_LEDGER *ledger)
{
    if (ledger == 0) return;
    zero_bytes(ledger, sizeof(*ledger));
    ledger->next_identity = 1;
}

GXOS_VM_STATUS gxos_vm_region_register(
    GXOS_VM_REGION_LEDGER *ledger,
    uint64_t base,
    uint64_t bytes,
    uint64_t allocation_base,
    uint32_t allocation_protect,
    uint32_t state,
    uint32_t protect,
    uint32_t type,
    uint64_t *allocation_identity_out)
{
    uint32_t index;
    uint64_t end;
    if (allocation_identity_out != 0) *allocation_identity_out = 0;
    if (ledger == 0 || allocation_identity_out == 0 || base == 0 ||
        allocation_base == 0 || bytes == 0 ||
        base % GXOS_VM_PAGE_SIZE != 0 ||
        bytes % GXOS_VM_PAGE_SIZE != 0 ||
        allocation_base % GXOS_VM_PAGE_SIZE != 0 ||
        !range_end(base, bytes, &end) || allocation_base > base ||
        (state != GXOS_VM_REGION_STATE_COMMIT &&
         state != GXOS_VM_REGION_STATE_RESERVE) || type == 0) {
        return base != 0 && bytes != 0 && base > UINT64_MAX - bytes
            ? GXOS_VM_STATUS_OVERFLOW : GXOS_VM_STATUS_INVALID_ARGUMENT;
    }
    for (index = 0; index != GXOS_VM_REGION_LEDGER_CAPACITY; ++index) {
        const GXOS_VM_REGION *existing = &ledger->entries[index];
        uint64_t existing_end;
        if (!existing->live) continue;
        if (!range_end(existing->base, existing->bytes, &existing_end)) {
            return GXOS_VM_STATUS_INVALID_STATE;
        }
        if (base < existing_end && existing->base < end) {
            return GXOS_VM_STATUS_OVERLAP;
        }
    }
    if (ledger->live_count >= GXOS_VM_REGION_LEDGER_CAPACITY) {
        ledger->exhausted = 1;
        return GXOS_VM_STATUS_CAPACITY;
    }
    if (ledger->next_identity == 0) ledger->next_identity = 1;
    for (index = 0; index != GXOS_VM_REGION_LEDGER_CAPACITY; ++index) {
        GXOS_VM_REGION *region = &ledger->entries[index];
        uint64_t identity;
        if (region->live) continue;
        identity = ledger->next_identity++;
        if (identity == 0) {
            identity = ledger->next_identity++;
            if (identity == 0) return GXOS_VM_STATUS_OVERFLOW;
        }
        zero_bytes(region, sizeof(*region));
        region->live = 1;
        region->base = base;
        region->bytes = bytes;
        region->allocation_base = allocation_base;
        region->allocation_protect = allocation_protect;
        region->state = state;
        region->protect = protect;
        region->type = type;
        region->allocation_identity = identity;
        ++ledger->live_count;
        *allocation_identity_out = identity;
        return GXOS_VM_STATUS_OK;
    }
    ledger->exhausted = 1;
    return GXOS_VM_STATUS_CAPACITY;
}

GXOS_VM_STATUS gxos_vm_region_unregister(
    GXOS_VM_REGION_LEDGER *ledger,
    uint64_t base,
    uint64_t bytes,
    uint64_t allocation_identity)
{
    uint32_t index;
    if (ledger == 0 || base == 0 || bytes == 0 ||
        allocation_identity == 0) return GXOS_VM_STATUS_INVALID_ARGUMENT;
    for (index = 0; index != GXOS_VM_REGION_LEDGER_CAPACITY; ++index) {
        GXOS_VM_REGION *region = &ledger->entries[index];
        if (region->live && region->base == base && region->bytes == bytes &&
            region->allocation_identity == allocation_identity) {
            zero_bytes(region, sizeof(*region));
            if (ledger->live_count == 0) return GXOS_VM_STATUS_INVALID_STATE;
            --ledger->live_count;
            return GXOS_VM_STATUS_OK;
        }
    }
    return GXOS_VM_STATUS_NOT_FOUND;
}

int gxos_vm_region_ledger_validate(const GXOS_VM_REGION_LEDGER *ledger)
{
    uint32_t index;
    uint32_t live_count = 0;
    if (ledger == 0 || ledger->next_identity == 0 ||
        ledger->live_count > GXOS_VM_REGION_LEDGER_CAPACITY) return 0;
    for (index = 0; index != GXOS_VM_REGION_LEDGER_CAPACITY; ++index) {
        const GXOS_VM_REGION *region = &ledger->entries[index];
        uint32_t other;
        uint64_t end;
        if (!region->live) continue;
        ++live_count;
        if (region->base == 0 || region->allocation_base == 0 ||
            region->bytes == 0 || region->base % GXOS_VM_PAGE_SIZE != 0 ||
            region->bytes % GXOS_VM_PAGE_SIZE != 0 ||
            region->allocation_base % GXOS_VM_PAGE_SIZE != 0 ||
            region->allocation_base > region->base ||
            !range_end(region->base, region->bytes, &end) ||
            (region->state != GXOS_VM_REGION_STATE_COMMIT &&
             region->state != GXOS_VM_REGION_STATE_RESERVE) ||
            region->type == 0 || region->allocation_identity == 0) {
            return 0;
        }
        for (other = index + 1; other != GXOS_VM_REGION_LEDGER_CAPACITY;
             ++other) {
            const GXOS_VM_REGION *candidate = &ledger->entries[other];
            uint64_t candidate_end;
            if (!candidate->live) continue;
            if (!range_end(candidate->base, candidate->bytes, &candidate_end) ||
                (region->base < candidate_end &&
                 candidate->base < end)) return 0;
        }
    }
    return live_count == ledger->live_count;
}

uint64_t gxos_vm_region_virtual_query(
    const GXOS_VM_REGION_LEDGER *ledger,
    uint64_t address,
    GXOS_VM_MEMORY_BASIC_INFORMATION *information,
    uint64_t length)
{
    uint32_t index;
    const GXOS_VM_REGION *region = 0;
    if (ledger == 0 || information == 0 ||
        length < sizeof(*information)) return 0;
    for (index = 0; index != GXOS_VM_REGION_LEDGER_CAPACITY; ++index) {
        if (vm_region_contains(&ledger->entries[index], address)) {
            region = &ledger->entries[index];
            break;
        }
    }
    if (region == 0) return 0;
    zero_bytes(information, sizeof(*information));
    information->BaseAddress = region->base;
    information->AllocationBase = region->allocation_base;
    information->AllocationProtect = region->allocation_protect;
    information->RegionSize = region->bytes;
    information->State = region->state;
    information->Protect = region->protect;
    information->Type = region->type;
    return sizeof(*information);
}

static int canonical48(uint64_t address)
{
    uint64_t upper = address >> 48;
    uint64_t bit47 = (address >> 47) & 1U;
    return (bit47 == 0 && upper == 0) ||
        (bit47 != 0 && upper == 0xFFFFU);
}

static uint64_t pml4_index(uint64_t address)
{
    return (address >> 39) & 0x1FFU;
}

static uint64_t pdpt_index(uint64_t address)
{
    return (address >> 30) & 0x1FFU;
}

static uint64_t pd_index(uint64_t address)
{
    return (address >> 21) & 0x1FFU;
}

static uint64_t pt_index(uint64_t address)
{
    return (address >> 12) & 0x1FFU;
}

static uint64_t entry_address(uint64_t entry)
{
    return entry & GXOS_X64_PAGING_PHYSICAL_MASK;
}

static int range_contains_page(uint64_t range_base, uint64_t range_length,
                               uint64_t virtual_page)
{
    uint64_t end;
    if (range_length == 0 ||
        !range_end(range_base, range_length, &end) ||
        virtual_page % GXOS_VM_PAGE_SIZE != 0) return 0;
    return virtual_page >= range_base &&
        virtual_page <= end - GXOS_VM_PAGE_SIZE;
}

static int paging_range_is_valid(const GXOS_VM_PAGING *paging,
                                 uint64_t range_base,
                                 uint64_t range_length)
{
    uint64_t range_end_address;
    if (paging == 0 || range_length == 0 ||
        !canonical48(range_base) ||
        !range_end(range_base, range_length, &range_end_address) ||
        pml4_index(range_base) != pml4_index(range_end_address - 1U) ||
        pml4_index(range_base) != paging->arena_pml4_index) {
        return 0;
    }
    return 1;
}

static GXOS_VM_PAGING_STATUS table_alias(
    uint64_t physical,
    GXOS_VM_PHYSICAL_ALIAS physical_alias,
    void *alias_context,
    volatile uint64_t **table_out)
{
    void *alias;
    if (table_out == 0 || physical_alias == 0) {
        return GXOS_VM_PAGING_STATUS_INVALID_ARGUMENT;
    }
    alias = physical_alias(alias_context, physical);
    if (alias == 0) return GXOS_VM_PAGING_STATUS_INVALID_TABLE;
    *table_out = (volatile uint64_t *)alias;
    return GXOS_VM_PAGING_STATUS_OK;
}

static GXOS_VM_PAGING_STATUS current_control_state(
    uint64_t *cr0, uint64_t *cr3, uint64_t *cr4, uint64_t *efer)
{
#if defined(__x86_64__)
    uint32_t low;
    uint32_t high;
    uint32_t msr = 0xC0000080U;
    if (cr0 == 0 || cr3 == 0 || cr4 == 0 || efer == 0) {
        return GXOS_VM_PAGING_STATUS_INVALID_ARGUMENT;
    }
    __asm__ volatile ("mov %%cr0,%0" : "=r"(*cr0));
    __asm__ volatile ("mov %%cr3,%0" : "=r"(*cr3));
    __asm__ volatile ("mov %%cr4,%0" : "=r"(*cr4));
    __asm__ volatile ("rdmsr" : "=a"(low), "=d"(high) : "c"(msr));
    *efer = ((uint64_t)high << 32) | low;
    return GXOS_VM_PAGING_STATUS_OK;
#else
    (void)cr0;
    (void)cr3;
    (void)cr4;
    (void)efer;
    return GXOS_VM_PAGING_STATUS_INVALID_ARGUMENT;
#endif
}

static void invalidate_page(const GXOS_VM_PAGING *paging, uint64_t address)
{
#if defined(__x86_64__)
    if (paging != 0 && paging->active) {
        __asm__ volatile ("invlpg (%0)" : : "r"(address) : "memory");
    }
#else
    (void)paging;
    (void)address;
#endif
}

GXOS_VM_PAGING_STATUS gxos_vm_paging_query_root(
    uint64_t root_physical,
    uint64_t virtual_address,
    GXOS_VM_PHYSICAL_ALIAS physical_alias,
    void *alias_context,
    GXOS_VM_MAPPING *mapping_out)
{
    volatile uint64_t *pml4;
    volatile uint64_t *pdpt;
    volatile uint64_t *pd;
    volatile uint64_t *pt;
    uint64_t entry;
    GXOS_VM_PAGING_STATUS status;
    if (mapping_out == 0 || physical_alias == 0 || root_physical == 0) {
        return GXOS_VM_PAGING_STATUS_INVALID_ARGUMENT;
    }
    zero_bytes(mapping_out, sizeof(*mapping_out));
    if (!canonical48(virtual_address)) {
        return GXOS_VM_PAGING_STATUS_NONCANONICAL;
    }
    status = table_alias(entry_address(root_physical), physical_alias,
                         alias_context, &pml4);
    if (status != GXOS_VM_PAGING_STATUS_OK) return status;
    entry = pml4[pml4_index(virtual_address)];
    if ((entry & GXOS_X64_PAGING_ENTRY_PRESENT) == 0) {
        return GXOS_VM_PAGING_STATUS_NOT_PRESENT;
    }
    status = table_alias(entry_address(entry), physical_alias, alias_context,
                         &pdpt);
    if (status != GXOS_VM_PAGING_STATUS_OK) return status;
    entry = pdpt[pdpt_index(virtual_address)];
    if ((entry & GXOS_X64_PAGING_ENTRY_PRESENT) == 0) {
        return GXOS_VM_PAGING_STATUS_NOT_PRESENT;
    }
    if ((entry & GXOS_X64_PAGING_ENTRY_PAGE_SIZE) != 0) {
        mapping_out->physical_base = (entry_address(entry) &
                                      ~((uint64_t)0x40000000U - 1U)) +
            (virtual_address & ((uint64_t)0x40000000U - 1U));
        mapping_out->page_size = 0x40000000ULL;
        mapping_out->entry_flags = entry;
        mapping_out->level = 3;
        mapping_out->present = 1;
        return GXOS_VM_PAGING_STATUS_OK;
    }
    status = table_alias(entry_address(entry), physical_alias, alias_context,
                         &pd);
    if (status != GXOS_VM_PAGING_STATUS_OK) return status;
    entry = pd[pd_index(virtual_address)];
    if ((entry & GXOS_X64_PAGING_ENTRY_PRESENT) == 0) {
        return GXOS_VM_PAGING_STATUS_NOT_PRESENT;
    }
    if ((entry & GXOS_X64_PAGING_ENTRY_PAGE_SIZE) != 0) {
        mapping_out->physical_base = (entry_address(entry) &
                                      ~((uint64_t)0x200000U - 1U)) +
            (virtual_address & ((uint64_t)0x200000U - 1U));
        mapping_out->page_size = 0x200000ULL;
        mapping_out->entry_flags = entry;
        mapping_out->level = 2;
        mapping_out->present = 1;
        return GXOS_VM_PAGING_STATUS_OK;
    }
    status = table_alias(entry_address(entry), physical_alias, alias_context,
                         &pt);
    if (status != GXOS_VM_PAGING_STATUS_OK) return status;
    entry = pt[pt_index(virtual_address)];
    if ((entry & GXOS_X64_PAGING_ENTRY_PRESENT) == 0) {
        return GXOS_VM_PAGING_STATUS_NOT_PRESENT;
    }
    mapping_out->physical_base = entry_address(entry) +
        (virtual_address & (GXOS_VM_PAGE_SIZE - 1U));
    mapping_out->page_size = GXOS_VM_PAGE_SIZE;
    mapping_out->entry_flags = entry;
    mapping_out->level = 1;
    mapping_out->present = 1;
    return GXOS_VM_PAGING_STATUS_OK;
}

GXOS_VM_PAGING_STATUS gxos_vm_paging_audit_current(
    GXOS_X64_PAGING_AUDIT *audit,
    GXOS_VM_PHYSICAL_ALIAS physical_alias,
    void *alias_context)
{
    uint64_t address = 0;
    uint64_t limit = 0x100000000ULL;
    if (audit == 0 || physical_alias == 0) {
        return GXOS_VM_PAGING_STATUS_INVALID_ARGUMENT;
    }
    zero_bytes(audit, sizeof(*audit));
    if (current_control_state(&audit->cr0, &audit->cr3, &audit->cr4,
                              &audit->efer) != GXOS_VM_PAGING_STATUS_OK) {
        return GXOS_VM_PAGING_STATUS_INVALID_ARGUMENT;
    }
    audit->pae_enabled = (audit->cr4 & ((uint64_t)1U << 5)) != 0;
    audit->la57_enabled = (audit->cr4 & ((uint64_t)1U << 12)) != 0;
    audit->nx_enabled = (audit->efer & ((uint64_t)1U << 11)) != 0;
    while (address < limit) {
        GXOS_VM_MAPPING mapping;
        GXOS_VM_PAGING_STATUS status = gxos_vm_paging_query_root(
            audit->cr3 & GXOS_X64_PAGING_PHYSICAL_MASK, address,
            physical_alias, alias_context, &mapping);
        uint64_t mapping_base;
        if (status != GXOS_VM_PAGING_STATUS_OK || !mapping.present) break;
        mapping_base = address & ~(mapping.page_size - 1U);
        if (mapping.physical_base != mapping_base) break;
        audit->direct_identity_bytes += mapping.page_size;
        if (mapping.page_size == GXOS_VM_PAGE_SIZE) {
            audit->page_4k_count++;
        } else if (mapping.page_size == 0x200000ULL) {
            audit->page_2m_count++;
        } else if (mapping.page_size == 0x40000000ULL) {
            audit->page_1g_count++;
        }
        if (!add_u64(address, mapping.page_size, &address)) break;
    }
    return GXOS_VM_PAGING_STATUS_OK;
}

GXOS_VM_PAGING_STATUS gxos_vm_paging_create(
    GXOS_VM_PAGING *paging,
    uint64_t current_cr3,
    uint64_t arena_base,
    uint64_t arena_length,
    uint32_t nx_enabled,
    const GXOS_VM_PAGE_ALLOCATOR *table_allocator)
{
    volatile uint64_t *current_root;
    uint64_t arena_end;
    uint64_t root_physical;
    void *root_alias;
    uint32_t index;
    if (paging == 0 || current_cr3 == 0 || arena_length == 0 ||
        table_allocator == 0 || table_allocator->allocate_page == 0 ||
        table_allocator->free_page == 0 || table_allocator->physical_alias == 0 ||
        !canonical48(arena_base) || !range_end(arena_base, arena_length,
                                                &arena_end) ||
        pml4_index(arena_base) != pml4_index(arena_end - 1U)) {
        return GXOS_VM_PAGING_STATUS_INVALID_ARGUMENT;
    }
    zero_bytes(paging, sizeof(*paging));
    if (table_alias(current_cr3 & GXOS_X64_PAGING_PHYSICAL_MASK,
                    table_allocator->physical_alias,
                    table_allocator->context, &current_root) !=
            GXOS_VM_PAGING_STATUS_OK) {
        return GXOS_VM_PAGING_STATUS_INVALID_TABLE;
    }
    if (!table_allocator->allocate_page(table_allocator->context,
                                        &root_physical, &root_alias)) {
        return GXOS_VM_PAGING_STATUS_ALLOCATION;
    }
    if (root_physical == 0 || root_alias == 0) {
        table_allocator->free_page(table_allocator->context, root_physical,
                                   root_alias);
        return GXOS_VM_PAGING_STATUS_ALLOCATION;
    }
    zero_bytes(root_alias, GXOS_VM_PAGE_SIZE);
    for (index = 0; index != 512U; ++index) {
        ((uint64_t *)root_alias)[index] = current_root[index];
    }
    if (((uint64_t *)root_alias)[pml4_index(arena_base)] &
            GXOS_X64_PAGING_ENTRY_PRESENT) {
        table_allocator->free_page(table_allocator->context, root_physical,
                                   root_alias);
        return GXOS_VM_PAGING_STATUS_ARENA_CONFLICT;
    }
    paging->root_physical = root_physical;
    paging->root_alias = root_alias;
    paging->arena_base = arena_base;
    paging->arena_length = arena_length;
    paging->arena_pml4_index = (uint32_t)pml4_index(arena_base);
    paging->nx_enabled = nx_enabled != 0;
    paging->table_allocator = *table_allocator;
    paging->owned_table_page_count = 1;
    paging->owned_table_pages[0].physical_base = root_physical;
    paging->owned_table_pages[0].alias = root_alias;
    return GXOS_VM_PAGING_STATUS_OK;
}

GXOS_VM_PAGING_STATUS gxos_vm_paging_switch_to_owned_root(
    GXOS_VM_PAGING *paging,
    uint64_t *old_cr3_out,
    uint64_t *new_cr3_out)
{
#if defined(__x86_64__)
    uint64_t old_cr3;
    if (paging == 0 || paging->root_physical == 0) {
        return GXOS_VM_PAGING_STATUS_INVALID_ARGUMENT;
    }
    __asm__ volatile ("mov %%cr3,%0" : "=r"(old_cr3));
    __asm__ volatile ("mov %0,%%cr3" : : "r"(paging->root_physical)
                     : "memory");
    paging->previous_cr3 = old_cr3;
    if (old_cr3_out != 0) *old_cr3_out = old_cr3;
    if (new_cr3_out != 0) *new_cr3_out = paging->root_physical;
    paging->active = 1;
    return GXOS_VM_PAGING_STATUS_OK;
#else
    (void)paging;
    (void)old_cr3_out;
    (void)new_cr3_out;
    return GXOS_VM_PAGING_STATUS_INVALID_ARGUMENT;
#endif
}

GXOS_VM_PAGING_STATUS gxos_vm_paging_switch_to_previous_root(
    GXOS_VM_PAGING *paging)
{
#if defined(__x86_64__)
    if (paging == 0 || !paging->active || paging->previous_cr3 == 0) {
        return GXOS_VM_PAGING_STATUS_INVALID_ARGUMENT;
    }
    __asm__ volatile ("mov %0,%%cr3" : : "r"(paging->previous_cr3)
                     : "memory");
    paging->active = 0;
    return GXOS_VM_PAGING_STATUS_OK;
#else
    (void)paging;
    return GXOS_VM_PAGING_STATUS_INVALID_ARGUMENT;
#endif
}

GXOS_VM_PAGING_STATUS gxos_vm_paging_destroy(GXOS_VM_PAGING *paging)
{
    uint32_t index;
    if (paging == 0 || paging->root_physical == 0 ||
        paging->table_allocator.free_page == 0) {
        return GXOS_VM_PAGING_STATUS_INVALID_ARGUMENT;
    }
    if (paging->active && gxos_vm_paging_switch_to_previous_root(paging) !=
        GXOS_VM_PAGING_STATUS_OK) {
        return GXOS_VM_PAGING_STATUS_INVALID_ARGUMENT;
    }
    for (index = paging->owned_table_page_count; index != 0; --index) {
        paging->table_allocator.free_page(
            paging->table_allocator.context,
            paging->owned_table_pages[index - 1U].physical_base,
            paging->owned_table_pages[index - 1U].alias);
    }
    zero_bytes(paging, sizeof(*paging));
    return GXOS_VM_PAGING_STATUS_OK;
}

void gxos_vm_paging_mark_active(GXOS_VM_PAGING *paging, uint32_t active)
{
    if (paging != 0) paging->active = active != 0;
}

GXOS_VM_PAGING_STATUS gxos_vm_paging_query(
    const GXOS_VM_PAGING *paging,
    uint64_t virtual_address,
    GXOS_VM_MAPPING *mapping_out)
{
    if (paging == 0 || paging->root_physical == 0 ||
        paging->table_allocator.physical_alias == 0) {
        return GXOS_VM_PAGING_STATUS_INVALID_ARGUMENT;
    }
    return gxos_vm_paging_query_root(
        paging->root_physical, virtual_address,
        paging->table_allocator.physical_alias,
        paging->table_allocator.context, mapping_out);
}

static GXOS_VM_PAGING_STATUS allocate_table(
    GXOS_VM_PAGING *paging,
    volatile uint64_t *parent,
    uint64_t index,
    volatile uint64_t **child_out)
{
    uint64_t physical;
    void *alias;
    GXOS_VM_PAGING_STATUS status;
    if (parent[index] & GXOS_X64_PAGING_ENTRY_PRESENT) {
        if (parent[index] & GXOS_X64_PAGING_ENTRY_PAGE_SIZE) {
            return GXOS_VM_PAGING_STATUS_LARGE_PAGE;
        }
        return table_alias(entry_address(parent[index]),
                           paging->table_allocator.physical_alias,
                           paging->table_allocator.context, child_out);
    }
    if (paging->owned_table_page_count >= GXOS_VM_MAX_OWNED_TABLE_PAGES) {
        return GXOS_VM_PAGING_STATUS_CAPACITY;
    }
    if (!paging->table_allocator.allocate_page(
            paging->table_allocator.context, &physical, &alias)) {
        return GXOS_VM_PAGING_STATUS_ALLOCATION;
    }
    if (physical == 0 || alias == 0) {
        paging->table_allocator.free_page(paging->table_allocator.context,
                                          physical, alias);
        return GXOS_VM_PAGING_STATUS_ALLOCATION;
    }
    zero_bytes(alias, GXOS_VM_PAGE_SIZE);
    parent[index] = physical | GXOS_X64_PAGING_ENTRY_PRESENT |
        GXOS_X64_PAGING_ENTRY_WRITABLE;
    paging->owned_table_pages[paging->owned_table_page_count].physical_base =
        physical;
    paging->owned_table_pages[paging->owned_table_page_count].alias = alias;
    paging->owned_table_page_count++;
    status = table_alias(physical, paging->table_allocator.physical_alias,
                         paging->table_allocator.context, child_out);
    if (status != GXOS_VM_PAGING_STATUS_OK) {
        parent[index] = 0;
        paging->owned_table_page_count--;
        paging->owned_table_pages[paging->owned_table_page_count].physical_base = 0;
        paging->owned_table_pages[paging->owned_table_page_count].alias = 0;
        paging->table_allocator.free_page(paging->table_allocator.context,
                                           physical, alias);
    }
    return status;
}

static GXOS_VM_PAGING_STATUS gxos_vm_paging_map_page_with_flags_in_range(
    GXOS_VM_PAGING *paging,
    uint64_t virtual_page,
    uint64_t physical_page,
    uint32_t writable,
    uint32_t executable,
    uint64_t leaf_flags,
    uint64_t allowed_base,
    uint64_t allowed_length);

GXOS_VM_PAGING_STATUS gxos_vm_paging_map_page(
    GXOS_VM_PAGING *paging,
    uint64_t virtual_page,
    uint64_t physical_page,
    uint32_t writable,
    uint32_t executable)
{
    return gxos_vm_paging_map_page_with_flags_in_range(
        paging, virtual_page, physical_page, writable, executable, 0,
        paging == 0 ? 0 : paging->arena_base,
        paging == 0 ? 0 : paging->arena_length);
}

GXOS_VM_PAGING_STATUS gxos_vm_paging_map_page_with_flags(
    GXOS_VM_PAGING *paging,
    uint64_t virtual_page,
    uint64_t physical_page,
    uint32_t writable,
    uint32_t executable,
    uint64_t leaf_flags)
{
    return gxos_vm_paging_map_page_with_flags_in_range(
        paging, virtual_page, physical_page, writable, executable, leaf_flags,
        paging == 0 ? 0 : paging->arena_base,
        paging == 0 ? 0 : paging->arena_length);
}

static GXOS_VM_PAGING_STATUS gxos_vm_paging_map_page_with_flags_in_range(
    GXOS_VM_PAGING *paging,
    uint64_t virtual_page,
    uint64_t physical_page,
    uint32_t writable,
    uint32_t executable,
    uint64_t leaf_flags,
    uint64_t allowed_base,
    uint64_t allowed_length)
{
    volatile uint64_t *pml4;
    volatile uint64_t *pdpt;
    volatile uint64_t *pd;
    volatile uint64_t *pt;
    uint64_t *pte;
    uint64_t flags;
    GXOS_VM_PAGING_STATUS status;
    if (paging == 0 || physical_page == 0 || !canonical48(virtual_page)) {
        return !canonical48(virtual_page)
            ? GXOS_VM_PAGING_STATUS_NONCANONICAL
            : GXOS_VM_PAGING_STATUS_INVALID_ARGUMENT;
    }
    if (virtual_page % GXOS_VM_PAGE_SIZE != 0 ||
        physical_page % GXOS_VM_PAGE_SIZE != 0) {
        return GXOS_VM_PAGING_STATUS_ALIGNMENT;
    }
    if ((leaf_flags & ~(GXOS_X64_PAGING_ENTRY_WRITE_THROUGH |
                        GXOS_X64_PAGING_ENTRY_CACHE_DISABLE)) != 0) {
        return GXOS_VM_PAGING_STATUS_INVALID_ARGUMENT;
    }
    if (!paging_range_is_valid(paging, allowed_base, allowed_length) ||
        !range_contains_page(allowed_base, allowed_length, virtual_page)) {
        return GXOS_VM_PAGING_STATUS_OUTSIDE_ARENA;
    }
    pml4 = (volatile uint64_t *)paging->root_alias;
    if (pml4 == 0) return GXOS_VM_PAGING_STATUS_INVALID_TABLE;
    status = allocate_table(paging, pml4, pml4_index(virtual_page), &pdpt);
    if (status != GXOS_VM_PAGING_STATUS_OK) return status;
    status = allocate_table(paging, pdpt, pdpt_index(virtual_page), &pd);
    if (status != GXOS_VM_PAGING_STATUS_OK) return status;
    status = allocate_table(paging, pd, pd_index(virtual_page), &pt);
    if (status != GXOS_VM_PAGING_STATUS_OK) return status;
    pte = (uint64_t *)(uintptr_t)&pt[pt_index(virtual_page)];
    if (*pte & GXOS_X64_PAGING_ENTRY_PRESENT) {
        return GXOS_VM_PAGING_STATUS_CONFLICT;
    }
    flags = GXOS_X64_PAGING_ENTRY_PRESENT |
        (writable != 0 ? GXOS_X64_PAGING_ENTRY_WRITABLE : 0);
    if (!executable && paging->nx_enabled) {
        flags |= GXOS_X64_PAGING_ENTRY_NO_EXECUTE;
    }
    *pte = physical_page | flags | leaf_flags;
    invalidate_page(paging, virtual_page);
    return GXOS_VM_PAGING_STATUS_OK;
}

GXOS_VM_PAGING_STATUS gxos_vm_paging_map_range(
    GXOS_VM_PAGING *paging,
    uint64_t virtual_start,
    uint64_t physical_start,
    uint64_t page_count,
    uint32_t writable,
    uint32_t executable)
{
    return gxos_vm_paging_map_range_with_flags(
        paging, virtual_start, physical_start, page_count,
        writable, executable, 0);
}

GXOS_VM_PAGING_STATUS gxos_vm_paging_map_range_with_flags(
    GXOS_VM_PAGING *paging,
    uint64_t virtual_start,
    uint64_t physical_start,
    uint64_t page_count,
    uint32_t writable,
    uint32_t executable,
    uint64_t leaf_flags)
{
    return gxos_vm_paging_map_range_with_flags_in_window(
        paging, virtual_start, physical_start, page_count, writable,
        executable, leaf_flags, paging == 0 ? 0 : paging->arena_base,
        paging == 0 ? 0 : paging->arena_length);
}

GXOS_VM_PAGING_STATUS gxos_vm_paging_map_range_with_flags_in_window(
    GXOS_VM_PAGING *paging,
    uint64_t virtual_start,
    uint64_t physical_start,
    uint64_t page_count,
    uint32_t writable,
    uint32_t executable,
    uint64_t leaf_flags,
    uint64_t window_base,
    uint64_t window_length)
{
    uint64_t index;
    if (page_count == 0 || page_count > GXOS_VM_MAX_COMMITMENTS ||
        virtual_start % GXOS_VM_PAGE_SIZE != 0 ||
        physical_start % GXOS_VM_PAGE_SIZE != 0 ||
        page_count > (UINT64_MAX - virtual_start) / GXOS_VM_PAGE_SIZE ||
        page_count > (UINT64_MAX - physical_start) / GXOS_VM_PAGE_SIZE) {
        return GXOS_VM_PAGING_STATUS_INVALID_ARGUMENT;
    }
    if ((leaf_flags & ~(GXOS_X64_PAGING_ENTRY_WRITE_THROUGH |
                        GXOS_X64_PAGING_ENTRY_CACHE_DISABLE)) != 0) {
        return GXOS_VM_PAGING_STATUS_INVALID_ARGUMENT;
    }
    for (index = 0; index != page_count; ++index) {
        GXOS_VM_PAGING_STATUS status = gxos_vm_paging_map_page_with_flags_in_range(
            paging, virtual_start + index * GXOS_VM_PAGE_SIZE,
            physical_start + index * GXOS_VM_PAGE_SIZE, writable, executable,
            leaf_flags, window_base, window_length);
        if (status != GXOS_VM_PAGING_STATUS_OK) {
            while (index != 0) {
                --index;
                (void)gxos_vm_paging_unmap_page_in_window(
                    paging, virtual_start + index * GXOS_VM_PAGE_SIZE, 0,
                    window_base, window_length);
            }
            return status;
        }
    }
    return GXOS_VM_PAGING_STATUS_OK;
}

GXOS_VM_PAGING_STATUS gxos_vm_paging_unmap_page_in_window(
    GXOS_VM_PAGING *paging,
    uint64_t virtual_page,
    uint64_t *physical_page_out,
    uint64_t window_base,
    uint64_t window_length)
{
    volatile uint64_t *pml4;
    volatile uint64_t *pdpt;
    volatile uint64_t *pd;
    volatile uint64_t *pt;
    uint64_t entry;
    GXOS_VM_PAGING_STATUS status;
    if (physical_page_out != 0) *physical_page_out = 0;
    if (paging == 0 || !canonical48(virtual_page)) {
        return GXOS_VM_PAGING_STATUS_NONCANONICAL;
    }
    if (virtual_page % GXOS_VM_PAGE_SIZE != 0) {
        return GXOS_VM_PAGING_STATUS_ALIGNMENT;
    }
    if (!paging_range_is_valid(paging, window_base, window_length) ||
        !range_contains_page(window_base, window_length, virtual_page)) {
        return GXOS_VM_PAGING_STATUS_OUTSIDE_ARENA;
    }
    pml4 = (volatile uint64_t *)paging->root_alias;
    if (pml4 == 0) return GXOS_VM_PAGING_STATUS_INVALID_TABLE;
    entry = pml4[pml4_index(virtual_page)];
    if (!(entry & GXOS_X64_PAGING_ENTRY_PRESENT)) {
        return GXOS_VM_PAGING_STATUS_NOT_PRESENT;
    }
    status = table_alias(entry_address(entry),
                         paging->table_allocator.physical_alias,
                         paging->table_allocator.context, &pdpt);
    if (status != GXOS_VM_PAGING_STATUS_OK) return status;
    entry = pdpt[pdpt_index(virtual_page)];
    if (!(entry & GXOS_X64_PAGING_ENTRY_PRESENT)) {
        return GXOS_VM_PAGING_STATUS_NOT_PRESENT;
    }
    if (entry & GXOS_X64_PAGING_ENTRY_PAGE_SIZE) {
        return GXOS_VM_PAGING_STATUS_LARGE_PAGE;
    }
    status = table_alias(entry_address(entry),
                         paging->table_allocator.physical_alias,
                         paging->table_allocator.context, &pd);
    if (status != GXOS_VM_PAGING_STATUS_OK) return status;
    entry = pd[pd_index(virtual_page)];
    if (!(entry & GXOS_X64_PAGING_ENTRY_PRESENT)) {
        return GXOS_VM_PAGING_STATUS_NOT_PRESENT;
    }
    if (entry & GXOS_X64_PAGING_ENTRY_PAGE_SIZE) {
        return GXOS_VM_PAGING_STATUS_LARGE_PAGE;
    }
    status = table_alias(entry_address(entry),
                         paging->table_allocator.physical_alias,
                         paging->table_allocator.context, &pt);
    if (status != GXOS_VM_PAGING_STATUS_OK) return status;
    entry = pt[pt_index(virtual_page)];
    if (!(entry & GXOS_X64_PAGING_ENTRY_PRESENT)) {
        return GXOS_VM_PAGING_STATUS_NOT_PRESENT;
    }
    if (physical_page_out != 0) *physical_page_out = entry_address(entry);
    pt[pt_index(virtual_page)] = 0;
    invalidate_page(paging, virtual_page);
    return GXOS_VM_PAGING_STATUS_OK;
}

GXOS_VM_PAGING_STATUS gxos_vm_paging_unmap_page(
    GXOS_VM_PAGING *paging,
    uint64_t virtual_page,
    uint64_t *physical_page_out)
{
    return gxos_vm_paging_unmap_page_in_window(
        paging, virtual_page, physical_page_out,
        paging == 0 ? 0 : paging->arena_base,
        paging == 0 ? 0 : paging->arena_length);
}

static uint64_t round_down_page(uint64_t value)
{
    return value & ~(GXOS_VM_PAGE_SIZE - 1U);
}

static int round_up_page(uint64_t value, uint64_t *rounded)
{
    uint64_t adjusted;
    if (value > UINT64_MAX - (GXOS_VM_PAGE_SIZE - 1U)) return 0;
    adjusted = value + GXOS_VM_PAGE_SIZE - 1U;
    *rounded = adjusted & ~(GXOS_VM_PAGE_SIZE - 1U);
    return 1;
}

GXOS_VM_COMMIT_OPERATION_STATUS gxos_vm_commit_range(
    GXOS_VM_COMMIT_OPERATION *operation,
    uint32_t reservation_slot,
    uint64_t requested_start,
    uint64_t requested_bytes,
    uint32_t writable,
    uint32_t executable,
    uint32_t *new_page_count_out)
{
    GXOS_VM_CREATED_PAGE *created = g_vm_created_pages;
    uint64_t requested_end;
    uint64_t end;
    uint64_t reservation_end;
    uint64_t start_page;
    uint64_t end_page;
    uint64_t page_count;
    uint64_t index;
    uint32_t created_count = 0;
    GXOS_VM_RESERVATION *reservation;
    if (new_page_count_out != 0) *new_page_count_out = 0;
    if (operation == 0 || operation->arena == 0 || operation->paging == 0 ||
        operation->generation == 0 || operation->data_allocator.allocate_page == 0 ||
        operation->data_allocator.free_page == 0 || requested_bytes == 0 ||
        reservation_slot >= GXOS_VM_MAX_RESERVATIONS) {
        return GXOS_VM_COMMIT_OPERATION_INVALID_ARGUMENT;
    }
    reservation = &operation->arena->reservations[reservation_slot];
    if (!reservation->live || !range_end(requested_start, requested_bytes,
                                         &requested_end) ||
        !round_up_page(requested_end, &end_page)) {
        return GXOS_VM_COMMIT_OPERATION_OVERFLOW;
    }
    start_page = round_down_page(requested_start);
    if (end_page <= start_page ||
        !range_end(start_page, end_page - start_page, &end) ||
        !range_end(reservation->base, reservation->bytes, &reservation_end) ||
        start_page < reservation->base || end_page > reservation_end) {
        return GXOS_VM_COMMIT_OPERATION_OUTSIDE_RESERVATION;
    }
    page_count = (end_page - start_page) / GXOS_VM_PAGE_SIZE;
    if (page_count == 0 || page_count > GXOS_VM_MAX_COMMITMENTS) {
        return GXOS_VM_COMMIT_OPERATION_CAPACITY;
    }
    zero_bytes(created, sizeof(g_vm_created_pages));
    for (index = 0; index != page_count; ++index) {
        uint64_t virtual_page = start_page + index * GXOS_VM_PAGE_SIZE;
        uint32_t existing_slot;
        GXOS_VM_MAPPING mapping;
        GXOS_VM_PAGING_STATUS map_status;
        if (gxos_vm_arena_find_commitment(operation->arena, virtual_page,
                                          &existing_slot) == GXOS_VM_STATUS_OK) {
            const GXOS_VM_COMMITMENT *existing =
                &operation->arena->commitments[existing_slot];
            if (existing->base != virtual_page ||
                existing->physical_base == 0 ||
                gxos_vm_paging_query(operation->paging, virtual_page,
                                     &mapping) != GXOS_VM_PAGING_STATUS_OK ||
                mapping.page_size != GXOS_VM_PAGE_SIZE ||
                mapping.physical_base != existing->physical_base) {
                goto inconsistent;
            }
            continue;
        }
        if (!operation->data_allocator.allocate_page(
                operation->data_allocator.context,
                &created[created_count].physical_page,
                &created[created_count].alias) ||
            created[created_count].physical_page == 0 ||
            created[created_count].alias == 0) {
            if (created[created_count].physical_page != 0 ||
                created[created_count].alias != 0) {
                operation->data_allocator.free_page(
                    operation->data_allocator.context,
                    created[created_count].physical_page,
                    created[created_count].alias);
            }
            goto allocation_failure;
        }
        zero_bytes(created[created_count].alias, GXOS_VM_PAGE_SIZE);
        created[created_count].virtual_page = virtual_page;
        map_status = gxos_vm_paging_map_page(
            operation->paging, virtual_page,
            created[created_count].physical_page, writable, executable);
        if (map_status != GXOS_VM_PAGING_STATUS_OK) goto mapping_failure;
        if (gxos_vm_arena_commit_page(
                operation->arena, virtual_page,
                created[created_count].physical_page, operation->generation,
                0) != GXOS_VM_STATUS_OK) goto bookkeeping_failure;
        created_count++;
    }
    if (new_page_count_out != 0) *new_page_count_out = created_count;
    return GXOS_VM_COMMIT_OPERATION_OK;

bookkeeping_failure:
    (void)gxos_vm_paging_unmap_page(operation->paging,
                                    created[created_count].virtual_page, 0);
mapping_failure:
    operation->data_allocator.free_page(
        operation->data_allocator.context,
        created[created_count].physical_page,
        created[created_count].alias);
allocation_failure:
    while (created_count != 0) {
        --created_count;
        (void)gxos_vm_paging_unmap_page(operation->paging,
                                        created[created_count].virtual_page, 0);
        (void)gxos_vm_arena_decommit_page(
            operation->arena, created[created_count].virtual_page, 0);
        operation->data_allocator.free_page(
            operation->data_allocator.context,
            created[created_count].physical_page,
            created[created_count].alias);
    }
    return GXOS_VM_COMMIT_OPERATION_ALLOCATION;

inconsistent:
    while (created_count != 0) {
        --created_count;
        (void)gxos_vm_paging_unmap_page(operation->paging,
                                        created[created_count].virtual_page, 0);
        (void)gxos_vm_arena_decommit_page(
            operation->arena, created[created_count].virtual_page, 0);
        operation->data_allocator.free_page(
            operation->data_allocator.context,
            created[created_count].physical_page,
            created[created_count].alias);
    }
    return GXOS_VM_COMMIT_OPERATION_INCONSISTENT;
}

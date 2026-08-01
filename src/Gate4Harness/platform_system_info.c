#include "platform_system_info.h"

static GXOS_SYSTEM_FACTS g_system_info_facts;
static GXOS_SYSTEM_INFO_MEMORY_REGION g_system_info_regions[GXOS_SYSTEM_INFO_MAX_MEMORY_REGIONS];
static uint32_t g_system_info_configured;

static int gxos_system_info_is_canonical(uintptr_t address)
{
    uint64_t high = (uint64_t)address >> 47;
    return high == 0 || high == 0x1FFFFU;
}

static int gxos_system_info_is_power_of_two(uint32_t value)
{
    return value != 0 && (value & (value - 1U)) == 0;
}

static uint32_t gxos_system_info_population(uintptr_t value)
{
    uint32_t count = 0;
    while (value != 0) {
        value &= value - (uintptr_t)1U;
        count++;
    }
    return count;
}

static GXOS_SYSTEM_INFO_STATUS gxos_system_info_validate_range(
    uintptr_t minimum,
    uintptr_t maximum)
{
    if (minimum == 0 || maximum == 0 ||
        !gxos_system_info_is_canonical(minimum) ||
        !gxos_system_info_is_canonical(maximum) ||
        minimum > maximum) {
        return GXOS_SYSTEM_INFO_STATUS_INVALID_ADDRESS_RANGE;
    }
    return GXOS_SYSTEM_INFO_STATUS_OK;
}

static GXOS_SYSTEM_INFO_STATUS gxos_system_info_validate_facts(
    const GXOS_SYSTEM_FACTS *facts)
{
    GXOS_SYSTEM_INFO_STATUS status;
    uint32_t population;

    if (facts == 0) return GXOS_SYSTEM_INFO_STATUS_NULL_POINTER;
    if (facts->processor_architecture != GXOS_SYSTEM_INFO_PROCESSOR_ARCHITECTURE_AMD64) {
        return GXOS_SYSTEM_INFO_STATUS_INVALID_ARCHITECTURE;
    }
    if (!gxos_system_info_is_power_of_two(facts->page_size)) {
        return GXOS_SYSTEM_INFO_STATUS_INVALID_PAGE_SIZE;
    }
    if (!gxos_system_info_is_power_of_two(facts->allocation_granularity) ||
        facts->allocation_granularity < facts->page_size ||
        facts->allocation_granularity % facts->page_size != 0) {
        return GXOS_SYSTEM_INFO_STATUS_INVALID_ALLOCATION_GRANULARITY;
    }
    if (facts->number_of_processors == 0 || facts->number_of_processors > sizeof(uintptr_t) * 8U) {
        return GXOS_SYSTEM_INFO_STATUS_INVALID_PROCESSOR_COUNT;
    }
    if (facts->active_processor_mask == 0) {
        return GXOS_SYSTEM_INFO_STATUS_INVALID_PROCESSOR_MASK;
    }
    population = gxos_system_info_population(facts->active_processor_mask);
    if (population != facts->number_of_processors) {
        return GXOS_SYSTEM_INFO_STATUS_INVALID_PROCESSOR_MASK;
    }
    if (facts->address_range_policy != GXOS_SYSTEM_INFO_ADDRESS_RANGE_IMAGE_BACKED) {
        return GXOS_SYSTEM_INFO_STATUS_INVALID_ADDRESS_RANGE;
    }
    status = gxos_system_info_validate_range(
        facts->minimum_application_address,
        facts->maximum_application_address);
    if (status != GXOS_SYSTEM_INFO_STATUS_OK) return status;
    return GXOS_SYSTEM_INFO_STATUS_OK;
}

static GXOS_SYSTEM_INFO_STATUS gxos_system_info_validate_memory(
    const GXOS_SYSTEM_INFO_MEMORY_CONTEXT *memory)
{
    uint32_t index;

    if (memory == 0 || (memory->region_count != 0 && memory->regions == 0)) {
        return GXOS_SYSTEM_INFO_STATUS_INVALID_MEMORY_CONTEXT;
    }
    if (memory->region_count > GXOS_SYSTEM_INFO_MAX_MEMORY_REGIONS) {
        return GXOS_SYSTEM_INFO_STATUS_INVALID_MEMORY_CONTEXT;
    }
    for (index = 0; index != memory->region_count; index++) {
        const GXOS_SYSTEM_INFO_MEMORY_REGION *region = &memory->regions[index];
        if (region->base == 0 || region->end <= region->base ||
            !gxos_system_info_is_canonical(region->base) ||
            !gxos_system_info_is_canonical(region->end - (uintptr_t)1U)) {
            return GXOS_SYSTEM_INFO_STATUS_INVALID_MEMORY_CONTEXT;
        }
    }
    return GXOS_SYSTEM_INFO_STATUS_OK;
}

static GXOS_SYSTEM_INFO_STATUS gxos_system_info_validate_destination(
    const GXOS_SYSTEM_INFO *destination,
    const GXOS_SYSTEM_INFO_MEMORY_CONTEXT *memory)
{
    uintptr_t address;
    uintptr_t end;
    uint32_t index;

    if (destination == 0) return GXOS_SYSTEM_INFO_STATUS_NULL_POINTER;
    address = (uintptr_t)destination;
    if (!gxos_system_info_is_canonical(address)) {
        return GXOS_SYSTEM_INFO_STATUS_NONCANONICAL_POINTER;
    }
    if ((address & (_Alignof(GXOS_SYSTEM_INFO) - 1U)) != 0) {
        return GXOS_SYSTEM_INFO_STATUS_NONCANONICAL_POINTER;
    }
    if (address > UINTPTR_MAX - sizeof(GXOS_SYSTEM_INFO)) {
        return GXOS_SYSTEM_INFO_STATUS_INSUFFICIENT_WRITABLE_RANGE;
    }
    end = address + sizeof(GXOS_SYSTEM_INFO);
    if (end == 0 || !gxos_system_info_is_canonical(end - (uintptr_t)1U)) {
        return GXOS_SYSTEM_INFO_STATUS_NONCANONICAL_POINTER;
    }
    for (index = 0; index != memory->region_count; index++) {
        const GXOS_SYSTEM_INFO_MEMORY_REGION *region = &memory->regions[index];
        if (region->writable != 0 && address >= region->base && end <= region->end) {
            return GXOS_SYSTEM_INFO_STATUS_OK;
        }
    }
    return GXOS_SYSTEM_INFO_STATUS_UNWRITABLE_POINTER;
}

static void gxos_system_info_zero(uint8_t *destination, size_t size)
{
    while (size-- != 0) *destination++ = 0;
}

GXOS_SYSTEM_INFO_STATUS GXOS_SYSTEM_INFO_MS_ABI gxos_get_system_info_checked(
    GXOS_SYSTEM_INFO *destination,
    const GXOS_SYSTEM_FACTS *facts,
    const GXOS_SYSTEM_INFO_MEMORY_CONTEXT *memory)
{
    GXOS_SYSTEM_INFO_STATUS status;

    status = gxos_system_info_validate_facts(facts);
    if (status != GXOS_SYSTEM_INFO_STATUS_OK) return status;
    status = gxos_system_info_validate_memory(memory);
    if (status != GXOS_SYSTEM_INFO_STATUS_OK) return status;
    status = gxos_system_info_validate_destination(destination, memory);
    if (status != GXOS_SYSTEM_INFO_STATUS_OK) return status;

    gxos_system_info_zero((uint8_t *)destination, sizeof(*destination));
    destination->architecture_union.architecture.wProcessorArchitecture =
        facts->processor_architecture;
    destination->architecture_union.architecture.wReserved = 0;
    destination->dwPageSize = facts->page_size;
    destination->lpMinimumApplicationAddress =
        (void *)facts->minimum_application_address;
    destination->lpMaximumApplicationAddress =
        (void *)facts->maximum_application_address;
    destination->dwActiveProcessorMask = facts->active_processor_mask;
    destination->dwNumberOfProcessors = facts->number_of_processors;
    destination->dwProcessorType = facts->processor_type;
    destination->dwAllocationGranularity = facts->allocation_granularity;
    destination->wProcessorLevel = facts->processor_level;
    destination->wProcessorRevision = facts->processor_revision;
    return GXOS_SYSTEM_INFO_STATUS_OK;
}

GXOS_SYSTEM_INFO_STATUS GXOS_SYSTEM_INFO_MS_ABI gxos_system_info_configure(
    const GXOS_SYSTEM_FACTS *facts,
    const GXOS_SYSTEM_INFO_MEMORY_CONTEXT *memory)
{
    GXOS_SYSTEM_INFO_STATUS status;
    uint32_t index;

    status = gxos_system_info_validate_facts(facts);
    if (status != GXOS_SYSTEM_INFO_STATUS_OK) return status;
    status = gxos_system_info_validate_memory(memory);
    if (status != GXOS_SYSTEM_INFO_STATUS_OK) return status;
    g_system_info_facts = *facts;
    for (index = 0; index != memory->region_count; index++) {
        g_system_info_regions[index] = memory->regions[index];
    }
    g_system_info_facts.address_range_policy = facts->address_range_policy;
    g_system_info_configured = memory->region_count;
    return GXOS_SYSTEM_INFO_STATUS_OK;
}

GXOS_SYSTEM_INFO_STATUS GXOS_SYSTEM_INFO_MS_ABI gxos_system_info_get_snapshot(
    GXOS_SYSTEM_FACTS *facts_out)
{
    if (facts_out == 0) return GXOS_SYSTEM_INFO_STATUS_NULL_POINTER;
    if (g_system_info_configured == 0) return GXOS_SYSTEM_INFO_STATUS_INVALID_MEMORY_CONTEXT;
    *facts_out = g_system_info_facts;
    return GXOS_SYSTEM_INFO_STATUS_OK;
}

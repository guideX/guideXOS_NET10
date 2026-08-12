#include "platform_processor_topology.h"

static void zero_bytes(void *memory, size_t bytes)
{
    uint8_t *cursor = (uint8_t *)memory;
    while (bytes-- != 0) *cursor++ = 0;
}

static void copy_bytes(uint8_t *destination, const uint8_t *source, size_t bytes)
{
    while (bytes-- != 0) *destination++ = *source++;
}

static GXOS_LOGICAL_PROCESSOR_INFORMATION
    g_processor_topology_local_records[GXOS_PROCESSOR_TOPOLOGY_MAX_RECORDS];

static int is_canonical(uintptr_t address)
{
#if UINTPTR_MAX > 0xFFFFFFFFU
    uint64_t high = (uint64_t)address >> 47;
    return high == 0 || high == 0x1FFFFU;
#else
    (void)address;
    return 1;
#endif
}

static int range_end(uintptr_t base, uint64_t bytes, uintptr_t *end)
{
    if (bytes == 0 || bytes > UINTPTR_MAX ||
        base > UINTPTR_MAX - (uintptr_t)bytes) return 0;
    *end = base + (uintptr_t)bytes;
    return *end != 0;
}

static uint32_t population(uint64_t value)
{
    uint32_t count = 0;
    while (value != 0) {
        value &= value - 1U;
        ++count;
    }
    return count;
}

static int mask_is_valid(uint64_t mask, uint64_t active_mask)
{
    return mask != 0 && (mask & ~active_mask) == 0;
}

static int validate_partition_masks(const uint64_t *masks, uint32_t count,
                                    uint64_t active_mask)
{
    uint64_t covered = 0;
    uint32_t index;

    if (count == 0 || count > GXOS_PROCESSOR_TOPOLOGY_MAX_RELATIONSHIPS) {
        return 0;
    }
    for (index = 0; index != count; ++index) {
        if (!mask_is_valid(masks[index], active_mask) ||
            (covered & masks[index]) != 0) return 0;
        covered |= masks[index];
    }
    return covered == active_mask;
}

static GXOS_PROCESSOR_TOPOLOGY_STATUS validate_memory_context(
    const GXOS_MEMORY_STATUS_EX_CONTEXT *memory)
{
    uint32_t index;

    if (memory == 0 || memory->region_count > GXOS_MEMORY_STATUS_EX_MAX_MEMORY_REGIONS ||
        (memory->region_count != 0 && memory->regions == 0)) {
        return GXOS_PROCESSOR_TOPOLOGY_STATUS_INVALID_MEMORY_CONTEXT;
    }
    for (index = 0; index != memory->region_count; ++index) {
        const GXOS_MEMORY_STATUS_EX_MEMORY_REGION *region = &memory->regions[index];
        if (region->base == 0 || region->end <= region->base ||
            !is_canonical(region->base) ||
            !is_canonical(region->end - (uintptr_t)1U)) {
            return GXOS_PROCESSOR_TOPOLOGY_STATUS_INVALID_MEMORY_CONTEXT;
        }
    }
    return GXOS_PROCESSOR_TOPOLOGY_STATUS_OK;
}

static int range_is_accessible(const GXOS_MEMORY_STATUS_EX_CONTEXT *memory,
                               uintptr_t base, uint64_t bytes,
                               uint32_t require_readable,
                               uint32_t require_writable)
{
    uintptr_t end;
    uint32_t index;
    uint32_t region_seen = 0;

    if (memory == 0 || !range_end(base, bytes, &end) ||
        !is_canonical(base) || !is_canonical(end - (uintptr_t)1U)) {
        return 0;
    }
    for (index = 0; index != memory->region_count; ++index) {
        const GXOS_MEMORY_STATUS_EX_MEMORY_REGION *region = &memory->regions[index];
        if (base < region->end && region->base < end) {
            region_seen = 1;
            if (base < region->base || end > region->end ||
                (require_readable != 0 && region->readable == 0) ||
                (require_writable != 0 && region->writable == 0)) return 0;
            return 1;
        }
    }
    if (region_seen != 0) return 0;
    if (memory->ledger != 0 && gxos_physical_ledger_validate(memory->ledger)) {
        for (index = 0; index != GXOS_PHYSICAL_LEDGER_CAPACITY; ++index) {
            const GXOS_PHYSICAL_ALLOCATION *allocation =
                &memory->ledger->entries[index];
            uint64_t allocation_end;
            if (!allocation->live || allocation->bytes == 0 ||
                allocation->base > UINT64_MAX - allocation->bytes) continue;
            allocation_end = allocation->base + allocation->bytes;
            if ((uint64_t)base >= allocation->base &&
                (uint64_t)end <= allocation_end) return 1;
        }
    }
    if (memory->virtual_arena == 0 ||
        !gxos_vm_arena_validate(memory->virtual_arena)) return 0;
    for (index = 0; index != GXOS_VM_MAX_COMMITMENTS; ++index) {
        const GXOS_VM_COMMITMENT *commitment =
            &memory->virtual_arena->commitments[index];
        uint64_t commitment_end;
        if (!commitment->live || commitment->bytes == 0 ||
            commitment->base > UINT64_MAX - commitment->bytes) continue;
        commitment_end = commitment->base + commitment->bytes;
        if ((uint64_t)base >= commitment->base &&
            (uint64_t)end <= commitment_end) return 1;
    }
    return 0;
}

static GXOS_PROCESSOR_TOPOLOGY_STATUS validate_returned_length(
    uint32_t *returned_length,
    const GXOS_MEMORY_STATUS_EX_CONTEXT *memory,
    GXOS_PROCESSOR_TOPOLOGY_REPORT *report)
{
    uintptr_t address;

    if (returned_length == 0) {
        return GXOS_PROCESSOR_TOPOLOGY_STATUS_NULL_RETURNED_LENGTH;
    }
    address = (uintptr_t)returned_length;
    if (!is_canonical(address)) {
        return GXOS_PROCESSOR_TOPOLOGY_STATUS_NONCANONICAL_RETURNED_LENGTH;
    }
    if (report != 0) report->returned_length_pointer_canonical = 1;
    if (address > UINTPTR_MAX - sizeof(*returned_length)) {
        return GXOS_PROCESSOR_TOPOLOGY_STATUS_RETURNED_LENGTH_RANGE_OVERFLOW;
    }
    if (!is_canonical(address + sizeof(*returned_length) - (uintptr_t)1U)) {
        return GXOS_PROCESSOR_TOPOLOGY_STATUS_NONCANONICAL_RETURNED_LENGTH;
    }
    if (memory != 0) {
        uint32_t index;
        for (index = 0; index != memory->region_count; ++index) {
            const GXOS_MEMORY_STATUS_EX_MEMORY_REGION *region =
                &memory->regions[index];
            if (address >= region->base &&
                address + sizeof(*returned_length) <= region->end) {
                if (region->readable != 0 && report != 0) {
                    report->returned_length_pointer_readable = 1;
                }
                if (region->writable != 0 && report != 0) {
                    report->returned_length_pointer_writable = 1;
                }
            }
        }
    }
    if (!range_is_accessible(memory, address, sizeof(*returned_length), 1, 1)) {
        if (report != 0 && report->returned_length_pointer_readable == 0) {
            return GXOS_PROCESSOR_TOPOLOGY_STATUS_UNREADABLE_RETURNED_LENGTH;
        }
        return GXOS_PROCESSOR_TOPOLOGY_STATUS_UNWRITABLE_RETURNED_LENGTH;
    }
    return GXOS_PROCESSOR_TOPOLOGY_STATUS_OK;
}

GXOS_PROCESSOR_TOPOLOGY_STATUS GXOS_PROCESSOR_TOPOLOGY_MS_ABI
gxos_processor_topology_make_single_cpu(
    GXOS_PROCESSOR_TOPOLOGY_SNAPSHOT *snapshot, uint64_t generation)
{
    if (snapshot == 0) return GXOS_PROCESSOR_TOPOLOGY_STATUS_INVALID_SNAPSHOT;
    zero_bytes(snapshot, sizeof(*snapshot));
    snapshot->valid = 1;
    snapshot->generation = generation;
    snapshot->logical_processor_count = 1;
    snapshot->logical_processor_numbers[0] = 0;
    snapshot->active_processor_mask = 1;
    snapshot->core_count = 1;
    snapshot->cores[0].processor_mask = 1;
    snapshot->cores[0].flags = 0;
    snapshot->numa_node_count = 1;
    snapshot->numa_nodes[0].processor_mask = 1;
    snapshot->numa_nodes[0].node_number = 0;
    snapshot->package_count = 1;
    snapshot->packages[0].processor_mask = 1;
    snapshot->cache_count = 0;
    return gxos_processor_topology_validate(snapshot);
}

GXOS_PROCESSOR_TOPOLOGY_STATUS GXOS_PROCESSOR_TOPOLOGY_MS_ABI
gxos_processor_topology_validate(
    const GXOS_PROCESSOR_TOPOLOGY_SNAPSHOT *snapshot)
{
    uint64_t seen = 0;
    uint64_t core_masks[GXOS_PROCESSOR_TOPOLOGY_MAX_RELATIONSHIPS];
    uint64_t numa_masks[GXOS_PROCESSOR_TOPOLOGY_MAX_RELATIONSHIPS];
    uint64_t package_masks[GXOS_PROCESSOR_TOPOLOGY_MAX_RELATIONSHIPS];
    uint32_t index;

    if (snapshot == 0 || snapshot->valid == 0) {
        return GXOS_PROCESSOR_TOPOLOGY_STATUS_INVALID_SNAPSHOT;
    }
    if (snapshot->generation == 0) {
        return GXOS_PROCESSOR_TOPOLOGY_STATUS_INVALID_GENERATION;
    }
    if (snapshot->logical_processor_count == 0 ||
        snapshot->logical_processor_count >
            GXOS_PROCESSOR_TOPOLOGY_MAX_LOGICAL_PROCESSORS) {
        return GXOS_PROCESSOR_TOPOLOGY_STATUS_INVALID_LOGICAL_PROCESSOR_COUNT;
    }
    if (snapshot->active_processor_mask == 0 ||
        population(snapshot->active_processor_mask) !=
            snapshot->logical_processor_count) {
        return GXOS_PROCESSOR_TOPOLOGY_STATUS_INVALID_ACTIVE_PROCESSOR_MASK;
    }
    for (index = 0; index != snapshot->logical_processor_count; ++index) {
        uint32_t logical = snapshot->logical_processor_numbers[index];
        uint64_t bit;
        if (logical >= GXOS_PROCESSOR_TOPOLOGY_MAX_LOGICAL_PROCESSORS) {
            return GXOS_PROCESSOR_TOPOLOGY_STATUS_OUT_OF_RANGE_LOGICAL_PROCESSOR;
        }
        bit = (uint64_t)1U << logical;
        if ((seen & bit) != 0) {
            return GXOS_PROCESSOR_TOPOLOGY_STATUS_DUPLICATE_LOGICAL_PROCESSOR;
        }
        if ((snapshot->active_processor_mask & bit) == 0) {
            return GXOS_PROCESSOR_TOPOLOGY_STATUS_INVALID_ACTIVE_PROCESSOR_MASK;
        }
        seen |= bit;
    }
    if (seen != snapshot->active_processor_mask) {
        return GXOS_PROCESSOR_TOPOLOGY_STATUS_INVALID_ACTIVE_PROCESSOR_MASK;
    }
    if (snapshot->core_count == 0 || snapshot->core_count >
            GXOS_PROCESSOR_TOPOLOGY_MAX_RELATIONSHIPS ||
        snapshot->numa_node_count == 0 || snapshot->numa_node_count >
            GXOS_PROCESSOR_TOPOLOGY_MAX_RELATIONSHIPS ||
        snapshot->package_count == 0 || snapshot->package_count >
            GXOS_PROCESSOR_TOPOLOGY_MAX_RELATIONSHIPS ||
        snapshot->cache_count > GXOS_PROCESSOR_TOPOLOGY_MAX_RELATIONSHIPS) {
        return GXOS_PROCESSOR_TOPOLOGY_STATUS_RELATIONSHIP_CAPACITY;
    }
    for (index = 0; index != snapshot->core_count; ++index) {
        core_masks[index] = snapshot->cores[index].processor_mask;
    }
    if (!validate_partition_masks(core_masks, snapshot->core_count,
                                  snapshot->active_processor_mask)) {
        return GXOS_PROCESSOR_TOPOLOGY_STATUS_INVALID_CORE_RELATIONSHIPS;
    }
    for (index = 0; index != snapshot->numa_node_count; ++index) {
        uint32_t other;
        numa_masks[index] = snapshot->numa_nodes[index].processor_mask;
        for (other = 0; other != index; ++other) {
            if (snapshot->numa_nodes[other].node_number ==
                snapshot->numa_nodes[index].node_number) {
                return GXOS_PROCESSOR_TOPOLOGY_STATUS_INVALID_NUMA_RELATIONSHIPS;
            }
        }
    }
    if (!validate_partition_masks(numa_masks, snapshot->numa_node_count,
                                  snapshot->active_processor_mask)) {
        return GXOS_PROCESSOR_TOPOLOGY_STATUS_INVALID_NUMA_RELATIONSHIPS;
    }
    for (index = 0; index != snapshot->package_count; ++index) {
        package_masks[index] = snapshot->packages[index].processor_mask;
    }
    if (!validate_partition_masks(package_masks, snapshot->package_count,
                                  snapshot->active_processor_mask)) {
        return GXOS_PROCESSOR_TOPOLOGY_STATUS_INVALID_PACKAGE_RELATIONSHIPS;
    }
    for (index = 0; index != snapshot->cache_count; ++index) {
        if (!mask_is_valid(snapshot->caches[index].processor_mask,
                           snapshot->active_processor_mask)) {
            return GXOS_PROCESSOR_TOPOLOGY_STATUS_INVALID_CACHE_RELATIONSHIPS;
        }
    }
    return GXOS_PROCESSOR_TOPOLOGY_STATUS_OK;
}

GXOS_PROCESSOR_TOPOLOGY_STATUS GXOS_PROCESSOR_TOPOLOGY_MS_ABI
gxos_processor_topology_record_count(
    const GXOS_PROCESSOR_TOPOLOGY_SNAPSHOT *snapshot,
    uint32_t *record_count)
{
    uint64_t total;
    GXOS_PROCESSOR_TOPOLOGY_STATUS status;

    if (record_count == 0) {
        return GXOS_PROCESSOR_TOPOLOGY_STATUS_INVALID_RECORD_STORAGE;
    }
    *record_count = 0;
    status = gxos_processor_topology_validate(snapshot);
    if (status != GXOS_PROCESSOR_TOPOLOGY_STATUS_OK) return status;
    total = (uint64_t)snapshot->core_count + snapshot->numa_node_count +
            snapshot->package_count + snapshot->cache_count;
    if (total > UINT32_MAX) {
        return GXOS_PROCESSOR_TOPOLOGY_STATUS_RECORD_COUNT_OVERFLOW;
    }
    *record_count = (uint32_t)total;
    return GXOS_PROCESSOR_TOPOLOGY_STATUS_OK;
}

int GXOS_PROCESSOR_TOPOLOGY_MS_ABI gxos_processor_topology_required_size(
    uint64_t record_count, size_t *required_size)
{
    if (required_size == 0 ||
        record_count > (uint64_t)(SIZE_MAX /
                                  sizeof(GXOS_LOGICAL_PROCESSOR_INFORMATION))) {
        return 0;
    }
    *required_size = (size_t)record_count *
                     sizeof(GXOS_LOGICAL_PROCESSOR_INFORMATION);
    return 1;
}

GXOS_PROCESSOR_TOPOLOGY_STATUS GXOS_PROCESSOR_TOPOLOGY_MS_ABI
gxos_processor_topology_build_records(
    const GXOS_PROCESSOR_TOPOLOGY_SNAPSHOT *snapshot,
    GXOS_LOGICAL_PROCESSOR_INFORMATION *records,
    uint32_t record_capacity,
    uint32_t *record_count)
{
    GXOS_PROCESSOR_TOPOLOGY_STATUS status;
    uint32_t required_count;
    uint32_t index;

    if (records == 0 || record_count == 0) {
        return GXOS_PROCESSOR_TOPOLOGY_STATUS_INVALID_RECORD_STORAGE;
    }
    *record_count = 0;
    status = gxos_processor_topology_record_count(snapshot, &required_count);
    if (status != GXOS_PROCESSOR_TOPOLOGY_STATUS_OK) return status;
    if (record_capacity < required_count) {
        return GXOS_PROCESSOR_TOPOLOGY_STATUS_INVALID_RECORD_STORAGE;
    }
    index = 0;
    for (uint32_t relation = 0; relation != snapshot->core_count; ++relation) {
        GXOS_LOGICAL_PROCESSOR_INFORMATION *record = &records[index++];
        zero_bytes(record, sizeof(*record));
        record->processor_mask = snapshot->cores[relation].processor_mask;
        record->relationship = GXOS_RELATION_PROCESSOR_CORE;
        record->relationship_info.processor_core.flags =
            snapshot->cores[relation].flags;
    }
    for (uint32_t relation = 0; relation != snapshot->numa_node_count; ++relation) {
        GXOS_LOGICAL_PROCESSOR_INFORMATION *record = &records[index++];
        zero_bytes(record, sizeof(*record));
        record->processor_mask = snapshot->numa_nodes[relation].processor_mask;
        record->relationship = GXOS_RELATION_NUMA_NODE;
        record->relationship_info.numa_node.node_number =
            snapshot->numa_nodes[relation].node_number;
    }
    for (uint32_t relation = 0; relation != snapshot->package_count; ++relation) {
        GXOS_LOGICAL_PROCESSOR_INFORMATION *record = &records[index++];
        zero_bytes(record, sizeof(*record));
        record->processor_mask = snapshot->packages[relation].processor_mask;
        record->relationship = GXOS_RELATION_PROCESSOR_PACKAGE;
    }
    for (uint32_t relation = 0; relation != snapshot->cache_count; ++relation) {
        GXOS_LOGICAL_PROCESSOR_INFORMATION *record = &records[index++];
        const GXOS_PROCESSOR_TOPOLOGY_CACHE_RELATIONSHIP *cache =
            &snapshot->caches[relation];
        zero_bytes(record, sizeof(*record));
        record->processor_mask = cache->processor_mask;
        record->relationship = GXOS_RELATION_CACHE;
        record->relationship_info.cache.level = cache->level;
        record->relationship_info.cache.associativity = cache->associativity;
        record->relationship_info.cache.line_size = cache->line_size;
        record->relationship_info.cache.size = cache->size;
        record->relationship_info.cache.type = cache->type;
    }
    *record_count = required_count;
    return GXOS_PROCESSOR_TOPOLOGY_STATUS_OK;
}

GXOS_PROCESSOR_TOPOLOGY_STATUS GXOS_PROCESSOR_TOPOLOGY_MS_ABI
gxos_get_logical_processor_information_checked(
    GXOS_LOGICAL_PROCESSOR_INFORMATION *buffer,
    uint32_t *returned_length,
    const GXOS_PROCESSOR_TOPOLOGY_SNAPSHOT *snapshot,
    const GXOS_MEMORY_STATUS_EX_CONTEXT *memory,
    GXOS_PROCESSOR_TOPOLOGY_REPORT *report)
{
    GXOS_PROCESSOR_TOPOLOGY_STATUS status;
    uint32_t input_length;
    uint32_t record_count;
    size_t required_size;
    uintptr_t buffer_address = (uintptr_t)buffer;

    if (report != 0) {
        zero_bytes(report, sizeof(*report));
        report->buffer = buffer_address;
        report->returned_length = (uintptr_t)returned_length;
    }
    status = gxos_processor_topology_validate(snapshot);
    if (status != GXOS_PROCESSOR_TOPOLOGY_STATUS_OK) goto failure;
    status = validate_memory_context(memory);
    if (status != GXOS_PROCESSOR_TOPOLOGY_STATUS_OK) goto failure;
    status = validate_returned_length(returned_length, memory, report);
    if (status != GXOS_PROCESSOR_TOPOLOGY_STATUS_OK) goto failure;

    input_length = *returned_length;
    if (report != 0) {
        report->input_length_read = 1;
        report->input_length = input_length;
    }
    status = gxos_processor_topology_record_count(snapshot, &record_count);
    if (status != GXOS_PROCESSOR_TOPOLOGY_STATUS_OK) goto failure;
    if (!gxos_processor_topology_required_size(record_count, &required_size) ||
        required_size > UINT32_MAX) {
        status = GXOS_PROCESSOR_TOPOLOGY_STATUS_SIZE_OVERFLOW;
        goto failure;
    }
    if (report != 0) {
        report->required_length = (uint32_t)required_size;
        report->record_count = record_count;
        report->cache_record_count = snapshot->cache_count;
        report->snapshot_generation = snapshot->generation;
    }
    if (buffer == 0 || input_length < (uint32_t)required_size) {
        *returned_length = (uint32_t)required_size;
        if (report != 0) {
            report->status = GXOS_PROCESSOR_TOPOLOGY_STATUS_INSUFFICIENT_BUFFER;
            report->return_value = 0;
        }
        return GXOS_PROCESSOR_TOPOLOGY_STATUS_INSUFFICIENT_BUFFER;
    }
    status = gxos_processor_topology_build_records(
        snapshot, g_processor_topology_local_records,
        GXOS_PROCESSOR_TOPOLOGY_MAX_RECORDS,
        &record_count);
    if (status != GXOS_PROCESSOR_TOPOLOGY_STATUS_OK) goto failure;
    if (!is_canonical(buffer_address)) {
        status = GXOS_PROCESSOR_TOPOLOGY_STATUS_NONCANONICAL_BUFFER;
        goto failure;
    }
    if (report != 0) report->buffer_pointer_canonical = 1;
    if (!range_end(buffer_address, required_size, &buffer_address)) {
        status = GXOS_PROCESSOR_TOPOLOGY_STATUS_BUFFER_RANGE_OVERFLOW;
        goto failure;
    }
    buffer_address = (uintptr_t)buffer;
    if (!range_is_accessible(memory, buffer_address, required_size, 0, 1)) {
        status = GXOS_PROCESSOR_TOPOLOGY_STATUS_UNWRITABLE_BUFFER;
        goto failure;
    }
    if (validate_returned_length(returned_length, memory, 0) !=
        GXOS_PROCESSOR_TOPOLOGY_STATUS_OK) {
        status = GXOS_PROCESSOR_TOPOLOGY_STATUS_UNWRITABLE_RETURNED_LENGTH;
        goto failure;
    }
    copy_bytes((uint8_t *)buffer,
               (const uint8_t *)g_processor_topology_local_records,
               required_size);
    *returned_length = (uint32_t)required_size;
    if (report != 0) {
        report->status = GXOS_PROCESSOR_TOPOLOGY_STATUS_OK;
        report->buffer_range_valid = 1;
        report->output_written = 1;
        report->return_value = 1;
    }
    return GXOS_PROCESSOR_TOPOLOGY_STATUS_OK;

failure:
    if (report != 0) report->status = status;
    return status;
}

GXOS_PROCESSOR_TOPOLOGY_STATUS GXOS_PROCESSOR_TOPOLOGY_MS_ABI
gxos_processor_topology_status_last_error(
    GXOS_PROCESSOR_TOPOLOGY_STATUS status, uint32_t *last_error)
{
    if (last_error == 0) return GXOS_PROCESSOR_TOPOLOGY_STATUS_INVALID_RECORD_STORAGE;
    if (status == GXOS_PROCESSOR_TOPOLOGY_STATUS_INSUFFICIENT_BUFFER) {
        *last_error = GXOS_PROCESSOR_TOPOLOGY_ERROR_INSUFFICIENT_BUFFER;
    } else {
        *last_error = GXOS_PROCESSOR_TOPOLOGY_ERROR_INVALID_PARAMETER;
    }
    return GXOS_PROCESSOR_TOPOLOGY_STATUS_OK;
}

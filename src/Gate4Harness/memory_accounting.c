#include "memory_accounting.h"

static void zero_bytes(void *memory, size_t bytes)
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

static int multiply_u64(uint64_t left, uint64_t right, uint64_t *result)
{
    if (left != 0 && right > UINT64_MAX / left) return 0;
    *result = left * right;
    return 1;
}

static int range_end(uint64_t base, uint64_t bytes, uint64_t *end)
{
    if (bytes == 0 || base > UINT64_MAX - bytes) return 0;
    *end = base + bytes;
    return 1;
}

static int ranges_overlap(uint64_t left_base,
                          uint64_t left_bytes,
                          uint64_t right_base,
                          uint64_t right_bytes)
{
    uint64_t left_end;
    uint64_t right_end;
    if (!range_end(left_base, left_bytes, &left_end) ||
        !range_end(right_base, right_bytes, &right_end)) {
        return 1;
    }
    return left_base < right_end && right_base < left_end;
}

static int range_contains(uint64_t outer_base,
                          uint64_t outer_bytes,
                          uint64_t inner_base,
                          uint64_t inner_bytes)
{
    uint64_t outer_end;
    uint64_t inner_end;
    if (!range_end(outer_base, outer_bytes, &outer_end) ||
        !range_end(inner_base, inner_bytes, &inner_end)) {
        return 0;
    }
    return inner_base >= outer_base && inner_end <= outer_end;
}

static int align_up_u64(uint64_t value, uint64_t alignment,
                        uint64_t *result)
{
    uint64_t remainder;
    uint64_t increment;
    if (result == 0 || alignment == 0) return 0;
    remainder = value % alignment;
    increment = remainder == 0 ? 0 : alignment - remainder;
    return add_u64(value, increment, result);
}

static uint64_t page_round_bytes(uint64_t bytes)
{
    uint64_t rounded;
    if (bytes == 0 || bytes > UINT64_MAX - (GXOS_VM_PAGE_SIZE - 1U)) {
        return 0;
    }
    rounded = bytes + GXOS_VM_PAGE_SIZE - 1U;
    return rounded & ~(GXOS_VM_PAGE_SIZE - 1U);
}

static uint64_t map_growth_size(uint64_t map_bytes, uint64_t descriptor_size,
                                int *ok)
{
    uint64_t slack;
    *ok = 0;
    if (descriptor_size < sizeof(EFI_MEMORY_DESCRIPTOR) ||
        descriptor_size > GXOS_MEMORY_MAP_MAX_BYTES ||
        !multiply_u64(descriptor_size, GXOS_MEMORY_MAP_GROWTH_SLACK_DESCRIPTORS,
                      &slack) || !add_u64(map_bytes, slack, &slack) ||
        slack > GXOS_MEMORY_MAP_MAX_BYTES) {
        return 0;
    }
    *ok = 1;
    return slack;
}

int gxos_uefi_memory_map_parse(const void *memory_map,
                               uint64_t map_bytes,
                               uint64_t descriptor_size,
                               uint32_t *descriptor_count_out)
{
    uint64_t count;
    if (descriptor_count_out == 0 || descriptor_size < sizeof(EFI_MEMORY_DESCRIPTOR) ||
        descriptor_size > GXOS_MEMORY_MAP_MAX_BYTES || map_bytes > GXOS_MEMORY_MAP_MAX_BYTES) {
        return 0;
    }
    if (map_bytes == 0) {
        *descriptor_count_out = 0;
        return 1;
    }
    if (memory_map == 0 || map_bytes % descriptor_size != 0) return 0;
    count = map_bytes / descriptor_size;
    if (count == 0 || count > GXOS_MEMORY_MAP_MAX_DESCRIPTORS ||
        count > UINT32_MAX) {
        return 0;
    }
    *descriptor_count_out = (uint32_t)count;
    return 1;
}

GXOS_MEMORY_MAP_STATUS gxos_uefi_memory_map_acquire(
    GXOS_UEFI_MEMORY_MAP *map,
    GXOS_EFI_GET_MEMORY_MAP get_memory_map,
    GXOS_EFI_ALLOCATE_POOL allocate_pool,
    GXOS_EFI_FREE_POOL free_pool)
{
    GXOS_EFI_UINTN map_bytes = 0;
    GXOS_EFI_UINTN map_key = 0;
    GXOS_EFI_UINTN descriptor_size = 0;
    uint32_t descriptor_version = 0;
    GXOS_EFI_STATUS status;
    uint8_t *backing = 0;
    uint64_t backing_bytes = 0;
    uint32_t retry;

    if (map == 0 || get_memory_map == 0 || allocate_pool == 0 || free_pool == 0) {
        return GXOS_MEMORY_MAP_STATUS_INVALID_ARGUMENT;
    }
    zero_bytes(map, sizeof(*map));
    status = get_memory_map(&map_bytes, 0, &map_key, &descriptor_size,
                            &descriptor_version);
    if (status != GXOS_EFI_BUFFER_TOO_SMALL && status != GXOS_EFI_SUCCESS) {
        return GXOS_MEMORY_MAP_STATUS_FIRMWARE_QUERY;
    }
    if (status == GXOS_EFI_SUCCESS && map_bytes == 0) {
        map->descriptor_size = descriptor_size == 0
            ? sizeof(EFI_MEMORY_DESCRIPTOR) : descriptor_size;
        map->descriptor_version = descriptor_version;
        map->map_key = map_key;
        map->generation = 1;
        map->valid = 1;
        return GXOS_MEMORY_MAP_STATUS_OK;
    }
    if (map_bytes == 0) return GXOS_MEMORY_MAP_STATUS_MALFORMED;
    if (descriptor_size == 0) descriptor_size = sizeof(EFI_MEMORY_DESCRIPTOR);

    for (retry = 0; retry != GXOS_MEMORY_MAP_MAX_RETRIES; ++retry) {
        uint64_t required;
        int size_ok;
        uint64_t returned_bytes;

        required = map_growth_size(map_bytes, descriptor_size, &size_ok);
        if (!size_ok) return GXOS_MEMORY_MAP_STATUS_OVERFLOW;
        if (backing != 0) {
            if (free_pool(backing) != GXOS_EFI_SUCCESS) {
                return GXOS_MEMORY_MAP_STATUS_ALLOCATION;
            }
            backing = 0;
            backing_bytes = 0;
        }
        if (allocate_pool(GXOS_EFI_LOADER_DATA, required, (void **)&backing) !=
                GXOS_EFI_SUCCESS || backing == 0) {
            return GXOS_MEMORY_MAP_STATUS_ALLOCATION;
        }
        backing_bytes = required;
        returned_bytes = required;
        map_key = 0;
        descriptor_size = 0;
        descriptor_version = 0;
        status = get_memory_map(&returned_bytes, backing, &map_key,
                                &descriptor_size, &descriptor_version);
        if (status == GXOS_EFI_BUFFER_TOO_SMALL) {
            if (returned_bytes <= required) {
                (void)free_pool(backing);
                return GXOS_MEMORY_MAP_STATUS_RETRY_EXHAUSTED;
            }
            map_bytes = returned_bytes;
            if (descriptor_size == 0) descriptor_size = sizeof(EFI_MEMORY_DESCRIPTOR);
            continue;
        }
        if (status != GXOS_EFI_SUCCESS) {
            (void)free_pool(backing);
            return GXOS_MEMORY_MAP_STATUS_FIRMWARE_QUERY;
        }
        if (returned_bytes > backing_bytes ||
            !gxos_uefi_memory_map_parse(backing, returned_bytes, descriptor_size,
                                         &map->descriptor_count)) {
            (void)free_pool(backing);
            return GXOS_MEMORY_MAP_STATUS_MALFORMED;
        }
        map->backing = backing;
        map->backing_bytes = backing_bytes;
        map->map_bytes = returned_bytes;
        map->descriptor_size = descriptor_size;
        map->descriptor_version = descriptor_version;
        map->map_key = map_key;
        map->generation = 1;
        map->valid = 1;
        return GXOS_MEMORY_MAP_STATUS_OK;
    }
    if (backing != 0) (void)free_pool(backing);
    return GXOS_MEMORY_MAP_STATUS_RETRY_EXHAUSTED;
}

const EFI_MEMORY_DESCRIPTOR *gxos_uefi_memory_map_descriptor(
    const GXOS_UEFI_MEMORY_MAP *map,
    uint32_t index)
{
    uint64_t offset;
    if (map == 0 || !map->valid || map->backing == 0 ||
        index >= map->descriptor_count ||
        !multiply_u64(index, map->descriptor_size, &offset) ||
        offset > map->map_bytes ||
        sizeof(EFI_MEMORY_DESCRIPTOR) > map->map_bytes - offset ||
        offset > map->backing_bytes ||
        sizeof(EFI_MEMORY_DESCRIPTOR) > map->backing_bytes - offset) {
        return 0;
    }
    return (const EFI_MEMORY_DESCRIPTOR *)(const void *)(map->backing + offset);
}

GXOS_MEMORY_CLASS gxos_memory_class_for_efi_type(uint32_t type)
{
    switch (type) {
    case GXOS_EFI_CONVENTIONAL_MEMORY_TYPE: return GXOS_MEMORY_CLASS_CONVENTIONAL;
    case GXOS_EFI_LOADER_CODE_MEMORY_TYPE: return GXOS_MEMORY_CLASS_LOADER_CODE;
    case GXOS_EFI_LOADER_DATA_MEMORY_TYPE: return GXOS_MEMORY_CLASS_LOADER_DATA;
    case GXOS_EFI_BOOT_SERVICES_CODE_MEMORY_TYPE:
        return GXOS_MEMORY_CLASS_BOOT_SERVICES_CODE;
    case GXOS_EFI_BOOT_SERVICES_DATA_MEMORY_TYPE:
        return GXOS_MEMORY_CLASS_BOOT_SERVICES_DATA;
    case GXOS_EFI_RUNTIME_SERVICES_CODE_MEMORY_TYPE:
        return GXOS_MEMORY_CLASS_RUNTIME_SERVICES_CODE;
    case GXOS_EFI_RUNTIME_SERVICES_DATA_MEMORY_TYPE:
        return GXOS_MEMORY_CLASS_RUNTIME_SERVICES_DATA;
    case GXOS_EFI_ACPI_RECLAIM_MEMORY_TYPE: return GXOS_MEMORY_CLASS_ACPI_RECLAIM;
    case GXOS_EFI_ACPI_NVS_MEMORY_TYPE: return GXOS_MEMORY_CLASS_ACPI_NVS;
    case GXOS_EFI_RESERVED_MEMORY_TYPE: return GXOS_MEMORY_CLASS_RESERVED;
    case GXOS_EFI_UNUSABLE_MEMORY_TYPE: return GXOS_MEMORY_CLASS_UNUSABLE;
    case GXOS_EFI_MEMORY_MAPPED_IO_MEMORY_TYPE: return GXOS_MEMORY_CLASS_MMIO;
    case GXOS_EFI_MEMORY_MAPPED_IO_PORT_SPACE_TYPE:
        return GXOS_MEMORY_CLASS_MMIO_PORT_SPACE;
    case GXOS_EFI_PERSISTENT_MEMORY_TYPE: return GXOS_MEMORY_CLASS_PERSISTENT;
    case GXOS_EFI_PAL_CODE_MEMORY_TYPE: return GXOS_MEMORY_CLASS_PAL_CODE;
    default: return GXOS_MEMORY_CLASS_UNKNOWN;
    }
}

const char *gxos_memory_class_name(GXOS_MEMORY_CLASS memory_class)
{
    static const char *const names[GXOS_MEMORY_CLASS_COUNT] = {
        "CONVENTIONAL", "LOADER_CODE", "LOADER_DATA", "BOOT_SERVICES_CODE",
        "BOOT_SERVICES_DATA", "RUNTIME_SERVICES_CODE", "RUNTIME_SERVICES_DATA",
        "ACPI_RECLAIM", "ACPI_NVS", "RESERVED", "UNUSABLE", "MMIO",
        "MMIO_PORT_SPACE", "PERSISTENT", "PAL_CODE", "UNKNOWN"
    };
    if ((uint32_t)memory_class >= GXOS_MEMORY_CLASS_COUNT) return "INVALID";
    return names[memory_class];
}

int gxos_memory_class_is_ram_like(GXOS_MEMORY_CLASS memory_class)
{
    return memory_class == GXOS_MEMORY_CLASS_CONVENTIONAL ||
           memory_class == GXOS_MEMORY_CLASS_LOADER_CODE ||
           memory_class == GXOS_MEMORY_CLASS_LOADER_DATA ||
           memory_class == GXOS_MEMORY_CLASS_BOOT_SERVICES_CODE ||
           memory_class == GXOS_MEMORY_CLASS_BOOT_SERVICES_DATA ||
           memory_class == GXOS_MEMORY_CLASS_RUNTIME_SERVICES_CODE ||
           memory_class == GXOS_MEMORY_CLASS_RUNTIME_SERVICES_DATA ||
           memory_class == GXOS_MEMORY_CLASS_ACPI_RECLAIM ||
           memory_class == GXOS_MEMORY_CLASS_ACPI_NVS ||
           memory_class == GXOS_MEMORY_CLASS_PERSISTENT ||
           memory_class == GXOS_MEMORY_CLASS_PAL_CODE;
}

GXOS_MEMORY_CLASSIFICATION_STATUS gxos_uefi_memory_map_classify(
    const GXOS_UEFI_MEMORY_MAP *map,
    GXOS_MEMORY_CLASSIFICATION *classification)
{
    uint32_t index;
    if (map == 0 || classification == 0 || !map->valid) {
        return GXOS_MEMORY_CLASSIFICATION_INVALID_ARGUMENT;
    }
    zero_bytes(classification, sizeof(*classification));
    for (index = 0; index != map->descriptor_count; ++index) {
        const EFI_MEMORY_DESCRIPTOR *descriptor =
            gxos_uefi_memory_map_descriptor(map, index);
        GXOS_MEMORY_CLASS memory_class;
        uint64_t bytes;
        uint64_t end;
        if (descriptor == 0 || descriptor->NumberOfPages == 0 ||
            !multiply_u64(descriptor->NumberOfPages, GXOS_MEMORY_PAGE_SIZE, &bytes) ||
            !add_u64(descriptor->PhysicalStart, bytes, &end)) {
            return descriptor == 0 || descriptor->NumberOfPages == 0
                       ? GXOS_MEMORY_CLASSIFICATION_MALFORMED
                                   : GXOS_MEMORY_CLASSIFICATION_OVERFLOW;
        }
        {
            uint32_t prior;
            for (prior = 0; prior != index; ++prior) {
                const EFI_MEMORY_DESCRIPTOR *previous =
                    gxos_uefi_memory_map_descriptor(map, prior);
                uint64_t previous_bytes;
                uint64_t previous_end;
                if (previous == 0 || previous->NumberOfPages == 0 ||
                    !multiply_u64(previous->NumberOfPages,
                                  GXOS_MEMORY_PAGE_SIZE, &previous_bytes) ||
                    !add_u64(previous->PhysicalStart, previous_bytes,
                             &previous_end) ||
                    (descriptor->PhysicalStart < previous_end &&
                     previous->PhysicalStart < end)) {
                    return GXOS_MEMORY_CLASSIFICATION_MALFORMED;
                }
            }
        }
        memory_class = gxos_memory_class_for_efi_type(descriptor->Type);
        if (!add_u64(classification->class_bytes[memory_class], bytes,
                     &classification->class_bytes[memory_class]) ||
            !add_u64(classification->class_pages[memory_class],
                     descriptor->NumberOfPages,
                     &classification->class_pages[memory_class])) {
            return GXOS_MEMORY_CLASSIFICATION_OVERFLOW;
        }
        if (memory_class == GXOS_MEMORY_CLASS_CONVENTIONAL &&
            !add_u64(classification->conventional_bytes, bytes,
                     &classification->conventional_bytes)) {
            return GXOS_MEMORY_CLASSIFICATION_OVERFLOW;
        }
        if (gxos_memory_class_is_ram_like(memory_class) &&
            !add_u64(classification->total_ram_like_bytes, bytes,
                     &classification->total_ram_like_bytes)) {
            return GXOS_MEMORY_CLASSIFICATION_OVERFLOW;
        }
    }
    classification->descriptor_count = map->descriptor_count;
    classification->valid = 1;
    return GXOS_MEMORY_CLASSIFICATION_OK;
}

void gxos_physical_ledger_init(GXOS_PHYSICAL_LEDGER *ledger,
                               uint64_t generation)
{
    if (ledger == 0) return;
    zero_bytes(ledger, sizeof(*ledger));
    ledger->generation = generation;
}

GXOS_LEDGER_STATUS gxos_physical_ledger_insert(
    GXOS_PHYSICAL_LEDGER *ledger,
    const GXOS_PHYSICAL_ALLOCATION *allocation,
    uint32_t *slot_out)
{
    uint32_t index;
    uint64_t ignored_end;
    if (ledger == 0 || allocation == 0 || slot_out == 0 ||
        allocation->generation == 0 || allocation->bytes == 0 ||
        allocation->allocation_class >= GXOS_MEMORY_ALLOCATION_COUNT ||
        allocation->owner >= GXOS_MEMORY_OWNER_COUNT) {
        return allocation != 0 && allocation->bytes == 0
            ? GXOS_LEDGER_STATUS_ZERO_LENGTH : GXOS_LEDGER_STATUS_INVALID_ARGUMENT;
    }
    if (allocation->base != 0 && !range_end(allocation->base, allocation->bytes,
                                             &ignored_end)) {
        return GXOS_LEDGER_STATUS_OVERFLOW;
    }
    if (allocation->pages != 0) {
        uint64_t page_bytes;
        if (!multiply_u64(allocation->pages, GXOS_MEMORY_PAGE_SIZE, &page_bytes) ||
            page_bytes < allocation->bytes) {
            return GXOS_LEDGER_STATUS_OVERFLOW;
        }
    }
    for (index = 0; index != GXOS_PHYSICAL_LEDGER_CAPACITY; ++index) {
        const GXOS_PHYSICAL_ALLOCATION *existing = &ledger->entries[index];
        if (!existing->live) continue;
        if (allocation->base != 0 && existing->base != 0 &&
            ranges_overlap(allocation->base, allocation->bytes,
                           existing->base, existing->bytes)) {
            return GXOS_LEDGER_STATUS_OVERLAP;
        }
    }
    if (ledger->live_count >= GXOS_PHYSICAL_LEDGER_CAPACITY) {
        ledger->exhausted = 1;
        return GXOS_LEDGER_STATUS_CAPACITY;
    }
    if (!add_u64(ledger->physical_bytes, allocation->physical_impact_bytes,
                 &ignored_end) ||
        !add_u64(ledger->commit_bytes, allocation->commit_impact_bytes,
                 &ignored_end) ||
        !add_u64(ledger->virtual_reservation_bytes,
                 allocation->virtual_reservation_impact_bytes, &ignored_end)) {
        return GXOS_LEDGER_STATUS_OVERFLOW;
    }
    for (index = 0; index != GXOS_PHYSICAL_LEDGER_CAPACITY; ++index) {
        if (!ledger->entries[index].live) {
            ledger->entries[index] = *allocation;
            ledger->entries[index].live = 1;
            ledger->live_count++;
            ledger->physical_bytes += allocation->physical_impact_bytes;
            ledger->commit_bytes += allocation->commit_impact_bytes;
            ledger->virtual_reservation_bytes +=
                allocation->virtual_reservation_impact_bytes;
            *slot_out = index;
            return GXOS_LEDGER_STATUS_OK;
        }
    }
    ledger->exhausted = 1;
    return GXOS_LEDGER_STATUS_CAPACITY;
}

GXOS_LEDGER_STATUS gxos_physical_ledger_remove(
    GXOS_PHYSICAL_LEDGER *ledger,
    uint32_t slot)
{
    GXOS_PHYSICAL_ALLOCATION *allocation;
    if (ledger == 0 || slot >= GXOS_PHYSICAL_LEDGER_CAPACITY) {
        return GXOS_LEDGER_STATUS_INVALID_ARGUMENT;
    }
    allocation = &ledger->entries[slot];
    if (!allocation->live || ledger->live_count == 0 ||
        ledger->physical_bytes < allocation->physical_impact_bytes ||
        ledger->commit_bytes < allocation->commit_impact_bytes ||
        ledger->virtual_reservation_bytes <
            allocation->virtual_reservation_impact_bytes) {
        return GXOS_LEDGER_STATUS_INVALID_STATE;
    }
    ledger->physical_bytes -= allocation->physical_impact_bytes;
    ledger->commit_bytes -= allocation->commit_impact_bytes;
    ledger->virtual_reservation_bytes -= allocation->virtual_reservation_impact_bytes;
    zero_bytes(allocation, sizeof(*allocation));
    ledger->live_count--;
    return GXOS_LEDGER_STATUS_OK;
}

int gxos_physical_ledger_find(const GXOS_PHYSICAL_LEDGER *ledger,
                              uint64_t base,
                              uint64_t bytes,
                              uint32_t *slot_out)
{
    uint32_t index;
    if (ledger == 0 || slot_out == 0 || base == 0 || bytes == 0) return 0;
    for (index = 0; index != GXOS_PHYSICAL_LEDGER_CAPACITY; ++index) {
        if (ledger->entries[index].live && ledger->entries[index].base == base &&
            ledger->entries[index].bytes == bytes) {
            *slot_out = index;
            return 1;
        }
    }
    return 0;
}

int gxos_physical_ledger_validate(const GXOS_PHYSICAL_LEDGER *ledger)
{
    uint32_t index;
    uint32_t other;
    uint32_t live_count = 0;
    uint64_t physical = 0;
    uint64_t commit = 0;
    uint64_t virtual_bytes = 0;
    if (ledger == 0 || ledger->generation == 0) return 0;
    for (index = 0; index != GXOS_PHYSICAL_LEDGER_CAPACITY; ++index) {
        const GXOS_PHYSICAL_ALLOCATION *allocation = &ledger->entries[index];
        uint64_t end;
        if (!allocation->live) continue;
        if (allocation->bytes == 0 || allocation->generation == 0 ||
            allocation->allocation_class >= GXOS_MEMORY_ALLOCATION_COUNT ||
            allocation->owner >= GXOS_MEMORY_OWNER_COUNT ||
            (allocation->base != 0 &&
             !range_end(allocation->base, allocation->bytes, &end))) {
            return 0;
        }
        for (other = index + 1U; other != GXOS_PHYSICAL_LEDGER_CAPACITY; ++other) {
            const GXOS_PHYSICAL_ALLOCATION *right = &ledger->entries[other];
            if (right->live && allocation->base != 0 && right->base != 0 &&
                ranges_overlap(allocation->base, allocation->bytes,
                               right->base, right->bytes)) {
                return 0;
            }
        }
        if (!add_u64(physical, allocation->physical_impact_bytes, &physical) ||
            !add_u64(commit, allocation->commit_impact_bytes, &commit) ||
            !add_u64(virtual_bytes, allocation->virtual_reservation_impact_bytes,
                     &virtual_bytes)) {
            return 0;
        }
        live_count++;
    }
    return live_count == ledger->live_count && physical == ledger->physical_bytes &&
           commit == ledger->commit_bytes &&
           virtual_bytes == ledger->virtual_reservation_bytes;
}

const char *gxos_memory_allocation_class_name(
    GXOS_MEMORY_ALLOCATION_CLASS allocation_class)
{
    static const char *const names[GXOS_MEMORY_ALLOCATION_COUNT] = {
        "IMAGE", "PAYLOAD_STAGING", "IMPORT_STUB", "TLS_VECTOR",
        "TLS_BLOCK", "GS", "TEB", "MAIN_STACK", "SCHEDULER_STACK",
        "SCHEDULER_PAGE", "MEMORY_MAP", "PERSISTENT_POOL", "PAGE_TABLE",
        "VM_DATA", "MANAGED_KERNEL", "OTHER"
    };
    if ((uint32_t)allocation_class >= GXOS_MEMORY_ALLOCATION_COUNT) return "INVALID";
    return names[allocation_class];
}

const char *gxos_memory_owner_name(GXOS_MEMORY_OWNER owner)
{
    static const char *const names[GXOS_MEMORY_OWNER_COUNT] = {
        "LOADER", "NATIVEAOT", "IMPORTS", "TLS", "SCHEDULER", "CRT",
        "MEMORY_ACCOUNTING", "PAGING", "VM", "MANAGED_KERNEL", "OTHER"
    };
    if ((uint32_t)owner >= GXOS_MEMORY_OWNER_COUNT) return "INVALID";
    return names[owner];
}

void gxos_vm_arena_init(GXOS_VM_ARENA *arena,
                        uint64_t base,
                        uint64_t length,
                        uint64_t generation)
{
    if (arena == 0) return;
    zero_bytes(arena, sizeof(*arena));
    arena->base = base;
    arena->length = length;
    arena->generation = generation;
    arena->valid = generation != 0 && length != 0 &&
        base <= UINT64_MAX - length;
}

int gxos_vm_arena_contains(const GXOS_VM_ARENA *arena,
                           uint64_t base,
                           uint64_t bytes)
{
    if (arena == 0 || !arena->valid) return 0;
    return range_contains(arena->base, arena->length, base, bytes);
}

GXOS_VM_STATUS gxos_vm_arena_reserve(GXOS_VM_ARENA *arena,
                                     uint64_t base,
                                     uint64_t bytes,
                                     uint32_t kind,
                                     uint64_t generation,
                                     uint32_t *slot_out)
{
    uint32_t index;
    uint64_t new_total_reserved;
    if (arena == 0 || slot_out == 0 || !arena->valid || generation == 0 ||
        bytes == 0) {
        return GXOS_VM_STATUS_INVALID_ARGUMENT;
    }
    if (base > UINT64_MAX - bytes) return GXOS_VM_STATUS_OVERFLOW;
    if (!gxos_vm_arena_contains(arena, base, bytes)) {
        return GXOS_VM_STATUS_OUTSIDE_ARENA;
    }
    for (index = 0; index != GXOS_VM_MAX_RESERVATIONS; ++index) {
        if (arena->reservations[index].live &&
            ranges_overlap(base, bytes, arena->reservations[index].base,
                           arena->reservations[index].bytes)) {
            return GXOS_VM_STATUS_OVERLAP;
        }
    }
    if (arena->reservation_count >= GXOS_VM_MAX_RESERVATIONS ||
        !add_u64(arena->total_reserved_bytes, bytes, &new_total_reserved)) {
        return arena->reservation_count >= GXOS_VM_MAX_RESERVATIONS
            ? GXOS_VM_STATUS_CAPACITY : GXOS_VM_STATUS_OVERFLOW;
    }
    for (index = 0; index != GXOS_VM_MAX_RESERVATIONS; ++index) {
        if (!arena->reservations[index].live) {
            GXOS_VM_RESERVATION *reservation = &arena->reservations[index];
            reservation->live = 1;
            reservation->base = base;
            reservation->bytes = bytes;
            reservation->requested_bytes = bytes;
            reservation->kind = kind;
            reservation->state = GXOS_VM_RESERVATION_STATE_RESERVED;
            reservation->owner = 0;
            reservation->generation = generation;
            reservation->committed_bytes = 0;
            arena->reservation_count++;
            arena->total_reserved_bytes = new_total_reserved;
            *slot_out = index;
            return GXOS_VM_STATUS_OK;
        }
    }
    return GXOS_VM_STATUS_CAPACITY;
}

GXOS_VM_STATUS gxos_vm_arena_reserve_fixed(
    GXOS_VM_ARENA *arena,
    uint64_t base,
    uint64_t requested_bytes,
    uint32_t kind,
    uint32_t owner,
    uint64_t generation,
    uint32_t *slot_out)
{
    uint64_t reserved_bytes;
    GXOS_VM_STATUS status;
    if (arena == 0 || slot_out == 0 || requested_bytes == 0) {
        return GXOS_VM_STATUS_INVALID_ARGUMENT;
    }
    if (base % GXOS_VM_RESERVATION_GRANULARITY != 0) {
        return GXOS_VM_STATUS_ALIGNMENT;
    }
    reserved_bytes = page_round_bytes(requested_bytes);
    if (reserved_bytes == 0) return GXOS_VM_STATUS_OVERFLOW;
    status = gxos_vm_arena_reserve(arena, base, reserved_bytes, kind,
                                   generation, slot_out);
    if (status != GXOS_VM_STATUS_OK) return status;
    arena->reservations[*slot_out].requested_bytes = requested_bytes;
    arena->reservations[*slot_out].owner = owner;
    arena->reservations[*slot_out].state = GXOS_VM_RESERVATION_STATE_RESERVED;
    return GXOS_VM_STATUS_OK;
}

GXOS_VM_STATUS gxos_vm_arena_reserve_any(
    GXOS_VM_ARENA *arena,
    uint64_t requested_bytes,
    uint32_t kind,
    uint32_t owner,
    uint64_t generation,
    uint64_t *base_out,
    uint32_t *slot_out)
{
    uint64_t reserved_bytes;
    uint64_t arena_end;
    uint64_t candidate;
    uint32_t index;
    if (arena == 0 || base_out == 0 || slot_out == 0 ||
        requested_bytes == 0 || !arena->valid || generation == 0) {
        return GXOS_VM_STATUS_INVALID_ARGUMENT;
    }
    reserved_bytes = page_round_bytes(requested_bytes);
    if (reserved_bytes == 0) return GXOS_VM_STATUS_OVERFLOW;
    if (!range_end(arena->base, arena->length, &arena_end) ||
        !align_up_u64(arena->base, GXOS_VM_RESERVATION_GRANULARITY,
                      &candidate)) {
        return GXOS_VM_STATUS_OVERFLOW;
    }
    while (candidate <= arena_end &&
           reserved_bytes <= arena_end - candidate) {
        uint64_t next_candidate = candidate;
        int conflict = 0;
        for (index = 0; index != GXOS_VM_MAX_RESERVATIONS; ++index) {
            const GXOS_VM_RESERVATION *reservation =
                &arena->reservations[index];
            uint64_t reservation_end;
            if (!reservation->live) continue;
            if (!range_end(reservation->base, reservation->bytes,
                           &reservation_end)) {
                return GXOS_VM_STATUS_INVALID_STATE;
            }
            if (ranges_overlap(candidate, reserved_bytes, reservation->base,
                               reservation->bytes)) {
                conflict = 1;
                if (reservation_end > next_candidate) {
                    if (!align_up_u64(reservation_end,
                                      GXOS_VM_RESERVATION_GRANULARITY,
                                      &next_candidate)) {
                        return GXOS_VM_STATUS_OVERFLOW;
                    }
                }
            }
        }
        if (!conflict) {
            GXOS_VM_STATUS status = gxos_vm_arena_reserve_fixed(
                arena, candidate, requested_bytes, kind, owner, generation,
                slot_out);
            if (status == GXOS_VM_STATUS_OK) *base_out = candidate;
            return status;
        }
        if (next_candidate <= candidate) return GXOS_VM_STATUS_OVERFLOW;
        candidate = next_candidate;
    }
    return GXOS_VM_STATUS_OUTSIDE_ARENA;
}

int gxos_vm_arena_find_reservation(
    const GXOS_VM_ARENA *arena,
    uint64_t address,
    uint32_t *slot_out)
{
    uint32_t index;
    if (arena == 0 || slot_out == 0) return 0;
    for (index = 0; index != GXOS_VM_MAX_RESERVATIONS; ++index) {
        const GXOS_VM_RESERVATION *reservation = &arena->reservations[index];
        if (reservation->live && address >= reservation->base &&
            address - reservation->base < reservation->bytes) {
            *slot_out = index;
            return 1;
        }
    }
    return 0;
}

GXOS_VM_STATUS gxos_vm_arena_commit(GXOS_VM_ARENA *arena,
                                    uint64_t base,
                                    uint64_t bytes,
                                    uint64_t generation)
{
    uint32_t reservation_slot = GXOS_VM_MAX_RESERVATIONS;
    uint32_t index;
    uint64_t new_total;
    if (arena == 0 || !arena->valid || generation == 0 || bytes == 0) {
        return GXOS_VM_STATUS_INVALID_ARGUMENT;
    }
    if (base > UINT64_MAX - bytes) return GXOS_VM_STATUS_OVERFLOW;
    for (index = 0; index != GXOS_VM_MAX_RESERVATIONS; ++index) {
        if (arena->reservations[index].live &&
            range_contains(arena->reservations[index].base,
                           arena->reservations[index].bytes, base, bytes)) {
            reservation_slot = index;
            break;
        }
    }
    if (reservation_slot == GXOS_VM_MAX_RESERVATIONS) {
        return GXOS_VM_STATUS_COMMIT_OUTSIDE_RESERVATION;
    }
    for (index = 0; index != GXOS_VM_MAX_COMMITMENTS; ++index) {
        if (arena->commitments[index].live &&
            ranges_overlap(base, bytes, arena->commitments[index].base,
                           arena->commitments[index].bytes)) {
            return GXOS_VM_STATUS_COMMIT_OVERLAP;
        }
    }
    if (arena->commitment_count >= GXOS_VM_MAX_COMMITMENTS ||
        !add_u64(arena->total_committed_bytes, bytes, &new_total) ||
        !add_u64(arena->reservations[reservation_slot].committed_bytes, bytes,
                 &new_total)) {
        return arena->commitment_count >= GXOS_VM_MAX_COMMITMENTS
            ? GXOS_VM_STATUS_CAPACITY : GXOS_VM_STATUS_OVERFLOW;
    }
    for (index = 0; index != GXOS_VM_MAX_COMMITMENTS; ++index) {
        if (!arena->commitments[index].live) {
            GXOS_VM_COMMITMENT *commitment = &arena->commitments[index];
            commitment->live = 1;
            commitment->reservation_slot = reservation_slot;
            commitment->base = base;
            commitment->bytes = bytes;
            commitment->physical_base = 0;
            commitment->page_count = bytes / GXOS_VM_PAGE_SIZE;
            if (bytes % GXOS_VM_PAGE_SIZE != 0) ++commitment->page_count;
            commitment->state = GXOS_VM_RESERVATION_STATE_COMMITTED;
            commitment->generation = generation;
            arena->commitment_count++;
            arena->total_committed_bytes += bytes;
            arena->reservations[reservation_slot].committed_bytes += bytes;
            arena->reservations[reservation_slot].state =
                GXOS_VM_RESERVATION_STATE_COMMITTED;
            return GXOS_VM_STATUS_OK;
        }
    }
    return GXOS_VM_STATUS_CAPACITY;
}

GXOS_VM_STATUS gxos_vm_arena_find_commitment(
    const GXOS_VM_ARENA *arena,
    uint64_t address,
    uint32_t *slot_out)
{
    uint32_t index;
    if (arena == 0 || slot_out == 0) return GXOS_VM_STATUS_INVALID_ARGUMENT;
    for (index = 0; index != GXOS_VM_MAX_COMMITMENTS; ++index) {
        const GXOS_VM_COMMITMENT *commitment = &arena->commitments[index];
        if (commitment->live && address >= commitment->base &&
            address - commitment->base < commitment->bytes) {
            *slot_out = index;
            return GXOS_VM_STATUS_OK;
        }
    }
    return GXOS_VM_STATUS_NOT_FOUND;
}

GXOS_VM_STATUS gxos_vm_arena_commit_page(
    GXOS_VM_ARENA *arena,
    uint64_t virtual_page,
    uint64_t physical_page,
    uint64_t generation,
    uint32_t *already_committed_out)
{
    uint32_t reservation_slot;
    uint32_t index;
    uint64_t new_total;
    if (already_committed_out != 0) *already_committed_out = 0;
    if (arena == 0 || generation == 0 || physical_page == 0) {
        return GXOS_VM_STATUS_INVALID_ARGUMENT;
    }
    if (virtual_page % GXOS_VM_PAGE_SIZE != 0 ||
        physical_page % GXOS_VM_PAGE_SIZE != 0) {
        return GXOS_VM_STATUS_ALIGNMENT;
    }
    if (!gxos_vm_arena_find_reservation(arena, virtual_page,
                                        &reservation_slot)) {
        return GXOS_VM_STATUS_COMMIT_OUTSIDE_RESERVATION;
    }
    if (!gxos_vm_arena_contains(arena, virtual_page, GXOS_VM_PAGE_SIZE) ||
        !range_contains(arena->reservations[reservation_slot].base,
                        arena->reservations[reservation_slot].bytes,
                        virtual_page, GXOS_VM_PAGE_SIZE)) {
        return GXOS_VM_STATUS_COMMIT_OUTSIDE_RESERVATION;
    }
    for (index = 0; index != GXOS_VM_MAX_COMMITMENTS; ++index) {
        GXOS_VM_COMMITMENT *commitment = &arena->commitments[index];
        if (commitment->live && commitment->base == virtual_page &&
            commitment->bytes == GXOS_VM_PAGE_SIZE) {
            if (commitment->physical_base != physical_page) {
                return GXOS_VM_STATUS_COMMIT_OVERLAP;
            }
            if (already_committed_out != 0) *already_committed_out = 1;
            return GXOS_VM_STATUS_OK;
        }
    }
    if (arena->commitment_count >= GXOS_VM_MAX_COMMITMENTS ||
        !add_u64(arena->total_committed_bytes, GXOS_VM_PAGE_SIZE,
                 &new_total) ||
        !add_u64(arena->reservations[reservation_slot].committed_bytes,
                 GXOS_VM_PAGE_SIZE, &new_total)) {
        return arena->commitment_count >= GXOS_VM_MAX_COMMITMENTS
            ? GXOS_VM_STATUS_CAPACITY : GXOS_VM_STATUS_OVERFLOW;
    }
    for (index = 0; index != GXOS_VM_MAX_COMMITMENTS; ++index) {
        GXOS_VM_COMMITMENT *commitment = &arena->commitments[index];
        if (!commitment->live) {
            zero_bytes(commitment, sizeof(*commitment));
            commitment->live = 1;
            commitment->reservation_slot = reservation_slot;
            commitment->base = virtual_page;
            commitment->bytes = GXOS_VM_PAGE_SIZE;
            commitment->physical_base = physical_page;
            commitment->page_count = 1;
            commitment->state = GXOS_VM_RESERVATION_STATE_COMMITTED;
            commitment->generation = generation;
            arena->commitment_count++;
            arena->total_committed_bytes += GXOS_VM_PAGE_SIZE;
            arena->reservations[reservation_slot].committed_bytes +=
                GXOS_VM_PAGE_SIZE;
            arena->reservations[reservation_slot].state =
                GXOS_VM_RESERVATION_STATE_COMMITTED;
            return GXOS_VM_STATUS_OK;
        }
    }
    return GXOS_VM_STATUS_CAPACITY;
}

GXOS_VM_STATUS gxos_vm_arena_decommit_page(
    GXOS_VM_ARENA *arena,
    uint64_t virtual_page,
    uint64_t *physical_page_out)
{
    uint32_t index;
    if (physical_page_out != 0) *physical_page_out = 0;
    if (arena == 0 || virtual_page % GXOS_VM_PAGE_SIZE != 0) {
        return GXOS_VM_STATUS_INVALID_ARGUMENT;
    }
    for (index = 0; index != GXOS_VM_MAX_COMMITMENTS; ++index) {
        GXOS_VM_COMMITMENT *commitment = &arena->commitments[index];
        GXOS_VM_RESERVATION *reservation;
        if (!commitment->live || commitment->base != virtual_page ||
            commitment->bytes != GXOS_VM_PAGE_SIZE) continue;
        if (commitment->reservation_slot >= GXOS_VM_MAX_RESERVATIONS) {
            return GXOS_VM_STATUS_INVALID_STATE;
        }
        reservation = &arena->reservations[commitment->reservation_slot];
        if (!reservation->live || arena->commitment_count == 0 ||
            arena->total_committed_bytes < GXOS_VM_PAGE_SIZE ||
            reservation->committed_bytes < GXOS_VM_PAGE_SIZE) {
            return GXOS_VM_STATUS_INVALID_STATE;
        }
        if (physical_page_out != 0) {
            *physical_page_out = commitment->physical_base;
        }
        arena->commitment_count--;
        arena->total_committed_bytes -= GXOS_VM_PAGE_SIZE;
        reservation->committed_bytes -= GXOS_VM_PAGE_SIZE;
        if (reservation->committed_bytes == 0) {
            reservation->state = GXOS_VM_RESERVATION_STATE_RESERVED;
        }
        zero_bytes(commitment, sizeof(*commitment));
        return GXOS_VM_STATUS_OK;
    }
    return GXOS_VM_STATUS_NOT_FOUND;
}

GXOS_VM_STATUS gxos_vm_arena_decommit(GXOS_VM_ARENA *arena,
                                      uint64_t base,
                                      uint64_t bytes)
{
    uint32_t index;
    if (arena == 0 || !arena->valid || bytes == 0) {
        return GXOS_VM_STATUS_INVALID_ARGUMENT;
    }
    for (index = 0; index != GXOS_VM_MAX_COMMITMENTS; ++index) {
        GXOS_VM_COMMITMENT *commitment = &arena->commitments[index];
        if (commitment->live && commitment->base == base &&
            commitment->bytes == bytes) {
            GXOS_VM_RESERVATION *reservation =
                &arena->reservations[commitment->reservation_slot];
            if (arena->commitment_count == 0 ||
                arena->total_committed_bytes < bytes ||
                reservation->committed_bytes < bytes) {
                return GXOS_VM_STATUS_INVALID_ARGUMENT;
            }
            arena->commitment_count--;
            arena->total_committed_bytes -= bytes;
            reservation->committed_bytes -= bytes;
            if (reservation->committed_bytes == 0) {
                reservation->state = GXOS_VM_RESERVATION_STATE_RESERVED;
            }
            zero_bytes(commitment, sizeof(*commitment));
            return GXOS_VM_STATUS_OK;
        }
    }
    return GXOS_VM_STATUS_NOT_FOUND;
}

GXOS_VM_STATUS gxos_vm_arena_release(GXOS_VM_ARENA *arena,
                                     uint32_t slot)
{
    GXOS_VM_RESERVATION *reservation;
    if (arena == 0 || !arena->valid || slot >= GXOS_VM_MAX_RESERVATIONS) {
        return GXOS_VM_STATUS_INVALID_ARGUMENT;
    }
    reservation = &arena->reservations[slot];
    if (!reservation->live) return GXOS_VM_STATUS_NOT_FOUND;
    if (reservation->committed_bytes != 0) {
        return GXOS_VM_STATUS_COMMITTED_RESERVATION;
    }
    if (arena->reservation_count == 0 ||
        arena->total_reserved_bytes < reservation->bytes) {
        return GXOS_VM_STATUS_INVALID_ARGUMENT;
    }
    arena->total_reserved_bytes -= reservation->bytes;
    arena->reservation_count--;
    zero_bytes(reservation, sizeof(*reservation));
    return GXOS_VM_STATUS_OK;
}

int gxos_vm_arena_validate(const GXOS_VM_ARENA *arena)
{
    uint32_t index;
    uint32_t other;
    uint32_t reservations = 0;
    uint32_t commitments = 0;
    uint64_t reserved = 0;
    uint64_t committed = 0;
    uint64_t reservation_committed[GXOS_VM_MAX_RESERVATIONS];
    if (arena == 0 || !arena->valid || arena->length == 0 ||
        arena->base > UINT64_MAX - arena->length || arena->generation == 0) {
        return 0;
    }
    zero_bytes(reservation_committed, sizeof(reservation_committed));
    for (index = 0; index != GXOS_VM_MAX_RESERVATIONS; ++index) {
        const GXOS_VM_RESERVATION *reservation = &arena->reservations[index];
        if (!reservation->live) continue;
        if (!gxos_vm_arena_contains(arena, reservation->base,
                                    reservation->bytes) ||
            reservation->generation == 0 ||
            reservation->requested_bytes == 0 ||
            reservation->requested_bytes > reservation->bytes ||
            reservation->state < GXOS_VM_RESERVATION_STATE_RESERVED ||
            reservation->state > GXOS_VM_RESERVATION_STATE_COMMITTED ||
            (reservation->committed_bytes == 0 &&
             reservation->state != GXOS_VM_RESERVATION_STATE_RESERVED) ||
            (reservation->committed_bytes != 0 &&
             reservation->state != GXOS_VM_RESERVATION_STATE_COMMITTED) ||
            reservation->committed_bytes > reservation->bytes ||
            !add_u64(reserved, reservation->bytes, &reserved)) {
            return 0;
        }
        for (other = index + 1U; other != GXOS_VM_MAX_RESERVATIONS; ++other) {
            const GXOS_VM_RESERVATION *right = &arena->reservations[other];
            if (right->live && ranges_overlap(reservation->base,
                                              reservation->bytes,
                                              right->base, right->bytes)) {
                return 0;
            }
        }
        reservations++;
    }
    for (index = 0; index != GXOS_VM_MAX_COMMITMENTS; ++index) {
        const GXOS_VM_COMMITMENT *commitment = &arena->commitments[index];
        if (!commitment->live) continue;
        if (commitment->reservation_slot >= GXOS_VM_MAX_RESERVATIONS ||
            !arena->reservations[commitment->reservation_slot].live ||
            commitment->generation == 0 ||
            commitment->state != GXOS_VM_RESERVATION_STATE_COMMITTED ||
            (commitment->physical_base != 0 && commitment->page_count == 0) ||
            !range_contains(
                arena->reservations[commitment->reservation_slot].base,
                arena->reservations[commitment->reservation_slot].bytes,
                commitment->base, commitment->bytes) ||
            !add_u64(committed, commitment->bytes, &committed)) {
            return 0;
        }
        if (!add_u64(reservation_committed[commitment->reservation_slot],
                     commitment->bytes,
                     &reservation_committed[commitment->reservation_slot])) {
            return 0;
        }
        for (other = index + 1U; other != GXOS_VM_MAX_COMMITMENTS; ++other) {
            const GXOS_VM_COMMITMENT *right = &arena->commitments[other];
            if (right->live && ranges_overlap(commitment->base, commitment->bytes,
                                              right->base, right->bytes)) {
                return 0;
            }
        }
        commitments++;
    }
    for (index = 0; index != GXOS_VM_MAX_RESERVATIONS; ++index) {
        if (arena->reservations[index].live &&
            reservation_committed[index] !=
                arena->reservations[index].committed_bytes) {
            return 0;
        }
    }
    return reservations == arena->reservation_count &&
           commitments == arena->commitment_count &&
           reserved == arena->total_reserved_bytes &&
           committed == arena->total_committed_bytes;
}

uint64_t gxos_vm_arena_available(const GXOS_VM_ARENA *arena)
{
    if (arena == 0 || !arena->valid || arena->total_reserved_bytes > arena->length) {
        return 0;
    }
    return arena->length - arena->total_reserved_bytes;
}

GXOS_COMMIT_STATUS gxos_commit_model_create(GXOS_COMMIT_MODEL *model,
                                            uint64_t commit_limit,
                                            uint64_t committed_bytes,
                                            uint64_t generation)
{
    if (model == 0 || generation == 0) return GXOS_COMMIT_STATUS_INVALID_ARGUMENT;
    if (committed_bytes > commit_limit) return GXOS_COMMIT_STATUS_OVERCOMMIT;
    model->commit_limit = commit_limit;
    model->committed_bytes = committed_bytes;
    model->available_commit = commit_limit - committed_bytes;
    model->generation = generation;
    model->valid = 1;
    model->no_pagefile = 0;
    return GXOS_COMMIT_STATUS_OK;
}

GXOS_COMMIT_STATUS gxos_commit_model_create_no_pagefile(
    GXOS_COMMIT_MODEL *model,
    uint64_t total_physical_bytes,
    uint64_t available_physical_bytes,
    uint64_t committed_bytes,
    uint64_t generation)
{
    uint64_t limit;
    GXOS_COMMIT_STATUS status;
    if (model == 0 || total_physical_bytes == 0 ||
        available_physical_bytes > total_physical_bytes) {
        return GXOS_COMMIT_STATUS_INVALID_ARGUMENT;
    }
    if (!add_u64(available_physical_bytes, committed_bytes, &limit)) {
        return GXOS_COMMIT_STATUS_OVERFLOW;
    }
    if (limit > total_physical_bytes) limit = total_physical_bytes;
    status = gxos_commit_model_create(model, limit, committed_bytes, generation);
    if (status == GXOS_COMMIT_STATUS_OK) model->no_pagefile = 1;
    return status;
}

GXOS_SNAPSHOT_STATUS gxos_physical_snapshot_create(
    GXOS_PHYSICAL_SNAPSHOT *snapshot,
    const GXOS_MEMORY_CLASSIFICATION *classification,
    const GXOS_PHYSICAL_LEDGER *ledger,
    uint64_t generation)
{
    uint32_t index;
    if (snapshot == 0 || classification == 0 || ledger == 0 || generation == 0) {
        return GXOS_SNAPSHOT_STATUS_INVALID_ARGUMENT;
    }
    zero_bytes(snapshot, sizeof(*snapshot));
    if (!classification->valid || !gxos_physical_ledger_validate(ledger) ||
        classification->total_ram_like_bytes == 0 ||
        ledger->physical_bytes > classification->conventional_bytes) {
        return GXOS_SNAPSHOT_STATUS_INVALID_PHYSICAL;
    }
    snapshot->total_ram_like_bytes = classification->total_ram_like_bytes;
    snapshot->available_physical_bytes =
        classification->conventional_bytes - ledger->physical_bytes;
    if (snapshot->available_physical_bytes > snapshot->total_ram_like_bytes) {
        return GXOS_SNAPSHOT_STATUS_INVALID_PHYSICAL;
    }
    snapshot->accounted_used_bytes = snapshot->total_ram_like_bytes -
        snapshot->available_physical_bytes;
    snapshot->post_epoch_physical_bytes = ledger->physical_bytes;
    snapshot->generation = generation;
    for (index = 0; index != GXOS_MEMORY_CLASS_COUNT; ++index) {
        snapshot->descriptor_class_bytes[index] = classification->class_bytes[index];
    }
    snapshot->valid = 1;
    return GXOS_SNAPSHOT_STATUS_OK;
}

static uint32_t percentage_100(uint64_t used, uint64_t total)
{
    uint64_t quotient = 0;
    uint64_t remainder = 0;
    uint32_t index;
    if (total == 0 || used > total) return 0;
    if (used == total) return 100;
    /* Long multiplication by 100 with a quotient/remainder pair avoids
       overflowing used * 100 even when total is UINT64_MAX. */
    remainder = 0;
    for (index = 0; index != 100; ++index) {
        uint64_t threshold = total - used;
        if (remainder >= threshold) {
            /* remainder + used >= total, expressed without overflowing. */
            remainder -= threshold;
            quotient++;
        } else {
            remainder += used;
        }
    }
    return quotient > 100 ? 100U : (uint32_t)quotient;
}

GXOS_SNAPSHOT_STATUS gxos_memory_snapshot_create(
    GXOS_MEMORY_SNAPSHOT *snapshot,
    const GXOS_PHYSICAL_SNAPSHOT *physical,
    const GXOS_VM_ARENA *virtual_arena,
    const GXOS_COMMIT_MODEL *commit,
    uint64_t generation)
{
    uint64_t available_virtual;
    if (snapshot == 0 || physical == 0 || virtual_arena == 0 || commit == 0 ||
        generation == 0) return GXOS_SNAPSHOT_STATUS_INVALID_ARGUMENT;
    zero_bytes(snapshot, sizeof(*snapshot));
    if (!physical->valid || physical->total_ram_like_bytes == 0 ||
        physical->available_physical_bytes > physical->total_ram_like_bytes) {
        return GXOS_SNAPSHOT_STATUS_INVALID_PHYSICAL;
    }
    if (!virtual_arena->valid || !gxos_vm_arena_validate(virtual_arena) ||
        virtual_arena->total_reserved_bytes > virtual_arena->length ||
        virtual_arena->total_committed_bytes > virtual_arena->length) {
        return GXOS_SNAPSHOT_STATUS_INVALID_VIRTUAL;
    }
    if (!commit->valid || commit->committed_bytes > commit->commit_limit ||
        commit->available_commit != commit->commit_limit - commit->committed_bytes) {
        return GXOS_SNAPSHOT_STATUS_INVALID_COMMIT;
    }
    available_virtual = gxos_vm_arena_available(virtual_arena);
    if (available_virtual > virtual_arena->length) {
        return GXOS_SNAPSHOT_STATUS_INVALID_VIRTUAL;
    }
    snapshot->generation = generation;
    snapshot->total_physical_bytes = physical->total_ram_like_bytes;
    snapshot->available_physical_bytes = physical->available_physical_bytes;
    snapshot->memory_load_percent = percentage_100(
        physical->total_ram_like_bytes - physical->available_physical_bytes,
        physical->total_ram_like_bytes);
    snapshot->commit_limit_bytes = commit->commit_limit;
    snapshot->available_commit_bytes = commit->available_commit;
    snapshot->process_virtual_total_bytes = virtual_arena->length;
    snapshot->process_virtual_available_bytes = available_virtual;
    snapshot->accounted_physical_usage_bytes = physical->accounted_used_bytes;
    snapshot->process_reserved_virtual_bytes = virtual_arena->total_reserved_bytes;
    snapshot->process_committed_virtual_bytes = virtual_arena->total_committed_bytes;
    snapshot->valid = 1;
    return GXOS_SNAPSHOT_STATUS_OK;
}

static int memory_snapshot_is_coherent(const GXOS_MEMORY_SNAPSHOT *snapshot)
{
    uint64_t used;
    if (snapshot == 0 || !snapshot->valid || snapshot->generation == 0 ||
        snapshot->total_physical_bytes == 0 ||
        snapshot->available_physical_bytes > snapshot->total_physical_bytes ||
        snapshot->memory_load_percent > 100U ||
        snapshot->available_commit_bytes > snapshot->commit_limit_bytes ||
        snapshot->process_committed_virtual_bytes >
            snapshot->commit_limit_bytes ||
        snapshot->process_virtual_available_bytes >
            snapshot->process_virtual_total_bytes ||
        snapshot->process_reserved_virtual_bytes >
            snapshot->process_virtual_total_bytes ||
        snapshot->process_committed_virtual_bytes >
            snapshot->process_reserved_virtual_bytes) {
        return 0;
    }
    used = snapshot->total_physical_bytes -
        snapshot->available_physical_bytes;
    return snapshot->accounted_physical_usage_bytes == used &&
           snapshot->available_commit_bytes ==
               snapshot->commit_limit_bytes -
                   snapshot->process_committed_virtual_bytes &&
           snapshot->memory_load_percent == percentage_100(
               used, snapshot->total_physical_bytes);
}

GXOS_SNAPSHOT_STATUS gxos_memory_snapshot_query_current(
    GXOS_MEMORY_SNAPSHOT *view,
    const GXOS_MEMORY_CLASSIFICATION *classification,
    const GXOS_MEMORY_SNAPSHOT *startup_snapshot,
    const GXOS_PHYSICAL_LEDGER *ledger,
    const GXOS_VM_ARENA *virtual_arena,
    uint64_t generation)
{
    GXOS_PHYSICAL_SNAPSHOT physical;
    GXOS_COMMIT_MODEL commit;
    GXOS_COMMIT_STATUS commit_status;
    GXOS_SNAPSHOT_STATUS status;

    if (view == 0 || classification == 0 || startup_snapshot == 0 ||
        ledger == 0 || virtual_arena == 0 || generation == 0 ||
        generation < startup_snapshot->generation ||
        !classification->valid || classification->total_ram_like_bytes == 0 ||
        !memory_snapshot_is_coherent(startup_snapshot) ||
        startup_snapshot->total_physical_bytes !=
            classification->total_ram_like_bytes) {
        return GXOS_SNAPSHOT_STATUS_INVALID_ARGUMENT;
    }
    zero_bytes(view, sizeof(*view));
    status = gxos_physical_snapshot_create(&physical, classification, ledger,
                                           generation);
    if (status != GXOS_SNAPSHOT_STATUS_OK) return status;
    commit_status = gxos_commit_model_create_no_pagefile(
        &commit, physical.total_ram_like_bytes,
        physical.available_physical_bytes,
        virtual_arena->total_committed_bytes, generation);
    if (commit_status == GXOS_COMMIT_STATUS_OVERFLOW) {
        return GXOS_SNAPSHOT_STATUS_OVERFLOW;
    }
    if (commit_status != GXOS_COMMIT_STATUS_OK) {
        return GXOS_SNAPSHOT_STATUS_INVALID_COMMIT;
    }
    return gxos_memory_snapshot_create(view, &physical, virtual_arena,
                                       &commit, generation);
}

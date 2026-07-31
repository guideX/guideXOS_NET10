#include "platform_environment.h"

static int gxos_environment_range_contains(
    const GXOS_ENVIRONMENT_MEMORY_CONTEXT *memory,
    uintptr_t address,
    uintptr_t size,
    uint32_t writable)
{
    uint32_t index;
    uintptr_t end;

    if (size == 0) return 1;
    if (memory == 0 || memory->regions == 0 || address == 0) return 0;
    if (address > UINTPTR_MAX - size) return 0;
    end = address + size;
    for (index = 0; index != memory->region_count; index++) {
        const GXOS_ENVIRONMENT_MEMORY_REGION *region = &memory->regions[index];
        if (region->begin > region->end ||
            address < region->begin || end > region->end ||
            region->readable == 0 || (writable != 0 && region->writable == 0)) {
            continue;
        }
        return 1;
    }
    return 0;
}

static GXOS_ENVIRONMENT_STATUS gxos_environment_context_valid(
    const GXOS_ENVIRONMENT_MEMORY_CONTEXT *memory)
{
    uint32_t index;

    if (memory == 0 || memory->regions == 0 ||
        memory->region_count == 0 ||
        memory->region_count > GXOS_ENVIRONMENT_MAX_MEMORY_REGIONS) {
        return GXOS_ENVIRONMENT_STATUS_INVALID_CONTEXT;
    }
    for (index = 0; index != memory->region_count; index++) {
        const GXOS_ENVIRONMENT_MEMORY_REGION *region = &memory->regions[index];
        if (region->begin == 0 || region->begin >= region->end ||
            region->readable == 0) {
            return GXOS_ENVIRONMENT_STATUS_INVALID_CONTEXT;
        }
    }
    return GXOS_ENVIRONMENT_STATUS_OK;
}

static GXOS_ENVIRONMENT_STATUS gxos_environment_name_length(
    const GXOS_ENVIRONMENT_WCHAR *name,
    const GXOS_ENVIRONMENT_MEMORY_CONTEXT *memory,
    uint32_t *length)
{
    uint32_t index;

    if (name == 0 || length == 0) return GXOS_ENVIRONMENT_STATUS_INVALID_NAME;
    for (index = 0; index != GXOS_ENVIRONMENT_MAX_NAME_CHARS; index++) {
        uintptr_t address;
        if ((uintptr_t)name > UINTPTR_MAX - (uintptr_t)index * sizeof(GXOS_ENVIRONMENT_WCHAR)) {
            return GXOS_ENVIRONMENT_STATUS_UNTERMINATED_NAME;
        }
        address = (uintptr_t)name + (uintptr_t)index * sizeof(GXOS_ENVIRONMENT_WCHAR);
        if (!gxos_environment_range_contains(memory, address,
                                             sizeof(GXOS_ENVIRONMENT_WCHAR), 0)) {
            return GXOS_ENVIRONMENT_STATUS_INVALID_NAME;
        }
        if (name[index] == 0) {
            *length = index;
            return GXOS_ENVIRONMENT_STATUS_OK;
        }
    }
    return GXOS_ENVIRONMENT_STATUS_UNTERMINATED_NAME;
}

static int gxos_environment_names_equal(
    const GXOS_ENVIRONMENT_WCHAR *left,
    uint32_t left_length,
    const GXOS_ENVIRONMENT_WCHAR *right,
    uint32_t right_length)
{
    uint32_t index;
    if (left_length != right_length) return 0;
    for (index = 0; index != left_length; index++) {
        if (left[index] != right[index]) return 0;
    }
    return 1;
}

GXOS_ENVIRONMENT_STATUS GXOS_ENVIRONMENT_MS_ABI gxos_get_environment_variable_w_checked(
    const GXOS_ENVIRONMENT_WCHAR *name,
    GXOS_ENVIRONMENT_WCHAR *buffer,
    GXOS_ENVIRONMENT_DWORD buffer_size,
    const GXOS_ENVIRONMENT_ENTRY *entries,
    uint32_t entry_count,
    const GXOS_ENVIRONMENT_MEMORY_CONTEXT *memory,
    GXOS_ENVIRONMENT_DWORD previous_last_error,
    GXOS_ENVIRONMENT_DWORD *return_value,
    GXOS_ENVIRONMENT_DWORD *last_error)
{
    GXOS_ENVIRONMENT_STATUS status;
    uint32_t name_length;
    uint32_t entry_index;
    const GXOS_ENVIRONMENT_ENTRY *entry = 0;
    uint64_t required_size;

    if (return_value == 0 || last_error == 0) {
        return GXOS_ENVIRONMENT_STATUS_INVALID_OUTPUT;
    }
    *return_value = 0;
    *last_error = previous_last_error;
    status = gxos_environment_context_valid(memory);
    if (status != GXOS_ENVIRONMENT_STATUS_OK) return status;
    if (entry_count != 0 && entries == 0) return GXOS_ENVIRONMENT_STATUS_INVALID_TABLE;
    status = gxos_environment_name_length(name, memory, &name_length);
    if (status != GXOS_ENVIRONMENT_STATUS_OK) return status;

    for (entry_index = 0; entry_index != entry_count; entry_index++) {
        const GXOS_ENVIRONMENT_ENTRY *candidate = &entries[entry_index];
        if (candidate->name == 0 ||
            (candidate->value == 0 && candidate->value_length != 0)) {
            return GXOS_ENVIRONMENT_STATUS_INVALID_TABLE;
        }
        if (gxos_environment_names_equal(name, name_length,
                                         candidate->name, candidate->name_length)) {
            entry = candidate;
            break;
        }
    }
    if (entry == 0) {
        *last_error = GXOS_ENVIRONMENT_ERROR_ENVVAR_NOT_FOUND;
        return GXOS_ENVIRONMENT_STATUS_OK;
    }

    required_size = (uint64_t)entry->value_length + 1;
    if (required_size > UINT32_MAX) {
        return GXOS_ENVIRONMENT_STATUS_SIZE_OVERFLOW;
    }
    if (buffer != 0 && buffer_size != 0) {
        uintptr_t byte_count = (uintptr_t)buffer_size * sizeof(GXOS_ENVIRONMENT_WCHAR);
        if (!gxos_environment_range_contains(memory, (uintptr_t)buffer, byte_count, 1)) {
            return GXOS_ENVIRONMENT_STATUS_INVALID_BUFFER;
        }
    }
    if ((uint64_t)buffer_size < required_size) {
        *return_value = (GXOS_ENVIRONMENT_DWORD)required_size;
        return GXOS_ENVIRONMENT_STATUS_OK;
    }
    if (buffer != 0) {
        uint32_t index;
        for (index = 0; index != entry->value_length; index++) {
            buffer[index] = entry->value[index];
        }
        buffer[entry->value_length] = 0;
    }
    *return_value = entry->value_length;
    return GXOS_ENVIRONMENT_STATUS_OK;
}

GXOS_ENVIRONMENT_DWORD GXOS_ENVIRONMENT_MS_ABI gxos_get_environment_variable_w_not_found(
    const GXOS_ENVIRONMENT_WCHAR *name,
    GXOS_ENVIRONMENT_WCHAR *buffer,
    GXOS_ENVIRONMENT_DWORD buffer_size,
    GXOS_ENVIRONMENT_DWORD *last_error)
{
    (void)name;
    (void)buffer;
    (void)buffer_size;
    if (last_error != 0) *last_error = GXOS_ENVIRONMENT_ERROR_ENVVAR_NOT_FOUND;
    return 0;
}

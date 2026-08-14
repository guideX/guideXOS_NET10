#include "write_file.h"

static void zero_bytes(void *destination, size_t count)
{
    uint8_t *bytes = (uint8_t *)destination;
    while (count-- != 0) *bytes++ = 0;
}

static void copy_bytes(uint8_t *destination, const uint8_t *source,
                       uint32_t count)
{
    while (count-- != 0) *destination++ = *source++;
}

static int canonical_address(uintptr_t address)
{
#if UINTPTR_MAX > 0xFFFFFFFFU
    uint64_t high = (uint64_t)address >> 47;
    return high == 0 || high == 0x1FFFFU;
#else
    (void)address;
    return 1;
#endif
}

static void set_error(const GXOS_WRITE_FILE_CONTEXT *context,
                      GXOS_WRITE_FILE_REPORT *report, uint32_t error)
{
    if (context != 0 && context->last_error != 0) {
        *context->last_error = error;
    }
    if (report != 0) report->last_error_after = error;
}

static int range_in_region(const GXOS_CRT_INITTERM_MEMORY_REGION *region,
                           uintptr_t address, uintptr_t *take,
                           uint32_t writable)
{
    uintptr_t available;
    if (region == 0 || region->base >= region->end ||
        address < region->base || address >= region->end ||
        region->readable == 0 || (writable != 0 && region->writable == 0)) {
        return 0;
    }
    available = region->end - address;
    *take = available;
    return 1;
}

static int range_covered(const GXOS_WRITE_FILE_CONTEXT *context,
                         uintptr_t address, uint64_t length, uint32_t writable,
                         GXOS_WRITE_FILE_STATUS *status)
{
    uintptr_t current = address;
    uint64_t remaining = length;

    if (length == 0) return 1;
    if (address == 0) {
        *status = writable != 0
            ? GXOS_WRITE_FILE_STATUS_UNWRITABLE_BYTES_WRITTEN
            : GXOS_WRITE_FILE_STATUS_NULL_BUFFER;
        return 0;
    }
    if (!canonical_address(address)) {
        *status = writable != 0
            ? GXOS_WRITE_FILE_STATUS_NONCANONICAL_BYTES_WRITTEN
            : GXOS_WRITE_FILE_STATUS_NONCANONICAL_BUFFER;
        return 0;
    }
    if (length > (uint64_t)UINTPTR_MAX - (uint64_t)address) {
        *status = writable != 0
            ? GXOS_WRITE_FILE_STATUS_BYTES_WRITTEN_RANGE_OVERFLOW
            : GXOS_WRITE_FILE_STATUS_BUFFER_RANGE_OVERFLOW;
        return 0;
    }

    while (remaining != 0) {
        uintptr_t take = 0;
        uint32_t index;
        int found = 0;

        for (index = 0; index != context->region_count; ++index) {
            if (range_in_region(&context->regions[index], current, &take,
                                writable)) {
                found = 1;
                break;
            }
        }
        if (!found && context->stack_lower < context->stack_upper &&
            current >= context->stack_lower && current < context->stack_upper) {
            take = context->stack_upper - current;
            found = 1;
        }
        if (!found) {
            *status = writable != 0
                ? GXOS_WRITE_FILE_STATUS_UNWRITABLE_BYTES_WRITTEN
                : GXOS_WRITE_FILE_STATUS_UNREADABLE_BUFFER;
            return 0;
        }
        if ((uint64_t)take >= remaining) return 1;
        current += take;
        remaining -= take;
    }
    return 1;
}

static GXOS_WRITE_FILE_STATUS validate_context(
    const GXOS_WRITE_FILE_CONTEXT *context)
{
    uint32_t index;
    if (context == 0 || context->scheduler == 0 ||
        !context->scheduler->active || context->regions == 0 ||
        context->region_count > GXOS_WRITE_FILE_MAX_MEMORY_REGIONS ||
        context->backend_write == 0) {
        return GXOS_WRITE_FILE_STATUS_INVALID_CONTEXT;
    }
    for (index = 0; index != context->region_count; ++index) {
        const GXOS_CRT_INITTERM_MEMORY_REGION *region =
            &context->regions[index];
        if (region->base >= region->end ||
            !canonical_address(region->base) ||
            !canonical_address(region->end - 1U)) {
            return GXOS_WRITE_FILE_STATUS_INVALID_CONTEXT;
        }
    }
    if ((context->stack_lower == 0) != (context->stack_upper == 0) ||
        (context->stack_lower != 0 &&
         (context->stack_lower >= context->stack_upper ||
         !canonical_address(context->stack_lower) ||
         !canonical_address(context->stack_upper - 1U)))) {
        return GXOS_WRITE_FILE_STATUS_INVALID_CONTEXT;
    }
    return GXOS_WRITE_FILE_STATUS_OK;
}

static GXOS_WRITE_FILE_STATUS fail_status(
    const GXOS_WRITE_FILE_CONTEXT *context,
    GXOS_WRITE_FILE_REPORT *report,
    GXOS_WRITE_FILE_STATUS status,
    uint32_t error)
{
    report->status = status;
    report->win32_error = error;
    report->result_bool = 0;
    set_error(context, report, error);
    return status;
}

static void capture_buffer(GXOS_WRITE_FILE_REPORT *report)
{
    uint32_t first = report->bytes_to_write < GXOS_WRITE_FILE_MAX_CAPTURE_BYTES
        ? report->bytes_to_write : GXOS_WRITE_FILE_MAX_CAPTURE_BYTES;
    uint32_t last = report->bytes_to_write < GXOS_WRITE_FILE_MAX_CAPTURE_BYTES
        ? report->bytes_to_write : GXOS_WRITE_FILE_MAX_CAPTURE_BYTES;
    report->first_capture_count = first;
    report->last_capture_count = last;
    if (first != 0) {
        copy_bytes(report->first_capture,
                   (const uint8_t *)(uintptr_t)report->buffer, first);
    }
    if (last != 0) {
        copy_bytes(report->last_capture,
                   (const uint8_t *)(uintptr_t)(report->buffer +
                       report->bytes_to_write - last), last);
    }
}

uint32_t GXOS_WRITE_FILE_MS_ABI gxos_write_file_contract(
    const GXOS_WRITE_FILE_CONTEXT *context,
    const GXOS_WRITE_FILE_CALL *call,
    GXOS_WRITE_FILE_REPORT *report)
{
    GXOS_SCHEDULER_OBJECT *object = 0;
    GXOS_SCHEDULER_STANDARD_STREAM *stream = 0;
    GXOS_SCHEDULER_TCB *thread;
    GXOS_WRITE_FILE_STATUS status;
    uint32_t backend_count = 0;
    uint32_t previous_error;

    if (report == 0) return 0;
    zero_bytes(report, sizeof(*report));
    if (call != 0) {
        report->h_file = call->h_file;
        report->buffer = (uintptr_t)call->buffer;
        report->bytes_to_write = call->bytes_to_write;
        report->bytes_written = (uintptr_t)call->bytes_written;
        report->overlapped = (uintptr_t)call->overlapped;
        report->caller_return_address = call->return_address;
    }
    previous_error = context != 0 && context->last_error != 0
        ? *context->last_error : 0;
    report->prior_last_error = previous_error;
    report->last_error_after = previous_error;

    status = validate_context(context);
    if (status != GXOS_WRITE_FILE_STATUS_OK) {
        fail_status(context, report, status, GXOS_WRITE_FILE_ERROR_INVALID_PARAMETER);
        return 0;
    }
    if (call == 0) {
        fail_status(context, report, GXOS_WRITE_FILE_STATUS_INVALID_PARAMETER,
                    GXOS_WRITE_FILE_ERROR_INVALID_PARAMETER);
        return 0;
    }

    thread = gxos_scheduler_current_thread();
    report->thread_identity = thread == 0 ? 0 : thread->identity;
    object = gxos_scheduler_object_from_handle(call->h_file);
    if (object == 0 || !object->live || object->close_state ||
        object->public_handle_refs == 0 || object->internal_refs == 0 ||
        object->target == 0) {
        fail_status(context, report, GXOS_WRITE_FILE_STATUS_INVALID_HANDLE,
                    GXOS_WRITE_FILE_ERROR_INVALID_HANDLE);
        return 0;
    }
    report->object_type = object->type;
    report->object_slot = object->slot;
    report->object_generation = object->generation;
    report->public_handle_refs_before = object->public_handle_refs;
    report->internal_refs_before = object->internal_refs;
    if (object->type != GXOS_SCHEDULER_OBJECT_STANDARD_STREAM) {
        fail_status(context, report, GXOS_WRITE_FILE_STATUS_UNSUPPORTED_OBJECT,
                    GXOS_WRITE_FILE_ERROR_INVALID_HANDLE);
        return 0;
    }
    stream = (GXOS_SCHEDULER_STANDARD_STREAM *)object->target;
    report->stream_backend = stream->backend;
    report->stream_capabilities = stream->capabilities;
    if (!stream->live || stream->object_slot != object->slot ||
        stream->generation != object->generation) {
        fail_status(context, report, GXOS_WRITE_FILE_STATUS_INVALID_HANDLE,
                    GXOS_WRITE_FILE_ERROR_INVALID_HANDLE);
        return 0;
    }
    if ((stream->capabilities & GXOS_SCHEDULER_STANDARD_STREAM_CAPABILITY_WRITE) == 0) {
        fail_status(context, report, GXOS_WRITE_FILE_STATUS_ACCESS_DENIED,
                    GXOS_WRITE_FILE_ERROR_ACCESS_DENIED);
        return 0;
    }
    if (stream->backend != GXOS_SCHEDULER_STANDARD_STREAM_BACKEND_SERIAL_COM1) {
        fail_status(context, report, GXOS_WRITE_FILE_STATUS_UNSUPPORTED_OBJECT,
                    GXOS_WRITE_FILE_ERROR_NOT_SUPPORTED);
        return 0;
    }
    if (call->overlapped != 0) {
        fail_status(context, report,
                    GXOS_WRITE_FILE_STATUS_OVERLAPPED_UNSUPPORTED,
                    GXOS_WRITE_FILE_ERROR_INVALID_PARAMETER);
        return 0;
    }
    if (call->bytes_to_write != 0) {
        if (call->buffer == 0) {
            fail_status(context, report, GXOS_WRITE_FILE_STATUS_NULL_BUFFER,
                        GXOS_WRITE_FILE_ERROR_INVALID_PARAMETER);
            return 0;
        }
        status = GXOS_WRITE_FILE_STATUS_OK;
        if (!range_covered(context, (uintptr_t)call->buffer,
                           call->bytes_to_write, 0, &status)) {
            fail_status(context, report, status,
                        status == GXOS_WRITE_FILE_STATUS_UNREADABLE_BUFFER
                            ? GXOS_WRITE_FILE_ERROR_NOACCESS
                            : GXOS_WRITE_FILE_ERROR_INVALID_PARAMETER);
            return 0;
        }
        report->buffer_range_valid = 1;
        capture_buffer(report);
    } else {
        report->buffer_range_valid = 1;
    }
    if (call->bytes_written == 0) {
        fail_status(context, report, GXOS_WRITE_FILE_STATUS_NULL_BYTES_WRITTEN,
                    GXOS_WRITE_FILE_ERROR_INVALID_PARAMETER);
        return 0;
    }
    status = GXOS_WRITE_FILE_STATUS_OK;
    if (!range_covered(context, (uintptr_t)call->bytes_written,
                       sizeof(uint32_t), 1, &status)) {
        fail_status(context, report, status,
                    status == GXOS_WRITE_FILE_STATUS_UNWRITABLE_BYTES_WRITTEN
                        ? GXOS_WRITE_FILE_ERROR_NOACCESS
                        : GXOS_WRITE_FILE_ERROR_INVALID_PARAMETER);
        return 0;
    }
    report->bytes_written_range_valid = 1;

    if (context->pre_output != 0) context->pre_output(report);
    if (call->bytes_to_write != 0) {
        if (!context->backend_write(context->backend_context,
                                    (const uint8_t *)call->buffer,
                                    call->bytes_to_write, &backend_count)) {
            fail_status(context, report, GXOS_WRITE_FILE_STATUS_BACKEND_FAILURE,
                        GXOS_WRITE_FILE_ERROR_NOT_SUPPORTED);
            report->output_started = backend_count != 0;
            report->backend_succeeded = 0;
            return 0;
        }
        report->output_started = backend_count != 0;
        if (backend_count > call->bytes_to_write) {
            fail_status(context, report,
                        GXOS_WRITE_FILE_STATUS_BACKEND_COUNT_INVALID,
                        GXOS_WRITE_FILE_ERROR_NOT_SUPPORTED);
            return 0;
        }
    }
    report->backend_succeeded = 1;
    report->backend_count_valid = 1;
    report->bytes_written_result = backend_count;
    *(uint32_t *)call->bytes_written = backend_count;
    report->public_handle_refs_after = object->public_handle_refs;
    report->internal_refs_after = object->internal_refs;
    report->result_bool = 1;
    report->status = GXOS_WRITE_FILE_STATUS_OK;
    report->win32_error = previous_error;
    report->last_error_after = context->last_error == 0
        ? previous_error : *context->last_error;
    return 1;
}

#include "standard_handle.h"
#include "write_file.h"

#include <stdio.h>

static unsigned char g_pages[256U * GXOS_SCHEDULER_PAGE_SIZE]
    __attribute__((aligned(GXOS_SCHEDULER_PAGE_SIZE)));
static unsigned char g_page_used[256U];
static GXOS_SCHEDULER g_scheduler;
static uint8_t g_payload[128U];
static uint8_t g_result_guard[12U] __attribute__((aligned(4)));
static uint8_t g_read_only_result[4U];
static unsigned int g_checks;
static unsigned int g_failures;

typedef struct {
    uint8_t bytes[256U];
    uint32_t length;
    uint32_t calls;
    uint32_t fail;
    uint32_t forced_count;
} TEST_BACKEND;

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

static uint64_t GXOS_SCHEDULER_MS_ABI model_free(
    uint64_t memory, uint64_t pages)
{
    uint64_t base = (uint64_t)(uintptr_t)g_pages;
    uint64_t end = base + sizeof(g_pages);
    uint32_t index;
    if (memory < base || memory >= end ||
        (memory - base) % GXOS_SCHEDULER_PAGE_SIZE != 0 ||
        pages == 0 || pages > 256U ||
        memory + pages * GXOS_SCHEDULER_PAGE_SIZE > end) return 1;
    index = (uint32_t)((memory - base) / GXOS_SCHEDULER_PAGE_SIZE);
    while (pages-- != 0) g_page_used[index++] = 0;
    return 0;
}

static void GXOS_SCHEDULER_MS_ABI model_log_text(const char *text)
{
    (void)text;
}

static void GXOS_SCHEDULER_MS_ABI model_log_hex(const char *name,
                                                 uint64_t value)
{
    (void)name;
    (void)value;
}

static void GXOS_SCHEDULER_MS_ABI model_log_u32(const char *name,
                                                 uint32_t value)
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

static int GXOS_WRITE_FILE_MS_ABI test_backend_write(
    void *context, const uint8_t *bytes, uint32_t length,
    uint32_t *bytes_written)
{
    TEST_BACKEND *backend = (TEST_BACKEND *)context;
    uint32_t index;
    ++backend->calls;
    if (backend->fail) {
        *bytes_written = backend->forced_count;
        return 0;
    }
    if (length > sizeof(backend->bytes)) return 0;
    for (index = 0; index != length; ++index) {
        backend->bytes[index] = bytes[index];
    }
    backend->length = length;
    *bytes_written = length;
    return 1;
}

static uint32_t live_object_count(void)
{
    uint32_t index;
    uint32_t count = 0;
    for (index = 0; index != GXOS_SCHEDULER_MAX_OBJECTS; ++index) {
        if (g_scheduler.objects[index].live) ++count;
    }
    return count;
}

static uint32_t live_public_handle_count(void)
{
    uint32_t index;
    uint32_t count = 0;
    for (index = 0; index != GXOS_SCHEDULER_MAX_OBJECTS; ++index) {
        if (g_scheduler.objects[index].live) {
            count += g_scheduler.objects[index].public_handle_refs;
        }
    }
    return count;
}

static void set_region(GXOS_CRT_INITTERM_MEMORY_REGION *region,
                       const void *base, uint32_t size, uint32_t writable)
{
    region->base = (uintptr_t)base;
    region->end = region->base + size;
    region->readable = 1;
    region->executable = 0;
    region->writable = writable;
}

static GXOS_WRITE_FILE_CALL valid_call(GXOS_SCHEDULER_HANDLE handle,
                                       uint32_t *bytes_written,
                                       uint32_t length)
{
    GXOS_WRITE_FILE_CALL call;
    call.h_file = handle;
    call.buffer = g_payload;
    call.bytes_to_write = length;
    call.bytes_written = bytes_written;
    call.overlapped = 0;
    call.return_address = 0x18003CD9DU;
    return call;
}

static int bytes_equal(const uint8_t *left, const uint8_t *right,
                       uint32_t length)
{
    uint32_t index;
    for (index = 0; index != length; ++index) {
        if (left[index] != right[index]) return 0;
    }
    return 1;
}

int main(void)
{
    GXOS_STANDARD_HANDLE_CONTEXT standard_context;
    GXOS_WRITE_FILE_CONTEXT write_context;
    GXOS_CRT_INITTERM_MEMORY_REGION regions[3];
    TEST_BACKEND backend;
    GXOS_SCHEDULER_HANDLE stderr_handle;
    GXOS_SCHEDULER_HANDLE event_handle;
    GXOS_SCHEDULER_HANDLE thread_handle;
    GXOS_SCHEDULER_TCB *created_thread;
    GXOS_SCHEDULER_OBJECT *object;
    GXOS_SCHEDULER_STANDARD_STREAM *stream;
    GXOS_WRITE_FILE_CALL call;
    GXOS_WRITE_FILE_REPORT report;
    uint32_t last_error = 0x13572468U;
    uint32_t bytes_written;
    uint32_t before_public_refs;
    uint32_t before_internal_refs;
    uint32_t before_live_objects;
    uint32_t before_live_handles;
    uint32_t index;
    uint8_t expected[] = {0x41U, 0x00U, 0x80U, 0xFFU, 0x0AU};
    uint8_t result_before[sizeof(g_result_guard)];
    uint8_t payload_before[sizeof(g_payload)];
    GXOS_SCHEDULER_HANDLE stale_handle;

    for (index = 0; index != sizeof(g_payload); ++index) {
        g_payload[index] = (uint8_t)(index ^ 0xA5U);
    }
    for (index = 0; index != sizeof(g_result_guard); ++index) {
        g_result_guard[index] = (uint8_t)(0xD0U + index);
    }
    for (index = 0; index != sizeof(g_read_only_result); ++index) {
        g_read_only_result[index] = 0xCCU;
    }
    (void)printf("WRITE_FILE_HOST_TEST_BEGIN\n");

    CHECK(gxos_scheduler_initialize(&g_scheduler, model_allocate, model_free,
                                    model_log_text, model_log_hex,
                                    model_log_u32));
    standard_context.scheduler = &g_scheduler;
    standard_context.last_error = &last_error;
    standard_context.input_available = 0;
    standard_context.output_available = 1;
    standard_context.error_available = 1;
    standard_context.output_backend =
        GXOS_SCHEDULER_STANDARD_STREAM_BACKEND_SERIAL_COM1;
    standard_context.output_capabilities =
        GXOS_SCHEDULER_STANDARD_STREAM_CAPABILITY_WRITE;
    stderr_handle = gxos_get_std_handle_contract(
        &standard_context, GXOS_STANDARD_HANDLE_ERROR);
    CHECK(stderr_handle != 0 &&
          stderr_handle != GXOS_STANDARD_HANDLE_INVALID_VALUE);
    object = gxos_scheduler_object_from_handle(stderr_handle);
    stream = gxos_scheduler_standard_stream_from_handle(stderr_handle);
    CHECK(object != 0 && stream != 0 &&
          object->type == GXOS_SCHEDULER_OBJECT_STANDARD_STREAM);
    before_public_refs = object->public_handle_refs;
    before_internal_refs = object->internal_refs;

    set_region(&regions[0], g_payload, 64U, 0);
    set_region(&regions[1], g_result_guard, sizeof(g_result_guard), 1);
    set_region(&regions[2], g_read_only_result, sizeof(g_read_only_result), 0);
    backend.length = 0;
    backend.calls = 0;
    backend.fail = 0;
    backend.forced_count = 0;
    write_context.scheduler = &g_scheduler;
    write_context.last_error = &last_error;
    write_context.regions = regions;
    write_context.region_count = 3;
    write_context.stack_lower = (uintptr_t)&bytes_written;
    write_context.stack_upper = write_context.stack_lower + sizeof(bytes_written);
    write_context.backend_write = test_backend_write;
    write_context.backend_context = &backend;
    write_context.pre_output = 0;

    for (index = 0; index != sizeof(payload_before); ++index) {
        payload_before[index] = g_payload[index];
    }
    for (index = 0; index != sizeof(result_before); ++index) {
        result_before[index] = g_result_guard[index];
    }
    bytes_written = 0xDEADBEEFU;
    call = valid_call(stderr_handle, &bytes_written, sizeof(expected));
    call.buffer = expected;
    call.bytes_written = (uint32_t *)(uintptr_t)(g_result_guard + 4U);
    regions[0].base = (uintptr_t)expected;
    regions[0].end = regions[0].base + sizeof(expected);
    last_error = 0x24681357U;
    CHECK(gxos_write_file_contract(&write_context, &call, &report) == 1);
    CHECK(report.status == GXOS_WRITE_FILE_STATUS_OK &&
          report.result_bool == 1);
    CHECK(report.buffer_range_valid && report.bytes_written_range_valid);
    CHECK(report.first_capture_count == sizeof(expected) &&
          report.last_capture_count == sizeof(expected));
    CHECK(bytes_equal(backend.bytes, expected, sizeof(expected)));
    CHECK(backend.length == sizeof(expected) && backend.calls == 1U);
    CHECK(bytes_written == 0xDEADBEEFU);
    CHECK(*(uint32_t *)(uintptr_t)(g_result_guard + 4U) == sizeof(expected));
    CHECK(g_result_guard[0] == result_before[0] &&
          g_result_guard[1] == result_before[1] &&
          g_result_guard[2] == result_before[2] &&
          g_result_guard[3] == result_before[3] &&
          g_result_guard[8] == result_before[8] &&
          g_result_guard[9] == result_before[9] &&
          g_result_guard[10] == result_before[10] &&
          g_result_guard[11] == result_before[11]);
    CHECK(last_error == 0x24681357U);
    CHECK(object->public_handle_refs == before_public_refs &&
          object->internal_refs == before_internal_refs);
    CHECK(bytes_equal(g_payload, payload_before, sizeof(g_payload)));

    backend.length = 0;
    bytes_written = 0xFFFFFFFFU;
    call = valid_call(stderr_handle, &bytes_written, 0);
    call.buffer = 0;
    CHECK(gxos_write_file_contract(&write_context, &call, &report) == 1);
    CHECK(report.result_bool == 1 && report.bytes_written_result == 0 &&
          report.buffer_range_valid && report.bytes_written_range_valid);
    CHECK(bytes_written == 0 && backend.calls == 1U && backend.length == 0);

    regions[0].base = (uintptr_t)g_payload;
    regions[0].end = regions[0].base + sizeof(g_payload);
    bytes_written = 0;
    call = valid_call(stderr_handle, &bytes_written, 64U);
    call.buffer = g_payload;
    CHECK(gxos_write_file_contract(&write_context, &call, &report) == 1);
    CHECK(report.first_capture_count == GXOS_WRITE_FILE_MAX_CAPTURE_BYTES &&
          report.last_capture_count == GXOS_WRITE_FILE_MAX_CAPTURE_BYTES);
    CHECK(bytes_equal(report.first_capture, g_payload,
                      GXOS_WRITE_FILE_MAX_CAPTURE_BYTES) &&
          bytes_equal(report.last_capture, g_payload + 32U,
                      GXOS_WRITE_FILE_MAX_CAPTURE_BYTES));
    CHECK(backend.length == 64U && bytes_written == 64U && backend.calls == 2U);
    regions[0].base = (uintptr_t)expected;
    regions[0].end = regions[0].base + sizeof(expected);

    call = valid_call((GXOS_SCHEDULER_HANDLE)0x1234U, &bytes_written, 1);
    bytes_written = 0xAABBCCDDU;
    backend.length = 0;
    CHECK(gxos_write_file_contract(&write_context, &call, &report) == 0);
    CHECK(report.status == GXOS_WRITE_FILE_STATUS_INVALID_HANDLE &&
          report.win32_error == GXOS_WRITE_FILE_ERROR_INVALID_HANDLE);
    CHECK(bytes_written == 0xAABBCCDDU && backend.length == 0);
    stale_handle = stderr_handle ^ ((GXOS_SCHEDULER_HANDLE)1U << 16);
    call.h_file = stale_handle;
    CHECK(gxos_write_file_contract(&write_context, &call, &report) == 0);
    CHECK(report.status == GXOS_WRITE_FILE_STATUS_INVALID_HANDLE);

    CHECK(gxos_scheduler_create_event(&g_scheduler, 1, 0, &event_handle));
    call.h_file = event_handle;
    bytes_written = 0xAABBCCDDU;
    backend.length = 0;
    CHECK(gxos_write_file_contract(&write_context, &call, &report) == 0);
    CHECK(report.status == GXOS_WRITE_FILE_STATUS_UNSUPPORTED_OBJECT &&
          report.win32_error == GXOS_WRITE_FILE_ERROR_INVALID_HANDLE);
    CHECK(bytes_written == 0xAABBCCDDU && backend.length == 0);
    CHECK(gxos_scheduler_close_handle(event_handle));
    CHECK(gxos_write_file_contract(&write_context, &call, &report) == 0);
    CHECK(report.status == GXOS_WRITE_FILE_STATUS_INVALID_HANDLE);
    CHECK(gxos_scheduler_try_destroy_event(event_handle));

    created_thread = 0;
    CHECK(gxos_scheduler_create_suspended_thread(
              &g_scheduler, model_entry, (void *)(uintptr_t)0x1234U,
              &thread_handle, &created_thread));
    call.h_file = thread_handle;
    bytes_written = 0xAABBCCDDU;
    backend.length = 0;
    CHECK(gxos_write_file_contract(&write_context, &call, &report) == 0);
    CHECK(report.status == GXOS_WRITE_FILE_STATUS_UNSUPPORTED_OBJECT &&
          report.win32_error == GXOS_WRITE_FILE_ERROR_INVALID_HANDLE);
    CHECK(bytes_written == 0xAABBCCDDU && backend.length == 0);
    CHECK(gxos_scheduler_close_handle(thread_handle));
    CHECK(gxos_scheduler_discard_created_thread(created_thread));

    stream->capabilities = GXOS_SCHEDULER_STANDARD_STREAM_CAPABILITY_READ;
    call.h_file = stderr_handle;
    bytes_written = 0xAABBCCDDU;
    backend.length = 0;
    CHECK(gxos_write_file_contract(&write_context, &call, &report) == 0);
    CHECK(report.status == GXOS_WRITE_FILE_STATUS_ACCESS_DENIED &&
          report.win32_error == GXOS_WRITE_FILE_ERROR_ACCESS_DENIED);
    CHECK(bytes_written == 0xAABBCCDDU && backend.length == 0);
    stream->capabilities = GXOS_SCHEDULER_STANDARD_STREAM_CAPABILITY_WRITE;

    call = valid_call(stderr_handle, &bytes_written, sizeof(expected));
    call.buffer = 0;
    bytes_written = 0xAABBCCDDU;
    CHECK(gxos_write_file_contract(&write_context, &call, &report) == 0);
    CHECK(report.status == GXOS_WRITE_FILE_STATUS_NULL_BUFFER &&
          bytes_written == 0xAABBCCDDU && backend.length == 0);

    call = valid_call(stderr_handle, &bytes_written, 4);
    call.buffer = g_payload + 62U;
    regions[0].base = (uintptr_t)g_payload;
    regions[0].end = regions[0].base + 64U;
    bytes_written = 0xAABBCCDDU;
    backend.length = 0;
    CHECK(gxos_write_file_contract(&write_context, &call, &report) == 0);
    CHECK(report.status == GXOS_WRITE_FILE_STATUS_UNREADABLE_BUFFER &&
          report.win32_error == GXOS_WRITE_FILE_ERROR_NOACCESS);
    CHECK(bytes_written == 0xAABBCCDDU && backend.length == 0);

    call.buffer = (const void *)(uintptr_t)(UINTPTR_MAX - 1U);
    bytes_written = 0xAABBCCDDU;
    CHECK(gxos_write_file_contract(&write_context, &call, &report) == 0);
    CHECK(report.status == GXOS_WRITE_FILE_STATUS_BUFFER_RANGE_OVERFLOW &&
          bytes_written == 0xAABBCCDDU && backend.length == 0);

    call.buffer = (const void *)(uintptr_t)0x0000800000000000ULL;
    bytes_written = 0xAABBCCDDU;
    CHECK(gxos_write_file_contract(&write_context, &call, &report) == 0);
    CHECK(report.status == GXOS_WRITE_FILE_STATUS_NONCANONICAL_BUFFER &&
          report.win32_error == GXOS_WRITE_FILE_ERROR_INVALID_PARAMETER &&
          bytes_written == 0xAABBCCDDU && backend.length == 0);

    call = valid_call(stderr_handle, &bytes_written, sizeof(expected));
    call.buffer = expected;
    regions[0].base = (uintptr_t)expected;
    regions[0].end = regions[0].base + sizeof(expected);
    call.bytes_written = (uint32_t *)(uintptr_t)g_read_only_result;
    bytes_written = 0xAABBCCDDU;
    CHECK(gxos_write_file_contract(&write_context, &call, &report) == 0);
    CHECK(report.status == GXOS_WRITE_FILE_STATUS_UNWRITABLE_BYTES_WRITTEN &&
          report.win32_error == GXOS_WRITE_FILE_ERROR_NOACCESS &&
          bytes_written == 0xAABBCCDDU && backend.length == 0);

    call.bytes_written = (uint32_t *)(uintptr_t)(UINTPTR_MAX - 1U);
    CHECK(gxos_write_file_contract(&write_context, &call, &report) == 0);
    CHECK(report.status == GXOS_WRITE_FILE_STATUS_BYTES_WRITTEN_RANGE_OVERFLOW &&
          report.win32_error == GXOS_WRITE_FILE_ERROR_INVALID_PARAMETER);

    call.bytes_written = (uint32_t *)(uintptr_t)0x0000800000000000ULL;
    CHECK(gxos_write_file_contract(&write_context, &call, &report) == 0);
    CHECK(report.status == GXOS_WRITE_FILE_STATUS_NONCANONICAL_BYTES_WRITTEN &&
          report.win32_error == GXOS_WRITE_FILE_ERROR_INVALID_PARAMETER);

    call.bytes_written = 0;
    CHECK(gxos_write_file_contract(&write_context, &call, &report) == 0);
    CHECK(report.status == GXOS_WRITE_FILE_STATUS_NULL_BYTES_WRITTEN &&
          report.win32_error == GXOS_WRITE_FILE_ERROR_INVALID_PARAMETER);

    bytes_written = 0xAABBCCDDU;
    call.bytes_written = &bytes_written;
    call.overlapped = (const void *)(uintptr_t)0x1000U;
    backend.length = 0;
    CHECK(gxos_write_file_contract(&write_context, &call, &report) == 0);
    CHECK(report.status == GXOS_WRITE_FILE_STATUS_OVERLAPPED_UNSUPPORTED &&
          report.win32_error == GXOS_WRITE_FILE_ERROR_INVALID_PARAMETER &&
          bytes_written == 0xAABBCCDDU && backend.length == 0);
    call.overlapped = 0;

    backend.fail = 1;
    backend.forced_count = 0;
    bytes_written = 0xAABBCCDDU;
    before_public_refs = object->public_handle_refs;
    before_internal_refs = object->internal_refs;
    before_live_objects = live_object_count();
    before_live_handles = live_public_handle_count();
    CHECK(gxos_write_file_contract(&write_context, &call, &report) == 0);
    CHECK(report.status == GXOS_WRITE_FILE_STATUS_BACKEND_FAILURE &&
          report.win32_error == GXOS_WRITE_FILE_ERROR_NOT_SUPPORTED &&
          report.result_bool == 0 && report.backend_succeeded == 0);
    CHECK(bytes_written == 0xAABBCCDDU &&
          object->public_handle_refs == before_public_refs &&
          object->internal_refs == before_internal_refs &&
          live_object_count() == before_live_objects &&
          live_public_handle_count() == before_live_handles);
    backend.fail = 0;

    for (index = 0; index != 100U; ++index) {
        bytes_written = 0;
        call = valid_call(stderr_handle, &bytes_written, sizeof(expected));
        call.buffer = expected;
        CHECK(gxos_write_file_contract(&write_context, &call, &report) == 1);
        CHECK(bytes_written == sizeof(expected));
    }
    CHECK(object->public_handle_refs == before_public_refs &&
          object->internal_refs == before_internal_refs);

    CHECK(gxos_scheduler_teardown(&g_scheduler));
    CHECK(g_failures == 0);
    (void)printf("WRITE_FILE_BACKEND_TESTS=PASSED checks=%u\n", g_checks);
    (void)printf("WRITE_FILE_HOST_TESTS=PASSED checks=%u\n", g_checks);
    return g_failures == 0 ? 0 : 1;
}

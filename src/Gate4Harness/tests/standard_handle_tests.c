#include "standard_handle.h"

#include <stdio.h>

static unsigned char g_pages[256U * GXOS_SCHEDULER_PAGE_SIZE]
    __attribute__((aligned(GXOS_SCHEDULER_PAGE_SIZE)));
static unsigned char g_page_used[256U];
static GXOS_SCHEDULER g_scheduler;
static GXOS_SCHEDULER g_absent_scheduler;
static unsigned int g_checks;
static unsigned int g_failures;

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

int main(void)
{
    GXOS_STANDARD_HANDLE_CONTEXT context;
    GXOS_STANDARD_HANDLE_CONTEXT absent_context;
    GXOS_SCHEDULER_HANDLE stderr_handle;
    GXOS_SCHEDULER_HANDLE stdout_handle;
    GXOS_SCHEDULER_HANDLE event_handle;
    GXOS_SCHEDULER_HANDLE thread_handle;
    GXOS_SCHEDULER_TCB *thread;
    GXOS_SCHEDULER_OBJECT *object;
    GXOS_SCHEDULER_STANDARD_STREAM *stream;
    uint32_t last_error = 0x13572468U;
    uint32_t before_refs;
    uint32_t before_handles;
    GXOS_SCHEDULER_HANDLE stale_generation;

    CHECK(gxos_scheduler_initialize(&g_scheduler, model_allocate, model_free,
                                    model_log_text, model_log_hex,
                                    model_log_u32));
    context.scheduler = &g_scheduler;
    context.last_error = &last_error;
    context.input_available = 0;
    context.output_available = 1;
    context.error_available = 1;
    context.output_backend =
        GXOS_SCHEDULER_STANDARD_STREAM_BACKEND_SERIAL_COM1;
    context.output_capabilities =
        GXOS_SCHEDULER_STANDARD_STREAM_CAPABILITY_WRITE;

    before_handles = live_public_handle_count();
    stderr_handle = gxos_get_std_handle_contract(
        &context, GXOS_STANDARD_HANDLE_ERROR);
    CHECK(stderr_handle != 0 &&
          stderr_handle != GXOS_STANDARD_HANDLE_INVALID_VALUE);
    object = gxos_scheduler_object_from_handle(stderr_handle);
    stream = gxos_scheduler_standard_stream_from_handle(stderr_handle);
    CHECK(object != 0 && stream != 0);
    CHECK(object->type == GXOS_SCHEDULER_OBJECT_STANDARD_STREAM);
    CHECK(object->live && object->public_handle_refs == 1 &&
          object->internal_refs == 1);
    CHECK(stream->live && stream->backend ==
          GXOS_SCHEDULER_STANDARD_STREAM_BACKEND_SERIAL_COM1);
    CHECK(stream->capabilities ==
          GXOS_SCHEDULER_STANDARD_STREAM_CAPABILITY_WRITE);
    CHECK((stream->role_mask &
           (GXOS_SCHEDULER_STANDARD_STREAM_ROLE_OUTPUT |
            GXOS_SCHEDULER_STANDARD_STREAM_ROLE_ERROR)) ==
          (GXOS_SCHEDULER_STANDARD_STREAM_ROLE_OUTPUT |
           GXOS_SCHEDULER_STANDARD_STREAM_ROLE_ERROR));
    CHECK(live_public_handle_count() == before_handles + 1U);
    before_refs = object->public_handle_refs;
    last_error = 0x24681357U;

    CHECK(gxos_get_std_handle_contract(&context,
                                      GXOS_STANDARD_HANDLE_ERROR) ==
          stderr_handle);
    CHECK(gxos_get_std_handle_contract(&context,
                                      GXOS_STANDARD_HANDLE_OUTPUT) ==
          stderr_handle);
    stdout_handle = gxos_scheduler_standard_handle_for_role(
        GXOS_SCHEDULER_STANDARD_STREAM_ROLE_OUTPUT);
    CHECK(stdout_handle == stderr_handle);
    CHECK(object->public_handle_refs == before_refs);
    CHECK(last_error == 0x24681357U);

    CHECK(gxos_get_std_handle_contract(&context,
                                      GXOS_STANDARD_HANDLE_INPUT) == 0);
    CHECK(last_error == 0x24681357U);
    CHECK(gxos_get_std_handle_contract(&context, 0x12345678U) ==
          GXOS_STANDARD_HANDLE_INVALID_VALUE);
    CHECK(last_error == GXOS_STANDARD_HANDLE_ERROR_INVALID_HANDLE);

    stale_generation = stderr_handle ^ ((GXOS_SCHEDULER_HANDLE)1U << 16);
    CHECK(gxos_scheduler_object_from_handle(stale_generation) == 0);
    CHECK(gxos_scheduler_standard_stream_from_handle(stale_generation) == 0);
    CHECK((uint8_t)(stderr_handle >> 48) !=
          GXOS_SCHEDULER_OBJECT_EVENT);
    CHECK((uint8_t)(stderr_handle >> 48) !=
          GXOS_SCHEDULER_OBJECT_THREAD);
    CHECK(!gxos_scheduler_close_handle(stderr_handle));
    CHECK(gxos_scheduler_standard_stream_from_handle(stderr_handle) == stream);

    CHECK(gxos_scheduler_create_event(&g_scheduler, 1, 0, &event_handle));
    CHECK(gxos_scheduler_create_suspended_thread(
              &g_scheduler, model_entry, (void *)(uintptr_t)0x1234,
              &thread_handle, &thread));
    CHECK(gxos_scheduler_object_from_handle(event_handle)->type ==
          GXOS_SCHEDULER_OBJECT_EVENT);
    CHECK(gxos_scheduler_object_from_handle(thread_handle)->type ==
          GXOS_SCHEDULER_OBJECT_THREAD);
    CHECK(gxos_scheduler_close_handle(event_handle));
    CHECK(gxos_scheduler_try_destroy_event(event_handle));
    CHECK(gxos_scheduler_close_handle(thread_handle));
    CHECK(gxos_scheduler_discard_created_thread(thread));
    CHECK(gxos_scheduler_standard_stream_from_handle(stderr_handle) == stream);
    CHECK(object->generation == stream->generation && object->slot == stream->object_slot);
    CHECK(gxos_scheduler_teardown(&g_scheduler));

    last_error = 0xCAFEBABEU;
    CHECK(gxos_scheduler_initialize(&g_absent_scheduler, model_allocate,
                                    model_free, model_log_text, model_log_hex,
                                    model_log_u32));
    absent_context.scheduler = &g_absent_scheduler;
    absent_context.last_error = &last_error;
    absent_context.input_available = 0;
    absent_context.output_available = 0;
    absent_context.error_available = 0;
    absent_context.output_backend =
        GXOS_SCHEDULER_STANDARD_STREAM_BACKEND_NONE;
    absent_context.output_capabilities = 0;
    CHECK(gxos_get_std_handle_contract(
              &absent_context, GXOS_STANDARD_HANDLE_INPUT) == 0);
    CHECK(gxos_get_std_handle_contract(
              &absent_context, GXOS_STANDARD_HANDLE_OUTPUT) == 0);
    CHECK(gxos_get_std_handle_contract(
              &absent_context, GXOS_STANDARD_HANDLE_ERROR) == 0);
    CHECK(last_error == 0xCAFEBABEU);
    CHECK(gxos_scheduler_standard_handle_for_role(
              GXOS_SCHEDULER_STANDARD_STREAM_ROLE_ERROR) == 0);
    CHECK(gxos_scheduler_teardown(&g_absent_scheduler));

    CHECK(g_failures == 0);
    (void)printf("STANDARD_HANDLE_TESTS=PASSED checks=%u\n", g_checks);
    return g_failures == 0 ? 0 : 1;
}

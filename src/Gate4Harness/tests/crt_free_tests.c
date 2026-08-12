#include "../crt_malloc.h"
#include "../platform_processor_topology.h"

#include <stdint.h>
#include <stdio.h>
#include <string.h>

#define MOCK_BLOCK_COUNT GXOS_CRT_MALLOC_REGISTRY_CAPACITY
#define MOCK_BLOCK_SIZE 4096U

typedef struct MOCK_POOL {
    _Alignas(16) unsigned char blocks[MOCK_BLOCK_COUNT][MOCK_BLOCK_SIZE];
    uint32_t live[MOCK_BLOCK_COUNT];
    uint64_t requested[MOCK_BLOCK_COUNT];
    uint32_t allocation_count;
    uint32_t free_count;
    uint32_t fail_free;
} MOCK_POOL;

static GXOS_CRT_MALLOC_CONTEXT g_context;
static MOCK_POOL g_pool;
static uint32_t g_failures;

static void expect(int condition, const char *name)
{
    if (!condition) {
        fprintf(stderr, "CRT_FREE_TEST_FAILURE=%s\n", name);
        ++g_failures;
    }
}

static uint64_t GXOS_CRT_MALLOC_MS_ABI mock_allocate_pool(
    uint32_t pool_type,
    uintptr_t size,
    void **buffer,
    void *context)
{
    MOCK_POOL *pool = (MOCK_POOL *)context;
    uint32_t index;
    (void)pool_type;
    if (pool == 0 || buffer == 0 || size == 0 || size > MOCK_BLOCK_SIZE) {
        return ((uint64_t)1 << 63) | 2U;
    }
    for (index = 0; index != MOCK_BLOCK_COUNT; ++index) {
        if (!pool->live[index]) {
            pool->live[index] = 1;
            pool->requested[index] = size;
            pool->allocation_count++;
            *buffer = pool->blocks[index];
            return 0;
        }
    }
    return ((uint64_t)1 << 63) | 9U;
}

static uint64_t GXOS_CRT_MALLOC_MS_ABI mock_free_pool(
    void *buffer,
    void *context)
{
    MOCK_POOL *pool = (MOCK_POOL *)context;
    uint32_t index;
    if (pool == 0 || buffer == 0 || pool->fail_free) {
        return ((uint64_t)1 << 63) | 14U;
    }
    for (index = 0; index != MOCK_BLOCK_COUNT; ++index) {
        if (buffer == pool->blocks[index]) {
            if (!pool->live[index]) return ((uint64_t)1 << 63) | 14U;
            pool->live[index] = 0;
            pool->requested[index] = 0;
            pool->free_count++;
            return 0;
        }
    }
    return ((uint64_t)1 << 63) | 14U;
}

static void context_setup(void)
{
    memset(&g_context, 0, sizeof(g_context));
    memset(&g_pool, 0, sizeof(g_pool));
    gxos_crt_malloc_context_reset(&g_context);
    g_context.boot_services = &g_context;
    g_context.boot_services_available = 1;
    g_context.allocate_pool = mock_allocate_pool;
    g_context.free_pool = mock_free_pool;
    g_context.allocator_context = &g_pool;
    expect(gxos_crt_malloc_add_protected_range(
               &g_context, (uintptr_t)0x10000000U,
               (uintptr_t)0x10100000U, 1) == 0,
           "protected range setup");
}

static void *call_malloc(uint64_t size)
{
    return gxos_crt_malloc_call(
        &g_context, size, (uintptr_t)0x40004305U,
        (uintptr_t)0x180004300ULL);
}

static void call_free(void *pointer)
{
    gxos_crt_free_call(
        &g_context, pointer, (uintptr_t)0x40004305U,
        (uintptr_t)0x180004300ULL);
}

static const GXOS_CRT_FREE_DIAGNOSTIC *last_free(void)
{
    return gxos_crt_malloc_get_free_diagnostic(
        &g_context, g_context.free_diagnostic_count - 1U);
}

static void test_malloc_then_free(void)
{
    void *allocation;
    const GXOS_CRT_MALLOC_RECORD *record;
    const GXOS_CRT_MALLOC_RELEASE_RECORD *release;
    const GXOS_CRT_FREE_DIAGNOSTIC *diagnostic;
    uint64_t generation_before;

    context_setup();
    allocation = call_malloc(0x60);
    expect(allocation != 0, "malloc then free allocation");
    record = gxos_crt_malloc_find_live_record(&g_context,
                                               (uintptr_t)allocation);
    expect(record != 0 && record->requested_size == 0x60 &&
               record->backing_size == 0x60 &&
               record->owner == GXOS_CRT_MALLOC_OWNER_CRT &&
               record->allocation_class ==
                   GXOS_CRT_MALLOC_CLASS_PERSISTENT_POOL &&
               record->state == GXOS_CRT_MALLOC_RECORD_LIVE,
           "live record ownership");
    generation_before = g_context.accounting_generation;
    call_free(allocation);
    diagnostic = last_free();
    release = gxos_crt_malloc_find_release_record(
        &g_context, (uintptr_t)allocation);
    expect(diagnostic != 0 && diagnostic->failure ==
               GXOS_CRT_FREE_FAILURE_NONE &&
               diagnostic->backing_release_attempted == 1 &&
               diagnostic->backing_released == 1 &&
               diagnostic->live_count_before == 1 &&
               diagnostic->live_count_after == 0 &&
               diagnostic->total_requested_bytes_before == 0x60 &&
               diagnostic->total_requested_bytes_after == 0 &&
               diagnostic->record_state_before ==
                   GXOS_CRT_MALLOC_RECORD_LIVE &&
               diagnostic->record_state_after ==
                   GXOS_CRT_MALLOC_RECORD_FREED &&
               diagnostic->accounting_generation_before == generation_before &&
               diagnostic->accounting_generation_after == generation_before + 1,
           "successful free diagnostic");
    expect(g_context.live_count == 0 &&
               g_context.total_requested_bytes == 0 &&
               g_context.largest_request == 0 &&
               g_context.accounting_generation == generation_before + 1 &&
               g_pool.free_count == 1 &&
               gxos_crt_malloc_find_live_record(
                   &g_context, (uintptr_t)allocation) == 0 &&
               release != 0 && release->state == GXOS_CRT_MALLOC_RECORD_FREED &&
               gxos_crt_malloc_registry_valid(&g_context),
           "free clears live state and backing");
}

static void test_null_and_invalid_free(void)
{
    void *allocation;
    uint64_t generation_before;
    uint32_t free_count_before;

    context_setup();
    generation_before = g_context.accounting_generation;
    free_count_before = g_pool.free_count;
    call_free(0);
    expect(last_free()->failure == GXOS_CRT_FREE_FAILURE_NONE &&
               g_context.null_free_count == 1 &&
               g_context.accounting_generation == generation_before &&
               g_pool.free_count == free_count_before,
           "free null is no-op");
    allocation = call_malloc(32);
    expect(allocation != 0, "invalid free baseline");
    generation_before = g_context.accounting_generation;
    call_free((unsigned char *)allocation + 8);
    expect(last_free()->failure == GXOS_CRT_FREE_FAILURE_INTERIOR_POINTER &&
               g_context.live_count == 1 &&
               g_context.total_requested_bytes == 32 &&
               g_context.accounting_generation == generation_before &&
               g_pool.free_count == 0,
           "interior pointer does not release");
    call_free((void *)(uintptr_t)0x12345000U);
    expect(last_free()->failure == GXOS_CRT_FREE_FAILURE_UNKNOWN_POINTER &&
               g_context.live_count == 1 && g_pool.free_count == 0,
           "unknown pointer does not release");
    call_free(allocation);
}

static void test_independent_and_reverse_free(void)
{
    void *first;
    void *second;

    context_setup();
    first = call_malloc(8);
    second = call_malloc(16);
    expect(first != 0 && second != 0 && first != second,
           "independent allocations");
    call_free(first);
    expect(g_context.live_count == 1 &&
               g_context.total_requested_bytes == 16 &&
               gxos_crt_malloc_find_live_record(
                   &g_context, (uintptr_t)second) != 0,
           "free one leaves other live");
    call_free(second);
    expect(g_context.live_count == 0 && g_pool.free_count == 2,
           "reverse allocation order free");
}

static void test_double_free_and_backing_failure(void)
{
    void *allocation;

    context_setup();
    allocation = call_malloc(16);
    call_free(allocation);
    call_free(allocation);
    expect(last_free()->failure == GXOS_CRT_FREE_FAILURE_DOUBLE_FREE &&
               g_context.double_free_count == 1 &&
               g_pool.free_count == 1 && g_context.live_count == 0,
           "double free is safe");

    context_setup();
    allocation = call_malloc(16);
    g_pool.fail_free = 1;
    call_free(allocation);
    expect(last_free()->failure == GXOS_CRT_FREE_FAILURE_BACKING_RELEASE &&
               g_context.live_count == 1 &&
               gxos_crt_malloc_find_live_record(
                   &g_context, (uintptr_t)allocation) != 0 &&
               g_pool.free_count == 0,
           "backing failure preserves live allocation");
    g_pool.fail_free = 0;
    call_free(allocation);
}

static void test_zero_size_and_capacity_reuse(void)
{
    void *allocations[GXOS_CRT_MALLOC_REGISTRY_CAPACITY];
    void *replacement;
    uint32_t index;

    context_setup();
    expect(call_malloc(0) == 0 &&
               g_context.total_requested_bytes == 0 &&
               g_context.live_count == 0,
           "zero size malloc policy");
    call_free(0);
    expect(last_free()->failure == GXOS_CRT_FREE_FAILURE_NONE,
           "zero size free pairing");

    context_setup();
    for (index = 0; index != GXOS_CRT_MALLOC_REGISTRY_CAPACITY; ++index) {
        allocations[index] = call_malloc(1);
        expect(allocations[index] != 0, "capacity allocation");
    }
    call_free(allocations[0]);
    replacement = call_malloc(1);
    expect(replacement != 0 && g_context.live_count ==
               GXOS_CRT_MALLOC_REGISTRY_CAPACITY &&
               g_context.metadata_exhaustion_count == 0 &&
               g_context.next_allocation_sequence ==
                   GXOS_CRT_MALLOC_REGISTRY_CAPACITY + 2ULL,
           "capacity reuse after free");
    for (index = 1; index != GXOS_CRT_MALLOC_REGISTRY_CAPACITY; ++index) {
        call_free(allocations[index]);
    }
    call_free(replacement);
    expect(g_context.live_count == 0 && g_context.total_requested_bytes == 0 &&
               g_context.accounting_generation ==
                   1ULL + GXOS_CRT_MALLOC_REGISTRY_CAPACITY * 2ULL + 2ULL,
           "capacity reuse accounting generation");
}

static void test_topology_lifecycle(void)
{
    GXOS_PROCESSOR_TOPOLOGY_SNAPSHOT snapshot;
    GXOS_LOGICAL_PROCESSOR_INFORMATION records[3];
    GXOS_LOGICAL_PROCESSOR_INFORMATION *buffer;
    void *unrelated;
    uint32_t record_count = 0;
    uint32_t index;

    context_setup();
    expect(gxos_processor_topology_make_single_cpu(&snapshot, 1) ==
               GXOS_PROCESSOR_TOPOLOGY_STATUS_OK &&
               gxos_processor_topology_build_records(
                   &snapshot, records, 3, &record_count) ==
                   GXOS_PROCESSOR_TOPOLOGY_STATUS_OK && record_count == 3,
           "topology record construction");
    buffer = (GXOS_LOGICAL_PROCESSOR_INFORMATION *)call_malloc(0x60);
    unrelated = call_malloc(8);
    expect(buffer != 0 && unrelated != 0, "topology malloc pattern");
    if (buffer != 0) {
        memcpy(buffer, records, sizeof(records));
        for (index = 0; index != 3; ++index) {
            expect(memcmp(&buffer[index], &records[index], sizeof(records[index])) == 0,
                   "topology record consumed");
        }
    }
    call_free(buffer);
    expect(g_context.live_count == 1 &&
               g_context.total_requested_bytes == 8 &&
               gxos_crt_malloc_find_live_record(
                   &g_context, (uintptr_t)unrelated) != 0 &&
               g_pool.free_count == 1,
           "topology free preserves unrelated allocation");
    call_free(unrelated);
}

static void test_import_identity(void)
{
    expect(strcmp(GXOS_CRT_HEAP_API_SET_DLL,
                  "api-ms-win-crt-heap-l1-1-0.dll") == 0 &&
               strcmp(GXOS_CRT_HEAP_FREE_SYMBOL, "free") == 0 &&
               strcmp(GXOS_CRT_HEAP_MALLOC_SYMBOL, "malloc") == 0,
           "exact CRT heap import identity");
}

int main(void)
{
    test_malloc_then_free();
    test_null_and_invalid_free();
    test_independent_and_reverse_free();
    test_double_free_and_backing_failure();
    test_zero_size_and_capacity_reuse();
    test_topology_lifecycle();
    test_import_identity();
    if (g_failures != 0) {
        printf("CRT_FREE_HOST_FAILURES=%u\n", g_failures);
        return 1;
    }
    puts("CRT_FREE_HOST_TESTS=PASSED");
    return 0;
}

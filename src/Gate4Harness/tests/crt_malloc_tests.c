#include "../crt_malloc.h"
#include "crt_malloc_trace_fixture.h"

#include <inttypes.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#define EFI_ERROR_STATUS ((uint64_t)0x8000000000000001ULL)
#define MOCK_CAPACITY 256U
#define PAYLOAD_BASE ((uintptr_t)0x10000000U)
#define PAYLOAD_END ((uintptr_t)0x10010000U)
#define ONEXIT_BASE ((uintptr_t)0x20000000U)
#define ONEXIT_END ((uintptr_t)0x20000100U)
#define STACK_BASE ((uintptr_t)0x30000000U)
#define STACK_END ((uintptr_t)0x30100000U)

enum {
    MOCK_NORMAL = 0,
    MOCK_FAILURE,
    MOCK_NULL_SUCCESS,
    MOCK_UNALIGNED,
    MOCK_FORCED_POINTER
};

typedef struct MOCK_ALLOCATION {
    void *raw;
    void *returned;
    uint32_t freed;
} MOCK_ALLOCATION;

typedef struct MOCK_POOL {
    uint32_t mode;
    void *forced_pointer;
    uint64_t requested[MOCK_CAPACITY];
    MOCK_ALLOCATION allocations[MOCK_CAPACITY];
    uint32_t allocation_count;
    uint32_t free_count;
    uint32_t exact_size_failures;
    uint32_t memory_read_count;
} MOCK_POOL;

static GXOS_CRT_MALLOC_CONTEXT g_context;
static unsigned g_failures;

static void expect(int condition, const char *name)
{
    if (!condition) {
        printf("FAIL: %s\n", name);
        g_failures++;
    }
}

static uint64_t GXOS_CRT_MALLOC_MS_ABI mock_allocate_pool(
    uint32_t pool_type,
    uintptr_t size,
    void **buffer,
    void *opaque)
{
    MOCK_POOL *pool = (MOCK_POOL *)opaque;
    uintptr_t aligned;
    void *raw;

    if (pool_type != GXOS_CRT_MALLOC_EFI_LOADER_DATA ||
        size == 0 || pool->allocation_count >= MOCK_CAPACITY) {
        return EFI_ERROR_STATUS;
    }
    pool->requested[pool->allocation_count] = size;
    if (pool->mode == MOCK_FAILURE) return EFI_ERROR_STATUS;
    if (pool->mode == MOCK_NULL_SUCCESS) {
        *buffer = 0;
        pool->allocation_count++;
        return 0;
    }
    if (pool->mode == MOCK_FORCED_POINTER) {
        *buffer = pool->forced_pointer;
        pool->allocation_count++;
        return 0;
    }
    raw = malloc((size_t)size + 32U);
    if (raw == 0) return EFI_ERROR_STATUS;
    aligned = ((uintptr_t)raw + 15U) & ~(uintptr_t)15U;
    *buffer = (void *)aligned;
    memset(*buffer, 0xA5, (size_t)size);
    pool->allocations[pool->allocation_count].raw = raw;
    pool->allocations[pool->allocation_count].returned = *buffer;
    pool->allocations[pool->allocation_count].freed = 0;
    if (pool->mode == MOCK_UNALIGNED) {
        *buffer = (void *)(aligned + 1U);
        pool->allocations[pool->allocation_count].returned = *buffer;
    }
    pool->allocation_count++;
    return 0;
}

static uint64_t GXOS_CRT_MALLOC_MS_ABI mock_free_pool(
    void *buffer,
    void *opaque)
{
    MOCK_POOL *pool = (MOCK_POOL *)opaque;
    uint32_t index;

    pool->free_count++;
    if (pool->mode == MOCK_NULL_SUCCESS ||
        pool->mode == MOCK_FORCED_POINTER) return 0;
    for (index = 0; index != pool->allocation_count; index++) {
        if (pool->allocations[index].returned == buffer &&
            !pool->allocations[index].freed) {
            free(pool->allocations[index].raw);
            pool->allocations[index].freed = 1;
            return 0;
        }
    }
    return 0;
}

static void mock_cleanup(MOCK_POOL *pool)
{
    uint32_t index;
    for (index = 0; index != pool->allocation_count; index++) {
        if (pool->allocations[index].raw != 0 &&
            !pool->allocations[index].freed) {
            free(pool->allocations[index].raw);
            pool->allocations[index].freed = 1;
        }
    }
}

static void mock_reset(MOCK_POOL *pool)
{
    memset(pool, 0, sizeof(*pool));
}

static void context_setup(
    GXOS_CRT_MALLOC_CONTEXT *context,
    MOCK_POOL *pool)
{
    gxos_crt_malloc_context_reset(context);
    context->boot_services = context;
    context->boot_services_available = 1;
    context->allocate_pool = mock_allocate_pool;
    context->free_pool = mock_free_pool;
    context->allocator_context = pool;
    context->preferred_image_base = (uintptr_t)0x180000000ULL;
    context->image_base = (uintptr_t)0x40000000ULL;
    context->image_end = (uintptr_t)0x40100000ULL;
    expect(gxos_crt_malloc_add_protected_range(
               context, PAYLOAD_BASE, PAYLOAD_END, 1) == 0,
           "payload protected range setup");
    expect(gxos_crt_malloc_add_protected_range(
               context, ONEXIT_BASE, ONEXIT_END, 2) == 0,
           "on-exit protected range setup");
    expect(gxos_crt_malloc_add_protected_range(
               context, STACK_BASE, STACK_END, 3) == 0,
           "managed stack protected range setup");
}

static void *call_malloc(
    GXOS_CRT_MALLOC_CONTEXT *context,
    uint64_t size)
{
    return gxos_crt_malloc_call(
        context,
        size,
        (uintptr_t)0x4007833EU,
        (uintptr_t)0x180078339ULL);
}

static const GXOS_CRT_MALLOC_DIAGNOSTIC *last_diagnostic(
    const GXOS_CRT_MALLOC_CONTEXT *context)
{
    return gxos_crt_malloc_get_diagnostic(
        context,
        context->diagnostic_count - 1U);
}

static int records_equal(
    const GXOS_CRT_MALLOC_RECORD *left,
    const GXOS_CRT_MALLOC_RECORD *right)
{
    return memcmp(
               left,
               right,
               sizeof(GXOS_CRT_MALLOC_RECORD) * GXOS_CRT_MALLOC_REGISTRY_CAPACITY) ==
           0;
}

static void expect_failure_preserves_record(
    const char *name,
    uint64_t size,
    uint32_t mode,
    void *forced_pointer,
    GXOS_CRT_MALLOC_FAILURE expected_failure,
    uint32_t expected_rollbacks)
{
    MOCK_POOL pool;
    GXOS_CRT_MALLOC_RECORD snapshot[
        GXOS_CRT_MALLOC_REGISTRY_CAPACITY];
    uint32_t live_before;
    uint64_t total_before;
    uint64_t sequence_before;
    uint32_t allocations_before;
    void *existing;
    void *result;

    mock_reset(&pool);
    context_setup(&g_context, &pool);
    existing = call_malloc(&g_context, 88);
    expect(existing != 0, "negative vector baseline allocation");
    memcpy(snapshot, g_context.records, sizeof(snapshot));
    live_before = g_context.live_count;
    total_before = g_context.total_requested_bytes;
    sequence_before = g_context.next_allocation_sequence;
    allocations_before = pool.allocation_count;
    pool.mode = mode;
    pool.forced_pointer = forced_pointer;
    result = call_malloc(&g_context, size);
    expect(result == 0, name);
    expect(last_diagnostic(&g_context)->failure == expected_failure, name);
    expect(last_diagnostic(&g_context)->rollback_count == expected_rollbacks,
           name);
    expect(records_equal(snapshot, g_context.records), name);
    expect(g_context.live_count == live_before &&
               g_context.total_requested_bytes == total_before &&
               g_context.next_allocation_sequence == sequence_before,
           name);
    if (expected_rollbacks != 0) {
        expect(pool.free_count == expected_rollbacks, name);
    } else {
        expect(pool.allocation_count == allocations_before ||
                   mode == MOCK_FAILURE,
               name);
    }
    mock_cleanup(&pool);
}

static void test_canonical_replay(void)
{
    MOCK_POOL pool;
    uint64_t total = 0;
    uint32_t index;
    void *first = 0;

    mock_reset(&pool);
    context_setup(&g_context, &pool);
    for (index = 0; index != GXOS_CRT_MALLOC_CANONICAL_CALL_COUNT; index++) {
        const GXOS_CRT_MALLOC_DIAGNOSTIC *diagnostic;
        const GXOS_CRT_MALLOC_RECORD *record;
        void *allocation = call_malloc(
            &g_context, gxos_crt_malloc_canonical_sizes[index]);
        expect(allocation != 0, "canonical request accepted");
        expect(((uintptr_t)allocation & 7U) == 0, "canonical 8-byte alignment");
        expect(pool.requested[index] == gxos_crt_malloc_canonical_sizes[index],
               "canonical exact allocator size");
        record = gxos_crt_malloc_find_live_record(
            &g_context, (uintptr_t)allocation);
        expect(record != 0, "canonical committed ownership record");
        expect(record != 0 && record->requested_size ==
                   gxos_crt_malloc_canonical_sizes[index],
               "canonical recorded size");
        expect(record != 0 && record->allocation_sequence == (uint64_t)index + 1U,
               "canonical allocation sequence");
        total += gxos_crt_malloc_canonical_sizes[index];
        expect(g_context.live_count == index + 1U, "canonical live count");
        expect(g_context.total_requested_bytes == total, "canonical total");
        diagnostic = last_diagnostic(&g_context);
        expect(diagnostic != 0 && diagnostic->failure == GXOS_CRT_MALLOC_FAILURE_NONE,
               "canonical diagnostic success");
        expect(diagnostic != 0 && diagnostic->overlap_validation == 1,
               "canonical overlap validation");
        if (index == 0) first = allocation;
    }
    expect(g_context.invocation_count == GXOS_CRT_MALLOC_CANONICAL_CALL_COUNT,
           "canonical call count");
    expect(g_context.live_count == GXOS_CRT_MALLOC_CANONICAL_CALL_COUNT,
           "canonical live count after replay");
    expect(g_context.total_requested_bytes == 1054602ULL,
           "canonical total requested bytes");
    expect(g_context.largest_request == 0xC8000ULL,
           "canonical largest request");
    expect(g_context.max_live_allocation_count ==
               GXOS_CRT_MALLOC_CANONICAL_CALL_COUNT,
           "canonical maximum live count");
    expect(g_context.pool_rollback_count == 0, "canonical no rollback");
    expect(g_context.allocation_failure_count == 0, "canonical no failures");
    expect(pool.allocation_count == GXOS_CRT_MALLOC_CANONICAL_CALL_COUNT,
           "canonical allocator call count");
    expect(first != 0 && ((uint8_t *)first)[0] == 0xA5,
           "compatibility path did not clear returned storage");
    expect(pool.memory_read_count == 0,
           "compatibility diagnostics did not read returned storage");
    expect((uintptr_t)&g_context.records[0] < (uintptr_t)first ||
               (uintptr_t)&g_context.records[0] >=
                   (uintptr_t)first + 88U,
           "records are external to first returned block");
    mock_cleanup(&pool);
}

static void test_positive_contracts(void)
{
    static const uint64_t sizes[] = {1, 8, 30, 88, 64188, 147456, 0xC8000};
    MOCK_POOL pool;
    uint32_t index;
    void *previous = 0;

    mock_reset(&pool);
    context_setup(&g_context, &pool);
    for (index = 0; index != sizeof(sizes) / sizeof(sizes[0]); index++) {
        void *allocation = call_malloc(&g_context, sizes[index]);
        expect(allocation != 0, "positive size accepted");
        expect(allocation != previous, "positive pointers unique");
        expect((uintptr_t)allocation % 8U == 0, "positive 8-byte alignment");
        previous = allocation;
    }
    expect(call_malloc(&g_context, 8) != 0, "repeated equal-sized allocation one");
    expect(call_malloc(&g_context, 8) != 0, "repeated equal-sized allocation two");
    expect(g_context.live_count == 9, "positive live accounting");
    expect(g_context.total_requested_bytes ==
               1 + 8 + 30 + 88 + 64188 + 147456 + 0xC8000 + 8 + 8,
           "positive cumulative accounting");
    mock_cleanup(&pool);
}

static void test_capacity_and_state(void)
{
    MOCK_POOL pool;
    GXOS_CRT_MALLOC_CONTEXT second;
    MOCK_POOL second_pool;
    uint32_t index;
    void *result;

    mock_reset(&pool);
    context_setup(&g_context, &pool);
    for (index = 0; index != GXOS_CRT_MALLOC_REGISTRY_CAPACITY; index++) {
        expect(call_malloc(&g_context, 1) != 0, "capacity allocation");
    }
    expect(g_context.live_count == GXOS_CRT_MALLOC_REGISTRY_CAPACITY,
           "capacity live count");
    result = call_malloc(&g_context, 1);
    expect(result == 0, "registry exhaustion returns null");
    expect(pool.allocation_count == GXOS_CRT_MALLOC_REGISTRY_CAPACITY,
           "registry exhaustion avoids pool call");
    expect(g_context.metadata_exhaustion_count == 1,
           "registry exhaustion diagnostic count");
    expect(last_diagnostic(&g_context)->failure ==
               GXOS_CRT_MALLOC_FAILURE_METADATA_EXHAUSTED,
           "registry exhaustion reason");
    gxos_crt_malloc_context_reset(&g_context);
    expect(g_context.live_count == 0 && g_context.total_requested_bytes == 0 &&
               g_context.diagnostic_count == 0 &&
               g_context.next_allocation_sequence == 1,
           "reset clears metadata");
    mock_cleanup(&pool);

    mock_reset(&pool);
    mock_reset(&second_pool);
    context_setup(&g_context, &pool);
    context_setup(&second, &second_pool);
    expect(call_malloc(&g_context, 8) != 0, "independent context one");
    expect(call_malloc(&second, 8) != 0, "independent context two");
    expect(g_context.live_count == 1 && second.live_count == 1,
           "independent registry live counts");
    expect(g_context.invocation_count == 1 && second.invocation_count == 1,
           "independent invocation counts");
    mock_cleanup(&pool);
    mock_cleanup(&second_pool);
}

static void test_failures(void)
{
    MOCK_POOL pool;
    uint64_t sequence_before;
    void *result;

    expect_failure_preserves_record(
        "zero size rejected", 0, MOCK_NORMAL, 0,
        GXOS_CRT_MALLOC_FAILURE_ZERO_SIZE, 0);
    expect_failure_preserves_record(
        "size above evidence limit rejected", 0xC8001, MOCK_NORMAL, 0,
        GXOS_CRT_MALLOC_FAILURE_SIZE_LIMIT, 0);

    mock_reset(&pool);
    context_setup(&g_context, &pool);
    g_context.boot_services = 0;
    result = call_malloc(&g_context, 88);
    expect(result == 0, "missing BootServices pointer rejected");
    expect(last_diagnostic(&g_context)->failure ==
               GXOS_CRT_MALLOC_FAILURE_BOOT_SERVICES_UNAVAILABLE,
           "missing BootServices reason");
    mock_cleanup(&pool);

    mock_reset(&pool);
    context_setup(&g_context, &pool);
    g_context.allocate_pool = 0;
    result = call_malloc(&g_context, 88);
    expect(result == 0, "missing AllocatePool rejected");
    expect(last_diagnostic(&g_context)->failure ==
               GXOS_CRT_MALLOC_FAILURE_POOL_SERVICE_UNAVAILABLE,
           "missing AllocatePool reason");
    mock_cleanup(&pool);

    expect_failure_preserves_record(
        "pool allocation failure rejected", 88, MOCK_FAILURE, 0,
        GXOS_CRT_MALLOC_FAILURE_POOL_ALLOCATION, 0);
    expect_failure_preserves_record(
        "null pointer with success rejected", 88, MOCK_NULL_SUCCESS, 0,
        GXOS_CRT_MALLOC_FAILURE_NULL_SUCCESS, 1);
    expect_failure_preserves_record(
        "under-aligned pointer rejected", 88, MOCK_UNALIGNED, 0,
        GXOS_CRT_MALLOC_FAILURE_UNALIGNED, 1);
    expect_failure_preserves_record(
        "overflowing pointer range rejected", 88, MOCK_FORCED_POINTER,
        (void *)(UINTPTR_MAX - 7U),
        GXOS_CRT_MALLOC_FAILURE_RANGE_OVERFLOW, 1);
    expect_failure_preserves_record(
        "mapped payload overlap rejected", 88, MOCK_FORCED_POINTER,
        (void *)PAYLOAD_BASE,
        GXOS_CRT_MALLOC_FAILURE_PROTECTED_OVERLAP, 1);
    expect_failure_preserves_record(
        "on-exit table overlap rejected", 88, MOCK_FORCED_POINTER,
        (void *)ONEXIT_BASE,
        GXOS_CRT_MALLOC_FAILURE_PROTECTED_OVERLAP, 1);
    expect_failure_preserves_record(
        "managed stack overlap rejected", 88, MOCK_FORCED_POINTER,
        (void *)STACK_BASE,
        GXOS_CRT_MALLOC_FAILURE_PROTECTED_OVERLAP, 1);

    mock_reset(&pool);
    context_setup(&g_context, &pool);
    result = call_malloc(&g_context, 88);
    expect(result != 0, "duplicate baseline allocation");
    pool.mode = MOCK_FORCED_POINTER;
    pool.forced_pointer = result;
    expect(call_malloc(&g_context, 88) == 0,
           "duplicate live pointer rejected");
    expect(last_diagnostic(&g_context)->failure ==
               GXOS_CRT_MALLOC_FAILURE_DUPLICATE_POINTER,
           "duplicate live pointer reason");
    expect(g_context.duplicate_pointer_rejection_count == 1,
           "duplicate pointer diagnostic count");
    expect(pool.free_count == 1, "duplicate pointer rollback exactly once");
    expect(g_context.live_count == 1 &&
               gxos_crt_malloc_find_live_record(&g_context, (uintptr_t)result) != 0,
           "duplicate rejection preserves existing ownership");
    mock_cleanup(&pool);

    mock_reset(&pool);
    context_setup(&g_context, &pool);
    expect(call_malloc(&g_context, 8) != 0, "malformed-state baseline allocation");
    sequence_before = g_context.next_allocation_sequence;
    g_context.records[0].requested_size = 0;
    expect(gxos_crt_malloc_registry_valid(&g_context) == 0,
           "malformed registry detected");
    expect(call_malloc(&g_context, 8) == 0, "malformed registry returns null");
    expect(last_diagnostic(&g_context)->failure ==
               GXOS_CRT_MALLOC_FAILURE_MALFORMED_REGISTRY,
           "malformed registry reason");
    expect(pool.allocation_count == 1 &&
               g_context.next_allocation_sequence == sequence_before,
           "malformed registry does not allocate or consume sequence");
    mock_cleanup(&pool);

#if UINTPTR_MAX < UINT64_MAX
    mock_reset(&pool);
    context_setup(&g_context, &pool);
    expect(call_malloc(&g_context, UINT64_MAX) == 0,
           "non- UINTN size rejected");
    expect(last_diagnostic(&g_context)->failure ==
               GXOS_CRT_MALLOC_FAILURE_NOT_UINTN,
           "non-UINTN size reason");
    mock_cleanup(&pool);
#else
    puts("UINTN_UNREPRESENTABLE_NEGATIVE_TEST=NOT_APPLICABLE_X64");
#endif
}

static void test_sequence_and_diagnostics(void)
{
    MOCK_POOL pool;
    GXOS_CRT_MALLOC_CONTEXT snapshot;
    void *allocation;
    const GXOS_CRT_MALLOC_DIAGNOSTIC *diagnostic;

    mock_reset(&pool);
    context_setup(&g_context, &pool);
    allocation = call_malloc(&g_context, 88);
    expect(allocation != 0, "diagnostic baseline allocation");
    diagnostic = last_diagnostic(&g_context);
    expect(diagnostic->invocation_number == 1 &&
               diagnostic->static_call_site == (uintptr_t)0x180078339ULL &&
               diagnostic->runtime_call_site == (uintptr_t)0x4007833EU,
           "diagnostic call-site identity");
    expect(diagnostic->requested_size == 88 &&
               diagnostic->live_count_before == 0 &&
               diagnostic->registry_slot == 0 &&
               diagnostic->pool_service_available == 1 &&
               diagnostic->allocate_pool_status == 0 &&
               diagnostic->returned_pointer == (uintptr_t)allocation &&
               diagnostic->alignment_mod8 == 0 &&
               diagnostic->alignment_mod16 == 0 &&
               diagnostic->overlap_validation == 1 &&
               diagnostic->live_count_after == 1 &&
               diagnostic->rollback_count == 0 &&
               diagnostic->return_value == (uintptr_t)allocation,
           "diagnostic fields");
    memcpy(&snapshot, &g_context, sizeof(snapshot));
    expect(call_malloc(&g_context, 0) == 0, "failed call returns null");
    expect(g_context.records[0].pointer == snapshot.records[0].pointer &&
               g_context.records[0].allocation_sequence == 1 &&
               g_context.next_allocation_sequence == 2,
           "failed call preserves committed record and sequence");
    expect(call_malloc(&g_context, 8) != 0, "second successful call");
    expect(g_context.records[1].allocation_sequence == 2,
           "successful sequences are monotonic");
    expect(((uint8_t *)allocation)[0] == 0xA5,
           "diagnostics do not modify returned memory");
    mock_cleanup(&pool);
}

int main(void)
{
    test_positive_contracts();
    test_canonical_replay();
    test_capacity_and_state();
    test_failures();
    test_sequence_and_diagnostics();
    if (g_failures != 0) {
        printf("CRT_MALLOC_HOST_FAILURES=%u\n", g_failures);
        return 1;
    }
    puts("CRT_MALLOC_HOST_TESTS=PASSED");
    return 0;
}

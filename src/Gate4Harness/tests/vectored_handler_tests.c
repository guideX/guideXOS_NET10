#include "../vectored_handler.h"

#include <stdint.h>
#include <stdio.h>

static int failures;

static void expect(int condition, const char *message)
{
    if (!condition) {
        printf("VEH_TEST_FAILURE=%s\n", message);
        failures++;
    }
}

static int32_t GXOS_VEH_MS_ABI callback_a(
    GXOS_EXCEPTION_POINTERS_COMPAT *exception_pointers)
{
    (void)exception_pointers;
    return GXOS_EXCEPTION_CONTINUE_SEARCH;
}

static int32_t GXOS_VEH_MS_ABI callback_b(
    GXOS_EXCEPTION_POINTERS_COMPAT *exception_pointers)
{
    (void)exception_pointers;
    return GXOS_EXCEPTION_CONTINUE_SEARCH;
}

static int32_t GXOS_VEH_MS_ABI callback_c(
    GXOS_EXCEPTION_POINTERS_COMPAT *exception_pointers)
{
    (void)exception_pointers;
    return GXOS_EXCEPTION_CONTINUE_EXECUTION;
}

static int32_t GXOS_VEH_MS_ABI callback_invalid(
    GXOS_EXCEPTION_POINTERS_COMPAT *exception_pointers)
{
    (void)exception_pointers;
    return 1;
}

static void build_harness_image(GXOS_VEH_IMAGE *image)
{
    uintptr_t addresses[4] = {
        (uintptr_t)(void *)callback_a,
        (uintptr_t)(void *)callback_b,
        (uintptr_t)(void *)callback_c,
        (uintptr_t)(void *)callback_invalid
    };
    uintptr_t low = addresses[0];
    uintptr_t high = addresses[0];
    uint32_t i;

    for (i = 1; i != 4; i++) {
        if (addresses[i] < low) low = addresses[i];
        if (addresses[i] > high) high = addresses[i];
    }
    image->identity = image;
    image->image_base = (low & ~(uintptr_t)0xFFFU);
    image->image_size = (uint64_t)(high - image->image_base) + 0x1000U;
    image->section_count = 1;
    image->sections[0].base = image->image_base;
    image->sections[0].end = image->image_base + (uintptr_t)image->image_size;
    image->sections[0].characteristics = GXOS_VEH_SECTION_READABLE |
                                         GXOS_VEH_SECTION_EXECUTABLE;
    image->sections[0].name[0] = '.';
    image->sections[0].name[1] = 't';
    image->sections[0].name[2] = 'e';
    image->sections[0].name[3] = 'x';
    image->sections[0].name[4] = 't';
    image->sections[0].name[5] = 0;
}

static void configure_registry(
    GXOS_VEH_REGISTRY *registry,
    GXOS_VEH_IMAGE *image)
{
    const GXOS_VEH_IMAGE *images[1] = { image };
    gxos_veh_registry_init(registry);
    expect(gxos_veh_registry_configure_images(registry, images, 1),
           "image configuration");
}

static void test_initialization_and_order(void)
{
    GXOS_VEH_REGISTRY registry;
    GXOS_VEH_IMAGE image;
    GXOS_VEH_CALLBACK_DIAGNOSTICS diagnostics = {0};
    void *handles[8] = {0};
    uint32_t expected[8];
    uint32_t i;

    build_harness_image(&image);
    configure_registry(&registry, &image);
    expect(gxos_veh_registry_live_count(&registry) == 0, "initial live count");
    expect(gxos_veh_registry_dispatch_active(&registry) == 0, "initial dispatch active");
    expect(gxos_veh_registry_allocation_count(&registry) == 0, "initial allocation count");
    handles[0] = gxos_veh_registry_add(&registry, 0, callback_a, &diagnostics);
    handles[1] = gxos_veh_registry_add(&registry, 0, callback_b, &diagnostics);
    handles[2] = gxos_veh_registry_add(&registry, 1, callback_c, &diagnostics);
    handles[3] = gxos_veh_registry_add(&registry, 2, callback_invalid, &diagnostics);
    expect(handles[0] != 0 && handles[1] != 0 && handles[2] != 0 && handles[3] != 0,
           "ordered registration succeeds");
    expect(handles[0] != handles[1] && handles[1] != handles[2] &&
               handles[2] != handles[3], "handles unique");
    expect(gxos_veh_registry_handle_is_live(&registry, handles[0]), "handle live");
    expect(gxos_veh_registry_handle_is_live(&registry, handles[0]), "handle stable");
    expected[0] = 3;
    expected[1] = 2;
    expected[2] = 0;
    expected[3] = 1;
    for (i = 0; i != 4; i++) {
        expect(gxos_veh_registry_order_slot(&registry, i) == expected[i],
               "mixed first-last ordering");
    }
    for (i = 4; i != 8; i++) {
        handles[i] = gxos_veh_registry_add(&registry, 0, callback_a, &diagnostics);
        expect(handles[i] != 0, "capacity fill succeeds");
    }
    {
        uint32_t order_before[8];
        for (i = 0; i != 8; i++) order_before[i] = gxos_veh_registry_order_slot(&registry, i);
        expect(gxos_veh_registry_add(&registry, 0, callback_a, &diagnostics) == 0,
               "capacity exhaustion returns null");
        expect(diagnostics.validation == GXOS_VEH_VALIDATION_REGISTRY_FULL,
               "capacity exhaustion reason");
        for (i = 0; i != 8; i++) {
            expect(gxos_veh_registry_order_slot(&registry, i) == order_before[i],
                   "capacity failure preserves order");
        }
    }
    expect(registry.next_registration_sequence == 9, "monotonic sequence");
}

static void test_callback_validation(void)
{
    GXOS_VEH_REGISTRY registry;
    GXOS_VEH_IMAGE image;
    GXOS_VEH_CALLBACK_DIAGNOSTICS diagnostics = {0};
    uint8_t stack_memory[8] = {0};
    uintptr_t base;

    build_harness_image(&image);
    configure_registry(&registry, &image);
    expect(gxos_veh_registry_add(&registry, 0, 0, &diagnostics) == 0,
           "null callback rejected");
    expect(diagnostics.validation == GXOS_VEH_VALIDATION_NULL_CALLBACK,
           "null callback reason");
    expect(gxos_veh_registry_add(
               &registry, 0, (GXOS_VEH_CALLBACK)(uintptr_t)0x0000800000000000ULL,
               &diagnostics) == 0, "noncanonical callback rejected");
    expect(diagnostics.validation == GXOS_VEH_VALIDATION_NONCANONICAL_CALLBACK,
           "noncanonical callback reason");
    base = image.image_base;
    expect(gxos_veh_registry_add(
               &registry, 0, (GXOS_VEH_CALLBACK)(uintptr_t)(base - 1U), &diagnostics) == 0,
           "callback below image rejected");
    expect(gxos_veh_registry_add(
               &registry, 0, (GXOS_VEH_CALLBACK)(uintptr_t)(base + image.image_size),
               &diagnostics) == 0, "callback at image end rejected");
    image.sections[0].characteristics = GXOS_VEH_SECTION_READABLE;
    expect(gxos_veh_registry_add(&registry, 0, callback_a, &diagnostics) == 0,
           "non-executable callback rejected");
    expect(diagnostics.validation == GXOS_VEH_VALIDATION_NOT_EXECUTABLE,
           "non-executable reason");
    image.sections[0].characteristics = GXOS_VEH_SECTION_READABLE |
                                         GXOS_VEH_SECTION_EXECUTABLE |
                                         GXOS_VEH_SECTION_WRITABLE;
    expect(gxos_veh_registry_add(&registry, 0, callback_a, &diagnostics) == 0,
           "writable callback rejected");
    expect(diagnostics.validation == GXOS_VEH_VALIDATION_WRITABLE_SECTION,
           "writable reason");
    image.sections[0].characteristics = GXOS_VEH_SECTION_READABLE |
                                         GXOS_VEH_SECTION_EXECUTABLE;
    expect(gxos_veh_registry_add(
               &registry, 0, (GXOS_VEH_CALLBACK)(uintptr_t)stack_memory, &diagnostics) == 0,
           "stack callback rejected");
    expect(diagnostics.validation == GXOS_VEH_VALIDATION_NO_IMAGE,
           "stack callback reason");
    image.image_base = UINTPTR_MAX - 7U;
    image.image_size = 16;
    image.sections[0].base = image.image_base;
    image.sections[0].end = UINTPTR_MAX;
    expect(gxos_veh_registry_add(&registry, 0, callback_a, &diagnostics) == 0,
           "overflow image rejected");
    expect(diagnostics.validation == GXOS_VEH_VALIDATION_IMAGE_OVERFLOW,
           "overflow image reason");
    image.image_base = base;
    image.image_size = 0x1000;
    image.sections[0].base = base;
    image.sections[0].end = base + 0x1000;
    image.sections[0].characteristics = GXOS_VEH_SECTION_READABLE |
                                         GXOS_VEH_SECTION_EXECUTABLE;
    image.sections[1].base = base + 0x2000;
    image.sections[1].end = base + 0x1000;
    image.section_count = 2;
    expect(gxos_veh_registry_add(&registry, 0, callback_a, &diagnostics) == 0,
           "malformed section rejected");
    expect(diagnostics.validation == GXOS_VEH_VALIDATION_BAD_SECTION,
           "malformed section reason");
}

typedef struct {
    GXOS_VEH_REGISTRY *registry;
    uint32_t calls;
    uint32_t nested_rejected;
} INVOKE_STATE;

static int32_t invoke_probe(
    GXOS_VEH_CALLBACK callback,
    GXOS_EXCEPTION_POINTERS_COMPAT *exception_pointers,
    void *opaque)
{
    INVOKE_STATE *state = (INVOKE_STATE *)opaque;
    GXOS_VEH_CALLBACK_DIAGNOSTICS diagnostics = {0};
    GXOS_CONTEXT_COMPAT *context = (GXOS_CONTEXT_COMPAT *)exception_pointers->context_record;
    state->calls++;
    if (callback == callback_b) {
        if (gxos_veh_registry_add(state->registry, 0, callback_a, &diagnostics) == 0 &&
            diagnostics.validation == GXOS_VEH_VALIDATION_REGISTRY_ACTIVE) {
            state->nested_rejected = 1;
        }
        return GXOS_EXCEPTION_CONTINUE_SEARCH;
    }
    if (callback == callback_c) {
        context->rcx = 0xAA55AA55AA55AA55ULL;
        context->rdx = 0x55AA55AA55AA55AAULL;
        return GXOS_EXCEPTION_CONTINUE_EXECUTION;
    }
    return GXOS_EXCEPTION_CONTINUE_SEARCH;
}

static int32_t invoke_all_search(
    GXOS_VEH_CALLBACK callback,
    GXOS_EXCEPTION_POINTERS_COMPAT *exception_pointers,
    void *opaque)
{
    uint32_t *calls = (uint32_t *)opaque;
    (void)callback;
    (void)exception_pointers;
    (*calls)++;
    return GXOS_EXCEPTION_CONTINUE_SEARCH;
}

static int32_t invoke_invalid_then_search(
    GXOS_VEH_CALLBACK callback,
    GXOS_EXCEPTION_POINTERS_COMPAT *exception_pointers,
    void *opaque)
{
    uint32_t *calls = (uint32_t *)opaque;
    (void)exception_pointers;
    (*calls)++;
    return callback == callback_invalid ? 1 : GXOS_EXCEPTION_CONTINUE_SEARCH;
}

static void test_dispatch(void)
{
    GXOS_VEH_REGISTRY registry;
    GXOS_VEH_IMAGE image;
    GXOS_VEH_CALLBACK_DIAGNOSTICS diagnostics = {0};
    GXOS_EXCEPTION_RECORD_COMPAT record = {0};
    GXOS_CONTEXT_COMPAT context = {0};
    GXOS_EXCEPTION_POINTERS_COMPAT pointers = { &record, &context };
    GXOS_VEH_DISPATCH_REPORT report;
    INVOKE_STATE state;

    build_harness_image(&image);
    configure_registry(&registry, &image);
    expect(gxos_veh_registry_add(&registry, 1, callback_b, &diagnostics) != 0,
           "dispatch B registration");
    expect(gxos_veh_registry_add(&registry, 0, callback_c, &diagnostics) != 0,
           "dispatch C registration");
    expect(gxos_veh_registry_add(&registry, 0, callback_a, &diagnostics) != 0,
           "dispatch A registration");
    state.registry = &registry;
    state.calls = 0;
    state.nested_rejected = 0;
    expect(gxos_veh_dispatch(&registry, &pointers, invoke_probe, &state, &report),
           "dispatch succeeds");
    expect(report.snapshot_count == 3 && report.snapshot_slots[0] == 0 &&
               report.snapshot_slots[1] == 1 && report.snapshot_slots[2] == 2,
           "dispatch snapshot order");
    expect(report.invoked_count == 2 && report.invoked_slots[0] == 0 &&
               report.invoked_slots[1] == 1, "stop on continue execution");
    expect(report.return_values[0] == 0 && report.return_values[1] == -1,
           "dispatch returns");
    expect(registry.records[0].invocation_count == 1 &&
               registry.records[1].invocation_count == 1 &&
               registry.records[2].invocation_count == 0,
           "invocation counts");
    expect(registry.dispatch_active == 0 && state.nested_rejected != 0,
           "dispatch active cleanup and nested rejection");
    expect(context.rcx == 0xAA55AA55AA55AA55ULL &&
               context.rdx == 0x55AA55AA55AA55AAULL,
           "context changes retained for validation");

    gxos_veh_registry_reset_for_test(&registry);
    configure_registry(&registry, &image);
    expect(gxos_veh_registry_add(&registry, 0, callback_a, &diagnostics) != 0,
           "all-search A registration");
    expect(gxos_veh_registry_add(&registry, 0, callback_b, &diagnostics) != 0,
           "all-search B registration");
    {
        uint32_t calls = 0;
        expect(gxos_veh_dispatch(&registry, &pointers, invoke_all_search, &calls, &report),
               "all-search dispatch succeeds");
        expect(calls == 2 && report.final_continue_search != 0 &&
                   report.final_continue_execution == 0,
               "all-search reaches fatal disposition");
    }
    gxos_veh_registry_reset_for_test(&registry);
    configure_registry(&registry, &image);
    expect(gxos_veh_registry_add(&registry, 0, callback_invalid, &diagnostics) != 0,
           "invalid return registration");
    expect(gxos_veh_registry_add(&registry, 0, callback_a, &diagnostics) != 0,
           "invalid control registration");
    {
        uint32_t calls = 0;
        expect(gxos_veh_dispatch(
                   &registry, &pointers, invoke_invalid_then_search, &calls, &report),
               "invalid return dispatch succeeds");
        expect(calls == 2 && report.invalid_return_count == 1 &&
                   report.final_continue_search != 0,
               "invalid return treated as continue search");
    }
}

static void test_independent_contexts(void)
{
    GXOS_VEH_REGISTRY first;
    GXOS_VEH_REGISTRY second;
    GXOS_VEH_IMAGE first_image;
    GXOS_VEH_IMAGE second_image;
    GXOS_VEH_CALLBACK_DIAGNOSTICS diagnostics = {0};
    build_harness_image(&first_image);
    build_harness_image(&second_image);
    configure_registry(&first, &first_image);
    configure_registry(&second, &second_image);
    expect(gxos_veh_registry_add(&first, 0, callback_a, &diagnostics) != 0,
           "first independent context registration");
    expect(gxos_veh_registry_live_count(&first) == 1 &&
               gxos_veh_registry_live_count(&second) == 0,
           "independent context state");
    expect(gxos_veh_registry_add(&second, 0, callback_b, &diagnostics) != 0,
           "second independent context registration");
    expect(gxos_veh_registry_live_count(&first) == 1 &&
               gxos_veh_registry_live_count(&second) == 1,
           "independent context isolation");
}

int main(void)
{
    test_initialization_and_order();
    test_callback_validation();
    test_dispatch();
    test_independent_contexts();
    if (failures != 0) return 1;
    puts("VEH_HOST_TESTS=PASSED");
    return 0;
}

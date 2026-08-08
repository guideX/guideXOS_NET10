#include "scheduler_foundation.h"

/* The proof is intentionally single-instance and single-CPU. */
static GXOS_SCHEDULER *g_scheduler;

#ifdef GXOS_SCHEDULER_HOST_TEST
static uint64_t g_host_gs_base;
static uint64_t g_host_flags = 0x202U;
#endif

static void zero_bytes(void *destination, size_t count)
{
    uint8_t *bytes = (uint8_t *)destination;
    while (count-- != 0) *bytes++ = 0;
}

static uint64_t read_msr(uint32_t number)
{
#ifdef GXOS_SCHEDULER_HOST_TEST
    (void)number;
    return g_host_gs_base;
#else
    uint32_t low;
    uint32_t high;
    __asm__ volatile ("rdmsr" : "=a"(low), "=d"(high) : "c"(number));
    return ((uint64_t)high << 32) | low;
#endif
}

static void write_msr(uint32_t number, uint64_t value)
{
#ifdef GXOS_SCHEDULER_HOST_TEST
    (void)number;
    g_host_gs_base = value;
#else
    uint32_t low = (uint32_t)value;
    uint32_t high = (uint32_t)(value >> 32);
    __asm__ volatile ("wrmsr" : : "c"(number), "a"(low), "d"(high));
#endif
}

static uint64_t read_flags(void)
{
#ifdef GXOS_SCHEDULER_HOST_TEST
    return g_host_flags;
#else
    uint64_t flags;
    __asm__ volatile ("pushfq\n\tpopq %0" : "=r"(flags));
    return flags;
#endif
}

static void restore_boot_flags(uint64_t flags)
{
#ifdef GXOS_SCHEDULER_HOST_TEST
    g_host_flags = flags;
#else
    __asm__ volatile ("pushq %0\n\tpopfq" : : "r"(flags) : "cc");
#endif
}

static void set_gs_base(uint64_t value)
{
    write_msr(0xC0000101U, value);
}

static uint64_t page_allocate(GXOS_SCHEDULER *scheduler)
{
    uint64_t memory = 0;
    if (scheduler->allocate_pages == 0 ||
        scheduler->allocate_pages(0, 4, 1, &memory) != 0 || memory == 0) {
        return 0;
    }
    zero_bytes((void *)(uintptr_t)memory, GXOS_SCHEDULER_PAGE_SIZE);
    return memory;
}

static void page_free(GXOS_SCHEDULER *scheduler, uint64_t memory)
{
    if (memory != 0 && scheduler->free_pages != 0) {
        (void)scheduler->free_pages(memory, 1);
    }
}

static GXOS_SCHEDULER_HANDLE make_handle(uint8_t type, uint16_t slot,
                                         uint16_t generation)
{
    return ((uint64_t)GXOS_SCHEDULER_HANDLE_MAGIC << 56) |
           ((uint64_t)type << 48) |
           ((uint64_t)generation << 16) |
           ((uint64_t)slot + 1U);
}

static int decode_handle(GXOS_SCHEDULER_HANDLE handle, uint8_t expected_type,
                         uint16_t *slot, uint16_t *generation)
{
    uint8_t magic = (uint8_t)(handle >> 56);
    uint8_t type = (uint8_t)(handle >> 48);
    uint16_t decoded_slot = (uint16_t)(handle & 0xFFFFU);
    uint16_t decoded_generation = (uint16_t)(handle >> 16);
    if (magic != GXOS_SCHEDULER_HANDLE_MAGIC || type != expected_type ||
        decoded_slot == 0 || decoded_slot > GXOS_SCHEDULER_MAX_OBJECTS ||
        decoded_generation == 0) {
        return 0;
    }
    *slot = (uint16_t)(decoded_slot - 1U);
    *generation = decoded_generation;
    return 1;
}

static GXOS_SCHEDULER_OBJECT *lookup_object(GXOS_SCHEDULER_HANDLE handle,
                                             uint8_t expected_type)
{
    uint16_t slot;
    uint16_t generation;
    GXOS_SCHEDULER_OBJECT *object;
    if (g_scheduler == 0 || !decode_handle(handle, expected_type, &slot,
                                             &generation)) {
        return 0;
    }
    object = &g_scheduler->objects[slot];
    if (!object->live || object->type != expected_type ||
        object->generation != generation) {
        return 0;
    }
    return object;
}

static GXOS_SCHEDULER_OBJECT *allocate_object(uint8_t type, void *target,
                                               uint16_t *slot_out,
                                               GXOS_SCHEDULER_HANDLE *handle_out)
{
    uint32_t index;
    if (g_scheduler == 0) return 0;
    for (index = 0; index != GXOS_SCHEDULER_MAX_OBJECTS; ++index) {
        GXOS_SCHEDULER_OBJECT *object = &g_scheduler->objects[index];
        if (!object->live) {
            uint16_t generation = (uint16_t)(object->generation + 1U);
            if (generation == 0) generation = 1;
            zero_bytes(object, sizeof(*object));
            object->live = 1;
            object->type = type;
            object->generation = generation;
            object->slot = (uint16_t)index;
            object->public_handle_refs = 1;
            object->internal_refs = 1;
            object->target = target;
            *slot_out = (uint16_t)index;
            *handle_out = make_handle(type, (uint16_t)index, generation);
            return object;
        }
    }
    return 0;
}

static void release_object_record(GXOS_SCHEDULER_OBJECT *object)
{
    uint16_t generation;
    if (object == 0) return;
    generation = object->generation;
    zero_bytes(object, sizeof(*object));
    object->generation = generation;
}

static void set_canaries(GXOS_SCHEDULER_TCB *thread)
{
    uint32_t index;
    for (index = 0; index != GXOS_SCHEDULER_CANARY_BYTES; ++index) {
        thread->low_canary[index] = (uint8_t)(0xC1U + index);
        thread->high_canary[index] = (uint8_t)(0xD7U + index);
    }
    for (index = 0; index != GXOS_SCHEDULER_CANARY_BYTES; ++index) {
        ((uint8_t *)(uintptr_t)thread->stack_base)[index] = thread->low_canary[index];
        ((uint8_t *)(uintptr_t)thread->stack_limit - GXOS_SCHEDULER_CANARY_BYTES)[index] =
            thread->high_canary[index];
    }
}

int gxos_scheduler_check_canaries(const GXOS_SCHEDULER_TCB *thread)
{
    uint32_t index;
    if (thread == 0 || !thread->live || thread->stack_base == 0 ||
        thread->stack_limit <= thread->stack_base + GXOS_SCHEDULER_CANARY_BYTES) {
        return 0;
    }
    for (index = 0; index != GXOS_SCHEDULER_CANARY_BYTES; ++index) {
        if (((const uint8_t *)(uintptr_t)thread->stack_base)[index] !=
                thread->low_canary[index] ||
            ((const uint8_t *)(uintptr_t)thread->stack_limit -
             GXOS_SCHEDULER_CANARY_BYTES)[index] != thread->high_canary[index]) {
            return 0;
        }
    }
    return 1;
}

static int allocate_thread_environment(GXOS_SCHEDULER_TCB *thread)
{
    GXOS_SCHEDULER *scheduler = g_scheduler;
    uint64_t gs_area;
    uint64_t vector;
    uint64_t block;
    uint64_t teb;
    uint64_t *tls_vector;
    uint8_t *gs;
    uint8_t *teb_bytes;
    if (scheduler == 0) return 0;
    gs_area = page_allocate(scheduler);
    vector = page_allocate(scheduler);
    block = page_allocate(scheduler);
    teb = page_allocate(scheduler);
    if (gs_area == 0 || vector == 0 || block == 0 || teb == 0) {
        page_free(scheduler, gs_area);
        page_free(scheduler, vector);
        page_free(scheduler, block);
        page_free(scheduler, teb);
        return 0;
    }
    thread->gs_base = gs_area;
    thread->tls_vector_base = vector;
    thread->tls_block_base = block;
    thread->teb_base = teb;
    tls_vector = (uint64_t *)(uintptr_t)vector;
    gs = (uint8_t *)(uintptr_t)gs_area;
    teb_bytes = (uint8_t *)(uintptr_t)teb;
    tls_vector[0] = block;
    *(uint64_t *)(gs + 0x30) = teb;
    *(uint64_t *)(gs + 0x58) = vector;
    *(uint64_t *)(teb_bytes + 0x08) = thread->is_boot_thread
        ? scheduler->boot_stack_upper : thread->stack_limit;
    *(uint64_t *)(teb_bytes + 0x10) = thread->is_boot_thread
        ? scheduler->boot_stack_lower : thread->stack_base;
    *(uint64_t *)(teb_bytes + 0x100) = thread->identity;
    zero_bytes((void *)(uintptr_t)block, GXOS_SCHEDULER_PAGE_SIZE);
    thread->environment_owned = 1;
    return 1;
}

static void free_thread_environment(GXOS_SCHEDULER_TCB *thread)
{
    if (thread == 0 || g_scheduler == 0) return;
    if (thread->environment_owned) {
        page_free(g_scheduler, thread->gs_base);
        page_free(g_scheduler, thread->tls_vector_base);
        page_free(g_scheduler, thread->tls_block_base);
        page_free(g_scheduler, thread->teb_base);
    }
    thread->gs_base = 0;
    thread->tls_vector_base = 0;
    thread->tls_block_base = 0;
    thread->teb_base = 0;
    thread->environment_owned = 0;
}

static GXOS_SCHEDULER_TCB *find_free_thread(void)
{
    uint32_t index;
    for (index = 0; index != GXOS_SCHEDULER_MAX_THREADS; ++index) {
        if (!g_scheduler->threads[index].live) return &g_scheduler->threads[index];
    }
    return 0;
}

static GXOS_SCHEDULER_MEMORY_RESOURCE_NOTIFICATION *
find_free_memory_resource_notification(void)
{
    uint32_t index;
    for (index = 0;
         index != GXOS_SCHEDULER_MAX_MEMORY_RESOURCE_NOTIFICATIONS;
         ++index) {
        if (!g_scheduler->memory_resource_notifications[index].live) {
            return &g_scheduler->memory_resource_notifications[index];
        }
    }
    return 0;
}

static GXOS_SCHEDULER_TCB *pick_next_runnable(GXOS_SCHEDULER_TCB *current)
{
    uint32_t current_index = 0;
    uint32_t offset;
    uint32_t index;
    if (current == 0) return 0;
    current_index = (uint32_t)(current - g_scheduler->threads);
    for (offset = 1; offset <= GXOS_SCHEDULER_MAX_THREADS; ++offset) {
        index = (current_index + offset) % GXOS_SCHEDULER_MAX_THREADS;
        if (g_scheduler->threads[index].live &&
            g_scheduler->threads[index].state == GXOS_SCHEDULER_THREAD_RUNNABLE) {
            return &g_scheduler->threads[index];
        }
    }
    return 0;
}

static void choose_next(GXOS_SCHEDULER_TCB *next,
                        GXOS_SCHEDULER_SWITCH_PLAN *plan)
{
    GXOS_SCHEDULER_TCB *old = g_scheduler->current;
    next->state = GXOS_SCHEDULER_THREAD_RUNNING;
    g_scheduler->current = next;
    plan->old_context = &old->saved_context;
    plan->new_context = next->saved_context;
}

static int add_waiter(GXOS_SCHEDULER_EVENT *event, GXOS_SCHEDULER_TCB *thread)
{
    if (event->waiter_count >= GXOS_SCHEDULER_MAX_WAITERS) return 0;
    event->waiters[event->waiter_count++] = thread;
    return 1;
}

static void remove_waiter(GXOS_SCHEDULER_EVENT *event, uint32_t index)
{
    uint32_t tail;
    if (index >= event->waiter_count) return;
    tail = event->waiter_count - 1U;
    event->waiters[index] = event->waiters[tail];
    event->waiters[tail] = 0;
    event->waiter_count = tail;
}

static void wake_waiter(GXOS_SCHEDULER_EVENT *event, uint32_t index)
{
    GXOS_SCHEDULER_TCB *thread = event->waiters[index];
    remove_waiter(event, index);
    thread->blocked_object = 0;
    thread->blocked_result = GXOS_SCHEDULER_WAIT_SIGNALED;
    thread->state = GXOS_SCHEDULER_THREAD_RUNNABLE;
}

int gxos_scheduler_initialize(GXOS_SCHEDULER *scheduler,
                              GXOS_SCHEDULER_ALLOCATE_PAGES allocate_pages,
                              GXOS_SCHEDULER_FREE_PAGES free_pages,
                              GXOS_SCHEDULER_LOG_TEXT log_text,
                              GXOS_SCHEDULER_LOG_HEX log_hex,
                              GXOS_SCHEDULER_LOG_U32 log_u32)
{
    GXOS_SCHEDULER_TCB *boot;
    GXOS_SCHEDULER_OBJECT *object;
    GXOS_SCHEDULER_HANDLE unused_handle;
    uint16_t object_slot;
    uint64_t rsp;
    if (scheduler == 0 || allocate_pages == 0 || free_pages == 0) return 0;
    if ((g_scheduler != 0 && g_scheduler->active) || scheduler->active) {
        return 0;
    }
    zero_bytes(scheduler, sizeof(*scheduler));
    scheduler->allocate_pages = allocate_pages;
    scheduler->free_pages = free_pages;
    scheduler->log_text = log_text;
    scheduler->log_hex = log_hex;
    scheduler->log_u32 = log_u32;
    scheduler->next_identity = 1;
    scheduler->saved_boot_gs_base = read_msr(0xC0000101U);
    scheduler->saved_boot_flags = read_flags();
    __asm__ volatile ("mov %%rsp, %0" : "=r"(rsp));
    scheduler->boot_stack_lower = rsp & ~((uint64_t)GXOS_SCHEDULER_PAGE_SIZE - 1U);
    scheduler->boot_stack_upper = scheduler->boot_stack_lower + 0x100000U;
    g_scheduler = scheduler;

    boot = &scheduler->threads[0];
    zero_bytes(boot, sizeof(*boot));
    boot->live = 1;
    boot->is_boot_thread = 1;
    boot->identity = scheduler->next_identity++;
    boot->generation = 1;
    boot->state = GXOS_SCHEDULER_THREAD_RUNNING;
    boot->execution_refs = 1;
    object = allocate_object(GXOS_SCHEDULER_OBJECT_THREAD, boot, &object_slot,
                             &unused_handle);
    if (object == 0 || !allocate_thread_environment(boot)) {
        if (object != 0) release_object_record(object);
        free_thread_environment(boot);
        g_scheduler = 0;
        return 0;
    }
    boot->object_slot = object_slot;
    object->public_handle_refs = 0;
    boot->public_handle_refs = 0;
    boot->context.gs_base = boot->gs_base;
    boot->saved_context = &boot->context;
    scheduler->current = boot;
    scheduler->boot_thread = boot;
    scheduler->active = 1;
    set_gs_base(boot->gs_base);
    return 1;
}

int gxos_scheduler_adopt_boot_environment(GXOS_SCHEDULER *scheduler,
                                           uint64_t gs_base,
                                           uint64_t teb_base,
                                           uint64_t tls_vector_base,
                                           uint64_t tls_block_base,
                                           uint64_t stack_lower,
                                           uint64_t stack_upper)
{
    GXOS_SCHEDULER_TCB *boot;
    uint8_t *gs;
    uint8_t *teb;
    if (scheduler == 0 || scheduler != g_scheduler || !scheduler->active ||
        scheduler->boot_thread == 0 || scheduler->current != scheduler->boot_thread ||
        gs_base == 0 || teb_base == 0 || tls_vector_base == 0 ||
        tls_block_base == 0 || stack_upper <= stack_lower) {
        return 0;
    }
    boot = scheduler->boot_thread;
    free_thread_environment(boot);
    boot->gs_base = gs_base;
    boot->teb_base = teb_base;
    boot->tls_vector_base = tls_vector_base;
    boot->tls_block_base = tls_block_base;
    boot->environment_owned = 0;
    scheduler->boot_stack_lower = stack_lower;
    scheduler->boot_stack_upper = stack_upper;
    gs = (uint8_t *)(uintptr_t)gs_base;
    teb = (uint8_t *)(uintptr_t)teb_base;
    *(uint64_t *)(gs + 0x30) = teb_base;
    *(uint64_t *)(gs + 0x58) = tls_vector_base;
    *(uint64_t *)(teb + 0x08) = stack_upper;
    *(uint64_t *)(teb + 0x10) = stack_lower;
    boot->context.gs_base = gs_base;
    boot->saved_context = &boot->context;
    set_gs_base(gs_base);
    return 1;
}

int gxos_scheduler_create_event(GXOS_SCHEDULER *scheduler,
                                uint8_t manual_reset,
                                uint8_t initial_signaled,
                                GXOS_SCHEDULER_HANDLE *handle)
{
    uint32_t index;
    uint16_t object_slot;
    GXOS_SCHEDULER_OBJECT *object;
    GXOS_SCHEDULER_EVENT *event = 0;
    if (handle != 0) *handle = 0;
    if (scheduler == 0 || scheduler != g_scheduler || handle == 0) return 0;
    for (index = 0; index != GXOS_SCHEDULER_MAX_EVENTS; ++index) {
        if (!scheduler->events[index].live) {
            event = &scheduler->events[index];
            break;
        }
    }
    if (event == 0) return 0;
    zero_bytes(event, sizeof(*event));
    object = allocate_object(GXOS_SCHEDULER_OBJECT_EVENT, event, &object_slot,
                             handle);
    if (object == 0) {
        zero_bytes(event, sizeof(*event));
        return 0;
    }
    event->live = 1;
    event->manual_reset = manual_reset != 0;
    event->signaled = initial_signaled != 0;
    event->generation = object->generation;
    event->object_slot = object_slot;
    return 1;
}

int gxos_scheduler_create_memory_resource_notification(
    GXOS_SCHEDULER *scheduler,
    uint32_t notification_type,
    GXOS_SCHEDULER_HANDLE *handle)
{
    GXOS_SCHEDULER_MEMORY_RESOURCE_NOTIFICATION *notification;
    GXOS_SCHEDULER_OBJECT *object;
    uint16_t object_slot;

    if (handle != 0) *handle = 0;
    if (scheduler == 0 || scheduler != g_scheduler || !scheduler->active ||
        handle == 0 || notification_type != 0) {
        return 0;
    }
    notification = find_free_memory_resource_notification();
    if (notification == 0) return 0;
    object = allocate_object(
        GXOS_SCHEDULER_OBJECT_MEMORY_RESOURCE_NOTIFICATION,
        notification, &object_slot, handle);
    if (object == 0) return 0;

    zero_bytes(notification, sizeof(*notification));
    notification->live = 1;
    notification->generation = object->generation;
    notification->object_slot = object_slot;
    notification->registry_slot =
        (uint16_t)(notification - scheduler->memory_resource_notifications);
    notification->notification_type = notification_type;
    notification->waitable.live = 1;
    notification->waitable.manual_reset = 1;
    notification->waitable.signaled = 0;
    notification->waitable.generation = notification->generation;
    notification->waitable.object_slot = object_slot;
    /* Bootstrap is deliberately nonsignaled until a proven pressure model exists. */
    return 1;
}

int gxos_scheduler_create_suspended_thread(GXOS_SCHEDULER *scheduler,
                                            GXOS_SCHEDULER_ENTRY entry,
                                            void *argument,
                                            GXOS_SCHEDULER_HANDLE *handle,
                                            GXOS_SCHEDULER_TCB **thread_out)
{
    GXOS_SCHEDULER_TCB *thread;
    GXOS_SCHEDULER_OBJECT *object;
    uint16_t object_slot;
    uint64_t stack_memory;
    uint64_t stack_top;
    if (scheduler == 0 || scheduler != g_scheduler || entry == 0 ||
        handle == 0 || thread_out == 0) return 0;
    thread = find_free_thread();
    if (thread == 0) return 0;
    zero_bytes(thread, sizeof(*thread));
    thread->live = 1;
    thread->identity = scheduler->next_identity++;
    thread->generation = 1;
    thread->state = GXOS_SCHEDULER_THREAD_CREATED_SUSPENDED;
    thread->suspend_count = 1;
    thread->public_handle_refs = 1;
    thread->execution_refs = 1;
    thread->entry = entry;
    thread->entry_argument = argument;
    object = allocate_object(GXOS_SCHEDULER_OBJECT_THREAD, thread, &object_slot,
                             handle);
    if (object == 0) {
        zero_bytes(thread, sizeof(*thread));
        return 0;
    }
    thread->object_slot = object_slot;
    stack_memory = 0;
    if (scheduler->allocate_pages(0, 4, GXOS_SCHEDULER_STACK_PAGES,
                                  &stack_memory) != 0 || stack_memory == 0) {
        release_object_record(object);
        zero_bytes(thread, sizeof(*thread));
        return 0;
    }
    zero_bytes((void *)(uintptr_t)stack_memory, GXOS_SCHEDULER_STACK_SIZE);
    thread->stack_pages_memory = stack_memory;
    thread->stack_base = stack_memory;
    thread->stack_limit = stack_memory + GXOS_SCHEDULER_STACK_SIZE;
    set_canaries(thread);
    if (!allocate_thread_environment(thread)) {
        page_free(scheduler, stack_memory);
        release_object_record(object);
        zero_bytes(thread, sizeof(*thread));
        return 0;
    }
    stack_top = thread->stack_limit - GXOS_SCHEDULER_CANARY_BYTES;
    stack_top &= ~0xFULL;
    thread->initial_rsp = stack_top - 8U;
    *(uint64_t *)(uintptr_t)thread->initial_rsp =
        (uint64_t)(uintptr_t)gxos_scheduler_invalid_thread_return;
    zero_bytes(&thread->context, sizeof(thread->context));
    thread->context.rbx = GXOS_SCHEDULER_WORKER_GPR_SENTINEL(1);
    thread->context.rbp = GXOS_SCHEDULER_WORKER_GPR_SENTINEL(2);
    thread->context.rsi = GXOS_SCHEDULER_WORKER_GPR_SENTINEL(3);
    thread->context.rdi = GXOS_SCHEDULER_WORKER_GPR_SENTINEL(4);
    thread->context.r12 = (uint64_t)(uintptr_t)entry;
    thread->context.r13 = (uint64_t)(uintptr_t)argument;
    thread->context.r14 = GXOS_SCHEDULER_WORKER_GPR_SENTINEL(7);
    thread->context.r15 = GXOS_SCHEDULER_WORKER_GPR_SENTINEL(8);
    thread->context.rsp = thread->initial_rsp;
    thread->context.rip = (uint64_t)(uintptr_t)gxos_scheduler_start_worker;
    thread->context.rflags = 0x2U; /* IF=0, DF=0: cooperative critical-section policy. */
    thread->context.mxcsr = 0x3F80U;
    thread->context.x87_control = 0x077FU;
    thread->context.gs_base = thread->gs_base;
    thread->saved_context = &thread->context;
    *thread_out = thread;
    return 1;
}

int gxos_scheduler_resume_thread(GXOS_SCHEDULER_HANDLE handle,
                                 uint32_t *previous_suspend_count)
{
    GXOS_SCHEDULER_OBJECT *object = lookup_object(handle,
                                                   GXOS_SCHEDULER_OBJECT_THREAD);
    GXOS_SCHEDULER_TCB *thread;
    if (object == 0) return 0;
    thread = (GXOS_SCHEDULER_TCB *)object->target;
    if (thread == 0 || thread->state != GXOS_SCHEDULER_THREAD_CREATED_SUSPENDED ||
        thread->suspend_count == 0) return 0;
    if (previous_suspend_count != 0) *previous_suspend_count = thread->suspend_count;
    --thread->suspend_count;
    if (thread->suspend_count == 0) thread->state = GXOS_SCHEDULER_THREAD_RUNNABLE;
    return 1;
}

static void maybe_reclaim_thread(GXOS_SCHEDULER_TCB *thread)
{
    GXOS_SCHEDULER_OBJECT *object;
    if (thread == 0 || !thread->live || thread == g_scheduler->current ||
        thread->state != GXOS_SCHEDULER_THREAD_TERMINATED ||
        thread->execution_refs != 0 || thread->public_handle_refs != 0 ||
        !gxos_scheduler_check_canaries(thread)) return;
    object = &g_scheduler->objects[thread->object_slot];
    if (!object->live || object->type != GXOS_SCHEDULER_OBJECT_THREAD) return;
    object->internal_refs = 0;
    page_free(g_scheduler, thread->stack_pages_memory);
    thread->stack_pages_memory = 0;
    free_thread_environment(thread);
    release_object_record(object);
    zero_bytes(thread, sizeof(*thread));
}

int gxos_scheduler_close_handle(GXOS_SCHEDULER_HANDLE handle)
{
    uint8_t type = (uint8_t)(handle >> 48);
    GXOS_SCHEDULER_OBJECT *object;
    if (type != GXOS_SCHEDULER_OBJECT_THREAD && type != GXOS_SCHEDULER_OBJECT_EVENT) {
        return 0;
    }
    object = lookup_object(handle, type);
    if (object == 0 || object->public_handle_refs == 0) return 0;
    --object->public_handle_refs;
    object->close_state = 1;
    if (type == GXOS_SCHEDULER_OBJECT_THREAD) {
        GXOS_SCHEDULER_TCB *thread = (GXOS_SCHEDULER_TCB *)object->target;
        if (thread != 0 && thread->public_handle_refs != 0) --thread->public_handle_refs;
        maybe_reclaim_thread(thread);
    }
    return 1;
}

int gxos_scheduler_signal_event(GXOS_SCHEDULER_HANDLE handle)
{
    GXOS_SCHEDULER_OBJECT *object = lookup_object(handle,
                                                   GXOS_SCHEDULER_OBJECT_EVENT);
    GXOS_SCHEDULER_EVENT *event;
    if (object == 0) return 0;
    event = (GXOS_SCHEDULER_EVENT *)object->target;
    if (event == 0 || !event->live) return 0;
    if (event->manual_reset) {
        event->signaled = 1;
        while (event->waiter_count != 0) wake_waiter(event, 0);
    } else if (event->waiter_count != 0) {
        event->signaled = 0;
        wake_waiter(event, 0);
    } else {
        event->signaled = 1;
    }
    return 1;
}

int gxos_scheduler_reset_event(GXOS_SCHEDULER_HANDLE handle)
{
    GXOS_SCHEDULER_OBJECT *object = lookup_object(handle,
                                                   GXOS_SCHEDULER_OBJECT_EVENT);
    GXOS_SCHEDULER_EVENT *event;
    if (object == 0) return 0;
    event = (GXOS_SCHEDULER_EVENT *)object->target;
    if (event == 0 || !event->live) return 0;
    event->signaled = 0;
    return 1;
}

int gxos_scheduler_prepare_wait(GXOS_SCHEDULER_HANDLE handle,
                                GXOS_SCHEDULER_SWITCH_PLAN *plan)
{
    GXOS_SCHEDULER_WAITABLE *waitable =
        gxos_scheduler_waitable_from_handle(handle);
    GXOS_SCHEDULER_TCB *current;
    GXOS_SCHEDULER_TCB *next;
    if (waitable == 0 || plan == 0) return GXOS_SCHEDULER_WAIT_FAILURE;
    current = g_scheduler->current;
    if (!waitable->live || current == 0 ||
        current->state != GXOS_SCHEDULER_THREAD_RUNNING) {
        return GXOS_SCHEDULER_WAIT_FAILURE;
    }
    if (waitable->signaled) {
        if (!waitable->manual_reset) waitable->signaled = 0;
        current->blocked_result = GXOS_SCHEDULER_WAIT_SIGNALED;
        return GXOS_SCHEDULER_WAIT_SIGNALED;
    }
    if (!add_waiter(waitable, current)) return GXOS_SCHEDULER_WAIT_FAILURE;
    current->blocked_object = handle;
    current->blocked_result = GXOS_SCHEDULER_WAIT_FAILURE;
    current->state = GXOS_SCHEDULER_THREAD_BLOCKED;
    next = pick_next_runnable(current);
    if (next == 0) {
        current->state = GXOS_SCHEDULER_THREAD_RUNNING;
        current->blocked_object = 0;
        remove_waiter(waitable, waitable->waiter_count - 1U);
        return GXOS_SCHEDULER_WAIT_FAILURE;
    }
    choose_next(next, plan);
    return GXOS_SCHEDULER_WAIT_BLOCKED;
}

int gxos_scheduler_finish_wait(GXOS_SCHEDULER_HANDLE handle)
{
    GXOS_SCHEDULER_TCB *current = g_scheduler == 0 ? 0 : g_scheduler->current;
    if (current == 0 || current->blocked_result != GXOS_SCHEDULER_WAIT_SIGNALED ||
        (handle != 0 && current->blocked_object != 0 &&
         current->blocked_object != handle)) return GXOS_SCHEDULER_WAIT_FAILURE;
    current->blocked_object = 0;
    current->blocked_result = GXOS_SCHEDULER_WAIT_FAILURE;
    return GXOS_SCHEDULER_WAIT_SIGNALED;
}


int gxos_scheduler_prepare_yield(GXOS_SCHEDULER_SWITCH_PLAN *plan)
{
    GXOS_SCHEDULER_TCB *current;
    GXOS_SCHEDULER_TCB *next;
    if (g_scheduler == 0 || plan == 0 || g_scheduler->current == 0) return 0;
    current = g_scheduler->current;
    if (current->state != GXOS_SCHEDULER_THREAD_RUNNING) return 0;
    current->state = GXOS_SCHEDULER_THREAD_RUNNABLE;
    next = pick_next_runnable(current);
    if (next == 0) {
        current->state = GXOS_SCHEDULER_THREAD_RUNNING;
        return 0;
    }
    choose_next(next, plan);
    g_scheduler->pending_plan.old_context = plan->old_context;
    g_scheduler->pending_plan.new_context = plan->new_context;
    return 1;
}

GXOS_SCHEDULER_SWITCH_PLAN *gxos_scheduler_pending_plan(void)
{
    return g_scheduler == 0 ? 0 : &g_scheduler->pending_plan;
}

int gxos_scheduler_prepare_terminate(uintptr_t return_value,
                                     GXOS_SCHEDULER_SWITCH_PLAN *plan)
{
    GXOS_SCHEDULER_TCB *current;
    GXOS_SCHEDULER_TCB *next;
    if (g_scheduler == 0 || plan == 0 || g_scheduler->current == 0) return 0;
    current = g_scheduler->current;
    if (current->state != GXOS_SCHEDULER_THREAD_RUNNING || current->is_boot_thread) return 0;
    current->return_value = return_value;
    current->state = GXOS_SCHEDULER_THREAD_TERMINATED;
    current->deferred_reclaim = 1;
    current->execution_refs = 0;
    next = pick_next_runnable(current);
    if (next == 0) return 0;
    choose_next(next, plan);
    return 1;
}

int gxos_scheduler_thread_is_terminated(const GXOS_SCHEDULER_TCB *thread)
{
    return thread != 0 && thread->live &&
           thread->state == GXOS_SCHEDULER_THREAD_TERMINATED;
}

int gxos_scheduler_event_is_signaled(GXOS_SCHEDULER_HANDLE handle)
{
    GXOS_SCHEDULER_OBJECT *object = lookup_object(handle,
                                                   GXOS_SCHEDULER_OBJECT_EVENT);
    GXOS_SCHEDULER_EVENT *event;
    if (object == 0) return 0;
    event = (GXOS_SCHEDULER_EVENT *)object->target;
    return event != 0 && event->live && event->signaled;
}

int gxos_scheduler_try_destroy_event(GXOS_SCHEDULER_HANDLE handle)
{
    GXOS_SCHEDULER_OBJECT *object = lookup_object(handle,
                                                   GXOS_SCHEDULER_OBJECT_EVENT);
    GXOS_SCHEDULER_EVENT *event;
    if (object == 0) return 0;
    event = (GXOS_SCHEDULER_EVENT *)object->target;
    if (event == 0 || event->waiter_count != 0 || object->public_handle_refs != 0) {
        return 0;
    }
    event->live = 0;
    object->internal_refs = 0;
    release_object_record(object);
    return 1;
}

int gxos_scheduler_try_destroy_memory_resource_notification(
    GXOS_SCHEDULER_HANDLE handle)
{
    GXOS_SCHEDULER_OBJECT *object = lookup_object(
        handle, GXOS_SCHEDULER_OBJECT_MEMORY_RESOURCE_NOTIFICATION);
    GXOS_SCHEDULER_MEMORY_RESOURCE_NOTIFICATION *notification;
    if (object == 0) return 0;
    notification = (GXOS_SCHEDULER_MEMORY_RESOURCE_NOTIFICATION *)object->target;
    if (notification == 0 || notification->waitable.waiter_count != 0 ||
        object->public_handle_refs != 0) {
        return 0;
    }
    notification->close_state = 1;
    notification->live = 0;
    object->internal_refs = 0;
    release_object_record(object);
    return 1;
}

int gxos_scheduler_try_reclaim_thread(GXOS_SCHEDULER_TCB *thread)
{
    if (thread == 0 || !thread->live || thread->execution_refs != 0 ||
        thread->state == GXOS_SCHEDULER_THREAD_BLOCKED ||
        thread->state != GXOS_SCHEDULER_THREAD_TERMINATED) return 0;
    maybe_reclaim_thread(thread);
    return !thread->live;
}

int gxos_scheduler_discard_created_thread(GXOS_SCHEDULER_TCB *thread)
{
    if (thread == 0 || !thread->live ||
        (thread->state != GXOS_SCHEDULER_THREAD_CREATED_SUSPENDED &&
         thread->state != GXOS_SCHEDULER_THREAD_RUNNABLE) ||
        thread->public_handle_refs != 0) return 0;
    thread->state = GXOS_SCHEDULER_THREAD_TERMINATED;
    thread->execution_refs = 0;
    maybe_reclaim_thread(thread);
    return !thread->live;
}

int gxos_scheduler_collect(GXOS_SCHEDULER *scheduler)
{
    uint32_t index;
    if (scheduler == 0 || scheduler != g_scheduler) return 0;
    for (index = 0; index != GXOS_SCHEDULER_MAX_THREADS; ++index) {
        maybe_reclaim_thread(&scheduler->threads[index]);
    }
    return 1;
}

int gxos_scheduler_teardown(GXOS_SCHEDULER *scheduler)
{
    uint32_t index;
    int success = 1;
    if (scheduler == 0 || scheduler != g_scheduler || !scheduler->active ||
        scheduler->current != scheduler->boot_thread) return 0;
    if (scheduler->boot_thread->state != GXOS_SCHEDULER_THREAD_RUNNING) return 0;
    (void)gxos_scheduler_collect(scheduler);
    for (index = 0; index != GXOS_SCHEDULER_MAX_EVENTS; ++index) {
        if (scheduler->events[index].live) success = 0;
    }
    for (index = 0;
         index != GXOS_SCHEDULER_MAX_MEMORY_RESOURCE_NOTIFICATIONS;
         ++index) {
        if (scheduler->memory_resource_notifications[index].live) success = 0;
    }
    for (index = 1; index != GXOS_SCHEDULER_MAX_THREADS; ++index) {
        if (scheduler->threads[index].live) success = 0;
    }
    if (success) {
        GXOS_SCHEDULER_OBJECT *boot_object =
            &scheduler->objects[scheduler->boot_thread->object_slot];
        free_thread_environment(scheduler->boot_thread);
        boot_object->internal_refs = 0;
        release_object_record(boot_object);
        zero_bytes(scheduler->boot_thread, sizeof(*scheduler->boot_thread));
        set_gs_base(scheduler->saved_boot_gs_base);
        restore_boot_flags(scheduler->saved_boot_flags);
        scheduler->current = 0;
        scheduler->boot_thread = 0;
        scheduler->active = 0;
        g_scheduler = 0;
    }
    return success;
}

GXOS_SCHEDULER_TCB *gxos_scheduler_current_thread(void)
{
    return g_scheduler == 0 ? 0 : g_scheduler->current;
}

GXOS_SCHEDULER_TCB *gxos_scheduler_thread_from_handle(GXOS_SCHEDULER_HANDLE handle)
{
    GXOS_SCHEDULER_OBJECT *object = lookup_object(handle,
                                                   GXOS_SCHEDULER_OBJECT_THREAD);
    return object == 0 ? 0 : (GXOS_SCHEDULER_TCB *)object->target;
}

GXOS_SCHEDULER_EVENT *gxos_scheduler_event_from_handle(GXOS_SCHEDULER_HANDLE handle)
{
    GXOS_SCHEDULER_OBJECT *object = lookup_object(handle,
                                                   GXOS_SCHEDULER_OBJECT_EVENT);
    return object == 0 ? 0 : (GXOS_SCHEDULER_EVENT *)object->target;
}

GXOS_SCHEDULER_MEMORY_RESOURCE_NOTIFICATION *
gxos_scheduler_memory_resource_notification_from_handle(
    GXOS_SCHEDULER_HANDLE handle)
{
    GXOS_SCHEDULER_OBJECT *object = lookup_object(
        handle, GXOS_SCHEDULER_OBJECT_MEMORY_RESOURCE_NOTIFICATION);
    return object == 0
        ? 0
        : (GXOS_SCHEDULER_MEMORY_RESOURCE_NOTIFICATION *)object->target;
}

GXOS_SCHEDULER_OBJECT *gxos_scheduler_object_from_handle(
    GXOS_SCHEDULER_HANDLE handle)
{
    uint8_t type = (uint8_t)(handle >> 48);
    if (type != GXOS_SCHEDULER_OBJECT_THREAD &&
        type != GXOS_SCHEDULER_OBJECT_EVENT &&
        type != GXOS_SCHEDULER_OBJECT_MEMORY_RESOURCE_NOTIFICATION) {
        return 0;
    }
    return lookup_object(handle, type);
}

GXOS_SCHEDULER_WAITABLE *gxos_scheduler_waitable_from_handle(
    GXOS_SCHEDULER_HANDLE handle)
{
    GXOS_SCHEDULER_OBJECT *object = gxos_scheduler_object_from_handle(handle);
    if (object == 0) return 0;
    if (object->type == GXOS_SCHEDULER_OBJECT_EVENT) {
        return (GXOS_SCHEDULER_WAITABLE *)object->target;
    }
    if (object->type == GXOS_SCHEDULER_OBJECT_MEMORY_RESOURCE_NOTIFICATION) {
        GXOS_SCHEDULER_MEMORY_RESOURCE_NOTIFICATION *notification =
            (GXOS_SCHEDULER_MEMORY_RESOURCE_NOTIFICATION *)object->target;
        return notification == 0 ? 0 : &notification->waitable;
    }
    return 0;
}

uint64_t gxos_scheduler_current_gs_base(void)
{
    return read_msr(0xC0000101U);
}

uint64_t gxos_scheduler_current_teb_base(void)
{
#ifdef GXOS_SCHEDULER_HOST_TEST
    return g_scheduler == 0 || g_scheduler->current == 0
        ? 0 : g_scheduler->current->teb_base;
#else
    uint64_t value;
    __asm__ volatile ("movq %%gs:0x30, %0" : "=r"(value));
    return value;
#endif
}

uint64_t gxos_scheduler_current_tls_vector(void)
{
#ifdef GXOS_SCHEDULER_HOST_TEST
    return g_scheduler == 0 || g_scheduler->current == 0
        ? 0 : g_scheduler->current->tls_vector_base;
#else
    uint64_t value;
    __asm__ volatile ("movq %%gs:0x58, %0" : "=r"(value));
    return value;
#endif
}

uint64_t gxos_scheduler_current_tls_block(void)
{
    uint64_t vector = gxos_scheduler_current_tls_vector();
    return vector == 0 ? 0 : *(uint64_t *)(uintptr_t)vector;
}

uint64_t gxos_scheduler_gs_tls_read(void)
{
    uint64_t block = gxos_scheduler_current_tls_block();
    return block == 0 ? 0 : *(uint64_t *)(uintptr_t)(block + GXOS_SCHEDULER_TLS_OFFSET);
}

void gxos_scheduler_gs_tls_write(uint64_t value)
{
    uint64_t block = gxos_scheduler_current_tls_block();
    if (block != 0) *(uint64_t *)(uintptr_t)(block + GXOS_SCHEDULER_TLS_OFFSET) = value;
}

void gxos_scheduler_set_fls(uint32_t slot, uintptr_t value)
{
    if (g_scheduler != 0 && g_scheduler->current != 0 &&
        slot < GXOS_SCHEDULER_FLS_SLOTS) {
        g_scheduler->current->fls_allocated[slot] = 1;
        g_scheduler->current->fls_values[slot] = (uint64_t)value;
    }
}

uintptr_t gxos_scheduler_get_fls(uint32_t slot)
{
    if (g_scheduler == 0 || g_scheduler->current == 0 ||
        slot >= GXOS_SCHEDULER_FLS_SLOTS ||
        !g_scheduler->current->fls_allocated[slot]) return 0;
    return (uintptr_t)g_scheduler->current->fls_values[slot];
}

void gxos_scheduler_set_last_error(uint32_t value)
{
    if (g_scheduler != 0 && g_scheduler->current != 0) {
        g_scheduler->current->last_error = value;
    }
}

uint32_t gxos_scheduler_get_last_error(void)
{
    return g_scheduler == 0 || g_scheduler->current == 0
        ? 0 : g_scheduler->current->last_error;
}

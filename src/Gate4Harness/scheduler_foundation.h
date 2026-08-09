#ifndef GXOS_SCHEDULER_FOUNDATION_H
#define GXOS_SCHEDULER_FOUNDATION_H

#include <stdint.h>
#include <stddef.h>

#if defined(__x86_64__)
#define GXOS_SCHEDULER_MS_ABI __attribute__((ms_abi))
#else
#define GXOS_SCHEDULER_MS_ABI
#endif

/*
 * Synthetic Gate4 scheduler contract.
 *
 * This header is deliberately private to the freestanding proof harness.  It
 * is not an import-routing or Windows-compatibility header.
 */
#define GXOS_SCHEDULER_MAX_THREADS 6U
#define GXOS_SCHEDULER_MAX_EVENTS 12U
#define GXOS_SCHEDULER_MAX_MEMORY_RESOURCE_NOTIFICATIONS 1U
#define GXOS_SCHEDULER_MAX_OBJECTS 16U
#define GXOS_SCHEDULER_MAX_WAITERS GXOS_SCHEDULER_MAX_THREADS
#define GXOS_SCHEDULER_FLS_SLOTS 64U
#define GXOS_SCHEDULER_TLS_VECTOR_SLOTS 512U
#define GXOS_SCHEDULER_PAGE_SIZE 4096U
#define GXOS_SCHEDULER_STACK_SIZE 16384U
#define GXOS_SCHEDULER_STACK_PAGES (GXOS_SCHEDULER_STACK_SIZE / GXOS_SCHEDULER_PAGE_SIZE)
#define GXOS_SCHEDULER_CANARY_BYTES 16U
#define GXOS_SCHEDULER_TLS_OFFSET 0x100U
#define GXOS_SCHEDULER_FLS_PROOF_SLOT 7U
#define GXOS_SCHEDULER_HANDLE_MAGIC 0xA7U
#define GXOS_SCHEDULER_DEFAULT_RELATIVE_PRIORITY 0
#define GXOS_SCHEDULER_SUPPORTED_RELATIVE_PRIORITY 2

#define GXOS_SCHEDULER_MAIN_GPR_SENTINEL(index) \
    (0x4D4D000000000000ULL + (uint64_t)(index))
#define GXOS_SCHEDULER_WORKER_GPR_SENTINEL(index) \
    (0x5757000000000000ULL + (uint64_t)(index))

typedef uint64_t (GXOS_SCHEDULER_MS_ABI *GXOS_SCHEDULER_ALLOCATE_PAGES)(
    uint32_t type, uint32_t memory_type, uint64_t pages, uint64_t *memory);
typedef uint64_t (GXOS_SCHEDULER_MS_ABI *GXOS_SCHEDULER_FREE_PAGES)(
    uint64_t memory, uint64_t pages);
typedef void (GXOS_SCHEDULER_MS_ABI *GXOS_SCHEDULER_LOG_TEXT)(const char *text);
typedef void (GXOS_SCHEDULER_MS_ABI *GXOS_SCHEDULER_LOG_HEX)(const char *name,
                                                               uint64_t value);
typedef void (GXOS_SCHEDULER_MS_ABI *GXOS_SCHEDULER_LOG_U32)(const char *name,
                                                              uint32_t value);

typedef struct __attribute__((aligned(16))) {
    uint64_t rbx;
    uint64_t rbp;
    uint64_t rsi;
    uint64_t rdi;
    uint64_t r12;
    uint64_t r13;
    uint64_t r14;
    uint64_t r15;
    uint64_t rsp;
    uint64_t rip;
    uint64_t rflags;
    uint32_t mxcsr;
    uint16_t x87_control;
    uint16_t reserved;
    uint64_t gs_base;
    uint8_t xmm[10][16]; /* XMM6 through XMM15. */
} GXOS_SCHEDULER_CONTEXT;

_Static_assert(offsetof(GXOS_SCHEDULER_CONTEXT, rbx) == 0x00,
               "scheduler context RBX offset");
_Static_assert(offsetof(GXOS_SCHEDULER_CONTEXT, rsp) == 0x40,
               "scheduler context RSP offset");
_Static_assert(offsetof(GXOS_SCHEDULER_CONTEXT, rip) == 0x48,
               "scheduler context RIP offset");
_Static_assert(offsetof(GXOS_SCHEDULER_CONTEXT, rflags) == 0x50,
               "scheduler context RFLAGS offset");
_Static_assert(offsetof(GXOS_SCHEDULER_CONTEXT, mxcsr) == 0x58,
               "scheduler context MXCSR offset");
_Static_assert(offsetof(GXOS_SCHEDULER_CONTEXT, x87_control) == 0x5C,
               "scheduler context x87 offset");
_Static_assert(offsetof(GXOS_SCHEDULER_CONTEXT, gs_base) == 0x60,
               "scheduler context GS offset");
_Static_assert(offsetof(GXOS_SCHEDULER_CONTEXT, xmm) == 0x68,
               "scheduler context XMM offset");
_Static_assert(sizeof(GXOS_SCHEDULER_CONTEXT) == 272,
               "scheduler context size");

typedef struct {
    uint64_t gpr[8]; /* RBX, RBP, RSI, RDI, R12, R13, R14, R15. */
    uint8_t xmm[10][16];
    uint32_t mxcsr;
    uint16_t x87_control;
    uint16_t reserved;
    uint64_t rsp;
    uint64_t rflags;
    uint64_t gs_base;
} GXOS_SCHEDULER_REGISTER_SNAPSHOT;

typedef enum {
    GXOS_SCHEDULER_THREAD_FREE = 0,
    GXOS_SCHEDULER_THREAD_CREATED_SUSPENDED = 1,
    GXOS_SCHEDULER_THREAD_RUNNABLE = 2,
    GXOS_SCHEDULER_THREAD_RUNNING = 3,
    GXOS_SCHEDULER_THREAD_BLOCKED = 4,
    GXOS_SCHEDULER_THREAD_TERMINATED = 5
} GXOS_SCHEDULER_THREAD_STATE;

typedef enum {
    GXOS_SCHEDULER_OBJECT_FREE = 0,
    GXOS_SCHEDULER_OBJECT_THREAD = 1,
    GXOS_SCHEDULER_OBJECT_EVENT = 2,
    GXOS_SCHEDULER_OBJECT_MEMORY_RESOURCE_NOTIFICATION = 3
} GXOS_SCHEDULER_OBJECT_TYPE;

typedef enum {
    GXOS_SCHEDULER_WAIT_FAILURE = -1,
    GXOS_SCHEDULER_WAIT_BLOCKED = 0,
    GXOS_SCHEDULER_WAIT_SIGNALED = 1
} GXOS_SCHEDULER_WAIT_RESULT;

typedef uint64_t GXOS_SCHEDULER_HANDLE;
typedef uintptr_t (GXOS_SCHEDULER_MS_ABI *GXOS_SCHEDULER_ENTRY)(void *argument);

struct GXOS_SCHEDULER;
struct GXOS_SCHEDULER_TCB;
struct GXOS_SCHEDULER_MEMORY_RESOURCE_NOTIFICATION;

typedef struct {
    GXOS_SCHEDULER_CONTEXT **old_context;
    GXOS_SCHEDULER_CONTEXT *new_context;
} GXOS_SCHEDULER_SWITCH_PLAN;

typedef struct {
    uint8_t live;
    uint8_t type;
    uint8_t close_state;
    uint8_t reserved;
    uint16_t generation;
    uint16_t slot;
    uint32_t public_handle_refs;
    uint32_t internal_refs;
    void *target;
} GXOS_SCHEDULER_OBJECT;

typedef struct GXOS_SCHEDULER_WAITABLE {
    uint8_t live;
    uint8_t manual_reset;
    uint8_t signaled;
    uint8_t reserved;
    uint16_t generation;
    uint16_t object_slot;
    uint32_t waiter_count;
    struct GXOS_SCHEDULER_TCB *waiters[GXOS_SCHEDULER_MAX_WAITERS];
} GXOS_SCHEDULER_WAITABLE;

typedef GXOS_SCHEDULER_WAITABLE GXOS_SCHEDULER_EVENT;

typedef struct GXOS_SCHEDULER_MEMORY_RESOURCE_NOTIFICATION {
    uint8_t live;
    uint8_t close_state;
    uint16_t generation;
    uint16_t object_slot;
    uint16_t registry_slot;
    uint32_t notification_type;
    GXOS_SCHEDULER_WAITABLE waitable;
} GXOS_SCHEDULER_MEMORY_RESOURCE_NOTIFICATION;

typedef struct GXOS_SCHEDULER_TCB {
    uint8_t live;
    uint8_t is_boot_thread;
    uint8_t close_state;
    uint8_t environment_owned;
    uint32_t identity;
    uint16_t generation;
    uint16_t object_slot;
    GXOS_SCHEDULER_THREAD_STATE state;
    uint8_t runnable_queued;
    uint8_t reserved_state[3];
    uint32_t suspend_count;
    int32_t relative_priority;
    uint32_t public_handle_refs;
    uint32_t execution_refs;
    uint64_t execution_count;
    uint32_t deferred_reclaim;
    GXOS_SCHEDULER_CONTEXT context;
    GXOS_SCHEDULER_CONTEXT *saved_context;
    GXOS_SCHEDULER_ENTRY entry;
    void *entry_argument;
    uintptr_t return_value;
    uint64_t stack_base;
    uint64_t stack_limit;
    uint64_t initial_rsp;
    uint64_t stack_pages_memory;
    uint64_t teb_base;
    uint64_t gs_base;
    uint64_t tls_vector_base;
    uint64_t tls_block_base;
    uint64_t fls_values[GXOS_SCHEDULER_FLS_SLOTS];
    uint8_t fls_allocated[GXOS_SCHEDULER_FLS_SLOTS];
    uint32_t last_error;
    GXOS_SCHEDULER_HANDLE blocked_object;
    GXOS_SCHEDULER_WAIT_RESULT blocked_result;
    uint8_t low_canary[GXOS_SCHEDULER_CANARY_BYTES];
    uint8_t high_canary[GXOS_SCHEDULER_CANARY_BYTES];
} GXOS_SCHEDULER_TCB;

typedef struct GXOS_SCHEDULER {
    GXOS_SCHEDULER_ALLOCATE_PAGES allocate_pages;
    GXOS_SCHEDULER_FREE_PAGES free_pages;
    GXOS_SCHEDULER_LOG_TEXT log_text;
    GXOS_SCHEDULER_LOG_HEX log_hex;
    GXOS_SCHEDULER_LOG_U32 log_u32;
    GXOS_SCHEDULER_TCB threads[GXOS_SCHEDULER_MAX_THREADS];
    GXOS_SCHEDULER_EVENT events[GXOS_SCHEDULER_MAX_EVENTS];
    GXOS_SCHEDULER_MEMORY_RESOURCE_NOTIFICATION
        memory_resource_notifications[GXOS_SCHEDULER_MAX_MEMORY_RESOURCE_NOTIFICATIONS];
    GXOS_SCHEDULER_OBJECT objects[GXOS_SCHEDULER_MAX_OBJECTS];
    GXOS_SCHEDULER_TCB *runnable_queue[GXOS_SCHEDULER_MAX_THREADS];
    uint32_t runnable_count;
    GXOS_SCHEDULER_TCB *current;
    GXOS_SCHEDULER_TCB *boot_thread;
    uint32_t next_identity;
    uint16_t next_generation;
    uint16_t reserved;
    uint64_t saved_boot_gs_base;
    uint64_t saved_boot_flags;
    uint64_t boot_stack_lower;
    uint64_t boot_stack_upper;
    uint8_t active;
    GXOS_SCHEDULER_SWITCH_PLAN pending_plan;
} GXOS_SCHEDULER;

/* Assembly ABI.  The switch saves the post-call RSP and the call RIP. */
void gxos_scheduler_context_switch(GXOS_SCHEDULER_CONTEXT **old_context,
                                   GXOS_SCHEDULER_CONTEXT *new_context);
void gxos_scheduler_capture_registers(GXOS_SCHEDULER_REGISTER_SNAPSHOT *snapshot);
void gxos_scheduler_main_block(GXOS_SCHEDULER_HANDLE event,
                               GXOS_SCHEDULER_REGISTER_SNAPSHOT *snapshot,
                               int32_t *wait_result);
void gxos_scheduler_main_dispatch(GXOS_SCHEDULER_REGISTER_SNAPSHOT *snapshot);
void gxos_scheduler_worker_wait(GXOS_SCHEDULER_HANDLE event,
                                GXOS_SCHEDULER_REGISTER_SNAPSHOT *snapshot,
                                int32_t *wait_result);
GXOS_SCHEDULER_SWITCH_PLAN *gxos_scheduler_pending_plan(void);
void gxos_scheduler_set_worker_sentinels(void);
void gxos_scheduler_capture_worker_sentinels(
    GXOS_SCHEDULER_REGISTER_SNAPSHOT *snapshot);
void gxos_scheduler_start_worker(void);
void gxos_scheduler_note_worker_started(void);
void gxos_scheduler_invalid_thread_return(void);

int gxos_scheduler_initialize(GXOS_SCHEDULER *scheduler,
                              GXOS_SCHEDULER_ALLOCATE_PAGES allocate_pages,
                              GXOS_SCHEDULER_FREE_PAGES free_pages,
                              GXOS_SCHEDULER_LOG_TEXT log_text,
                              GXOS_SCHEDULER_LOG_HEX log_hex,
                              GXOS_SCHEDULER_LOG_U32 log_u32);
int gxos_scheduler_adopt_boot_environment(GXOS_SCHEDULER *scheduler,
                                           uint64_t gs_base,
                                           uint64_t teb_base,
                                           uint64_t tls_vector_base,
                                           uint64_t tls_block_base,
                                           uint64_t stack_lower,
                                           uint64_t stack_upper);
int gxos_scheduler_create_event(GXOS_SCHEDULER *scheduler,
                                uint8_t manual_reset,
                                uint8_t initial_signaled,
                                GXOS_SCHEDULER_HANDLE *handle);
int gxos_scheduler_create_memory_resource_notification(
    GXOS_SCHEDULER *scheduler,
    uint32_t notification_type,
    GXOS_SCHEDULER_HANDLE *handle);
int gxos_scheduler_create_suspended_thread(GXOS_SCHEDULER *scheduler,
                                            GXOS_SCHEDULER_ENTRY entry,
                                            void *argument,
                                            GXOS_SCHEDULER_HANDLE *handle,
                                            GXOS_SCHEDULER_TCB **thread_out);
int gxos_scheduler_resume_thread(GXOS_SCHEDULER_HANDLE handle,
                                 uint32_t *previous_suspend_count);
int gxos_scheduler_validate_thread_context(const GXOS_SCHEDULER_TCB *thread);
uint32_t gxos_scheduler_runnable_count(void);
uint32_t gxos_scheduler_runnable_position(const GXOS_SCHEDULER_TCB *thread);
int gxos_scheduler_is_runnable_queued(const GXOS_SCHEDULER_TCB *thread);
int gxos_scheduler_close_handle(GXOS_SCHEDULER_HANDLE handle);
int gxos_scheduler_signal_event(GXOS_SCHEDULER_HANDLE handle);
int gxos_scheduler_reset_event(GXOS_SCHEDULER_HANDLE handle);
int gxos_scheduler_prepare_wait(GXOS_SCHEDULER_HANDLE handle,
                                GXOS_SCHEDULER_SWITCH_PLAN *plan);
int gxos_scheduler_finish_wait(GXOS_SCHEDULER_HANDLE handle);
int gxos_scheduler_prepare_yield(GXOS_SCHEDULER_SWITCH_PLAN *plan);
int gxos_scheduler_prepare_terminate(uintptr_t return_value,
                                     GXOS_SCHEDULER_SWITCH_PLAN *plan);
int gxos_scheduler_thread_is_terminated(const GXOS_SCHEDULER_TCB *thread);
int gxos_scheduler_event_is_signaled(GXOS_SCHEDULER_HANDLE handle);
int gxos_scheduler_try_destroy_event(GXOS_SCHEDULER_HANDLE handle);
int gxos_scheduler_try_destroy_memory_resource_notification(
    GXOS_SCHEDULER_HANDLE handle);
int gxos_scheduler_try_reclaim_thread(GXOS_SCHEDULER_TCB *thread);
int gxos_scheduler_check_canaries(const GXOS_SCHEDULER_TCB *thread);
int gxos_scheduler_discard_created_thread(GXOS_SCHEDULER_TCB *thread);
int gxos_scheduler_collect(GXOS_SCHEDULER *scheduler);
int gxos_scheduler_teardown(GXOS_SCHEDULER *scheduler);

GXOS_SCHEDULER_TCB *gxos_scheduler_current_thread(void);
GXOS_SCHEDULER_TCB *gxos_scheduler_thread_from_handle(GXOS_SCHEDULER_HANDLE handle);
GXOS_SCHEDULER_EVENT *gxos_scheduler_event_from_handle(GXOS_SCHEDULER_HANDLE handle);
GXOS_SCHEDULER_MEMORY_RESOURCE_NOTIFICATION *
gxos_scheduler_memory_resource_notification_from_handle(
    GXOS_SCHEDULER_HANDLE handle);
GXOS_SCHEDULER_OBJECT *gxos_scheduler_object_from_handle(
    GXOS_SCHEDULER_HANDLE handle);
GXOS_SCHEDULER_WAITABLE *gxos_scheduler_waitable_from_handle(
    GXOS_SCHEDULER_HANDLE handle);
uint64_t gxos_scheduler_current_gs_base(void);
uint64_t gxos_scheduler_current_teb_base(void);
uint64_t gxos_scheduler_current_tls_vector(void);
uint64_t gxos_scheduler_current_tls_block(void);
uint64_t gxos_scheduler_gs_tls_read(void);
void gxos_scheduler_gs_tls_write(uint64_t value);
void gxos_scheduler_set_fls(uint32_t slot, uintptr_t value);
uintptr_t gxos_scheduler_get_fls(uint32_t slot);
void gxos_scheduler_set_last_error(uint32_t value);
uint32_t gxos_scheduler_get_last_error(void);

/* The QEMU-only proof entry point. */
int gxos_synthetic_scheduler_proof(
    GXOS_SCHEDULER_ALLOCATE_PAGES allocate_pages,
    GXOS_SCHEDULER_FREE_PAGES free_pages,
    GXOS_SCHEDULER_LOG_TEXT log_text,
    GXOS_SCHEDULER_LOG_HEX log_hex,
    GXOS_SCHEDULER_LOG_U32 log_u32);

#endif

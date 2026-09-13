#ifndef GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE_H
#define GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE_H

#include <stdint.h>
#include "nativeaot_callback_bridge.h"
#include "nativeaot_gc_probe_contract.h"
#include "scheduler_foundation.h"

#if defined(__x86_64__)
#define GXOS_PHASE53O_MS_ABI __attribute__((ms_abi))
#else
#define GXOS_PHASE53O_MS_ABI
#endif

typedef void (GXOS_PHASE53O_MS_ABI *GXOS_PHASE53O_LOG_TEXT)(
    const char *text);
typedef void (GXOS_PHASE53O_MS_ABI *GXOS_PHASE53O_LOG_HEX)(
    const char *name, uint64_t value);
typedef void (GXOS_PHASE53O_MS_ABI *GXOS_PHASE53O_SET_PHASE)(
    uint32_t phase);
typedef void (GXOS_PHASE53O_MS_ABI *GXOS_PHASE53O_FLS_CLEANUP_CALLBACK)(
    void *value);

typedef struct {
    GXOS_SCHEDULER *scheduler;
    GXOS_SCHEDULER_TCB *main_thread;
    GXOS_NATIVEAOT_CALLBACK_BRIDGE *callback_bridge;
    GXOS_NATIVEAOT_CALLBACK_BRIDGE *gc_bridge;
    uint32_t runtime_fls_slot;
    GXOS_PHASE53O_FLS_CLEANUP_CALLBACK runtime_fls_cleanup;
    uint64_t main_tls_block;
    uint32_t *vm_region_count;
    GXOS_PHASE53O_LOG_TEXT log_text;
    GXOS_PHASE53O_LOG_HEX log_hex;
    GXOS_PHASE53O_SET_PHASE phase_in_managed;
    GXOS_PHASE53O_SET_PHASE phase_after_managed;
} GXOS_PHASE53O_PROBE;

int gxos_nativeaot_scheduler_thread_lifecycle_probe(
    GXOS_PHASE53O_PROBE *probe);

#endif

#ifndef GXOS_NATIVEAOT_MANAGED_WORKER_API_INTERNAL_H
#define GXOS_NATIVEAOT_MANAGED_WORKER_API_INTERNAL_H

#include "nativeaot_managed_worker_api.h"

typedef struct {
    GXOS_PHASE53O_PROBE *probe;
    GXOS_NATIVEAOT_CALLBACK_BRIDGE *callback_bridge;
    GXOS_NATIVEAOT_CALLBACK_BRIDGE *gc_bridge;
    GXOS_NATIVEAOT_MANAGED_WORKER_API_RECORD records[
        GXOS_NATIVEAOT_MANAGED_WORKER_API_CAPACITY];
    GXOS_NATIVEAOT_MANAGED_WORKER_RESULT last_results[
        GXOS_NATIVEAOT_MANAGED_WORKER_API_CAPACITY];
    GXOS_NATIVEAOT_MANAGED_WORKER_HANDLE last_closed[
        GXOS_NATIVEAOT_MANAGED_WORKER_API_CAPACITY];
    uint8_t has_last_closed[GXOS_NATIVEAOT_MANAGED_WORKER_API_CAPACITY];
    uint8_t concurrent_mode;
    uint8_t runtime_overlap_observed;
    uint8_t completion_count;
    uint8_t reserved[5];
    GXOS_NATIVEAOT_MANAGED_WORKER_HANDLE completion_order[
        GXOS_NATIVEAOT_MANAGED_WORKER_API_CAPACITY];
    uint32_t peak_vm;
    uint32_t peak_threads;
    uint32_t peak_objects;
    uint32_t peak_api_workers;
    uint32_t peak_roots;
    uint32_t max_attached_workers;
    uint32_t max_root_workers;
    uint32_t stale_root_token;
    GXOS_NATIVEAOT_MANAGED_WORKER_HANDLE attach_failure_handle;
    uint8_t phase64_mode;
    uint8_t attach_failure_requested;
    uint8_t attach_failure_fired;
    uint8_t attach_failure_runtime_acquired;
    uint8_t phase64_b_root_release_peer_live;
    uint8_t phase64_c_root_release_peer_live;
    uint8_t phase64_stale_root_rejection;
    uint8_t phase64_reserved;
    uint32_t attach_failure_detach_count;
    uint8_t phase65_mode;
    uint8_t phase65_checkpoint_ready;
    uint8_t phase65_cancel_observed;
    uint8_t phase65_release_peers;
    uint8_t phase65_peer_a_held;
    uint8_t phase65_peer_c_held;
    uint8_t phase65_reserved[3];
    GXOS_NATIVEAOT_MANAGED_WORKER_HANDLE phase65_target;
    GXOS_NATIVEAOT_MANAGED_WORKER_HANDLE phase65_peer_a;
    GXOS_NATIVEAOT_MANAGED_WORKER_HANDLE phase65_peer_c;
    uint32_t phase65_scenarios_passed;
    uint32_t phase65_peak_api_workers;
    uint32_t phase65_peak_vm;
    uint32_t phase65_peak_threads;
    uint32_t phase65_peak_objects;
    uint32_t phase65_peak_roots;
} GXOS_NATIVEAOT_MANAGED_WORKER_API_CONTEXT;

#endif

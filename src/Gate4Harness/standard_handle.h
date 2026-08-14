#ifndef GXOS_STANDARD_HANDLE_H
#define GXOS_STANDARD_HANDLE_H

#include "scheduler_foundation.h"

#define GXOS_STANDARD_HANDLE_INPUT ((uint32_t)0xFFFFFFF6U)
#define GXOS_STANDARD_HANDLE_OUTPUT ((uint32_t)0xFFFFFFF5U)
#define GXOS_STANDARD_HANDLE_ERROR ((uint32_t)0xFFFFFFF4U)
#define GXOS_STANDARD_HANDLE_INVALID_VALUE ((GXOS_SCHEDULER_HANDLE)UINT64_MAX)

#define GXOS_STANDARD_HANDLE_ERROR_INVALID_HANDLE 6U
#define GXOS_STANDARD_HANDLE_ERROR_NOT_ENOUGH_MEMORY 8U

typedef struct {
    GXOS_SCHEDULER *scheduler;
    uint32_t *last_error;
    uint8_t input_available;
    uint8_t output_available;
    uint8_t error_available;
    uint8_t output_backend;
    uint8_t output_capabilities;
} GXOS_STANDARD_HANDLE_CONTEXT;

GXOS_SCHEDULER_HANDLE gxos_get_std_handle_contract(
    const GXOS_STANDARD_HANDLE_CONTEXT *context,
    uint32_t selector);

#endif

#ifndef GXOS_NATIVEAOT_CALLBACK_BRIDGE_H
#define GXOS_NATIVEAOT_CALLBACK_BRIDGE_H

#include <stdint.h>

#if defined(__x86_64__)
#define GXOS_NATIVEAOT_MS_ABI __attribute__((ms_abi))
#else
#define GXOS_NATIVEAOT_MS_ABI
#endif

typedef enum {
    GXOS_NATIVEAOT_EXPORT_OK = 0,
    GXOS_NATIVEAOT_EXPORT_NULL_IMAGE = 1,
    GXOS_NATIVEAOT_EXPORT_NULL_NAME = 2,
    GXOS_NATIVEAOT_EXPORT_INVALID_DIRECTORY = 3,
    GXOS_NATIVEAOT_EXPORT_INVALID_TABLE = 4,
    GXOS_NATIVEAOT_EXPORT_NOT_FOUND = 5,
    GXOS_NATIVEAOT_EXPORT_FORWARDER = 6
} GXOS_NATIVEAOT_EXPORT_STATUS;

typedef struct {
    const uint8_t *loaded_image;
    uint64_t loaded_size;
    uint32_t export_rva;
    uint32_t export_size;
} GXOS_NATIVEAOT_EXPORT_IMAGE;

typedef struct {
    uint32_t rva;
    uint32_t ordinal;
    uintptr_t address;
} GXOS_NATIVEAOT_EXPORT_RESOLUTION;

typedef enum {
    GXOS_NATIVEAOT_CALLBACK_OK = 0,
    GXOS_NATIVEAOT_CALLBACK_NULL_BRIDGE = 1,
    GXOS_NATIVEAOT_CALLBACK_NULL_RESULT = 2,
    GXOS_NATIVEAOT_CALLBACK_NOT_REGISTERED = 3,
    GXOS_NATIVEAOT_CALLBACK_NOT_READY = 4
} GXOS_NATIVEAOT_CALLBACK_STATUS;

typedef struct {
    uintptr_t callback;
    uint32_t rva;
    uint32_t ready;
    uint32_t invocation_count;
} GXOS_NATIVEAOT_CALLBACK_BRIDGE;

typedef int (GXOS_NATIVEAOT_MS_ABI *GXOS_NATIVEAOT_CALLBACK32)(int32_t value);

GXOS_NATIVEAOT_EXPORT_STATUS gxos_nativeaot_find_export(
    const GXOS_NATIVEAOT_EXPORT_IMAGE *image,
    const char *name,
    GXOS_NATIVEAOT_EXPORT_RESOLUTION *resolution);

int gxos_nativeaot_callback_register(
    GXOS_NATIVEAOT_CALLBACK_BRIDGE *bridge,
    const GXOS_NATIVEAOT_EXPORT_RESOLUTION *resolution);

int gxos_nativeaot_callback_mark_ready(
    GXOS_NATIVEAOT_CALLBACK_BRIDGE *bridge);

GXOS_NATIVEAOT_CALLBACK_STATUS GXOS_NATIVEAOT_MS_ABI
gxos_nativeaot_callback_invoke(
    GXOS_NATIVEAOT_CALLBACK_BRIDGE *bridge,
    int32_t input,
    int32_t *result);

#endif

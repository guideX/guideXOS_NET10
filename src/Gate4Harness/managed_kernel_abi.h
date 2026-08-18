#ifndef GXOS_MANAGED_KERNEL_ABI_H
#define GXOS_MANAGED_KERNEL_ABI_H

#include <stddef.h>
#include <stdint.h>

#if defined(__x86_64__)
#define GX_MANAGED_KERNEL_MS_ABI __attribute__((ms_abi))
#else
#define GX_MANAGED_KERNEL_MS_ABI
#endif

/* The first stable native/managed guideXOS service contract. */
#define GX_MANAGED_KERNEL_ABI_V1 1U
#define GX_MANAGED_KERNEL_ARCH_X64 0x8664U
#define GX_MANAGED_KERNEL_INIT_REQUEST_V1_SIZE 16U
#define GX_MANAGED_KERNEL_SYSTEM_INFO_V1_SIZE 32U
#define GX_MANAGED_KERNEL_SERVICE_VERSION_V1 1U

typedef enum {
    GX_MANAGED_OK = 0U,
    GX_MANAGED_INVALID_ARGUMENT = 1U,
    GX_MANAGED_UNSUPPORTED_ABI = 2U,
    GX_MANAGED_BUFFER_TOO_SMALL = 3U,
    GX_MANAGED_NOT_INITIALIZED = 4U,
    GX_MANAGED_ALREADY_INITIALIZED = 5U
} GX_MANAGED_STATUS;

/* Public capabilities describe useful interfaces, not proof markers. */
enum {
    GX_MANAGED_CAPABILITY_SERVICE_ABI = 1ULL << 0,
    GX_MANAGED_CAPABILITY_SYSTEM_INFORMATION = 1ULL << 1
};

#pragma pack(push, 1)
typedef struct {
    uint32_t Size;
    uint32_t AbiVersion;
    uint32_t Architecture;
    uint32_t Flags;
} GX_MANAGED_KERNEL_INIT_REQUEST_V1;

typedef struct {
    uint32_t Size;
    uint32_t AbiVersion;
    uint32_t ServiceVersion;
    uint32_t Architecture;
    uint64_t Capabilities;
    uint64_t Reserved;
} GX_MANAGED_KERNEL_SYSTEM_INFO_V1;
#pragma pack(pop)

_Static_assert(sizeof(GX_MANAGED_KERNEL_INIT_REQUEST_V1) ==
                   GX_MANAGED_KERNEL_INIT_REQUEST_V1_SIZE,
               "managed kernel init request size");
_Static_assert(offsetof(GX_MANAGED_KERNEL_INIT_REQUEST_V1, Size) == 0,
               "managed kernel init request Size offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_INIT_REQUEST_V1, AbiVersion) == 4,
               "managed kernel init request AbiVersion offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_INIT_REQUEST_V1, Architecture) == 8,
               "managed kernel init request Architecture offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_INIT_REQUEST_V1, Flags) == 12,
               "managed kernel init request Flags offset");
_Static_assert(sizeof(GX_MANAGED_KERNEL_SYSTEM_INFO_V1) ==
                   GX_MANAGED_KERNEL_SYSTEM_INFO_V1_SIZE,
               "managed kernel system info size");
_Static_assert(offsetof(GX_MANAGED_KERNEL_SYSTEM_INFO_V1, Size) == 0,
               "managed kernel system info Size offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_SYSTEM_INFO_V1, AbiVersion) == 4,
               "managed kernel system info AbiVersion offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_SYSTEM_INFO_V1, ServiceVersion) == 8,
               "managed kernel system info ServiceVersion offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_SYSTEM_INFO_V1, Architecture) == 12,
               "managed kernel system info Architecture offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_SYSTEM_INFO_V1, Capabilities) == 16,
               "managed kernel system info Capabilities offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_SYSTEM_INFO_V1, Reserved) == 24,
               "managed kernel system info Reserved offset");

typedef uint32_t (GX_MANAGED_KERNEL_MS_ABI *GX_MANAGED_KERNEL_INITIALIZE_ENTRY)(
    uint32_t requested_abi_version, uintptr_t request_address);
typedef uint32_t (GX_MANAGED_KERNEL_MS_ABI *GX_MANAGED_KERNEL_QUERY_SYSTEM_INFO_ENTRY)(
    uint32_t requested_abi_version, uintptr_t output_address,
    uintptr_t output_capacity);

/* Native callers use this before crossing into managed code. */
static inline GX_MANAGED_STATUS gxos_managed_kernel_validate_output_buffer(
    uintptr_t output_address, uintptr_t output_capacity)
{
    if (output_address == 0) return GX_MANAGED_INVALID_ARGUMENT;
    if (output_capacity < GX_MANAGED_KERNEL_SYSTEM_INFO_V1_SIZE) {
        return GX_MANAGED_BUFFER_TOO_SMALL;
    }
    if (output_capacity > UINTPTR_MAX - output_address) {
        return GX_MANAGED_INVALID_ARGUMENT;
    }
    return GX_MANAGED_OK;
}

#endif

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
#define GX_MANAGED_KERNEL_BOOT_RESOURCES_ABI_V1 1U
#define GX_MANAGED_KERNEL_BOOT_RESOURCES_SERVICE_VERSION_V1 1U
#define GX_MANAGED_KERNEL_BOOT_RESOURCE_SUMMARY_V1_SIZE 56U
#define GX_MANAGED_KERNEL_BOOT_RESOURCE_REGION_V1_SIZE 32U
#define GX_MANAGED_KERNEL_BOOT_RESOURCE_PUBLICATION_V1_SIZE 48U
#define GX_MANAGED_KERNEL_BOOT_RESOURCE_MAX_REGIONS 2048U
#define GX_MANAGED_KERNEL_BOOT_RESOURCE_MAP_ID_UEFI_NORMALIZED_V1 1U

typedef enum {
    GX_MANAGED_OK = 0U,
    GX_MANAGED_INVALID_ARGUMENT = 1U,
    GX_MANAGED_UNSUPPORTED_ABI = 2U,
    GX_MANAGED_BUFFER_TOO_SMALL = 3U,
    GX_MANAGED_NOT_INITIALIZED = 4U,
    GX_MANAGED_ALREADY_INITIALIZED = 5U,
    GX_MANAGED_OUT_OF_RANGE = 6U
} GX_MANAGED_STATUS;

/* Public capabilities describe useful interfaces, not proof markers. */
enum {
    GX_MANAGED_CAPABILITY_SERVICE_ABI = 1ULL << 0,
    GX_MANAGED_CAPABILITY_SYSTEM_INFORMATION = 1ULL << 1
};

enum {
    GX_MANAGED_BOOT_RESOURCE_CAPABILITY_SUMMARY = 1ULL << 0,
    GX_MANAGED_BOOT_RESOURCE_CAPABILITY_REGIONS = 1ULL << 1,
    GX_MANAGED_BOOT_RESOURCE_CAPABILITY_TOTALS = 1ULL << 2
};

/* Stable guideXOS meanings. These are not UEFI EFI_MEMORY_TYPE values. */
typedef enum {
    GX_MANAGED_BOOT_RESOURCE_TYPE_CONVENTIONAL = 1U,
    GX_MANAGED_BOOT_RESOURCE_TYPE_LOADER_CODE = 2U,
    GX_MANAGED_BOOT_RESOURCE_TYPE_LOADER_DATA = 3U,
    GX_MANAGED_BOOT_RESOURCE_TYPE_BOOT_SERVICES_CODE = 4U,
    GX_MANAGED_BOOT_RESOURCE_TYPE_BOOT_SERVICES_DATA = 5U,
    GX_MANAGED_BOOT_RESOURCE_TYPE_RUNTIME_SERVICES_CODE = 6U,
    GX_MANAGED_BOOT_RESOURCE_TYPE_RUNTIME_SERVICES_DATA = 7U,
    GX_MANAGED_BOOT_RESOURCE_TYPE_ACPI_RECLAIM = 8U,
    GX_MANAGED_BOOT_RESOURCE_TYPE_ACPI_NVS = 9U,
    GX_MANAGED_BOOT_RESOURCE_TYPE_RESERVED = 10U,
    GX_MANAGED_BOOT_RESOURCE_TYPE_UNUSABLE = 11U,
    GX_MANAGED_BOOT_RESOURCE_TYPE_MMIO = 12U,
    GX_MANAGED_BOOT_RESOURCE_TYPE_MMIO_PORT_SPACE = 13U,
    GX_MANAGED_BOOT_RESOURCE_TYPE_PERSISTENT = 14U,
    GX_MANAGED_BOOT_RESOURCE_TYPE_PAL_CODE = 15U,
    GX_MANAGED_BOOT_RESOURCE_TYPE_UNKNOWN = 16U
} GX_MANAGED_BOOT_RESOURCE_TYPE;

enum {
    GX_MANAGED_BOOT_RESOURCE_FLAG_USABLE = 1U << 0,
    GX_MANAGED_BOOT_RESOURCE_FLAG_RAM_LIKE = 1U << 1,
    GX_MANAGED_BOOT_RESOURCE_FLAG_RUNTIME = 1U << 2
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

typedef struct {
    uint32_t Size;
    uint32_t AbiVersion;
    uint32_t ServiceVersion;
    uint32_t Architecture;
    uint32_t RegionCount;
    uint32_t ResourceMapIdentity;
    uint64_t TotalPhysicalBytes;
    uint64_t UsablePhysicalBytes;
    uint64_t Capabilities;
    uint64_t Reserved;
} GX_MANAGED_KERNEL_BOOT_RESOURCE_SUMMARY_V1;

typedef struct {
    uint32_t Size;
    uint32_t AbiVersion;
    uint64_t BaseAddress;
    uint64_t Length;
    uint32_t Type;
    uint32_t Flags;
} GX_MANAGED_KERNEL_BOOT_RESOURCE_REGION_V1;

typedef struct {
    uint32_t Size;
    uint32_t AbiVersion;
    uint64_t SummaryAddress;
    uint64_t DescriptorAddress;
    uint32_t DescriptorCount;
    uint32_t DescriptorSize;
    uint64_t DescriptorByteLength;
    uint64_t Reserved;
} GX_MANAGED_KERNEL_BOOT_RESOURCE_PUBLICATION_V1;
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
_Static_assert(sizeof(GX_MANAGED_KERNEL_BOOT_RESOURCE_SUMMARY_V1) ==
                   GX_MANAGED_KERNEL_BOOT_RESOURCE_SUMMARY_V1_SIZE,
               "managed boot resource summary size");
_Static_assert(offsetof(GX_MANAGED_KERNEL_BOOT_RESOURCE_SUMMARY_V1, Size) == 0,
               "managed boot resource summary Size offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_BOOT_RESOURCE_SUMMARY_V1, AbiVersion) == 4,
               "managed boot resource summary AbiVersion offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_BOOT_RESOURCE_SUMMARY_V1, ServiceVersion) == 8,
               "managed boot resource summary ServiceVersion offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_BOOT_RESOURCE_SUMMARY_V1, Architecture) == 12,
               "managed boot resource summary Architecture offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_BOOT_RESOURCE_SUMMARY_V1, RegionCount) == 16,
               "managed boot resource summary RegionCount offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_BOOT_RESOURCE_SUMMARY_V1, ResourceMapIdentity) == 20,
               "managed boot resource summary ResourceMapIdentity offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_BOOT_RESOURCE_SUMMARY_V1, TotalPhysicalBytes) == 24,
               "managed boot resource summary TotalPhysicalBytes offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_BOOT_RESOURCE_SUMMARY_V1, UsablePhysicalBytes) == 32,
               "managed boot resource summary UsablePhysicalBytes offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_BOOT_RESOURCE_SUMMARY_V1, Capabilities) == 40,
               "managed boot resource summary Capabilities offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_BOOT_RESOURCE_SUMMARY_V1, Reserved) == 48,
               "managed boot resource summary Reserved offset");
_Static_assert(sizeof(GX_MANAGED_KERNEL_BOOT_RESOURCE_REGION_V1) ==
                   GX_MANAGED_KERNEL_BOOT_RESOURCE_REGION_V1_SIZE,
               "managed boot resource region size");
_Static_assert(offsetof(GX_MANAGED_KERNEL_BOOT_RESOURCE_REGION_V1, Size) == 0,
               "managed boot resource region Size offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_BOOT_RESOURCE_REGION_V1, AbiVersion) == 4,
               "managed boot resource region AbiVersion offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_BOOT_RESOURCE_REGION_V1, BaseAddress) == 8,
               "managed boot resource region BaseAddress offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_BOOT_RESOURCE_REGION_V1, Length) == 16,
               "managed boot resource region Length offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_BOOT_RESOURCE_REGION_V1, Type) == 24,
               "managed boot resource region Type offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_BOOT_RESOURCE_REGION_V1, Flags) == 28,
               "managed boot resource region Flags offset");
_Static_assert(sizeof(GX_MANAGED_KERNEL_BOOT_RESOURCE_PUBLICATION_V1) ==
                   GX_MANAGED_KERNEL_BOOT_RESOURCE_PUBLICATION_V1_SIZE,
               "managed boot resource publication size");
_Static_assert(offsetof(GX_MANAGED_KERNEL_BOOT_RESOURCE_PUBLICATION_V1, Size) == 0,
               "managed boot resource publication Size offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_BOOT_RESOURCE_PUBLICATION_V1, AbiVersion) == 4,
               "managed boot resource publication AbiVersion offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_BOOT_RESOURCE_PUBLICATION_V1, SummaryAddress) == 8,
               "managed boot resource publication SummaryAddress offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_BOOT_RESOURCE_PUBLICATION_V1, DescriptorAddress) == 16,
               "managed boot resource publication DescriptorAddress offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_BOOT_RESOURCE_PUBLICATION_V1, DescriptorCount) == 24,
               "managed boot resource publication DescriptorCount offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_BOOT_RESOURCE_PUBLICATION_V1, DescriptorSize) == 28,
               "managed boot resource publication DescriptorSize offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_BOOT_RESOURCE_PUBLICATION_V1, DescriptorByteLength) == 32,
               "managed boot resource publication DescriptorByteLength offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_BOOT_RESOURCE_PUBLICATION_V1, Reserved) == 40,
               "managed boot resource publication Reserved offset");

typedef uint32_t (GX_MANAGED_KERNEL_MS_ABI *GX_MANAGED_KERNEL_INITIALIZE_ENTRY)(
    uint32_t requested_abi_version, uintptr_t request_address);
typedef uint32_t (GX_MANAGED_KERNEL_MS_ABI *GX_MANAGED_KERNEL_QUERY_SYSTEM_INFO_ENTRY)(
    uint32_t requested_abi_version, uintptr_t output_address,
    uintptr_t output_capacity);
typedef uint32_t (GX_MANAGED_KERNEL_MS_ABI *GX_MANAGED_KERNEL_INSTALL_BOOT_RESOURCES_ENTRY)(
    uint32_t requested_abi_version, uintptr_t publication_address);
typedef uint32_t (GX_MANAGED_KERNEL_MS_ABI *GX_MANAGED_KERNEL_QUERY_BOOT_RESOURCES_ENTRY)(
    uint32_t requested_abi_version, uintptr_t output_address,
    uintptr_t output_capacity);
typedef uint32_t (GX_MANAGED_KERNEL_MS_ABI *GX_MANAGED_KERNEL_QUERY_MEMORY_REGION_ENTRY)(
    uint32_t requested_abi_version, uint32_t index,
    uintptr_t output_address, uintptr_t output_capacity);

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

static inline GX_MANAGED_STATUS gxos_managed_kernel_validate_boot_resource_output_buffer(
    uintptr_t output_address, uintptr_t output_capacity)
{
    if (output_address == 0) return GX_MANAGED_INVALID_ARGUMENT;
    if (output_capacity < GX_MANAGED_KERNEL_BOOT_RESOURCE_SUMMARY_V1_SIZE) {
        return GX_MANAGED_BUFFER_TOO_SMALL;
    }
    if (output_capacity > UINTPTR_MAX - output_address) {
        return GX_MANAGED_INVALID_ARGUMENT;
    }
    return GX_MANAGED_OK;
}

static inline GX_MANAGED_STATUS gxos_managed_kernel_validate_memory_region_output_buffer(
    uintptr_t output_address, uintptr_t output_capacity)
{
    if (output_address == 0) return GX_MANAGED_INVALID_ARGUMENT;
    if (output_capacity < GX_MANAGED_KERNEL_BOOT_RESOURCE_REGION_V1_SIZE) {
        return GX_MANAGED_BUFFER_TOO_SMALL;
    }
    if (output_capacity > UINTPTR_MAX - output_address) {
        return GX_MANAGED_INVALID_ARGUMENT;
    }
    return GX_MANAGED_OK;
}

#endif

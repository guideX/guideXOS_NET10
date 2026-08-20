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
#define GX_MANAGED_KERNEL_HOST_SERVICES_ABI_V1 1U
#define GX_MANAGED_KERNEL_HOST_SERVICES_VERSION_V1 1U
#define GX_MANAGED_KERNEL_HOST_SERVICES_V1_SIZE 56U
#define GX_MANAGED_KERNEL_MONOTONIC_TIME_V1_SIZE 40U
#define GX_MANAGED_KERNEL_HOST_LOG_MAX_BYTES 1024U
#define GX_MANAGED_KERNEL_MEMORY_SERVICES_ABI_V1 1U
#define GX_MANAGED_KERNEL_MEMORY_SERVICES_VERSION_V1 1U
#define GX_MANAGED_KERNEL_MEMORY_SERVICES_V1_SIZE 72U
#define GX_MANAGED_KERNEL_MEMORY_ALLOCATION_V1_SIZE 56U
#define GX_MANAGED_KERNEL_MEMORY_RELEASE_V1_SIZE 56U
#define GX_MANAGED_KERNEL_MEMORY_MAX_PAGES_PER_ALLOCATION 256U
#define GX_MANAGED_KERNEL_MEMORY_MAX_LIVE_ALLOCATIONS 16U
#define GX_MANAGED_KERNEL_MEMORY_MAX_TOTAL_PAGES 1024U
#define GX_MANAGED_KERNEL_MEMORY_PAGE_SIZE 4096U
#define GX_MANAGED_KERNEL_MEMORY_FLAG_NONE 0U
#define GX_MANAGED_KERNEL_DEVICE_INVENTORY_ABI_V1 1U
#define GX_MANAGED_KERNEL_DEVICE_INVENTORY_SERVICE_VERSION_V1 1U
#define GX_MANAGED_KERNEL_DEVICE_INVENTORY_SUMMARY_V1_SIZE 40U
#define GX_MANAGED_KERNEL_DEVICE_V1_SIZE 48U
#define GX_MANAGED_KERNEL_DEVICE_INVENTORY_PUBLICATION_V1_SIZE 48U
#define GX_MANAGED_KERNEL_DEVICE_INVENTORY_MAX_DEVICES 256U
#define GX_MANAGED_KERNEL_DEVICE_INVENTORY_MAX_RESOURCES 1024U
#define GX_MANAGED_KERNEL_PCI_SERVICES_ABI_V1 1U
#define GX_MANAGED_KERNEL_PCI_SERVICES_VERSION_V1 1U
#define GX_MANAGED_KERNEL_PCI_SERVICES_V1_SIZE 48U
#define GX_MANAGED_KERNEL_PCI_READ_RESULT_V1_SIZE 32U
#define GX_MANAGED_KERNEL_PCI_CONFIG_SPACE_SIZE 256U
#define GX_MANAGED_KERNEL_PCI_READ_WIDTH_8 1U
#define GX_MANAGED_KERNEL_PCI_READ_WIDTH_16 2U
#define GX_MANAGED_KERNEL_PCI_READ_WIDTH_32 4U

typedef enum {
    GX_MANAGED_OK = 0U,
    GX_MANAGED_INVALID_ARGUMENT = 1U,
    GX_MANAGED_UNSUPPORTED_ABI = 2U,
    GX_MANAGED_BUFFER_TOO_SMALL = 3U,
    GX_MANAGED_NOT_INITIALIZED = 4U,
    GX_MANAGED_ALREADY_INITIALIZED = 5U,
    GX_MANAGED_OUT_OF_RANGE = 6U,
    GX_MANAGED_INVALID_STATE = 7U,
    GX_MANAGED_RESOURCE_EXHAUSTED = 8U,
    GX_MANAGED_NOT_FOUND = 9U,
    GX_MANAGED_OWNERSHIP_MISMATCH = 10U
} GX_MANAGED_STATUS;

/* Public capabilities describe useful interfaces, not proof markers. */
enum {
    GX_MANAGED_CAPABILITY_SERVICE_ABI = 1ULL << 0,
    GX_MANAGED_CAPABILITY_SYSTEM_INFORMATION = 1ULL << 1
};

enum {
    GX_MANAGED_HOST_CAPABILITY_ABI = 1ULL << 0,
    GX_MANAGED_HOST_CAPABILITY_LOG_UTF8 = 1ULL << 1,
    GX_MANAGED_HOST_CAPABILITY_MONOTONIC_TIME = 1ULL << 2
};

enum {
    GX_MANAGED_MONOTONIC_TIME_FLAG_NORMALIZED_FROM_START = 1ULL << 0
};

enum {
    GX_MANAGED_MEMORY_CAPABILITY_ABI = 1ULL << 0,
    GX_MANAGED_MEMORY_CAPABILITY_ALLOCATE_PAGES = 1ULL << 1,
    GX_MANAGED_MEMORY_CAPABILITY_RELEASE_PAGES = 1ULL << 2
};

enum {
    GX_MANAGED_BOOT_RESOURCE_CAPABILITY_SUMMARY = 1ULL << 0,
    GX_MANAGED_BOOT_RESOURCE_CAPABILITY_REGIONS = 1ULL << 1,
    GX_MANAGED_BOOT_RESOURCE_CAPABILITY_TOTALS = 1ULL << 2
};

enum {
    GX_MANAGED_DEVICE_INVENTORY_CAPABILITY_SUMMARY = 1ULL << 0,
    GX_MANAGED_DEVICE_INVENTORY_CAPABILITY_DEVICES = 1ULL << 1,
    GX_MANAGED_DEVICE_INVENTORY_CAPABILITY_IMMUTABLE_BOOT_SNAPSHOT = 1ULL << 2
};

typedef enum {
    GX_MANAGED_DEVICE_KIND_UNKNOWN = 0U,
    GX_MANAGED_DEVICE_KIND_PCI = 1U
} GX_MANAGED_DEVICE_KIND;

enum {
    GX_MANAGED_DEVICE_FLAG_PCI_MULTIFUNCTION = 1U << 0
};

enum {
    GX_MANAGED_PCI_CAPABILITY_CONFIG_READ = 1ULL << 0
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

typedef struct {
    uint32_t Size;
    uint32_t AbiVersion;
    uint32_t ServiceVersion;
    uint32_t Architecture;
    uint64_t Capabilities;
    uint64_t LogUtf8Address;
    uint64_t MonotonicTimeAddress;
    uint64_t Reserved0;
    uint64_t Reserved1;
} GX_MANAGED_KERNEL_HOST_SERVICES_V1;

typedef struct {
    uint32_t Size;
    uint32_t AbiVersion;
    uint64_t Ticks;
    uint64_t FrequencyHz;
    uint64_t Flags;
    uint64_t Reserved;
} GX_MANAGED_KERNEL_MONOTONIC_TIME_V1;

typedef struct {
    uint32_t Size;
    uint32_t AbiVersion;
    uint32_t ServiceVersion;
    uint32_t Architecture;
    uint64_t Capabilities;
    uint64_t PageSize;
    uint64_t AllocatePagesAddress;
    uint64_t ReleasePagesAddress;
    uint32_t MaxPagesPerAllocation;
    uint32_t MaxLiveAllocations;
    uint64_t MaxTotalPages;
    uint64_t Reserved;
} GX_MANAGED_KERNEL_MEMORY_SERVICES_V1;

typedef struct {
    uint32_t Size;
    uint32_t AbiVersion;
    uint64_t AllocationId;
    uint64_t VirtualAddress;
    uint64_t ByteLength;
    uint64_t PageCount;
    uint64_t PageSize;
    uint32_t Flags;
    uint32_t Reserved;
} GX_MANAGED_KERNEL_MEMORY_ALLOCATION_V1;

typedef struct {
    uint32_t Size;
    uint32_t AbiVersion;
    uint64_t AllocationId;
    uint64_t VirtualAddress;
    uint64_t ByteLength;
    uint64_t PageCount;
    uint64_t PageSize;
    uint32_t Flags;
    uint32_t Reserved;
} GX_MANAGED_KERNEL_MEMORY_RELEASE_V1;

typedef struct {
    uint32_t Size;
    uint32_t AbiVersion;
    uint32_t ServiceVersion;
    uint32_t Architecture;
    uint32_t DeviceCount;
    uint32_t ResourceCount;
    uint64_t Capabilities;
    uint64_t Reserved;
} GX_MANAGED_KERNEL_DEVICE_INVENTORY_SUMMARY_V1;

typedef struct {
    uint32_t Size;
    uint32_t AbiVersion;
    uint32_t DeviceKind;
    uint32_t Flags;
    uint16_t Segment;
    uint8_t Bus;
    uint8_t Device;
    uint8_t Function;
    uint8_t ReservedLocation;
    uint16_t VendorId;
    uint16_t DeviceId;
    uint8_t RevisionId;
    uint8_t ClassCode;
    uint8_t Subclass;
    uint8_t ProgrammingInterface;
    uint8_t HeaderType;
    uint8_t ReservedClass;
    uint32_t ResourceStartIndex;
    uint32_t ResourceCount;
    uint64_t Reserved;
} GX_MANAGED_KERNEL_DEVICE_V1;

typedef struct {
    uint32_t Size;
    uint32_t AbiVersion;
    uint64_t SummaryAddress;
    uint64_t DescriptorAddress;
    uint32_t DescriptorCount;
    uint32_t DescriptorSize;
    uint64_t DescriptorByteLength;
    uint64_t Reserved;
} GX_MANAGED_KERNEL_DEVICE_INVENTORY_PUBLICATION_V1;

typedef struct {
    uint32_t Size;
    uint32_t AbiVersion;
    uint32_t ServiceVersion;
    uint32_t Architecture;
    uint64_t Capabilities;
    uint64_t ConfigReadAddress;
    uint64_t Reserved0;
    uint64_t Reserved1;
} GX_MANAGED_KERNEL_PCI_SERVICES_V1;

typedef struct {
    uint32_t Size;
    uint32_t AbiVersion;
    uint32_t Width;
    uint32_t Reserved0;
    uint64_t Value;
    uint64_t Reserved1;
} GX_MANAGED_KERNEL_PCI_READ_RESULT_V1;
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
_Static_assert(sizeof(GX_MANAGED_KERNEL_HOST_SERVICES_V1) ==
                   GX_MANAGED_KERNEL_HOST_SERVICES_V1_SIZE,
               "managed host services size");
_Static_assert(offsetof(GX_MANAGED_KERNEL_HOST_SERVICES_V1, Size) == 0,
               "managed host services Size offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_HOST_SERVICES_V1, AbiVersion) == 4,
               "managed host services AbiVersion offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_HOST_SERVICES_V1, ServiceVersion) == 8,
               "managed host services ServiceVersion offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_HOST_SERVICES_V1, Architecture) == 12,
               "managed host services Architecture offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_HOST_SERVICES_V1, Capabilities) == 16,
               "managed host services Capabilities offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_HOST_SERVICES_V1, LogUtf8Address) == 24,
               "managed host services LogUtf8Address offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_HOST_SERVICES_V1, MonotonicTimeAddress) == 32,
               "managed host services MonotonicTimeAddress offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_HOST_SERVICES_V1, Reserved0) == 40,
               "managed host services Reserved0 offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_HOST_SERVICES_V1, Reserved1) == 48,
               "managed host services Reserved1 offset");
_Static_assert(sizeof(GX_MANAGED_KERNEL_MONOTONIC_TIME_V1) ==
                   GX_MANAGED_KERNEL_MONOTONIC_TIME_V1_SIZE,
               "managed monotonic time size");
_Static_assert(offsetof(GX_MANAGED_KERNEL_MONOTONIC_TIME_V1, Size) == 0,
               "managed monotonic time Size offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_MONOTONIC_TIME_V1, AbiVersion) == 4,
               "managed monotonic time AbiVersion offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_MONOTONIC_TIME_V1, Ticks) == 8,
               "managed monotonic time Ticks offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_MONOTONIC_TIME_V1, FrequencyHz) == 16,
               "managed monotonic time FrequencyHz offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_MONOTONIC_TIME_V1, Flags) == 24,
               "managed monotonic time Flags offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_MONOTONIC_TIME_V1, Reserved) == 32,
               "managed monotonic time Reserved offset");
_Static_assert(sizeof(GX_MANAGED_KERNEL_MEMORY_SERVICES_V1) ==
                   GX_MANAGED_KERNEL_MEMORY_SERVICES_V1_SIZE,
               "managed memory services size");
_Static_assert(offsetof(GX_MANAGED_KERNEL_MEMORY_SERVICES_V1, Size) == 0,
               "managed memory services Size offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_MEMORY_SERVICES_V1, AbiVersion) == 4,
               "managed memory services AbiVersion offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_MEMORY_SERVICES_V1, ServiceVersion) == 8,
               "managed memory services ServiceVersion offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_MEMORY_SERVICES_V1, Architecture) == 12,
               "managed memory services Architecture offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_MEMORY_SERVICES_V1, Capabilities) == 16,
               "managed memory services Capabilities offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_MEMORY_SERVICES_V1, PageSize) == 24,
               "managed memory services PageSize offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_MEMORY_SERVICES_V1, AllocatePagesAddress) == 32,
               "managed memory services AllocatePagesAddress offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_MEMORY_SERVICES_V1, ReleasePagesAddress) == 40,
               "managed memory services ReleasePagesAddress offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_MEMORY_SERVICES_V1, MaxPagesPerAllocation) == 48,
               "managed memory services MaxPagesPerAllocation offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_MEMORY_SERVICES_V1, MaxLiveAllocations) == 52,
               "managed memory services MaxLiveAllocations offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_MEMORY_SERVICES_V1, MaxTotalPages) == 56,
               "managed memory services MaxTotalPages offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_MEMORY_SERVICES_V1, Reserved) == 64,
               "managed memory services Reserved offset");
_Static_assert(sizeof(GX_MANAGED_KERNEL_MEMORY_ALLOCATION_V1) ==
                   GX_MANAGED_KERNEL_MEMORY_ALLOCATION_V1_SIZE,
               "managed memory allocation size");
_Static_assert(offsetof(GX_MANAGED_KERNEL_MEMORY_ALLOCATION_V1, Size) == 0,
               "managed memory allocation Size offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_MEMORY_ALLOCATION_V1, AbiVersion) == 4,
               "managed memory allocation AbiVersion offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_MEMORY_ALLOCATION_V1, AllocationId) == 8,
               "managed memory allocation AllocationId offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_MEMORY_ALLOCATION_V1, VirtualAddress) == 16,
               "managed memory allocation VirtualAddress offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_MEMORY_ALLOCATION_V1, ByteLength) == 24,
               "managed memory allocation ByteLength offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_MEMORY_ALLOCATION_V1, PageCount) == 32,
               "managed memory allocation PageCount offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_MEMORY_ALLOCATION_V1, PageSize) == 40,
               "managed memory allocation PageSize offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_MEMORY_ALLOCATION_V1, Flags) == 48,
               "managed memory allocation Flags offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_MEMORY_ALLOCATION_V1, Reserved) == 52,
               "managed memory allocation Reserved offset");
_Static_assert(sizeof(GX_MANAGED_KERNEL_MEMORY_RELEASE_V1) ==
                   GX_MANAGED_KERNEL_MEMORY_RELEASE_V1_SIZE,
               "managed memory release size");
_Static_assert(offsetof(GX_MANAGED_KERNEL_MEMORY_RELEASE_V1, Size) == 0,
               "managed memory release Size offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_MEMORY_RELEASE_V1, AbiVersion) == 4,
               "managed memory release AbiVersion offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_MEMORY_RELEASE_V1, AllocationId) == 8,
               "managed memory release AllocationId offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_MEMORY_RELEASE_V1, VirtualAddress) == 16,
               "managed memory release VirtualAddress offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_MEMORY_RELEASE_V1, ByteLength) == 24,
               "managed memory release ByteLength offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_MEMORY_RELEASE_V1, PageCount) == 32,
               "managed memory release PageCount offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_MEMORY_RELEASE_V1, PageSize) == 40,
               "managed memory release PageSize offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_MEMORY_RELEASE_V1, Flags) == 48,
               "managed memory release Flags offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_MEMORY_RELEASE_V1, Reserved) == 52,
               "managed memory release Reserved offset");
_Static_assert(sizeof(GX_MANAGED_KERNEL_DEVICE_INVENTORY_SUMMARY_V1) ==
                   GX_MANAGED_KERNEL_DEVICE_INVENTORY_SUMMARY_V1_SIZE,
               "managed device inventory summary size");
_Static_assert(offsetof(GX_MANAGED_KERNEL_DEVICE_INVENTORY_SUMMARY_V1, Size) == 0,
               "managed device inventory summary Size offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_DEVICE_INVENTORY_SUMMARY_V1, AbiVersion) == 4,
               "managed device inventory summary AbiVersion offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_DEVICE_INVENTORY_SUMMARY_V1, ServiceVersion) == 8,
               "managed device inventory summary ServiceVersion offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_DEVICE_INVENTORY_SUMMARY_V1, Architecture) == 12,
               "managed device inventory summary Architecture offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_DEVICE_INVENTORY_SUMMARY_V1, DeviceCount) == 16,
               "managed device inventory summary DeviceCount offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_DEVICE_INVENTORY_SUMMARY_V1, ResourceCount) == 20,
               "managed device inventory summary ResourceCount offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_DEVICE_INVENTORY_SUMMARY_V1, Capabilities) == 24,
               "managed device inventory summary Capabilities offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_DEVICE_INVENTORY_SUMMARY_V1, Reserved) == 32,
               "managed device inventory summary Reserved offset");
_Static_assert(sizeof(GX_MANAGED_KERNEL_DEVICE_V1) == GX_MANAGED_KERNEL_DEVICE_V1_SIZE,
               "managed device descriptor size");
_Static_assert(offsetof(GX_MANAGED_KERNEL_DEVICE_V1, Size) == 0,
               "managed device Size offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_DEVICE_V1, AbiVersion) == 4,
               "managed device AbiVersion offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_DEVICE_V1, DeviceKind) == 8,
               "managed device DeviceKind offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_DEVICE_V1, Flags) == 12,
               "managed device Flags offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_DEVICE_V1, Segment) == 16,
               "managed device Segment offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_DEVICE_V1, Bus) == 18,
               "managed device Bus offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_DEVICE_V1, Device) == 19,
               "managed device Device offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_DEVICE_V1, Function) == 20,
               "managed device Function offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_DEVICE_V1, VendorId) == 22,
               "managed device VendorId offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_DEVICE_V1, DeviceId) == 24,
               "managed device DeviceId offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_DEVICE_V1, RevisionId) == 26,
               "managed device RevisionId offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_DEVICE_V1, ClassCode) == 27,
               "managed device ClassCode offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_DEVICE_V1, Subclass) == 28,
               "managed device Subclass offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_DEVICE_V1, ProgrammingInterface) == 29,
               "managed device ProgrammingInterface offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_DEVICE_V1, HeaderType) == 30,
               "managed device HeaderType offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_DEVICE_V1, ResourceStartIndex) == 32,
               "managed device ResourceStartIndex offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_DEVICE_V1, ResourceCount) == 36,
               "managed device ResourceCount offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_DEVICE_V1, Reserved) == 40,
               "managed device Reserved offset");
_Static_assert(sizeof(GX_MANAGED_KERNEL_DEVICE_INVENTORY_PUBLICATION_V1) ==
                   GX_MANAGED_KERNEL_DEVICE_INVENTORY_PUBLICATION_V1_SIZE,
               "managed device inventory publication size");
_Static_assert(offsetof(GX_MANAGED_KERNEL_DEVICE_INVENTORY_PUBLICATION_V1, Size) == 0,
               "managed device inventory publication Size offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_DEVICE_INVENTORY_PUBLICATION_V1, AbiVersion) == 4,
               "managed device inventory publication AbiVersion offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_DEVICE_INVENTORY_PUBLICATION_V1, SummaryAddress) == 8,
               "managed device inventory publication SummaryAddress offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_DEVICE_INVENTORY_PUBLICATION_V1, DescriptorAddress) == 16,
               "managed device inventory publication DescriptorAddress offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_DEVICE_INVENTORY_PUBLICATION_V1, DescriptorCount) == 24,
               "managed device inventory publication DescriptorCount offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_DEVICE_INVENTORY_PUBLICATION_V1, DescriptorSize) == 28,
               "managed device inventory publication DescriptorSize offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_DEVICE_INVENTORY_PUBLICATION_V1, DescriptorByteLength) == 32,
               "managed device inventory publication DescriptorByteLength offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_DEVICE_INVENTORY_PUBLICATION_V1, Reserved) == 40,
               "managed device inventory publication Reserved offset");
_Static_assert(sizeof(GX_MANAGED_KERNEL_PCI_SERVICES_V1) ==
                   GX_MANAGED_KERNEL_PCI_SERVICES_V1_SIZE,
               "managed PCI services size");
_Static_assert(offsetof(GX_MANAGED_KERNEL_PCI_SERVICES_V1, Size) == 0,
               "managed PCI services Size offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_PCI_SERVICES_V1, AbiVersion) == 4,
               "managed PCI services AbiVersion offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_PCI_SERVICES_V1, ServiceVersion) == 8,
               "managed PCI services ServiceVersion offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_PCI_SERVICES_V1, Architecture) == 12,
               "managed PCI services Architecture offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_PCI_SERVICES_V1, Capabilities) == 16,
               "managed PCI services Capabilities offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_PCI_SERVICES_V1, ConfigReadAddress) == 24,
               "managed PCI services ConfigReadAddress offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_PCI_SERVICES_V1, Reserved0) == 32,
               "managed PCI services Reserved0 offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_PCI_SERVICES_V1, Reserved1) == 40,
               "managed PCI services Reserved1 offset");
_Static_assert(sizeof(GX_MANAGED_KERNEL_PCI_READ_RESULT_V1) ==
                   GX_MANAGED_KERNEL_PCI_READ_RESULT_V1_SIZE,
               "managed PCI read result size");
_Static_assert(offsetof(GX_MANAGED_KERNEL_PCI_READ_RESULT_V1, Size) == 0,
               "managed PCI read result Size offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_PCI_READ_RESULT_V1, AbiVersion) == 4,
               "managed PCI read result AbiVersion offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_PCI_READ_RESULT_V1, Width) == 8,
               "managed PCI read result Width offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_PCI_READ_RESULT_V1, Value) == 16,
               "managed PCI read result Value offset");
_Static_assert(offsetof(GX_MANAGED_KERNEL_PCI_READ_RESULT_V1, Reserved1) == 24,
               "managed PCI read result Reserved1 offset");

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
typedef uint32_t (GX_MANAGED_KERNEL_MS_ABI *GX_MANAGED_KERNEL_INSTALL_HOST_SERVICES_ENTRY)(
    uint32_t requested_abi_version, uintptr_t host_services_address);
typedef uint32_t (GX_MANAGED_KERNEL_MS_ABI *GX_MANAGED_KERNEL_START_ENTRY)(void);
typedef uint32_t (GX_MANAGED_KERNEL_MS_ABI *GX_MANAGED_KERNEL_LOG_UTF8_ENTRY)(
    uintptr_t bytes_address, uintptr_t byte_length, uint32_t flags);
typedef uint32_t (GX_MANAGED_KERNEL_MS_ABI *GX_MANAGED_KERNEL_QUERY_MONOTONIC_TIME_ENTRY)(
    uint32_t requested_abi_version, uintptr_t output_address,
    uintptr_t output_capacity);
typedef uint32_t (GX_MANAGED_KERNEL_MS_ABI *GX_MANAGED_KERNEL_INSTALL_MEMORY_SERVICES_ENTRY)(
    uint32_t requested_abi_version, uintptr_t memory_services_address);
typedef uint32_t (GX_MANAGED_KERNEL_MS_ABI *GX_MANAGED_KERNEL_MEMORY_ALLOCATE_PAGES_ENTRY)(
    uint64_t page_count, uint32_t flags, uintptr_t output_address,
    uintptr_t output_capacity);
typedef uint32_t (GX_MANAGED_KERNEL_MS_ABI *GX_MANAGED_KERNEL_MEMORY_RELEASE_PAGES_ENTRY)(
    uintptr_t request_address, uintptr_t request_capacity);
typedef uint32_t (GX_MANAGED_KERNEL_MS_ABI *GX_MANAGED_KERNEL_RUN_PHASE4_ENTRY)(void);
enum {
    GX_MANAGED_KERNEL_PHASE5_STAGE_CREATE = 1,
    GX_MANAGED_KERNEL_PHASE5_STAGE_REUSE = 2,
    GX_MANAGED_KERNEL_PHASE5_STAGE_GROWTH = 3,
    GX_MANAGED_KERNEL_PHASE5_STAGE_NEGATIVE = 4,
    GX_MANAGED_KERNEL_PHASE5_STAGE_DESTROY = 5
};
typedef uint32_t (GX_MANAGED_KERNEL_MS_ABI *GX_MANAGED_KERNEL_RUN_PHASE5_ENTRY)(
    uint32_t stage);
typedef uint32_t (GX_MANAGED_KERNEL_MS_ABI *GX_MANAGED_KERNEL_PCI_CONFIG_READ_ENTRY)(
    uint32_t segment, uint32_t bus, uint32_t device, uint32_t function,
    uint32_t offset, uint32_t width, uintptr_t result_address,
    uintptr_t result_capacity);
typedef uint32_t (GX_MANAGED_KERNEL_MS_ABI *GX_MANAGED_KERNEL_INSTALL_PCI_SERVICES_ENTRY)(
    uint32_t requested_abi_version, uintptr_t services_address);
typedef uint32_t (GX_MANAGED_KERNEL_MS_ABI *GX_MANAGED_KERNEL_RUN_PHASE7_ENTRY)(void);
typedef uint32_t (GX_MANAGED_KERNEL_MS_ABI *GX_MANAGED_KERNEL_RUN_PHASE7_ACCOUNTING_ENTRY)(void);

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

static inline GX_MANAGED_STATUS gxos_managed_kernel_validate_memory_allocation_output_buffer(
    uintptr_t output_address, uintptr_t output_capacity)
{
    if (output_address == 0) return GX_MANAGED_INVALID_ARGUMENT;
    if (output_capacity < GX_MANAGED_KERNEL_MEMORY_ALLOCATION_V1_SIZE) {
        return GX_MANAGED_BUFFER_TOO_SMALL;
    }
    if (output_capacity > UINTPTR_MAX - output_address) {
        return GX_MANAGED_INVALID_ARGUMENT;
    }
    return GX_MANAGED_OK;
}

static inline GX_MANAGED_STATUS gxos_managed_kernel_validate_memory_release_input_buffer(
    uintptr_t request_address, uintptr_t request_capacity)
{
    if (request_address == 0) return GX_MANAGED_INVALID_ARGUMENT;
    if (request_capacity < GX_MANAGED_KERNEL_MEMORY_RELEASE_V1_SIZE) {
        return GX_MANAGED_BUFFER_TOO_SMALL;
    }
    if (request_capacity > UINTPTR_MAX - request_address) {
        return GX_MANAGED_INVALID_ARGUMENT;
    }
    return GX_MANAGED_OK;
}

#endif

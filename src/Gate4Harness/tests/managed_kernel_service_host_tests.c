#include <stdint.h>
#include <stdio.h>
#include <string.h>
#include <windows.h>

#include "../managed_kernel_abi.h"

static uint32_t g_failures;
static uint32_t g_log_calls;
static uint32_t g_time_calls;
static uint8_t g_last_log[GX_MANAGED_KERNEL_HOST_LOG_MAX_BYTES];
static uint32_t g_last_log_length;

static uint32_t GX_MANAGED_KERNEL_MS_ABI test_log_utf8(
    uintptr_t bytes_address, uintptr_t byte_length, uint32_t flags)
{
    if (flags != 0 || byte_length > GX_MANAGED_KERNEL_HOST_LOG_MAX_BYTES ||
        (byte_length != 0 &&
         (bytes_address == 0 || byte_length > UINTPTR_MAX - bytes_address))) {
        return GX_MANAGED_INVALID_ARGUMENT;
    }
    if (byte_length != 0) {
        memcpy(g_last_log, (const void *)(uintptr_t)bytes_address,
               (size_t)byte_length);
    }
    g_last_log_length = (uint32_t)byte_length;
    ++g_log_calls;
    return GX_MANAGED_OK;
}

static uint32_t GX_MANAGED_KERNEL_MS_ABI test_monotonic_time(
    uint32_t requested_abi_version, uintptr_t output_address,
    uintptr_t output_capacity)
{
    GX_MANAGED_KERNEL_MONOTONIC_TIME_V1 result;
    if (requested_abi_version != GX_MANAGED_KERNEL_HOST_SERVICES_ABI_V1) {
        return GX_MANAGED_UNSUPPORTED_ABI;
    }
    if (output_address == 0) return GX_MANAGED_INVALID_ARGUMENT;
    if (output_capacity < GX_MANAGED_KERNEL_MONOTONIC_TIME_V1_SIZE) {
        return GX_MANAGED_BUFFER_TOO_SMALL;
    }
    if (output_capacity > UINTPTR_MAX - output_address) {
        return GX_MANAGED_INVALID_ARGUMENT;
    }
    result.Size = GX_MANAGED_KERNEL_MONOTONIC_TIME_V1_SIZE;
    result.AbiVersion = GX_MANAGED_KERNEL_HOST_SERVICES_ABI_V1;
    result.Ticks = 100U + g_time_calls;
    result.FrequencyHz = 1000U;
    result.Flags = GX_MANAGED_MONOTONIC_TIME_FLAG_NORMALIZED_FROM_START;
    result.Reserved = 0;
    *(GX_MANAGED_KERNEL_MONOTONIC_TIME_V1 *)(uintptr_t)output_address = result;
    ++g_time_calls;
    return GX_MANAGED_OK;
}

static uint32_t GX_MANAGED_KERNEL_MS_ABI test_memory_allocate_pages(
    uint64_t page_count, uint32_t flags, uintptr_t output_address,
    uintptr_t output_capacity)
{
    (void)page_count;
    (void)flags;
    (void)output_address;
    (void)output_capacity;
    return GX_MANAGED_INVALID_STATE;
}

static uint32_t GX_MANAGED_KERNEL_MS_ABI test_memory_release_pages(
    uintptr_t request_address, uintptr_t request_capacity)
{
    (void)request_address;
    (void)request_capacity;
    return GX_MANAGED_INVALID_STATE;
}

static void expect(int condition, const char *message)
{
    if (!condition) {
        ++g_failures;
        printf("FAIL: %s\n", message);
    }
}

static int all_bytes_equal(const void *address, size_t count, uint8_t value)
{
    const uint8_t *bytes = (const uint8_t *)address;
    size_t index;
    for (index = 0; index != count; ++index) {
        if (bytes[index] != value) return 0;
    }
    return 1;
}

static int summary_equal(const GX_MANAGED_KERNEL_BOOT_RESOURCE_SUMMARY_V1 *left,
                         const GX_MANAGED_KERNEL_BOOT_RESOURCE_SUMMARY_V1 *right)
{
    return memcmp(left, right, sizeof(*left)) == 0;
}

static int region_equal(const GX_MANAGED_KERNEL_BOOT_RESOURCE_REGION_V1 *left,
                        const GX_MANAGED_KERNEL_BOOT_RESOURCE_REGION_V1 *right)
{
    return memcmp(left, right, sizeof(*left)) == 0;
}

int main(int argc, char **argv)
{
    HMODULE module;
    FARPROC initialize_proc;
    FARPROC query_proc;
    FARPROC install_proc;
    FARPROC query_boot_resources_proc;
    FARPROC query_region_proc;
    FARPROC install_host_services_proc;
    FARPROC install_memory_services_proc;
    FARPROC start_proc;
    GX_MANAGED_KERNEL_INITIALIZE_ENTRY initialize;
    GX_MANAGED_KERNEL_QUERY_SYSTEM_INFO_ENTRY query;
    GX_MANAGED_KERNEL_INSTALL_BOOT_RESOURCES_ENTRY install;
    GX_MANAGED_KERNEL_QUERY_BOOT_RESOURCES_ENTRY query_boot_resources;
    GX_MANAGED_KERNEL_QUERY_MEMORY_REGION_ENTRY query_region;
    GX_MANAGED_KERNEL_INSTALL_HOST_SERVICES_ENTRY install_host_services;
    GX_MANAGED_KERNEL_INSTALL_MEMORY_SERVICES_ENTRY install_memory_services;
    GX_MANAGED_KERNEL_START_ENTRY start;
    GX_MANAGED_KERNEL_INIT_REQUEST_V1 request = {
        GX_MANAGED_KERNEL_INIT_REQUEST_V1_SIZE,
        GX_MANAGED_KERNEL_ABI_V1,
        GX_MANAGED_KERNEL_ARCH_X64,
        0};
    GX_MANAGED_KERNEL_SYSTEM_INFO_V1 info;
    GX_MANAGED_KERNEL_SYSTEM_INFO_V1 repeat;
    GX_MANAGED_KERNEL_BOOT_RESOURCE_SUMMARY_V1 summary;
    GX_MANAGED_KERNEL_BOOT_RESOURCE_SUMMARY_V1 repeat_summary;
    GX_MANAGED_KERNEL_BOOT_RESOURCE_REGION_V1 regions[16];
    GX_MANAGED_KERNEL_BOOT_RESOURCE_REGION_V1 region;
    GX_MANAGED_KERNEL_BOOT_RESOURCE_REGION_V1 repeat_region;
    GX_MANAGED_KERNEL_BOOT_RESOURCE_PUBLICATION_V1 publication;
    GX_MANAGED_KERNEL_BOOT_RESOURCE_SUMMARY_V1 bad_summary;
    GX_MANAGED_KERNEL_BOOT_RESOURCE_REGION_V1 bad_region;
    GX_MANAGED_KERNEL_HOST_SERVICES_V1 host_services;
    GX_MANAGED_KERNEL_HOST_SERVICES_V1 host_candidate;
    GX_MANAGED_KERNEL_MEMORY_SERVICES_V1 memory_services;
    uint32_t status;
    uint32_t index;
    const char *payload = argc > 1
        ? argv[1] : "artifacts\\managed-kernel\\publish\\gxos-managed-kernel.dll";

    module = LoadLibraryA(payload);
    if (module == NULL) {
        printf("FAIL: LoadLibraryA(%s) error=%lu\n", payload,
               (unsigned long)GetLastError());
        return 1;
    }
    initialize_proc = GetProcAddress(module, "GxManagedKernelInitialize");
    query_proc = GetProcAddress(module, "GxManagedQuerySystemInfo");
    install_proc = GetProcAddress(module, "GxManagedKernelInstallBootResources");
    query_boot_resources_proc = GetProcAddress(module, "GxManagedQueryBootResources");
    query_region_proc = GetProcAddress(module, "GxManagedQueryMemoryRegion");
    install_host_services_proc = GetProcAddress(module, "GxManagedKernelInstallHostServices");
    install_memory_services_proc = GetProcAddress(module, "GxManagedKernelInstallMemoryServices");
    start_proc = GetProcAddress(module, "GxManagedKernelStart");
    initialize = NULL;
    query = NULL;
    if (initialize_proc != NULL) {
        memcpy(&initialize, &initialize_proc, sizeof(initialize));
    }
    if (query_proc != NULL) {
        memcpy(&query, &query_proc, sizeof(query));
    }
    install = NULL;
    query_boot_resources = NULL;
    query_region = NULL;
    install_host_services = NULL;
    install_memory_services = NULL;
    start = NULL;
    if (install_proc != NULL) memcpy(&install, &install_proc, sizeof(install));
    if (query_boot_resources_proc != NULL) {
        memcpy(&query_boot_resources, &query_boot_resources_proc,
               sizeof(query_boot_resources));
    }
    if (query_region_proc != NULL) {
        memcpy(&query_region, &query_region_proc, sizeof(query_region));
    }
    if (install_host_services_proc != NULL) {
        memcpy(&install_host_services, &install_host_services_proc,
               sizeof(install_host_services));
    }
    if (install_memory_services_proc != NULL) {
        memcpy(&install_memory_services, &install_memory_services_proc,
               sizeof(install_memory_services));
    }
    if (start_proc != NULL) memcpy(&start, &start_proc, sizeof(start));
    expect(initialize != NULL, "initialization export discovered");
    expect(query != NULL, "system-info export discovered");
    expect(install != NULL, "boot-resource installation export discovered");
    expect(query_boot_resources != NULL, "boot-resource summary export discovered");
    expect(query_region != NULL, "memory-region export discovered");
    expect(install_host_services != NULL, "host-service installation export discovered");
    expect(install_memory_services != NULL, "memory-service installation export discovered");
    expect(start != NULL, "start export discovered");
    if (initialize == NULL || query == NULL || install == NULL ||
        query_boot_resources == NULL || query_region == NULL ||
        install_host_services == NULL || install_memory_services == NULL ||
        start == NULL) {
        FreeLibrary(module);
        return 1;
    }

    memset(&summary, 0, sizeof(summary));
    summary.Size = GX_MANAGED_KERNEL_BOOT_RESOURCE_SUMMARY_V1_SIZE;
    summary.AbiVersion = GX_MANAGED_KERNEL_BOOT_RESOURCES_ABI_V1;
    summary.ServiceVersion = GX_MANAGED_KERNEL_BOOT_RESOURCES_SERVICE_VERSION_V1;
    summary.Architecture = GX_MANAGED_KERNEL_ARCH_X64;
    summary.RegionCount = 16;
    summary.ResourceMapIdentity = GX_MANAGED_KERNEL_BOOT_RESOURCE_MAP_ID_UEFI_NORMALIZED_V1;
    summary.TotalPhysicalBytes = 0xB000;
    summary.UsablePhysicalBytes = 0x1000;
    summary.Capabilities = GX_MANAGED_BOOT_RESOURCE_CAPABILITY_SUMMARY |
                           GX_MANAGED_BOOT_RESOURCE_CAPABILITY_REGIONS |
                           GX_MANAGED_BOOT_RESOURCE_CAPABILITY_TOTALS;
    for (index = 0; index != 16; ++index) {
        regions[index].Size = GX_MANAGED_KERNEL_BOOT_RESOURCE_REGION_V1_SIZE;
        regions[index].AbiVersion = GX_MANAGED_KERNEL_BOOT_RESOURCES_ABI_V1;
        regions[index].BaseAddress = 0x1000ULL + index * 0x1000ULL;
        regions[index].Length = 0x1000;
        regions[index].Type = index + 1U;
        regions[index].Flags = 0;
        if (index < 9U || index == 13U || index == 14U) {
            regions[index].Flags |= GX_MANAGED_BOOT_RESOURCE_FLAG_RAM_LIKE;
        }
        if (index == 0) {
            regions[index].Flags |= GX_MANAGED_BOOT_RESOURCE_FLAG_USABLE;
        }
        if (index == 5U || index == 6U) {
            regions[index].Flags |= GX_MANAGED_BOOT_RESOURCE_FLAG_RUNTIME;
        }
    }
    memset(&publication, 0, sizeof(publication));
    publication.Size = GX_MANAGED_KERNEL_BOOT_RESOURCE_PUBLICATION_V1_SIZE;
    publication.AbiVersion = GX_MANAGED_KERNEL_BOOT_RESOURCES_ABI_V1;
    publication.SummaryAddress = (uint64_t)(uintptr_t)&summary;
    publication.DescriptorAddress = (uint64_t)(uintptr_t)regions;
    publication.DescriptorCount = 16;
    publication.DescriptorSize = GX_MANAGED_KERNEL_BOOT_RESOURCE_REGION_V1_SIZE;
    publication.DescriptorByteLength =
        16ULL * GX_MANAGED_KERNEL_BOOT_RESOURCE_REGION_V1_SIZE;
    publication.Reserved = 0;
    memset(&host_services, 0, sizeof(host_services));
    host_services.Size = GX_MANAGED_KERNEL_HOST_SERVICES_V1_SIZE;
    host_services.AbiVersion = GX_MANAGED_KERNEL_HOST_SERVICES_ABI_V1;
    host_services.ServiceVersion = GX_MANAGED_KERNEL_HOST_SERVICES_VERSION_V1;
    host_services.Architecture = GX_MANAGED_KERNEL_ARCH_X64;
    host_services.Capabilities = GX_MANAGED_HOST_CAPABILITY_ABI |
                                 GX_MANAGED_HOST_CAPABILITY_LOG_UTF8 |
                                 GX_MANAGED_HOST_CAPABILITY_MONOTONIC_TIME;
    host_services.LogUtf8Address = (uint64_t)(uintptr_t)test_log_utf8;
    host_services.MonotonicTimeAddress = (uint64_t)(uintptr_t)test_monotonic_time;
    memset(&memory_services, 0, sizeof(memory_services));
    memory_services.Size = GX_MANAGED_KERNEL_MEMORY_SERVICES_V1_SIZE;
    memory_services.AbiVersion = GX_MANAGED_KERNEL_MEMORY_SERVICES_ABI_V1;
    memory_services.ServiceVersion = GX_MANAGED_KERNEL_MEMORY_SERVICES_VERSION_V1;
    memory_services.Architecture = GX_MANAGED_KERNEL_ARCH_X64;
    memory_services.Capabilities = GX_MANAGED_MEMORY_CAPABILITY_ABI |
                                   GX_MANAGED_MEMORY_CAPABILITY_ALLOCATE_PAGES |
                                   GX_MANAGED_MEMORY_CAPABILITY_RELEASE_PAGES;
    memory_services.PageSize = GX_MANAGED_KERNEL_MEMORY_PAGE_SIZE;
    memory_services.AllocatePagesAddress =
        (uint64_t)(uintptr_t)test_memory_allocate_pages;
    memory_services.ReleasePagesAddress =
        (uint64_t)(uintptr_t)test_memory_release_pages;
    memory_services.MaxPagesPerAllocation =
        GX_MANAGED_KERNEL_MEMORY_MAX_PAGES_PER_ALLOCATION;
    memory_services.MaxLiveAllocations =
        GX_MANAGED_KERNEL_MEMORY_MAX_LIVE_ALLOCATIONS;
    memory_services.MaxTotalPages = GX_MANAGED_KERNEL_MEMORY_MAX_TOTAL_PAGES;

    memset(&repeat_summary, 0xA5, sizeof(repeat_summary));
    status = query_boot_resources(GX_MANAGED_KERNEL_BOOT_RESOURCES_ABI_V1,
                                  (uintptr_t)&repeat_summary,
                                  sizeof(repeat_summary));
    expect(status == GX_MANAGED_NOT_INITIALIZED &&
               all_bytes_equal(&repeat_summary, sizeof(repeat_summary), 0xA5),
           "boot-resource summary before initialization preserves output");
    memset(&region, 0xA5, sizeof(region));
    status = query_region(GX_MANAGED_KERNEL_BOOT_RESOURCES_ABI_V1, 0,
                          (uintptr_t)&region, sizeof(region));
    expect(status == GX_MANAGED_NOT_INITIALIZED &&
               all_bytes_equal(&region, sizeof(region), 0xA5),
           "memory region before initialization preserves output");
    expect(install(GX_MANAGED_KERNEL_BOOT_RESOURCES_ABI_V1,
                   (uintptr_t)&publication) == GX_MANAGED_NOT_INITIALIZED,
           "publication before initialization rejects");
    expect(start() == GX_MANAGED_NOT_INITIALIZED,
           "start before initialization rejects");
    expect(install_host_services(GX_MANAGED_KERNEL_HOST_SERVICES_ABI_V1, 0) ==
               GX_MANAGED_NOT_INITIALIZED,
           "host services before initialization rejects");
    expect(install_memory_services(GX_MANAGED_KERNEL_MEMORY_SERVICES_ABI_V1, 0) ==
               GX_MANAGED_NOT_INITIALIZED,
           "memory services before initialization rejects");

    memset(&info, 0xA5, sizeof(info));
    status = query(GX_MANAGED_KERNEL_ABI_V1, (uintptr_t)&info, sizeof(info));
    expect(status == GX_MANAGED_NOT_INITIALIZED &&
               all_bytes_equal(&info, sizeof(info), 0xA5),
           "query before initialization rejects without writing");

    status = initialize(GX_MANAGED_KERNEL_ABI_V1 + 1U, (uintptr_t)&request);
    expect(status == GX_MANAGED_UNSUPPORTED_ABI, "unsupported init ABI rejects");
    request.Size = GX_MANAGED_KERNEL_INIT_REQUEST_V1_SIZE - 1U;
    status = initialize(GX_MANAGED_KERNEL_ABI_V1, (uintptr_t)&request);
    expect(status == GX_MANAGED_INVALID_ARGUMENT, "undersized init rejects");
    request.Size = GX_MANAGED_KERNEL_INIT_REQUEST_V1_SIZE;
    status = initialize(GX_MANAGED_KERNEL_ABI_V1, (uintptr_t)&request);
    expect(status == GX_MANAGED_OK, "valid initialization succeeds");
    status = initialize(GX_MANAGED_KERNEL_ABI_V1, (uintptr_t)&request);
    expect(status == GX_MANAGED_ALREADY_INITIALIZED, "double initialization rejects");

    memset(&repeat_summary, 0x5A, sizeof(repeat_summary));
    status = query_boot_resources(GX_MANAGED_KERNEL_BOOT_RESOURCES_ABI_V1,
                                  (uintptr_t)&repeat_summary,
                                  sizeof(repeat_summary));
    expect(status == GX_MANAGED_NOT_INITIALIZED &&
               all_bytes_equal(&repeat_summary, sizeof(repeat_summary), 0x5A),
           "summary before publication rejects without writing");
    expect(start() == GX_MANAGED_INVALID_STATE,
           "start before boot-resource publication rejects");
    expect(install_host_services(GX_MANAGED_KERNEL_HOST_SERVICES_ABI_V1,
                                 (uintptr_t)&host_services) == GX_MANAGED_INVALID_STATE,
           "host services before boot-resource publication rejects");
    expect(install_memory_services(GX_MANAGED_KERNEL_MEMORY_SERVICES_ABI_V1,
                                   (uintptr_t)&memory_services) == GX_MANAGED_INVALID_STATE,
           "memory services before boot-resource publication rejects");
    memset(&region, 0x5A, sizeof(region));
    status = query_region(GX_MANAGED_KERNEL_BOOT_RESOURCES_ABI_V1, 0,
                          (uintptr_t)&region, sizeof(region));
    expect(status == GX_MANAGED_NOT_INITIALIZED &&
               all_bytes_equal(&region, sizeof(region), 0x5A),
           "region before publication rejects without writing");
    publication.Size = GX_MANAGED_KERNEL_BOOT_RESOURCE_PUBLICATION_V1_SIZE - 1U;
    expect(install(GX_MANAGED_KERNEL_BOOT_RESOURCES_ABI_V1,
                   (uintptr_t)&publication) == GX_MANAGED_INVALID_ARGUMENT,
           "malformed publication size rejects");
    publication.Size = GX_MANAGED_KERNEL_BOOT_RESOURCE_PUBLICATION_V1_SIZE;
    expect(install(GX_MANAGED_KERNEL_BOOT_RESOURCES_ABI_V1 + 1U,
                   (uintptr_t)&publication) == GX_MANAGED_UNSUPPORTED_ABI,
           "unsupported publication ABI rejects");
    publication.DescriptorByteLength = UINT64_MAX;
    expect(install(GX_MANAGED_KERNEL_BOOT_RESOURCES_ABI_V1,
                   (uintptr_t)&publication) == GX_MANAGED_INVALID_ARGUMENT,
           "publication byte overflow rejects");
    publication.DescriptorByteLength =
        16ULL * GX_MANAGED_KERNEL_BOOT_RESOURCE_REGION_V1_SIZE;
    publication.DescriptorAddress = UINT64_MAX - 15U;
    expect(install(GX_MANAGED_KERNEL_BOOT_RESOURCES_ABI_V1,
                   (uintptr_t)&publication) == GX_MANAGED_INVALID_ARGUMENT,
           "publication pointer overflow rejects");
    publication.DescriptorAddress = (uint64_t)(uintptr_t)regions;
    publication.DescriptorCount = GX_MANAGED_KERNEL_BOOT_RESOURCE_MAX_REGIONS + 1U;
    expect(install(GX_MANAGED_KERNEL_BOOT_RESOURCES_ABI_V1,
                   (uintptr_t)&publication) == GX_MANAGED_INVALID_ARGUMENT,
           "publication descriptor bound rejects");
    publication.DescriptorCount = 16;
    bad_summary = summary;
    bad_region = regions[0];
    bad_summary.RegionCount = 1;
    bad_region.BaseAddress = UINT64_MAX - 15U;
    bad_region.Length = 0x100;
    publication.SummaryAddress = (uint64_t)(uintptr_t)&bad_summary;
    publication.DescriptorAddress = (uint64_t)(uintptr_t)&bad_region;
    publication.DescriptorCount = 1;
    publication.DescriptorByteLength =
        GX_MANAGED_KERNEL_BOOT_RESOURCE_REGION_V1_SIZE;
    expect(install(GX_MANAGED_KERNEL_BOOT_RESOURCES_ABI_V1,
                   (uintptr_t)&publication) == GX_MANAGED_INVALID_ARGUMENT,
           "overflowing descriptor rejects");
    bad_region.Length = 0;
    expect(install(GX_MANAGED_KERNEL_BOOT_RESOURCES_ABI_V1,
                   (uintptr_t)&publication) == GX_MANAGED_INVALID_ARGUMENT,
           "zero-length descriptor rejects");
    publication.SummaryAddress = (uint64_t)(uintptr_t)&summary;
    publication.DescriptorAddress = (uint64_t)(uintptr_t)regions;
    publication.DescriptorCount = 16;
    publication.DescriptorByteLength =
        16ULL * GX_MANAGED_KERNEL_BOOT_RESOURCE_REGION_V1_SIZE;
    expect(install(GX_MANAGED_KERNEL_BOOT_RESOURCES_ABI_V1,
                   (uintptr_t)&publication) == GX_MANAGED_OK,
           "valid publication succeeds");
    expect(install(GX_MANAGED_KERNEL_BOOT_RESOURCES_ABI_V1,
                   (uintptr_t)&publication) == GX_MANAGED_ALREADY_INITIALIZED,
           "repeated publication rejects");
    expect(start() == GX_MANAGED_INVALID_STATE,
           "start before host-service publication rejects");
    expect(install_memory_services(GX_MANAGED_KERNEL_MEMORY_SERVICES_ABI_V1,
                                   (uintptr_t)&memory_services) == GX_MANAGED_OK,
           "valid memory services install succeeds");
    expect(install_memory_services(GX_MANAGED_KERNEL_MEMORY_SERVICES_ABI_V1,
                                   (uintptr_t)&memory_services) == GX_MANAGED_ALREADY_INITIALIZED,
           "repeated memory services install rejects");
    expect(install_host_services(GX_MANAGED_KERNEL_HOST_SERVICES_ABI_V1, 0) ==
               GX_MANAGED_INVALID_ARGUMENT,
           "null host-service table rejects");
    host_candidate = host_services;
    host_candidate.Size = GX_MANAGED_KERNEL_HOST_SERVICES_V1_SIZE - 1U;
    expect(install_host_services(GX_MANAGED_KERNEL_HOST_SERVICES_ABI_V1,
                                 (uintptr_t)&host_candidate) == GX_MANAGED_INVALID_ARGUMENT,
           "undersized host-service table rejects");
    host_candidate = host_services;
    host_candidate.AbiVersion = GX_MANAGED_KERNEL_HOST_SERVICES_ABI_V1 + 1U;
    expect(install_host_services(GX_MANAGED_KERNEL_HOST_SERVICES_ABI_V1,
                                 (uintptr_t)&host_candidate) == GX_MANAGED_INVALID_ARGUMENT,
           "unsupported host-service table ABI rejects");
    host_candidate = host_services;
    host_candidate.Architecture = 0;
    expect(install_host_services(GX_MANAGED_KERNEL_HOST_SERVICES_ABI_V1,
                                 (uintptr_t)&host_candidate) == GX_MANAGED_INVALID_ARGUMENT,
           "wrong host-service architecture rejects");
    host_candidate = host_services;
    host_candidate.Capabilities |= 1ULL << 63;
    expect(install_host_services(GX_MANAGED_KERNEL_HOST_SERVICES_ABI_V1,
                                 (uintptr_t)&host_candidate) == GX_MANAGED_INVALID_ARGUMENT,
           "unknown host-service capability rejects");
    host_candidate = host_services;
    host_candidate.Reserved0 = 1;
    expect(install_host_services(GX_MANAGED_KERNEL_HOST_SERVICES_ABI_V1,
                                 (uintptr_t)&host_candidate) == GX_MANAGED_INVALID_ARGUMENT,
           "nonzero host-service reserved field rejects");
    host_candidate = host_services;
    host_candidate.LogUtf8Address = 0;
    expect(install_host_services(GX_MANAGED_KERNEL_HOST_SERVICES_ABI_V1,
                                 (uintptr_t)&host_candidate) == GX_MANAGED_INVALID_ARGUMENT,
           "claimed logging capability with null callback rejects");
    host_candidate = host_services;
    host_candidate.MonotonicTimeAddress = 0;
    expect(install_host_services(GX_MANAGED_KERNEL_HOST_SERVICES_ABI_V1,
                                 (uintptr_t)&host_candidate) == GX_MANAGED_INVALID_ARGUMENT,
           "claimed time capability with null callback rejects");
    expect(install_host_services(GX_MANAGED_KERNEL_HOST_SERVICES_ABI_V1,
                                 (uintptr_t)&host_services) == GX_MANAGED_OK,
           "valid host services install succeeds");
    expect(install_host_services(GX_MANAGED_KERNEL_HOST_SERVICES_ABI_V1,
                                 (uintptr_t)&host_services) == GX_MANAGED_ALREADY_INITIALIZED,
           "repeated host services install rejects");
    expect(start() == GX_MANAGED_OK, "valid start succeeds");
    expect(start() == GX_MANAGED_ALREADY_INITIALIZED,
           "repeated start rejects without rerunning");
    expect(g_log_calls == 3 && g_time_calls == 2,
           "managed start invoked host logging and time callbacks");
    expect(g_last_log_length ==
               sizeof("GXOS_NET10:MANAGED_KERNEL_MONOTONIC_TIME_OK\r\n") - 1U &&
               memcmp(g_last_log, "GXOS_NET10:MANAGED_KERNEL_MONOTONIC_TIME_OK\r\n",
                      sizeof("GXOS_NET10:MANAGED_KERNEL_MONOTONIC_TIME_OK\r\n") - 1U) == 0,
           "managed final host log marker is present");

    memset(&repeat_summary, 0xA5, sizeof(repeat_summary));
    status = query_boot_resources(GX_MANAGED_KERNEL_BOOT_RESOURCES_ABI_V1,
                                  (uintptr_t)&repeat_summary,
                                  GX_MANAGED_KERNEL_BOOT_RESOURCE_SUMMARY_V1_SIZE - 1U);
    expect(status == GX_MANAGED_BUFFER_TOO_SMALL &&
               all_bytes_equal(&repeat_summary, sizeof(repeat_summary), 0xA5),
           "undersized summary preserves output");
    status = query_boot_resources(GX_MANAGED_KERNEL_BOOT_RESOURCES_ABI_V1 + 1U,
                                  (uintptr_t)&repeat_summary,
                                  sizeof(repeat_summary));
    expect(status == GX_MANAGED_UNSUPPORTED_ABI &&
               all_bytes_equal(&repeat_summary, sizeof(repeat_summary), 0xA5),
           "unsupported summary ABI preserves output");
    expect(query_boot_resources(GX_MANAGED_KERNEL_BOOT_RESOURCES_ABI_V1, 0,
                                sizeof(repeat_summary)) == GX_MANAGED_INVALID_ARGUMENT,
           "null summary output rejects");
    status = query_boot_resources(GX_MANAGED_KERNEL_BOOT_RESOURCES_ABI_V1,
                                  UINTPTR_MAX - 15U,
                                  GX_MANAGED_KERNEL_BOOT_RESOURCE_SUMMARY_V1_SIZE);
    expect(status == GX_MANAGED_INVALID_ARGUMENT,
           "summary pointer range overflow rejects");
    status = query_boot_resources(GX_MANAGED_KERNEL_BOOT_RESOURCES_ABI_V1,
                                  (uintptr_t)&repeat_summary,
                                  sizeof(repeat_summary));
    expect(status == GX_MANAGED_OK && summary_equal(&repeat_summary, &summary),
           "summary query returns authoritative fixture");
    status = query_boot_resources(GX_MANAGED_KERNEL_BOOT_RESOURCES_ABI_V1,
                                  (uintptr_t)&repeat_summary,
                                  sizeof(repeat_summary));
    expect(status == GX_MANAGED_OK && summary_equal(&repeat_summary, &summary),
           "summary repeat is stable");

    memset(&region, 0xA5, sizeof(region));
    status = query_region(GX_MANAGED_KERNEL_BOOT_RESOURCES_ABI_V1, 16,
                          (uintptr_t)&region, sizeof(region));
    expect(status == GX_MANAGED_OUT_OF_RANGE &&
               all_bytes_equal(&region, sizeof(region), 0xA5),
           "index equal to count preserves output");
    status = query_region(GX_MANAGED_KERNEL_BOOT_RESOURCES_ABI_V1, 17,
                          (uintptr_t)&region, sizeof(region));
    expect(status == GX_MANAGED_OUT_OF_RANGE &&
               all_bytes_equal(&region, sizeof(region), 0xA5),
           "index greater than count preserves output");
    expect(query_region(GX_MANAGED_KERNEL_BOOT_RESOURCES_ABI_V1, 0, 0,
                        sizeof(region)) == GX_MANAGED_INVALID_ARGUMENT,
           "null region output rejects");
    status = query_region(GX_MANAGED_KERNEL_BOOT_RESOURCES_ABI_V1, 0,
                          (uintptr_t)&region,
                          GX_MANAGED_KERNEL_BOOT_RESOURCE_REGION_V1_SIZE - 1U);
    expect(status == GX_MANAGED_BUFFER_TOO_SMALL &&
               all_bytes_equal(&region, sizeof(region), 0xA5),
           "undersized region preserves output");
    status = query_region(GX_MANAGED_KERNEL_BOOT_RESOURCES_ABI_V1 + 1U, 0,
                          (uintptr_t)&region, sizeof(region));
    expect(status == GX_MANAGED_UNSUPPORTED_ABI &&
               all_bytes_equal(&region, sizeof(region), 0xA5),
           "unsupported region ABI preserves output");
    status = query_region(GX_MANAGED_KERNEL_BOOT_RESOURCES_ABI_V1, 0,
                          (uintptr_t)&region, sizeof(region));
    expect(status == GX_MANAGED_OK && region_equal(&region, &regions[0]),
           "first region query is authoritative");
    status = query_region(GX_MANAGED_KERNEL_BOOT_RESOURCES_ABI_V1, 0,
                          (uintptr_t)&repeat_region, sizeof(repeat_region));
    expect(status == GX_MANAGED_OK && region_equal(&repeat_region, &regions[0]),
           "repeated region query is stable");
    status = query(GX_MANAGED_KERNEL_ABI_V1, 0, sizeof(info));
    expect(status == GX_MANAGED_INVALID_ARGUMENT, "null output rejects");
    status = query(GX_MANAGED_KERNEL_ABI_V1, (uintptr_t)&info, 0);
    expect(status == GX_MANAGED_BUFFER_TOO_SMALL, "zero capacity rejects");

    memset(&info, 0x5A, sizeof(info));
    status = query(GX_MANAGED_KERNEL_ABI_V1, (uintptr_t)&info,
                   GX_MANAGED_KERNEL_SYSTEM_INFO_V1_SIZE - 1U);
    expect(status == GX_MANAGED_BUFFER_TOO_SMALL &&
               all_bytes_equal(&info, sizeof(info), 0x5A),
           "small output rejects without writing");
    status = query(GX_MANAGED_KERNEL_ABI_V1, (uintptr_t)&info, sizeof(info));
    expect(status == GX_MANAGED_OK &&
               info.Size == GX_MANAGED_KERNEL_SYSTEM_INFO_V1_SIZE &&
               info.AbiVersion == GX_MANAGED_KERNEL_ABI_V1 &&
               info.ServiceVersion == GX_MANAGED_KERNEL_SERVICE_VERSION_V1 &&
               info.Architecture == GX_MANAGED_KERNEL_ARCH_X64 &&
               info.Capabilities ==
                   (GX_MANAGED_CAPABILITY_SERVICE_ABI |
                    GX_MANAGED_CAPABILITY_SYSTEM_INFORMATION) &&
               info.Reserved == 0,
           "system-info fields are truthful");
    memset(&repeat, 0xC3, sizeof(repeat));
    status = query(GX_MANAGED_KERNEL_ABI_V1 + 1U, (uintptr_t)&repeat,
                   sizeof(repeat));
    expect(status == GX_MANAGED_UNSUPPORTED_ABI &&
               all_bytes_equal(&repeat, sizeof(repeat), 0xC3),
           "unsupported query ABI rejects");
    status = query(GX_MANAGED_KERNEL_ABI_V1, (uintptr_t)&repeat, sizeof(repeat));
    expect(status == GX_MANAGED_OK && memcmp(&info, &repeat, sizeof(info)) == 0,
           "repeat query is stable");

    FreeLibrary(module);
    if (g_failures != 0) {
        printf("MANAGED_KERNEL_SERVICE_HOST_TESTS=FAILED failures=%u\n",
               g_failures);
        return 1;
    }
    printf("MANAGED_KERNEL_SERVICE_HOST_TESTS=PASSED\n");
    return 0;
}

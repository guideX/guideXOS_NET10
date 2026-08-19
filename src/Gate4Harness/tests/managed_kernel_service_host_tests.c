#include <stdint.h>
#include <stdio.h>
#include <string.h>
#include <windows.h>

#include "../managed_kernel_abi.h"

static uint32_t g_failures;

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
    GX_MANAGED_KERNEL_INITIALIZE_ENTRY initialize;
    GX_MANAGED_KERNEL_QUERY_SYSTEM_INFO_ENTRY query;
    GX_MANAGED_KERNEL_INSTALL_BOOT_RESOURCES_ENTRY install;
    GX_MANAGED_KERNEL_QUERY_BOOT_RESOURCES_ENTRY query_boot_resources;
    GX_MANAGED_KERNEL_QUERY_MEMORY_REGION_ENTRY query_region;
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
    if (install_proc != NULL) memcpy(&install, &install_proc, sizeof(install));
    if (query_boot_resources_proc != NULL) {
        memcpy(&query_boot_resources, &query_boot_resources_proc,
               sizeof(query_boot_resources));
    }
    if (query_region_proc != NULL) {
        memcpy(&query_region, &query_region_proc, sizeof(query_region));
    }
    expect(initialize != NULL, "initialization export discovered");
    expect(query != NULL, "system-info export discovered");
    expect(install != NULL, "boot-resource installation export discovered");
    expect(query_boot_resources != NULL, "boot-resource summary export discovered");
    expect(query_region != NULL, "memory-region export discovered");
    if (initialize == NULL || query == NULL || install == NULL ||
        query_boot_resources == NULL || query_region == NULL) {
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

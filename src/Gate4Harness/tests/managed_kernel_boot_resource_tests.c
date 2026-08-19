#include <stdint.h>
#include <stdio.h>
#include <string.h>

#include "../managed_kernel_boot_resources.h"

static uint32_t g_failures;

static void expect(int condition, const char *message)
{
    if (!condition) {
        ++g_failures;
        printf("FAIL: %s\n", message);
    }
}

static void make_map(GXOS_UEFI_MEMORY_MAP *map,
                     EFI_MEMORY_DESCRIPTOR *descriptors,
                     uint32_t count)
{
    memset(map, 0, sizeof(*map));
    map->backing = (uint8_t *)descriptors;
    map->backing_bytes = (uint64_t)count * sizeof(*descriptors);
    map->map_bytes = map->backing_bytes;
    map->descriptor_size = sizeof(*descriptors);
    map->descriptor_version = 1;
    map->descriptor_count = count;
    map->generation = 1;
    map->valid = 1;
}

static void make_valid_fixture(GXOS_UEFI_MEMORY_MAP *map,
                               EFI_MEMORY_DESCRIPTOR *descriptors,
                               GXOS_MEMORY_CLASSIFICATION *classification)
{
    memset(descriptors, 0, sizeof(EFI_MEMORY_DESCRIPTOR) * 6U);
    descriptors[0].Type = GXOS_EFI_CONVENTIONAL_MEMORY_TYPE;
    descriptors[0].PhysicalStart = 0x1000;
    descriptors[0].NumberOfPages = 2;
    descriptors[1].Type = GXOS_EFI_RESERVED_MEMORY_TYPE;
    descriptors[1].PhysicalStart = 0x3000;
    descriptors[1].NumberOfPages = 1;
    descriptors[2].Type = GXOS_EFI_LOADER_DATA_MEMORY_TYPE;
    descriptors[2].PhysicalStart = 0x4000;
    descriptors[2].NumberOfPages = 1;
    descriptors[3].Type = GXOS_EFI_RUNTIME_SERVICES_CODE_MEMORY_TYPE;
    descriptors[3].PhysicalStart = 0x0000001000000000ULL;
    descriptors[3].NumberOfPages = 1;
    descriptors[4].Type = 99;
    descriptors[4].PhysicalStart = 0x0000001000001000ULL;
    descriptors[4].NumberOfPages = 1;
    descriptors[5].Type = GXOS_EFI_ACPI_RECLAIM_MEMORY_TYPE;
    descriptors[5].PhysicalStart = UINT64_MAX - 0x1000ULL;
    descriptors[5].NumberOfPages = 1;
    make_map(map, descriptors, 6);
    expect(gxos_uefi_memory_map_classify(map, classification) ==
               GXOS_MEMORY_CLASSIFICATION_OK,
           "valid fixture classification succeeds");
}

static void test_normalization(void)
{
    EFI_MEMORY_DESCRIPTOR descriptors[6];
    GXOS_UEFI_MEMORY_MAP map;
    GXOS_MEMORY_CLASSIFICATION classification;
    GX_MANAGED_KERNEL_BOOT_RESOURCE_REGION_V1 regions[6];
    GX_MANAGED_KERNEL_BOOT_RESOURCE_SUMMARY_V1 summary;
    GXOS_MANAGED_BOOT_RESOURCE_NORMALIZATION_STATUS status;

    make_valid_fixture(&map, descriptors, &classification);
    status = gxos_managed_kernel_normalize_boot_resources(
        &map, &classification, regions, 6, &summary);
    expect(status == GXOS_MANAGED_BOOT_RESOURCE_NORMALIZATION_OK,
           "normalization succeeds");
    expect(summary.RegionCount == 6 && summary.TotalPhysicalBytes == 0x5000 &&
               summary.UsablePhysicalBytes == 0x2000,
           "authoritative totals are normalized");
    expect(regions[0].Type == GX_MANAGED_BOOT_RESOURCE_TYPE_CONVENTIONAL &&
               regions[0].Flags ==
                   (GX_MANAGED_BOOT_RESOURCE_FLAG_USABLE |
                    GX_MANAGED_BOOT_RESOURCE_FLAG_RAM_LIKE) &&
               regions[0].BaseAddress == 0x1000 && regions[0].Length == 0x2000,
           "conventional descriptor is normalized");
    expect(regions[1].Type == GX_MANAGED_BOOT_RESOURCE_TYPE_RESERVED &&
               regions[1].Flags == 0,
           "reserved descriptor is normalized without usable flags");
    expect(regions[2].Type == GX_MANAGED_BOOT_RESOURCE_TYPE_LOADER_DATA &&
               (regions[2].Flags & GX_MANAGED_BOOT_RESOURCE_FLAG_RAM_LIKE) != 0,
           "loader data is ram-like");
    expect(regions[3].Type == GX_MANAGED_BOOT_RESOURCE_TYPE_RUNTIME_SERVICES_CODE &&
               regions[3].Flags ==
                   (GX_MANAGED_BOOT_RESOURCE_FLAG_RAM_LIKE |
                    GX_MANAGED_BOOT_RESOURCE_FLAG_RUNTIME),
           "runtime code preserves normalized runtime meaning");
    expect(regions[4].Type == GX_MANAGED_BOOT_RESOURCE_TYPE_UNKNOWN &&
               regions[4].Flags == 0,
           "unsupported native type becomes unknown");
    expect(regions[5].BaseAddress == UINT64_MAX - 0x1000ULL &&
               regions[5].Length == 0x1000,
           "high physical address remains bounded");
    expect(gxos_managed_kernel_normalize_boot_resources(
               &map, &classification, regions, 5, &summary) ==
               GXOS_MANAGED_BOOT_RESOURCE_NORMALIZATION_CAPACITY,
           "descriptor capacity is bounded");
}

static void test_edge_cases(void)
{
    EFI_MEMORY_DESCRIPTOR descriptor;
    GXOS_UEFI_MEMORY_MAP map;
    GXOS_MEMORY_CLASSIFICATION classification;
    GX_MANAGED_KERNEL_BOOT_RESOURCE_REGION_V1 region;
    GX_MANAGED_KERNEL_BOOT_RESOURCE_SUMMARY_V1 summary;

    memset(&descriptor, 0, sizeof(descriptor));
    descriptor.Type = GXOS_EFI_CONVENTIONAL_MEMORY_TYPE;
    descriptor.PhysicalStart = UINT64_MAX - 0x1000ULL;
    descriptor.NumberOfPages = 1;
    make_map(&map, &descriptor, 1);
    expect(gxos_uefi_memory_map_classify(&map, &classification) ==
               GXOS_MEMORY_CLASSIFICATION_OK,
           "last non-overflowing physical range is accepted");
    expect(gxos_managed_kernel_normalize_boot_resources(
               &map, &classification, &region, 1, &summary) ==
               GXOS_MANAGED_BOOT_RESOURCE_NORMALIZATION_OK,
           "last non-overflowing range normalizes");

    descriptor.PhysicalStart = UINT64_MAX - 0x0FFFULL;
    expect(gxos_uefi_memory_map_classify(&map, &classification) ==
               GXOS_MEMORY_CLASSIFICATION_OVERFLOW,
           "physical range overflow is rejected");
    expect(gxos_managed_kernel_normalize_boot_resources(
               &map, &classification, &region, 1, &summary) ==
               GXOS_MANAGED_BOOT_RESOURCE_NORMALIZATION_OVERFLOW,
           "normalization reports physical range overflow");

    descriptor.PhysicalStart = 0x1000;
    descriptor.NumberOfPages = 0;
    expect(gxos_uefi_memory_map_classify(&map, &classification) ==
               GXOS_MEMORY_CLASSIFICATION_MALFORMED,
           "zero-length native descriptor is rejected");
}

int main(void)
{
    test_normalization();
    test_edge_cases();
    if (g_failures != 0) {
        printf("MANAGED_KERNEL_BOOT_RESOURCE_HOST_TESTS=FAILED failures=%u\n",
               g_failures);
        return 1;
    }
    printf("MANAGED_KERNEL_BOOT_RESOURCE_HOST_TESTS=PASSED\n");
    return 0;
}

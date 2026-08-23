#include <stdio.h>
#include <string.h>

#include "../managed_kernel_mmio.h"

static unsigned failures;

static void expect(int condition, const char *message)
{
    if (condition) return;
    ++failures;
    printf("FAIL: %s\n", message);
}

GXOS_VM_PAGING_STATUS gxos_vm_paging_map_range_with_flags_in_window(
    GXOS_VM_PAGING *paging, uint64_t virtual_start, uint64_t physical_start,
    uint64_t page_count, uint32_t writable, uint32_t executable,
    uint64_t leaf_flags, uint64_t window_base, uint64_t window_length)
{
    (void)paging; (void)virtual_start; (void)physical_start;
    (void)page_count; (void)writable; (void)executable; (void)leaf_flags;
    (void)window_base; (void)window_length;
    return GXOS_VM_PAGING_STATUS_OK;
}

GXOS_VM_PAGING_STATUS gxos_vm_paging_unmap_page_in_window(
    GXOS_VM_PAGING *paging, uint64_t virtual_page, uint64_t *physical_page_out,
    uint64_t window_base, uint64_t window_length)
{
    (void)paging; (void)virtual_page; (void)window_base;
    (void)window_length;
    if (physical_page_out != 0) *physical_page_out = 0;
    return GXOS_VM_PAGING_STATUS_OK;
}

static GX_MANAGED_KERNEL_DEVICE_RESOURCE_V1 make_mmio_resource(
    uint64_t resource_id, uint64_t base, uint64_t length)
{
    GX_MANAGED_KERNEL_DEVICE_RESOURCE_V1 resource = {0};
    resource.Size = GX_MANAGED_KERNEL_DEVICE_RESOURCE_V1_SIZE;
    resource.AbiVersion = GX_MANAGED_KERNEL_DEVICE_RESOURCES_ABI_V1;
    resource.ResourceId = resource_id;
    resource.OwnerDeviceKind = GX_MANAGED_DEVICE_KIND_PCI;
    resource.OwnerDeviceId = 0x808610D3U;
    resource.ResourceType = GX_MANAGED_DEVICE_RESOURCE_TYPE_MMIO;
    resource.Flags = GX_MANAGED_DEVICE_RESOURCE_FLAG_READABLE |
                     GX_MANAGED_DEVICE_RESOURCE_FLAG_MEMORY |
                     GX_MANAGED_DEVICE_RESOURCE_FLAG_CACHE_UNCACHED |
                     GX_MANAGED_DEVICE_RESOURCE_FLAG_PCI_ASSIGNED;
    resource.OwnerSegment = 0;
    resource.OwnerBus = 0;
    resource.OwnerDevice = 2;
    resource.OwnerFunction = 0;
    resource.ResourceIndex = 0;
    resource.PhysicalBase = base;
    resource.Length = length;
    resource.Alignment = GXOS_VM_PAGE_SIZE;
    return resource;
}

static GX_MANAGED_KERNEL_DEVICE_RESOURCE_V1 make_io_resource(void)
{
    GX_MANAGED_KERNEL_DEVICE_RESOURCE_V1 resource = {0};
    resource.Size = GX_MANAGED_KERNEL_DEVICE_RESOURCE_V1_SIZE;
    resource.AbiVersion = GX_MANAGED_KERNEL_DEVICE_RESOURCES_ABI_V1;
    resource.ResourceId = 2;
    resource.OwnerDeviceKind = GX_MANAGED_DEVICE_KIND_PLATFORM_SERIAL;
    resource.OwnerDeviceId = GX_MANAGED_SERIAL_DEVICE_ID_COM1;
    resource.ResourceType = GX_MANAGED_DEVICE_RESOURCE_TYPE_IO_PORT;
    resource.Flags = GX_MANAGED_DEVICE_RESOURCE_FLAG_READABLE |
                     GX_MANAGED_DEVICE_RESOURCE_FLAG_IO_PORT |
                     GX_MANAGED_DEVICE_RESOURCE_FLAG_PLATFORM;
    resource.PhysicalBase = 0x3F8;
    resource.Length = 8;
    resource.Alignment = 1;
    return resource;
}

static void make_memory_map(GXOS_UEFI_MEMORY_MAP *map,
                            EFI_MEMORY_DESCRIPTOR *descriptor)
{
    memset(map, 0, sizeof(*map));
    memset(descriptor, 0, sizeof(*descriptor));
    descriptor->Type = GXOS_EFI_MEMORY_MAPPED_IO_MEMORY_TYPE;
    descriptor->PhysicalStart = 0x80000000ULL;
    descriptor->NumberOfPages = 0x1000;
    map->backing = (uint8_t *)descriptor;
    map->backing_bytes = sizeof(*descriptor);
    map->map_bytes = sizeof(*descriptor);
    map->descriptor_size = sizeof(*descriptor);
    map->descriptor_count = 1;
    map->generation = 1;
    map->valid = 1;
}

static int make_service(GXOS_MMIO_SERVICE *service,
                        GX_MANAGED_KERNEL_DEVICE_RESOURCE_V1 *resources,
                        GXOS_VM_PAGING *paging, GXOS_VM_ARENA *arena,
                        GXOS_UEFI_MEMORY_MAP *map,
                        EFI_MEMORY_DESCRIPTOR *descriptor)
{
    GXOS_MMIO_CACHE_POLICY policy;
    uint32_t slot;
    memset(service, 0, sizeof(*service));
    resources[0] = make_mmio_resource(1, 0x81060000ULL, 0x1000);
    resources[1] = make_io_resource();
    make_memory_map(map, descriptor);
    memset(paging, 0, sizeof(*paging));
    paging->nx_enabled = 1;
    gxos_vm_arena_init(arena, GXOS_MMIO_WINDOW_BASE,
                       GXOS_MMIO_WINDOW_LENGTH, 1);
    if (gxos_vm_arena_reserve_fixed(
            arena, GXOS_MMIO_WINDOW_BASE, GXOS_MMIO_WINDOW_LENGTH,
            GXOS_VM_RESERVATION_KIND_MMIO, GXOS_MMIO_WINDOW_OWNER, 1,
            &slot) != GXOS_VM_STATUS_OK) return 0;
    if (gxos_mmio_cache_policy_validate(
            1, 0x0007040600070406ULL, 0xC06ULL, &policy) !=
            GXOS_MMIO_CACHE_STATUS_OK) return 0;
    return gxos_mmio_service_init(
               service, paging, arena, resources, 2,
               1, map, &policy) == GXOS_MMIO_SERVICE_OK;
}

static void test_cache_policy(void)
{
    GXOS_MMIO_CACHE_POLICY policy;
    expect(gxos_mmio_cache_policy_validate(
               0, 0, 0, &policy) == GXOS_MMIO_CACHE_STATUS_UNSUPPORTED,
           "PAT absence fails closed");
    expect(gxos_mmio_cache_policy_validate(
               1, 0x0007040601070406ULL, 0xC06ULL, &policy) ==
               GXOS_MMIO_CACHE_STATUS_UNPROVEN,
           "non-UC PAT entry is rejected");
    expect(gxos_mmio_cache_policy_validate(
               1, 0x0007040600070406ULL, 0xC06ULL, &policy) ==
               GXOS_MMIO_CACHE_STATUS_OK && policy.pte_flags == 0x18 &&
               policy.safe_uncacheable != 0,
           "PAT entry three plus PWT/PCD proves UC");
}

static void test_bounds_capacity_and_lifetime(void)
{
    GXOS_MMIO_SERVICE service;
    GX_MANAGED_KERNEL_DEVICE_RESOURCE_V1 resources[2];
    GXOS_VM_PAGING paging;
    GXOS_VM_ARENA arena;
    GXOS_UEFI_MEMORY_MAP map;
    EFI_MEMORY_DESCRIPTOR descriptor;
    uint64_t claim = 0;
    uint64_t duplicate_claim = 0;
    uint64_t mappings[GXOS_MMIO_MAPPING_CAPACITY];
    uint64_t overflow_mapping = 0;
    uint32_t index;
    expect(make_service(&service, resources, &paging, &arena, &map, &descriptor),
           "valid service initializes");
    expect(gxos_mmio_claim(&service, 1, 0xD013, GX_MANAGED_DEVICE_KIND_PCI,
                           0x808610D3U, &claim) == GXOS_MMIO_SERVICE_OK &&
               claim != 0,
           "authorized claim succeeds");
    expect(gxos_mmio_claim(&service, 1, 0xD014, GX_MANAGED_DEVICE_KIND_PCI,
                           0x808610D3U, &duplicate_claim) != GXOS_MMIO_SERVICE_OK &&
               claim != 0,
           "duplicate claim is rejected");
    expect(gxos_mmio_map(&service, claim, 0xD014, 0, 16, 1, &mappings[0]) ==
               GXOS_MMIO_SERVICE_OWNERSHIP_MISMATCH,
           "wrong mapping owner is rejected");
    expect(gxos_mmio_map(&service, claim, 0xD013, 0, 0, 1, &mappings[0]) ==
               GXOS_MMIO_SERVICE_INVALID_ARGUMENT,
           "zero length is rejected");
    expect(gxos_mmio_map(&service, claim, 0xD013, 0x1000, 1, 1,
                         &mappings[0]) == GXOS_MMIO_SERVICE_INVALID_ARGUMENT,
           "offset at resource end is rejected");
    expect(gxos_mmio_map(&service, claim, 0xD013, 0xFFF, 2, 1,
                         &mappings[0]) == GXOS_MMIO_SERVICE_INVALID_ARGUMENT,
           "read crossing resource end is rejected");
    expect(gxos_mmio_map(&service, claim, 0xD013, 0, 16, 2,
                         &mappings[0]) == GXOS_MMIO_SERVICE_INVALID_ARGUMENT,
           "write access is rejected");
    expect(gxos_mmio_map(&service, claim, 0xD013, 0, 16, 1,
                         &mappings[0]) == GXOS_MMIO_SERVICE_OK,
           "first bounded mapping succeeds");
    expect(gxos_mmio_release(&service, claim, 0xD013) ==
               GXOS_MMIO_SERVICE_INVALID_STATE,
           "claim release with live mapping is rejected");
    for (index = 1; index != GXOS_MMIO_MAPPING_CAPACITY; ++index) {
        expect(gxos_mmio_map(&service, claim, 0xD013, index * 16, 16, 1,
                             &mappings[index]) == GXOS_MMIO_SERVICE_OK,
               "mapping capacity slot succeeds");
    }
    expect(gxos_mmio_map(&service, claim, 0xD013, 0xF00, 16, 1,
                         &overflow_mapping) == GXOS_MMIO_SERVICE_RESOURCE_EXHAUSTED,
           "mapping capacity is bounded");
    expect(gxos_mmio_unmap(&service, mappings[0], 0xD013) ==
               GXOS_MMIO_SERVICE_OK,
           "unmap succeeds");
    expect(gxos_mmio_unmap(&service, mappings[0], 0xD013) ==
               GXOS_MMIO_SERVICE_NOT_FOUND,
           "double unmap is rejected");
    expect(gxos_mmio_map(&service, claim, 0xD013, 0xE00, 16, 1,
                         &mappings[0]) == GXOS_MMIO_SERVICE_OK,
           "released slot is reusable");
    expect(mappings[0] != 0x0000000100000001ULL,
           "generation protects stale mapping handle");
    for (index = 0; index != GXOS_MMIO_MAPPING_CAPACITY; ++index)
        (void)gxos_mmio_unmap(&service, mappings[index], 0xD013);
    expect(gxos_mmio_release(&service, claim, 0xD013) ==
               GXOS_MMIO_SERVICE_OK,
           "release after teardown succeeds");
    expect(gxos_mmio_release(&service, claim, 0xD013) ==
               GXOS_MMIO_SERVICE_NOT_FOUND,
           "stale claim handle is rejected");
    expect(gxos_mmio_claim(&service, 2, 0xD013,
                           GX_MANAGED_DEVICE_KIND_PLATFORM_SERIAL,
                           GX_MANAGED_SERIAL_DEVICE_ID_COM1, &claim) ==
               GXOS_MMIO_SERVICE_OK &&
               gxos_mmio_map(&service, claim, 0xD013, 0, 1, 1,
                             &mappings[0]) == GXOS_MMIO_SERVICE_UNSUPPORTED,
           "non-MMIO resource cannot be mapped");
}

static void test_malformed_resource(void)
{
    GXOS_MMIO_SERVICE service;
    GX_MANAGED_KERNEL_DEVICE_RESOURCE_V1 resources[2];
    GXOS_VM_PAGING paging;
    GXOS_VM_ARENA arena;
    GXOS_UEFI_MEMORY_MAP map;
    EFI_MEMORY_DESCRIPTOR descriptor;
    expect(make_service(&service, resources, &paging, &arena, &map, &descriptor),
           "malformed fixture starts valid");
    resources[0].Length = 0;
    expect(gxos_mmio_service_init(
               &service, &paging, &arena, resources, 2,
               1, &map, &service.cache_policy) ==
               GXOS_MMIO_SERVICE_INVALID_ARGUMENT,
           "malformed descriptor is rejected");
    resources[0] = make_mmio_resource(1, UINT64_MAX - 0xFFFULL, 0x1000);
    expect(gxos_mmio_service_init(
               &service, &paging, &arena, resources, 2, 1, &map,
               &service.cache_policy) == GXOS_MMIO_SERVICE_INVALID_ARGUMENT,
           "physical range overflow is rejected");
    resources[0] = make_mmio_resource(1, 0x81060000ULL, 0x1000);
    paging.nx_enabled = 0;
    expect(gxos_mmio_service_init(
               &service, &paging, &arena, resources, 2, 1, &map,
               &service.cache_policy) == GXOS_MMIO_SERVICE_INVALID_ARGUMENT,
           "missing NX support is rejected");
}

int main(void)
{
    test_cache_policy();
    test_bounds_capacity_and_lifetime();
    test_malformed_resource();
    if (failures != 0) {
        printf("MANAGED_KERNEL_MMIO_HOST_TESTS=FAILED failures=%u\n", failures);
        return 1;
    }
    printf("MANAGED_KERNEL_MMIO_HOST_TESTS=PASSED\n");
    return 0;
}

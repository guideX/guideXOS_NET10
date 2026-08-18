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

int main(int argc, char **argv)
{
    HMODULE module;
    FARPROC initialize_proc;
    FARPROC query_proc;
    GX_MANAGED_KERNEL_INITIALIZE_ENTRY initialize;
    GX_MANAGED_KERNEL_QUERY_SYSTEM_INFO_ENTRY query;
    GX_MANAGED_KERNEL_INIT_REQUEST_V1 request = {
        GX_MANAGED_KERNEL_INIT_REQUEST_V1_SIZE,
        GX_MANAGED_KERNEL_ABI_V1,
        GX_MANAGED_KERNEL_ARCH_X64,
        0};
    GX_MANAGED_KERNEL_SYSTEM_INFO_V1 info;
    GX_MANAGED_KERNEL_SYSTEM_INFO_V1 repeat;
    uint32_t status;
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
    initialize = NULL;
    query = NULL;
    if (initialize_proc != NULL) {
        memcpy(&initialize, &initialize_proc, sizeof(initialize));
    }
    if (query_proc != NULL) {
        memcpy(&query, &query_proc, sizeof(query));
    }
    expect(initialize != NULL, "initialization export discovered");
    expect(query != NULL, "system-info export discovered");
    if (initialize == NULL || query == NULL) {
        FreeLibrary(module);
        return 1;
    }

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

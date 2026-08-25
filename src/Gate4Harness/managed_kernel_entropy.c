#include "managed_kernel_entropy.h"

#define GXOS_ENTROPY_CPUID_RDRAND (1U << 30)
#define GXOS_ENTROPY_CPUID_RDSEED (1U << 18)

static uint32_t g_entropy_max_basic_leaf;
static uint32_t g_entropy_leaf1_ecx;
static uint32_t g_entropy_leaf7_ebx;
static uint32_t g_entropy_feature_flags;

static void entropy_cpuid(uint32_t leaf, uint32_t subleaf,
                          uint32_t registers[4])
{
    uint32_t eax = leaf;
    uint32_t ebx;
    uint32_t ecx = subleaf;
    uint32_t edx;
    __asm__ volatile ("cpuid"
                      : "+a"(eax), "=b"(ebx), "+c"(ecx), "=d"(edx)
                      :
                      : "memory");
    registers[0] = eax;
    registers[1] = ebx;
    registers[2] = ecx;
    registers[3] = edx;
}

static int entropy_rdrand64(uint64_t *value)
{
    uint64_t output;
    unsigned char success;
    __asm__ volatile (".byte 0x48, 0x0f, 0xc7, 0xf0"
                      : "=a"(output), "=@ccc"(success)
                      :
                      : "cc", "memory");
    *value = output;
    return success != 0;
}

static int entropy_rdseed64(uint64_t *value)
{
    uint64_t output;
    unsigned char success;
    __asm__ volatile (".byte 0x48, 0x0f, 0xc7, 0xf8"
                      : "=a"(output), "=@ccc"(success)
                      :
                      : "cc", "memory");
    *value = output;
    return success != 0;
}

static void entropy_clear(uint8_t *buffer, uint32_t length)
{
    uint32_t index;
    if (buffer == 0) return;
    for (index = 0; index != length; ++index) buffer[index] = 0;
}

static int entropy_try_word(uint64_t *value)
{
    uint32_t attempt;
    if ((g_entropy_feature_flags & GX_MANAGED_ENTROPY_CAPABILITY_RDSEED) != 0) {
        for (attempt = 0; attempt != GX_MANAGED_KERNEL_ENTROPY_MAX_RETRIES;
             ++attempt) {
            if (entropy_rdseed64(value)) return 1;
        }
    }
    if ((g_entropy_feature_flags & GX_MANAGED_ENTROPY_CAPABILITY_RDRAND) != 0) {
        for (attempt = 0; attempt != GX_MANAGED_KERNEL_ENTROPY_MAX_RETRIES;
             ++attempt) {
            if (entropy_rdrand64(value)) return 1;
        }
    }
    return 0;
}

void gxos_managed_kernel_entropy_prepare(
    GX_MANAGED_KERNEL_ENTROPY_SERVICES_V1 *services)
{
    uint32_t registers[4] = {0, 0, 0, 0};
    uint32_t leaf7[4] = {0, 0, 0, 0};

    g_entropy_max_basic_leaf = 0;
    g_entropy_leaf1_ecx = 0;
    g_entropy_leaf7_ebx = 0;
    g_entropy_feature_flags = 0;
    if (services == 0) return;

    entropy_cpuid(0, 0, registers);
    g_entropy_max_basic_leaf = registers[0];
    if (g_entropy_max_basic_leaf >= 1U) {
        entropy_cpuid(1, 0, registers);
        g_entropy_leaf1_ecx = registers[2];
        if ((g_entropy_leaf1_ecx & GXOS_ENTROPY_CPUID_RDRAND) != 0) {
            g_entropy_feature_flags |= GX_MANAGED_ENTROPY_CAPABILITY_RDRAND;
        }
    }
    if (g_entropy_max_basic_leaf >= 7U) {
        entropy_cpuid(7, 0, leaf7);
        g_entropy_leaf7_ebx = leaf7[1];
        if ((g_entropy_leaf7_ebx & GXOS_ENTROPY_CPUID_RDSEED) != 0) {
            g_entropy_feature_flags |= GX_MANAGED_ENTROPY_CAPABILITY_RDSEED;
        }
    }
    if ((g_entropy_feature_flags &
            (GX_MANAGED_ENTROPY_CAPABILITY_RDRAND |
             GX_MANAGED_ENTROPY_CAPABILITY_RDSEED)) != 0) {
        g_entropy_feature_flags |= GX_MANAGED_ENTROPY_CAPABILITY_HARDWARE;
    }

    services->Size = GX_MANAGED_KERNEL_ENTROPY_SERVICES_V1_SIZE;
    services->AbiVersion = GX_MANAGED_KERNEL_ENTROPY_SERVICES_ABI_V1;
    services->ServiceVersion = GX_MANAGED_KERNEL_ENTROPY_SERVICES_VERSION_V1;
    services->Architecture = GX_MANAGED_KERNEL_ARCH_X64;
    services->Capabilities = g_entropy_feature_flags;
    services->FillAddress = (uint64_t)(uintptr_t)&gxos_managed_kernel_entropy_fill;
    services->MaxBytesPerFill = GX_MANAGED_KERNEL_ENTROPY_MAX_BYTES_PER_FILL;
    services->RetryCount = GX_MANAGED_KERNEL_ENTROPY_MAX_RETRIES;
    services->Reserved = 0;
}

GX_MANAGED_STATUS GX_MANAGED_KERNEL_MS_ABI
gxos_managed_kernel_entropy_fill(uintptr_t buffer_address,
                                  uint32_t byte_length)
{
    uint8_t *buffer = (uint8_t *)(uintptr_t)buffer_address;
    uint32_t offset = 0;
    if ((g_entropy_feature_flags & GX_MANAGED_ENTROPY_CAPABILITY_HARDWARE) == 0) {
        return GX_MANAGED_ENTROPY_UNAVAILABLE;
    }
    if (byte_length > GX_MANAGED_KERNEL_ENTROPY_MAX_BYTES_PER_FILL ||
        (byte_length != 0 && buffer == 0)) {
        return GX_MANAGED_INVALID_ARGUMENT;
    }
    while (offset < byte_length) {
        uint64_t word = 0;
        uint32_t remaining;
        uint32_t copy_length;
        uint32_t index;
        if (!entropy_try_word(&word)) {
            entropy_clear(buffer, byte_length);
            return GX_MANAGED_ENTROPY_RETRY_EXHAUSTED;
        }
        remaining = byte_length - offset;
        copy_length = remaining < sizeof(word) ? remaining : sizeof(word);
        for (index = 0; index != copy_length; ++index) {
            buffer[offset + index] = (uint8_t)(word >> (index * 8));
        }
        offset += copy_length;
    }
    return GX_MANAGED_OK;
}

uint32_t gxos_managed_kernel_entropy_max_basic_leaf(void)
{
    return g_entropy_max_basic_leaf;
}

uint32_t gxos_managed_kernel_entropy_leaf1_ecx(void)
{
    return g_entropy_leaf1_ecx;
}

uint32_t gxos_managed_kernel_entropy_leaf7_ebx(void)
{
    return g_entropy_leaf7_ebx;
}

uint32_t gxos_managed_kernel_entropy_feature_flags(void)
{
    return g_entropy_feature_flags;
}

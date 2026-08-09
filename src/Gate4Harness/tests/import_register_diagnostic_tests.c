#include <stdint.h>
#include <stdio.h>

#if defined(__x86_64__)
#define MS_ABI __attribute__((ms_abi))
#else
#define MS_ABI
#endif

typedef struct {
    uint64_t rcx;
    uint64_t rdx;
    uint64_t r8;
    uint64_t r9;
} REGISTER_CAPTURE;

REGISTER_CAPTURE *gxos_import_register_probe_destination;
extern int MS_ABI gxos_import_register_probe_entry(
    uint64_t rcx, uint64_t rdx, uint64_t r8, uint64_t r9);

int main(void)
{
    REGISTER_CAPTURE capture = {0, 0, 0, 0};
    const uint64_t expected_rcx = 0xFFFFFFFFFFFFFFFFULL;
    const uint64_t expected_rdx = 0x0000000000000000ULL;
    const uint64_t expected_r8 = 0x0000000007E64AC0ULL;
    const uint64_t expected_r9 = 0x0000000000000005ULL;

    gxos_import_register_probe_destination = &capture;
    (void)gxos_import_register_probe_entry(
        expected_rcx, expected_rdx, expected_r8, expected_r9);
    if (capture.rcx != expected_rcx || capture.rdx != expected_rdx ||
        capture.r8 != expected_r8 || capture.r9 != expected_r9) {
        (void)printf("REGISTER_CAPTURE_FAILED rcx=%llx rdx=%llx r8=%llx r9=%llx\n",
                      (unsigned long long)capture.rcx,
                      (unsigned long long)capture.rdx,
                      (unsigned long long)capture.r8,
                      (unsigned long long)capture.r9);
        return 1;
    }
    (void)printf("REGISTER_CAPTURE_RCX=0x%llx\n",
                 (unsigned long long)capture.rcx);
    (void)printf("REGISTER_CAPTURE_RDX=0x%llx\n",
                 (unsigned long long)capture.rdx);
    (void)printf("REGISTER_CAPTURE_R8=0x%llx\n",
                 (unsigned long long)capture.r8);
    (void)printf("REGISTER_CAPTURE_R9=0x%llx\n",
                 (unsigned long long)capture.r9);
    (void)printf("IMPORT_REGISTER_DIAGNOSTIC=PASSED\n");
    return 0;
}

#include <stdint.h>
#include <stdio.h>
#include <string.h>
#include "../platform_slist.h"

static int expect(int condition, const char *name)
{
    if (condition) return 0;
    printf("SLIST_TEST_FAILURE=%s\n", name);
    return 1;
}

static int is_empty(const GXOS_SLIST_HEADER *head)
{
    return head->Original.Alignment == 0 &&
           head->Original.Region == 0 &&
           head->HeaderX64.Depth == 0 &&
           head->HeaderX64.Sequence == 0 &&
           head->HeaderX64.Reserved == 0 &&
           head->HeaderX64.NextEntry == 0;
}

int main(void)
{
    static const uint8_t canary = 0xC7;
    static const uint8_t poison = 0xA5;
    uint8_t expected[sizeof(GXOS_SLIST_HEADER)] = {0};
    uint8_t bytes[sizeof(GXOS_SLIST_HEADER) + 32] __attribute__((aligned(16)));
    GXOS_SLIST_HEADER *head = (GXOS_SLIST_HEADER *)(bytes + 16);
    GXOS_SLIST_HEADER *misaligned = (GXOS_SLIST_HEADER *)(bytes + 1);
    size_t i;
    int failures = 0;

    for (i = 0; i != sizeof(bytes); i++) bytes[i] = canary;
    for (i = 0; i != sizeof(GXOS_SLIST_HEADER); i++) ((uint8_t *)head)[i] = poison;
    failures += expect(gxos_initialize_slist_head(head) == 0, "aligned-return");
    failures += expect(memcmp(head, expected, sizeof(expected)) == 0, "exact-empty-bytes");
    failures += expect(is_empty(head), "empty-fields");
    failures += expect(head->HeaderX64.Depth == 0, "depth-zero");
    failures += expect(head->HeaderX64.Sequence == 0, "sequence-zero");
    failures += expect(((uintptr_t)head & 0x0Fu) == 0, "header-alignment");
    for (i = 0; i != 16; i++) failures += expect(bytes[i] == canary, "leading-guard");
    for (i = 16 + sizeof(GXOS_SLIST_HEADER); i != sizeof(bytes); i++) {
        failures += expect(bytes[i] == canary, "trailing-guard");
    }
    printf("SLIST_TEST_INITIALIZATION=%s\n", failures == 0 ? "PASS" : "FAIL");

    failures += expect(gxos_initialize_slist_head(head) == 0, "repeated-empty-return");
    failures += expect(memcmp(head, expected, sizeof(expected)) == 0, "repeated-empty-state");
    printf("SLIST_TEST_REINITIALIZATION=%s\n", failures == 0 ? "PASS" : "FAIL");

    for (i = 0; i != sizeof(GXOS_SLIST_HEADER); i++) ((uint8_t *)head)[i] = poison;
    failures += expect(gxos_initialize_slist_head(head) == 0, "opaque-state-return");
    failures += expect(memcmp(head, expected, sizeof(expected)) == 0, "opaque-state-reset");
    printf("SLIST_TEST_OPAQUE_STATE=%s\n", failures == 0 ? "PASS" : "FAIL");

    memset(bytes, 0x5B, sizeof(bytes));
    failures += expect(gxos_initialize_slist_head(0) == -1, "null-rejection");
    for (i = 0; i != sizeof(bytes); i++) failures += expect(bytes[i] == 0x5B, "null-no-write");
    printf("SLIST_TEST_NULL=%s\n", failures == 0 ? "PASS" : "FAIL");

    memset(bytes, 0x6D, sizeof(bytes));
    failures += expect(gxos_initialize_slist_head(misaligned) == -1, "misaligned-rejection");
    for (i = 0; i != sizeof(bytes); i++) failures += expect(bytes[i] == 0x6D, "misaligned-no-write");
    printf("SLIST_TEST_MISALIGNMENT=%s\n", failures == 0 ? "PASS" : "FAIL");

    failures += expect(sizeof(GXOS_SLIST_HEADER) == 16, "header-size-assertion");
    failures += expect(_Alignof(GXOS_SLIST_HEADER) == 16, "header-alignment-assertion");
    failures += expect(sizeof(GXOS_SLIST_ENTRY) == 16, "entry-size-assertion");
    failures += expect(_Alignof(GXOS_SLIST_ENTRY) == 16, "entry-alignment-assertion");
    printf("SLIST_TEST_LAYOUT_ASSERTIONS=%s\n", failures == 0 ? "PASS" : "FAIL");

    if (failures != 0) return 1;
    printf("SLIST_TEST_NO_ALLOCATION_OR_PLATFORM_SERVICES=PASS\n");
    printf("SLIST_HOST_TESTS=PASSED\n");
    return 0;
}

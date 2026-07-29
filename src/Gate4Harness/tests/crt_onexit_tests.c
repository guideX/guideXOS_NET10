#include <stdint.h>
#include <stdio.h>
#include "../crt_onexit.h"

static int expect(int condition, const char *name)
{
    if (!condition) {
        printf("CRT_TEST_FAILURE=%s\n", name);
        return 1;
    }
    return 0;
}

int main(void)
{
    const uintptr_t encoded_null = (uintptr_t)0xA5A5A5A5A5A5A5A5ULL;
    GXOS_CRT_ONEXIT_TABLE table;
    GXOS_CRT_ONEXIT_TABLE corrupted;
    int failures = 0;

    gxos_crt_onexit_set_encoded_null(encoded_null);
    failures += expect(gxos_crt_onexit_get_encoded_null() == encoded_null, "configured-marker");

    failures += expect(gxos_crt_initialize_onexit_table(0) == -1, "null-argument");
    printf("CRT_TEST_INVALID_ARGUMENTS=%s\n", failures == 0 ? "PASS" : "FAIL");

    table.first = 0;
    table.last = 0;
    table.end = 0;
    failures += expect(gxos_crt_initialize_onexit_table(&table) == 0, "empty-init-return");
    failures += expect((uintptr_t)table.first == encoded_null &&
                       (uintptr_t)table.last == encoded_null &&
                       (uintptr_t)table.end == encoded_null, "empty-init-state");
    printf("CRT_TEST_INITIALIZATION=%s\n", failures == 0 ? "PASS" : "FAIL");

    corrupted = table;
    corrupted.last = (void *)(encoded_null ^ 1u);
    failures += expect(gxos_crt_initialize_onexit_table(&corrupted) == 0, "marker-repair-return");
    failures += expect((uintptr_t)corrupted.first == encoded_null &&
                       (uintptr_t)corrupted.last == encoded_null &&
                       (uintptr_t)corrupted.end == encoded_null, "marker-repair-state");
    printf("CRT_TEST_MARKER_MUTATION=%s\n", failures == 0 ? "PASS" : "FAIL");

    table.last = (void *)(encoded_null + sizeof(void *));
    table.end = (void *)(encoded_null + 2 * sizeof(void *));
    failures += expect(gxos_crt_initialize_onexit_table(&table) == 0, "repeated-init-return");
    failures += expect((uintptr_t)table.first == encoded_null &&
                       (uintptr_t)table.last == encoded_null + sizeof(void *) &&
                       (uintptr_t)table.end == encoded_null + 2 * sizeof(void *),
                       "repeated-init-preserves-state");
    printf("CRT_TEST_CORRUPTED_STATE=%s\n", failures == 0 ? "PASS" : "FAIL");

    gxos_crt_onexit_set_encoded_null(0);
    table.first = 0;
    table.last = 0;
    table.end = 0;
    failures += expect(gxos_crt_initialize_onexit_table(&table) == -1, "disabled-encoding");
    printf("CRT_TEST_DISABLED_IMPLEMENTATION=%s\n", failures == 0 ? "PASS" : "FAIL");

    if (failures != 0) return 1;
    printf("CRT_ONEXIT_HOST_TESTS=PASSED\n");
    return 0;
}

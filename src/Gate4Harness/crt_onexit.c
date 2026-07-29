#include "crt_onexit.h"

/*
 * The UCRT stores encoded null pointers in the opaque on-exit table.  The
 * caller supplies the profile's fast-encoded null value because this
 * freestanding harness does not contain the UCRT security-cookie global.
 */
static uintptr_t g_encoded_null;
static const uintptr_t *g_encoded_null_address;

void gxos_crt_onexit_set_encoded_null(uintptr_t encoded_null)
{
    g_encoded_null_address = 0;
    g_encoded_null = encoded_null;
}

void gxos_crt_onexit_set_encoded_null_address(const uintptr_t *encoded_null_address)
{
    g_encoded_null_address = encoded_null_address;
}

uintptr_t gxos_crt_onexit_get_encoded_null(void)
{
    return g_encoded_null_address != 0 ? *g_encoded_null_address : g_encoded_null;
}

int gxos_crt_initialize_onexit_table(GXOS_CRT_ONEXIT_TABLE *table)
{
    uintptr_t encoded_null;

    if (table == 0) return -1;

    /* Microsoft CRT semantics: a non-empty state is already initialized. */
    if (table->first != table->end) return 0;

    /* A valid NativeAOT security cookie supplies a non-zero encoded null. */
    encoded_null = gxos_crt_onexit_get_encoded_null();
    if (encoded_null == 0) return -1;

    table->first = (void *)encoded_null;
    table->last = (void *)encoded_null;
    table->end = (void *)encoded_null;
    return 0;
}

#ifndef GXOS_CRT_ONEXIT_H
#define GXOS_CRT_ONEXIT_H

#include <stdint.h>

#if defined(__x86_64__)
#define GXOS_CRT_EFIAPI __attribute__((ms_abi))
#else
#define GXOS_CRT_EFIAPI
#endif

typedef struct {
    void *first;
    void *last;
    void *end;
} GXOS_CRT_ONEXIT_TABLE;

void gxos_crt_onexit_set_encoded_null(uintptr_t encoded_null);
void gxos_crt_onexit_set_encoded_null_address(const uintptr_t *encoded_null_address);
uintptr_t gxos_crt_onexit_get_encoded_null(void);
int gxos_crt_initialize_onexit_table(GXOS_CRT_ONEXIT_TABLE *table);

#endif

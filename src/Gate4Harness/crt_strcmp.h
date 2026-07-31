#ifndef GXOS_CRT_STRCMP_H
#define GXOS_CRT_STRCMP_H

#if defined(__x86_64__)
#define GXOS_CRT_STRCMP_MS_ABI __attribute__((ms_abi))
#else
#define GXOS_CRT_STRCMP_MS_ABI
#endif

/* The Microsoft x64 CRT strcmp entry point. */
int GXOS_CRT_STRCMP_MS_ABI gxos_crt_strcmp(const char *lhs, const char *rhs);

#endif

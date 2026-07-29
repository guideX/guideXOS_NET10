#ifndef GXOS_PLATFORM_SLIST_H
#define GXOS_PLATFORM_SLIST_H

#include <stdint.h>

#if defined(__x86_64__)
#define GXOS_SLIST_EFIAPI __attribute__((ms_abi))
#else
#define GXOS_SLIST_EFIAPI
#endif

/*
 * This is the Windows x64 SLIST_HEADER contract from the Windows SDK
 * winnt.h.  The union is intentionally kept opaque to callers; the named
 * view exists so tests can assert the documented depth/sequence encoding.
 */
typedef struct __attribute__((aligned(16))) GXOS_SLIST_ENTRY {
    struct GXOS_SLIST_ENTRY *Next;
} GXOS_SLIST_ENTRY;

typedef union __attribute__((aligned(16))) GXOS_SLIST_HEADER {
    struct {
        uint64_t Alignment;
        uint64_t Region;
    } Original;
    struct {
        uint64_t Depth:16;
        uint64_t Sequence:48;
        uint64_t Reserved:4;
        uint64_t NextEntry:60;
    } HeaderX64;
} GXOS_SLIST_HEADER;

_Static_assert(sizeof(GXOS_SLIST_ENTRY) == 16, "x64 SLIST_ENTRY size changed");
_Static_assert(_Alignof(GXOS_SLIST_ENTRY) == 16, "x64 SLIST_ENTRY alignment changed");
_Static_assert(sizeof(GXOS_SLIST_HEADER) == 16, "x64 SLIST_HEADER size changed");
_Static_assert(_Alignof(GXOS_SLIST_HEADER) == 16, "x64 SLIST_HEADER alignment changed");

/* Internal status is only for validation; the Windows-facing wrapper is void. */
int GXOS_SLIST_EFIAPI gxos_initialize_slist_head(GXOS_SLIST_HEADER *head);

#endif

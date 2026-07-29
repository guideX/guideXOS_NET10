#include <stdint.h>
#include "platform_slist.h"

int GXOS_SLIST_EFIAPI gxos_initialize_slist_head(GXOS_SLIST_HEADER *head)
{
    if (head == 0 || (((uintptr_t)head) & 0x0Fu) != 0) return -1;

    /* Windows x64's documented empty state is two zero 64-bit words. */
    head->Original.Alignment = 0;
    head->Original.Region = 0;
    return 0;
}

#ifdef GXOS_SLIST_TEST_WRONG_LAYOUT
_Static_assert(sizeof(GXOS_SLIST_HEADER) == 8, "intentional incorrect SLIST layout");
#endif

#include "crt_strcmp.h"

int GXOS_CRT_STRCMP_MS_ABI gxos_crt_strcmp(const char *lhs, const char *rhs)
{
    for (;;) {
        unsigned char left = (unsigned char)*lhs;
        unsigned char right = (unsigned char)*rhs;

        if (left != right) return left < right ? -1 : 1;
        if (left == 0) return 0;
        lhs++;
        rhs++;
    }
}

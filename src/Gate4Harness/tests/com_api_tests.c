#include "com_api.h"

#include <stdio.h>

static unsigned int g_checks;
static unsigned int g_failures;

#define CHECK(condition) do { \
    ++g_checks; \
    if (!(condition)) { \
        ++g_failures; \
        (void)printf("FAIL:%s:%u\n", #condition, (unsigned)__LINE__); \
    } \
} while (0)

int main(void)
{
    CHECK(gxos_com_initialize_ex(0, GXOS_COM_COINIT_MULTITHREADED) ==
          GXOS_COM_E_NOTIMPL);
    CHECK(gxos_com_initialize_ex(0, GXOS_COM_COINIT_DISABLE_OLE1DDE |
                                    GXOS_COM_COINIT_SPEED_OVER_MEMORY) ==
          GXOS_COM_E_NOTIMPL);
    CHECK(gxos_com_initialize_ex(0, GXOS_COM_COINIT_APARTMENTTHREADED) ==
          GXOS_COM_E_NOTIMPL);
    CHECK(gxos_com_initialize_ex((void *)(uintptr_t)1, 0) ==
          GXOS_COM_E_INVALIDARG);
    CHECK(gxos_com_initialize_ex(0, 0x10U) == GXOS_COM_E_INVALIDARG);
    gxos_com_uninitialize();
    gxos_com_uninitialize();
    CHECK(g_failures == 0);
    (void)printf("COM_API_TESTS=PASSED checks=%u\n", g_checks);
    return g_failures == 0 ? 0 : 1;
}

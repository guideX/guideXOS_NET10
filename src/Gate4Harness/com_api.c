#include "com_api.h"

int32_t gxos_com_initialize_ex(void *pv_reserved, uint32_t coinit)
{
    if (pv_reserved != 0 ||
        (coinit & ~GXOS_COM_KNOWN_COINIT_FLAGS) != 0U) {
        return GXOS_COM_E_INVALIDARG;
    }

    /* The exact NativeAOT consumer accepts a negative HRESULT, performs its
       real SetEvent before taking the failure branch, and does not require
       COM state or services on that path.  Keep this boundary explicit. */
    return GXOS_COM_E_NOTIMPL;
}

void gxos_com_uninitialize(void)
{
    /* No initialization state was established by the Outcome B fallback. */
}

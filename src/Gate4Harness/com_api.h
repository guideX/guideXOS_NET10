#ifndef GXOS_COM_API_H
#define GXOS_COM_API_H

#include <stdint.h>

#define GXOS_COM_COINIT_MULTITHREADED 0x00000000U
#define GXOS_COM_COINIT_APARTMENTTHREADED 0x00000002U
#define GXOS_COM_COINIT_DISABLE_OLE1DDE 0x00000004U
#define GXOS_COM_COINIT_SPEED_OVER_MEMORY 0x00000008U
#define GXOS_COM_COINIT_CONCURRENCY_MASK GXOS_COM_COINIT_APARTMENTTHREADED
#define GXOS_COM_COINIT_ANCILLARY_MASK \
    (GXOS_COM_COINIT_DISABLE_OLE1DDE | GXOS_COM_COINIT_SPEED_OVER_MEMORY)
#define GXOS_COM_KNOWN_COINIT_FLAGS \
    (GXOS_COM_COINIT_APARTMENTTHREADED | \
     GXOS_COM_COINIT_DISABLE_OLE1DDE | \
     GXOS_COM_COINIT_SPEED_OVER_MEMORY)

#define GXOS_COM_S_OK ((int32_t)0x00000000U)
#define GXOS_COM_S_FALSE ((int32_t)0x00000001U)
#define GXOS_COM_E_INVALIDARG ((int32_t)0x80070057U)
#define GXOS_COM_E_NOTIMPL ((int32_t)0x80004001U)
#define GXOS_COM_RPC_E_CHANGED_MODE ((int32_t)0x80010106U)

#define GXOS_COM_MODEL_NONE 0U
#define GXOS_COM_MODEL_MTA 1U
#define GXOS_COM_MODEL_STA 2U

struct GXOS_SCHEDULER_TCB;

int32_t gxos_com_initialize_ex(void *pv_reserved, uint32_t coinit);
void gxos_com_uninitialize(void);
uint32_t gxos_com_is_initialized(const struct GXOS_SCHEDULER_TCB *thread);
uint32_t gxos_com_model(const struct GXOS_SCHEDULER_TCB *thread);
uint32_t gxos_com_ancillary_flags(const struct GXOS_SCHEDULER_TCB *thread);
uint32_t gxos_com_nesting_count(const struct GXOS_SCHEDULER_TCB *thread);
uint32_t gxos_com_state_generation(const struct GXOS_SCHEDULER_TCB *thread);

#endif

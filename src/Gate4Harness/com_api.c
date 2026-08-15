#include "com_api.h"
#include "scheduler_foundation.h"

static int com_state_is_valid(const GXOS_SCHEDULER_TCB *thread)
{
    return thread != 0 && thread->live && thread->com_initialized != 0 &&
           thread->com_model != GXOS_COM_MODEL_NONE &&
           thread->com_initialization_count != 0 &&
           thread->com_generation != 0 &&
           thread->com_generation == thread->generation;
}

static void clear_com_state(GXOS_SCHEDULER_TCB *thread)
{
    if (thread == 0) return;
    thread->com_initialized = 0;
    thread->com_model = GXOS_COM_MODEL_NONE;
    thread->com_ancillary_flags = 0;
    thread->com_state_reserved = 0;
    thread->com_initialization_count = 0;
    thread->com_generation = 0;
    thread->com_state_reserved2 = 0;
}

int32_t gxos_com_initialize_ex(void *pv_reserved, uint32_t coinit)
{
    GXOS_SCHEDULER_TCB *thread;
    uint32_t model;

    if (pv_reserved != 0 ||
        (coinit & ~GXOS_COM_KNOWN_COINIT_FLAGS) != 0U) {
        return GXOS_COM_E_INVALIDARG;
    }

    thread = gxos_scheduler_current_thread();
    if (thread == 0 || !thread->live) return GXOS_COM_E_NOTIMPL;

    model = (coinit & GXOS_COM_COINIT_CONCURRENCY_MASK) != 0U
        ? GXOS_COM_MODEL_STA : GXOS_COM_MODEL_MTA;

    if (com_state_is_valid(thread)) {
        if (thread->com_model != model) {
            return GXOS_COM_RPC_E_CHANGED_MODE;
        }
        if (thread->com_initialization_count == UINT32_MAX) {
            return GXOS_COM_E_NOTIMPL;
        }
        ++thread->com_initialization_count;
        return GXOS_COM_S_FALSE;
    }

    /* STA bookkeeping would imply message-pump/OLE semantics that this
       substrate does not provide, so reject it without mutating the TCB. */
    if (model == GXOS_COM_MODEL_STA) return GXOS_COM_E_NOTIMPL;

    clear_com_state(thread);
    thread->com_initialized = 1;
    thread->com_model = GXOS_COM_MODEL_MTA;
    thread->com_ancillary_flags =
        (uint8_t)(coinit & GXOS_COM_COINIT_ANCILLARY_MASK);
    thread->com_initialization_count = 1;
    thread->com_generation = thread->generation;
    return GXOS_COM_S_OK;
}

void gxos_com_uninitialize(void)
{
    GXOS_SCHEDULER_TCB *thread = gxos_scheduler_current_thread();
    if (!com_state_is_valid(thread)) return;
    if (thread->com_initialization_count == 0) return;
    --thread->com_initialization_count;
    if (thread->com_initialization_count == 0) clear_com_state(thread);
}

uint32_t gxos_com_is_initialized(const struct GXOS_SCHEDULER_TCB *thread)
{
    return com_state_is_valid((const GXOS_SCHEDULER_TCB *)thread) ? 1U : 0U;
}

uint32_t gxos_com_model(const struct GXOS_SCHEDULER_TCB *thread)
{
    return com_state_is_valid((const GXOS_SCHEDULER_TCB *)thread)
        ? ((const GXOS_SCHEDULER_TCB *)thread)->com_model
        : GXOS_COM_MODEL_NONE;
}

uint32_t gxos_com_ancillary_flags(const struct GXOS_SCHEDULER_TCB *thread)
{
    return com_state_is_valid((const GXOS_SCHEDULER_TCB *)thread)
        ? ((const GXOS_SCHEDULER_TCB *)thread)->com_ancillary_flags : 0U;
}

uint32_t gxos_com_nesting_count(const struct GXOS_SCHEDULER_TCB *thread)
{
    return com_state_is_valid((const GXOS_SCHEDULER_TCB *)thread)
        ? ((const GXOS_SCHEDULER_TCB *)thread)->com_initialization_count : 0U;
}

uint32_t gxos_com_state_generation(const struct GXOS_SCHEDULER_TCB *thread)
{
    return com_state_is_valid((const GXOS_SCHEDULER_TCB *)thread)
        ? ((const GXOS_SCHEDULER_TCB *)thread)->com_generation : 0U;
}

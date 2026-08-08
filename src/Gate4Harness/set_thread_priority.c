#include "set_thread_priority.h"

int gxos_scheduler_set_thread_priority(GXOS_SCHEDULER_HANDLE handle,
                                        int32_t relative_priority)
{
    GXOS_SCHEDULER_OBJECT *object;
    GXOS_SCHEDULER_TCB *thread;

    if (relative_priority != GXOS_SET_THREAD_PRIORITY_SUPPORTED_VALUE) {
        return 0;
    }

    object = gxos_scheduler_object_from_handle(handle);
    if (object == 0 || object->type != GXOS_SCHEDULER_OBJECT_THREAD ||
        object->public_handle_refs == 0 || object->internal_refs == 0 ||
        object->target == 0) {
        return 0;
    }

    thread = (GXOS_SCHEDULER_TCB *)object->target;
    if (!thread->live || thread->execution_refs == 0 ||
        thread->state == GXOS_SCHEDULER_THREAD_TERMINATED ||
        thread->object_slot != object->slot ||
        thread->generation != object->generation) {
        return 0;
    }

    /* Metadata-only update: no runnable-state or dispatch transition occurs. */
    thread->relative_priority = relative_priority;
    return 1;
}

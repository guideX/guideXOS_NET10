#ifndef GXOS_SET_THREAD_PRIORITY_H
#define GXOS_SET_THREAD_PRIORITY_H

#include "scheduler_foundation.h"

/* This milestone accepts only the observed THREAD_PRIORITY_HIGHEST value. */
#define GXOS_SET_THREAD_PRIORITY_SUPPORTED_VALUE \
    GXOS_SCHEDULER_SUPPORTED_RELATIVE_PRIORITY

int gxos_scheduler_set_thread_priority(GXOS_SCHEDULER_HANDLE handle,
                                        int32_t relative_priority);

#endif

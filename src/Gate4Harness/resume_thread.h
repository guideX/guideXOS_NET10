#ifndef GXOS_RESUME_THREAD_H
#define GXOS_RESUME_THREAD_H

#include "scheduler_foundation.h"

/* DWORD ResumeThread(HANDLE) has one payload argument.  The remaining values
   passed to the implementation are captured only as diagnostics. */
uint32_t GXOS_SCHEDULER_MS_ABI gxos_resume_thread_platform_impl(
    void *thread_handle,
    uintptr_t import_entry_rsp,
    uint64_t original_rdx,
    uint64_t original_r8,
    uint64_t original_r9);

#endif

#include "exception_context.h"

int gxos_exception_is_canonical(uint64_t address)
{
    return address <= 0x00007FFFFFFFFFFFULL ||
           address >= 0xFFFF800000000000ULL;
}

uint32_t gxos_exception_translate_vector_code(uint64_t vector)
{
    return vector == 3 ? 0x80000003U : 0U;
}

int gxos_exception_translate_breakpoint_rip(
    uint64_t captured_rip,
    uintptr_t int3_address,
    uint8_t byte_before_rip,
    uint8_t byte_at_rip,
    uint64_t *exception_address,
    uint32_t *rip_semantics)
{
    if (exception_address == 0 || rip_semantics == 0 || int3_address == 0) return 0;
    if (captured_rip == (uint64_t)(int3_address + 1U) && byte_before_rip == 0xCCU) {
        *exception_address = captured_rip - 1U;
        *rip_semantics = GXOS_EXCEPTION_BP_RIP_AFTER_INT3;
        return 1;
    }
    if (captured_rip == (uint64_t)int3_address && byte_at_rip == 0xCCU) {
        *exception_address = captured_rip;
        *rip_semantics = GXOS_EXCEPTION_BP_RIP_AT_INT3;
        return 1;
    }
    return 0;
}

int gxos_exception_dispatch_entry_allowed(uint32_t active)
{
    return active == 0;
}

int gxos_exception_trap_is_well_formed(const GXOS_X64_TRAP_FRAME *trap)
{
    return trap != 0 && trap->vector <= 31 &&
           gxos_exception_is_canonical(trap->rip) &&
           gxos_exception_is_canonical(trap->rsp);
}

int gxos_exception_validate_context_modifications(
    const GXOS_X64_TRAP_FRAME *trap,
    const GXOS_CONTEXT_COMPAT *context,
    const GXOS_EXCEPTION_VALIDATION_BOUNDS *bounds)
{
    uint64_t changed_flags;

    if (trap == 0) return GXOS_EXCEPTION_VALIDATION_NULL_TRAP;
    if (context == 0) return GXOS_EXCEPTION_VALIDATION_NULL_CONTEXT;
    if (bounds == 0 || bounds->stack_lower >= bounds->stack_upper ||
        bounds->executable_lower >= bounds->executable_upper) {
        return GXOS_EXCEPTION_VALIDATION_BAD_BOUNDS;
    }
    if (!gxos_exception_is_canonical(context->rip)) {
        return GXOS_EXCEPTION_VALIDATION_NONCANONICAL_RIP;
    }
    if (context->rip < bounds->executable_lower ||
        context->rip >= bounds->executable_upper) {
        return GXOS_EXCEPTION_VALIDATION_UNAPPROVED_RIP;
    }
    if (!gxos_exception_is_canonical(context->rsp)) {
        return GXOS_EXCEPTION_VALIDATION_NONCANONICAL_RSP;
    }
    if (context->rsp < bounds->stack_lower || context->rsp >= bounds->stack_upper) {
        return GXOS_EXCEPTION_VALIDATION_UNSAFE_RSP;
    }
    changed_flags = (trap->rflags ^ (uint64_t)context->eflags);
    if ((changed_flags & ~GXOS_EXCEPTION_ALLOWED_RFLAGS_MASK) != 0) {
        return GXOS_EXCEPTION_VALIDATION_FORBIDDEN_RFLAGS;
    }
    return GXOS_EXCEPTION_VALIDATION_OK;
}

void gxos_exception_apply_context_modifications(
    GXOS_X64_TRAP_FRAME *trap,
    const GXOS_CONTEXT_COMPAT *context)
{
    trap->rcx = context->rcx;
    trap->rdx = context->rdx;
    trap->rsp = context->rsp;
    trap->rip = context->rip;
    trap->rflags = (trap->rflags & ~GXOS_EXCEPTION_ALLOWED_RFLAGS_MASK) |
                   ((uint64_t)context->eflags & GXOS_EXCEPTION_ALLOWED_RFLAGS_MASK);
}

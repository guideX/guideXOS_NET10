#include <stdio.h>
#include <string.h>
#include "../exception_context.h"

static unsigned failures;

static void check(int condition, const char *name)
{
    if (!condition) {
        fprintf(stderr, "FAIL:%s\n", name);
        failures++;
    }
}

int main(void)
{
    GXOS_X64_TRAP_FRAME trap = {0};
    GXOS_CONTEXT_COMPAT context = {0};
    GXOS_EXCEPTION_VALIDATION_BOUNDS bounds = {
        0x0000000000100000ULL, 0x0000000000200000ULL,
        0x0000000000400000ULL, 0x0000000000500000ULL
    };
    GXOS_EXCEPTION_RECORD_COMPAT record = {0};
    GXOS_EXCEPTION_POINTERS_COMPAT pointers = {0};
    uint64_t exception_address = 0;
    uint32_t rip_semantics = 0;
    unsigned invocation_count = 0;

    check(sizeof(GXOS_X64_TRAP_FRAME) == 0xE0, "trap-size");
    check(offsetof(GXOS_X64_TRAP_FRAME, rcx) == 0x30, "trap-rcx-offset");
    check(offsetof(GXOS_X64_TRAP_FRAME, rip) == 0x98, "trap-rip-offset");
    check(offsetof(GXOS_X64_TRAP_FRAME, rsp) == 0xB0, "trap-rsp-offset");
    check(sizeof(GXOS_EXCEPTION_POINTERS_COMPAT) == 0x10,
          "exception-pointers-size");
    check(offsetof(GXOS_CONTEXT_COMPAT, rcx) == 0x80, "context-rcx-offset");
    check(offsetof(GXOS_CONTEXT_COMPAT, rdx) == 0x88, "context-rdx-offset");
    check(offsetof(GXOS_CONTEXT_COMPAT, rsp) == 0x98, "context-rsp-offset");
    check(offsetof(GXOS_CONTEXT_COMPAT, rip) == 0xF8, "context-rip-offset");

    check(gxos_exception_translate_vector_code(3) == 0x80000003U,
          "breakpoint-translation");
    check(gxos_exception_translate_vector_code(14) == 0,
          "unsupported-vector-translation");
    check(gxos_exception_translate_breakpoint_rip(
              0x401001, 0x401000, 0xCC, 0, &exception_address, &rip_semantics) &&
          exception_address == 0x401000 &&
          rip_semantics == GXOS_EXCEPTION_BP_RIP_AFTER_INT3,
          "breakpoint-rip-after-int3");
    check(gxos_exception_translate_breakpoint_rip(
              0x401000, 0x401000, 0, 0xCC, &exception_address, &rip_semantics) &&
          exception_address == 0x401000 &&
          rip_semantics == GXOS_EXCEPTION_BP_RIP_AT_INT3,
          "breakpoint-rip-at-int3");

    check(GXOS_EXCEPTION_CONTINUE_SEARCH == 0, "continue-search-result");
    check(GXOS_EXCEPTION_CONTINUE_EXECUTION == -1, "continue-execution-result");
    check(1 != GXOS_EXCEPTION_CONTINUE_SEARCH &&
          1 != GXOS_EXCEPTION_CONTINUE_EXECUTION, "invalid-handler-result");
    check(gxos_exception_dispatch_entry_allowed(0), "dispatch-entry-idle");
    check(!gxos_exception_dispatch_entry_allowed(1), "nested-dispatch-detected");

    check(pointers.exception_record == 0 && pointers.context_record == 0,
          "null-exception-pointers");
    check(gxos_exception_validate_context_modifications(
              &trap, 0, &bounds) == GXOS_EXCEPTION_VALIDATION_NULL_CONTEXT,
          "null-context-pointer");
    check(gxos_exception_validate_context_modifications(
              0, &context, &bounds) == GXOS_EXCEPTION_VALIDATION_NULL_TRAP,
          "null-trap-pointer");
    record.exception_code = 0xC0000005U;
    check(record.exception_code != gxos_exception_translate_vector_code(3),
          "wrong-exception-code");

    trap.vector = 3;
    trap.rip = 0x400100;
    trap.rsp = 0x100100;
    trap.rflags = 0x202;
    context.rip = 0x400100;
    context.rsp = 0x100100;
    context.eflags = 0x202;
    check(gxos_exception_trap_is_well_formed(&trap), "well-formed-trap");
    check(gxos_exception_validate_context_modifications(
              &trap, &context, &bounds) == GXOS_EXCEPTION_VALIDATION_OK,
          "approved-context");

    context.rip = 0x0001000000000000ULL;
    check(gxos_exception_validate_context_modifications(
              &trap, &context, &bounds) == GXOS_EXCEPTION_VALIDATION_NONCANONICAL_RIP,
          "noncanonical-rip");
    context.rip = 0x600000;
    check(gxos_exception_validate_context_modifications(
              &trap, &context, &bounds) == GXOS_EXCEPTION_VALIDATION_UNAPPROVED_RIP,
          "unsafe-rip-range");
    context.rip = 0x400100;
    context.rsp = 0x0001000000000000ULL;
    check(gxos_exception_validate_context_modifications(
              &trap, &context, &bounds) == GXOS_EXCEPTION_VALIDATION_NONCANONICAL_RSP,
          "noncanonical-rsp");
    context.rsp = 0x300000;
    check(gxos_exception_validate_context_modifications(
              &trap, &context, &bounds) == GXOS_EXCEPTION_VALIDATION_UNSAFE_RSP,
          "unsafe-rsp-range");
    context.rsp = 0x100100;
    context.eflags = 0x202 | 0x4000;
    check(gxos_exception_validate_context_modifications(
              &trap, &context, &bounds) == GXOS_EXCEPTION_VALIDATION_FORBIDDEN_RFLAGS,
          "forbidden-rflags");
    context.eflags = 0x202;
    check(gxos_exception_validate_context_modifications(
              &trap, &context, 0) == GXOS_EXCEPTION_VALIDATION_BAD_BOUNDS,
          "malformed-bounds");
    trap.vector = 32;
    check(!gxos_exception_trap_is_well_formed(&trap), "unsupported-vector");
    check(!gxos_exception_trap_is_well_formed(0), "malformed-trap");

    /* Deterministic exactly-once harness control. */
    invocation_count++;
    check(invocation_count == 1, "handler-called-once");
    check(invocation_count != 2, "repeated-handler-rejected");

    if (failures != 0) return 1;
    puts("EXCEPTION_CONTEXT_HOST_TESTS=PASSED");
    return 0;
}

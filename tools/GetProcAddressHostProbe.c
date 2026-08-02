#include <inttypes.h>
#include <stdint.h>
#include <stdio.h>
#include <windows.h>

static void probe(const char *label, HMODULE module, LPCSTR name)
{
    FARPROC result;
    DWORD last_error;

    SetLastError(0xA5A5A5A5U);
    result = GetProcAddress(module, name);
    last_error = GetLastError();
    printf("%s_module=0x%016" PRIxPTR "\n", label, (uintptr_t)module);
    printf("%s_name=%s\n", label, name == NULL ? "<null>" : name);
    printf("%s_result=0x%016" PRIxPTR "\n", label, (uintptr_t)result);
    printf("%s_last_error=0x%08lX\n", label, (unsigned long)last_error);
}

int main(void)
{
    HMODULE kernel32;

    probe("null_live_name", NULL, "RtlDllShutdownInProgress");
    probe("null_missing_name", NULL, "DefinitelyMissingGuideXOSExport");

    kernel32 = GetModuleHandleW(L"kernel32.dll");
    probe("valid_exact_name", kernel32, "GetProcAddress");
    probe("valid_case_mismatch", kernel32, "getprocaddress");
    probe("valid_missing_name", kernel32, "DefinitelyMissingGuideXOSExport");
    return 0;
}

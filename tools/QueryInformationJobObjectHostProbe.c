#include <stdint.h>
#include <stdio.h>
#include <string.h>
#include <windows.h>

int main(void)
{
    JOBOBJECT_CPU_RATE_CONTROL_INFORMATION information;
    JOBOBJECT_CPU_RATE_CONTROL_INFORMATION job_information;
    DWORD return_length = 0xA5A5A5A5U;
    DWORD job_return_length = 0xA5A5A5A5U;
    BOOL result;
    BOOL job_result;
    BOOL in_job = FALSE;
    BOOL in_job_result;
    DWORD last_error;
    const unsigned char *bytes;
    size_t index;

    SetLastError(0xA5A5A5A5U);
    in_job_result = IsProcessInJob(
        GetCurrentProcess(), NULL, &in_job);
    printf("pid=%lu\n", (unsigned long)GetCurrentProcessId());
    printf("is_process_in_job_result=%lu\n", (unsigned long)in_job_result);
    printf("is_process_in_job=%lu\n", (unsigned long)in_job);

    memset(&information, 0xCC, sizeof(information));
    SetLastError(0xA5A5A5A5U);
    result = QueryInformationJobObject(
        NULL, JobObjectCpuRateControlInformation, &information,
        (DWORD)sizeof(information), NULL);
    last_error = GetLastError();
    bytes = (const unsigned char *)&information;

    printf("result=%lu\n", (unsigned long)result);
    printf("last_error=%lu\n", (unsigned long)last_error);
    printf("return_length=0x%08lX\n", (unsigned long)return_length);
    printf("sizeof_info=%zu\n", sizeof(information));
    printf("bytes=");
    for (index = 0; index != sizeof(information); ++index) {
        printf("%02X", (unsigned int)bytes[index]);
    }
    printf("\n");

    {
        HANDLE job = CreateJobObjectW(NULL, NULL);
        DWORD job_error;
        const unsigned char *job_bytes;

        memset(&job_information, 0xCC, sizeof(job_information));
        SetLastError(0xA5A5A5A5U);
        job_result = QueryInformationJobObject(
            job, JobObjectCpuRateControlInformation, &job_information,
            (DWORD)sizeof(job_information), &job_return_length);
        job_error = GetLastError();
        job_bytes = (const unsigned char *)&job_information;
        printf("created_job=%lu\n", (unsigned long)(job != NULL));
        printf("job_query_result=%lu\n", (unsigned long)job_result);
        printf("job_query_last_error=%lu\n", (unsigned long)job_error);
        printf("job_query_return_length=0x%08lX\n",
               (unsigned long)job_return_length);
        printf("job_query_bytes=");
        for (index = 0; index != sizeof(job_information); ++index) {
            printf("%02X", (unsigned int)job_bytes[index]);
        }
        printf("\n");
        if (job != NULL) CloseHandle(job);
    }
    return 0;
}

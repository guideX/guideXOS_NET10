#include "../platform_time.h"

#include <stdio.h>
#include <string.h>

typedef struct {
    const char *name;
    GXOS_CIVIL_TIME input;
    uint64_t expected;
    int should_pass;
} VECTOR;

static int expect_vector(const VECTOR *vector)
{
    uint64_t actual = UINT64_MAX;
    GXOS_TIME_RESULT result = gxos_filetime_from_utc_civil(&vector->input, &actual);
    uint32_t low = (uint32_t)actual;
    uint32_t high = (uint32_t)(actual >> 32);
    int pass = vector->should_pass ? (result == GXOS_TIME_OK && actual == vector->expected)
                                   : (result != GXOS_TIME_OK);
    printf("%-28s input=%04u-%02u-%02uT%02u:%02u:%02u.%09uZ expected=0x%016llX actual=0x%016llX low=0x%08X high=0x%08X result=%s\n",
           vector->name, vector->input.year, vector->input.month, vector->input.day,
           vector->input.hour, vector->input.minute, vector->input.second,
           vector->input.nanosecond, (unsigned long long)vector->expected,
           (unsigned long long)actual, low, high,
           result == GXOS_TIME_OK ? (pass ? "PASS" : "FAIL") : "REJECTED");
    return pass;
}

static int expect_efi(const char *name, GXOS_EFI_TIME input, uint64_t expected, int should_pass)
{
    uint64_t actual = UINT64_MAX;
    GXOS_TIME_RESULT result = gxos_filetime_from_efi_time(&input, &actual);
    int pass = should_pass ? (result == GXOS_TIME_OK && actual == expected) : (result != GXOS_TIME_OK);
    printf("%-28s EFI=%04u-%02u-%02u tz=%d daylight=0x%02X expected=0x%016llX actual=0x%016llX result=%s\n",
           name, input.Year, input.Month, input.Day, input.TimeZone, input.Daylight,
           (unsigned long long)expected, (unsigned long long)actual,
           result == GXOS_TIME_OK ? (pass ? "PASS" : "FAIL") : "REJECTED");
    return pass;
}

int main(void)
{
    VECTOR vectors[] = {
        {"epoch", {1601, 1, 1, 0, 0, 0, 0}, 0x0000000000000000ULL, 1},
        {"100ns", {1601, 1, 1, 0, 0, 0, 100}, 0x0000000000000001ULL, 1},
        {"known modern UTC", {2024, 2, 29, 23, 59, 59, 123456700}, 0x01DA6B6B66CD0007ULL, 1},
        {"1900-02-28", {1900, 2, 28, 0, 0, 0, 0}, 0x014F64CF99D5C000ULL, 1},
        {"1900-03-01", {1900, 3, 1, 0, 0, 0, 0}, 0x014F6598C43F8000ULL, 1},
        {"2000-02-29", {2000, 2, 29, 0, 0, 0, 0}, 0x01BF8247EBCC8000ULL, 1},
        {"end of year", {1999, 12, 31, 23, 59, 59, 999999999}, 0x01BF53EB256D3FFFULL, 1},
        {"next year", {2000, 1, 1, 0, 0, 0, 0}, 0x01BF53EB256D4000ULL, 1},
        {"nanosecond truncation", {2020, 1, 2, 3, 4, 5, 678901299}, 0x01D5C1194B2B9814ULL, 1},
        {"maximum accepted year", {9999, 12, 31, 23, 59, 59, 999999999}, 0x24C85A5ED1C03FFFULL, 1},
        {"before epoch", {1600, 12, 31, 23, 59, 59, 0}, 0, 0},
        {"invalid month", {2024, 13, 1, 0, 0, 0, 0}, 0, 0},
        {"invalid day", {2024, 2, 30, 0, 0, 0, 0}, 0, 0},
        {"invalid hour", {2024, 1, 1, 24, 0, 0, 0}, 0, 0},
        {"invalid minute", {2024, 1, 1, 0, 60, 0, 0}, 0, 0},
        {"invalid second", {2024, 1, 1, 0, 0, 60, 0}, 0, 0},
        {"invalid nanoseconds", {2024, 1, 1, 0, 0, 0, 1000000000}, 0, 0},
        {"deterministic test clock", {2020, 1, 2, 3, 4, 5, 600000000}, 0x01D5C1194B1F8E00ULL, 1}
    };
    GXOS_EFI_TIME efi = {2024, 2, 29, 23, 59, 59, 0, 123456700, 0, 0, 0};
    uint8_t output[10];
    uint8_t expected_bytes[8] = {0x07, 0x00, 0xCD, 0x66, 0x6B, 0x6B, 0xDA, 0x01};
    uint64_t checked;
    size_t i;
    int failures = 0;

    for (i = 0; i != sizeof(vectors) / sizeof(vectors[0]); i++) {
        if (!expect_vector(&vectors[i])) failures++;
    }
    if (!expect_efi("EFI known leap day", efi, 0x01DA6B6B66CD0007ULL, 1)) failures++;
    efi.Month = 13;
    if (!expect_efi("EFI invalid month", efi, 0, 0)) failures++;
    efi.Month = 2;
    efi.Day = 30;
    if (!expect_efi("EFI invalid day", efi, 0, 0)) failures++;
    efi.Day = 29;
    efi.TimeZone = 2047;
    if (!expect_efi("EFI unspecified timezone", efi, 0, 0)) failures++;
    efi.TimeZone = 480;
    efi.Daylight = 1;
    if (!expect_efi("EFI pending daylight", efi, 0, 0)) failures++;
    efi.Daylight = 2;
    if (!expect_efi("EFI daylight already applied", efi, 0x01DA6BAE74F04007ULL, 1)) failures++;
    if (gxos_checked_add_u64(UINT64_MAX, 1, &checked) != 0) failures++;
    if (gxos_checked_mul_u64(UINT64_MAX, 2, &checked) != 0) failures++;
    if (gxos_write_filetime(0, 1) != GXOS_TIME_NULL_OUTPUT) failures++;
    memset(output, 0xA5, sizeof(output));
    if (gxos_write_filetime(output + 1, 0x01DA6B6B66CD0007ULL) != GXOS_TIME_OK) failures++;
    if (memcmp(output, "\xA5\x07\x00\xCD\x66\x6B\x6B\xDA\x01\xA5", 10) != 0) failures++;
    if (memcmp(output + 1, expected_bytes, 8) != 0) failures++;

    printf("PLATFORM_TIME_TESTS=%s failures=%d\n", failures == 0 ? "PASSED" : "FAILED", failures);
    return failures == 0 ? 0 : 1;
}

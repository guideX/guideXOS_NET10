#include "platform_time.h"

#include <stddef.h>

#define GXOS_MIN_YEAR 1601u
#define GXOS_MAX_YEAR 9999u
#define GXOS_EFI_MIN_YEAR 1900u
#define GXOS_UNSPECIFIED_TIMEZONE 2047
#define GXOS_TIMEZONE_MINUTES_MIN (-1440)
#define GXOS_TIMEZONE_MINUTES_MAX 1440
#define GXOS_SECONDS_PER_DAY 86400ULL
#define GXOS_FILETIME_UNITS_PER_SECOND 10000000ULL
#define GXOS_NANOSECONDS_PER_SECOND 1000000000u
#define GXOS_NANOSECONDS_PER_FILETIME_UNIT 100u

static GXOS_TIME_CONTEXT g_time_context;

#ifdef GXOS_TIME_MARKER_MUTATION
static int gxos_text_equal(const char *left, const char *right)
{
    while (*left != 0 && *left == *right) {
        left++;
        right++;
    }
    return *left == 0 && *right == 0;
}
#endif

static void gxos_trace(const char *marker, uint64_t value, uint32_t has_value)
{
    const char *emitted = marker;
#ifdef GXOS_TIME_MARKER_MUTATION
    if (gxos_text_equal(marker, "TIME_API_ENTER")) emitted = "TIME_API_ENTEr";
#endif
    if (g_time_context.trace != 0) g_time_context.trace(emitted, value, has_value);
}

int gxos_checked_add_u64(uint64_t left, uint64_t right, uint64_t *result)
{
    if (result == 0 || right > UINT64_MAX - left) return 0;
    *result = left + right;
    return 1;
}

int gxos_checked_mul_u64(uint64_t left, uint64_t right, uint64_t *result)
{
    if (result == 0 || (left != 0 && right > UINT64_MAX / left)) return 0;
    *result = left * right;
    return 1;
}

static int gxos_is_leap_year(uint32_t year)
{
    return (year % 4u) == 0 && ((year % 100u) != 0 || (year % 400u) == 0);
}

static uint32_t gxos_days_in_month(uint32_t year, uint32_t month)
{
    static const uint8_t days[] = {0, 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31};
    if (month == 2u && gxos_is_leap_year(year)) return 29u;
    return days[month];
}

static GXOS_TIME_RESULT gxos_validate_civil(const GXOS_CIVIL_TIME *civil)
{
    uint32_t month_days;
    if (civil == 0) return GXOS_TIME_INVALID_FIELD;
    if (civil->year < GXOS_MIN_YEAR || civil->year > GXOS_MAX_YEAR) return GXOS_TIME_INVALID_FIELD;
    if (civil->month < 1u || civil->month > 12u) return GXOS_TIME_INVALID_FIELD;
    month_days = gxos_days_in_month(civil->year, civil->month);
    if (civil->day < 1u || civil->day > month_days) return GXOS_TIME_INVALID_FIELD;
    if (civil->hour > 23u || civil->minute > 59u || civil->second > 59u) return GXOS_TIME_INVALID_FIELD;
    if (civil->nanosecond >= GXOS_NANOSECONDS_PER_SECOND) return GXOS_TIME_INVALID_FIELD;
    return GXOS_TIME_OK;
}

static GXOS_TIME_RESULT gxos_days_since_epoch(const GXOS_CIVIL_TIME *civil, uint64_t *days)
{
    uint32_t year;
    uint32_t month;
    uint64_t current = 0;
    uint64_t increment;

    for (year = GXOS_MIN_YEAR; year < civil->year; year++) {
        increment = gxos_is_leap_year(year) ? 366u : 365u;
        if (!gxos_checked_add_u64(current, increment, &current)) return GXOS_TIME_CONVERSION_OVERFLOW;
    }
    for (month = 1u; month < civil->month; month++) {
        if (!gxos_checked_add_u64(current, gxos_days_in_month(civil->year, month), &current)) {
            return GXOS_TIME_CONVERSION_OVERFLOW;
        }
    }
    if (!gxos_checked_add_u64(current, (uint64_t)civil->day - 1u, &current)) {
        return GXOS_TIME_CONVERSION_OVERFLOW;
    }
    *days = current;
    return GXOS_TIME_OK;
}

static GXOS_TIME_RESULT gxos_filetime_from_local_civil(const GXOS_CIVIL_TIME *civil,
                                                       int32_t timezone_minutes,
                                                       uint64_t *filetime)
{
    GXOS_TIME_RESULT result;
    uint64_t days;
    uint64_t seconds;
    uint64_t day_seconds;
    uint64_t time_seconds;
    uint64_t adjusted_seconds;
    uint64_t units;
    uint64_t fractional_units;

    result = gxos_validate_civil(civil);
    if (result != GXOS_TIME_OK) return result;
    if (timezone_minutes < GXOS_TIMEZONE_MINUTES_MIN || timezone_minutes > GXOS_TIMEZONE_MINUTES_MAX) {
        return GXOS_TIME_INVALID_TIMEZONE;
    }
    result = gxos_days_since_epoch(civil, &days);
    if (result != GXOS_TIME_OK) return result;
    if (!gxos_checked_mul_u64(days, GXOS_SECONDS_PER_DAY, &day_seconds)) {
        return GXOS_TIME_CONVERSION_OVERFLOW;
    }
    time_seconds = (uint64_t)civil->hour * 3600ULL +
                   (uint64_t)civil->minute * 60ULL + civil->second;
    if (!gxos_checked_add_u64(day_seconds, time_seconds, &seconds)) {
        return GXOS_TIME_CONVERSION_OVERFLOW;
    }

    /* EFI defines Localtime = UTC - TimeZone, so UTC = Localtime + TimeZone. */
    if (timezone_minutes >= 0) {
        if (!gxos_checked_add_u64(seconds, (uint64_t)timezone_minutes * 60ULL, &adjusted_seconds)) {
            return GXOS_TIME_CONVERSION_OVERFLOW;
        }
    } else {
        uint64_t magnitude = (uint64_t)(-timezone_minutes) * 60ULL;
        if (magnitude > seconds) return GXOS_TIME_CONVERSION_OVERFLOW;
        adjusted_seconds = seconds - magnitude;
    }

#ifdef GXOS_TEST_WRONG_EPOCH
    if (!gxos_checked_add_u64(adjusted_seconds, GXOS_SECONDS_PER_DAY, &adjusted_seconds)) {
        return GXOS_TIME_CONVERSION_OVERFLOW;
    }
#endif
    if (!gxos_checked_mul_u64(adjusted_seconds, GXOS_FILETIME_UNITS_PER_SECOND, &units)) {
        return GXOS_TIME_CONVERSION_OVERFLOW;
    }
    fractional_units = civil->nanosecond / GXOS_NANOSECONDS_PER_FILETIME_UNIT;
    if (!gxos_checked_add_u64(units, fractional_units, filetime)) {
        return GXOS_TIME_CONVERSION_OVERFLOW;
    }
    return GXOS_TIME_OK;
}

GXOS_TIME_RESULT gxos_filetime_from_utc_civil(const GXOS_CIVIL_TIME *civil, uint64_t *filetime)
{
    if (filetime == 0) return GXOS_TIME_NULL_OUTPUT;
    return gxos_filetime_from_local_civil(civil, 0, filetime);
}

GXOS_TIME_RESULT gxos_filetime_from_efi_time(const GXOS_EFI_TIME *firmware_time, uint64_t *filetime)
{
    GXOS_CIVIL_TIME civil;
    int32_t timezone_minutes;

    if (filetime == 0) return GXOS_TIME_NULL_OUTPUT;
    if (firmware_time == 0) return GXOS_TIME_INVALID_FIELD;
    if (firmware_time->Year < GXOS_EFI_MIN_YEAR || firmware_time->Year > GXOS_MAX_YEAR) {
        return GXOS_TIME_INVALID_FIELD;
    }
    if (firmware_time->Pad1 != 0 || firmware_time->Pad2 != 0) return GXOS_TIME_INVALID_FIELD;
    if (firmware_time->TimeZone == GXOS_UNSPECIFIED_TIMEZONE) return GXOS_TIME_INVALID_TIMEZONE;
    if (firmware_time->TimeZone < GXOS_TIMEZONE_MINUTES_MIN ||
        firmware_time->TimeZone > GXOS_TIMEZONE_MINUTES_MAX) return GXOS_TIME_INVALID_TIMEZONE;
    if ((firmware_time->Daylight & (uint8_t)~3u) != 0) return GXOS_TIME_INVALID_FIELD;
    if ((firmware_time->Daylight & 1u) != 0 && (firmware_time->Daylight & 2u) == 0) {
        /* The EFI value says DST is pending, but gives no adjustment amount. */
        return GXOS_TIME_INVALID_TIMEZONE;
    }

    civil.year = firmware_time->Year;
    civil.month = firmware_time->Month;
    civil.day = firmware_time->Day;
    civil.hour = firmware_time->Hour;
    civil.minute = firmware_time->Minute;
    civil.second = firmware_time->Second;
    civil.nanosecond = firmware_time->Nanosecond;
    timezone_minutes = firmware_time->TimeZone;
    return gxos_filetime_from_local_civil(&civil, timezone_minutes, filetime);
}

GXOS_TIME_RESULT gxos_write_filetime(void *output, uint64_t filetime)
{
    uint8_t *bytes = (uint8_t *)output;
    uint32_t low;
    uint32_t high;
    uint32_t i;

    if (bytes == 0) return GXOS_TIME_NULL_OUTPUT;
    low = (uint32_t)filetime;
    high = (uint32_t)(filetime >> 32);
    for (i = 0; i != 4; i++) bytes[i] = (uint8_t)(low >> (i * 8u));
    for (i = 0; i != 4; i++) bytes[4u + i] = (uint8_t)(high >> (i * 8u));
    return GXOS_TIME_OK;
}

void gxos_time_configure(const GXOS_TIME_CONTEXT *context)
{
    if (context == 0) {
        g_time_context.runtime_services = 0;
        g_time_context.trace = 0;
        g_time_context.set_phase = 0;
        g_time_context.halt = 0;
        g_time_context.call_count = 0;
        return;
    }
    g_time_context = *context;
}

static void gxos_time_fail(const char *marker)
{
    gxos_trace(marker, 0, 0);
    if (g_time_context.halt != 0) g_time_context.halt();
    for (;;) { }
}

void GXOS_EFIAPI gxos_get_system_time_as_file_time(void *output)
{
    GXOS_EFI_TIME firmware_time;
    GXOS_TIME_RESULT result;
    GXOS_EFI_STATUS status;
    uint64_t filetime = 0;
    uint64_t caller;
    uint32_t i;

    if (g_time_context.set_phase != 0) g_time_context.set_phase(3u);
    gxos_trace("TIME_API_ENTER", 0, 0);
    if (g_time_context.call_count != 0) (*g_time_context.call_count)++;
    if (g_time_context.call_count != 0) gxos_trace("TIME_API_COUNT", *g_time_context.call_count, 1);
    if (g_time_context.last_output != 0) *g_time_context.last_output = (uint64_t)(uintptr_t)output;
    gxos_trace("TIME_OUTPUT_POINTER", (uint64_t)(uintptr_t)output, 1);
    caller = (uint64_t)(uintptr_t)__builtin_return_address(0);
    if (g_time_context.last_caller != 0) *g_time_context.last_caller = caller;
    gxos_trace("TIME_CALLER", caller, 1);
    if (output == 0) gxos_time_fail("TIME_NULL_OUTPUT");
    if (g_time_context.runtime_services == 0 || g_time_context.runtime_services->GetTime == 0) {
        gxos_time_fail("TIME_FIRMWARE_ERROR");
    }
    for (i = 0; i != sizeof(firmware_time); i++) ((uint8_t *)&firmware_time)[i] = 0;
    status = g_time_context.runtime_services->GetTime(&firmware_time, 0);
    if (g_time_context.last_firmware_status != 0) *g_time_context.last_firmware_status = status;
    gxos_trace("TIME_FIRMWARE_STATUS", status, 1);
    if (status != 0) gxos_time_fail("TIME_FIRMWARE_ERROR");

#ifdef GXOS_TIME_TEST_INVALID_MONTH
    firmware_time.Month = 13;
#endif
#ifdef GXOS_TIME_TEST_INVALID_DAY
    firmware_time.Day = 32;
#endif
#ifdef GXOS_TIME_TEST_INVALID_TIMEZONE
    firmware_time.TimeZone = GXOS_UNSPECIFIED_TIMEZONE;
#endif
    gxos_trace("UEFI_TIME_OK", 0, 0);
    gxos_trace("TIME_TIMEZONE", (uint64_t)(int64_t)firmware_time.TimeZone, 1);
    gxos_trace("TIME_DAYLIGHT", firmware_time.Daylight, 1);

#ifdef GXOS_TIME_TEST_FIXED_ZERO
    filetime = 0;
    gxos_trace("TIME_TEST_FIXED_ZERO", filetime, 1);
#else
#ifdef GXOS_TIME_TEST_INVALID_TIMEZONE
    gxos_time_fail("TIME_INVALID_TIMEZONE");
#endif
    if (firmware_time.TimeZone == GXOS_UNSPECIFIED_TIMEZONE && g_time_context.unspecified_timezone_is_utc != 0) {
        gxos_trace("TIME_UNSPECIFIED_TIMEZONE_UTC_POLICY", 0, 0);
        firmware_time.TimeZone = 0;
    }
    result = gxos_filetime_from_efi_time(&firmware_time, &filetime);
    if (result == GXOS_TIME_INVALID_TIMEZONE) gxos_time_fail("TIME_INVALID_TIMEZONE");
    if (result == GXOS_TIME_INVALID_FIELD) gxos_time_fail("TIME_INVALID_FIELD");
    if (result == GXOS_TIME_CONVERSION_OVERFLOW) gxos_time_fail("TIME_CONVERSION_OVERFLOW");
    if (result != GXOS_TIME_OK) gxos_time_fail("TIME_INVALID_FIELD");
#endif
    gxos_trace("FILETIME_CONVERSION_OK", filetime, 1);
    gxos_trace("FILETIME_LOW", (uint32_t)filetime, 1);
    gxos_trace("FILETIME_HIGH", (uint32_t)(filetime >> 32), 1);
    if (g_time_context.last_filetime != 0) *g_time_context.last_filetime = filetime;
    result = gxos_write_filetime(output, filetime);
    if (result != GXOS_TIME_OK) gxos_time_fail("TIME_NULL_OUTPUT");
    gxos_trace("TIME_API_RETURN", filetime, 1);
    if (g_time_context.set_phase != 0) g_time_context.set_phase(4u);
}

#ifndef GXOS_PLATFORM_TIME_H
#define GXOS_PLATFORM_TIME_H

#include <stdint.h>

#if defined(__x86_64__)
#define GXOS_EFIAPI __attribute__((ms_abi))
#else
#define GXOS_EFIAPI
#endif

typedef uint64_t GXOS_EFI_STATUS;

typedef struct {
    uint16_t Year;
    uint8_t Month;
    uint8_t Day;
    uint8_t Hour;
    uint8_t Minute;
    uint8_t Second;
    uint8_t Pad1;
    uint32_t Nanosecond;
    int16_t TimeZone;
    uint8_t Daylight;
    uint8_t Pad2;
} GXOS_EFI_TIME;

typedef GXOS_EFI_STATUS (GXOS_EFIAPI *GXOS_EFI_GET_TIME)(GXOS_EFI_TIME *time, void *capabilities);

typedef struct {
    uint64_t Header[3];
    GXOS_EFI_GET_TIME GetTime;
} GXOS_EFI_RUNTIME_SERVICES;

typedef struct {
    uint32_t year;
    uint32_t month;
    uint32_t day;
    uint32_t hour;
    uint32_t minute;
    uint32_t second;
    uint32_t nanosecond;
} GXOS_CIVIL_TIME;

typedef enum {
    GXOS_TIME_OK = 0,
    GXOS_TIME_NULL_OUTPUT = 1,
    GXOS_TIME_INVALID_FIELD = 2,
    GXOS_TIME_INVALID_TIMEZONE = 3,
    GXOS_TIME_CONVERSION_OVERFLOW = 4,
    GXOS_TIME_FIRMWARE_ERROR = 5
} GXOS_TIME_RESULT;

typedef void (*GXOS_TIME_TRACE)(const char *marker, uint64_t value, uint32_t has_value);
typedef void (*GXOS_TIME_PHASE_SETTER)(uint32_t phase);
typedef void (*GXOS_TIME_HALT)(void);

typedef struct {
    GXOS_EFI_RUNTIME_SERVICES *runtime_services;
    uint32_t unspecified_timezone_is_utc;
    GXOS_TIME_TRACE trace;
    GXOS_TIME_PHASE_SETTER set_phase;
    GXOS_TIME_HALT halt;
    uint64_t *last_caller;
    uint64_t *last_output;
    uint64_t *last_firmware_status;
    uint64_t *last_filetime;
    uint64_t *call_count;
} GXOS_TIME_CONTEXT;

_Static_assert(sizeof(GXOS_EFI_TIME) == 16, "EFI_TIME layout must remain 16 bytes");
_Static_assert(sizeof(GXOS_EFI_RUNTIME_SERVICES) == 32, "runtime service prefix layout");

int gxos_checked_add_u64(uint64_t left, uint64_t right, uint64_t *result);
int gxos_checked_mul_u64(uint64_t left, uint64_t right, uint64_t *result);
GXOS_TIME_RESULT gxos_filetime_from_utc_civil(const GXOS_CIVIL_TIME *civil, uint64_t *filetime);
GXOS_TIME_RESULT gxos_filetime_from_efi_time(const GXOS_EFI_TIME *firmware_time, uint64_t *filetime);
GXOS_TIME_RESULT gxos_write_filetime(void *output, uint64_t filetime);

void gxos_time_configure(const GXOS_TIME_CONTEXT *context);
void GXOS_EFIAPI gxos_get_system_time_as_file_time(void *output);

#endif

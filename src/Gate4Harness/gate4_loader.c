#include <stdint.h>
#include <stddef.h>
#include "platform_time.h"
#include "platform_performance.h"
#include "crt_onexit.h"

typedef uint64_t EFI_STATUS;
typedef uint64_t EFI_PHYSICAL_ADDRESS;
typedef uint64_t EFI_VIRTUAL_ADDRESS;
typedef uint64_t EFI_TPL;
typedef uint64_t EFI_MEMORY_TYPE;
typedef uint64_t EFI_HANDLE;
typedef uint64_t EFI_EVENT;
typedef uint64_t EFI_LBA;
typedef uint64_t EFI_UINTN;
typedef void *EFI_INTERFACE;

#if defined(__x86_64__)
#define EFIAPI __attribute__((ms_abi))
#else
#define EFIAPI
#endif
#define EFI_SUCCESS ((EFI_STATUS)0)
#define EFI_ERROR(status) (((status) >> 63) != 0)
#define EFI_OPEN_MODE_READ ((uint64_t)1)
#define EFI_ALLOCATE_ANY_PAGES ((uint32_t)0)
#define EFI_LOADER_CODE ((uint32_t)1)
#define EFI_LOADER_DATA ((uint32_t)4)
#define EFI_PAGE_SIZE ((uint64_t)4096)
#define EFI_LOADED_IMAGE_PROTOCOL_REVISION ((uint32_t)0x1000)

typedef struct {
    uint32_t Data1;
    uint16_t Data2;
    uint16_t Data3;
    uint8_t Data4[8];
} EFI_GUID;

typedef struct {
    uint64_t Signature;
    uint32_t Revision;
    uint32_t HeaderSize;
    uint32_t CRC32;
    uint32_t Reserved;
} EFI_TABLE_HEADER;

typedef EFI_STATUS (EFIAPI *EFI_RAISE_TPL)(EFI_TPL NewTpl);
typedef EFI_STATUS (EFIAPI *EFI_RESTORE_TPL)(EFI_TPL OldTpl);
typedef EFI_STATUS (EFIAPI *EFI_ALLOCATE_PAGES)(uint32_t Type, uint32_t MemoryType, uint64_t Pages, EFI_PHYSICAL_ADDRESS *Memory);
typedef EFI_STATUS (EFIAPI *EFI_FREE_PAGES)(EFI_PHYSICAL_ADDRESS Memory, uint64_t Pages);
typedef EFI_STATUS (EFIAPI *EFI_GET_MEMORY_MAP)(EFI_UINTN *MemoryMapSize, void *MemoryMap, EFI_UINTN *MapKey, EFI_UINTN *DescriptorSize, uint32_t *DescriptorVersion);
typedef EFI_STATUS (EFIAPI *EFI_ALLOCATE_POOL)(uint32_t PoolType, EFI_UINTN Size, void **Buffer);
typedef EFI_STATUS (EFIAPI *EFI_FREE_POOL)(void *Buffer);
typedef EFI_STATUS (EFIAPI *EFI_CREATE_EVENT)(uint32_t Type, EFI_TPL NotifyTpl, void *NotifyFunction, void *NotifyContext, EFI_EVENT *Event);
typedef EFI_STATUS (EFIAPI *EFI_SET_TIMER)(EFI_EVENT Event, uint32_t Type, uint64_t TriggerTime);
typedef EFI_STATUS (EFIAPI *EFI_WAIT_FOR_EVENT)(EFI_UINTN NumberOfEvents, EFI_EVENT *Event, EFI_UINTN *Index);
typedef EFI_STATUS (EFIAPI *EFI_SIGNAL_EVENT)(EFI_EVENT Event);
typedef EFI_STATUS (EFIAPI *EFI_CLOSE_EVENT)(EFI_EVENT Event);
typedef EFI_STATUS (EFIAPI *EFI_CHECK_EVENT)(EFI_EVENT Event);
typedef EFI_STATUS (EFIAPI *EFI_INSTALL_PROTOCOL_INTERFACE)(EFI_HANDLE *Handle, EFI_GUID *Protocol, uint32_t InterfaceType, void *Interface);
typedef EFI_STATUS (EFIAPI *EFI_REINSTALL_PROTOCOL_INTERFACE)(EFI_HANDLE Handle, EFI_GUID *Protocol, void *OldInterface, void *NewInterface);
typedef EFI_STATUS (EFIAPI *EFI_UNINSTALL_PROTOCOL_INTERFACE)(EFI_HANDLE Handle, EFI_GUID *Protocol, void *Interface);
typedef EFI_STATUS (EFIAPI *EFI_HANDLE_PROTOCOL)(EFI_HANDLE Handle, EFI_GUID *Protocol, void **Interface);
typedef EFI_STATUS (EFIAPI *EFI_REGISTER_PROTOCOL_NOTIFY)(EFI_GUID *Protocol, EFI_EVENT Event, void **Registration);
typedef EFI_STATUS (EFIAPI *EFI_LOCATE_HANDLE)(uint32_t SearchType, EFI_GUID *Protocol, void *SearchKey, EFI_UINTN *BufferSize, EFI_HANDLE *Buffer);
typedef EFI_STATUS (EFIAPI *EFI_LOCATE_DEVICE_PATH)(EFI_GUID *Protocol, void **DevicePath, EFI_HANDLE *Device);
typedef EFI_STATUS (EFIAPI *EFI_INSTALL_CONFIGURATION_TABLE)(EFI_GUID *Guid, void *Table);
typedef EFI_STATUS (EFIAPI *EFI_LOAD_IMAGE)(uint8_t BootPolicy, EFI_HANDLE ParentImageHandle, void *DevicePath, void *SourceBuffer, EFI_UINTN SourceSize, EFI_HANDLE *ImageHandle);
typedef EFI_STATUS (EFIAPI *EFI_START_IMAGE)(EFI_HANDLE ImageHandle, EFI_UINTN *ExitDataSize, uint16_t **ExitData);
typedef EFI_STATUS (EFIAPI *EFI_EXIT)(EFI_HANDLE ImageHandle, EFI_STATUS ExitStatus, EFI_UINTN ExitDataSize, uint16_t *ExitData);
typedef EFI_STATUS (EFIAPI *EFI_UNLOAD_IMAGE)(EFI_HANDLE ImageHandle);
typedef EFI_STATUS (EFIAPI *EFI_EXIT_BOOT_SERVICES)(EFI_HANDLE ImageHandle, EFI_UINTN MapKey);
typedef EFI_STATUS (EFIAPI *EFI_GET_NEXT_MONOTONIC_COUNT)(uint64_t *Count);
typedef EFI_STATUS (EFIAPI *EFI_STALL)(uint64_t Microseconds);
typedef EFI_STATUS (EFIAPI *EFI_SET_WATCHDOG_TIMER)(uint64_t Timeout, uint64_t WatchdogCode, EFI_UINTN DataSize, uint16_t *WatchdogData);

typedef struct {
    EFI_TABLE_HEADER Hdr;
    EFI_RAISE_TPL RaiseTPL;
    EFI_RESTORE_TPL RestoreTPL;
    EFI_ALLOCATE_PAGES AllocatePages;
    EFI_FREE_PAGES FreePages;
    EFI_GET_MEMORY_MAP GetMemoryMap;
    EFI_ALLOCATE_POOL AllocatePool;
    EFI_FREE_POOL FreePool;
    EFI_CREATE_EVENT CreateEvent;
    EFI_SET_TIMER SetTimer;
    EFI_WAIT_FOR_EVENT WaitForEvent;
    EFI_SIGNAL_EVENT SignalEvent;
    EFI_CLOSE_EVENT CloseEvent;
    EFI_CHECK_EVENT CheckEvent;
    EFI_INSTALL_PROTOCOL_INTERFACE InstallProtocolInterface;
    EFI_REINSTALL_PROTOCOL_INTERFACE ReinstallProtocolInterface;
    EFI_UNINSTALL_PROTOCOL_INTERFACE UninstallProtocolInterface;
    EFI_HANDLE_PROTOCOL HandleProtocol;
    void *Reserved;
    EFI_REGISTER_PROTOCOL_NOTIFY RegisterProtocolNotify;
    EFI_LOCATE_HANDLE LocateHandle;
    EFI_LOCATE_DEVICE_PATH LocateDevicePath;
    EFI_INSTALL_CONFIGURATION_TABLE InstallConfigurationTable;
    EFI_LOAD_IMAGE LoadImage;
    EFI_START_IMAGE StartImage;
    EFI_EXIT Exit;
    EFI_UNLOAD_IMAGE UnloadImage;
    EFI_EXIT_BOOT_SERVICES ExitBootServices;
    EFI_GET_NEXT_MONOTONIC_COUNT GetNextMonotonicCount;
    EFI_STALL Stall;
    EFI_SET_WATCHDOG_TIMER SetWatchdogTimer;
} EFI_BOOT_SERVICES;

typedef struct {
    EFI_TABLE_HEADER Hdr;
    uint16_t *FirmwareVendor;
    uint32_t FirmwareRevision;
    EFI_HANDLE ConsoleInHandle;
    void *ConIn;
    EFI_HANDLE ConsoleOutHandle;
    void *ConOut;
    EFI_HANDLE StandardErrorHandle;
    void *StdErr;
    GXOS_EFI_RUNTIME_SERVICES *RuntimeServices;
    EFI_BOOT_SERVICES *BootServices;
    EFI_UINTN NumberOfTableEntries;
    struct EFI_CONFIGURATION_TABLE *ConfigurationTable;
} EFI_SYSTEM_TABLE;

typedef struct EFI_CONFIGURATION_TABLE {
    EFI_GUID VendorGuid;
    void *VendorTable;
} EFI_CONFIGURATION_TABLE;

typedef struct {
    uint64_t Revision;
    EFI_HANDLE ParentHandle;
    EFI_SYSTEM_TABLE *SystemTable;
    EFI_HANDLE DeviceHandle;
    void *FilePath;
} EFI_LOADED_IMAGE_PROTOCOL;

typedef struct _EFI_FILE_PROTOCOL EFI_FILE_PROTOCOL;
typedef EFI_STATUS (EFIAPI *EFI_FILE_OPEN)(EFI_FILE_PROTOCOL *This, EFI_FILE_PROTOCOL **NewHandle, uint16_t *FileName, uint64_t OpenMode, uint64_t Attributes);
typedef EFI_STATUS (EFIAPI *EFI_FILE_CLOSE)(EFI_FILE_PROTOCOL *This);
typedef EFI_STATUS (EFIAPI *EFI_FILE_READ)(EFI_FILE_PROTOCOL *This, EFI_UINTN *BufferSize, void *Buffer);

struct _EFI_FILE_PROTOCOL {
    uint64_t Revision;
    EFI_FILE_OPEN Open;
    EFI_FILE_CLOSE Close;
    void *Delete;
    EFI_FILE_READ Read;
};

typedef struct {
    uint64_t Revision;
    EFI_STATUS (EFIAPI *OpenVolume)(void *This, EFI_FILE_PROTOCOL **Root);
} EFI_SIMPLE_FILE_SYSTEM_PROTOCOL;

static const EFI_GUID gLoadedImageProtocol = {0x5B1B31A1, 0x9562, 0x11D2, {0x8E, 0x3F, 0x00, 0xA0, 0xC9, 0x69, 0x72, 0x3B}};
static const EFI_GUID gSimpleFileSystemProtocol = {0x964E5B22, 0x6459, 0x11D2, {0x8E, 0x39, 0x00, 0xA0, 0xC9, 0x69, 0x72, 0x3B}};

#pragma pack(push, 1)
typedef struct {
    uint32_t Magic;
    uint16_t Version;
    uint16_t Size;
    uint32_t Architecture;
    uint32_t Flags;
    uint64_t SerialWrite;
} GuideXBootInfo;
#pragma pack(pop)

typedef void (EFIAPI *GuideXSerialWrite)(const uint8_t *bytes, EFI_UINTN length);
typedef int (EFIAPI *ManagedMainEntry)(uintptr_t boot_info_address);

enum {
    GUIDEX_BOOT_MAGIC = 0x534F5847u,
    GUIDEX_BOOT_VERSION = 1u,
    GUIDEX_BOOT_SIZE = 24u,
    GUIDEX_BOOT_ARCH_X64 = 0x8664u
};

_Static_assert(sizeof(GuideXBootInfo) == 24, "GuideXBootInfo size must remain 24 bytes");
_Static_assert(offsetof(GuideXBootInfo, Magic) == 0, "GuideXBootInfo.Magic offset");
_Static_assert(offsetof(GuideXBootInfo, Version) == 4, "GuideXBootInfo.Version offset");
_Static_assert(offsetof(GuideXBootInfo, Size) == 6, "GuideXBootInfo.Size offset");
_Static_assert(offsetof(GuideXBootInfo, Architecture) == 8, "GuideXBootInfo.Architecture offset");
_Static_assert(offsetof(GuideXBootInfo, Flags) == 12, "GuideXBootInfo.Flags offset");
_Static_assert(offsetof(GuideXBootInfo, SerialWrite) == 16, "GuideXBootInfo.SerialWrite offset");
_Static_assert(sizeof(uintptr_t) == 8, "Gate 4 requires x64 pointers");
_Static_assert(GUIDEX_BOOT_MAGIC == 0x534F5847u, "GuideXBootInfo magic");
_Static_assert(GUIDEX_BOOT_VERSION == 1u, "GuideXBootInfo version");
_Static_assert(GUIDEX_BOOT_SIZE == sizeof(GuideXBootInfo), "GuideXBootInfo size constant");
_Static_assert(GUIDEX_BOOT_ARCH_X64 == 0x8664u, "GuideXBootInfo architecture");
_Static_assert(sizeof(GuideXSerialWrite) == sizeof(uintptr_t), "GuideXSerialWrite pointer ABI");

enum {
    PHASE_LOADER = 0,
    PHASE_NEGATIVE = 1,
    PHASE_BEFORE_TIME_CALL = 2,
    PHASE_IN_TIME_CALL = 3,
    PHASE_AFTER_TIME_CALL = 4,
    PHASE_IN_TIME_CONSUMER = 5,
    PHASE_AFTER_TIME_CONSUMER = 6,
    PHASE_BEFORE_MANAGED_CALL = 7,
    PHASE_IN_MANAGED = 8,
    PHASE_AFTER_MANAGED_RETURN = 9,
    PHASE_COMPLETE = 10,
    PHASE_BEFORE_PERF_SOURCE_DISCOVERY = 11,
    PHASE_IN_PERF_SOURCE_DISCOVERY = 12,
    PHASE_BEFORE_PERF_SOURCE_INIT = 13,
    PHASE_IN_PERF_SOURCE_INIT = 14,
    PHASE_BEFORE_QPC_CALL = 15,
    PHASE_IN_QPC_CALL = 16,
    PHASE_AFTER_QPC_CALL = 17,
    PHASE_AFTER_SECURITY_COOKIE_INIT = 18
};

static uint32_t g_phase;
static uint64_t g_managed_target;
static uint64_t g_managed_image_base;
static uint64_t g_boot_info_address;
static uint64_t g_last_time_caller;
static uint64_t g_last_time_output;
static uint64_t g_last_time_firmware_status;
static uint64_t g_last_time_filetime;
static uint64_t g_time_call_count;
static uint64_t g_perf_source_code;
static uint64_t g_perf_source_address;
static uint64_t g_perf_frequency;
static uint64_t g_perf_last_raw;
static int64_t g_perf_last_normalized;
static uint64_t g_perf_qpc_call_count;
static int64_t g_perf_qpc_first;
static int64_t g_perf_qpc_last;
static uint64_t g_perf_qpc_min_delta;
static uint64_t g_perf_qpc_max_delta;
static uint64_t g_perf_qpc_regressions;
static EFI_PHYSICAL_ADDRESS g_tls_block;
static GuideXBootInfo g_boot_info;

static void serial_out8(uint16_t port, uint8_t value)
{
    __asm__ volatile ("outb %0, %1" : : "a"(value), "Nd"(port));
}

static uint8_t serial_in8(uint16_t port)
{
    uint8_t value;
    __asm__ volatile ("inb %1, %0" : "=a"(value) : "Nd"(port));
    return value;
}

static void serial_init(void)
{
    serial_out8(0x3F8 + 1, 0x00);
    serial_out8(0x3F8 + 3, 0x80);
    serial_out8(0x3F8 + 0, 0x03);
    serial_out8(0x3F8 + 1, 0x00);
    serial_out8(0x3F8 + 3, 0x03);
    serial_out8(0x3F8 + 2, 0xC7);
    serial_out8(0x3F8 + 4, 0x0B);
}

static void serial_char(uint8_t value)
{
    while ((serial_in8(0x3F8 + 5) & 0x20) == 0) { }
    serial_out8(0x3F8, value);
}

static void serial_text(const char *text)
{
    while (*text != 0) {
        serial_char((uint8_t)*text++);
    }
}

static void EFIAPI serial_write(const uint8_t *bytes, EFI_UINTN length)
{
    while (length-- != 0) serial_char(*bytes++);
}

static void serial_hex64(uint64_t value)
{
    static const char digits[] = "0123456789ABCDEF";
    uint32_t shift = 60;
    while (1) {
        serial_char((uint8_t)digits[(value >> shift) & 0xF]);
        if (shift == 0) break;
        shift -= 4;
    }
}

static void serial_u32(uint32_t value)
{
    char digits[10];
    uint32_t count = 0;
    if (value == 0) {
        serial_char('0');
        return;
    }
    while (value != 0 && count < 10) {
        digits[count++] = (char)('0' + (value % 10));
        value /= 10;
    }
    while (count != 0) serial_char((uint8_t)digits[--count]);
}

static void halt_forever(void)
{
    __asm__ volatile ("cli");
    for (;;) __asm__ volatile ("hlt");
}

static void fail(const char *reason)
{
    serial_text("GXOS_NET10:FAIL:");
    serial_text(reason);
    serial_text("\r\n");
    halt_forever();
}

static void serial_field_hex(const char *name, uint64_t value)
{
    serial_text(name);
    serial_hex64(value);
}

static void serial_field_u32(const char *name, uint32_t value)
{
    serial_text(name);
    serial_u32(value);
}

static void halt_forever(void);

static void time_trace(const char *marker, uint64_t value, uint32_t has_value)
{
    serial_text("GXOS_NET10:");
    serial_text(marker);
    if (has_value != 0) {
        serial_text("=0x");
        serial_hex64(value);
    }
    serial_text("\r\n");
}

static void time_set_phase(uint32_t phase)
{
    g_phase = phase;
}

static void time_halt(void)
{
    halt_forever();
}

static void configure_platform_time(GXOS_EFI_RUNTIME_SERVICES *runtime_services)
{
    GXOS_TIME_CONTEXT context;
    context.runtime_services = runtime_services;
#ifdef GXOS_ASSUME_UNSPECIFIED_TIMEZONE_UTC
    context.unspecified_timezone_is_utc = 1;
#else
    context.unspecified_timezone_is_utc = 0;
#endif
    context.trace = time_trace;
    context.set_phase = time_set_phase;
    context.halt = time_halt;
    context.last_caller = &g_last_time_caller;
    context.last_output = &g_last_time_output;
    context.last_firmware_status = &g_last_time_firmware_status;
    context.last_filetime = &g_last_time_filetime;
    context.call_count = &g_time_call_count;
    gxos_time_configure(&context);
}

__attribute__((unused)) static void configure_platform_performance(const EFI_SYSTEM_TABLE *system_table)
{
    GXOS_PERF_CONTEXT context;
    context.trace = time_trace;
    context.set_phase = time_set_phase;
    context.halt = time_halt;
    context.configuration_table = system_table == 0 ? 0 : system_table->ConfigurationTable;
    context.configuration_table_count = system_table == 0 ? 0 : system_table->NumberOfTableEntries;
    context.source_code = &g_perf_source_code;
    context.source_address = &g_perf_source_address;
    context.frequency = &g_perf_frequency;
    context.last_raw = &g_perf_last_raw;
    context.last_normalized = &g_perf_last_normalized;
    context.call_count = &g_perf_qpc_call_count;
    context.first_value = &g_perf_qpc_first;
    context.last_value = &g_perf_qpc_last;
    context.minimum_delta = &g_perf_qpc_min_delta;
    context.maximum_delta = &g_perf_qpc_max_delta;
    context.regressions = &g_perf_qpc_regressions;
    if (!gxos_perf_configure(&context)) fail("perf-source-init");
}

static void emit_qpc_summary(void)
{
    serial_field_hex("GXOS_NET10:QPC_COUNT=0x", g_perf_qpc_call_count);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:QPC_FIRST=0x", (uint64_t)g_perf_qpc_first);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:QPC_LAST=0x", (uint64_t)g_perf_qpc_last);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:QPC_MIN_DELTA=0x", g_perf_qpc_min_delta);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:QPC_MAX_DELTA=0x", g_perf_qpc_max_delta);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:QPC_REGRESSIONS=0x", g_perf_qpc_regressions);
    serial_text("\r\n");
}

__attribute__((unused)) static void emit_performance_diagnostics(void)
{
    serial_field_hex("GXOS_NET10:PERF_SOURCE_CODE=0x", g_perf_source_code);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:PERF_SOURCE_ADDRESS=0x", g_perf_source_address);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:PERF_FREQUENCY=0x", g_perf_frequency);
    serial_text("\r\n");
    emit_qpc_summary();
    serial_field_hex("GXOS_NET10:PERF_LAST_RAW=0x", g_perf_last_raw);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:PERF_LAST_NORMALIZED=0x", (uint64_t)g_perf_last_normalized);
    serial_text("\r\n");
}

__attribute__((unused)) static void run_performance_diagnostics(EFI_BOOT_SERVICES *boot_services)
{
    int64_t frequency_first;
#ifdef GXOS_PERF_STALL_DIAGNOSTIC
    int64_t frequency_second;
#endif
    int64_t first;
#ifdef GXOS_PERF_STALL_DIAGNOSTIC
    int64_t immediate;
    int64_t delayed;
    EFI_STATUS stall_status;
#endif

    (void)boot_services;
    if (!gxos_query_performance_frequency(&frequency_first) || frequency_first <= 0) {
        fail("perf-frequency-contract");
    }
#ifdef GXOS_PERF_STALL_DIAGNOSTIC
    if (!gxos_query_performance_frequency(&frequency_second) || frequency_first != frequency_second) {
        fail("perf-frequency-contract");
    }
#endif
    serial_field_hex("GXOS_NET10:PERF_FREQUENCY_QUERY=0x", (uint64_t)frequency_first);
    serial_text("\r\n");
    g_phase = PHASE_BEFORE_QPC_CALL;
    if (!gxos_query_performance_counter(&first)) fail("qpc-first-read");
#ifndef GXOS_PERF_STALL_DIAGNOSTIC
    serial_field_hex("GXOS_NET10:QPC_STARTUP_DIAGNOSTIC=0x", (uint64_t)first);
    serial_text("\r\n");
    serial_text("GXOS_NET10:PERF_STALL_NOT_RUN_IN_STARTUP\r\n");
    return;
#else
    g_phase = PHASE_BEFORE_QPC_CALL;
    if (!gxos_query_performance_counter(&immediate)) fail("qpc-immediate-read");
    if (immediate < first) fail("qpc-immediate-regression");
    serial_field_hex("GXOS_NET10:QPC_IMMEDIATE_DELTA=0x", (uint64_t)(immediate - first));
    serial_text("\r\n");
    if (boot_services != 0 && boot_services->Stall != 0) {
#ifdef GXOS_PERF_STALL_DIAGNOSTIC
        stall_status = boot_services->Stall(1);
        serial_field_hex("GXOS_NET10:PERF_STALL_STATUS=0x", stall_status);
        serial_text("\r\n");
        if (stall_status != EFI_SUCCESS) fail("perf-stall-diagnostic");
        g_phase = PHASE_BEFORE_QPC_CALL;
        if (!gxos_query_performance_counter(&delayed)) fail("qpc-delayed-read");
        if (delayed < immediate) fail("qpc-stall-regression");
        serial_field_hex("GXOS_NET10:QPC_STALL_DELTA=0x", (uint64_t)(delayed - immediate));
        serial_text("\r\n");
        serial_text("GXOS_NET10:PERF_STALL_TEST_OK\r\n");
#else
        serial_text("GXOS_NET10:PERF_STALL_NOT_RUN_IN_STARTUP\r\n");
#endif
    } else {
        serial_text("GXOS_NET10:PERF_STALL_UNAVAILABLE\r\n");
    }
#endif
}

__attribute__((used)) static void fault_handler(uint64_t *frame)
{
    uint64_t cr2;
    __asm__ volatile ("mov %%cr2, %0" : "=r"(cr2));
    if (g_phase < PHASE_BEFORE_MANAGED_CALL || g_phase >= PHASE_AFTER_SECURITY_COOKIE_INIT) serial_text("GXOS_NET10:FAULT_BEFORE_MANAGED\r\n");
    else if (g_phase == PHASE_IN_MANAGED || g_phase == PHASE_BEFORE_MANAGED_CALL) serial_text("GXOS_NET10:FAULT_IN_MANAGED\r\n");
    else serial_text("GXOS_NET10:FAULT_AFTER_MANAGED_RETURN\r\n");
    serial_field_u32("GXOS_NET10:FAULT_VECTOR=0x", (uint32_t)frame[0]);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:FAULT_ERROR=0x", frame[1]);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:FAULT_RIP=0x", frame[2]);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:FAULT_RSP=0x", frame[5]);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:FAULT_CR2=0x", cr2);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:FAULT_IMAGE_BASE=0x", g_managed_image_base);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:FAULT_MANAGED_TARGET=0x", g_managed_target);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:FAULT_BOOT_INFO=0x", g_boot_info_address);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:FAULT_CALLER=0x", g_last_time_caller);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:FAULT_OUTPUT=0x", g_last_time_output);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:FAULT_FIRMWARE_STATUS=0x", g_last_time_firmware_status);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:FAULT_FILETIME=0x", g_last_time_filetime);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:FAULT_TIME_CALL_COUNT=0x", g_time_call_count);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:FAULT_PERF_SOURCE=0x", g_perf_source_code);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:FAULT_PERF_SOURCE_ADDRESS=0x", g_perf_source_address);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:FAULT_PERF_FREQUENCY=0x", g_perf_frequency);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:FAULT_PERF_LAST_RAW=0x", g_perf_last_raw);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:FAULT_PERF_LAST_NORMALIZED=0x", (uint64_t)g_perf_last_normalized);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:FAULT_QPC_CALL_COUNT=0x", g_perf_qpc_call_count);
    serial_text("\r\n");
    serial_field_u32("GXOS_NET10:FAULT_PHASE=0x", g_phase);
    serial_text("\r\n");
    halt_forever();
}

__attribute__((used, naked)) static void fault_common(void)
{
    __asm__(
        "movq %rsp, %rcx\n"
        "andq $-16, %rsp\n"
        "subq $32, %rsp\n"
        "call fault_handler\n"
        "cli\n"
        "1: hlt\n"
        "jmp 1b\n");
}

#define GX_FAULT_NO_ERROR(number) \
    __attribute__((naked)) static void fault_no_error_##number(void) \
    { __asm__("pushq $0\n" "pushq $" #number "\n" "jmp fault_common\n"); }
#define GX_FAULT_WITH_ERROR(number) \
    __attribute__((naked)) static void fault_with_error_##number(void) \
    { __asm__("pushq $" #number "\n" "jmp fault_common\n"); }

GX_FAULT_NO_ERROR(0)
GX_FAULT_NO_ERROR(1)
GX_FAULT_NO_ERROR(2)
GX_FAULT_NO_ERROR(3)
GX_FAULT_NO_ERROR(4)
GX_FAULT_NO_ERROR(5)
GX_FAULT_NO_ERROR(6)
GX_FAULT_NO_ERROR(7)
GX_FAULT_WITH_ERROR(8)
GX_FAULT_NO_ERROR(9)
GX_FAULT_WITH_ERROR(10)
GX_FAULT_WITH_ERROR(11)
GX_FAULT_WITH_ERROR(12)
GX_FAULT_WITH_ERROR(13)
GX_FAULT_WITH_ERROR(14)
GX_FAULT_NO_ERROR(15)
GX_FAULT_NO_ERROR(16)
GX_FAULT_NO_ERROR(17)
GX_FAULT_NO_ERROR(18)
GX_FAULT_NO_ERROR(19)
GX_FAULT_WITH_ERROR(20)
GX_FAULT_WITH_ERROR(21)
GX_FAULT_NO_ERROR(22)
GX_FAULT_NO_ERROR(23)
GX_FAULT_NO_ERROR(24)
GX_FAULT_NO_ERROR(25)
GX_FAULT_NO_ERROR(26)
GX_FAULT_NO_ERROR(27)
GX_FAULT_NO_ERROR(28)
GX_FAULT_NO_ERROR(29)
GX_FAULT_NO_ERROR(30)
GX_FAULT_NO_ERROR(31)

typedef struct {
    uint16_t limit;
    uint64_t base;
} IDTR;

typedef struct {
    uint16_t offset_low;
    uint16_t selector;
    uint8_t ist;
    uint8_t attributes;
    uint16_t offset_middle;
    uint32_t offset_high;
    uint32_t reserved;
} IDT_GATE;

static IDTR g_saved_idtr;
static IDT_GATE g_gate4_idt[32] __attribute__((aligned(16)));

static void read_idtr(IDTR *idtr)
{
    __asm__ volatile ("sidt %0" : "=m"(*idtr));
}

static void write_idtr(const IDTR *idtr)
{
    __asm__ volatile ("lidt %0" : : "m"(*idtr));
}

static uint16_t read_cs(void)
{
    uint16_t cs;
    __asm__ volatile ("mov %%cs, %0" : "=r"(cs));
    return cs;
}

static void set_idt_gate(IDT_GATE *gate, void (*handler)(void))
{
    uint64_t address = (uint64_t)(uintptr_t)handler;
    gate->offset_low = (uint16_t)address;
    gate->selector = read_cs();
    gate->ist = 0;
    gate->attributes = 0x8E;
    gate->offset_middle = (uint16_t)(address >> 16);
    gate->offset_high = (uint32_t)(address >> 32);
    gate->reserved = 0;
}

static void install_fault_handlers(void)
{
    uint32_t i;
    read_idtr(&g_saved_idtr);
    for (i = 0; i != 32; i++) set_idt_gate(&g_gate4_idt[i], fault_no_error_0);
    set_idt_gate(&g_gate4_idt[0], fault_no_error_0);
    set_idt_gate(&g_gate4_idt[1], fault_no_error_1);
    set_idt_gate(&g_gate4_idt[2], fault_no_error_2);
    set_idt_gate(&g_gate4_idt[3], fault_no_error_3);
    set_idt_gate(&g_gate4_idt[4], fault_no_error_4);
    set_idt_gate(&g_gate4_idt[5], fault_no_error_5);
    set_idt_gate(&g_gate4_idt[6], fault_no_error_6);
    set_idt_gate(&g_gate4_idt[7], fault_no_error_7);
    set_idt_gate(&g_gate4_idt[8], fault_with_error_8);
    set_idt_gate(&g_gate4_idt[9], fault_no_error_9);
    set_idt_gate(&g_gate4_idt[10], fault_with_error_10);
    set_idt_gate(&g_gate4_idt[11], fault_with_error_11);
    set_idt_gate(&g_gate4_idt[12], fault_with_error_12);
    set_idt_gate(&g_gate4_idt[13], fault_with_error_13);
    set_idt_gate(&g_gate4_idt[14], fault_with_error_14);
    set_idt_gate(&g_gate4_idt[15], fault_no_error_15);
    set_idt_gate(&g_gate4_idt[16], fault_no_error_16);
    set_idt_gate(&g_gate4_idt[17], fault_no_error_17);
    set_idt_gate(&g_gate4_idt[18], fault_no_error_18);
    set_idt_gate(&g_gate4_idt[19], fault_no_error_19);
    set_idt_gate(&g_gate4_idt[20], fault_with_error_20);
    set_idt_gate(&g_gate4_idt[21], fault_with_error_21);
    set_idt_gate(&g_gate4_idt[22], fault_no_error_22);
    set_idt_gate(&g_gate4_idt[23], fault_no_error_23);
    set_idt_gate(&g_gate4_idt[24], fault_no_error_24);
    set_idt_gate(&g_gate4_idt[25], fault_no_error_25);
    set_idt_gate(&g_gate4_idt[26], fault_no_error_26);
    set_idt_gate(&g_gate4_idt[27], fault_no_error_27);
    set_idt_gate(&g_gate4_idt[28], fault_no_error_28);
    set_idt_gate(&g_gate4_idt[29], fault_no_error_29);
    set_idt_gate(&g_gate4_idt[30], fault_no_error_30);
    set_idt_gate(&g_gate4_idt[31], fault_no_error_31);
    {
        IDTR idtr = { (uint16_t)(sizeof(g_gate4_idt) - 1), (uint64_t)(uintptr_t)g_gate4_idt };
        write_idtr(&idtr);
    }
}

static void restore_fault_handlers(void)
{
    write_idtr(&g_saved_idtr);
}

static void zero_bytes(uint8_t *destination, uint64_t count)
{
    while (count-- != 0) *destination++ = 0;
}

static void copy_bytes(uint8_t *destination, const uint8_t *source, uint64_t count)
{
    while (count-- != 0) *destination++ = *source++;
}

static uint16_t read_u16(const uint8_t *p)
{
    return (uint16_t)p[0] | ((uint16_t)p[1] << 8);
}

static uint32_t read_u32(const uint8_t *p)
{
    return (uint32_t)read_u16(p) | ((uint32_t)read_u16(p + 2) << 16);
}

static uint64_t read_u64(const uint8_t *p)
{
    return (uint64_t)read_u32(p) | ((uint64_t)read_u32(p + 4) << 32);
}

static int equal_text(const char *left, const char *right)
{
    while (*left != 0 && *left == *right) {
        left++;
        right++;
    }
    return *left == 0 && *right == 0;
}

static int has_magic(const uint8_t *p, uint8_t a, uint8_t b, uint8_t c, uint8_t d)
{
    return p[0] == a && p[1] == b && p[2] == c && p[3] == d;
}

typedef struct {
    const uint8_t *file;
    uint64_t file_size;
    uint8_t *loaded;
    uint64_t loaded_size;
    uint64_t preferred_base;
    uint64_t actual_base;
    uint32_t size_of_headers;
    uint32_t entry_rva;
    uint32_t import_rva;
    uint32_t import_size;
    uint32_t reloc_rva;
    uint32_t reloc_size;
    uint32_t export_rva;
    uint32_t export_size;
    uint32_t managed_main_rva;
    uint32_t tls_template_rva;
    uint32_t tls_template_size;
    uint32_t tls_index_rva;
    uint32_t tls_callbacks_rva;
    uint32_t security_cookie_rva;
} PE_IMAGE;

typedef struct {
    const char *module;
    const char *symbol;
} IMPORT_RECORD;

#define MAX_IMPORT_SYMBOLS 256
static IMPORT_RECORD g_import_records[MAX_IMPORT_SYMBOLS];
static EFI_PHYSICAL_ADDRESS g_import_stub_pages;
static uint32_t g_import_symbol_count;

static void import_failfast(const IMPORT_RECORD *record)
{
    if (g_phase == PHASE_AFTER_TIME_CALL) g_phase = PHASE_IN_TIME_CONSUMER;
    if (g_phase == PHASE_AFTER_QPC_CALL) g_phase = PHASE_AFTER_SECURITY_COOKIE_INIT;
    serial_text("GXOS_NET10:UNEXPECTED_IMPORT_CALL:");
    serial_text(record->module);
    serial_text("!");
    serial_text(record->symbol);
    serial_text("\r\n");
    serial_field_u32("GXOS_NET10:TIME_CONSUMER_PHASE=0x", g_phase);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:TLS_ALLOC_LIMIT=0x", g_tls_block == 0 ? 0 : *(uint64_t *)((uint8_t *)(uintptr_t)g_tls_block + 0x30));
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:TLS_ALLOC_PTR=0x", g_tls_block == 0 ? 0 : *(uint64_t *)((uint8_t *)(uintptr_t)g_tls_block + 0x38));
    serial_text("\r\n");
    serial_text("GXOS_NET10:MANAGED_THREAD_REGISTERED=0\r\n");
    serial_text("GXOS_NET10:ALLOCATION_CONTEXT_VALID=0\r\n");
    emit_qpc_summary();
    halt_forever();
}

static void emit_import_failfast_stub(uint8_t *stub, const IMPORT_RECORD *record)
{
    uint64_t record_address = (uint64_t)(uintptr_t)record;
    uint64_t handler_address = (uint64_t)(uintptr_t)import_failfast;
    uint32_t cursor = 0;

    /* mov rcx, record; mov rax, import_failfast; jmp rax */
    stub[cursor++] = 0x48;
    stub[cursor++] = 0xB9;
    *(uint64_t *)(stub + cursor) = record_address;
    cursor += 8;
    stub[cursor++] = 0x48;
    stub[cursor++] = 0xB8;
    *(uint64_t *)(stub + cursor) = handler_address;
    cursor += 8;
    stub[cursor++] = 0xFF;
    stub[cursor++] = 0xE0;
    while (cursor < 32) stub[cursor++] = 0xCC;
}

typedef void (EFIAPI *FlsCleanupCallback)(void *value);
static uint8_t g_fls_allocated[64];
static void *g_fls_values[64];
static FlsCleanupCallback g_fls_callbacks[64];

typedef int (EFIAPI *NativeAotDllEntry)(uintptr_t module_handle, uint32_t reason, void *reserved);

static uint32_t EFIAPI platform_fls_alloc(FlsCleanupCallback callback)
{
    uint32_t index;
    for (index = 0; index != 64; index++) {
        if (!g_fls_allocated[index]) {
            g_fls_allocated[index] = 1;
            g_fls_values[index] = 0;
            g_fls_callbacks[index] = callback;
            return index;
        }
    }
    return 0xFFFFFFFFu;
}

static void *EFIAPI platform_fls_get(uint32_t index)
{
    if (index >= 64 || !g_fls_allocated[index]) return 0;
    return g_fls_values[index];
}

static int EFIAPI platform_fls_set(uint32_t index, void *value)
{
    if (index >= 64 || !g_fls_allocated[index]) return 0;
    g_fls_values[index] = value;
    return 1;
}

static int EFIAPI platform_fls_free(uint32_t index)
{
    void *value;
    if (index >= 64 || !g_fls_allocated[index]) return 0;
    value = g_fls_values[index];
    if (value != 0 && g_fls_callbacks[index] != 0) g_fls_callbacks[index](value);
    g_fls_allocated[index] = 0;
    g_fls_values[index] = 0;
    g_fls_callbacks[index] = 0;
    return 1;
}

static uint32_t g_platform_last_error;

static uint32_t EFIAPI platform_get_current_thread_id(void)
{
    return 1;
}

static uint32_t EFIAPI platform_get_current_process_id(void)
{
    return 1;
}

static void *EFIAPI platform_get_current_thread(void)
{
    return (void *)(intptr_t)-2;
}

static void *EFIAPI platform_get_current_process(void)
{
    return (void *)(intptr_t)-1;
}

static uint32_t EFIAPI platform_get_last_error(void)
{
    return g_platform_last_error;
}

static void EFIAPI platform_set_last_error(uint32_t error)
{
    g_platform_last_error = error;
}

static uint64_t g_platform_thread_handle = 1;

static int EFIAPI platform_duplicate_handle(void *source_process, void *source_handle,
                                            void *target_process, void **target_handle,
                                            uint32_t desired_access, int inherit_handle,
                                            uint32_t options)
{
    (void)desired_access;
    (void)inherit_handle;
    (void)options;
    if (source_process != (void *)(intptr_t)-1 || target_process != (void *)(intptr_t)-1 ||
        source_handle != (void *)(intptr_t)-2 || target_handle == 0) {
        g_platform_last_error = 6;
        return 0;
    }
    *target_handle = (void *)(uintptr_t)&g_platform_thread_handle;
    return 1;
}

static int EFIAPI platform_close_handle(void *handle)
{
    if (handle == (void *)(uintptr_t)&g_platform_thread_handle ||
        handle == (void *)(intptr_t)-1 || handle == (void *)(intptr_t)-2) return 1;
    g_platform_last_error = 6;
    return 0;
}

typedef struct {
    uint64_t BaseAddress;
    uint64_t AllocationBase;
    uint32_t AllocationProtect;
    uint32_t Padding0;
    uint64_t RegionSize;
    uint32_t State;
    uint32_t Protect;
    uint32_t Type;
    uint32_t Padding1;
} PlatformMemoryBasicInformation;

static uint64_t g_stack_lower;
static uint64_t g_stack_upper;

static EFI_UINTN EFIAPI platform_virtual_query(const void *address,
                                               PlatformMemoryBasicInformation *information,
                                               EFI_UINTN length)
{
    uint64_t address_value = (uint64_t)(uintptr_t)address;
    if (information == 0 || length < sizeof(PlatformMemoryBasicInformation) ||
        address_value < g_stack_lower || address_value >= g_stack_upper) return 0;
    zero_bytes((uint8_t *)information, sizeof(*information));
    information->BaseAddress = g_stack_lower;
    information->AllocationBase = g_stack_lower;
    information->AllocationProtect = 0x04;
    information->RegionSize = g_stack_upper - g_stack_lower;
    information->State = 0x1000;
    information->Protect = 0x04;
    information->Type = 0x20000;
    return sizeof(*information);
}

typedef struct {
    void *DebugInfo;
    int32_t LockCount;
    int32_t RecursionCount;
    void *OwningThread;
    void *LockSemaphore;
    uintptr_t SpinCount;
} PlatformCriticalSection;

static int EFIAPI platform_initialize_critical_section_ex(PlatformCriticalSection *section,
                                                           uint32_t spin_count,
                                                           uint32_t flags)
{
    (void)flags;
    if (section == 0) return 0;
    section->DebugInfo = 0;
    section->LockCount = -1;
    section->RecursionCount = 0;
    section->OwningThread = 0;
    section->LockSemaphore = 0;
    section->SpinCount = spin_count;
    return 1;
}

static void EFIAPI platform_initialize_critical_section(PlatformCriticalSection *section)
{
    if (!platform_initialize_critical_section_ex(section, 0, 0)) fail("critical-section-init");
}

static void EFIAPI platform_enter_critical_section(PlatformCriticalSection *section)
{
    void *current_thread = (void *)(intptr_t)-2;
    if (section == 0) fail("critical-section-null");
    if (section->OwningThread == 0) {
        section->OwningThread = current_thread;
        section->RecursionCount = 1;
        section->LockCount = 0;
        return;
    }
    if (section->OwningThread == current_thread) {
        section->RecursionCount++;
        return;
    }
    fail("critical-section-contention");
}

static void EFIAPI platform_leave_critical_section(PlatformCriticalSection *section)
{
    if (section == 0 || section->OwningThread != (void *)(intptr_t)-2 || section->RecursionCount <= 0) {
        fail("critical-section-leave");
    }
    section->RecursionCount--;
    if (section->RecursionCount == 0) {
        section->OwningThread = 0;
        section->LockCount = -1;
    }
}

static void EFIAPI platform_delete_critical_section(PlatformCriticalSection *section)
{
    if (section == 0) fail("critical-section-delete");
    if (section->OwningThread != 0 || section->RecursionCount != 0) fail("critical-section-delete-owned");
    zero_bytes((uint8_t *)section, sizeof(*section));
}

#ifdef GXOS_ENABLE_CRT_ONEXIT
static uint64_t g_crt_onexit_initialize_calls;

static int GXOS_CRT_EFIAPI platform_initialize_onexit_table(GXOS_CRT_ONEXIT_TABLE *table)
{
    int result;
    g_crt_onexit_initialize_calls++;
    serial_field_hex("GXOS_NET10:CRT_ONEXIT_INIT_CALL=0x", g_crt_onexit_initialize_calls);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CRT_ONEXIT_TABLE=0x", (uint64_t)(uintptr_t)table);
    serial_text("\r\n");
    result = gxos_crt_initialize_onexit_table(table);
    serial_field_hex("GXOS_NET10:CRT_ONEXIT_INIT_RETURN=0x", (uint64_t)(uint32_t)result);
    serial_text("\r\n");
    if (result == 0 && table != 0 && table->first == table->last && table->last == table->end) {
#ifdef GXOS_CRT_ONEXIT_MARKER_MUTATION
        serial_text("GXOS_NET10:CRT_ONEXIT_INITIALIZED_OX\r\n");
#else
        serial_text("GXOS_NET10:CRT_ONEXIT_INITIALIZED_OK\r\n");
#endif
    }
    return result;
}
#endif

static void *platform_import_target(const char *module, const char *symbol)
{
#ifndef GXOS_DISABLE_TIME_IMPLEMENTATION
    if (equal_text(module, "KERNEL32.dll") && equal_text(symbol, "GetSystemTimeAsFileTime")) return (void *)(uintptr_t)gxos_get_system_time_as_file_time;
#endif
#ifndef GXOS_DISABLE_PERF_IMPLEMENTATION
    if (equal_text(module, "KERNEL32.dll") && equal_text(symbol, "QueryPerformanceCounter")) return (void *)(uintptr_t)gxos_query_performance_counter;
    if (equal_text(module, "KERNEL32.dll") && equal_text(symbol, "QueryPerformanceFrequency")) return (void *)(uintptr_t)gxos_query_performance_frequency;
#endif
    if (equal_text(module, "KERNEL32.dll") && equal_text(symbol, "FlsAlloc")) return (void *)(uintptr_t)platform_fls_alloc;
    if (equal_text(module, "KERNEL32.dll") && equal_text(symbol, "FlsGetValue")) return (void *)(uintptr_t)platform_fls_get;
    if (equal_text(module, "KERNEL32.dll") && equal_text(symbol, "FlsSetValue")) return (void *)(uintptr_t)platform_fls_set;
    if (equal_text(module, "KERNEL32.dll") && equal_text(symbol, "FlsFree")) return (void *)(uintptr_t)platform_fls_free;
    if (equal_text(module, "KERNEL32.dll") && equal_text(symbol, "GetCurrentThreadId")) return (void *)(uintptr_t)platform_get_current_thread_id;
    if (equal_text(module, "KERNEL32.dll") && equal_text(symbol, "GetCurrentProcessId")) return (void *)(uintptr_t)platform_get_current_process_id;
    if (equal_text(module, "KERNEL32.dll") && equal_text(symbol, "GetCurrentThread")) return (void *)(uintptr_t)platform_get_current_thread;
    if (equal_text(module, "KERNEL32.dll") && equal_text(symbol, "GetCurrentProcess")) return (void *)(uintptr_t)platform_get_current_process;
    if (equal_text(module, "KERNEL32.dll") && equal_text(symbol, "GetLastError")) return (void *)(uintptr_t)platform_get_last_error;
    if (equal_text(module, "KERNEL32.dll") && equal_text(symbol, "SetLastError")) return (void *)(uintptr_t)platform_set_last_error;
    if (equal_text(module, "KERNEL32.dll") && equal_text(symbol, "DuplicateHandle")) return (void *)(uintptr_t)platform_duplicate_handle;
    if (equal_text(module, "KERNEL32.dll") && equal_text(symbol, "CloseHandle")) return (void *)(uintptr_t)platform_close_handle;
    if (equal_text(module, "KERNEL32.dll") && equal_text(symbol, "VirtualQuery")) return (void *)(uintptr_t)platform_virtual_query;
    if (equal_text(module, "KERNEL32.dll") && equal_text(symbol, "InitializeCriticalSectionEx")) return (void *)(uintptr_t)platform_initialize_critical_section_ex;
    if (equal_text(module, "KERNEL32.dll") && equal_text(symbol, "InitializeCriticalSection")) return (void *)(uintptr_t)platform_initialize_critical_section;
    if (equal_text(module, "KERNEL32.dll") && equal_text(symbol, "EnterCriticalSection")) return (void *)(uintptr_t)platform_enter_critical_section;
    if (equal_text(module, "KERNEL32.dll") && equal_text(symbol, "LeaveCriticalSection")) return (void *)(uintptr_t)platform_leave_critical_section;
    if (equal_text(module, "KERNEL32.dll") && equal_text(symbol, "DeleteCriticalSection")) return (void *)(uintptr_t)platform_delete_critical_section;
#ifdef GXOS_ENABLE_CRT_ONEXIT
    if (equal_text(module, "api-ms-win-crt-runtime-l1-1-0.dll") &&
        equal_text(symbol, "_initialize_onexit_table")) return (void *)(uintptr_t)platform_initialize_onexit_table;
#endif
    return 0;
}

static const uint8_t *rva_to_file(const PE_IMAGE *image, uint32_t rva, uint32_t size)
{
    const uint8_t *nt;
    uint16_t section_count;
    uint16_t optional_size;
    const uint8_t *section;
    uint16_t i;

    if ((uint64_t)rva + size <= image->size_of_headers) {
        if ((uint64_t)rva + size <= image->file_size) return image->file + rva;
        return 0;
    }
    nt = image->file + read_u32(image->file + 0x3C);
    section_count = read_u16(nt + 6);
    optional_size = read_u16(nt + 20);
    section = nt + 24 + optional_size;
    for (i = 0; i < section_count; i++, section += 40) {
        uint32_t virtual_size = read_u32(section + 8);
        uint32_t virtual_address = read_u32(section + 12);
        uint32_t raw_size = read_u32(section + 16);
        uint32_t raw_offset = read_u32(section + 20);
        uint32_t extent = virtual_size > raw_size ? virtual_size : raw_size;
        if (rva >= virtual_address && (uint64_t)rva + size <= (uint64_t)virtual_address + raw_size) {
            uint64_t offset = (uint64_t)raw_offset + (rva - virtual_address);
            if (offset + size <= image->file_size) return image->file + offset;
        }
        if (extent == 0) continue;
    }
    return 0;
}

static uint8_t *rva_to_loaded(const PE_IMAGE *image, uint32_t rva, uint32_t size)
{
    if ((uint64_t)rva + size > image->loaded_size) return 0;
    return image->loaded + rva;
}

static void apply_relocations(PE_IMAGE *image)
{
    uint64_t delta = image->actual_base - image->preferred_base;
    uint32_t cursor = 0;

    if (delta == 0) return;
    if (image->reloc_rva == 0 || image->reloc_size < 8) fail("relocations-required");
    while (cursor + 8 <= image->reloc_size) {
        const uint8_t *block = rva_to_file(image, image->reloc_rva + cursor, 8);
        uint32_t page_rva;
        uint32_t block_size;
        uint32_t entry_count;
        uint32_t i;
        if (!block) fail("relocation-bounds");
        page_rva = read_u32(block);
        block_size = read_u32(block + 4);
        if (block_size < 8 || cursor + block_size > image->reloc_size) fail("relocation-block");
        entry_count = (block_size - 8) / 2;
        for (i = 0; i < entry_count; i++) {
            uint16_t entry = read_u16(rva_to_file(image, image->reloc_rva + cursor + 8 + i * 2, 2));
            uint16_t type = entry >> 12;
            uint16_t offset = entry & 0x0FFF;
            if (type == 10) {
                uint64_t *target = (uint64_t *)rva_to_loaded(image, page_rva + offset, 8);
                if (!target) fail("relocation-target");
                *target += delta;
            } else if (type != 0) {
                fail("relocation-type");
            }
        }
        cursor += block_size;
    }
}

static void resolve_imports(PE_IMAGE *image, EFI_BOOT_SERVICES *boot_services,
                            uint32_t *descriptor_count, uint32_t *symbol_count,
                            uint32_t *functional_count, uint32_t *failfast_count,
                            uint32_t *unresolved_count)
{
    uint32_t descriptors = 0;
    uint32_t symbols = 0;
    uint32_t functional = 0;
    uint32_t failfast = 0;
    uint32_t unresolved = 0;
    uint32_t cursor = 0;
    uint8_t *stub_page;

    if (image->import_rva == 0 || image->import_size < 20) {
        *descriptor_count = 0;
        *symbol_count = 0;
        *functional_count = 0;
        *failfast_count = 0;
        *unresolved_count = 0;
        return;
    }
    if (EFI_ERROR(boot_services->AllocatePages(EFI_ALLOCATE_ANY_PAGES, EFI_LOADER_CODE, 1, &g_import_stub_pages))) {
        fail("allocate-import-stubs");
    }
    stub_page = (uint8_t *)(uintptr_t)g_import_stub_pages;
    zero_bytes(stub_page, EFI_PAGE_SIZE);

    while (cursor + 20 <= image->import_size) {
        const uint8_t *descriptor = rva_to_file(image, image->import_rva + cursor, 20);
        uint32_t lookup_rva;
        uint32_t name_rva;
        uint32_t first_thunk_rva;
        uint32_t index = 0;
        const char *module;
        if (!descriptor) fail("import-bounds");
        if (read_u32(descriptor) == 0 && read_u32(descriptor + 4) == 0 &&
            read_u32(descriptor + 8) == 0 && read_u32(descriptor + 12) == 0 &&
            read_u32(descriptor + 16) == 0) break;
        lookup_rva = read_u32(descriptor);
        name_rva = read_u32(descriptor + 12);
        first_thunk_rva = read_u32(descriptor + 16);
        if (lookup_rva == 0) lookup_rva = first_thunk_rva;
        module = (const char *)rva_to_file(image, name_rva, 1);
        if (!module || lookup_rva == 0 || first_thunk_rva == 0) fail("import-descriptor");
        descriptors++;
        while (1) {
            const uint8_t *lookup = rva_to_file(image, lookup_rva + index * 8, 8);
            uint64_t lookup_value;
            const uint8_t *hint_name;
            uint64_t *iat;
            if (!lookup) fail("import-lookup-bounds");
            lookup_value = read_u64(lookup);
            if (lookup_value == 0) break;
            if ((lookup_value & 0x8000000000000000ULL) != 0) {
                serial_text("GXOS_NET10:UNSUPPORTED_IMPORT_ORDINAL\r\n");
                fail("import-ordinal");
            }
            if (lookup_value > 0xFFFFFFFFULL) fail("import-name-rva");
            hint_name = rva_to_file(image, (uint32_t)lookup_value, 2);
            iat = (uint64_t *)rva_to_loaded(image, first_thunk_rva + index * 8, 8);
            if (!hint_name || !iat) fail("import-iat-bounds");
            if (symbols >= MAX_IMPORT_SYMBOLS) fail("import-symbol-capacity");
            g_import_records[symbols].module = module;
            g_import_records[symbols].symbol = (const char *)(hint_name + 2);
            {
                void *target = platform_import_target(module, g_import_records[symbols].symbol);
#ifdef GXOS_NEGATIVE_UNRESOLVED_IMPORT
                if (symbols == 0) {
                    *iat = 0;
                    unresolved++;
                    symbols++;
                    index++;
                    continue;
                }
#endif
                if (target != 0) {
                    *iat = (uint64_t)(uintptr_t)target;
                    functional++;
                }
                else {
                    emit_import_failfast_stub(stub_page + symbols * 32, &g_import_records[symbols]);
                    *iat = (uint64_t)(uintptr_t)(stub_page + symbols * 32);
                    failfast++;
                }
            }
            symbols++;
            index++;
        }
        cursor += 20;
    }
    g_import_symbol_count = symbols;
    *descriptor_count = descriptors;
    *symbol_count = symbols;
    *functional_count = functional;
    *failfast_count = failfast;
    *unresolved_count = unresolved;
}

static uint64_t read_msr(uint32_t number)
{
    uint32_t low;
    uint32_t high;
    __asm__ volatile ("rdmsr" : "=a"(low), "=d"(high) : "c"(number));
    return ((uint64_t)high << 32) | low;
}

static void write_msr(uint32_t number, uint64_t value)
{
    uint32_t low = (uint32_t)value;
    uint32_t high = (uint32_t)(value >> 32);
    __asm__ volatile ("wrmsr" : : "c"(number), "a"(low), "d"(high));
}

static uint64_t g_saved_gs_base;
static uint64_t g_saved_flags;
static EFI_PHYSICAL_ADDRESS g_gs_area;
static EFI_PHYSICAL_ADDRESS g_tls_vector;
static EFI_PHYSICAL_ADDRESS g_teb_area;

static void initialize_nativeaot_tls(const PE_IMAGE *image, EFI_BOOT_SERVICES *boot_services)
{
    uint32_t tls_index;
    uint64_t *vector;
    uint8_t *template_start;
    uint8_t *tls_block;
    uint8_t *gs_area;
    uint8_t *teb_area;
    uint64_t flags;
    uint64_t vector_pages = 1;
    uint64_t rsp;

    if (image->tls_template_size == 0 || image->tls_index_rva == 0) fail("tls-directory-missing");
    tls_index = read_u32(rva_to_loaded(image, image->tls_index_rva, 4));
    if (tls_index >= 512) fail("tls-index-too-large");
    if (EFI_ERROR(boot_services->AllocatePages(EFI_ALLOCATE_ANY_PAGES, EFI_LOADER_DATA, vector_pages, &g_tls_vector))) fail("allocate-tls-vector");
    if (EFI_ERROR(boot_services->AllocatePages(EFI_ALLOCATE_ANY_PAGES, EFI_LOADER_DATA, 1, &g_tls_block))) fail("allocate-tls-block");
    if (EFI_ERROR(boot_services->AllocatePages(EFI_ALLOCATE_ANY_PAGES, EFI_LOADER_DATA, 1, &g_gs_area))) fail("allocate-gs-area");
    if (EFI_ERROR(boot_services->AllocatePages(EFI_ALLOCATE_ANY_PAGES, EFI_LOADER_DATA, 1, &g_teb_area))) fail("allocate-teb-area");
    vector = (uint64_t *)(uintptr_t)g_tls_vector;
    zero_bytes((uint8_t *)vector, EFI_PAGE_SIZE);
    tls_block = (uint8_t *)(uintptr_t)g_tls_block;
    zero_bytes(tls_block, EFI_PAGE_SIZE);
    template_start = rva_to_loaded(image, image->tls_template_rva, image->tls_template_size);
    if (!template_start) fail("tls-template-bounds");
    copy_bytes(tls_block, template_start, image->tls_template_size);
    vector[tls_index] = (uint64_t)(uintptr_t)tls_block;
    gs_area = (uint8_t *)(uintptr_t)g_gs_area;
    zero_bytes(gs_area, EFI_PAGE_SIZE);
    __asm__ volatile ("mov %%rsp, %0" : "=r"(rsp));
    g_stack_lower = rsp & ~((uint64_t)EFI_PAGE_SIZE - 1);
    g_stack_upper = g_stack_lower + 0x100000;
    teb_area = (uint8_t *)(uintptr_t)g_teb_area;
    zero_bytes(teb_area, EFI_PAGE_SIZE);
    *(uint64_t *)(teb_area + 0x08) = g_stack_upper;
    *(uint64_t *)(teb_area + 0x10) = g_stack_lower;
    *(uint64_t *)(gs_area + 0x30) = (uint64_t)(uintptr_t)teb_area;
    *(uint64_t *)(gs_area + 0x58) = (uint64_t)(uintptr_t)vector;
    flags = 0;
    __asm__ volatile ("pushfq\n\tpopq %0" : "=r"(flags));
    g_saved_flags = flags;
    g_saved_gs_base = read_msr(0xC0000101);
    __asm__ volatile ("cli");
    write_msr(0xC0000101, (uint64_t)(uintptr_t)gs_area);
}

static void restore_nativeaot_tls(void)
{
    write_msr(0xC0000101, g_saved_gs_base);
    if ((g_saved_flags & 0x200) != 0) __asm__ volatile ("sti");
}

static void find_managed_main(PE_IMAGE *image)
{
    const uint8_t *exports = rva_to_file(image, image->export_rva, image->export_size);
    uint32_t name_count;
    uint32_t names_rva;
    uint32_t ordinals_rva;
    uint32_t functions_rva;
    uint32_t i;
    if (!exports || image->export_size < 40) fail("export-bounds");
    name_count = read_u32(exports + 24);
    functions_rva = read_u32(exports + 28);
    names_rva = read_u32(exports + 32);
    ordinals_rva = read_u32(exports + 36);
    for (i = 0; i < name_count; i++) {
        const uint8_t *name_rva_ptr = rva_to_file(image, names_rva + i * 4, 4);
        const char *name;
        uint16_t ordinal;
        const uint8_t *function_rva_ptr;
        if (!name_rva_ptr) fail("export-name-bounds");
        name = (const char *)rva_to_file(image, read_u32(name_rva_ptr), 1);
        ordinal = read_u16(rva_to_file(image, ordinals_rva + i * 2, 2));
        function_rva_ptr = rva_to_file(image, functions_rva + (uint32_t)ordinal * 4, 4);
        if (!name || !function_rva_ptr) fail("export-function-bounds");
        if (equal_text(name, "ManagedMain")) {
            image->managed_main_rva = read_u32(function_rva_ptr);
            return;
        }
    }
    fail("ManagedMain-export-missing");
}

static void load_pe_image(PE_IMAGE *image, EFI_BOOT_SERVICES *boot_services)
{
    const uint8_t *nt;
    const uint8_t *optional;
    const uint8_t *section;
    uint16_t section_count;
    uint16_t optional_size;
    uint32_t size_of_image;
    uint32_t raw_size;
    uint32_t raw_offset;
    uint32_t virtual_address;
    uint16_t i;
    uint64_t pages;
    EFI_PHYSICAL_ADDRESS physical_base = 0;
    uint32_t tls_rva;

    if (image->file_size < 0x40 || read_u16(image->file) != 0x5A4D) fail("dos-header");
    if ((uint64_t)read_u32(image->file + 0x3C) + 24 > image->file_size) fail("nt-header-bounds");
    nt = image->file + read_u32(image->file + 0x3C);
    if (!has_magic(nt, 'P', 'E', 0, 0)) fail("pe-signature");
    section_count = read_u16(nt + 6);
    optional_size = read_u16(nt + 20);
    if (read_u16(nt + 24) != 0x20B || optional_size < 0xF0) fail("pe32-plus");
    optional = nt + 24;
    size_of_image = read_u32(optional + 0x38);
    image->size_of_headers = read_u32(optional + 0x3C);
    image->entry_rva = read_u32(optional + 0x10);
    image->preferred_base = read_u64(optional + 0x18);
    image->loaded_size = size_of_image;
    image->import_rva = read_u32(optional + 0x70 + 8);
    image->import_size = read_u32(optional + 0x70 + 12);
    image->export_rva = read_u32(optional + 0x70);
    image->export_size = read_u32(optional + 0x74);
    image->reloc_rva = read_u32(optional + 0x70 + 5 * 8);
    image->reloc_size = read_u32(optional + 0x70 + 5 * 8 + 4);
    tls_rva = read_u32(optional + 0x70 + 9 * 8);
    {
        uint32_t load_config_rva = read_u32(optional + 0x70 + 10 * 8);
        uint32_t load_config_size = read_u32(optional + 0x70 + 10 * 8 + 4);
        if (load_config_rva != 0 && load_config_size >= 0x60) {
            const uint8_t *load_config = rva_to_file(image, load_config_rva, 0x60);
            uint64_t security_cookie;
            if (!load_config) fail("load-config-bounds");
            security_cookie = read_u64(load_config + 0x58);
            if (security_cookie != 0) {
                if (security_cookie < image->preferred_base ||
                    security_cookie - image->preferred_base > 0xFFFFFFFFULL) {
                    fail("security-cookie-address");
                }
                image->security_cookie_rva = (uint32_t)(security_cookie - image->preferred_base);
            }
        }
    }
    if (size_of_image == 0 || image->size_of_headers > image->file_size) fail("image-size");

    pages = ((uint64_t)size_of_image + EFI_PAGE_SIZE - 1) / EFI_PAGE_SIZE;
    if (EFI_ERROR(boot_services->AllocatePages(EFI_ALLOCATE_ANY_PAGES, EFI_LOADER_DATA, pages, &physical_base))) fail("allocate-image");
    image->loaded = (uint8_t *)(uint64_t)physical_base;
    image->actual_base = physical_base;
    zero_bytes(image->loaded, size_of_image);
    copy_bytes(image->loaded, image->file, image->size_of_headers);

    section = nt + 24 + optional_size;
    for (i = 0; i < section_count; i++, section += 40) {
        raw_size = read_u32(section + 16);
        raw_offset = read_u32(section + 20);
        virtual_address = read_u32(section + 12);
        if (raw_size == 0) continue;
        if ((uint64_t)raw_offset + raw_size > image->file_size || (uint64_t)virtual_address + raw_size > size_of_image) fail("section-bounds");
        copy_bytes(image->loaded + virtual_address, image->file + raw_offset, raw_size);
    }
    apply_relocations(image);
    find_managed_main(image);
    if (tls_rva != 0) {
        const uint8_t *tls = rva_to_file(image, tls_rva, 40);
        uint64_t tls_start;
        uint64_t tls_end;
        uint64_t tls_index;
        uint64_t tls_callbacks;
        if (!tls) fail("tls-directory-bounds");
        tls_start = read_u64(tls);
        tls_end = read_u64(tls + 8);
        tls_index = read_u64(tls + 16);
        tls_callbacks = read_u64(tls + 24);
        if (tls_start < image->preferred_base || tls_end < tls_start ||
            tls_end - tls_start > 0x100000 || tls_index < image->preferred_base ||
            tls_index - image->preferred_base > 0xFFFFFFFFULL ||
            tls_start - image->preferred_base > 0xFFFFFFFFULL) {
            fail("tls-directory-values");
        }
        image->tls_template_rva = (uint32_t)(tls_start - image->preferred_base);
        image->tls_template_size = (uint32_t)(tls_end - tls_start);
        image->tls_index_rva = (uint32_t)(tls_index - image->preferred_base);
        if (tls_callbacks != 0) {
            if (tls_callbacks < image->preferred_base ||
                tls_callbacks - image->preferred_base > 0xFFFFFFFFULL) fail("tls-callbacks-address");
            image->tls_callbacks_rva = (uint32_t)(tls_callbacks - image->preferred_base);
        }
    }
}

static int EFIAPI call_managed_entry(ManagedMainEntry entry, uintptr_t argument, uint64_t *rsp_before_call)
{
    uint64_t rsp;
    uint32_t mxcsr = 0x1F80;
    uint16_t x87_control = 0x037F;
    __asm__ volatile (
        "cld\n"
        "ldmxcsr %0\n"
        "fldcw %1\n"
        :
        : "m"(mxcsr), "m"(x87_control));
    __asm__ volatile ("mov %%rsp, %0" : "=r"(rsp));
    *rsp_before_call = rsp;
    return entry(argument);
}

static const uint16_t gPayloadPath[] = {
    '\\', 'G', 'X', 'O', 'S', '\\', 'g', 'x', 'o', 's', '-', 'm', 'a', 'n', 'a', 'g', 'e', 'd', '-', 'e', 'n', 't', 'r', 'y', '-', 'p', 'r', 'o', 'b', 'e', '.', 'd', 'l', 'l', 0
};

static void read_payload(EFI_HANDLE image_handle, EFI_SYSTEM_TABLE *system_table, PE_IMAGE *image)
{
    EFI_LOADED_IMAGE_PROTOCOL *loaded_image = 0;
    EFI_SIMPLE_FILE_SYSTEM_PROTOCOL *file_system = 0;
    EFI_FILE_PROTOCOL *root = 0;
    EFI_FILE_PROTOCOL *file = 0;
    uint8_t *buffer = 0;
    EFI_UINTN buffer_size = 8 * 1024 * 1024;
    EFI_STATUS status;

    status = system_table->BootServices->HandleProtocol(image_handle, (EFI_GUID *)&gLoadedImageProtocol, (void **)&loaded_image);
    if (EFI_ERROR(status) || !loaded_image) fail("loaded-image-protocol");
    status = system_table->BootServices->HandleProtocol(loaded_image->DeviceHandle, (EFI_GUID *)&gSimpleFileSystemProtocol, (void **)&file_system);
    if (EFI_ERROR(status) || !file_system) fail("simple-file-system");
    status = file_system->OpenVolume(file_system, &root);
    if (EFI_ERROR(status) || !root) fail("open-volume");
    status = root->Open(root, &file, (uint16_t *)gPayloadPath, EFI_OPEN_MODE_READ, 0);
    if (EFI_ERROR(status) || !file) fail("open-payload");
    status = system_table->BootServices->AllocatePool(EFI_LOADER_DATA, buffer_size, (void **)&buffer);
    if (EFI_ERROR(status) || !buffer) fail("allocate-payload");
    status = file->Read(file, &buffer_size, buffer);
    file->Close(file);
    if (EFI_ERROR(status) || buffer_size == 0) fail("read-payload");
    image->file = buffer;
    image->file_size = buffer_size;
}

EFI_STATUS EFIAPI efi_main(EFI_HANDLE image_handle, EFI_SYSTEM_TABLE *system_table)
{
    PE_IMAGE image = {0};
    EFI_BOOT_SERVICES *boot_services;
    uint32_t import_descriptors;
    uint32_t import_symbols;
    uint32_t import_functional;
    uint32_t import_failfast;
    uint32_t unresolved_imports;
    uint32_t managed_result;
    uint64_t rsp_before_call;
    ManagedMainEntry managed_entry;

    serial_init();
    serial_text("GXOS_NET10:LOADER_START\r\n");
    g_phase = PHASE_LOADER;
    boot_services = system_table->BootServices;
    configure_platform_time(system_table->RuntimeServices);
#ifdef GXOS_ENABLE_NATIVEAOT_STARTUP
    configure_platform_performance(system_table);
#endif
    read_payload(image_handle, system_table, &image);
    serial_text("GXOS_NET10:PE_READ_OK\r\n");
    load_pe_image(&image, boot_services);
    serial_text("GXOS_NET10:PE_RELOCATIONS_OK\r\n");
#ifdef GXOS_ENABLE_CRT_ONEXIT
    if (image.security_cookie_rva == 0) fail("security-cookie-missing");
    gxos_crt_onexit_set_encoded_null_address(
        (const uintptr_t *)rva_to_loaded(&image, image.security_cookie_rva, sizeof(uintptr_t)));
    if (gxos_crt_onexit_get_encoded_null() == 0) fail("security-cookie-uninitialized");
    serial_text("GXOS_NET10:CRT_ONEXIT_ENCODED_NULL_SOURCE=SECURITY_COOKIE\r\n");
#endif
    serial_text("GXOS_NET10:MANAGED_EXPORT_RVA=0x");
    serial_hex64(image.managed_main_rva);
    serial_text("\r\n");
    g_managed_image_base = image.actual_base;
    g_managed_target = image.actual_base + image.managed_main_rva;
    serial_field_hex("GXOS_NET10:IMAGE_BASE=0x", image.actual_base);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:MANAGED_TARGET_VA=0x", g_managed_target);
    serial_text("\r\n");
    resolve_imports(&image, boot_services, &import_descriptors, &import_symbols,
                    &import_functional, &import_failfast, &unresolved_imports);
    serial_text("GXOS_NET10:PE_IMPORT_DESCRIPTORS=");
    serial_u32(import_descriptors);
    serial_text("\r\n");
    serial_text("GXOS_NET10:PE_IMPORT_SYMBOLS=");
    serial_u32(import_symbols);
    serial_text("\r\n");
    serial_text("GXOS_NET10:PE_IMPORT_RESOLVED=");
    serial_u32(import_symbols);
    serial_text("\r\n");
    serial_text("GXOS_NET10:PE_IMPORT_FUNCTIONAL=");
    serial_u32(import_functional);
    serial_text("\r\n");
    serial_text("GXOS_NET10:PE_IMPORT_FAILFAST=");
    serial_u32(import_failfast);
    serial_text("\r\n");
    serial_text("GXOS_NET10:UNRESOLVED_REQUIRED_IMPORTS=");
    serial_u32(unresolved_imports);
    serial_text("\r\n");
    if (unresolved_imports != 0) fail("negative-unresolved-import");
#ifdef GXOS_NEGATIVE_INVOKE_FAILFAST
    serial_text("GXOS_NET10:NEGATIVE_INVOKE_FAILFAST\r\n");
    g_phase = PHASE_NEGATIVE;
    ((void (EFIAPI *)(void))(uintptr_t)g_import_stub_pages)();
#endif
    install_fault_handlers();
    initialize_nativeaot_tls(&image, boot_services);
#ifdef GXOS_ENABLE_NATIVEAOT_STARTUP
    if (image.entry_rva == 0 || image.entry_rva >= image.loaded_size) fail("nativeaot-entrypoint-missing");
    g_phase = PHASE_BEFORE_TIME_CALL;
    serial_text("GXOS_NET10:TIME_SOURCE=");
#ifdef GXOS_ASSUME_UNSPECIFIED_TIMEZONE_UTC
    serial_text("UEFI_GETTIME_QEMU_RTC_UTC_POLICY\r\n");
#else
    serial_text("UEFI_GETTIME_STRICT_TIMEZONE\r\n");
#endif
    serial_text("GXOS_NET10:GC_STARTUP_BEGIN\r\n");
    serial_text("GXOS_NET10:NATIVEAOT_STARTUP_BEGIN\r\n");
#ifdef GXOS_PERF_STALL_ONLY
    run_performance_diagnostics(boot_services);
    serial_text("GXOS_NET10:PERF_STALL_PROBE_COMPLETE\r\n");
    halt_forever();
#endif
    {
        NativeAotDllEntry nativeaot_entry = (NativeAotDllEntry)(uintptr_t)(image.actual_base + image.entry_rva);
        int nativeaot_result = nativeaot_entry((uintptr_t)image.actual_base, 1, 0);
        serial_field_hex("GXOS_NET10:NATIVEAOT_STARTUP_RETURN=0x", (uint64_t)(uint32_t)nativeaot_result);
        serial_text("\r\n");
        if (nativeaot_result == 0) fail("nativeaot-startup-failed");
    }
    serial_text("GXOS_NET10:NATIVEAOT_STARTUP_OK\r\n");
    g_phase = PHASE_AFTER_SECURITY_COOKIE_INIT;
    emit_performance_diagnostics();
    g_phase = PHASE_AFTER_TIME_CONSUMER;
    serial_text("GXOS_NET10:GC_STARTUP_ADVANCED\r\n");
    serial_field_hex("GXOS_NET10:TLS_ALLOC_LIMIT=0x", *(uint64_t *)((uint8_t *)(uintptr_t)g_tls_block + 0x30));
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:TLS_ALLOC_PTR=0x", *(uint64_t *)((uint8_t *)(uintptr_t)g_tls_block + 0x38));
    serial_text("\r\n");
#endif
    g_boot_info_address = (uint64_t)(uintptr_t)&g_boot_info;
    g_boot_info.Magic = GUIDEX_BOOT_MAGIC;
    g_boot_info.Version = GUIDEX_BOOT_VERSION;
    g_boot_info.Size = GUIDEX_BOOT_SIZE;
    g_boot_info.Architecture = GUIDEX_BOOT_ARCH_X64;
    g_boot_info.Flags = 0;
    g_boot_info.SerialWrite = (uint64_t)(uintptr_t)serial_write;
#ifdef GXOS_NEGATIVE_INVALID_BOOT_INFO
    g_boot_info.Version = 2;
#endif
#ifdef GXOS_NEGATIVE_NULL_SERIAL
    g_boot_info.SerialWrite = 0;
#endif
    managed_entry = (ManagedMainEntry)(uintptr_t)g_managed_target;
    serial_field_hex("GXOS_NET10:BOOT_INFO_PTR=0x", (uint64_t)(uintptr_t)&g_boot_info);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CALL_TARGET_VA=0x", g_managed_target);
    serial_text("\r\n");
    serial_text("GXOS_NET10:CPU_DF=0\r\n");
    serial_field_hex("GXOS_NET10:CPU_MXCSR=0x", 0x1F80);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CPU_X87_CONTROL=0x", 0x037F);
    serial_text("\r\n");
    serial_text("GXOS_NET10:BEFORE_MANAGED_CALL\r\n");
    g_phase = PHASE_IN_MANAGED;
    managed_result = (uint32_t)call_managed_entry(managed_entry, (uintptr_t)&g_boot_info, &rsp_before_call);
    g_phase = PHASE_AFTER_MANAGED_RETURN;
    restore_nativeaot_tls();
    restore_fault_handlers();
    serial_field_hex("GXOS_NET10:STACK_RSP_BEFORE_CALL=0x", rsp_before_call);
    serial_text("\r\n");
    serial_text("GXOS_NET10:STACK_RSP_MOD16=");
    serial_u32((uint32_t)(rsp_before_call & 0xFULL));
    serial_text("\r\n");
    serial_text("GXOS_NET10:AFTER_MANAGED_RETURN=0x");
    serial_hex64(managed_result);
    serial_text("\r\n");
    if (managed_result != 0) fail("managed-return-nonzero");
    g_phase = PHASE_COMPLETE;
    serial_text("GXOS_NET10:MANAGED_ENTRY_COMPLETE\r\n");
    halt_forever();
    return EFI_SUCCESS;
}

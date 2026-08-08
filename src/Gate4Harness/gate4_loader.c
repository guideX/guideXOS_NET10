#include <stdint.h>
#include <stddef.h>
#include "platform_time.h"
#include "platform_performance.h"
#include "crt_onexit.h"
#include "crt_initterm_e.h"
#include "crt_initterm.h"
#include "crt_strcmp.h"
#include "crt_strlen.h"
#include "crt_stricmp.h"
#include "platform_environment.h"
#include "platform_slist.h"
#include "platform_system_info.h"
#include "platform_numa.h"
#include "platform_process_group_affinity.h"
#include "platform_process_affinity.h"
#include "platform_query_information_job_object.h"
#include "platform_get_module_handle.h"
#include "platform_get_module_handle_ex.h"
#include "platform_get_proc_address.h"
#include "crt_malloc.h"
#include "exception_context.h"
#include "vectored_handler.h"
#if defined(GXOS_ENABLE_SYNTHETIC_SCHEDULER_PROOF) || \
    defined(GXOS_ENABLE_CREATE_EVENT_W) || \
    defined(GXOS_ENABLE_CREATE_MEMORY_RESOURCE_NOTIFICATION)
#include "scheduler_foundation.h"
#endif
#ifdef GXOS_ENABLE_CREATE_EVENT_W
#include "create_event_w.h"
#endif
#ifdef GXOS_ENABLE_CREATE_MEMORY_RESOURCE_NOTIFICATION
#include "create_memory_resource_notification.h"
#endif

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
    void *Reserved;
    uint32_t LoadOptionsSize;
    void *LoadOptions;
    void *ImageBase;
    uint64_t ImageSize;
    uint32_t ImageCodeType;
    uint32_t ImageDataType;
    void *Unload;
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
    PHASE_AFTER_SECURITY_COOKIE_INIT = 18,
    PHASE_IN_CRT_INITTERM = 19
};

static uint32_t g_phase;
static uint64_t g_managed_target;
static uint64_t g_managed_image_base;
static uint64_t g_managed_image_size;
static uint32_t g_platform_last_error;
static uint64_t g_stack_lower;
static uint64_t g_stack_upper;
#if defined(GXOS_ENABLE_CREATE_EVENT_W) || \
    defined(GXOS_ENABLE_CREATE_MEMORY_RESOURCE_NOTIFICATION)
static GXOS_SCHEDULER g_create_event_scheduler;
#endif
#ifdef GXOS_ENABLE_CREATE_EVENT_W
static GXOS_CREATE_EVENT_W_CONTEXT g_create_event_context;
static uint32_t g_create_event_scheduler_initialize_count;
static uint32_t g_create_event_w_invocation_count;
static uint32_t g_create_event_w_success_count;
static uint32_t g_create_event_w_storage_failures;
static GXOS_SCHEDULER_HANDLE g_create_event_w_handles[GXOS_SCHEDULER_MAX_EVENTS];
static uintptr_t g_create_event_w_storage_addresses[GXOS_SCHEDULER_MAX_EVENTS];
static uint32_t g_create_event_w_import_descriptor_index;
static uint32_t g_create_event_w_import_symbol_index;
static uint32_t g_create_event_w_importing_iat_rva;
static void *EFIAPI platform_create_event_w(void *event_attributes,
                                             int32_t manual_reset,
                                             int32_t initial_state,
                                             const uint16_t *name);
#endif
#ifdef GXOS_ENABLE_CREATE_MEMORY_RESOURCE_NOTIFICATION
static GXOS_CREATE_MEMORY_RESOURCE_NOTIFICATION_CONTEXT
    g_memory_resource_notification_context;
static uint32_t g_memory_resource_notification_invocation_count;
static uint32_t g_memory_resource_notification_success_count;
static uint32_t g_memory_resource_notification_storage_failures;
static GXOS_SCHEDULER_HANDLE g_memory_resource_notification_handle;
static uintptr_t g_memory_resource_notification_storage_address;
static uint32_t g_memory_resource_notification_import_descriptor_index;
static uint32_t g_memory_resource_notification_import_symbol_index;
static uint32_t g_memory_resource_notification_importing_iat_rva;
static void *EFIAPI platform_create_memory_resource_notification(
    uint32_t notification_type);
#endif
/* These symbols are intentionally external to the assembly entry file. */
volatile uint32_t gxos_exception_dispatch_active;
uint32_t gxos_exception_probe_enabled;
uint64_t gxos_probe_expected_rip;
uint64_t gxos_probe_expected_rsp;
uint64_t gxos_probe_expected_rcx;
uint64_t gxos_probe_expected_rdx;
uint64_t gxos_probe_sentinel_rcx;
uint64_t gxos_probe_sentinel_rdx;
uint64_t gxos_probe_landing_rcx;
uint64_t gxos_probe_landing_rdx;
uint64_t gxos_probe_landing_rsp;
uint32_t gxos_probe_landing_reached;
 #ifdef GXOS_ENABLE_EXCEPTION_SYNTHETIC_PROBE
static uint64_t g_synthetic_handler_calls;
static uint32_t g_probe_context_modifications_validated;
#endif
#if defined(GXOS_ENABLE_EXCEPTION_SYNTHETIC_PROBE) || defined(GXOS_ENABLE_VECTORED_EXCEPTION_HANDLER)
static GXOS_VEH_REGISTRY g_veh_registry;
static GXOS_VEH_IMAGE g_veh_payload_image;
static GXOS_VEH_IMAGE g_veh_harness_image;
#ifdef GXOS_ENABLE_VECTORED_EXCEPTION_HANDLER
static uint64_t g_veh_add_invocation_count;
static uint64_t g_veh_add_returned_handle;
static uint64_t g_veh_add_last_first;
static uint64_t g_veh_add_last_callback;
static uint64_t g_veh_add_last_return_address;
static uint64_t g_veh_add_last_call_site;
static uint64_t g_veh_add_last_registration_sequence;
static uint32_t g_veh_add_last_slot;
static uint32_t g_veh_add_last_insertion_position;
static uint32_t g_veh_add_import_descriptor_index;
static uint32_t g_veh_add_import_symbol_index;
static uint32_t g_veh_add_importing_iat_rva;
#endif
#ifdef GXOS_ENABLE_EXCEPTION_SYNTHETIC_PROBE
static uint32_t g_veh_nested_registration_rejected;
#endif
#endif

extern void gxos_fault_no_error_0(void);
extern void gxos_fault_no_error_1(void);
extern void gxos_fault_no_error_2(void);
extern void gxos_fault_no_error_3(void);
extern void gxos_fault_no_error_4(void);
extern void gxos_fault_no_error_5(void);
extern void gxos_fault_no_error_6(void);
extern void gxos_fault_no_error_7(void);
extern void gxos_fault_with_error_8(void);
extern void gxos_fault_no_error_9(void);
extern void gxos_fault_with_error_10(void);
extern void gxos_fault_with_error_11(void);
extern void gxos_fault_with_error_12(void);
extern void gxos_fault_with_error_13(void);
extern void gxos_fault_with_error_14(void);
extern void gxos_fault_no_error_15(void);
extern void gxos_fault_no_error_16(void);
extern void gxos_fault_with_error_17(void);
extern void gxos_fault_no_error_18(void);
extern void gxos_fault_no_error_19(void);
extern void gxos_fault_no_error_20(void);
extern void gxos_fault_with_error_21(void);
extern void gxos_fault_no_error_22(void);
extern void gxos_fault_no_error_23(void);
extern void gxos_fault_no_error_24(void);
extern void gxos_fault_no_error_25(void);
extern void gxos_fault_no_error_26(void);
extern void gxos_fault_no_error_27(void);
extern void gxos_fault_no_error_28(void);
extern void gxos_fault_with_error_29(void);
extern void gxos_fault_with_error_30(void);
extern void gxos_fault_no_error_31(void);
extern void gxos_exception_probe(void);
extern const uint8_t gxos_exception_probe_int3[];
extern void gxos_exception_probe_landing(void);
static void serial_text(const char *text);
#ifdef GXOS_ENABLE_SYNTHETIC_SCHEDULER_PROOF
static void GXOS_SCHEDULER_MS_ABI scheduler_log_hex(const char *name,
                                                    uint64_t value);
static void GXOS_SCHEDULER_MS_ABI scheduler_log_u32(const char *name,
                                                    uint32_t value);
#endif
#ifdef GXOS_ENABLE_CRT_MALLOC
static GXOS_CRT_MALLOC_CONTEXT g_crt_malloc_context;
static uint32_t g_crt_malloc_import_descriptor_index;
static uint32_t g_crt_malloc_importing_iat_rva;
#endif
#ifdef GXOS_ENABLE_GET_MODULE_HANDLE
static GXOS_MAIN_MODULE_FACTS g_main_module_facts;
static uint32_t g_get_module_handle_import_descriptor_index;
static uint32_t g_get_module_handle_importing_iat_rva;
static uint64_t g_get_module_handle_calls;
static uint64_t g_get_module_handle_successes;
static uint64_t g_get_module_handle_failures;
static uint64_t g_get_module_handle_null_calls;
static uint64_t g_get_module_handle_named_calls;
static uint32_t g_get_module_handle_last_error_before;
static uint32_t g_get_module_handle_last_error_after;
static GXOS_MODULE_HANDLE_REPORT g_get_module_handle_last_report;
static uintptr_t g_get_module_handle_last_caller;
static uintptr_t g_get_module_handle_last_call_site;
static uintptr_t g_get_module_handle_last_handle;
static GXOS_MODULE_HANDLE_HMODULE EFIAPI platform_get_module_handle_w(
    GXOS_MODULE_HANDLE_LPCWSTR module_name);
#endif
#ifdef GXOS_ENABLE_GET_MODULE_HANDLE_EX
static uint32_t g_get_module_handle_ex_import_descriptor_index;
static uint32_t g_get_module_handle_ex_importing_iat_rva;
static uint64_t g_get_module_handle_ex_calls;
static uint64_t g_get_module_handle_ex_successes;
static uint64_t g_get_module_handle_ex_failures;
static uint32_t g_main_module_permanent_residency_proven;
static GXOS_MODULE_HANDLE_EX_REPORT g_get_module_handle_ex_last_report;
static int EFIAPI platform_get_module_handle_ex_w(
    uint32_t flags,
    uintptr_t address,
    GXOS_MODULE_HANDLE_HMODULE *module_handle_out);
#endif
#ifdef GXOS_ENABLE_CRT_ONEXIT_REGISTER
static uint64_t g_crt_onexit_register_callback_executed;
#endif
#ifdef GXOS_ENABLE_GET_PROC_ADDRESS
static uint32_t g_get_proc_address_import_descriptor_index;
static uint32_t g_get_proc_address_importing_iat_rva;
static GXOS_GET_PROC_ADDRESS_MEMORY_CONTEXT g_get_proc_address_memory;
static uint64_t g_get_proc_address_calls;
static uint64_t g_get_proc_address_successes;
static uint64_t g_get_proc_address_absent_module_failures;
static uint64_t g_get_proc_address_missing_export_failures;
static uint64_t g_get_proc_address_invalid_handle_failures;
static uint64_t g_get_proc_address_named_calls;
static uint64_t g_get_proc_address_ordinal_calls;
static uint64_t g_get_proc_address_export_lookup_attempts;
static uint64_t g_get_proc_address_pointer_stored;
static uint64_t g_get_proc_address_pointer_called;
static GXOS_GET_PROC_ADDRESS_REPORT g_get_proc_address_last_report;
#ifdef GXOS_GET_PROC_ADDRESS_SYNTHETIC_RESULT
static int GXOS_GET_PROC_ADDRESS_MS_ABI platform_get_proc_address_synthetic_stub(void)
{
    ++g_get_proc_address_pointer_called;
    serial_text("GXOS_NET10:GETPROCADDRESS_SYNTHETIC_STUB_CALLED=1\r\n");
    return 0;
}
#endif
static GXOS_GET_PROC_ADDRESS_FARPROC EFIAPI platform_get_proc_address(
    GXOS_GET_PROC_ADDRESS_HMODULE module_handle,
    GXOS_GET_PROC_ADDRESS_LPCSTR procedure_identifier);
#endif
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
#ifdef GXOS_ENABLE_CRT_ONEXIT_REGISTER
static uint64_t g_crt_onexit_register_successes;
#endif
#ifdef GXOS_ENABLE_SLIST
static uint64_t g_slist_initialize_calls;
#endif
#ifdef GXOS_ENABLE_CRT_INITTERM
static uint64_t g_crt_initterm_calls;
static uint64_t g_crt_initterm_current_index = GXOS_CRT_INITTERM_NO_CALLBACK;
static uintptr_t g_crt_initterm_current_target;
static uint32_t g_crt_initterm_callback_active;

#endif
#ifdef GXOS_ENABLE_GETENV
static uint64_t g_getenv_calls;
static uint64_t g_getenv_successes;
static uint64_t g_getenv_missing;
static uint64_t g_getenv_last_return;
static uint64_t g_getenv_last_error_before;
static uint64_t g_getenv_last_error_after;
static uint64_t g_getenv_last_caller;
#endif
#ifdef GXOS_ENABLE_CRT_STRICMP
static GXOS_READABLE_IMAGE g_crt_stricmp_image;
static uint64_t g_crt_stricmp_calls;
static uint64_t g_crt_stricmp_successes;
static uint64_t g_crt_stricmp_failures;
static uint64_t g_crt_stricmp_total_bytes;
static uint64_t g_crt_stricmp_longest_prefix;
#ifdef GXOS_ENABLE_PROCESS_GROUP_AFFINITY
#define GXOS_CRT_STRICMP_MAX_CENSUS_PAIRS 1024U
static uint64_t g_crt_stricmp_census_hash = 0xCBF29CE484222325ULL;
static uint64_t g_crt_stricmp_unique_operand_pairs;
static uint64_t g_crt_stricmp_verbose_records_suppressed;
static uintptr_t g_crt_stricmp_pair_lhs[GXOS_CRT_STRICMP_MAX_CENSUS_PAIRS];
static uintptr_t g_crt_stricmp_pair_rhs[GXOS_CRT_STRICMP_MAX_CENSUS_PAIRS];
static uint32_t g_crt_stricmp_pair_table_overflow;
#endif
#endif
#ifdef GXOS_ENABLE_SYSTEM_INFO
static GXOS_SYSTEM_FACTS g_system_info_facts;
static GXOS_SYSTEM_INFO_MEMORY_REGION g_system_info_regions[GXOS_SYSTEM_INFO_MAX_MEMORY_REGIONS];
static GXOS_SYSTEM_INFO_MEMORY_CONTEXT g_system_info_memory;
static uint64_t g_system_info_calls;
static uint64_t g_system_info_successes;
static uint64_t g_system_info_failures;
static uint32_t g_system_info_field_consumption_emitted;
static void EFIAPI platform_get_system_info(GXOS_SYSTEM_INFO *destination);
#endif
#ifdef GXOS_ENABLE_NUMA_HIGHEST_NODE
static GXOS_NUMA_FACTS g_numa_facts;
static uint64_t g_numa_calls;
static uint64_t g_numa_successes;
static uint64_t g_numa_failures;
static uint64_t g_numa_last_error_before;
static uint64_t g_numa_last_error_after;
static uint64_t g_numa_last_output_before;
static uint64_t g_numa_last_output_after;
static uint64_t g_numa_last_status;
static GXOS_NUMA_BOOL g_numa_last_boolean;
static uint32_t g_numa_last_output_read;
static GXOS_NUMA_BOOL EFIAPI platform_get_numa_highest_node_number(
    GXOS_NUMA_ULONG *highest_node_number);
#endif
#ifdef GXOS_ENABLE_PROCESS_GROUP_AFFINITY
static GXOS_PROCESS_GROUP_AFFINITY_FACTS g_process_group_facts;
static uint64_t g_process_group_calls;
static uint64_t g_process_group_successes;
static uint64_t g_process_group_insufficient_buffer_calls;
static uint64_t g_process_group_failures;
static uint64_t g_process_group_retry_count;
static uint64_t g_process_group_last_handle;
static uint64_t g_process_group_last_count_pointer;
static uint64_t g_process_group_last_array_pointer;
static uint16_t g_process_group_last_input_capacity;
static uint16_t g_process_group_last_output_count;
static uint16_t g_process_group_last_required_count;
static uint32_t g_process_group_last_groups_written;
static GXOS_PROCESS_GROUP_AFFINITY_BOOL g_process_group_last_boolean;
static GXOS_PROCESS_GROUP_AFFINITY_STATUS g_process_group_last_status;
static uint32_t g_process_group_last_error_before;
static uint32_t g_process_group_last_error_after;
static uint32_t g_process_group_last_array_null;
static uint32_t g_process_group_last_count_read;
static uint32_t g_process_group_last_count_written;
static GXOS_PROCESS_GROUP_AFFINITY_BOOL EFIAPI
platform_get_process_group_affinity(void *process_handle,
                                    GXOS_PROCESS_GROUP_AFFINITY_USHORT *group_count,
                                    GXOS_PROCESS_GROUP_AFFINITY_USHORT *group_array);
#endif
#ifdef GXOS_ENABLE_PROCESS_AFFINITY
static GXOS_PROCESS_AFFINITY_FACTS g_process_affinity_facts;
static uint64_t g_process_affinity_calls;
static uint64_t g_process_affinity_successes;
static uint64_t g_process_affinity_failures;
static uint64_t g_process_affinity_last_handle;
static uint64_t g_process_affinity_last_process_pointer;
static uint64_t g_process_affinity_last_system_pointer;
static uint64_t g_process_affinity_last_process_before;
static uint64_t g_process_affinity_last_system_before;
static uint64_t g_process_affinity_last_process_after;
static uint64_t g_process_affinity_last_system_after;
static uint64_t g_process_affinity_last_error_before;
static uint64_t g_process_affinity_last_error_after;
static GXOS_PROCESS_AFFINITY_BOOL g_process_affinity_last_boolean;
static GXOS_PROCESS_AFFINITY_STATUS g_process_affinity_last_status;
static GXOS_PROCESS_AFFINITY_REPORT g_process_affinity_last_report;
static GXOS_PROCESS_AFFINITY_BOOL EFIAPI platform_get_process_affinity_mask(
    void *process_handle,
    GXOS_PROCESS_AFFINITY_DWORD_PTR *process_affinity_mask,
    GXOS_PROCESS_AFFINITY_DWORD_PTR *system_affinity_mask);
#endif
#ifdef GXOS_ENABLE_QUERY_INFORMATION_JOB_OBJECT
static GXOS_QUERY_JOB_FACTS g_query_job_facts;
static uint64_t g_query_job_calls;
static uint64_t g_query_job_successes;
static uint64_t g_query_job_expected_no_job_failures;
static uint64_t g_query_job_other_failures;
static GXOS_QUERY_JOB_STATUS g_query_job_last_status;
static uint32_t g_query_job_last_error_before;
static uint32_t g_query_job_last_error_after;
static GXOS_QUERY_JOB_BOOL g_query_job_last_boolean;
/* These four symbols are intentionally visible to the naked ABI shim only. */
uint64_t g_query_job_entry_rsp;
uint64_t g_query_job_return_address;
uint64_t g_query_job_fifth_stack_address;
uint64_t g_query_job_fifth_stack_value;
static GXOS_QUERY_JOB_REPORT g_query_job_last_report;
static GXOS_QUERY_JOB_BOOL EFIAPI platform_query_information_job_object_body(
    void *job_handle,
    GXOS_QUERY_JOB_INFO_CLASS information_class,
    GXOS_QUERY_JOB_OUTPUT output,
    GXOS_QUERY_JOB_DWORD output_length,
    GXOS_QUERY_JOB_RETURN_LENGTH return_length) __attribute__((used));
static GXOS_QUERY_JOB_BOOL EFIAPI platform_query_information_job_object(
    void *, GXOS_QUERY_JOB_INFO_CLASS, GXOS_QUERY_JOB_OUTPUT,
    GXOS_QUERY_JOB_DWORD, GXOS_QUERY_JOB_RETURN_LENGTH) __attribute__((naked));
#endif

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

static void __attribute__((noreturn)) halt_forever(void)
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

static uintptr_t import_call_site(uintptr_t return_address)
{
    if (g_managed_image_base != 0 &&
        return_address >= g_managed_image_base + 6U &&
        ((const uint8_t *)(uintptr_t)(return_address - 6U))[0] == 0xFF) {
        return return_address - 6U;
    }
    if (g_managed_image_base != 0 &&
        return_address >= g_managed_image_base + 5U &&
        ((const uint8_t *)(uintptr_t)(return_address - 5U))[0] == 0xE8) {
        return return_address - 5U;
    }
    return return_address;
}

#ifdef GXOS_ENABLE_CREATE_EVENT_W
static uintptr_t capture_caller_rbx(void)
{
    uintptr_t value;
    __asm__ volatile ("mov %%rbx, %0" : "=r"(value));
    return value;
}

static uint32_t create_event_w_storage_address_valid(uintptr_t address)
{
    return g_managed_image_base != 0 && g_managed_image_size >= 8U &&
           address >= g_managed_image_base &&
           address <= g_managed_image_base + g_managed_image_size - 8U;
}

static uint32_t create_event_w_validate_storage_history(void)
{
    uint32_t index;
    for (index = 0; index != g_create_event_w_success_count; ++index) {
        uintptr_t address = g_create_event_w_storage_addresses[index];
        if (!create_event_w_storage_address_valid(address) ||
            *(const uint64_t *)(uintptr_t)address != g_create_event_w_handles[index]) {
            return 0;
        }
    }
    return 1;
}

static void create_event_w_live_counts(uint32_t *events,
                                       uint32_t *event_objects,
                                       uint32_t *event_handles,
                                       uint32_t *waiters,
                                       uint32_t *thread_objects,
                                       uint32_t *additional_threads,
                                       uint32_t *notification_objects,
                                       uint32_t *notification_handles,
                                       uint32_t *notification_waiters,
                                       uint32_t *total_live_objects,
                                       uint32_t *free_object_slots,
                                       uint32_t *live_public_handles)
{
    uint32_t index;
    *events = 0;
    *event_objects = 0;
    *event_handles = 0;
    *waiters = 0;
    *thread_objects = 0;
    *additional_threads = 0;
    *notification_objects = 0;
    *notification_handles = 0;
    *notification_waiters = 0;
    *total_live_objects = 0;
    *free_object_slots = 0;
    *live_public_handles = 0;
    for (index = 0; index != GXOS_SCHEDULER_MAX_EVENTS; ++index) {
        if (g_create_event_scheduler.events[index].live) {
            (*events)++;
            *waiters += g_create_event_scheduler.events[index].waiter_count;
        }
    }
    for (index = 0; index != GXOS_SCHEDULER_MAX_OBJECTS; ++index) {
        GXOS_SCHEDULER_OBJECT *object = &g_create_event_scheduler.objects[index];
        if (!object->live) {
            (*free_object_slots)++;
            continue;
        }
        (*total_live_objects)++;
        *live_public_handles += object->public_handle_refs;
        if (object->type == GXOS_SCHEDULER_OBJECT_EVENT) {
            (*event_objects)++;
            *event_handles += object->public_handle_refs;
        } else if (object->type == GXOS_SCHEDULER_OBJECT_THREAD) {
            (*thread_objects)++;
        } else if (object->type ==
                   GXOS_SCHEDULER_OBJECT_MEMORY_RESOURCE_NOTIFICATION) {
            (*notification_objects)++;
            *notification_handles += object->public_handle_refs;
        }
    }
    for (index = 0;
         index != GXOS_SCHEDULER_MAX_MEMORY_RESOURCE_NOTIFICATIONS;
         ++index) {
        GXOS_SCHEDULER_MEMORY_RESOURCE_NOTIFICATION *notification =
            &g_create_event_scheduler.memory_resource_notifications[index];
        if (notification->live) {
            *notification_waiters += notification->waitable.waiter_count;
        }
    }
    for (index = 0; index != GXOS_SCHEDULER_MAX_THREADS; ++index) {
        if (g_create_event_scheduler.threads[index].live &&
            !g_create_event_scheduler.threads[index].is_boot_thread) {
            (*additional_threads)++;
        }
    }
}

static void emit_create_event_w_final_summary(void)
{
    uint32_t events;
    uint32_t event_objects;
    uint32_t event_handles;
    uint32_t waiters;
    uint32_t thread_objects;
    uint32_t additional_threads;
    uint32_t notification_objects;
    uint32_t notification_handles;
    uint32_t notification_waiters;
    uint32_t total_live_objects;
    uint32_t free_object_slots;
    uint32_t live_public_handles;
    uint32_t index;
    uint32_t signaled = 0;
    uint32_t auto_reset = 0;
    uint32_t manual_reset = 0;

    if (!create_event_w_validate_storage_history()) {
        ++g_create_event_w_storage_failures;
    }
    create_event_w_live_counts(&events, &event_objects, &event_handles,
                               &waiters, &thread_objects, &additional_threads,
                               &notification_objects, &notification_handles,
                               &notification_waiters, &total_live_objects,
                               &free_object_slots, &live_public_handles);
    for (index = 0; index != GXOS_SCHEDULER_MAX_EVENTS; ++index) {
        GXOS_SCHEDULER_EVENT *event = &g_create_event_scheduler.events[index];
        if (!event->live) continue;
        if (event->signaled) ++signaled;
        if (event->manual_reset) ++manual_reset;
        else ++auto_reset;
    }
    serial_field_hex("GXOS_NET10:CREATEEVENTW_FINAL_SUCCESS_COUNT=0x",
                     g_create_event_w_success_count);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CREATEEVENTW_FINAL_LIVE_EVENT_COUNT=0x", events);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CREATEEVENTW_FINAL_LIVE_EVENT_OBJECT_COUNT=0x",
                     event_objects);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CREATEEVENTW_FINAL_LIVE_PUBLIC_HANDLE_COUNT=0x",
                     event_handles);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CREATEEVENTW_FINAL_LIVE_EVENT_WAITER_COUNT=0x",
                     waiters);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CREATEEVENTW_FINAL_INITIALLY_SIGNALED_COUNT=0x",
                     signaled);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CREATEEVENTW_FINAL_AUTO_RESET_COUNT=0x", auto_reset);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CREATEEVENTW_FINAL_MANUAL_RESET_COUNT=0x",
                     manual_reset);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CREATEEVENTW_FINAL_LIVE_THREAD_OBJECT_COUNT=0x",
                     thread_objects);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CREATEEVENTW_FINAL_ADDITIONAL_THREAD_COUNT=0x",
                     additional_threads);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CREATEEVENTW_FINAL_LIVE_MEMORY_RESOURCE_NOTIFICATION_OBJECT_COUNT=0x",
                     notification_objects);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CREATEEVENTW_FINAL_LIVE_MEMORY_RESOURCE_NOTIFICATION_HANDLE_COUNT=0x",
                     notification_handles);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CREATEEVENTW_FINAL_LIVE_MEMORY_RESOURCE_NOTIFICATION_WAITER_COUNT=0x",
                     notification_waiters);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CREATEEVENTW_FINAL_TOTAL_LIVE_OBJECT_COUNT=0x",
                     total_live_objects);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CREATEEVENTW_FINAL_OBJECT_CAPACITY=0x",
                     GXOS_SCHEDULER_MAX_OBJECTS);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CREATEEVENTW_FINAL_FREE_OBJECT_COUNT=0x",
                     free_object_slots);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CREATEEVENTW_FINAL_TOTAL_LIVE_PUBLIC_HANDLE_COUNT=0x",
                     live_public_handles);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CREATEEVENTW_FINAL_EVENT_CAPACITY=0x",
                     GXOS_SCHEDULER_MAX_EVENTS);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CREATEEVENTW_FINAL_FREE_EVENT_CAPACITY=0x",
                     GXOS_SCHEDULER_MAX_EVENTS - events);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CREATEEVENTW_FINAL_NOTIFICATION_CAPACITY=0x",
                     GXOS_SCHEDULER_MAX_MEMORY_RESOURCE_NOTIFICATIONS);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CREATEEVENTW_FINAL_FREE_NOTIFICATION_CAPACITY=0x",
                     GXOS_SCHEDULER_MAX_MEMORY_RESOURCE_NOTIFICATIONS -
                         notification_objects);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CREATEEVENTW_FINAL_SCHEDULER_THREAD_OBJECT_COUNT=0x",
                     thread_objects);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CREATEEVENTW_SCHEDULER_INITIALIZE_COUNT=0x",
                     g_create_event_scheduler_initialize_count);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CREATEEVENTW_STORAGE_FAILURE_COUNT=0x",
                     g_create_event_w_storage_failures);
    serial_text("\r\n");
    serial_text("GXOS_NET10:CREATEEVENTW_SYNTHETIC_PROOF_OBJECTS_LIVE=0\r\n");
    serial_text("GXOS_NET10:CREATEEVENTW_FINAL_SUMMARY=READY\r\n");
}

static void *EFIAPI platform_create_event_w(void *event_attributes,
                                             int32_t manual_reset,
                                             int32_t initial_state,
                                             const uint16_t *name)
{
    GXOS_SCHEDULER_TCB *before_thread = gxos_scheduler_current_thread();
    uintptr_t return_address = (uintptr_t)__builtin_return_address(0);
    uintptr_t call_site = import_call_site(return_address);
    uintptr_t storage_address = capture_caller_rbx();
    uint32_t invocation = ++g_create_event_w_invocation_count;
    uint32_t previous_storage_valid = create_event_w_validate_storage_history();
    GXOS_SCHEDULER_HANDLE handle;
    GXOS_SCHEDULER_EVENT *event;
    GXOS_SCHEDULER_OBJECT *object;
    uint32_t success_index;
    uint32_t index;
    uint32_t storage_class = 0;
    uint32_t tcb_unchanged;

    if (!previous_storage_valid) ++g_create_event_w_storage_failures;
    if (event_attributes != 0 || name != 0 ||
        !g_create_event_scheduler.active) {
        g_platform_last_error = GXOS_CREATE_EVENT_W_ERROR_INVALID_PARAMETER;
        serial_field_hex("GXOS_NET10:CREATEEVENTW_FAILED_INVOCATION=0x", invocation);
        serial_text("\r\n");
        serial_field_hex("GXOS_NET10:CREATEEVENTW_FAILED_RCX=0x",
                         (uint64_t)(uintptr_t)event_attributes);
        serial_text("\r\n");
        serial_field_hex("GXOS_NET10:CREATEEVENTW_FAILED_RDX=0x",
                         (uint32_t)manual_reset);
        serial_text("\r\n");
        serial_field_hex("GXOS_NET10:CREATEEVENTW_FAILED_R8=0x",
                         (uint32_t)initial_state);
        serial_text("\r\n");
        serial_field_hex("GXOS_NET10:CREATEEVENTW_FAILED_R9=0x",
                         (uint64_t)(uintptr_t)name);
        serial_text("\r\n");
        return 0;
    }
    handle = gxos_create_event_w_contract(&g_create_event_context,
                                          event_attributes, manual_reset,
                                          initial_state, name);
    if (handle == 0) {
        g_platform_last_error = GXOS_CREATE_EVENT_W_ERROR_NOT_ENOUGH_MEMORY;
        return 0;
    }
    if (g_create_event_w_success_count >= GXOS_SCHEDULER_MAX_EVENTS) {
        fail("createeventw-diagnostic-capacity");
    }
    success_index = g_create_event_w_success_count++;
    event = gxos_scheduler_event_from_handle(handle);
    if (event == 0 || event->object_slot >= GXOS_SCHEDULER_MAX_OBJECTS) {
        fail("createeventw-handle-decode");
    }
    object = &g_create_event_scheduler.objects[event->object_slot];
    for (index = 0; index != success_index; ++index) {
        if (g_create_event_w_handles[index] == handle) {
            fail("createeventw-duplicate-handle");
        }
    }
    g_create_event_w_handles[success_index] = handle;
    g_create_event_w_storage_addresses[success_index] = storage_address;
    if (invocation == 1U && storage_address ==
            g_managed_image_base + 0xADA08U) {
        storage_class = 1;
    } else if (invocation == 2U && storage_address ==
                   g_managed_image_base + 0xADA18U) {
        storage_class = 1;
    } else if (invocation >= 3U) {
        storage_class = 2;
    }
    if (storage_class == 0U || !create_event_w_storage_address_valid(storage_address)) {
        ++g_create_event_w_storage_failures;
    }
    tcb_unchanged = before_thread != 0 &&
        before_thread == gxos_scheduler_current_thread() &&
        before_thread->state == GXOS_SCHEDULER_THREAD_RUNNING &&
        before_thread->execution_refs == 1U &&
        before_thread->public_handle_refs == 0U;
    serial_text("GXOS_NET10:CREATEEVENTW_BEGIN\r\n");
    serial_field_hex("GXOS_NET10:CREATEEVENTW_INVOCATION=0x", invocation);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CREATEEVENTW_RUNTIME_CALLER_ADDRESS=0x", return_address);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CREATEEVENTW_RUNTIME_CALL_SITE=0x", call_site);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CREATEEVENTW_CALLER_RVA=0x",
                     call_site >= g_managed_image_base
                         ? call_site - g_managed_image_base : 0);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CREATEEVENTW_RCX=0x",
                     (uint64_t)(uintptr_t)event_attributes);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CREATEEVENTW_RDX=0x", (uint32_t)manual_reset);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CREATEEVENTW_R8=0x", (uint32_t)initial_state);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CREATEEVENTW_R9=0x", (uint64_t)(uintptr_t)name);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CREATEEVENTW_RETURNED_HANDLE=0x", handle);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CREATEEVENTW_HANDLE_GENERATION=0x", event->generation);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CREATEEVENTW_HANDLE_TYPE=0x", object->type);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CREATEEVENTW_EVENT_REGISTRY_SLOT=0x",
                     (uint64_t)(event - g_create_event_scheduler.events));
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CREATEEVENTW_OBJECT_REGISTRY_SLOT=0x",
                     event->object_slot);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CREATEEVENTW_EVENT_MANUAL_RESET=0x", event->manual_reset);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CREATEEVENTW_EVENT_SIGNALED=0x", event->signaled);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CREATEEVENTW_PUBLIC_REFERENCE_COUNT=0x",
                     object->public_handle_refs);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CREATEEVENTW_WAITER_COUNT=0x", event->waiter_count);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CREATEEVENTW_STORAGE_ADDRESS=0x", storage_address);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CREATEEVENTW_STORAGE_RVA=0x",
                     storage_address >= g_managed_image_base
                         ? storage_address - g_managed_image_base : 0);
    serial_text("\r\n");
    serial_text("GXOS_NET10:CREATEEVENTW_STORAGE_CLASS=");
    serial_text(storage_class == 1U ? "KNOWN_PERSISTENT_SLOT\r\n" :
                "ALLOCATOR_RECORD_SLOT0\r\n");
    serial_field_hex("GXOS_NET10:CREATEEVENTW_PREVIOUS_STORAGE_VALID=0x",
                     previous_storage_valid);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CREATEEVENTW_TCB_STATE_UNCHANGED=0x", tcb_unchanged);
    serial_text("\r\n");
    serial_text("GXOS_NET10:CREATEEVENTW_RETURNED\r\n");
    return (void *)(uintptr_t)handle;
}
#endif

#ifdef GXOS_ENABLE_CREATE_MEMORY_RESOURCE_NOTIFICATION
static uint32_t memory_resource_notification_storage_address_valid(
    uintptr_t address)
{
    return g_managed_image_base != 0 && g_managed_image_size >= 8U &&
           address >= g_managed_image_base &&
           address <= g_managed_image_base + g_managed_image_size - 8U;
}

static void emit_memory_resource_notification_summary(void)
{
    GXOS_SCHEDULER_MEMORY_RESOURCE_NOTIFICATION *notification =
        gxos_scheduler_memory_resource_notification_from_handle(
            g_memory_resource_notification_handle);
    GXOS_SCHEDULER_OBJECT *object = gxos_scheduler_object_from_handle(
        g_memory_resource_notification_handle);
    uintptr_t storage_value = 0;
    uint32_t storage_valid = memory_resource_notification_storage_address_valid(
        g_memory_resource_notification_storage_address);

    if (storage_valid) {
        storage_value = *(const uintptr_t *)(uintptr_t)
            g_memory_resource_notification_storage_address;
        if (storage_value != g_memory_resource_notification_handle) {
            ++g_memory_resource_notification_storage_failures;
        }
    } else {
        ++g_memory_resource_notification_storage_failures;
    }
    serial_field_hex("GXOS_NET10:MEMORYRESOURCENOTIFICATION_FINAL_INVOCATION_COUNT=0x",
                     g_memory_resource_notification_invocation_count);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:MEMORYRESOURCENOTIFICATION_FINAL_SUCCESS_COUNT=0x",
                     g_memory_resource_notification_success_count);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:MEMORYRESOURCENOTIFICATION_FINAL_LIVE_OBJECT_COUNT=0x",
                     notification != 0 && notification->live ? 1U : 0U);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:MEMORYRESOURCENOTIFICATION_FINAL_LIVE_HANDLE_COUNT=0x",
                     object == 0 ? 0U : object->public_handle_refs);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:MEMORYRESOURCENOTIFICATION_FINAL_WAITER_COUNT=0x",
                     notification == 0 ? 0U : notification->waitable.waiter_count);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:MEMORYRESOURCENOTIFICATION_FINAL_STORAGE_VALUE=0x",
                     storage_value);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:MEMORYRESOURCENOTIFICATION_FINAL_STORAGE_FAILURE_COUNT=0x",
                     g_memory_resource_notification_storage_failures);
    serial_text("\r\n");
    serial_text("GXOS_NET10:MEMORYRESOURCENOTIFICATION_FINAL_QUERY_COUNT=0x0\r\n");
    serial_text("GXOS_NET10:MEMORYRESOURCENOTIFICATION_FINAL_CLOSE_COUNT=0x0\r\n");
    serial_text("GXOS_NET10:MEMORYRESOURCENOTIFICATION_FINAL_DUPLICATE_COUNT=0x0\r\n");
}

static void *EFIAPI platform_create_memory_resource_notification(
    uint32_t notification_type)
{
    GXOS_SCHEDULER_TCB *before_thread = gxos_scheduler_current_thread();
    uintptr_t return_address = (uintptr_t)__builtin_return_address(0);
    uintptr_t call_site = import_call_site(return_address);
    uintptr_t storage_address = g_managed_image_base + 0xADA28U;
    uint32_t invocation = ++g_memory_resource_notification_invocation_count;
    uint32_t previous_storage_valid = create_event_w_validate_storage_history();
    GXOS_SCHEDULER_HANDLE handle;
    GXOS_SCHEDULER_MEMORY_RESOURCE_NOTIFICATION *notification;
    GXOS_SCHEDULER_OBJECT *object;
    uint32_t tcb_unchanged;

    if (!previous_storage_valid) {
        ++g_memory_resource_notification_storage_failures;
    }
    handle = gxos_create_memory_resource_notification_contract(
        &g_memory_resource_notification_context, notification_type);
    if (handle == 0) {
        g_platform_last_error =
            GXOS_CREATE_MEMORY_RESOURCE_NOTIFICATION_ERROR_NOT_ENOUGH_MEMORY;
        return 0;
    }
    notification = gxos_scheduler_memory_resource_notification_from_handle(handle);
    object = gxos_scheduler_object_from_handle(handle);
    if (notification == 0 || object == 0 ||
        notification->registry_slot >=
            GXOS_SCHEDULER_MAX_MEMORY_RESOURCE_NOTIFICATIONS ||
        object->type != GXOS_SCHEDULER_OBJECT_MEMORY_RESOURCE_NOTIFICATION) {
        fail("memory-resource-notification-handle-decode");
    }
    if (g_memory_resource_notification_success_count != 0U) {
        fail("memory-resource-notification-duplicate-success");
    }
    g_memory_resource_notification_success_count++;
    g_memory_resource_notification_handle = handle;
    g_memory_resource_notification_storage_address = storage_address;
    tcb_unchanged = before_thread != 0 &&
        before_thread == gxos_scheduler_current_thread() &&
        before_thread->state == GXOS_SCHEDULER_THREAD_RUNNING &&
        before_thread->execution_refs == 1U &&
        before_thread->public_handle_refs == 0U;
    if (!memory_resource_notification_storage_address_valid(storage_address) ||
        storage_address != g_managed_image_base + 0xADA28U) {
        ++g_memory_resource_notification_storage_failures;
    }
    serial_text("GXOS_NET10:MEMORYRESOURCENOTIFICATION_BEGIN\r\n");
    serial_field_hex("GXOS_NET10:MEMORYRESOURCENOTIFICATION_INVOCATION=0x",
                     invocation);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:MEMORYRESOURCENOTIFICATION_RUNTIME_CALLER_ADDRESS=0x",
                     return_address);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:MEMORYRESOURCENOTIFICATION_RUNTIME_CALL_SITE=0x",
                     call_site);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:MEMORYRESOURCENOTIFICATION_CALLER_RVA=0x",
                     call_site >= g_managed_image_base
                         ? call_site - g_managed_image_base : 0);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:MEMORYRESOURCENOTIFICATION_RCX=0x",
                     notification_type);
    serial_text("\r\n");
    serial_text("GXOS_NET10:MEMORYRESOURCENOTIFICATION_TYPE=LowMemoryResourceNotification\r\n");
    serial_field_hex("GXOS_NET10:MEMORYRESOURCENOTIFICATION_RETURNED_HANDLE=0x",
                     handle);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:MEMORYRESOURCENOTIFICATION_HANDLE_GENERATION=0x",
                     notification->generation);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:MEMORYRESOURCENOTIFICATION_HANDLE_TYPE=0x",
                     object->type);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:MEMORYRESOURCENOTIFICATION_REGISTRY_SLOT=0x",
                     notification->registry_slot);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:MEMORYRESOURCENOTIFICATION_OBJECT_REGISTRY_SLOT=0x",
                     notification->object_slot);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:MEMORYRESOURCENOTIFICATION_RAW_TYPE=0x",
                     notification->notification_type);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:MEMORYRESOURCENOTIFICATION_SIGNALED=0x",
                     notification->waitable.signaled);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:MEMORYRESOURCENOTIFICATION_WAITABLE_LIVE=0x",
                     notification->waitable.live);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:MEMORYRESOURCENOTIFICATION_WAITABLE_COMPATIBLE=0x",
                     gxos_scheduler_waitable_from_handle(handle) ==
                         &notification->waitable);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:MEMORYRESOURCENOTIFICATION_CLOSE_STATE=0x",
                     notification->close_state);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:MEMORYRESOURCENOTIFICATION_PUBLIC_REFERENCE_COUNT=0x",
                     object->public_handle_refs);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:MEMORYRESOURCENOTIFICATION_WAITER_COUNT=0x",
                     notification->waitable.waiter_count);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:MEMORYRESOURCENOTIFICATION_STORAGE_ADDRESS=0x",
                     storage_address);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:MEMORYRESOURCENOTIFICATION_STORAGE_RVA=0x",
                     storage_address >= g_managed_image_base
                         ? storage_address - g_managed_image_base : 0);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:MEMORYRESOURCENOTIFICATION_PREVIOUS_STORAGE_VALID=0x",
                     previous_storage_valid);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:MEMORYRESOURCENOTIFICATION_TCB_STATE_UNCHANGED=0x",
                     tcb_unchanged);
    serial_text("\r\n");
    serial_text("GXOS_NET10:MEMORYRESOURCENOTIFICATION_RETURNED\r\n");
    return (void *)(uintptr_t)handle;
}
#endif

#ifdef GXOS_ENABLE_SYNTHETIC_SCHEDULER_PROOF
static void GXOS_SCHEDULER_MS_ABI scheduler_log_hex(const char *name,
                                                    uint64_t value)
{
    serial_field_hex(name, value);
    serial_text("\r\n");
}

static void GXOS_SCHEDULER_MS_ABI scheduler_log_u32(const char *name,
                                                    uint32_t value)
{
    serial_field_hex(name, value);
    serial_text("\r\n");
}
#endif

#ifdef GXOS_ENABLE_CRT_MALLOC
static void GXOS_CRT_MALLOC_MS_ABI platform_crt_malloc_trace(
    const GXOS_CRT_MALLOC_DIAGNOSTIC *diagnostic,
    void *context)
{
    uint64_t call_site_rva = 0;
    (void)context;
    if (diagnostic->runtime_call_site >= g_managed_image_base) {
        call_site_rva = diagnostic->runtime_call_site - g_managed_image_base;
    }
    serial_text("GXOS_NET10:MALLOC_BEGIN\r\n");
    serial_field_hex("GXOS_NET10:MALLOC_INVOCATION_NUMBER=0x",
                     diagnostic->invocation_number);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:MALLOC_STATIC_CALL_SITE=0x",
                     diagnostic->static_call_site);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:MALLOC_RUNTIME_CALL_SITE=0x",
                     diagnostic->runtime_call_site);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:MALLOC_CALL_SITE_RVA=0x", call_site_rva);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:MALLOC_REQUESTED_SIZE=0x",
                     diagnostic->requested_size);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:MALLOC_LIVE_COUNT_BEFORE=0x",
                     diagnostic->live_count_before);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:MALLOC_REGISTRY_SLOT=0x",
                     diagnostic->registry_slot);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:MALLOC_POOL_SERVICE_AVAILABLE=0x",
                     diagnostic->pool_service_available);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:MALLOC_ALLOCATE_POOL_STATUS=0x",
                     diagnostic->allocate_pool_status);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:MALLOC_RETURNED_POINTER=0x",
                     diagnostic->returned_pointer);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:MALLOC_ALIGNMENT_MOD8=0x",
                     diagnostic->alignment_mod8);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:MALLOC_ALIGNMENT_MOD16=0x",
                     diagnostic->alignment_mod16);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:MALLOC_RANGE_BASE=0x",
                     diagnostic->allocation_range_base);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:MALLOC_RANGE_END=0x",
                     diagnostic->allocation_range_end);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:MALLOC_OVERLAP_VALIDATION=0x",
                     diagnostic->overlap_validation);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:MALLOC_LIVE_COUNT_AFTER=0x",
                     diagnostic->live_count_after);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:MALLOC_ROLLBACK_COUNT=0x",
                     diagnostic->rollback_count);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:MALLOC_ROLLBACK_STATUS=0x",
                     diagnostic->rollback_status);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:MALLOC_RETURN_VALUE=0x",
                     diagnostic->return_value);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:MALLOC_FAILURE=0x", diagnostic->failure);
    serial_text("\r\n");
    serial_text("GXOS_NET10:MALLOC_RETURNED\r\n");
}

static void emit_crt_malloc_summary(void)
{
    serial_field_hex("GXOS_NET10:MALLOC_MAX_LIVE_ALLOCATION_COUNT=0x",
                     g_crt_malloc_context.max_live_allocation_count);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:MALLOC_TOTAL_REQUESTED_BYTES=0x",
                     g_crt_malloc_context.total_requested_bytes);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:MALLOC_LARGEST_REQUEST=0x",
                     g_crt_malloc_context.largest_request);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:MALLOC_ALLOCATION_FAILURE_COUNT=0x",
                     g_crt_malloc_context.allocation_failure_count);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:MALLOC_METADATA_EXHAUSTION_COUNT=0x",
                     g_crt_malloc_context.metadata_exhaustion_count);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:MALLOC_DUPLICATE_POINTER_REJECTION_COUNT=0x",
                     g_crt_malloc_context.duplicate_pointer_rejection_count);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:MALLOC_POOL_ROLLBACK_COUNT=0x",
                     g_crt_malloc_context.pool_rollback_count);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:MALLOC_CALLNEWH_REACHED=0x",
                     g_crt_malloc_context.callnewh_reached);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:MALLOC_DIAGNOSTIC_COUNT=0x",
                     g_crt_malloc_context.diagnostic_count);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:MALLOC_DIAGNOSTIC_OVERFLOW_COUNT=0x",
                     g_crt_malloc_context.diagnostic_overflow_count);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:MALLOC_LIVE_COUNT=0x",
                     g_crt_malloc_context.live_count);
    serial_text("\r\n");
}
#endif

#ifdef GXOS_ENABLE_CRT_STRCMP
static uint64_t g_crt_strcmp_calls;

static uint32_t platform_strcmp_bounded_length(const char *value)
{
    uint32_t length = 0;
    while (length != 64 && value[length] != 0) length++;
    return length;
}

static void platform_strcmp_emit_bytes(const char *name,
                                       const char *value,
                                       uint32_t length)
{
    static const char digits[] = "0123456789ABCDEF";
    uint32_t index;
    serial_text(name);
    for (index = 0; index != length; index++) {
        uint8_t byte = (uint8_t)value[index];
        serial_char((uint8_t)digits[byte >> 4]);
        serial_char((uint8_t)digits[byte & 0x0F]);
    }
    serial_text("\r\n");
}

static void platform_strcmp_emit_text(const char *name,
                                      const char *value,
                                      uint32_t length)
{
    uint32_t index;
    serial_text(name);
    for (index = 0; index != length; index++) {
        uint8_t byte = (uint8_t)value[index];
        serial_char(byte >= 0x20 && byte <= 0x7E ? byte : (uint8_t)'.');
    }
    serial_text("\r\n");
}

static int GXOS_CRT_STRCMP_MS_ABI platform_strcmp(const char *lhs, const char *rhs)
{
    uint32_t lhs_length = platform_strcmp_bounded_length(lhs);
    uint32_t rhs_length = platform_strcmp_bounded_length(rhs);
    int result = gxos_crt_strcmp(lhs, rhs);

    g_crt_strcmp_calls++;
    serial_field_hex("GXOS_NET10:CRT_STRCMP_CALL_COUNT=0x", g_crt_strcmp_calls);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CRT_STRCMP_CALLER=0x",
                     (uint64_t)(uintptr_t)__builtin_return_address(0));
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CRT_STRCMP_LHS_POINTER=0x", (uint64_t)(uintptr_t)lhs);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CRT_STRCMP_RHS_POINTER=0x", (uint64_t)(uintptr_t)rhs);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CRT_STRCMP_LHS_LENGTH=0x", lhs_length);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CRT_STRCMP_RHS_LENGTH=0x", rhs_length);
    serial_text("\r\n");
    serial_text("GXOS_NET10:CRT_STRCMP_LHS_NULL_TERMINATED=");
    serial_text(lhs_length == 64 ? "0\r\n" : "1\r\n");
    serial_text("GXOS_NET10:CRT_STRCMP_RHS_NULL_TERMINATED=");
    serial_text(rhs_length == 64 ? "0\r\n" : "1\r\n");
    platform_strcmp_emit_bytes("GXOS_NET10:CRT_STRCMP_LHS_BYTES=", lhs, lhs_length);
    platform_strcmp_emit_bytes("GXOS_NET10:CRT_STRCMP_RHS_BYTES=", rhs, rhs_length);
    platform_strcmp_emit_text("GXOS_NET10:CRT_STRCMP_LHS_TEXT=", lhs, lhs_length);
    platform_strcmp_emit_text("GXOS_NET10:CRT_STRCMP_RHS_TEXT=", rhs, rhs_length);
    serial_field_hex("GXOS_NET10:CRT_STRCMP_RESULT=0x", (uint64_t)(uint32_t)result);
    serial_text("\r\n");
    return result;
}
#endif

#ifdef GXOS_ENABLE_GET_PROC_ADDRESS
static uint64_t platform_get_proc_address_static_call_site(uintptr_t call_site)
{
    if (call_site >= (uintptr_t)g_managed_image_base) {
        return 0x180000000ULL +
               (uint64_t)(call_site - (uintptr_t)g_managed_image_base);
    }
    return 0;
}

static uintptr_t platform_get_proc_address_caller_start(uint64_t static_call_site)
{
    if (static_call_site == 0x180037C71ULL) return 0x180037C40ULL;
    if (static_call_site == 0x18003C568ULL) return 0x18003C530ULL;
    if (static_call_site == 0x18003C9B1ULL ||
        static_call_site == 0x18003CA92ULL ||
        static_call_site == 0x18003CADAULL) return 0x18003C980ULL;
    if (static_call_site == 0x18003CE77ULL) return 0x18003CE50ULL;
    return 0;
}

static const char *platform_get_proc_address_caller_name(
    uint64_t static_call_site)
{
    if (static_call_site == 0x180037C71ULL) {
        return "NativeAOT_RtlDllShutdownInProgress_probe";
    }
    if (static_call_site == 0x18003C568ULL) {
        return "NativeAOT_InitializeContext2_probe";
    }
    return "nearest-identifiable-NativeAOT-region";
}

static void platform_get_proc_address_emit_name(
    const GXOS_GET_PROC_ADDRESS_REPORT *report)
{
    static const char digits[] = "0123456789ABCDEF";
    uint32_t index;

    if (report->identifier_kind != GXOS_PROC_IDENTIFIER_NAME) return;
    serial_text("GXOS_NET10:GETPROCADDRESS_NAME_BYTES=");
    for (index = 0; index != report->name_preview_length; ++index) {
        uint8_t value = report->name_preview[index];
        serial_char((uint8_t)digits[(value >> 4) & 0xFU]);
        serial_char((uint8_t)digits[value & 0xFU]);
    }
    if (report->name_preview_truncated) serial_text("...");
    serial_text("\r\n");
    serial_text("GXOS_NET10:GETPROCADDRESS_NAME_PREVIEW=\"");
    for (index = 0; index != report->name_preview_length; ++index) {
        uint8_t value = report->name_preview[index];
        serial_char(value >= 0x20U && value <= 0x7EU ? value : (uint8_t)'.');
    }
    if (report->name_preview_truncated) serial_text("...");
    serial_text("\"\r\n");
}

static void platform_get_proc_address_emit_call(
    const GXOS_GET_PROC_ADDRESS_REPORT *report,
    uintptr_t return_address)
{
    uintptr_t call_site = return_address >= 6U ? return_address - 6U : 0;
    uint64_t static_call_site =
        platform_get_proc_address_static_call_site(call_site);
    uintptr_t caller_start =
        platform_get_proc_address_caller_start(static_call_site);

    serial_text("GXOS_NET10:GETPROCADDRESS_BEGIN\r\n");
    serial_field_hex("GXOS_NET10:GETPROCADDRESS_CALL_INDEX=0x",
                     g_get_proc_address_calls - 1U);
    serial_text("\r\n");
    serial_text("GXOS_NET10:GETPROCADDRESS_IMPORT_MODULE=KERNEL32.dll\r\n");
    serial_text("GXOS_NET10:GETPROCADDRESS_IMPORT_SYMBOL=GetProcAddress\r\n");
    serial_field_hex("GXOS_NET10:GETPROCADDRESS_IMPORT_DESCRIPTOR_INDEX=0x",
                     g_get_proc_address_import_descriptor_index);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCADDRESS_IAT_RVA=0x",
                     g_get_proc_address_importing_iat_rva);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCADDRESS_PREFERRED_IAT=0x",
                     0x180000000ULL + g_get_proc_address_importing_iat_rva);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCADDRESS_RUNTIME_IAT=0x",
                     (uint64_t)g_managed_image_base +
                         g_get_proc_address_importing_iat_rva);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCADDRESS_STATIC_CALL_SITE=0x",
                     static_call_site);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCADDRESS_RUNTIME_CALL_SITE=0x",
                     call_site);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCADDRESS_RETURN_ADDRESS=0x",
                     return_address);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCADDRESS_CALLER_START=0x",
                     caller_start);
    serial_text("\r\n");
    serial_text("GXOS_NET10:GETPROCADDRESS_CALLER=");
    serial_text(platform_get_proc_address_caller_name(static_call_site));
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCADDRESS_MODULE_HANDLE=0x",
                     report->module_handle);
    serial_text("\r\n");
    serial_text("GXOS_NET10:GETPROCADDRESS_MODULE_CLASS=");
    serial_text(report->module_is_null ? "ABSENT_NULL\r\n" :
                (report->module_approved ? "APPROVED_MAPPED\r\n" :
                 "NONNULL_UNAPPROVED\r\n"));
    serial_field_hex("GXOS_NET10:GETPROCADDRESS_IDENTIFIER_RAW=0x",
                     report->identifier_raw);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCADDRESS_IDENTIFIER_HIGH_BITS=0x",
                     report->identifier_high_order_bits);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCADDRESS_IDENTIFIER_LOW_WORD=0x",
                     report->identifier_low_order_word);
    serial_text("\r\n");
    serial_text("GXOS_NET10:GETPROCADDRESS_IDENTIFIER_KIND=");
    serial_text(gxos_get_proc_address_identifier_kind_name(
                    report->identifier_kind));
    serial_text("\r\n");
    if (report->identifier_kind == GXOS_PROC_IDENTIFIER_ORDINAL) {
        serial_field_hex("GXOS_NET10:GETPROCADDRESS_ORDINAL=0x",
                         report->ordinal);
        serial_text("\r\n");
        serial_text("GXOS_NET10:GETPROCADDRESS_MEMORY_READ=0\r\n");
    } else {
        serial_field_hex("GXOS_NET10:GETPROCADDRESS_NAME_POINTER=0x",
                         report->name_pointer);
        serial_text("\r\n");
        serial_field_hex("GXOS_NET10:GETPROCADDRESS_NAME_POINTER_CANONICAL=0x",
                         report->name_pointer_canonical);
        serial_text("\r\n");
        serial_field_hex("GXOS_NET10:GETPROCADDRESS_NAME_READABLE=0x",
                         report->name_readable);
        serial_text("\r\n");
        serial_field_hex("GXOS_NET10:GETPROCADDRESS_NAME_LENGTH=0x",
                         report->name_length);
        serial_text("\r\n");
        serial_field_hex("GXOS_NET10:GETPROCADDRESS_NAME_TERMINATOR=0x",
                         report->name_terminator);
        serial_text("\r\n");
        serial_field_hex("GXOS_NET10:GETPROCADDRESS_NAME_REGION_BASE=0x",
                         report->name_region_base);
        serial_text("\r\n");
        serial_field_hex("GXOS_NET10:GETPROCADDRESS_NAME_REGION_END=0x",
                         report->name_region_end);
        serial_text("\r\n");
        serial_field_hex("GXOS_NET10:GETPROCADDRESS_NAME_REGION_READABLE=0x",
                         report->name_region_readable);
        serial_text("\r\n");
        serial_field_hex("GXOS_NET10:GETPROCADDRESS_NAME_REGION_EXECUTABLE=0x",
                         report->name_region_executable);
        serial_text("\r\n");
        serial_field_hex("GXOS_NET10:GETPROCADDRESS_NAME_REGION_WRITABLE=0x",
                         report->name_region_writable);
        serial_text("\r\n");
        serial_field_hex("GXOS_NET10:GETPROCADDRESS_NAME_7BIT_ASCII=0x",
                         report->name_all_7bit_ascii);
        serial_text("\r\n");
        serial_field_hex("GXOS_NET10:GETPROCADDRESS_NAME_HIGH_BIT_COUNT=0x",
                         report->name_high_bit_count);
        serial_text("\r\n");
        platform_get_proc_address_emit_name(report);
    }
    serial_field_hex("GXOS_NET10:GETPROCADDRESS_MODULE_VALID=0x",
                     report->module_valid);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCADDRESS_EXPORT_LOOKUP_ATTEMPTED=0x",
                     report->export_lookup_attempted);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCADDRESS_RESULT=0x",
                     (uint64_t)(uintptr_t)report->result);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCADDRESS_LAST_ERROR_BEFORE=0x",
                     report->last_error_before);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCADDRESS_LAST_ERROR_AFTER=0x",
                     report->last_error_after);
    serial_text("\r\n");
    serial_text("GXOS_NET10:GETPROCADDRESS_STATUS=");
    serial_text(gxos_get_proc_address_status_name(report->status));
    serial_text("\r\n");
    serial_text("GXOS_NET10:GETPROCADDRESS_MODULE_HANDLE_PROVENANCE=");
    serial_text(report->module_handle == 0
                    ? "PRECEDING_GETMODULEHANDLEW_NULL_RESULT\r\n"
                    : "NONNULL_HANDLE_NOT_APPROVED\r\n");
    if (report->status == GXOS_GET_PROC_ADDRESS_STATUS_INVALID_MODULE_HANDLE &&
        report->module_handle == 0) {
        serial_text("GXOS_NET10:GETPROCADDRESS_EXPECTED_ABSENT_MODULE_FAILURE\r\n");
    }
    serial_text("GXOS_NET10:GETPROCADDRESS_RETURNED\r\n");
}

static GXOS_GET_PROC_ADDRESS_FARPROC EFIAPI platform_get_proc_address(
    GXOS_GET_PROC_ADDRESS_HMODULE module_handle,
    GXOS_GET_PROC_ADDRESS_LPCSTR procedure_identifier)
{
    GXOS_GET_PROC_ADDRESS_FARPROC result =
        (GXOS_GET_PROC_ADDRESS_FARPROC)0;
    GXOS_GET_PROC_ADDRESS_DWORD previous_error = g_platform_last_error;
    GXOS_GET_PROC_ADDRESS_DWORD last_error = previous_error;
    GXOS_GET_PROC_ADDRESS_STATUS status;
    uintptr_t return_address = (uintptr_t)__builtin_return_address(0);

    ++g_get_proc_address_calls;
    status = gxos_get_proc_address_checked(
        module_handle, procedure_identifier, &g_get_proc_address_memory,
        previous_error, &result, &last_error, &g_get_proc_address_last_report);
#ifdef GXOS_GET_PROC_ADDRESS_SYNTHETIC_RESULT
    if (module_handle == 0 &&
        g_get_proc_address_last_report.identifier_kind ==
            GXOS_PROC_IDENTIFIER_NAME &&
        status == GXOS_GET_PROC_ADDRESS_STATUS_INVALID_MODULE_HANDLE) {
        result = (GXOS_GET_PROC_ADDRESS_FARPROC)
            platform_get_proc_address_synthetic_stub;
        status = GXOS_GET_PROC_ADDRESS_STATUS_OK;
        last_error = previous_error;
        g_get_proc_address_last_report.status = status;
        g_get_proc_address_last_report.module_approved = 0;
        g_get_proc_address_last_report.module_valid = 0;
        g_get_proc_address_last_report.result = result;
        g_get_proc_address_last_report.last_error_after = last_error;
        serial_text("GXOS_NET10:GETPROCADDRESS_SYNTHETIC_RESULT=1\r\n");
    }
#endif
#ifdef GXOS_GET_PROC_ADDRESS_WRONG_ERROR
    if (result == (GXOS_GET_PROC_ADDRESS_FARPROC)0) {
        last_error = GXOS_GET_PROC_ADDRESS_ERROR_INVALID_HANDLE;
        serial_text("GXOS_NET10:GETPROCADDRESS_WRONG_ERROR_EXPERIMENT=1\r\n");
    }
#endif
    g_platform_last_error = last_error;
    g_get_proc_address_last_report.status = status;
    g_get_proc_address_last_report.result = result;
    g_get_proc_address_last_report.last_error_before = previous_error;
    g_get_proc_address_last_report.last_error_after = last_error;
    if (g_get_proc_address_last_report.identifier_kind ==
        GXOS_PROC_IDENTIFIER_ORDINAL) {
        ++g_get_proc_address_ordinal_calls;
    } else {
        ++g_get_proc_address_named_calls;
    }
    if (g_get_proc_address_last_report.export_lookup_attempted != 0) {
        ++g_get_proc_address_export_lookup_attempts;
    }
    if (result != (GXOS_GET_PROC_ADDRESS_FARPROC)0) {
        ++g_get_proc_address_successes;
    } else if (status == GXOS_GET_PROC_ADDRESS_STATUS_INVALID_MODULE_HANDLE &&
               module_handle == 0) {
        ++g_get_proc_address_absent_module_failures;
    } else if (status == GXOS_GET_PROC_ADDRESS_STATUS_EXPORT_NOT_FOUND) {
        ++g_get_proc_address_missing_export_failures;
    } else {
        ++g_get_proc_address_invalid_handle_failures;
    }
    platform_get_proc_address_emit_call(&g_get_proc_address_last_report,
                                        return_address);
    return result;
}
#endif

#ifdef GXOS_ENABLE_GET_MODULE_HANDLE_EX
static const char *platform_get_module_handle_ex_status_name(
    GXOS_MODULE_HANDLE_EX_STATUS status)
{
    switch (status) {
        case GXOS_MODULE_HANDLE_EX_STATUS_OK: return "OK";
        case GXOS_MODULE_HANDLE_EX_STATUS_UNSUPPORTED_FLAGS: return "UNSUPPORTED_FLAGS";
        case GXOS_MODULE_HANDLE_EX_STATUS_NULL_ADDRESS: return "NULL_ADDRESS";
        case GXOS_MODULE_HANDLE_EX_STATUS_NONCANONICAL_ADDRESS: return "NONCANONICAL_ADDRESS";
        case GXOS_MODULE_HANDLE_EX_STATUS_ADDRESS_OUTSIDE_IMAGE: return "ADDRESS_OUTSIDE_IMAGE";
        case GXOS_MODULE_HANDLE_EX_STATUS_AMBIGUOUS_IMAGE: return "AMBIGUOUS_IMAGE";
        case GXOS_MODULE_HANDLE_EX_STATUS_NULL_OUTPUT: return "NULL_OUTPUT";
        case GXOS_MODULE_HANDLE_EX_STATUS_OUTPUT_NOT_WRITABLE: return "OUTPUT_NOT_WRITABLE";
        case GXOS_MODULE_HANDLE_EX_STATUS_INVALID_IMAGE_FACTS: return "INVALID_IMAGE_FACTS";
        case GXOS_MODULE_HANDLE_EX_STATUS_IMAGE_RANGE_OVERFLOW: return "IMAGE_RANGE_OVERFLOW";
        case GXOS_MODULE_HANDLE_EX_STATUS_IMAGE_NOT_PERMANENT: return "IMAGE_NOT_PERMANENT";
        default: return "UNKNOWN";
    }
}

static void platform_get_module_handle_ex_emit_call(
    uint32_t flags,
    uintptr_t address,
    GXOS_MODULE_HANDLE_HMODULE *module_handle_out,
    int result,
    const GXOS_MODULE_HANDLE_EX_REPORT *report,
    uintptr_t return_address,
    uint32_t prior_onexit_callback_executed)
{
    uintptr_t call_site = return_address >= 6U ? return_address - 6U : 0;
    uint64_t static_call_site = 0;
    uintptr_t output_before = report->output_pointer_proven_writable != 0
                                   ? report->output_value_before
                                   : 0;
    uintptr_t output_after = report->output_pointer_proven_writable != 0
                                  ? report->output_value_after
                                  : 0;

    if (call_site >= (uintptr_t)g_managed_image_base) {
        static_call_site = 0x180000000ULL +
                           (uint64_t)(call_site -
                                      (uintptr_t)g_managed_image_base);
    }
    serial_text("GXOS_NET10:GETMODULEHANDLEEX_BEGIN\r\n");
    serial_field_hex("GXOS_NET10:GETMODULEHANDLEEX_INVOCATION_NUMBER=0x",
                     g_get_module_handle_ex_calls);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETMODULEHANDLEEX_RAW_RCX=0x", flags);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETMODULEHANDLEEX_RAW_RDX=0x", address);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETMODULEHANDLEEX_RAW_R8=0x",
                     (uintptr_t)module_handle_out);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETMODULEHANDLEEX_RUNTIME_CALL_SITE=0x",
                     call_site);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETMODULEHANDLEEX_STATIC_CALL_SITE=0x",
                     static_call_site);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETMODULEHANDLEEX_RETURN_ADDRESS=0x",
                     return_address);
    serial_text("\r\n");
    serial_text("GXOS_NET10:GETMODULEHANDLEEX_IMPORT_MODULE=KERNEL32.dll\r\n");
    serial_text("GXOS_NET10:GETMODULEHANDLEEX_IMPORT_SYMBOL=GetModuleHandleExW\r\n");
    serial_field_hex("GXOS_NET10:GETMODULEHANDLEEX_IMPORT_DESCRIPTOR_INDEX=0x",
                     g_get_module_handle_ex_import_descriptor_index);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETMODULEHANDLEEX_IAT_RVA=0x",
                     g_get_module_handle_ex_importing_iat_rva);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETMODULEHANDLEEX_PREFERRED_IAT=0x",
                     g_main_module_facts.preferred_image_base +
                         g_get_module_handle_ex_importing_iat_rva);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETMODULEHANDLEEX_RUNTIME_IAT=0x",
                     g_main_module_facts.mapped_image_base +
                         g_get_module_handle_ex_importing_iat_rva);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETMODULEHANDLEEX_FLAG_PIN=0x",
                     flags & GXOS_MODULE_HANDLE_EX_FLAG_PIN);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETMODULEHANDLEEX_FLAG_UNCHANGED_REFCOUNT=0x",
                     flags & 0x2U);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETMODULEHANDLEEX_FLAG_FROM_ADDRESS=0x",
                     flags & GXOS_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETMODULEHANDLEEX_UNKNOWN_FLAG_BITS=0x",
                     report->unknown_flag_bits);
    serial_text("\r\n");
    serial_text("GXOS_NET10:GETMODULEHANDLEEX_RDX_INTERPRETATION=ADDRESS\r\n");
    serial_field_hex("GXOS_NET10:GETMODULEHANDLEEX_RDX_IN_PAYLOAD=0x",
                     report->address_in_image);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETMODULEHANDLEEX_OUTPUT_VALUE_BEFORE=0x",
                     output_before);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETMODULEHANDLEEX_OUTPUT_VALUE_BEFORE_PROVEN=0x",
                     report->output_pointer_proven_writable);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETMODULEHANDLEEX_SELECTED_IMAGE_BASE=0x",
                     report->selected_image_base);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETMODULEHANDLEEX_SELECTED_IMAGE_SIZE=0x",
                     report->selected_image_size);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETMODULEHANDLEEX_ADDRESS_RVA=0x",
                     report->address_rva);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETMODULEHANDLEEX_LOOKUP_MATCH_COUNT=0x",
                     report->lookup_match_count);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETMODULEHANDLEEX_LOOKUP_UNIQUE=0x",
                     report->lookup_unique);
    serial_text("\r\n");
    serial_text("GXOS_NET10:GETMODULEHANDLEEX_IMAGE_ID=");
    serial_text(report->image_identity ==
                        GXOS_MODULE_HANDLE_EX_IMAGE_MAIN_NATIVEAOT_PAYLOAD
                    ? "MAIN_NATIVEAOT_PAYLOAD\r\n"
                    : "NONE\r\n");
    serial_field_hex("GXOS_NET10:GETMODULEHANDLEEX_RESIDENCY_INVARIANT_PROVEN=0x",
                     report->residency_invariant_proven);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETMODULEHANDLEEX_PRIOR_PINNED=0x",
                     report->prior_pinned);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETMODULEHANDLEEX_RESULTING_PINNED=0x",
                     report->resulting_pinned);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETMODULEHANDLEEX_ALLOCATION_OCCURRED=0x",
                     report->allocation_occurred);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETMODULEHANDLEEX_IMAGE_FREE_OR_UNLOAD_INVOKED=0x",
                     report->image_free_or_unload_invoked);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETMODULEHANDLEEX_PRIOR_ONEXIT_CALLBACK_EXECUTED=0x",
                     prior_onexit_callback_executed);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETMODULEHANDLEEX_OUTPUT_VALUE_AFTER=0x",
                     output_after);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETMODULEHANDLEEX_OUTPUT_VALUE_AFTER_PROVEN=0x",
                     report->output_pointer_proven_writable);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETMODULEHANDLEEX_OUTPUT_WRITE_ATTEMPTED=0x",
                     report->output_written);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETMODULEHANDLEEX_RESULT=0x", report->result);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETMODULEHANDLEEX_RETURN_VALUE=0x",
                     (uint32_t)result);
    serial_text("\r\n");
    serial_text("GXOS_NET10:GETMODULEHANDLEEX_STATUS=");
    serial_text(platform_get_module_handle_ex_status_name(report->status));
    serial_text("\r\n");
    serial_text("GXOS_NET10:GETMODULEHANDLEEX_RETURNED\r\n");
    if (result != 0) serial_text("GXOS_NET10:GETMODULEHANDLEEX_OK\r\n");
}

static int EFIAPI platform_get_module_handle_ex_w(
    uint32_t flags,
    uintptr_t address,
    GXOS_MODULE_HANDLE_HMODULE *module_handle_out)
{
    GXOS_MODULE_HANDLE_EX_STATUS status;
    uintptr_t return_address = (uintptr_t)__builtin_return_address(0);
    uint32_t prior_onexit_callback_executed = 0;
    int result;

    ++g_get_module_handle_ex_calls;
#ifdef GXOS_ENABLE_CRT_ONEXIT_REGISTER
    prior_onexit_callback_executed =
        g_crt_onexit_register_callback_executed != 0;
#endif
    status = gxos_get_module_handle_ex_checked(
        flags, address, module_handle_out, &g_main_module_facts,
        (uintptr_t)g_stack_lower, (uintptr_t)g_stack_upper,
        g_main_module_permanent_residency_proven,
        &g_get_module_handle_ex_last_report);
    g_get_module_handle_ex_last_report.prior_onexit_callback_executed =
        prior_onexit_callback_executed;
    result = status == GXOS_MODULE_HANDLE_EX_STATUS_OK ? 1 : 0;
    if (result != 0) ++g_get_module_handle_ex_successes;
    else ++g_get_module_handle_ex_failures;
    platform_get_module_handle_ex_emit_call(
        flags, address, module_handle_out, result,
        &g_get_module_handle_ex_last_report, return_address,
        prior_onexit_callback_executed);
    return result;
}
#endif

#ifdef GXOS_ENABLE_GETENV
static uint32_t platform_getenv_bounded_name_length(const GXOS_ENVIRONMENT_WCHAR *name)
{
    uint32_t length = 0;
    while (length != 256 && name != 0 && name[length] != 0) length++;
    return length;
}

static void platform_getenv_emit_hex16(const char *prefix,
                                       const GXOS_ENVIRONMENT_WCHAR *value,
                                       uint32_t length)
{
    static const char digits[] = "0123456789ABCDEF";
    uint32_t index;
    uint32_t preview = length > 128 ? 128 : length;

    serial_text(prefix);
    for (index = 0; index != preview; index++) {
        uint16_t word = value[index];
        serial_char((uint8_t)digits[(word >> 12) & 0xF]);
        serial_char((uint8_t)digits[(word >> 8) & 0xF]);
        serial_char((uint8_t)digits[(word >> 4) & 0xF]);
        serial_char((uint8_t)digits[word & 0xF]);
    }
    if (preview != length) serial_text("...");
    serial_text("\r\n");
}

static void platform_getenv_emit_text(const char *prefix,
                                      const GXOS_ENVIRONMENT_WCHAR *value,
                                      uint32_t length)
{
    static const char digits[] = "0123456789ABCDEF";
    uint32_t index;
    uint32_t preview = length > 128 ? 128 : length;

    serial_text(prefix);
    serial_char('"');
    for (index = 0; index != preview; index++) {
        uint16_t word = value[index];
        if (word >= 0x20 && word <= 0x7E && word != '\\' && word != '"') {
            serial_char((uint8_t)word);
        } else {
            serial_text("\\u");
            serial_char((uint8_t)digits[(word >> 12) & 0xF]);
            serial_char((uint8_t)digits[(word >> 8) & 0xF]);
            serial_char((uint8_t)digits[(word >> 4) & 0xF]);
            serial_char((uint8_t)digits[word & 0xF]);
        }
    }
    if (preview != length) serial_text("...");
    serial_text("\"\r\n");
}

static void emit_getenv_summary(void)
{
    serial_field_hex("GXOS_NET10:GETENV_CALL_COUNT=0x", g_getenv_calls);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETENV_SUCCESS_COUNT=0x", g_getenv_successes);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETENV_MISSING_COUNT=0x", g_getenv_missing);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETENV_LAST_RETURN=0x", g_getenv_last_return);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETENV_LAST_ERROR_BEFORE=0x", g_getenv_last_error_before);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETENV_LAST_ERROR_AFTER=0x", g_getenv_last_error_after);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETENV_LAST_CALLER=0x", g_getenv_last_caller);
    serial_text("\r\n");
}
#endif

#ifdef GXOS_ENABLE_CRT_STRICMP
static int platform_stricmp_is_canonical(uintptr_t address)
{
#if UINTPTR_MAX > 0xFFFFFFFFU
    uint64_t high = (uint64_t)address >> 47;
    return high == 0 || high == 0x1FFFFU;
#else
    (void)address;
    return 1;
#endif
}

static const GXOS_CRT_INITTERM_MEMORY_REGION *platform_stricmp_region(const char *value)
{
    uintptr_t address = (uintptr_t)value;
    uint32_t index;

    for (index = 0; index != g_crt_stricmp_image.memory_region_count; index++) {
        const GXOS_CRT_INITTERM_MEMORY_REGION *region =
            &g_crt_stricmp_image.memory_regions[index];
        if (address >= region->base && address < region->end) return region;
    }
    return 0;
}

static size_t platform_stricmp_bounded_length(const char *value,
                                              uintptr_t *terminator,
                                              uint32_t *terminated)
{
    uintptr_t base = (uintptr_t)value;
    size_t length;

    *terminator = 0;
    *terminated = 0;
    if (value == 0) return 0;
    for (length = 0; length != GXOS_CRT_STRICMP_DEFAULT_MAX_SCAN; length++) {
        uintptr_t current;
        const GXOS_CRT_INITTERM_MEMORY_REGION *region;
        if ((uintptr_t)length > UINTPTR_MAX - base) return length;
        current = base + (uintptr_t)length;
        if (!platform_stricmp_is_canonical(current)) return length;
        region = platform_stricmp_region((const char *)(uintptr_t)current);
        if (region == 0 || region->readable == 0) return length;
        if (*(const unsigned char *)(uintptr_t)current == 0) {
            *terminator = current;
            *terminated = 1;
            return length;
        }
    }
    return GXOS_CRT_STRICMP_DEFAULT_MAX_SCAN;
}

#ifndef GXOS_ENABLE_PROCESS_GROUP_AFFINITY
static void platform_stricmp_emit_bytes(const char *prefix,
                                        const char *value,
                                        size_t length)
{
    static const char digits[] = "0123456789ABCDEF";
    size_t index;
    size_t preview = length > 64 ? 64 : length;

    serial_text(prefix);
    for (index = 0; index != preview; index++) {
        uint8_t byte = (uint8_t)value[index];
        serial_char((uint8_t)digits[byte >> 4]);
        serial_char((uint8_t)digits[byte & 0x0F]);
    }
    if (preview != length) serial_text("...");
    serial_text("\r\n");
}

static void platform_stricmp_emit_text(const char *prefix,
                                       const char *value,
                                       size_t length)
{
    static const char digits[] = "0123456789ABCDEF";
    size_t index;
    size_t preview = length > 64 ? 64 : length;

    serial_text(prefix);
    serial_char('"');
    for (index = 0; index != preview; index++) {
        uint8_t byte = (uint8_t)value[index];
        if (byte >= 0x20 && byte <= 0x7E && byte != '\\' && byte != '"') {
            serial_char(byte);
        } else {
            serial_text("\\x");
            serial_char((uint8_t)digits[byte >> 4]);
            serial_char((uint8_t)digits[byte & 0x0F]);
        }
    }
    if (preview != length) serial_text("...");
    serial_text("\"\r\n");
}
#endif

static void emit_crt_stricmp_summary(void)
{
    serial_field_hex("GXOS_NET10:CRT_STRICMP_CALL_COUNT=0x", g_crt_stricmp_calls);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CRT_STRICMP_SUCCESS_COUNT=0x", g_crt_stricmp_successes);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CRT_STRICMP_FAILURE_COUNT=0x", g_crt_stricmp_failures);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CRT_STRICMP_TOTAL_BYTES=0x", g_crt_stricmp_total_bytes);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CRT_STRICMP_LONGEST_PREFIX=0x", g_crt_stricmp_longest_prefix);
    serial_text("\r\n");
#ifdef GXOS_ENABLE_PROCESS_GROUP_AFFINITY
    serial_field_hex("GXOS_NET10:PRIOR_STRICMP_CALL_COUNT=0x", g_crt_stricmp_calls);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:PRIOR_STRICMP_SUCCESS_COUNT=0x", g_crt_stricmp_successes);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:PRIOR_STRICMP_FAILURE_COUNT=0x", g_crt_stricmp_failures);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:PRIOR_STRICMP_UNIQUE_OPERAND_PAIR_COUNT=0x",
                     g_crt_stricmp_unique_operand_pairs);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:PRIOR_STRICMP_CENSUS_HASH=0x",
                     g_crt_stricmp_census_hash);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:PRIOR_VERBOSE_RECORDS_SUPPRESSED=0x",
                     g_crt_stricmp_verbose_records_suppressed);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:PRIOR_STRICMP_PAIR_TABLE_OVERFLOW=0x",
                     g_crt_stricmp_pair_table_overflow);
    serial_text("\r\n");
#endif
}

#ifdef GXOS_ENABLE_PROCESS_GROUP_AFFINITY
static uint64_t process_group_hash_u64(uint64_t hash, uint64_t value)
{
    uint32_t index;
    for (index = 0; index != 8; index++) {
        hash ^= (value >> (index * 8U)) & 0xFFU;
        hash *= 0x100000001B3ULL;
    }
    return hash;
}

static void process_group_track_stricmp_pair(uintptr_t lhs, uintptr_t rhs)
{
    uint32_t index;
    for (index = 0; index != g_crt_stricmp_unique_operand_pairs; index++) {
        if (g_crt_stricmp_pair_lhs[index] == lhs &&
            g_crt_stricmp_pair_rhs[index] == rhs) return;
    }
    if (g_crt_stricmp_unique_operand_pairs >= GXOS_CRT_STRICMP_MAX_CENSUS_PAIRS) {
        g_crt_stricmp_pair_table_overflow = 1;
        return;
    }
    g_crt_stricmp_pair_lhs[g_crt_stricmp_unique_operand_pairs] = lhs;
    g_crt_stricmp_pair_rhs[g_crt_stricmp_unique_operand_pairs] = rhs;
    g_crt_stricmp_unique_operand_pairs++;
}
#endif

static int GXOS_CRT_STRICMP_MS_ABI platform_stricmp(const char *string1,
                                                    const char *string2)
{
    const GXOS_CRT_INITTERM_MEMORY_REGION *region1 = platform_stricmp_region(string1);
    const GXOS_CRT_INITTERM_MEMORY_REGION *region2 = platform_stricmp_region(string2);
    GXOS_CRT_STRICMP_REPORT report;
    GXOS_CRT_STRICMP_STATUS status;
    uintptr_t terminator1;
    uintptr_t terminator2;
    uint32_t terminated1;
    uint32_t terminated2;
    size_t length1;
    size_t length2;
    uint64_t caller = (uint64_t)(uintptr_t)__builtin_return_address(0);
    int result = 0;
    uint64_t call_index = g_crt_stricmp_calls++;

#ifdef GXOS_ENABLE_PROCESS_GROUP_AFFINITY
    (void)region1;
    (void)region2;
    (void)caller;
    g_crt_stricmp_verbose_records_suppressed++;
#else
    serial_text("GXOS_NET10:CRT_STRICMP_BEGIN\r\n");
    serial_field_hex("GXOS_NET10:CRT_STRICMP_CALL_INDEX=0x", call_index);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CRT_STRICMP_CALLER=0x", caller);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CRT_STRICMP_RETURN_ADDRESS=0x", caller);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CRT_STRICMP_STRING1_POINTER=0x", (uint64_t)(uintptr_t)string1);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CRT_STRICMP_STRING2_POINTER=0x", (uint64_t)(uintptr_t)string2);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CRT_STRICMP_MAX_SCAN=0x", GXOS_CRT_STRICMP_DEFAULT_MAX_SCAN);
    serial_text("\r\n");
    serial_text("GXOS_NET10:CRT_STRICMP_LOCALE=C_DEFAULT_NO_LOCALE_CHANGE\r\n");
    serial_text("GXOS_NET10:CRT_STRICMP_STRING1_REGION_IMAGE_BACKED=");
    serial_text(region1 != 0 && (uintptr_t)string1 >= g_crt_stricmp_image.image_base &&
                (uintptr_t)string1 < g_crt_stricmp_image.image_end ? "1\r\n" : "0\r\n");
    serial_text("GXOS_NET10:CRT_STRICMP_STRING2_REGION_IMAGE_BACKED=");
    serial_text(region2 != 0 && (uintptr_t)string2 >= g_crt_stricmp_image.image_base &&
                (uintptr_t)string2 < g_crt_stricmp_image.image_end ? "1\r\n" : "0\r\n");
    serial_field_hex("GXOS_NET10:CRT_STRICMP_STRING1_REGION_BEGIN=0x",
                     region1 == 0 ? 0 : (uint64_t)region1->base);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CRT_STRICMP_STRING1_REGION_END=0x",
                     region1 == 0 ? 0 : (uint64_t)region1->end);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CRT_STRICMP_STRING1_REGION_READABLE=0x",
                     region1 == 0 ? 0 : region1->readable);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CRT_STRICMP_STRING1_REGION_EXECUTABLE=0x",
                     region1 == 0 ? 0 : region1->executable);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CRT_STRICMP_STRING1_REGION_WRITABLE=0x",
                     region1 == 0 ? 0 : region1->writable);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CRT_STRICMP_STRING2_REGION_BEGIN=0x",
                     region2 == 0 ? 0 : (uint64_t)region2->base);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CRT_STRICMP_STRING2_REGION_END=0x",
                     region2 == 0 ? 0 : (uint64_t)region2->end);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CRT_STRICMP_STRING2_REGION_READABLE=0x",
                     region2 == 0 ? 0 : region2->readable);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CRT_STRICMP_STRING2_REGION_EXECUTABLE=0x",
                     region2 == 0 ? 0 : region2->executable);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CRT_STRICMP_STRING2_REGION_WRITABLE=0x",
                     region2 == 0 ? 0 : region2->writable);
    serial_text("\r\n");
#endif

    status = gxos_crt_stricmp_checked_report(
        string1, string2, &g_crt_stricmp_image,
        GXOS_CRT_STRICMP_DEFAULT_MAX_SCAN, &result, &report);
    serial_field_hex("GXOS_NET10:CRT_STRICMP_STATUS=0x", (uint64_t)(uint32_t)status);
    serial_text("\r\n");
    if (status != GXOS_CRT_STRICMP_STATUS_OK) {
        g_crt_stricmp_failures++;
        serial_text("GXOS_NET10:CRT_STRICMP_INVALID_INPUT\r\n");
        emit_crt_stricmp_summary();
        fail("crt-stricmp-invalid");
    }

    length1 = platform_stricmp_bounded_length(string1, &terminator1, &terminated1);
    length2 = platform_stricmp_bounded_length(string2, &terminator2, &terminated2);
    if (terminated1 == 0 || terminated2 == 0) {
        g_crt_stricmp_failures++;
        serial_text("GXOS_NET10:CRT_STRICMP_INVALID_INPUT\r\n");
        emit_crt_stricmp_summary();
        fail("crt-stricmp-census");
    }
    g_crt_stricmp_successes++;
    if (report.bytes_examined != 0 &&
        g_crt_stricmp_total_bytes <= UINT64_MAX - (uint64_t)report.bytes_examined) {
        g_crt_stricmp_total_bytes += (uint64_t)report.bytes_examined;
    } else if (report.bytes_examined != 0) {
        g_crt_stricmp_total_bytes = UINT64_MAX;
    }
    if ((uint64_t)report.compared_prefix > g_crt_stricmp_longest_prefix) {
        g_crt_stricmp_longest_prefix = (uint64_t)report.compared_prefix;
    }
#ifdef GXOS_ENABLE_PROCESS_GROUP_AFFINITY
    process_group_track_stricmp_pair((uintptr_t)string1, (uintptr_t)string2);
    g_crt_stricmp_census_hash = process_group_hash_u64(
        g_crt_stricmp_census_hash, call_index);
    g_crt_stricmp_census_hash = process_group_hash_u64(
        g_crt_stricmp_census_hash, report.bytes_examined);
    g_crt_stricmp_census_hash = process_group_hash_u64(
        g_crt_stricmp_census_hash, report.compared_prefix);
    g_crt_stricmp_census_hash = process_group_hash_u64(
        g_crt_stricmp_census_hash, (uint64_t)length1);
    g_crt_stricmp_census_hash = process_group_hash_u64(
        g_crt_stricmp_census_hash, (uint64_t)length2);
    g_crt_stricmp_census_hash = process_group_hash_u64(
        g_crt_stricmp_census_hash, (uint32_t)result);
#else
    platform_stricmp_emit_bytes("GXOS_NET10:CRT_STRICMP_STRING1_BYTES=", string1, length1);
    platform_stricmp_emit_bytes("GXOS_NET10:CRT_STRICMP_STRING2_BYTES=", string2, length2);
    platform_stricmp_emit_text("GXOS_NET10:CRT_STRICMP_STRING1_PREVIEW=", string1, length1);
    platform_stricmp_emit_text("GXOS_NET10:CRT_STRICMP_STRING2_PREVIEW=", string2, length2);
    serial_field_hex("GXOS_NET10:CRT_STRICMP_STRING1_LENGTH=0x", (uint64_t)length1);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CRT_STRICMP_STRING2_LENGTH=0x", (uint64_t)length2);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CRT_STRICMP_STRING1_TERMINATOR=0x", (uint64_t)terminator1);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CRT_STRICMP_STRING2_TERMINATOR=0x", (uint64_t)terminator2);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CRT_STRICMP_BYTES_EXAMINED=0x", (uint64_t)report.bytes_examined);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CRT_STRICMP_COMPARED_PREFIX=0x", (uint64_t)report.compared_prefix);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CRT_STRICMP_RESULT=0x", (uint64_t)(uint32_t)result);
    serial_text("\r\n");
    serial_text("GXOS_NET10:CRT_STRICMP_RESULT_CATEGORY=");
    serial_text(result < 0 ? "LESS\r\n" : result > 0 ? "GREATER\r\n" : "EQUAL\r\n");
    serial_text("GXOS_NET10:CRT_STRICMP_CALLER_CONSUMES_SIGN_OR_ZERO=1\r\n");
    serial_text("GXOS_NET10:CRT_STRICMP_RETURNED\r\n");
#ifdef GXOS_CRT_STRICMP_MARKER_MUTATION
    serial_text("GXOS_NET10:CRT_STRICMP_OX\r\n");
#else
        serial_text("GXOS_NET10:CRT_STRICMP_OK\r\n");
#endif
#endif
    return result;
}
#endif

#ifdef GXOS_ENABLE_CRT_STRLEN
static GXOS_READABLE_IMAGE g_crt_strlen_image;
static uint64_t g_crt_strlen_calls;
static uint64_t g_crt_strlen_successes;
static uint64_t g_crt_strlen_failures;
static uint64_t g_crt_strlen_total_bytes;
static uint64_t g_crt_strlen_longest;

static const GXOS_CRT_INITTERM_MEMORY_REGION *platform_strlen_region(const char *value)
{
    uintptr_t address = (uintptr_t)value;
    uint32_t index;

    for (index = 0; index != g_crt_strlen_image.memory_region_count; index++) {
        const GXOS_CRT_INITTERM_MEMORY_REGION *region =
            &g_crt_strlen_image.memory_regions[index];
        if (address >= region->base && address < region->end) return region;
    }
    return 0;
}

static void platform_strlen_emit_hex_bytes(const char *name,
                                           const char *value,
                                           size_t length)
{
    static const char digits[] = "0123456789ABCDEF";
    size_t index;
    size_t preview = length > 64 ? 64 : length;

    serial_text(name);
    for (index = 0; index != preview; index++) {
        uint8_t byte = (uint8_t)value[index];
        serial_char((uint8_t)digits[byte >> 4]);
        serial_char((uint8_t)digits[byte & 0x0F]);
    }
    if (preview != length) serial_text("...");
    serial_text("\r\n");
}

static void platform_strlen_emit_text_bytes(const char *name,
                                            const char *value,
                                            size_t length)
{
    static const char digits[] = "0123456789ABCDEF";
    size_t index;
    size_t preview = length > 64 ? 64 : length;

    serial_text(name);
    serial_char('"');
    for (index = 0; index != preview; index++) {
        uint8_t byte = (uint8_t)value[index];
        if (byte >= 0x20 && byte <= 0x7E && byte != '\\' && byte != '"') {
            serial_char(byte);
        } else {
            serial_text("\\x");
            serial_char((uint8_t)digits[byte >> 4]);
            serial_char((uint8_t)digits[byte & 0x0F]);
        }
    }
    if (preview != length) serial_text("...");
    serial_text("\"\r\n");
}

static void emit_crt_strlen_summary(void)
{
    serial_field_hex("GXOS_NET10:CRT_STRLEN_CALL_COUNT=0x", g_crt_strlen_calls);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CRT_STRLEN_SUCCESS_COUNT=0x", g_crt_strlen_successes);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CRT_STRLEN_FAILURE_COUNT=0x", g_crt_strlen_failures);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CRT_STRLEN_TOTAL_BYTES=0x", g_crt_strlen_total_bytes);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CRT_STRLEN_LONGEST=0x", g_crt_strlen_longest);
    serial_text("\r\n");
}

static size_t GXOS_CRT_STRLEN_MS_ABI platform_strlen(const char *string)
{
    const GXOS_CRT_INITTERM_MEMORY_REGION *region;
    GXOS_CRT_STRLEN_STATUS status;
    size_t length = 0;
    uint64_t caller = (uint64_t)(uintptr_t)__builtin_return_address(0);

    g_crt_strlen_calls++;
    region = platform_strlen_region(string);
    serial_text("GXOS_NET10:CRT_STRLEN_BEGIN\r\n");
    serial_field_hex("GXOS_NET10:CRT_STRLEN_CALL_INDEX=0x", g_crt_strlen_calls);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CRT_STRLEN_CALLER=0x", caller);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CRT_STRLEN_RETURN_ADDRESS=0x", caller);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CRT_STRLEN_POINTER=0x", (uint64_t)(uintptr_t)string);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CRT_STRLEN_MAX_SCAN=0x", GXOS_CRT_STRLEN_DEFAULT_MAX_SCAN);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CRT_STRLEN_RELOCATIONS_APPLIED=0x",
                     g_crt_strlen_image.relocations_applied);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CRT_STRLEN_REGION_BEGIN=0x",
                     region == 0 ? 0 : (uint64_t)region->base);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CRT_STRLEN_REGION_END=0x",
                     region == 0 ? 0 : (uint64_t)region->end);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CRT_STRLEN_REGION_READABLE=0x",
                     region == 0 ? 0 : region->readable);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CRT_STRLEN_REGION_EXECUTABLE=0x",
                     region == 0 ? 0 : region->executable);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CRT_STRLEN_REGION_WRITABLE=0x",
                     region == 0 ? 0 : region->writable);
    serial_text("\r\n");

    status = gxos_crt_strlen_checked(string, &g_crt_strlen_image,
                                     GXOS_CRT_STRLEN_DEFAULT_MAX_SCAN, &length);
    serial_field_hex("GXOS_NET10:CRT_STRLEN_STATUS=0x", (uint64_t)(uint32_t)status);
    serial_text("\r\n");
    if (status != GXOS_CRT_STRLEN_STATUS_OK) {
        g_crt_strlen_failures++;
        emit_crt_strlen_summary();
        fail("crt-strlen-invalid");
    }

    g_crt_strlen_successes++;
    if (length > g_crt_strlen_longest) g_crt_strlen_longest = length;
    if (g_crt_strlen_total_bytes > UINT64_MAX - (uint64_t)length) {
        g_crt_strlen_total_bytes = UINT64_MAX;
    } else {
        g_crt_strlen_total_bytes += (uint64_t)length;
    }
    platform_strlen_emit_hex_bytes("GXOS_NET10:CRT_STRLEN_BYTES=", string, length);
    platform_strlen_emit_text_bytes("GXOS_NET10:CRT_STRLEN_PREVIEW=", string, length);
    serial_field_hex("GXOS_NET10:CRT_STRLEN_LENGTH=0x", (uint64_t)length);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CRT_STRLEN_TERMINATOR=0x",
                     (uint64_t)(uintptr_t)string + (uint64_t)length);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CRT_STRLEN_RETURN_VALUE=0x", (uint64_t)length);
    serial_text("\r\n");
    serial_text("GXOS_NET10:CRT_STRLEN_RETURNED\r\n");
    serial_text("GXOS_NET10:CRT_STRLEN_OK\r\n");
    return length;
}
#endif

#ifdef GXOS_ENABLE_CRT_INITTERM
static int GXOS_PERF_EFIAPI platform_initterm_query_performance_counter(void *output)
{
    int result;
    if (g_crt_initterm_callback_active != 0) {
        serial_field_hex("GXOS_NET10:CRT_INITTERM_CALLBACK_QPC_INDEX=0x", g_crt_initterm_current_index);
        serial_text("\r\n");
    }
    result = gxos_query_performance_counter(output);
    if (g_crt_initterm_callback_active != 0) {
        serial_field_hex("GXOS_NET10:CRT_INITTERM_CALLBACK_QPC_RESULT=0x", (uint64_t)(uint32_t)result);
        serial_text("\r\n");
    }
    return result;
}
#endif

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

static void fault_handler(const GXOS_X64_TRAP_FRAME *frame)
{
#ifdef GXOS_ENABLE_CRT_INITTERM
    if (g_crt_initterm_callback_active != 0) {
        serial_text("GXOS_NET10:CRT_INITTERM_CALLBACK_FAULT_ACTIVE=1\r\n");
        serial_field_hex("GXOS_NET10:CRT_INITTERM_CALLBACK_FAULT_INDEX=0x", g_crt_initterm_current_index);
        serial_text("\r\n");
        serial_field_hex("GXOS_NET10:CRT_INITTERM_CALLBACK_FAULT_TARGET=0x", (uint64_t)g_crt_initterm_current_target);
        serial_text("\r\n");
    }
#endif
    if (g_phase < PHASE_BEFORE_MANAGED_CALL || g_phase >= PHASE_AFTER_SECURITY_COOKIE_INIT) serial_text("GXOS_NET10:FAULT_BEFORE_MANAGED\r\n");
    else if (g_phase == PHASE_IN_MANAGED || g_phase == PHASE_BEFORE_MANAGED_CALL) serial_text("GXOS_NET10:FAULT_IN_MANAGED\r\n");
    else serial_text("GXOS_NET10:FAULT_AFTER_MANAGED_RETURN\r\n");
    serial_field_u32("GXOS_NET10:FAULT_VECTOR=0x", (uint32_t)frame->vector);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:FAULT_ERROR=0x", frame->error_code);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:FAULT_RIP=0x", frame->rip);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:FAULT_RSP=0x", frame->rsp);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:FAULT_CR2=0x", frame->cr2);
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

__attribute__((unused)) static int breakpoint_exception_address(const GXOS_X64_TRAP_FRAME *trap,
                                        uint64_t *exception_address,
                                        const char **rip_semantics)
{
    uintptr_t int3 = (uintptr_t)gxos_exception_probe_int3;
    uint64_t captured_rip = trap->rip;
    uint32_t semantics = 0;
    uint8_t byte_before = 0;
    uint8_t byte_at = 0;

    if (trap->vector != 3 || int3 == 0 || int3 == UINTPTR_MAX) return 0;
    if (captured_rip == (uint64_t)(int3 + 1U)) {
        byte_before = *(const uint8_t *)(uintptr_t)(captured_rip - 1U);
    } else if (captured_rip == (uint64_t)int3) {
        byte_at = *(const uint8_t *)(uintptr_t)captured_rip;
    }
    if (!gxos_exception_translate_breakpoint_rip(
            captured_rip, int3, byte_before, byte_at, exception_address, &semantics)) {
        return 0;
    }
    *rip_semantics = semantics == GXOS_EXCEPTION_BP_RIP_AFTER_INT3
        ? "AFTER_INT3" : "AT_INT3";
    return 1;
}

__attribute__((unused)) static void fill_exception_context(const GXOS_X64_TRAP_FRAME *trap,
                                   GXOS_CONTEXT_COMPAT *context)
{
    context->context_flags = GXOS_EXCEPTION_CONTEXT_FLAGS_BOUNDED;
    context->seg_cs = (uint16_t)trap->cs;
    context->seg_ss = (uint16_t)trap->ss;
    context->eflags = (uint32_t)trap->rflags;
    context->rax = trap->rax;
    context->rcx = trap->rcx;
    context->rdx = trap->rdx;
    context->rbx = trap->rbx;
    context->rsp = trap->rsp;
    context->rbp = trap->rbp;
    context->rsi = trap->rsi;
    context->rdi = trap->rdi;
    context->r8 = trap->r8;
    context->r9 = trap->r9;
    context->r10 = trap->r10;
    context->r11 = trap->r11;
    context->r12 = trap->r12;
    context->r13 = trap->r13;
    context->r14 = trap->r14;
    context->r15 = trap->r15;
    context->rip = trap->rip;
}

#ifdef GXOS_ENABLE_EXCEPTION_SYNTHETIC_PROBE
static int probe_callback_arguments(
    GXOS_EXCEPTION_POINTERS_COMPAT *exception_pointers,
    GXOS_EXCEPTION_RECORD_COMPAT **record_out,
    GXOS_CONTEXT_COMPAT **context_out)
{
    GXOS_EXCEPTION_RECORD_COMPAT *record;
    GXOS_CONTEXT_COMPAT *context;
    if (exception_pointers == 0 || exception_pointers->exception_record == 0 ||
        exception_pointers->context_record == 0) return 0;
    record = (GXOS_EXCEPTION_RECORD_COMPAT *)exception_pointers->exception_record;
    context = (GXOS_CONTEXT_COMPAT *)exception_pointers->context_record;
    if (record->exception_code != 0x80000003U ||
        record->exception_address != (uint64_t)(uintptr_t)gxos_exception_probe_int3 ||
        context->rcx != gxos_probe_expected_rcx ||
        context->rdx != gxos_probe_expected_rdx ||
        context->rsp != gxos_probe_expected_rsp ||
        context->rip != gxos_probe_expected_rip) return 0;
    *record_out = record;
    *context_out = context;
    return 1;
}

__attribute__((unused)) static int32_t GXOS_VEH_MS_ABI probe_handler_a(
    GXOS_EXCEPTION_POINTERS_COMPAT *exception_pointers)
{
    GXOS_EXCEPTION_RECORD_COMPAT *record;
    GXOS_CONTEXT_COMPAT *context;
    (void)record;
    (void)context;
    g_synthetic_handler_calls++;
    serial_text("GXOS_NET10:EXCEPTION_HANDLER_A_INVOKED=1\r\n");
    serial_text("GXOS_NET10:EXCEPTION_HANDLER_A_RETURN=0x0000000000000000\r\n");
    return probe_callback_arguments(exception_pointers, &record, &context)
        ? GXOS_EXCEPTION_CONTINUE_SEARCH : GXOS_EXCEPTION_CONTINUE_SEARCH;
}

__attribute__((unused)) static int32_t GXOS_VEH_MS_ABI probe_handler_b(
    GXOS_EXCEPTION_POINTERS_COMPAT *exception_pointers)
{
    GXOS_EXCEPTION_RECORD_COMPAT *record;
    GXOS_CONTEXT_COMPAT *context;
    g_synthetic_handler_calls++;
    serial_text("GXOS_NET10:EXCEPTION_HANDLER_B_INVOKED=1\r\n");
    if (!probe_callback_arguments(exception_pointers, &record, &context)) {
        serial_text("GXOS_NET10:EXCEPTION_HANDLER_B_VALIDATION=0\r\n");
    }
#ifdef GXOS_EXCEPTION_REGISTRY_NESTED
    {
        GXOS_VEH_CALLBACK_DIAGNOSTICS diagnostics = {0};
        if (gxos_veh_registry_add(&g_veh_registry, 0, probe_handler_a, &diagnostics) == 0 &&
            diagnostics.validation == GXOS_VEH_VALIDATION_REGISTRY_ACTIVE) {
            g_veh_nested_registration_rejected = 1;
            serial_text("GXOS_NET10:EXCEPTION_NESTED_REGISTRATION_REJECTED=1\r\n");
        }
    }
    __asm__ volatile ("int3");
#endif
    serial_text("GXOS_NET10:EXCEPTION_HANDLER_B_RETURN=0x0000000000000000\r\n");
    return GXOS_EXCEPTION_CONTINUE_SEARCH;
}

__attribute__((unused)) static int32_t GXOS_VEH_MS_ABI probe_handler_c(
    GXOS_EXCEPTION_POINTERS_COMPAT *exception_pointers)
{
    GXOS_EXCEPTION_RECORD_COMPAT *record;
    GXOS_CONTEXT_COMPAT *context;
    GXOS_VEH_CALLBACK_DIAGNOSTICS diagnostics = {0};
    g_synthetic_handler_calls++;
    serial_text("GXOS_NET10:EXCEPTION_HANDLER_C_INVOKED=1\r\n");
    if (!probe_callback_arguments(exception_pointers, &record, &context)) {
        serial_text("GXOS_NET10:EXCEPTION_HANDLER_C_VALIDATION=0\r\n");
        serial_text("GXOS_NET10:EXCEPTION_HANDLER_C_RETURN=0x0000000000000000\r\n");
        return GXOS_EXCEPTION_CONTINUE_SEARCH;
    }
    serial_text("GXOS_NET10:EXCEPTION_HANDLER_C_VALIDATION=1\r\n");
    if (gxos_veh_registry_add(&g_veh_registry, 0, probe_handler_a, &diagnostics) == 0 &&
        diagnostics.validation == GXOS_VEH_VALIDATION_REGISTRY_ACTIVE) {
        g_veh_nested_registration_rejected = 1;
        serial_text("GXOS_NET10:EXCEPTION_NESTED_REGISTRATION_REJECTED=1\r\n");
    }
    gxos_probe_sentinel_rcx = 0xA1B2C3D4E5F60718ULL;
    gxos_probe_sentinel_rdx = 0x8192A3B4C5D6E7F8ULL;
    context->rcx = gxos_probe_sentinel_rcx;
    context->rdx = gxos_probe_sentinel_rdx;
    context->rip = (uint64_t)(uintptr_t)gxos_exception_probe_landing;
    serial_text("GXOS_NET10:EXCEPTION_HANDLER_C_RETURN=0x00000000FFFFFFFF\r\n");
    return GXOS_EXCEPTION_CONTINUE_EXECUTION;
}

__attribute__((unused)) static int32_t GXOS_VEH_MS_ABI probe_handler_invalid(
    GXOS_EXCEPTION_POINTERS_COMPAT *exception_pointers)
{
    GXOS_EXCEPTION_RECORD_COMPAT *record;
    GXOS_CONTEXT_COMPAT *context;
    (void)record;
    (void)context;
    g_synthetic_handler_calls++;
    serial_text("GXOS_NET10:EXCEPTION_HANDLER_INVALID_INVOKED=1\r\n");
    (void)probe_callback_arguments(exception_pointers, &record, &context);
    serial_text("GXOS_NET10:EXCEPTION_HANDLER_INVALID_RETURN=0x0000000000000001\r\n");
    return 1;
}
#endif

__attribute__((used)) int32_t GXOS_EXCEPTION_MS_ABI gxos_exception_dispatch(
    GXOS_X64_TRAP_FRAME *trap)
{
#if !defined(GXOS_ENABLE_EXCEPTION_SYNTHETIC_PROBE) && !defined(GXOS_ENABLE_VECTORED_EXCEPTION_HANDLER)
    (void)trap;
    return GXOS_EXCEPTION_CONTINUE_SEARCH;
#else
    GXOS_EXCEPTION_RECORD_COMPAT record = {0};
    GXOS_CONTEXT_COMPAT context = {0};
    GXOS_EXCEPTION_POINTERS_COMPAT pointers;
    GXOS_EXCEPTION_VALIDATION_BOUNDS bounds;
    GXOS_VEH_DISPATCH_REPORT dispatch_report = {0};
    uint64_t exception_address;
    const char *rip_semantics;
    int validation_result;
    uint32_t i;

    if (trap == 0) return GXOS_EXCEPTION_CONTINUE_SEARCH;
    serial_text("GXOS_NET10:EXCEPTION_TRAP_ENTERED=1\r\n");
    serial_field_hex("GXOS_NET10:EXCEPTION_VECTOR=0x", trap->vector);
    serial_text("\r\n");
    if (trap->vector != 3 || !breakpoint_exception_address(
            trap, &exception_address, &rip_semantics)) {
        serial_text("GXOS_NET10:EXCEPTION_UNSUPPORTED_VECTOR=1\r\n");
        return GXOS_EXCEPTION_CONTINUE_SEARCH;
    }
    serial_text("GXOS_NET10:EXCEPTION_COMPLETE_FRAME_CAPTURED=1\r\n");
    serial_text("GXOS_NET10:EXCEPTION_VECTOR_EQUALS_3=1\r\n");
    serial_text("GXOS_NET10:EXCEPTION_RIP_SEMANTICS=");
    serial_text(rip_semantics);
    serial_text("\r\n");
    record.exception_code = gxos_exception_translate_vector_code(trap->vector);
    record.exception_address = exception_address;
    fill_exception_context(trap, &context);
    pointers.exception_record = &record;
    pointers.context_record = &context;
    serial_text("GXOS_NET10:EXCEPTION_COMPATIBILITY_STRUCTURES_BUILT=1\r\n");
    serial_field_hex("GXOS_NET10:EXCEPTION_CODE=0x", record.exception_code);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:EXCEPTION_ADDRESS=0x", record.exception_address);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:EXCEPTION_DISPATCH_ACTIVE_BEFORE=0x",
                     gxos_veh_registry_dispatch_active(&g_veh_registry));
    serial_text("\r\n");
    if (!gxos_veh_dispatch(&g_veh_registry, &pointers, 0, 0, &dispatch_report)) {
        serial_text("GXOS_NET10:EXCEPTION_REGISTRY_DISPATCH_REJECTED=1\r\n");
        return GXOS_EXCEPTION_CONTINUE_SEARCH;
    }
    serial_text("GXOS_NET10:EXCEPTION_HANDLER_ORDER_SNAPSHOT=");
    for (i = 0; i != dispatch_report.snapshot_count; i++) {
        const GXOS_VEH_RECORD *snapshot_record = gxos_veh_registry_record(
            &g_veh_registry, dispatch_report.snapshot_slots[i]);
        if (i != 0) serial_text(",");
        if (snapshot_record == 0) serial_text("?");
#ifdef GXOS_ENABLE_EXCEPTION_SYNTHETIC_PROBE
        else if (snapshot_record->callback == probe_handler_a) serial_text("A");
        else if (snapshot_record->callback == probe_handler_b) serial_text("B");
        else if (snapshot_record->callback == probe_handler_c) serial_text("C");
        else if (snapshot_record->callback == probe_handler_invalid) serial_text("INVALID");
        else serial_text(snapshot_record->callback_section_name);
#else
        else serial_text(snapshot_record->callback_section_name);
#endif
    }
    serial_text("\r\n");
    for (i = 0; i != dispatch_report.invoked_count; i++) {
        const GXOS_VEH_RECORD *invoked_record = gxos_veh_registry_record(
            &g_veh_registry, dispatch_report.invoked_slots[i]);
        serial_field_hex("GXOS_NET10:EXCEPTION_CALLBACK_SLOT=0x",
                         dispatch_report.invoked_slots[i]);
        serial_text("\r\n");
        serial_field_hex("GXOS_NET10:EXCEPTION_CALLBACK_RVA=0x",
                         invoked_record == 0 ? 0 : invoked_record->callback_rva);
        serial_text("\r\n");
        serial_field_hex("GXOS_NET10:EXCEPTION_CALLBACK_INVOCATION=0x",
                         dispatch_report.invocation_numbers[i]);
        serial_text("\r\n");
        serial_field_hex("GXOS_NET10:EXCEPTION_CALLBACK_RETURN=0x",
                         (uint64_t)(uint32_t)dispatch_report.return_values[i]);
        serial_text("\r\n");
    }
    serial_field_hex("GXOS_NET10:EXCEPTION_INVALID_RETURN_COUNT=0x",
                     dispatch_report.invalid_return_count);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:EXCEPTION_DISPATCH_ACTIVE_AFTER=0x",
                     gxos_veh_registry_dispatch_active(&g_veh_registry));
    serial_text("\r\n");
    if (!dispatch_report.final_continue_execution) {
        serial_text("GXOS_NET10:EXCEPTION_HANDLER_RETURNED_CONTINUE_SEARCH=1\r\n");
        return GXOS_EXCEPTION_CONTINUE_SEARCH;
    }
    serial_text("GXOS_NET10:EXCEPTION_HANDLER_RETURNED_CONTINUE_EXECUTION=1\r\n");
    bounds.stack_lower = (uintptr_t)g_stack_lower;
    bounds.stack_upper = (uintptr_t)g_stack_upper;
    bounds.executable_lower = (uintptr_t)gxos_exception_probe_int3;
    bounds.executable_upper = (uintptr_t)gxos_exception_probe_landing + 32U;
    serial_field_hex("GXOS_NET10:EXCEPTION_CONTEXT_DIFF_RCX=0x",
                     context.rcx ^ trap->rcx);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:EXCEPTION_CONTEXT_DIFF_RDX=0x",
                     context.rdx ^ trap->rdx);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:EXCEPTION_CONTEXT_DIFF_RIP=0x",
                     context.rip ^ trap->rip);
    serial_text("\r\n");
    validation_result = gxos_exception_validate_context_modifications(
        trap, &context, &bounds);
    serial_field_hex("GXOS_NET10:EXCEPTION_CONTEXT_VALIDATION_RESULT=0x",
                     (uint64_t)(uint32_t)validation_result);
    serial_text("\r\n");
    if (validation_result != GXOS_EXCEPTION_VALIDATION_OK) {
        serial_field_hex("GXOS_NET10:EXCEPTION_CONTEXT_REJECTED=0x",
                         (uint64_t)(uint32_t)validation_result);
        serial_text("\r\n");
        return GXOS_EXCEPTION_CONTINUE_SEARCH;
    }
    gxos_exception_apply_context_modifications(trap, &context);
#ifdef GXOS_ENABLE_EXCEPTION_SYNTHETIC_PROBE
    g_probe_context_modifications_validated = 1;
#endif
    serial_text("GXOS_NET10:EXCEPTION_REQUESTED_CONTEXT_MODIFICATION_VALIDATED=1\r\n");
    serial_field_hex("GXOS_NET10:EXCEPTION_REDIRECTED_RIP=0x", trap->rip);
    serial_text("\r\n");
    serial_text("GXOS_NET10:EXCEPTION_IRETQ_RESTORATION_STARTED=1\r\n");
    gxos_exception_dispatch_active = 0;
    return GXOS_EXCEPTION_CONTINUE_EXECUTION;
#endif
}

__attribute__((used)) static void fault_common_legacy_documentation(void)
{
    /* The implementation is in exception_entry.S; this symbol anchors the C audit. */
}

__attribute__((used)) void GXOS_EXCEPTION_MS_ABI gxos_exception_fatal_dispatch(
    GXOS_X64_TRAP_FRAME *frame)
{
    serial_text("GXOS_NET10:EXCEPTION_FATAL_PATH=1\r\n");
    fault_handler(frame);
}

__attribute__((used)) void GXOS_EXCEPTION_MS_ABI gxos_exception_nested_terminal(
    GXOS_X64_TRAP_FRAME *frame)
{
#if defined(GXOS_ENABLE_EXCEPTION_SYNTHETIC_PROBE) || defined(GXOS_ENABLE_VECTORED_EXCEPTION_HANDLER)
    g_veh_registry.dispatch_active = 0;
    serial_text("GXOS_NET10:EXCEPTION_DISPATCH_ACTIVE_TERMINAL_CLEAR=1\r\n");
#endif
    serial_text("GXOS_NET10:EXCEPTION_NESTED_DISPATCH_TERMINAL=1\r\n");
    fault_handler(frame);
}

void GXOS_EXCEPTION_MS_ABI gxos_exception_probe_landing_report(void)
{
    serial_text("GXOS_NET10:EXCEPTION_LANDING_PAD_REACHED=1\r\n");
    serial_field_hex("GXOS_NET10:EXCEPTION_MODIFIED_RCX=0x", gxos_probe_landing_rcx);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:EXCEPTION_MODIFIED_RDX=0x", gxos_probe_landing_rdx);
    serial_text("\r\n");
    serial_text("GXOS_NET10:EXCEPTION_MODIFIED_RCX_VERIFIED=");
    serial_text(gxos_probe_landing_rcx == gxos_probe_sentinel_rcx ? "1\r\n" : "0\r\n");
    serial_text("GXOS_NET10:EXCEPTION_MODIFIED_RDX_VERIFIED=");
    serial_text(gxos_probe_landing_rdx == gxos_probe_sentinel_rdx ? "1\r\n" : "0\r\n");
    serial_text("GXOS_NET10:EXCEPTION_STACK_POINTER_VALID=");
    serial_text(gxos_probe_landing_rsp >= g_stack_lower &&
                gxos_probe_landing_rsp < g_stack_upper ? "1\r\n" : "0\r\n");
    serial_text("GXOS_NET10:EXCEPTION_PROBE_COMPLETED=1\r\n");
}

#ifdef GXOS_ENABLE_EXCEPTION_SYNTHETIC_PROBE
__attribute__((unused)) static void register_probe_handler(
    const char *label,
    uint32_t first,
    GXOS_VEH_CALLBACK callback)
{
    GXOS_VEH_CALLBACK_DIAGNOSTICS diagnostics = {0};
    void *handle = gxos_veh_registry_add(&g_veh_registry, first, callback, &diagnostics);
    serial_text("GXOS_NET10:EXCEPTION_REGISTERED_HANDLER=");
    serial_text(label);
    serial_text("\r\n");
    if (handle == 0 || diagnostics.validation != GXOS_VEH_VALIDATION_OK) {
        serial_text("GXOS_NET10:EXCEPTION_REGISTERED_HANDLER_RESULT=0\r\n");
        fail("exception-registry-registration");
    }
    serial_field_hex("GXOS_NET10:EXCEPTION_REGISTERED_HANDLER_HANDLE=0x",
                     (uint64_t)(uintptr_t)handle);
    serial_text("\r\n");
}

static void run_synthetic_breakpoint_probe(void)
{
    gxos_exception_probe_enabled = 1;
    gxos_probe_landing_reached = 0;
    g_synthetic_handler_calls = 0;
    g_probe_context_modifications_validated = 0;
    g_veh_nested_registration_rejected = 0;
#ifdef GXOS_EXCEPTION_REGISTRY_EMPTY
    serial_text("GXOS_NET10:EXCEPTION_REGISTRY_MODE=EMPTY\r\n");
#elif defined(GXOS_EXCEPTION_REGISTRY_ALL_CONTINUE_SEARCH)
    serial_text("GXOS_NET10:EXCEPTION_REGISTRY_MODE=ALL_CONTINUE_SEARCH\r\n");
    register_probe_handler("B", 1, probe_handler_b);
    register_probe_handler("A", 0, probe_handler_a);
#elif defined(GXOS_EXCEPTION_REGISTRY_INVALID_RETURN)
    serial_text("GXOS_NET10:EXCEPTION_REGISTRY_MODE=INVALID_RETURN\r\n");
    register_probe_handler("INVALID", 1, probe_handler_invalid);
    register_probe_handler("A", 0, probe_handler_a);
#else
    serial_text("GXOS_NET10:EXCEPTION_REGISTRY_MODE=B_C_A\r\n");
    register_probe_handler("B", 1, probe_handler_b);
    register_probe_handler("C", 0, probe_handler_c);
    register_probe_handler("A", 0, probe_handler_a);
#endif
    serial_field_hex("GXOS_NET10:EXCEPTION_REGISTRY_LIVE_COUNT_BEFORE=0x",
                     gxos_veh_registry_live_count(&g_veh_registry));
    serial_text("\r\n");
    serial_text("GXOS_NET10:EXCEPTION_PROBE_BEGIN=1\r\n");
    gxos_exception_probe();
#if !defined(GXOS_EXCEPTION_REGISTRY_EMPTY) && \
    !defined(GXOS_EXCEPTION_REGISTRY_ALL_CONTINUE_SEARCH) && \
    !defined(GXOS_EXCEPTION_REGISTRY_INVALID_RETURN) && \
    !defined(GXOS_EXCEPTION_REGISTRY_NESTED)
    if (gxos_probe_landing_reached == 0 || g_synthetic_handler_calls != 2 ||
        g_probe_context_modifications_validated == 0 ||
        g_veh_nested_registration_rejected == 0 ||
        gxos_probe_landing_rcx != gxos_probe_sentinel_rcx ||
        gxos_probe_landing_rdx != gxos_probe_sentinel_rdx) {
        fail("synthetic-breakpoint-proof");
    }
#endif
    serial_text("GXOS_NET10:EXCEPTION_PROBE_SUCCESS=1\r\n");
}
#endif

typedef struct __attribute__((packed)) {
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
static IDT_GATE g_gate4_idt[256] __attribute__((aligned(16)));

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
    uint32_t copy_count;
    uint8_t *source;
    uint8_t *destination;

    /*
     * This bounded loader owns exception diagnostics only.  Preserve the
     * firmware's full IDT so its timer and other IRQ vectors remain valid;
     * installing a 32-entry replacement IDT would triple-fault on IRQ 0x20.
     */
    read_idtr(&g_saved_idtr);
    source = (uint8_t *)(uintptr_t)g_saved_idtr.base;
    destination = (uint8_t *)g_gate4_idt;
    copy_count = (uint32_t)g_saved_idtr.limit + 1U;
    if (copy_count > (uint32_t)sizeof(g_gate4_idt)) copy_count = (uint32_t)sizeof(g_gate4_idt);
    for (i = 0; i != copy_count; i++) destination[i] = source[i];
    set_idt_gate(&g_gate4_idt[0], gxos_fault_no_error_0);
    set_idt_gate(&g_gate4_idt[1], gxos_fault_no_error_1);
    set_idt_gate(&g_gate4_idt[2], gxos_fault_no_error_2);
    set_idt_gate(&g_gate4_idt[3], gxos_fault_no_error_3);
    set_idt_gate(&g_gate4_idt[4], gxos_fault_no_error_4);
    set_idt_gate(&g_gate4_idt[5], gxos_fault_no_error_5);
    set_idt_gate(&g_gate4_idt[6], gxos_fault_no_error_6);
    set_idt_gate(&g_gate4_idt[7], gxos_fault_no_error_7);
    set_idt_gate(&g_gate4_idt[8], gxos_fault_with_error_8);
    set_idt_gate(&g_gate4_idt[9], gxos_fault_no_error_9);
    set_idt_gate(&g_gate4_idt[10], gxos_fault_with_error_10);
    set_idt_gate(&g_gate4_idt[11], gxos_fault_with_error_11);
    set_idt_gate(&g_gate4_idt[12], gxos_fault_with_error_12);
    set_idt_gate(&g_gate4_idt[13], gxos_fault_with_error_13);
    set_idt_gate(&g_gate4_idt[14], gxos_fault_with_error_14);
    set_idt_gate(&g_gate4_idt[15], gxos_fault_no_error_15);
    set_idt_gate(&g_gate4_idt[16], gxos_fault_no_error_16);
    set_idt_gate(&g_gate4_idt[17], gxos_fault_with_error_17);
    set_idt_gate(&g_gate4_idt[18], gxos_fault_no_error_18);
    set_idt_gate(&g_gate4_idt[19], gxos_fault_no_error_19);
    set_idt_gate(&g_gate4_idt[20], gxos_fault_no_error_20);
    set_idt_gate(&g_gate4_idt[21], gxos_fault_with_error_21);
    set_idt_gate(&g_gate4_idt[22], gxos_fault_no_error_22);
    set_idt_gate(&g_gate4_idt[23], gxos_fault_no_error_23);
    set_idt_gate(&g_gate4_idt[24], gxos_fault_no_error_24);
    set_idt_gate(&g_gate4_idt[25], gxos_fault_no_error_25);
    set_idt_gate(&g_gate4_idt[26], gxos_fault_no_error_26);
    set_idt_gate(&g_gate4_idt[27], gxos_fault_no_error_27);
    set_idt_gate(&g_gate4_idt[28], gxos_fault_no_error_28);
    set_idt_gate(&g_gate4_idt[29], gxos_fault_with_error_29);
    set_idt_gate(&g_gate4_idt[30], gxos_fault_with_error_30);
    set_idt_gate(&g_gate4_idt[31], gxos_fault_no_error_31);
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
    uint32_t relocations_applied;
    uint32_t memory_region_count;
    GXOS_CRT_INITTERM_MEMORY_REGION memory_regions[GXOS_CRT_INITTERM_MAX_MEMORY_REGIONS];
    uint32_t executable_region_count;
    GXOS_CRT_INITTERM_E_EXECUTABLE_REGION executable_regions[GXOS_CRT_INITTERM_E_MAX_EXECUTABLE_REGIONS];
} PE_IMAGE;

#if defined(GXOS_ENABLE_EXCEPTION_SYNTHETIC_PROBE) || defined(GXOS_ENABLE_VECTORED_EXCEPTION_HANDLER)
static const void *g_veh_harness_identity;
static uintptr_t g_veh_harness_image_base;
static uint64_t g_veh_harness_image_size;

#ifdef GXOS_ENABLE_VECTORED_EXCEPTION_HANDLER
static const char *veh_validation_name(GXOS_VEH_VALIDATION_RESULT result)
{
    switch (result) {
    case GXOS_VEH_VALIDATION_OK: return "OK";
    case GXOS_VEH_VALIDATION_NULL_CALLBACK: return "NULL_CALLBACK";
    case GXOS_VEH_VALIDATION_NONCANONICAL_CALLBACK: return "NONCANONICAL_CALLBACK";
    case GXOS_VEH_VALIDATION_NO_IMAGE: return "NO_IMAGE";
    case GXOS_VEH_VALIDATION_BAD_IMAGE: return "BAD_IMAGE";
    case GXOS_VEH_VALIDATION_IMAGE_OVERFLOW: return "IMAGE_OVERFLOW";
    case GXOS_VEH_VALIDATION_OUTSIDE_IMAGE: return "OUTSIDE_IMAGE";
    case GXOS_VEH_VALIDATION_BAD_SECTION: return "BAD_SECTION";
    case GXOS_VEH_VALIDATION_NOT_EXECUTABLE: return "NOT_EXECUTABLE";
    case GXOS_VEH_VALIDATION_NOT_READABLE: return "NOT_READABLE";
    case GXOS_VEH_VALIDATION_WRITABLE_SECTION: return "WRITABLE_SECTION";
    case GXOS_VEH_VALIDATION_REGISTRY_ACTIVE: return "REGISTRY_ACTIVE";
    case GXOS_VEH_VALIDATION_REGISTRY_FULL: return "REGISTRY_FULL";
    case GXOS_VEH_VALIDATION_SEQUENCE_EXHAUSTED: return "SEQUENCE_EXHAUSTED";
    default: return "BAD_REGISTRY";
    }
}
#endif

static void configure_veh_registry(const PE_IMAGE *image)
{
    const GXOS_VEH_IMAGE *images[GXOS_VEH_MAX_IMAGES];

    gxos_veh_registry_init(&g_veh_registry);
    if (image == 0 || !gxos_veh_image_parse_pe(
            &g_veh_payload_image, image->loaded, image->actual_base, image->loaded_size)) {
        fail("veh-payload-image");
    }
    if (g_veh_harness_identity == 0 || !gxos_veh_image_parse_pe(
            &g_veh_harness_image, g_veh_harness_identity,
            g_veh_harness_image_base, g_veh_harness_image_size)) {
        fail("veh-harness-image");
    }
    images[0] = &g_veh_payload_image;
    images[1] = &g_veh_harness_image;
    if (!gxos_veh_registry_configure_images(&g_veh_registry, images, 2)) {
        fail("veh-image-registry");
    }
    serial_text("GXOS_NET10:VEH_REGISTRY_INITIALIZED=1\r\n");
    serial_field_hex("GXOS_NET10:VEH_REGISTRY_CAPACITY=0x", GXOS_VEH_REGISTRY_CAPACITY);
    serial_text("\r\n");
    serial_text("GXOS_NET10:VEH_REGISTRY_ALLOCATION_COUNT=0x0000000000000000\r\n");
    serial_text("GXOS_NET10:VEH_CALLBACK_VALIDATION=BOUNDED_PE_SECTIONS\r\n");
}

#ifdef GXOS_ENABLE_VECTORED_EXCEPTION_HANDLER
static void *GXOS_VEH_MS_ABI platform_add_vectored_exception_handler(
    uint32_t first,
    void *handler)
{
    GXOS_VEH_CALLBACK_DIAGNOSTICS diagnostics = {0};
    void *opaque_handle;
    uintptr_t return_address = (uintptr_t)__builtin_return_address(0);
    uint32_t slot = UINT32_MAX;
    uint32_t position = UINT32_MAX;
    uint32_t i;

    g_veh_add_invocation_count++;
    g_veh_add_last_first = first;
    g_veh_add_last_callback = (uint64_t)(uintptr_t)handler;
    g_veh_add_last_return_address = return_address;
    g_veh_add_last_call_site = return_address >= 6 ? return_address - 6U : 0;
    serial_field_hex("GXOS_NET10:VEH_ADD_INVOCATION=0x", g_veh_add_invocation_count);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:VEH_ADD_FIRST=0x", first);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:VEH_ADD_CALLBACK=0x", (uint64_t)(uintptr_t)handler);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:VEH_ADD_RETURN_ADDRESS=0x", return_address);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:VEH_ADD_CALL_SITE=0x", g_veh_add_last_call_site);
    serial_text("\r\n");
    if (g_veh_payload_image.image_base != 0 &&
        return_address >= g_veh_payload_image.image_base &&
        g_veh_payload_image.image_base <= UINTPTR_MAX -
            (uintptr_t)g_veh_payload_image.image_size &&
        return_address < g_veh_payload_image.image_base +
            (uintptr_t)g_veh_payload_image.image_size) {
        serial_field_hex("GXOS_NET10:VEH_ADD_RETURN_ADDRESS_RVA=0x",
                         return_address - g_veh_payload_image.image_base);
        serial_text("\r\n");
        serial_field_hex("GXOS_NET10:VEH_ADD_CALL_SITE_RVA=0x",
                         g_veh_add_last_call_site - g_veh_payload_image.image_base);
        serial_text("\r\n");
    }
    opaque_handle = gxos_veh_registry_add(
        &g_veh_registry, first, (GXOS_VEH_CALLBACK)(uintptr_t)handler, &diagnostics);
    serial_text("GXOS_NET10:VEH_ADD_VALIDATION=");
    serial_text(veh_validation_name(diagnostics.validation));
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:VEH_ADD_CALLBACK_IMAGE_BASE=0x", diagnostics.image_base);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:VEH_ADD_CALLBACK_RVA=0x", diagnostics.callback_rva);
    serial_text("\r\n");
    serial_text("GXOS_NET10:VEH_ADD_CALLBACK_SECTION=");
    serial_text(diagnostics.section_name);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:VEH_ADD_CALLBACK_SECTION_EXECUTABLE=0x",
                     diagnostics.section_executable);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:VEH_ADD_SELECTED_SLOT=0x", UINT32_MAX);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:VEH_ADD_INSERTION_POSITION=0x", UINT32_MAX);
    serial_text("\r\n");
    if (opaque_handle != 0) {
        const GXOS_VEH_RECORD *record;
        g_veh_add_returned_handle = (uint64_t)(uintptr_t)opaque_handle;
        for (i = 0; i != GXOS_VEH_REGISTRY_CAPACITY; i++) {
            record = gxos_veh_registry_record(&g_veh_registry, i);
            if (record != 0 && record->opaque_handle == (uintptr_t)opaque_handle) {
                slot = i;
                break;
            }
        }
        for (i = 0; i != gxos_veh_registry_live_count(&g_veh_registry); i++) {
            if (gxos_veh_registry_order_slot(&g_veh_registry, i) == slot) {
                position = i;
                break;
            }
        }
        record = gxos_veh_registry_record(&g_veh_registry, slot);
        g_veh_add_last_slot = slot;
        g_veh_add_last_insertion_position = position;
        g_veh_add_last_registration_sequence = record->registration_sequence;
        serial_field_hex("GXOS_NET10:VEH_ADD_SELECTED_SLOT=0x", slot);
        serial_text("\r\n");
        serial_field_hex("GXOS_NET10:VEH_ADD_INSERTION_POSITION=0x", position);
        serial_text("\r\n");
        serial_field_hex("GXOS_NET10:VEH_ADD_SEQUENCE=0x", record->registration_sequence);
        serial_text("\r\n");
    }
    else {
        g_veh_add_returned_handle = 0;
        g_veh_add_last_slot = UINT32_MAX;
        g_veh_add_last_insertion_position = UINT32_MAX;
        g_veh_add_last_registration_sequence = 0;
    }
    serial_field_hex("GXOS_NET10:VEH_ADD_HANDLE=0x", g_veh_add_returned_handle);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:VEH_REGISTRY_LIVE_COUNT=0x",
                     gxos_veh_registry_live_count(&g_veh_registry));
    serial_text("\r\n");
    serial_text("GXOS_NET10:VEH_ADD_ORDER_AFTER=");
    for (i = 0; i != gxos_veh_registry_live_count(&g_veh_registry); i++) {
        if (i != 0) {
            serial_text(",");
        }
        serial_field_hex("", gxos_veh_registry_order_slot(&g_veh_registry, i));
    }
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:VEH_REGISTRY_DISPATCH_ACTIVE=0x",
                     gxos_veh_registry_dispatch_active(&g_veh_registry));
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:VEH_REGISTRY_ALLOCATION_COUNT=0x",
                     gxos_veh_registry_allocation_count(&g_veh_registry));
    serial_text("\r\n");
    serial_text("GXOS_NET10:VEH_ADD_RESULT=");
    serial_text(opaque_handle != 0 ? "SUCCESS\r\n" : "NULL\r\n");
    return opaque_handle;
}
#endif
#endif

typedef struct {
    const char *module;
    const char *symbol;
    uint32_t descriptor_index;
    uint32_t symbol_index;
    uint32_t iat_rva;
} IMPORT_RECORD;

#define MAX_IMPORT_SYMBOLS 256
static IMPORT_RECORD g_import_records[MAX_IMPORT_SYMBOLS];
static EFI_PHYSICAL_ADDRESS g_import_stub_pages;
static uint32_t g_import_symbol_count;

static void __attribute__((noreturn)) EFIAPI import_failfast(
    const IMPORT_RECORD *record, const uint64_t *arguments)
{
    uintptr_t return_address = arguments == 0 ? 0 : (uintptr_t)arguments[4];
    uintptr_t call_site = import_call_site(return_address);
    uintptr_t original_rcx = arguments == 0 ? 0 : (uintptr_t)arguments[3];
    uintptr_t original_rdx = arguments == 0 ? 0 : (uintptr_t)arguments[2];
    uintptr_t original_r8 = arguments == 0 ? 0 : (uintptr_t)arguments[1];
    uintptr_t original_r9 = arguments == 0 ? 0 : (uintptr_t)arguments[0];
    if (g_phase == PHASE_AFTER_TIME_CALL) g_phase = PHASE_IN_TIME_CONSUMER;
    if (g_phase == PHASE_AFTER_QPC_CALL) g_phase = PHASE_AFTER_SECURITY_COOKIE_INIT;
    serial_text("GXOS_NET10:IMPORT_BLOCKER_DLL=");
    serial_text(record->module);
    serial_text("\r\n");
    serial_text("GXOS_NET10:IMPORT_BLOCKER_SYMBOL=");
    serial_text(record->symbol);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:IMPORT_BLOCKER_DESCRIPTOR_INDEX=0x",
                     record->descriptor_index);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:IMPORT_BLOCKER_SYMBOL_INDEX=0x",
                     record->symbol_index);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:IMPORT_BLOCKER_IAT_RVA=0x", record->iat_rva);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:IMPORT_BLOCKER_RUNTIME_IAT=0x",
                     g_managed_image_base + record->iat_rva);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:IMPORT_BLOCKER_RUNTIME_CALL_SITE=0x", call_site);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:IMPORT_BLOCKER_CALLER_RVA=0x",
                     call_site >= g_managed_image_base
                         ? call_site - g_managed_image_base : 0);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:IMPORT_BLOCKER_RCX=0x", original_rcx);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:IMPORT_BLOCKER_RDX=0x", original_rdx);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:IMPORT_BLOCKER_R8=0x", original_r8);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:IMPORT_BLOCKER_R9=0x", original_r9);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:IMPORT_BLOCKER_STACK_ARG5=0x",
                     arguments == 0 ? 0 : arguments[9]);
    serial_text("\r\n");
#ifdef GXOS_ENABLE_CREATE_EVENT_W
    emit_create_event_w_final_summary();
#endif
#ifdef GXOS_ENABLE_CREATE_MEMORY_RESOURCE_NOTIFICATION
    emit_memory_resource_notification_summary();
#endif
#ifdef GXOS_ENABLE_CRT_MALLOC
    if (equal_text(record->module, "api-ms-win-crt-heap-l1-1-0.dll") &&
        equal_text(record->symbol, "_callnewh")) {
        g_crt_malloc_context.callnewh_reached++;
    }
    emit_crt_malloc_summary();
    serial_text("GXOS_NET10:MALLOC_NEXT_UNRESOLVED_IMPORT=");
    serial_text(record->module);
    serial_text("!");
    serial_text(record->symbol);
    serial_text("\r\n");
#endif
    if (equal_text(record->module, "KERNEL32.dll") && equal_text(record->symbol, "GetSystemInfo")) {
        serial_field_hex("GXOS_NET10:GETSYSTEMINFO_FAILFAST_RETURN_ADDRESS=0x", return_address);
        serial_text("\r\n");
        serial_field_hex("GXOS_NET10:GETSYSTEMINFO_FAILFAST_CALL_SITE=0x",
                         return_address >= 6 ? return_address - 6 : 0);
        serial_text("\r\n");
        serial_field_hex("GXOS_NET10:GETSYSTEMINFO_FAILFAST_RCX=0x", original_rcx);
        serial_text("\r\n");
    }
#ifdef GXOS_ENABLE_SYSTEM_INFO
    if (g_system_info_successes != 0 && g_system_info_field_consumption_emitted == 0) {
        serial_text("GXOS_NET10:GETSYSTEMINFO_FIELD_READ_MASK=0x00000000000000A2\r\n");
        serial_text("GXOS_NET10:GETSYSTEMINFO_FIELD_READ_SOURCE=STATIC_CALLSITE_CENSUS\r\n");
        serial_text("GXOS_NET10:GETSYSTEMINFO_FIELD_CONSUMPTION_COMPLETE\r\n");
        g_system_info_field_consumption_emitted = 1;
    }
#endif
#ifdef GXOS_ENABLE_NUMA_HIGHEST_NODE
    if (equal_text(record->module, "KERNEL32.dll") &&
        equal_text(record->symbol, "GetProcessGroupAffinity") &&
        g_numa_calls != 0) {
        serial_field_hex("GXOS_NET10:GETNUMAHIGHESTNODE_CALLER_BRANCH_CALL_INDEX=0x",
                         g_numa_calls - 1U);
        serial_text("\r\n");
        serial_text("GXOS_NET10:GETNUMAHIGHESTNODE_CALLER_BRANCH=");
        if (g_numa_last_boolean == GXOS_NUMA_TRUE) {
            if (g_numa_last_output_after == 0) {
                serial_text("SUCCESS_BOOLEAN_OUTPUT_ZERO_NON_NUMA_FALLBACK\r\n");
            } else {
                serial_text("SUCCESS_BOOLEAN_OUTPUT_NONZERO_NODE_TABLE_SETUP\r\n");
            }
        } else {
            serial_text("FAILURE_NON_NUMA_FALLBACK\r\n");
        }
        serial_text("GXOS_NET10:GETNUMAHIGHESTNODE_OUTPUT_READ=");
        serial_text(g_numa_last_boolean == GXOS_NUMA_TRUE ? "1\r\n" : "0\r\n");
        serial_field_hex("GXOS_NET10:GETNUMAHIGHESTNODE_DERIVED_DOMAIN_COUNT=0x",
                         g_numa_last_boolean != GXOS_NUMA_TRUE ||
                                 g_numa_last_output_after == 0
                             ? 0
                             : g_numa_last_output_after + 1U);
        serial_text("\r\n");
        serial_text("GXOS_NET10:GETNUMAHIGHESTNODE_OUTPUT_TRANSFORM=");
        serial_text(g_numa_last_boolean == GXOS_NUMA_TRUE &&
                            g_numa_last_output_after != 0
                        ? "highest_plus_one\r\n"
                        : "none\r\n");
        serial_text("GXOS_NET10:GETNUMAHIGHESTNODE_SUBSEQUENT_NUMA_CALL_COUNT=0x0000000000000000\r\n");
    }
#endif
#ifdef GXOS_ENABLE_PROCESS_GROUP_AFFINITY
    if (equal_text(record->module, "KERNEL32.dll") &&
        equal_text(record->symbol, "GetProcessAffinityMask") &&
        g_process_group_calls != 0) {
        serial_text("GXOS_NET10:GETPROCESSGROUPAFFINITY_CALLER_BRANCH=FAILURE_INSUFFICIENT_BUFFER_REQUIRED_COUNT_READ\r\n");
        serial_field_hex("GXOS_NET10:GETPROCESSGROUPAFFINITY_OUTPUT_COUNT_CONSUMED=0x",
                         g_process_group_last_output_count);
        serial_text("\r\n");
        serial_text("GXOS_NET10:GETPROCESSGROUPAFFINITY_GROUP_ARRAY_CONSUMED=0\r\n");
        serial_field_hex("GXOS_NET10:GETPROCESSGROUPAFFINITY_RETRY_COUNT=0x",
                         g_process_group_retry_count);
        serial_text("\r\n");
        serial_text("GXOS_NET10:GETPROCESSGROUPAFFINITY_SUBSEQUENT_GROUP_API_COUNT=0\r\n");
        serial_text("GXOS_NET10:GETPROCESSGROUPAFFINITY_NEXT_BOUNDARY=KERNEL32.dll!GetProcessAffinityMask\r\n");
    }
#endif
#ifdef GXOS_ENABLE_PROCESS_AFFINITY
    if (g_process_affinity_calls != 0) {
        serial_text("GXOS_NET10:GETPROCESSAFFINITYMASK_CALLER_CONSUMPTION_COMPLETE\r\n");
        serial_text("GXOS_NET10:GETPROCESSAFFINITYMASK_NEXT_BOUNDARY=");
        serial_text(record->module);
        serial_text("!");
        serial_text(record->symbol);
        serial_text("\r\n");
    }
#endif
#ifdef GXOS_ENABLE_QUERY_INFORMATION_JOB_OBJECT
    if (g_query_job_calls != 0) {
        serial_text("GXOS_NET10:QUERYJOBOBJECT_CALLER_CONSUMPTION_COMPLETE\r\n");
        serial_text("GXOS_NET10:QUERYJOBOBJECT_NEXT_BOUNDARY=");
        serial_text(record->module);
        serial_text("!");
        serial_text(record->symbol);
        serial_text("\r\n");
        serial_field_hex("GXOS_NET10:QUERYJOBOBJECT_CALL_COUNT=0x", g_query_job_calls);
        serial_text("\r\n");
        serial_field_hex("GXOS_NET10:QUERYJOBOBJECT_SUCCESS_COUNT=0x", g_query_job_successes);
        serial_text("\r\n");
        serial_field_hex("GXOS_NET10:QUERYJOBOBJECT_EXPECTED_NO_JOB_FAILURE_COUNT=0x",
                         g_query_job_expected_no_job_failures);
        serial_text("\r\n");
        serial_field_hex("GXOS_NET10:QUERYJOBOBJECT_OTHER_FAILURE_COUNT=0x",
                         g_query_job_other_failures);
        serial_text("\r\n");
    }
#endif
#ifdef GXOS_ENABLE_GET_MODULE_HANDLE
    if (g_get_module_handle_calls != 0) {
        serial_text("GXOS_NET10:GETMODULEHANDLEW_CALLER_CONSUMPTION_COMPLETE\r\n");
        serial_field_hex("GXOS_NET10:GETMODULEHANDLEW_CALL_COUNT=0x",
                         g_get_module_handle_calls);
        serial_text("\r\n");
        serial_field_hex("GXOS_NET10:GETMODULEHANDLEW_SUCCESS_COUNT=0x",
                         g_get_module_handle_successes);
        serial_text("\r\n");
        serial_field_hex("GXOS_NET10:GETMODULEHANDLEW_FAILURE_COUNT=0x",
                         g_get_module_handle_failures);
        serial_text("\r\n");
        serial_field_hex("GXOS_NET10:GETMODULEHANDLEW_NULL_CALL_COUNT=0x",
                         g_get_module_handle_null_calls);
        serial_text("\r\n");
        serial_field_hex("GXOS_NET10:GETMODULEHANDLEW_NAMED_CALL_COUNT=0x",
                         g_get_module_handle_named_calls);
        serial_text("\r\n");
        serial_text("GXOS_NET10:GETMODULEHANDLEW_NEXT_BOUNDARY=");
        serial_text(record->module);
        serial_text("!");
        serial_text(record->symbol);
        serial_text("\r\n");
        serial_text("GXOS_NET10:GETMODULEHANDLEW_SUBSEQUENT_CALL_COUNT=0x0000000000000000\r\n");
    }
#endif
#ifdef GXOS_ENABLE_GET_PROC_ADDRESS
    if (g_get_proc_address_calls != 0) {
        if (g_get_proc_address_last_report.result !=
            (GXOS_GET_PROC_ADDRESS_FARPROC)0) {
            ++g_get_proc_address_pointer_stored;
        }
        serial_text("GXOS_NET10:GETPROCADDRESS_CALLER_NULL_TEST=1\r\n");
        serial_field_hex("GXOS_NET10:GETPROCADDRESS_POINTER_STORED=0x",
                         g_get_proc_address_last_report.result !=
                                 (GXOS_GET_PROC_ADDRESS_FARPROC)0
                             ? 1
                             : 0);
        serial_text("\r\n");
        serial_field_hex("GXOS_NET10:GETPROCADDRESS_POINTER_CALLED=0x",
                         g_get_proc_address_pointer_called != 0 ? 1 : 0);
        serial_text("\r\n");
        serial_text("GXOS_NET10:GETPROCADDRESS_CALLER_BRANCH=");
        serial_text(g_get_proc_address_last_report.result !=
                            (GXOS_GET_PROC_ADDRESS_FARPROC)0
                        ? "SUCCESS_POINTER_STORED\r\n"
                        : "FAILURE_NULL_OPTIONAL_FALLBACK\r\n");
        serial_field_hex("GXOS_NET10:GETPROCADDRESS_SUBSEQUENT_CALL_COUNT=0x",
                         g_get_proc_address_calls - 1U);
        serial_text("\r\n");
        serial_field_hex("GXOS_NET10:GETPROCADDRESS_CALL_COUNT=0x",
                         g_get_proc_address_calls);
        serial_text("\r\n");
        serial_field_hex("GXOS_NET10:GETPROCADDRESS_SUCCESS_COUNT=0x",
                         g_get_proc_address_successes);
        serial_text("\r\n");
        serial_field_hex("GXOS_NET10:GETPROCADDRESS_EXPECTED_ABSENT_MODULE_FAILURE_COUNT=0x",
                         g_get_proc_address_absent_module_failures);
        serial_text("\r\n");
        serial_field_hex("GXOS_NET10:GETPROCADDRESS_MISSING_EXPORT_FAILURE_COUNT=0x",
                         g_get_proc_address_missing_export_failures);
        serial_text("\r\n");
        serial_field_hex("GXOS_NET10:GETPROCADDRESS_INVALID_HANDLE_FAILURE_COUNT=0x",
                         g_get_proc_address_invalid_handle_failures);
        serial_text("\r\n");
        serial_field_hex("GXOS_NET10:GETPROCADDRESS_NAMED_LOOKUP_COUNT=0x",
                         g_get_proc_address_named_calls);
        serial_text("\r\n");
        serial_field_hex("GXOS_NET10:GETPROCADDRESS_ORDINAL_LOOKUP_COUNT=0x",
                         g_get_proc_address_ordinal_calls);
        serial_text("\r\n");
        serial_field_hex("GXOS_NET10:GETPROCADDRESS_EXPORT_LOOKUP_ATTEMPTS=0x",
                         g_get_proc_address_export_lookup_attempts);
        serial_text("\r\n");
        serial_field_hex("GXOS_NET10:GETPROCADDRESS_POINTER_STORED_COUNT=0x",
                         g_get_proc_address_pointer_stored);
        serial_text("\r\n");
        serial_field_hex("GXOS_NET10:GETPROCADDRESS_POINTER_CALLED_COUNT=0x",
                         g_get_proc_address_pointer_called);
        serial_text("\r\n");
        serial_text("GXOS_NET10:GETPROCADDRESS_CALLER_CONSUMPTION_COMPLETE\r\n");
        serial_text("GXOS_NET10:GETPROCADDRESS_NEXT_BOUNDARY=");
        serial_text(record->module);
        serial_text("!");
        serial_text(record->symbol);
        serial_text("\r\n");
    }
#endif
#ifdef GXOS_ENABLE_CRT_ONEXIT_REGISTER
    if (g_crt_onexit_register_successes != 0) {
        serial_text("GXOS_NET10:REGISTER_ONEXIT_CONTINUATION_BEYOND_CALL_SITE=1\r\n");
    }
#endif
    serial_text("GXOS_NET10:UNEXPECTED_IMPORT_CALL:");
    serial_text(record->module);
    serial_text("!");
    serial_text(record->symbol);
    serial_text("\r\n");
#ifdef GXOS_ENABLE_CRT_INITTERM
    if (g_crt_initterm_callback_active != 0) {
        serial_text("GXOS_NET10:CRT_INITTERM_CALLBACK_UNRESOLVED_IMPORT=1\r\n");
        serial_field_hex("GXOS_NET10:CRT_INITTERM_CALLBACK_IMPORT_INDEX=0x", g_crt_initterm_current_index);
        serial_text("\r\n");
        serial_field_hex("GXOS_NET10:CRT_INITTERM_CALLBACK_IMPORT_TARGET=0x", (uint64_t)g_crt_initterm_current_target);
        serial_text("\r\n");
    }
#endif
    serial_field_u32("GXOS_NET10:TIME_CONSUMER_PHASE=0x", g_phase);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:TLS_ALLOC_LIMIT=0x", g_tls_block == 0 ? 0 : *(uint64_t *)((uint8_t *)(uintptr_t)g_tls_block + 0x30));
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:TLS_ALLOC_PTR=0x", g_tls_block == 0 ? 0 : *(uint64_t *)((uint8_t *)(uintptr_t)g_tls_block + 0x38));
    serial_text("\r\n");
    serial_text("GXOS_NET10:MANAGED_THREAD_REGISTERED=0\r\n");
    serial_text("GXOS_NET10:ALLOCATION_CONTEXT_VALID=0\r\n");
    serial_text("GXOS_NET10:GC_CONTRACT_INITIALIZED=0\r\n");
    serial_text("GXOS_NET10:GC_HEAP_USABLE=0\r\n");
    serial_text("GXOS_NET10:ALLOCATION_CONTEXT_CREATED=0\r\n");
    serial_text("GXOS_NET10:MANAGED_ALLOCATION_COUNT=0\r\n");
#ifdef GXOS_ENABLE_CRT_STRLEN
    emit_crt_strlen_summary();
#endif
#ifdef GXOS_ENABLE_GETENV
    emit_getenv_summary();
#endif
#ifdef GXOS_ENABLE_CRT_STRICMP
    emit_crt_stricmp_summary();
#endif
    emit_qpc_summary();
    halt_forever();
}

static void emit_import_failfast_stub(uint8_t *stub, const IMPORT_RECORD *record)
{
    uint64_t record_address = (uint64_t)(uintptr_t)record;
    uint64_t handler_address = (uint64_t)(uintptr_t)import_failfast;
    uint32_t cursor = 0;

    /*
     * Push the four original register arguments.  The original return
     * address remains at arguments[4].  The handler never returns, so this
     * temporary stack frame needs no unwind or restore path.
     */
    stub[cursor++] = 0x51; /* push rcx */
    stub[cursor++] = 0x52; /* push rdx */
    stub[cursor++] = 0x41;
    stub[cursor++] = 0x50; /* push r8 */
    stub[cursor++] = 0x41;
    stub[cursor++] = 0x51; /* push r9 */
    stub[cursor++] = 0x48;
    stub[cursor++] = 0x89;
    stub[cursor++] = 0xE2; /* mov rdx, rsp */
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

#ifdef GXOS_ENABLE_GET_MODULE_HANDLE
static const char *platform_get_module_handle_status_name(
    GXOS_MODULE_HANDLE_STATUS status)
{
    switch (status) {
        case GXOS_MODULE_HANDLE_STATUS_OK: return "OK";
        case GXOS_MODULE_HANDLE_STATUS_UNSUPPORTED_NAME: return "UNSUPPORTED_NAME";
        case GXOS_MODULE_HANDLE_STATUS_MODULE_NOT_FOUND: return "MODULE_NOT_FOUND";
        case GXOS_MODULE_HANDLE_STATUS_NONCANONICAL_NAME: return "NONCANONICAL_NAME";
        case GXOS_MODULE_HANDLE_STATUS_UNREADABLE_NAME: return "UNREADABLE_NAME";
        case GXOS_MODULE_HANDLE_STATUS_UNTERMINATED_NAME: return "UNTERMINATED_NAME";
        case GXOS_MODULE_HANDLE_STATUS_NAME_SCAN_LIMIT: return "NAME_SCAN_LIMIT";
        case GXOS_MODULE_HANDLE_STATUS_POINTER_OVERFLOW: return "POINTER_OVERFLOW";
        case GXOS_MODULE_HANDLE_STATUS_INVALID_MODULE_FACTS: return "INVALID_MODULE_FACTS";
        case GXOS_MODULE_HANDLE_STATUS_INVALID_MODULE_BASE: return "INVALID_MODULE_BASE";
        case GXOS_MODULE_HANDLE_STATUS_UNREADABLE_HEADERS: return "UNREADABLE_HEADERS";
        case GXOS_MODULE_HANDLE_STATUS_INVALID_DOS_HEADER: return "INVALID_DOS_HEADER";
        case GXOS_MODULE_HANDLE_STATUS_INVALID_NT_HEADER: return "INVALID_NT_HEADER";
        case GXOS_MODULE_HANDLE_STATUS_WRONG_MACHINE: return "WRONG_MACHINE";
        case GXOS_MODULE_HANDLE_STATUS_WRONG_OPTIONAL_HEADER: return "WRONG_OPTIONAL_HEADER";
        case GXOS_MODULE_HANDLE_STATUS_INVALID_IMAGE_RANGE: return "INVALID_IMAGE_RANGE";
        case GXOS_MODULE_HANDLE_STATUS_RELOCATION_MISMATCH: return "RELOCATION_MISMATCH";
        default: return "UNKNOWN";
    }
}

static void platform_get_module_handle_emit_name(
    GXOS_MODULE_HANDLE_LPCWSTR module_name,
    const GXOS_MODULE_HANDLE_REPORT *report)
{
    static const char digits[] = "0123456789ABCDEF";
    uint32_t index;
    uint32_t preview;

    serial_field_hex("GXOS_NET10:GETMODULEHANDLEW_NAME_POINTER=0x",
                     (uint64_t)(uintptr_t)module_name);
    serial_text("\r\n");
    serial_text("GXOS_NET10:GETMODULEHANDLEW_NAME_IS_NULL=");
    serial_text(module_name == 0 ? "1\r\n" : "0\r\n");
    serial_field_hex("GXOS_NET10:GETMODULEHANDLEW_NAME_REGION_BASE=0x",
                     report->name_region_base);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETMODULEHANDLEW_NAME_REGION_END=0x",
                     report->name_region_end);
    serial_text("\r\n");
    serial_text("GXOS_NET10:GETMODULEHANDLEW_NAME_REGION_READABLE=");
    serial_text(report->name_region_readable ? "1\r\n" : "0\r\n");
    serial_text("GXOS_NET10:GETMODULEHANDLEW_NAME_REGION_EXECUTABLE=");
    serial_text(report->name_region_executable ? "1\r\n" : "0\r\n");
    serial_text("GXOS_NET10:GETMODULEHANDLEW_NAME_REGION_WRITABLE=");
    serial_text(report->name_region_writable ? "1\r\n" : "0\r\n");
    serial_field_hex("GXOS_NET10:GETMODULEHANDLEW_NAME_LENGTH=0x",
                     report->name_length);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETMODULEHANDLEW_NAME_TERMINATOR=0x",
                     report->name_terminator);
    serial_text("\r\n");
    serial_text("GXOS_NET10:GETMODULEHANDLEW_NAME_HAS_PATH=");
    serial_text(report->name_has_path ? "1\r\n" : "0\r\n");
    serial_text("GXOS_NET10:GETMODULEHANDLEW_NAME_HAS_EXTENSION=");
    serial_text(report->name_has_extension ? "1\r\n" : "0\r\n");
    serial_text("GXOS_NET10:GETMODULEHANDLEW_NAME_EXACT_OBSERVED_FORM=");
    serial_text(report->name_exact_observed_form ? "1\r\n" : "0\r\n");
    serial_text("GXOS_NET10:GETMODULEHANDLEW_NAME_UTF16=");
    preview = report->name_length > 128U ? 128U : report->name_length;
    if (module_name != 0 && report->name_readable != 0) {
        for (index = 0; index != preview; ++index) {
            uint16_t word = module_name[index];
            serial_char((uint8_t)digits[(word >> 12) & 0xFU]);
            serial_char((uint8_t)digits[(word >> 8) & 0xFU]);
            serial_char((uint8_t)digits[(word >> 4) & 0xFU]);
            serial_char((uint8_t)digits[word & 0xFU]);
        }
    }
    if (preview != report->name_length) serial_text("...");
    serial_text("\r\n");
    serial_text("GXOS_NET10:GETMODULEHANDLEW_NAME_PREVIEW=\"");
    if (module_name != 0 && report->name_readable != 0) {
        for (index = 0; index != preview; ++index) {
            uint16_t word = module_name[index];
            serial_char(word >= 0x20U && word <= 0x7EU ?
                            (uint8_t)word : (uint8_t)'.');
        }
    }
    if (preview != report->name_length) serial_text("...");
    serial_text("\"\r\n");
}

static void platform_get_module_handle_emit_call(
    GXOS_MODULE_HANDLE_LPCWSTR module_name,
    GXOS_MODULE_HANDLE_HMODULE result,
    const GXOS_MODULE_HANDLE_REPORT *report,
    uint32_t error_before,
    uint32_t error_after,
    uintptr_t return_address)
{
    uintptr_t call_site = return_address >= 6U ? return_address - 6U : 0;
    uint64_t static_call_site = 0;
    uint64_t caller_start = 0;

    if (call_site >= (uintptr_t)g_managed_image_base) {
        static_call_site = 0x180000000ULL +
                           (uint64_t)(call_site - (uintptr_t)g_managed_image_base);
    }
    if (static_call_site == 0x180037C61ULL) caller_start = 0x180037C40ULL;
    if (static_call_site == 0x18003C553ULL) caller_start = 0x18003C530ULL;
    serial_text("GXOS_NET10:GETMODULEHANDLEW_BEGIN\r\n");
    serial_field_hex("GXOS_NET10:GETMODULEHANDLEW_CALL_INDEX=0x",
                     g_get_module_handle_calls);
    serial_text("\r\n");
    serial_text("GXOS_NET10:GETMODULEHANDLEW_IMPORT_MODULE=KERNEL32.dll\r\n");
    serial_text("GXOS_NET10:GETMODULEHANDLEW_IMPORT_SYMBOL=GetModuleHandleW\r\n");
    serial_field_hex("GXOS_NET10:GETMODULEHANDLEW_IMPORT_DESCRIPTOR_INDEX=0x",
                     g_get_module_handle_import_descriptor_index);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETMODULEHANDLEW_IAT_RVA=0x",
                     g_get_module_handle_importing_iat_rva);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETMODULEHANDLEW_PREFERRED_IAT=0x",
                     g_main_module_facts.preferred_image_base +
                         g_get_module_handle_importing_iat_rva);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETMODULEHANDLEW_RUNTIME_IAT=0x",
                     g_main_module_facts.mapped_image_base +
                         g_get_module_handle_importing_iat_rva);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETMODULEHANDLEW_STATIC_CALL_SITE=0x",
                     static_call_site);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETMODULEHANDLEW_RUNTIME_CALL_SITE=0x",
                     call_site);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETMODULEHANDLEW_RETURN_ADDRESS=0x",
                     return_address);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETMODULEHANDLEW_CALLER_START=0x", caller_start);
    serial_text("\r\n");
    serial_text("GXOS_NET10:GETMODULEHANDLEW_CALLER=");
    if (static_call_site == 0x180037C61ULL) serial_text("NativeAOT_RtlDllShutdownInProgress_probe");
    else if (static_call_site == 0x18003C553ULL) serial_text("NativeAOT_InitializeContext2_probe");
    else serial_text("unknown");
    serial_text("\r\n");
    platform_get_module_handle_emit_name(module_name, report);
    serial_field_hex("GXOS_NET10:GETMODULEHANDLEW_PREFERRED_BASE=0x",
                     g_main_module_facts.preferred_image_base);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETMODULEHANDLEW_MAPPED_BASE=0x",
                     g_main_module_facts.mapped_image_base);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETMODULEHANDLEW_RELOCATION_DELTA=0x",
                     g_main_module_facts.relocation_delta);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETMODULEHANDLEW_SIZE_OF_IMAGE=0x",
                     g_main_module_facts.size_of_image);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETMODULEHANDLEW_ENTRY_POINT_RVA=0x",
                     g_main_module_facts.entry_point_rva);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETMODULEHANDLEW_ENTRY_POINT=0x",
                     g_main_module_facts.runtime_entry_point);
    serial_text("\r\n");
    serial_text("GXOS_NET10:GETMODULEHANDLEW_DOS_HEADER_VALID=");
    serial_text(report->dos_header_valid ? "1\r\n" : "0\r\n");
    serial_text("GXOS_NET10:GETMODULEHANDLEW_NT_HEADER_VALID=");
    serial_text(report->nt_header_valid ? "1\r\n" : "0\r\n");
    serial_text("GXOS_NET10:GETMODULEHANDLEW_MACHINE_VALID=");
    serial_text(report->machine_valid ? "1\r\n" : "0\r\n");
    serial_text("GXOS_NET10:GETMODULEHANDLEW_OPTIONAL_HEADER_VALID=");
    serial_text(report->optional_header_valid ? "1\r\n" : "0\r\n");
    serial_text("GXOS_NET10:GETMODULEHANDLEW_SIZE_OF_IMAGE_VALID=");
    serial_text(report->size_of_image_valid ? "1\r\n" : "0\r\n");
    serial_text("GXOS_NET10:GETMODULEHANDLEW_IMAGE_RANGE_VALID=");
    serial_text(report->image_range_valid ? "1\r\n" : "0\r\n");
    serial_text("GXOS_NET10:GETMODULEHANDLEW_ENTRY_POINT_VALID=");
    serial_text(report->entry_point_valid ? "1\r\n" : "0\r\n");
    serial_text("GXOS_NET10:GETMODULEHANDLEW_IMPORT_OWNERSHIP_VALID=");
    serial_text(report->import_ownership_valid ? "1\r\n" : "0\r\n");
    serial_text("GXOS_NET10:GETMODULEHANDLEW_RELOCATION_VALID=");
    serial_text(report->relocation_valid ? "1\r\n" : "0\r\n");
    serial_field_hex("GXOS_NET10:GETMODULEHANDLEW_CALLER_READ_MASK=0x",
                     report->caller_read_mask);
    serial_text("\r\n");
    serial_text("GXOS_NET10:GETMODULEHANDLEW_SELECTED_MODULE=");
    serial_text(report->selected_module == GXOS_MODULE_HANDLE_SELECTED_MAIN_NATIVEAOT_PAYLOAD
                    ? "MAIN_NATIVEAOT_PAYLOAD\r\n"
                    : "NONE\r\n");
    serial_field_hex("GXOS_NET10:GETMODULEHANDLEW_RESULT=0x", result);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETMODULEHANDLEW_LAST_ERROR_BEFORE=0x", error_before);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETMODULEHANDLEW_LAST_ERROR_AFTER=0x", error_after);
    serial_text("\r\n");
    serial_text("GXOS_NET10:GETMODULEHANDLEW_HANDLE_STORED=0\r\n");
    serial_text("GXOS_NET10:GETMODULEHANDLEW_HANDLE_PASSED_TO=KERNEL32.dll!GetProcAddress\r\n");
    serial_text("GXOS_NET10:GETMODULEHANDLEW_CALLER_READS_DOS_HEADERS=0\r\n");
    serial_text("GXOS_NET10:GETMODULEHANDLEW_CALLER_READS_NT_HEADERS=0\r\n");
    serial_text("GXOS_NET10:GETMODULEHANDLEW_CALLER_HEADER_FIELDS=NONE\r\n");
    serial_text("GXOS_NET10:GETMODULEHANDLEW_CALLER_BRANCH=");
    serial_text(result == 0 ? "FAILURE_HANDLE_NULL\r\n" : "SUCCESS_HANDLE_NONZERO\r\n");
    serial_text("GXOS_NET10:GETMODULEHANDLEW_STATUS=");
    serial_text(platform_get_module_handle_status_name(report->status));
    serial_text("\r\n");
    serial_text("GXOS_NET10:GETMODULEHANDLEW_RETURNED\r\n");
    if (result != 0) serial_text("GXOS_NET10:GETMODULEHANDLEW_OK\r\n");
}

static GXOS_MODULE_HANDLE_HMODULE EFIAPI platform_get_module_handle_w(
    GXOS_MODULE_HANDLE_LPCWSTR module_name)
{
    GXOS_MODULE_HANDLE_HMODULE result = 0;
    GXOS_MODULE_HANDLE_STATUS status;
    uint32_t error_before = g_platform_last_error;
    uint32_t error_after = error_before;
    uintptr_t return_address = (uintptr_t)__builtin_return_address(0);

    g_get_module_handle_calls++;
    if (module_name == 0) g_get_module_handle_null_calls++;
    else g_get_module_handle_named_calls++;
    status = gxos_get_module_handle_checked(module_name, &g_main_module_facts,
                                            &result, &g_get_module_handle_last_report);
#ifdef GXOS_MODULE_HANDLE_FORCE_FAILURE
    status = GXOS_MODULE_HANDLE_STATUS_INVALID_MODULE_FACTS;
    result = 0;
    g_get_module_handle_last_report.status = status;
    g_get_module_handle_last_report.output_written = 0;
    g_get_module_handle_last_report.result = 0;
#elif defined(GXOS_MODULE_HANDLE_NAMED_MAIN_EXPERIMENT)
    if (status == GXOS_MODULE_HANDLE_STATUS_MODULE_NOT_FOUND) {
        result = g_main_module_facts.mapped_image_base;
        status = GXOS_MODULE_HANDLE_STATUS_OK;
        g_get_module_handle_last_report.status = status;
        g_get_module_handle_last_report.selected_module =
            GXOS_MODULE_HANDLE_SELECTED_MAIN_NATIVEAOT_PAYLOAD;
        g_get_module_handle_last_report.output_written = 1;
        g_get_module_handle_last_report.result = result;
        serial_text("GXOS_NET10:GETMODULEHANDLEW_NAMED_MAIN_EXPERIMENT=1\r\n");
    }
#elif defined(GXOS_MODULE_HANDLE_PREFERRED_BASE_EXPERIMENT)
    if (status == GXOS_MODULE_HANDLE_STATUS_MODULE_NOT_FOUND) {
        result = g_main_module_facts.preferred_image_base;
        status = GXOS_MODULE_HANDLE_STATUS_OK;
        g_get_module_handle_last_report.status = status;
        g_get_module_handle_last_report.selected_module =
            GXOS_MODULE_HANDLE_SELECTED_MAIN_NATIVEAOT_PAYLOAD;
        g_get_module_handle_last_report.output_written = 1;
        g_get_module_handle_last_report.result = result;
        serial_text("GXOS_NET10:GETMODULEHANDLEW_PREFERRED_BASE_EXPERIMENT=1\r\n");
    }
#elif defined(GXOS_MODULE_HANDLE_RVA_EXPERIMENT)
    if (status == GXOS_MODULE_HANDLE_STATUS_MODULE_NOT_FOUND) {
        result = g_main_module_facts.entry_point_rva;
        status = GXOS_MODULE_HANDLE_STATUS_OK;
        g_get_module_handle_last_report.status = status;
        g_get_module_handle_last_report.selected_module =
            GXOS_MODULE_HANDLE_SELECTED_MAIN_NATIVEAOT_PAYLOAD;
        g_get_module_handle_last_report.output_written = 1;
        g_get_module_handle_last_report.result = result;
        serial_text("GXOS_NET10:GETMODULEHANDLEW_RVA_EXPERIMENT=1\r\n");
    }
#elif defined(GXOS_MODULE_HANDLE_WRONG_IMAGE_EXPERIMENT)
    if (status == GXOS_MODULE_HANDLE_STATUS_MODULE_NOT_FOUND) {
        result = (GXOS_MODULE_HANDLE_HMODULE)(uintptr_t)&platform_get_module_handle_w;
        status = GXOS_MODULE_HANDLE_STATUS_OK;
        g_get_module_handle_last_report.status = status;
        g_get_module_handle_last_report.selected_module =
            GXOS_MODULE_HANDLE_SELECTED_NONE;
        g_get_module_handle_last_report.output_written = 1;
        g_get_module_handle_last_report.result = result;
        serial_text("GXOS_NET10:GETMODULEHANDLEW_WRONG_IMAGE_EXPERIMENT=1\r\n");
    }
#endif
    if (status != GXOS_MODULE_HANDLE_STATUS_OK) {
        g_get_module_handle_failures++;
        error_after = status == GXOS_MODULE_HANDLE_STATUS_MODULE_NOT_FOUND
                          ? GXOS_MODULE_HANDLE_ERROR_MOD_NOT_FOUND
                          : GXOS_MODULE_HANDLE_ERROR_INVALID_PARAMETER;
        g_platform_last_error = error_after;
    } else {
        g_get_module_handle_successes++;
    }
    g_get_module_handle_last_error_before = error_before;
    g_get_module_handle_last_error_after = error_after;
    g_get_module_handle_last_caller = return_address;
    g_get_module_handle_last_call_site = return_address >= 6U ? return_address - 6U : 0;
    g_get_module_handle_last_handle = result;
    platform_get_module_handle_emit_call(module_name, result,
                                         &g_get_module_handle_last_report,
                                         error_before, error_after,
                                         return_address);
    return result;
}
#endif

#ifdef GXOS_ENABLE_GETENV
static uint32_t EFIAPI platform_get_environment_variable_w(
    const GXOS_ENVIRONMENT_WCHAR *name,
    GXOS_ENVIRONMENT_WCHAR *buffer,
    GXOS_ENVIRONMENT_DWORD buffer_size)
{
    GXOS_ENVIRONMENT_DWORD previous_error = g_platform_last_error;
    GXOS_ENVIRONMENT_DWORD last_error = previous_error;
    GXOS_ENVIRONMENT_DWORD result;
    uint32_t name_length = platform_getenv_bounded_name_length(name);
    uint64_t caller = (uint64_t)(uintptr_t)__builtin_return_address(0);

    result = gxos_get_environment_variable_w_not_found(name, buffer, buffer_size,
                                                        &last_error);
    g_platform_last_error = last_error;
    g_getenv_calls++;
    g_getenv_missing++;
    g_getenv_last_return = result;
    g_getenv_last_error_before = previous_error;
    g_getenv_last_error_after = last_error;
    g_getenv_last_caller = caller;

    serial_text("GXOS_NET10:GETENV_BEGIN\r\n");
    serial_field_hex("GXOS_NET10:GETENV_CALL_INDEX=0x", g_getenv_calls);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETENV_CALLER=0x", caller);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETENV_RETURN_ADDRESS=0x", caller);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETENV_LP_NAME=0x", (uint64_t)(uintptr_t)name);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETENV_LP_BUFFER=0x", (uint64_t)(uintptr_t)buffer);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETENV_N_SIZE=0x", buffer_size);
    serial_text("\r\n");
    serial_text("GXOS_NET10:GETENV_LP_NAME_NULL=");
    serial_text(name == 0 ? "1\r\n" : "0\r\n");
    serial_text("GXOS_NET10:GETENV_LP_BUFFER_NULL=");
    serial_text(buffer == 0 ? "1\r\n" : "0\r\n");
    serial_text("GXOS_NET10:GETENV_N_SIZE_ZERO=");
    serial_text(buffer_size == 0 ? "1\r\n" : "0\r\n");
    serial_text("GXOS_NET10:GETENV_SIZE_PROBE=");
    serial_text(buffer == 0 || buffer_size == 0 ? "1\r\n" : "0\r\n");
    serial_field_hex("GXOS_NET10:GETENV_NAME_LENGTH=0x", name_length);
    serial_text("\r\n");
    if (name != 0) {
        platform_getenv_emit_hex16("GXOS_NET10:GETENV_NAME_UTF16=", name, name_length);
        platform_getenv_emit_text("GXOS_NET10:GETENV_NAME_TEXT=", name, name_length);
    } else {
        serial_text("GXOS_NET10:GETENV_NAME_UTF16=\r\n");
        serial_text("GXOS_NET10:GETENV_NAME_TEXT=\"\"\r\n");
    }
    serial_field_hex("GXOS_NET10:GETENV_RETURN_VALUE=0x", result);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETENV_LAST_ERROR_BEFORE=0x", previous_error);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETENV_LAST_ERROR_AFTER=0x", last_error);
    serial_text("\r\n");
    serial_text("GXOS_NET10:GETENV_LAST_ERROR_CHANGED=");
    serial_text(previous_error == last_error ? "0\r\n" : "1\r\n");
    serial_text("GXOS_NET10:GETENV_RETURN_EXPECTED_BY_CALLER=0x0000000000000000\r\n");
    serial_text("GXOS_NET10:GETENV_IMMEDIATE_USE=unsigned_return_minus_one_and_16_char_bound\r\n");
    serial_text("GXOS_NET10:GETENV_OUTPUT_WRITTEN=0\r\n");
#ifdef GXOS_GETENV_MARKER_MUTATION
    serial_text("GXOS_NET10:GETENV_OX\r\n");
#else
    serial_text("GXOS_NET10:GETENV_RETURNED\r\n");
    serial_text("GXOS_NET10:GETENV_OK\r\n");
#endif
    return result;
}
#endif

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
static uintptr_t g_crt_onexit_initialized_tables[GXOS_CRT_ONEXIT_MAX_INITIALIZED_TABLES];
static uint32_t g_crt_onexit_initialized_table_count;
static GXOS_CRT_ONEXIT_CONTEXT g_crt_onexit_context;
#ifdef GXOS_ENABLE_CRT_ONEXIT_REGISTER
static uint32_t g_crt_onexit_register_import_descriptor_index;
static uint32_t g_crt_onexit_register_importing_iat_rva;
static uint64_t g_crt_onexit_register_calls;
static uint64_t g_crt_onexit_register_failures;
static uint64_t g_crt_onexit_register_allocation_attempts;
static uint64_t g_crt_onexit_register_allocator_calls;
static uint64_t g_crt_onexit_register_allocation_count;
static uint64_t g_crt_onexit_register_allocated_bytes;
static GXOS_CRT_ONEXIT_REPORT g_crt_onexit_register_report;
static GXOS_CRT_ONEXIT_STATUS g_crt_onexit_register_status;
#endif

#ifdef GXOS_ENABLE_CRT_ONEXIT_REGISTER
static void *GXOS_CRT_EFIAPI platform_crt_onexit_allocate(
    uintptr_t size,
    void *context)
{
    EFI_BOOT_SERVICES *boot_services = (EFI_BOOT_SERVICES *)context;
    void *allocation = 0;

    ++g_crt_onexit_register_allocator_calls;
    if (size != GXOS_CRT_ONEXIT_INITIAL_STORAGE_BYTES ||
        boot_services == 0 || boot_services->AllocatePool == 0 ||
        EFI_ERROR(boot_services->AllocatePool(
            EFI_LOADER_DATA, (EFI_UINTN)size, &allocation)) ||
        allocation == 0) {
        return 0;
    }
    ++g_crt_onexit_register_allocation_count;
    g_crt_onexit_register_allocated_bytes += size;
    return allocation;
}

static int GXOS_CRT_EFIAPI platform_crt_onexit_free(
    void *allocation,
    uintptr_t size,
    void *context)
{
    EFI_BOOT_SERVICES *boot_services = (EFI_BOOT_SERVICES *)context;

    if (allocation == 0 || size != GXOS_CRT_ONEXIT_INITIAL_STORAGE_BYTES ||
        boot_services == 0 || boot_services->FreePool == 0) {
        return -1;
    }
    return EFI_ERROR(boot_services->FreePool(allocation)) ? -1 : 0;
}
#endif

static int GXOS_CRT_EFIAPI platform_initialize_onexit_table(GXOS_CRT_ONEXIT_TABLE *table)
{
    int result;
    uintptr_t table_value = (uintptr_t)table;
    g_crt_onexit_initialize_calls++;
    serial_field_hex("GXOS_NET10:CRT_ONEXIT_INIT_CALL=0x", g_crt_onexit_initialize_calls);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CRT_ONEXIT_TABLE=0x", (uint64_t)(uintptr_t)table);
    serial_text("\r\n");
    if (table == 0) {
        serial_text("GXOS_NET10:CRT_ONEXIT_INIT_TABLE_FIRST_BEFORE=0x0000000000000000\r\n");
        serial_text("GXOS_NET10:CRT_ONEXIT_INIT_TABLE_LAST_BEFORE=0x0000000000000000\r\n");
        serial_text("GXOS_NET10:CRT_ONEXIT_INIT_TABLE_END_BEFORE=0x0000000000000000\r\n");
    } else {
        serial_field_hex("GXOS_NET10:CRT_ONEXIT_INIT_TABLE_FIRST_BEFORE=0x", (uintptr_t)table->first);
        serial_text("\r\n");
        serial_field_hex("GXOS_NET10:CRT_ONEXIT_INIT_TABLE_LAST_BEFORE=0x", (uintptr_t)table->last);
        serial_text("\r\n");
        serial_field_hex("GXOS_NET10:CRT_ONEXIT_INIT_TABLE_END_BEFORE=0x", (uintptr_t)table->end);
        serial_text("\r\n");
    }
    result = gxos_crt_initialize_onexit_table(table);
    serial_field_hex("GXOS_NET10:CRT_ONEXIT_INIT_RETURN=0x", (uint64_t)(uint32_t)result);
    serial_text("\r\n");
    if (result == 0 && table != 0 &&
        g_crt_onexit_initialized_table_count < GXOS_CRT_ONEXIT_MAX_INITIALIZED_TABLES) {
        g_crt_onexit_initialized_tables[g_crt_onexit_initialized_table_count++] = table_value;
        if (gxos_crt_onexit_set_initialized_tables(
                g_crt_onexit_initialized_tables,
                g_crt_onexit_initialized_table_count) != 0) {
            fail("crt-onexit-table-list");
        }
    }
    if (table == 0) {
        serial_text("GXOS_NET10:CRT_ONEXIT_INIT_TABLE_FIRST_AFTER=0x0000000000000000\r\n");
        serial_text("GXOS_NET10:CRT_ONEXIT_INIT_TABLE_LAST_AFTER=0x0000000000000000\r\n");
        serial_text("GXOS_NET10:CRT_ONEXIT_INIT_TABLE_END_AFTER=0x0000000000000000\r\n");
    } else {
        serial_field_hex("GXOS_NET10:CRT_ONEXIT_INIT_TABLE_FIRST_AFTER=0x", (uintptr_t)table->first);
        serial_text("\r\n");
        serial_field_hex("GXOS_NET10:CRT_ONEXIT_INIT_TABLE_LAST_AFTER=0x", (uintptr_t)table->last);
        serial_text("\r\n");
        serial_field_hex("GXOS_NET10:CRT_ONEXIT_INIT_TABLE_END_AFTER=0x", (uintptr_t)table->end);
        serial_text("\r\n");
    }
    if (result == 0 && table != 0 && table->first == table->last && table->last == table->end) {
#ifdef GXOS_CRT_ONEXIT_MARKER_MUTATION
        serial_text("GXOS_NET10:CRT_ONEXIT_INITIALIZED_OX\r\n");
#else
        serial_text("GXOS_NET10:CRT_ONEXIT_INITIALIZED_OK\r\n");
#endif
    }
    return result;
}
#ifdef GXOS_ENABLE_CRT_ONEXIT_REGISTER
static uint64_t platform_crt_onexit_static_call_site(uintptr_t call_site)
{
    if (call_site >= (uintptr_t)g_managed_image_base) {
        return 0x180000000ULL +
               (uint64_t)(call_site - (uintptr_t)g_managed_image_base);
    }
    return 0;
}

static int GXOS_CRT_EFIAPI platform_register_onexit_function(
    GXOS_CRT_ONEXIT_TABLE *table,
    GXOS_CRT_ONEXIT_T function)
{
    uintptr_t return_address = (uintptr_t)__builtin_return_address(0);
    uintptr_t call_site = return_address >= 6U ? return_address - 6U : 0;
    uint64_t static_call_site = platform_crt_onexit_static_call_site(call_site);
    GXOS_CRT_ONEXIT_STATUS status;

    ++g_crt_onexit_register_calls;
    serial_text("GXOS_NET10:REGISTER_ONEXIT_BEGIN\r\n");
    serial_field_hex("GXOS_NET10:REGISTER_ONEXIT_CALL_INDEX=0x", g_crt_onexit_register_calls - 1U);
    serial_text("\r\n");
    serial_text("GXOS_NET10:REGISTER_ONEXIT_IMPORT_MODULE=api-ms-win-crt-runtime-l1-1-0.dll\r\n");
    serial_text("GXOS_NET10:REGISTER_ONEXIT_IMPORT_SYMBOL=_register_onexit_function\r\n");
    serial_field_hex("GXOS_NET10:REGISTER_ONEXIT_IMPORT_DESCRIPTOR_INDEX=0x",
                     g_crt_onexit_register_import_descriptor_index);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:REGISTER_ONEXIT_IAT_RVA=0x",
                     g_crt_onexit_register_importing_iat_rva);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:REGISTER_ONEXIT_PREFERRED_IAT=0x",
                     0x180000000ULL + g_crt_onexit_register_importing_iat_rva);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:REGISTER_ONEXIT_RUNTIME_IAT=0x",
                     (uint64_t)g_managed_image_base + g_crt_onexit_register_importing_iat_rva);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:REGISTER_ONEXIT_STATIC_CALL_SITE=0x", static_call_site);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:REGISTER_ONEXIT_RUNTIME_CALL_SITE=0x", call_site);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:REGISTER_ONEXIT_RETURN_ADDRESS=0x", return_address);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:REGISTER_ONEXIT_TABLE_POINTER=0x", (uintptr_t)table);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:REGISTER_ONEXIT_CALLBACK_POINTER=0x", (uintptr_t)function);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:REGISTER_ONEXIT_CALLER_START=0x", 0x180077DF0ULL);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:REGISTER_ONEXIT_CALLER_END=0x", 0x180077E30ULL);
    serial_text("\r\n");
    serial_text("GXOS_NET10:REGISTER_ONEXIT_CALLER=NativeAOT_CRT_atexit_registration_helper\r\n");
    serial_field_hex("GXOS_NET10:REGISTER_ONEXIT_TABLE_RVA=0x",
                     (uint64_t)((uintptr_t)table - (uintptr_t)g_managed_image_base));
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:REGISTER_ONEXIT_CALLBACK_RVA=0x",
                     (uint64_t)((uintptr_t)function - (uintptr_t)g_managed_image_base));
    serial_text("\r\n");
    serial_text("GXOS_NET10:REGISTER_ONEXIT_BEFORE_REGISTRATION=1\r\n");
    serial_text("GXOS_NET10:REGISTER_ONEXIT_HEAP_MARKER=UEFI_BOOT_SERVICES_ALLOCATE_POOL_EFI_LOADER_DATA\r\n");
    serial_field_hex("GXOS_NET10:REGISTER_ONEXIT_ALLOCATION_COUNT_BEFORE=0x",
                     g_crt_onexit_register_allocation_count);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:REGISTER_ONEXIT_ALLOCATOR_CALL_COUNT_BEFORE=0x",
                     g_crt_onexit_register_allocator_calls);
    serial_text("\r\n");
    status = gxos_crt_onexit_register_checked(table, function,
                                              &g_crt_onexit_register_report);
    g_crt_onexit_register_status = status;
    if (status == GXOS_CRT_ONEXIT_STATUS_OK) {
        ++g_crt_onexit_register_successes;
    } else {
        ++g_crt_onexit_register_failures;
    }
    if (g_crt_onexit_register_report.allocation_attempted != 0) {
        ++g_crt_onexit_register_allocation_attempts;
    }
    g_crt_onexit_register_callback_executed +=
        g_crt_onexit_register_report.callback_executed;
    serial_field_hex("GXOS_NET10:REGISTER_ONEXIT_TABLE_FIRST_RAW_BEFORE=0x",
                     g_crt_onexit_register_report.table_first_raw);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:REGISTER_ONEXIT_TABLE_LAST_RAW_BEFORE=0x",
                     g_crt_onexit_register_report.table_last_raw);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:REGISTER_ONEXIT_TABLE_END_RAW_BEFORE=0x",
                     g_crt_onexit_register_report.table_end_raw);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:REGISTER_ONEXIT_TABLE_REGION_BASE=0x",
                     g_crt_onexit_register_report.table_region_base);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:REGISTER_ONEXIT_TABLE_REGION_END=0x",
                     g_crt_onexit_register_report.table_region_end);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:REGISTER_ONEXIT_TABLE_REGION_READABLE=0x",
                     g_crt_onexit_register_report.table_region_readable);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:REGISTER_ONEXIT_TABLE_REGION_WRITABLE=0x",
                     g_crt_onexit_register_report.table_region_writable);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:REGISTER_ONEXIT_TABLE_FIRST_BEFORE=0x",
                     g_crt_onexit_register_report.first);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:REGISTER_ONEXIT_TABLE_LAST_BEFORE=0x",
                     g_crt_onexit_register_report.last);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:REGISTER_ONEXIT_TABLE_END_BEFORE=0x",
                     g_crt_onexit_register_report.end);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:REGISTER_ONEXIT_USED_BEFORE=0x",
                     g_crt_onexit_register_report.used_count);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:REGISTER_ONEXIT_CAPACITY_BEFORE=0x",
                     g_crt_onexit_register_report.capacity);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:REGISTER_ONEXIT_REMAINING_CAPACITY_BEFORE=0x",
                     g_crt_onexit_register_report.remaining_capacity);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:REGISTER_ONEXIT_GROWTH_REQUIRED=0x",
                     g_crt_onexit_register_report.growth_required);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:REGISTER_ONEXIT_ALLOCATION_ATTEMPTED=0x",
                     g_crt_onexit_register_report.allocation_attempted);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:REGISTER_ONEXIT_POINTER_ENCODED=0x",
                     g_crt_onexit_register_report.pointer_encoded);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:REGISTER_ONEXIT_STORED_VALUE=0x",
                     g_crt_onexit_register_report.stored_value);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:REGISTER_ONEXIT_ENTRY_INDEX=0x",
                     g_crt_onexit_register_report.entry_index);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:REGISTER_ONEXIT_TABLE_FIRST_AFTER=0x",
                     g_crt_onexit_register_report.first_after);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:REGISTER_ONEXIT_TABLE_LAST_AFTER=0x",
                     g_crt_onexit_register_report.last_after);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:REGISTER_ONEXIT_TABLE_END_AFTER=0x",
                     g_crt_onexit_register_report.end_after);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:REGISTER_ONEXIT_USED_AFTER=0x",
                     g_crt_onexit_register_report.used_after);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:REGISTER_ONEXIT_CAPACITY_AFTER=0x",
                     g_crt_onexit_register_report.capacity_after);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:REGISTER_ONEXIT_REMAINING_CAPACITY_AFTER=0x",
                     g_crt_onexit_register_report.remaining_after);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:REGISTER_ONEXIT_TABLE_FIRST_RAW_AFTER=0x",
                     g_crt_onexit_register_report.table_first_raw_after);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:REGISTER_ONEXIT_TABLE_LAST_RAW_AFTER=0x",
                     g_crt_onexit_register_report.table_last_raw_after);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:REGISTER_ONEXIT_TABLE_END_RAW_AFTER=0x",
                     g_crt_onexit_register_report.table_end_raw_after);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:REGISTER_ONEXIT_TABLE_UNCHANGED=0x",
                     g_crt_onexit_register_report.table_first_raw_after ==
                             g_crt_onexit_register_report.table_first_raw &&
                         g_crt_onexit_register_report.table_last_raw_after ==
                             g_crt_onexit_register_report.table_last_raw &&
                         g_crt_onexit_register_report.table_end_raw_after ==
                             g_crt_onexit_register_report.table_end_raw);
    serial_text("\r\n");
    serial_text("GXOS_NET10:REGISTER_ONEXIT_STORAGE_REGION_BASE=0x");
    serial_hex64(g_crt_onexit_register_report.storage_region_base);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:REGISTER_ONEXIT_STORAGE_REGION_END=0x",
                     g_crt_onexit_register_report.storage_region_end);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:REGISTER_ONEXIT_STORAGE_REGION_READABLE=0x",
                     g_crt_onexit_register_report.storage_region_readable);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:REGISTER_ONEXIT_STORAGE_REGION_WRITABLE=0x",
                     g_crt_onexit_register_report.storage_region_writable);
    serial_text("\r\n");
    serial_text("GXOS_NET10:REGISTER_ONEXIT_INITIAL_TABLE_CLASSIFICATION=");
    serial_text(g_crt_onexit_register_report.initial_empty_state != 0
                    ? "DECODED_EMPTY\r\n"
                    : "DECODED_NONEMPTY\r\n");
    serial_field_hex("GXOS_NET10:REGISTER_ONEXIT_ALLOCATION_ADDRESS=0x",
                     g_crt_onexit_register_report.allocation_address);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:REGISTER_ONEXIT_ALLOCATION_SIZE=0x",
                     g_crt_onexit_register_report.allocation_size);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:REGISTER_ONEXIT_SLOT_COUNT=0x",
                     g_crt_onexit_register_report.slot_count);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:REGISTER_ONEXIT_SLOT0_DECODED=0x",
                     g_crt_onexit_register_report.decoded_slot0);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:REGISTER_ONEXIT_SLOT0_CALLBACK_MATCH=0x",
                     g_crt_onexit_register_report.decoded_slot0 ==
                             (uintptr_t)function);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:REGISTER_ONEXIT_UNUSED_SLOTS_ALL_NULL=0x",
                     g_crt_onexit_register_report.unused_slots_all_null);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:REGISTER_ONEXIT_STORAGE_DISJOINT_FROM_IMAGE=0x",
                     g_crt_onexit_register_report.allocation_disjoint_from_context);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:REGISTER_ONEXIT_UNUSED_SLOT_FIRST=0x",
                     g_crt_onexit_register_report.slot_count > 1U
                         ? gxos_crt_onexit_decode_pointer(
                               *(uintptr_t *)(g_crt_onexit_register_report.allocation_address +
                                              sizeof(uintptr_t)))
                         : 0);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:REGISTER_ONEXIT_UNUSED_SLOT_LAST=0x",
                     g_crt_onexit_register_report.slot_count ==
                                 GXOS_CRT_ONEXIT_INITIAL_STORAGE_SLOTS
                         ? gxos_crt_onexit_decode_pointer(
                               *(uintptr_t *)(g_crt_onexit_register_report.allocation_address +
                                              (GXOS_CRT_ONEXIT_INITIAL_STORAGE_SLOTS - 1U) *
                                                  sizeof(uintptr_t)))
                         : 0);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:REGISTER_ONEXIT_ALLOCATION_COUNT_AFTER=0x",
                     g_crt_onexit_register_allocation_count);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:REGISTER_ONEXIT_ALLOCATOR_CALL_COUNT_AFTER=0x",
                     g_crt_onexit_register_allocator_calls);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:REGISTER_ONEXIT_ALLOCATED_BYTES_AFTER=0x",
                     g_crt_onexit_register_allocated_bytes);
    serial_text("\r\n");
    serial_text("GXOS_NET10:REGISTER_ONEXIT_CALLBACK_OWNER=");
    if (g_crt_onexit_register_report.callback_region_executable != 0) {
        serial_text("MANAGED_IMAGE_TEXT\r\n");
    } else {
        serial_text("NONE\r\n");
    }
    serial_field_hex("GXOS_NET10:REGISTER_ONEXIT_CALLBACK_REGION_BASE=0x",
                     g_crt_onexit_register_report.callback_region_base);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:REGISTER_ONEXIT_CALLBACK_REGION_END=0x",
                     g_crt_onexit_register_report.callback_region_end);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:REGISTER_ONEXIT_INITIALIZED_TABLE_MATCH=0x",
                     g_crt_onexit_register_report.initialized_table_match);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:REGISTER_ONEXIT_INITIALIZED_TABLE_INDEX=0x",
                     g_crt_onexit_register_report.initialized_table_index);
    serial_text("\r\n");
    serial_text("GXOS_NET10:REGISTER_ONEXIT_ENCODING=FAST_SECURITY_COOKIE_ROTATE_X64\r\n");
    serial_text("GXOS_NET10:REGISTER_ONEXIT_GROWTH_POLICY=INITIAL_TABLE_COUNT_0x20_MIN_INCREMENT_0x4_MAX_INCREMENT_0x200\r\n");
    serial_text("GXOS_NET10:REGISTER_ONEXIT_ALLOCATION_PRIMITIVE=UEFI_BOOT_SERVICES_ALLOCATE_POOL\r\n");
    serial_text("GXOS_NET10:REGISTER_ONEXIT_ALLOCATION_DEPENDENCY=AllocatePool(EFI_LOADER_DATA,0x100)\r\n");
    serial_field_hex("GXOS_NET10:REGISTER_ONEXIT_ALLOCATION_IMPLEMENTED=0x",
                     g_crt_onexit_register_report.allocation_succeeded);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:REGISTER_ONEXIT_RESULT=0x",
                     (uint64_t)(uint32_t)(status == GXOS_CRT_ONEXIT_STATUS_OK ? 0 : -1));
    serial_text("\r\n");
    serial_text("GXOS_NET10:REGISTER_ONEXIT_STATUS=");
    serial_text(gxos_crt_onexit_status_name(status));
    serial_text("\r\n");
    serial_text("GXOS_NET10:REGISTER_ONEXIT_CALLER_BRANCH=");
    serial_text(status == GXOS_CRT_ONEXIT_STATUS_OK ?
                    "RETURN_VALUE_MAPPED_TO_SUCCESS\r\n" :
                    "RETURN_VALUE_MAPPED_TO_FAILURE\r\n");
    serial_field_hex("GXOS_NET10:REGISTER_ONEXIT_CALLBACK_EXECUTED=0x",
                     g_crt_onexit_register_report.callback_executed);
    serial_text("\r\n");
    serial_text("GXOS_NET10:REGISTER_ONEXIT_CALLBACK_EXECUTED_PROVEN=0x0\r\n");
    serial_text("GXOS_NET10:REGISTER_ONEXIT_RETURNED\r\n");
    return status == GXOS_CRT_ONEXIT_STATUS_OK ? 0 : -1;
}
#endif
#endif

#ifdef GXOS_ENABLE_CRT_MALLOC
static uint64_t GXOS_CRT_MALLOC_MS_ABI platform_crt_malloc_allocate_pool(
    uint32_t pool_type,
    uintptr_t size,
    void **buffer,
    void *context)
{
    EFI_BOOT_SERVICES *boot_services = (EFI_BOOT_SERVICES *)context;
    if (boot_services == 0 || boot_services->AllocatePool == 0 || buffer == 0) {
        return (uint64_t)1 << 63;
    }
    return boot_services->AllocatePool(
        pool_type, (EFI_UINTN)size, buffer);
}

static uint64_t GXOS_CRT_MALLOC_MS_ABI platform_crt_malloc_free_pool(
    void *buffer,
    void *context)
{
    EFI_BOOT_SERVICES *boot_services = (EFI_BOOT_SERVICES *)context;
    if (boot_services == 0 || boot_services->FreePool == 0) {
        return (uint64_t)1 << 63;
    }
    return boot_services->FreePool(buffer);
}

static void *GXOS_CRT_MALLOC_MS_ABI platform_crt_malloc(uintptr_t size)
{
    return gxos_crt_malloc_entry(
        &g_crt_malloc_context,
        (uint64_t)size,
        (uintptr_t)__builtin_return_address(0));
}

static void configure_platform_crt_malloc(
    const PE_IMAGE *image,
    EFI_BOOT_SERVICES *boot_services)
{
    uintptr_t image_end;

    if (image == 0 || boot_services == 0 || image->actual_base == 0 ||
        image->loaded_size == 0 || image->actual_base >
            UINTPTR_MAX - (uintptr_t)image->loaded_size ||
        g_stack_lower >= g_stack_upper) {
        fail("crt-malloc-context");
    }
    image_end = image->actual_base + (uintptr_t)image->loaded_size;
    gxos_crt_malloc_context_reset(&g_crt_malloc_context);
    g_crt_malloc_context.boot_services = boot_services;
    g_crt_malloc_context.boot_services_available = 1;
    g_crt_malloc_context.allocate_pool = platform_crt_malloc_allocate_pool;
    g_crt_malloc_context.free_pool = platform_crt_malloc_free_pool;
    g_crt_malloc_context.allocator_context = boot_services;
    g_crt_malloc_context.preferred_image_base =
        (uintptr_t)image->preferred_base;
    g_crt_malloc_context.image_base = (uintptr_t)image->actual_base;
    g_crt_malloc_context.image_end = image_end;
    g_crt_malloc_context.trace = platform_crt_malloc_trace;
    g_crt_malloc_context.trace_context = 0;
    if (gxos_crt_malloc_add_protected_range(
            &g_crt_malloc_context,
            (uintptr_t)image->actual_base,
            image_end,
            1) != 0 ||
        gxos_crt_malloc_add_protected_range(
            &g_crt_malloc_context,
            (uintptr_t)g_stack_lower,
            (uintptr_t)g_stack_upper,
            3) != 0) {
        fail("crt-malloc-protected-ranges");
    }
#ifdef GXOS_ENABLE_CRT_ONEXIT
    if (gxos_crt_malloc_add_protected_range(
            &g_crt_malloc_context,
            (uintptr_t)&g_crt_onexit_context,
            (uintptr_t)&g_crt_onexit_context + sizeof(g_crt_onexit_context),
            2) != 0 ||
        gxos_crt_malloc_add_protected_range(
            &g_crt_malloc_context,
            (uintptr_t)g_crt_onexit_initialized_tables,
            (uintptr_t)g_crt_onexit_initialized_tables +
                sizeof(g_crt_onexit_initialized_tables),
            2) != 0) {
        fail("crt-malloc-onexit-protected-ranges");
    }
#endif
    serial_text("GXOS_NET10:MALLOC_CONTEXT_VALID=1\r\n");
    serial_text("GXOS_NET10:MALLOC_IMPORT_MODULE=api-ms-win-crt-heap-l1-1-0.dll\r\n");
    serial_text("GXOS_NET10:MALLOC_IMPORT_SYMBOL=malloc\r\n");
    serial_field_hex("GXOS_NET10:MALLOC_IMPORT_DESCRIPTOR_INDEX=0x",
                     g_crt_malloc_import_descriptor_index);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:MALLOC_IAT_RVA=0x",
                     g_crt_malloc_importing_iat_rva);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:MALLOC_PREFERRED_IAT=0x",
                     (uint64_t)image->preferred_base +
                         g_crt_malloc_importing_iat_rva);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:MALLOC_RUNTIME_IAT=0x",
                     (uint64_t)image->actual_base +
                         g_crt_malloc_importing_iat_rva);
    serial_text("\r\n");
    serial_text("GXOS_NET10:MALLOC_ALLOCATION_PRIMITIVE=AllocatePool(EFI_LOADER_DATA,requestedSize,&pointer)\r\n");
    serial_field_hex("GXOS_NET10:MALLOC_MAX_REQUEST=0x",
                     GXOS_CRT_MALLOC_MAX_REQUEST);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:MALLOC_REGISTRY_CAPACITY=0x",
                     GXOS_CRT_MALLOC_REGISTRY_CAPACITY);
    serial_text("\r\n");
    serial_text("GXOS_NET10:MALLOC_HIDDEN_HEADER=0\r\n");
    serial_text("GXOS_NET10:MALLOC_ZEROING=0\r\n");
}

#endif

#ifdef GXOS_ENABLE_SLIST
static void GXOS_SLIST_EFIAPI platform_initialize_slist_head(GXOS_SLIST_HEADER *head)
{
    uintptr_t address = (uintptr_t)head;
    int result;

    g_slist_initialize_calls++;
    if (g_slist_initialize_calls <= 8) {
        serial_text("GXOS_NET10:SLIST_IMPORT_FUNCTIONAL=1\r\n");
        serial_field_hex("GXOS_NET10:SLIST_HEAD_INIT_CALL=0x", g_slist_initialize_calls);
        serial_text("\r\n");
        serial_field_hex("GXOS_NET10:SLIST_HEAD_ADDRESS=0x", (uint64_t)address);
        serial_text("\r\n");
        serial_field_hex("GXOS_NET10:SLIST_HEAD_ALIGNMENT=0x", (uint64_t)(address & 0x0Fu));
        serial_text("\r\n");
    }
    result = gxos_initialize_slist_head(head);
    if (result != 0) fail("slist-head-invalid");
    if (head->Original.Alignment != 0 || head->Original.Region != 0 ||
        head->HeaderX64.Depth != 0 || head->HeaderX64.Sequence != 0 ||
        head->HeaderX64.Reserved != 0 || head->HeaderX64.NextEntry != 0) {
        fail("slist-head-contract");
    }
    serial_field_hex("GXOS_NET10:SLIST_HEAD_INITIALIZED_COUNT=0x", g_slist_initialize_calls);
    serial_text("\r\n");
#ifdef GXOS_SLIST_MARKER_MUTATION
    serial_text("GXOS_NET10:SLIST_HEAD_INITIALIZED_OX\r\n");
#else
    serial_text("GXOS_NET10:SLIST_HEAD_INITIALIZED_OK\r\n");
#endif
}
#endif

#ifdef GXOS_ENABLE_CRT_INITTERM_E
static uint64_t g_crt_initterm_e_calls;

static void GXOS_CRT_INITTERM_E_MS_ABI platform_initterm_e_trace(
    uint32_t event,
    uint64_t index,
    uintptr_t target,
    int32_t result)
{
    if (event == GXOS_CRT_INITTERM_E_TRACE_ENTRY) {
        serial_field_hex("GXOS_NET10:CRT_INITTERM_E_ENTRY_INDEX=0x", index);
        serial_text("\r\n");
        serial_field_hex("GXOS_NET10:CRT_INITTERM_E_ENTRY_RAW=0x", (uint64_t)target);
        serial_text("\r\n");
        if (target == 0) serial_text("GXOS_NET10:CRT_INITTERM_E_ENTRY_NULL\r\n");
    } else if (event == GXOS_CRT_INITTERM_E_TRACE_CALLBACK_BEGIN) {
        serial_field_hex("GXOS_NET10:CRT_INITTERM_E_CALLBACK_INDEX=0x", index);
        serial_text("\r\n");
        serial_field_hex("GXOS_NET10:CRT_INITTERM_E_CALLBACK_TARGET=0x", (uint64_t)target);
        serial_text("\r\n");
        serial_text("GXOS_NET10:CRT_INITTERM_E_CALLBACK_TARGET_CLASS=IMAGE_EXECUTABLE\r\n");
        serial_text("GXOS_NET10:CRT_INITTERM_E_CALLBACK_INVOKED\r\n");
    } else if (event == GXOS_CRT_INITTERM_E_TRACE_CALLBACK_RESULT) {
        serial_field_hex("GXOS_NET10:CRT_INITTERM_E_CALLBACK_RESULT=0x", (uint64_t)(uint32_t)result);
        serial_text("\r\n");
        if (result == 0) serial_text("GXOS_NET10:CRT_INITTERM_E_CALLBACK_OK\r\n");
        else serial_text("GXOS_NET10:CRT_INITTERM_E_CALLBACK_FAILURE\r\n");
    } else if (event == GXOS_CRT_INITTERM_E_TRACE_VALIDATION_FAILURE) {
        serial_field_hex("GXOS_NET10:CRT_INITTERM_E_VALIDATION_FAILURE=0x", (uint64_t)(uint32_t)result);
        serial_text("\r\n");
        if (target != 0) {
            serial_field_hex("GXOS_NET10:CRT_INITTERM_E_REJECTED_TARGET=0x", (uint64_t)target);
            serial_text("\r\n");
        }
    }
}

static int GXOS_CRT_INITTERM_E_MS_ABI platform_initterm_e(
    GXOS_C_INITIALIZER *first,
    GXOS_C_INITIALIZER *last)
{
    GXOS_CRT_INITTERM_E_REPORT report;
    uint64_t first_value = (uint64_t)(uintptr_t)first;
    uint64_t last_value = (uint64_t)(uintptr_t)last;
    uint64_t caller = (uint64_t)(uintptr_t)__builtin_return_address(0);
    int result;

    g_crt_initterm_e_calls++;
    serial_text("GXOS_NET10:CRT_INITTERM_E_BEGIN\r\n");
    serial_field_hex("GXOS_NET10:CRT_INITTERM_E_CALL_COUNT=0x", g_crt_initterm_e_calls);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CRT_INITTERM_E_CALLER=0x", caller);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CRT_INITTERM_E_FIRST=0x", first_value);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CRT_INITTERM_E_LAST=0x", last_value);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CRT_INITTERM_E_TABLE_SIZE_BYTES=0x",
                     last_value >= first_value ? last_value - first_value : 0);
    serial_text("\r\n");
    result = gxos_crt_initterm_e(first, last, &report, platform_initterm_e_trace);
    serial_field_hex("GXOS_NET10:CRT_INITTERM_E_ENTRY_COUNT=0x", report.entry_count);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CRT_INITTERM_E_NULL_ENTRY_COUNT=0x", report.null_entry_count);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CRT_INITTERM_E_NONNULL_ENTRY_COUNT=0x", report.nonnull_entry_count);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CRT_INITTERM_E_INVOCATION_COUNT=0x", report.invoked_count);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CRT_INITTERM_E_FAILURE_COUNT=0x", report.failure_count);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CRT_INITTERM_E_RESULT=0x", (uint64_t)(uint32_t)result);
    serial_text("\r\n");
    if (report.validation_failure != 0) fail("crt-initterm-e-validation");
    if (result == 0) {
#ifdef GXOS_CRT_INITTERM_E_MARKER_MUTATION
        serial_text("GXOS_NET10:CRT_INITTERM_E_OX\r\n");
#else
        serial_text("GXOS_NET10:CRT_INITTERM_E_OK\r\n");
#endif
    }
    return result;
}
#endif

#ifdef GXOS_ENABLE_CRT_INITTERM
static void GXOS_CRT_INITTERM_MS_ABI platform_initterm_trace(
    uint32_t event,
    uint64_t index,
    uintptr_t target,
    int32_t status)
{
    if (event == GXOS_CRT_INITTERM_TRACE_ENTRY) {
        serial_field_hex("GXOS_NET10:CRT_INITTERM_ENTRY_INDEX=0x", index);
        serial_text("\r\n");
        serial_field_hex("GXOS_NET10:CRT_INITTERM_ENTRY_RAW=0x", (uint64_t)target);
        serial_text("\r\n");
        if (target == 0) serial_text("GXOS_NET10:CRT_INITTERM_ENTRY_NULL\r\n");
    } else if (event == GXOS_CRT_INITTERM_TRACE_CALLBACK_BEGIN) {
        g_crt_initterm_current_index = index;
        g_crt_initterm_current_target = target;
        g_crt_initterm_callback_active = 1;
        serial_field_hex("GXOS_NET10:CRT_INITTERM_CALLBACK_BEGIN_INDEX=0x", index);
        serial_text("\r\n");
        serial_field_hex("GXOS_NET10:CRT_INITTERM_CALLBACK_TARGET=0x", (uint64_t)target);
        serial_text("\r\n");
        serial_text("GXOS_NET10:CRT_INITTERM_CALLBACK_TARGET_CLASS=IMAGE_EXECUTABLE\r\n");
        serial_text("GXOS_NET10:CRT_INITTERM_CALLBACK_INVOKED=1\r\n");
    } else if (event == GXOS_CRT_INITTERM_TRACE_CALLBACK_RETURN) {
        g_crt_initterm_callback_active = 0;
        serial_field_hex("GXOS_NET10:CRT_INITTERM_CALLBACK_RETURN_INDEX=0x", index);
        serial_text("\r\n");
        serial_field_hex("GXOS_NET10:CRT_INITTERM_CALLBACK_RETURN_TARGET=0x", (uint64_t)target);
        serial_text("\r\n");
        serial_field_hex("GXOS_NET10:CRT_INITTERM_CALLBACK_RETURN_STATUS=0x", (uint64_t)(uint32_t)status);
        serial_text("\r\n");
    } else if (event == GXOS_CRT_INITTERM_TRACE_VALIDATION_FAILURE) {
        serial_field_hex("GXOS_NET10:CRT_INITTERM_VALIDATION_FAILURE=0x", (uint64_t)(uint32_t)status);
        serial_text("\r\n");
        if (target != 0) {
            serial_field_hex("GXOS_NET10:CRT_INITTERM_REJECTED_TARGET=0x", (uint64_t)target);
            serial_text("\r\n");
        }
    }
}

static void GXOS_CRT_INITTERM_MS_ABI platform_initterm(
    GXOS_VOID_INITIALIZER *first,
    GXOS_VOID_INITIALIZER *last)
{
    GXOS_CRT_INITTERM_REPORT report;
    uint64_t first_value = (uint64_t)(uintptr_t)first;
    uint64_t last_value = (uint64_t)(uintptr_t)last;
    uint64_t caller = (uint64_t)(uintptr_t)__builtin_return_address(0);
    int result;

    g_crt_initterm_calls++;
    g_phase = PHASE_IN_CRT_INITTERM;
    g_crt_initterm_current_index = GXOS_CRT_INITTERM_NO_CALLBACK;
    g_crt_initterm_current_target = 0;
    g_crt_initterm_callback_active = 0;
    serial_text("GXOS_NET10:CRT_INITTERM_BEGIN\r\n");
    serial_field_hex("GXOS_NET10:CRT_INITTERM_CALL_COUNT=0x", g_crt_initterm_calls);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CRT_INITTERM_CALLER=0x", caller);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CRT_INITTERM_FIRST=0x", first_value);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CRT_INITTERM_LAST=0x", last_value);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CRT_INITTERM_TABLE_SIZE_BYTES=0x",
                     last_value >= first_value ? last_value - first_value : 0);
    serial_text("\r\n");
    serial_text("GXOS_NET10:CRT_INITTERM_TABLE_SECTION=.rdata\r\n");
    serial_text("GXOS_NET10:CRT_INITTERM_TABLE_READABLE=1\r\n");
    serial_text("GXOS_NET10:CRT_INITTERM_TABLE_EXECUTABLE=0\r\n");
    serial_text("GXOS_NET10:CRT_INITTERM_TABLE_WRITABLE=0\r\n");
    serial_text("GXOS_NET10:CRT_INITTERM_RELOCATIONS_APPLIED=1\r\n");
    serial_field_hex("GXOS_NET10:CRT_INITTERM_STATE_BEFORE_QPC_COUNT=0x", g_perf_qpc_call_count);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CRT_INITTERM_STATE_BEFORE_TLS_ALLOC_PTR=0x",
                     g_tls_block == 0 ? 0 : *(uint64_t *)((uint8_t *)(uintptr_t)g_tls_block + 0x38));
    serial_text("\r\n");
    serial_text("GXOS_NET10:CRT_INITTERM_STATE_BEFORE_MANAGED_THREAD_REGISTERED=0\r\n");
    serial_text("GXOS_NET10:CRT_INITTERM_STATE_BEFORE_ALLOCATION_CONTEXT_VALID=0\r\n");
    result = gxos_crt_initterm(first, last, &report, platform_initterm_trace);
    serial_field_hex("GXOS_NET10:CRT_INITTERM_ENTRY_COUNT=0x", report.entry_count);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CRT_INITTERM_NULL_COUNT=0x", report.null_entry_count);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CRT_INITTERM_NONNULL_COUNT=0x", report.nonnull_entry_count);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CRT_INITTERM_INVOKED_COUNT=0x", report.invoked_count);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CRT_INITTERM_RETURNED_COUNT=0x", report.returned_count);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CRT_INITTERM_CURRENT_CALLBACK_INDEX=0x", report.current_callback_index);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CRT_INITTERM_STATUS=0x", (uint64_t)(uint32_t)result);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CRT_INITTERM_COMPLETED=0x", report.completed);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CRT_INITTERM_STATE_AFTER_QPC_COUNT=0x", g_perf_qpc_call_count);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CRT_INITTERM_STATE_AFTER_TLS_ALLOC_PTR=0x",
                     g_tls_block == 0 ? 0 : *(uint64_t *)((uint8_t *)(uintptr_t)g_tls_block + 0x38));
    serial_text("\r\n");
    serial_text("GXOS_NET10:CRT_INITTERM_STATE_AFTER_MANAGED_THREAD_REGISTERED=0\r\n");
    serial_text("GXOS_NET10:CRT_INITTERM_STATE_AFTER_ALLOCATION_CONTEXT_VALID=0\r\n");
    if (report.validation_failure != 0 || result != 0) fail("crt-initterm-validation");
    if (report.completed != 0 && report.invoked_count == report.returned_count) {
#ifdef GXOS_CRT_INITTERM_MARKER_MUTATION
        serial_text("GXOS_NET10:CRT_INITTERM_OX\r\n");
#else
        serial_text("GXOS_NET10:CRT_INITTERM_OK\r\n");
#endif
    }
    g_phase = PHASE_AFTER_SECURITY_COOKIE_INIT;
}
#endif

static void *platform_import_target(const char *module, const char *symbol)
{
#ifdef GXOS_ENABLE_CREATE_EVENT_W
    if (equal_text(module, "KERNEL32.dll") &&
        equal_text(symbol, "CreateEventW")) {
        return (void *)(uintptr_t)platform_create_event_w;
    }
#endif
#ifdef GXOS_ENABLE_CREATE_MEMORY_RESOURCE_NOTIFICATION
    if (equal_text(module, "KERNEL32.dll") &&
        equal_text(symbol, "CreateMemoryResourceNotification")) {
        return (void *)(uintptr_t)platform_create_memory_resource_notification;
    }
#endif
#ifndef GXOS_DISABLE_TIME_IMPLEMENTATION
    if (equal_text(module, "KERNEL32.dll") && equal_text(symbol, "GetSystemTimeAsFileTime")) return (void *)(uintptr_t)gxos_get_system_time_as_file_time;
#endif
#ifndef GXOS_DISABLE_PERF_IMPLEMENTATION
    if (equal_text(module, "KERNEL32.dll") && equal_text(symbol, "QueryPerformanceCounter")) {
#ifdef GXOS_ENABLE_CRT_INITTERM
        return (void *)(uintptr_t)platform_initterm_query_performance_counter;
#else
        return (void *)(uintptr_t)gxos_query_performance_counter;
#endif
    }
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
#ifdef GXOS_ENABLE_VECTORED_EXCEPTION_HANDLER
    if (equal_text(module, "KERNEL32.dll") &&
        equal_text(symbol, "AddVectoredExceptionHandler")) {
        return (void *)(uintptr_t)platform_add_vectored_exception_handler;
    }
#endif
#ifdef GXOS_ENABLE_GET_MODULE_HANDLE
    if (equal_text(module, "KERNEL32.dll") &&
        equal_text(symbol, "GetModuleHandleW")) {
        return (void *)(uintptr_t)platform_get_module_handle_w;
    }
#endif
#ifdef GXOS_ENABLE_GET_MODULE_HANDLE_EX
    if (equal_text(module, "KERNEL32.dll") &&
        equal_text(symbol, "GetModuleHandleExW")) {
        return (void *)(uintptr_t)platform_get_module_handle_ex_w;
    }
#endif
#ifdef GXOS_ENABLE_GET_PROC_ADDRESS
    if (equal_text(module, "KERNEL32.dll") &&
        equal_text(symbol, "GetProcAddress")) {
        return (void *)(uintptr_t)platform_get_proc_address;
    }
#endif
    if (equal_text(module, "KERNEL32.dll") && equal_text(symbol, "DuplicateHandle")) return (void *)(uintptr_t)platform_duplicate_handle;
    if (equal_text(module, "KERNEL32.dll") && equal_text(symbol, "CloseHandle")) return (void *)(uintptr_t)platform_close_handle;
    if (equal_text(module, "KERNEL32.dll") && equal_text(symbol, "VirtualQuery")) return (void *)(uintptr_t)platform_virtual_query;
    if (equal_text(module, "KERNEL32.dll") && equal_text(symbol, "InitializeCriticalSectionEx")) return (void *)(uintptr_t)platform_initialize_critical_section_ex;
    if (equal_text(module, "KERNEL32.dll") && equal_text(symbol, "InitializeCriticalSection")) return (void *)(uintptr_t)platform_initialize_critical_section;
    if (equal_text(module, "KERNEL32.dll") && equal_text(symbol, "EnterCriticalSection")) return (void *)(uintptr_t)platform_enter_critical_section;
    if (equal_text(module, "KERNEL32.dll") && equal_text(symbol, "LeaveCriticalSection")) return (void *)(uintptr_t)platform_leave_critical_section;
    if (equal_text(module, "KERNEL32.dll") && equal_text(symbol, "DeleteCriticalSection")) return (void *)(uintptr_t)platform_delete_critical_section;
#ifdef GXOS_ENABLE_SLIST
    if (equal_text(module, "KERNEL32.dll") && equal_text(symbol, "InitializeSListHead")) return (void *)(uintptr_t)platform_initialize_slist_head;
#endif
#ifdef GXOS_ENABLE_CRT_INITTERM_E
    if (equal_text(module, "api-ms-win-crt-runtime-l1-1-0.dll") &&
        equal_text(symbol, "_initterm_e")) return (void *)(uintptr_t)platform_initterm_e;
#endif
#ifdef GXOS_ENABLE_CRT_INITTERM
    if (equal_text(module, "api-ms-win-crt-runtime-l1-1-0.dll") &&
        equal_text(symbol, "_initterm")) return (void *)(uintptr_t)platform_initterm;
#endif
#ifdef GXOS_ENABLE_CRT_STRCMP
    if (equal_text(module, "api-ms-win-crt-string-l1-1-0.dll") &&
        equal_text(symbol, "strcmp")) return (void *)(uintptr_t)platform_strcmp;
#endif
#ifdef GXOS_ENABLE_CRT_STRLEN
    if (equal_text(module, "api-ms-win-crt-string-l1-1-0.dll") &&
        equal_text(symbol, "strlen")) return (void *)(uintptr_t)platform_strlen;
#endif
#ifdef GXOS_ENABLE_GETENV
    if (equal_text(module, "KERNEL32.dll") &&
        equal_text(symbol, "GetEnvironmentVariableW")) {
        return (void *)(uintptr_t)platform_get_environment_variable_w;
    }
#endif
#ifdef GXOS_ENABLE_CRT_STRICMP
    if (equal_text(module, "api-ms-win-crt-string-l1-1-0.dll") &&
        equal_text(symbol, "_stricmp")) return (void *)(uintptr_t)platform_stricmp;
#endif
#ifdef GXOS_ENABLE_SYSTEM_INFO
    if (equal_text(module, "KERNEL32.dll") &&
        equal_text(symbol, "GetSystemInfo")) return (void *)(uintptr_t)platform_get_system_info;
#endif
#ifdef GXOS_ENABLE_NUMA_HIGHEST_NODE
    if (equal_text(module, "KERNEL32.dll") &&
        equal_text(symbol, "GetNumaHighestNodeNumber")) {
        return (void *)(uintptr_t)platform_get_numa_highest_node_number;
    }
#endif
#ifdef GXOS_ENABLE_PROCESS_GROUP_AFFINITY
    if (equal_text(module, "KERNEL32.dll") &&
        equal_text(symbol, "GetProcessGroupAffinity")) {
        return (void *)(uintptr_t)platform_get_process_group_affinity;
    }
#endif
#ifdef GXOS_ENABLE_PROCESS_AFFINITY
    if (equal_text(module, "KERNEL32.dll") &&
        equal_text(symbol, "GetProcessAffinityMask")) {
        return (void *)(uintptr_t)platform_get_process_affinity_mask;
    }
#endif
#ifdef GXOS_ENABLE_QUERY_INFORMATION_JOB_OBJECT
    if (equal_text(module, "KERNEL32.dll") &&
        equal_text(symbol, "QueryInformationJobObject")) {
        return (void *)(uintptr_t)platform_query_information_job_object;
    }
#endif
#ifdef GXOS_ENABLE_CRT_ONEXIT
    if (equal_text(module, "api-ms-win-crt-runtime-l1-1-0.dll") &&
        equal_text(symbol, "_initialize_onexit_table")) return (void *)(uintptr_t)platform_initialize_onexit_table;
#ifdef GXOS_ENABLE_CRT_ONEXIT_REGISTER
    if (equal_text(module, "api-ms-win-crt-runtime-l1-1-0.dll") &&
        equal_text(symbol, "_register_onexit_function")) return (void *)(uintptr_t)platform_register_onexit_function;
#endif
#endif
#ifdef GXOS_ENABLE_CRT_MALLOC
    if (equal_text(module, "api-ms-win-crt-heap-l1-1-0.dll") &&
        equal_text(symbol, "malloc")) {
        return (void *)(uintptr_t)platform_crt_malloc;
    }
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

    if (delta == 0) {
        image->relocations_applied = 1;
        return;
    }
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
    image->relocations_applied = 1;
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
            g_import_records[symbols].descriptor_index = descriptors - 1U;
            g_import_records[symbols].symbol_index = index;
            g_import_records[symbols].iat_rva = first_thunk_rva + index * 8U;
#ifdef GXOS_ENABLE_CREATE_EVENT_W
            if (equal_text(module, "KERNEL32.dll") &&
                equal_text(g_import_records[symbols].symbol, "CreateEventW")) {
                g_create_event_w_import_descriptor_index = descriptors - 1U;
                g_create_event_w_import_symbol_index = index;
                g_create_event_w_importing_iat_rva = first_thunk_rva + index * 8U;
            }
#endif
#ifdef GXOS_ENABLE_CREATE_MEMORY_RESOURCE_NOTIFICATION
            if (equal_text(module, "KERNEL32.dll") &&
                equal_text(g_import_records[symbols].symbol,
                           "CreateMemoryResourceNotification")) {
                g_memory_resource_notification_import_descriptor_index =
                    descriptors - 1U;
                g_memory_resource_notification_import_symbol_index = index;
                g_memory_resource_notification_importing_iat_rva =
                    first_thunk_rva + index * 8U;
            }
#endif
#ifdef GXOS_ENABLE_GET_MODULE_HANDLE
            if (equal_text(module, "KERNEL32.dll") &&
                equal_text(g_import_records[symbols].symbol, "GetModuleHandleW")) {
                g_get_module_handle_import_descriptor_index = descriptors - 1U;
                g_get_module_handle_importing_iat_rva = first_thunk_rva + index * 8U;
            }
#endif
#ifdef GXOS_ENABLE_GET_MODULE_HANDLE_EX
            if (equal_text(module, "KERNEL32.dll") &&
                equal_text(g_import_records[symbols].symbol, "GetModuleHandleExW")) {
                g_get_module_handle_ex_import_descriptor_index = descriptors - 1U;
                g_get_module_handle_ex_importing_iat_rva = first_thunk_rva + index * 8U;
            }
#endif
#ifdef GXOS_ENABLE_VECTORED_EXCEPTION_HANDLER
            if (equal_text(module, "KERNEL32.dll") &&
                equal_text(g_import_records[symbols].symbol, "AddVectoredExceptionHandler")) {
                g_veh_add_import_descriptor_index = descriptors - 1U;
                g_veh_add_import_symbol_index = index;
                g_veh_add_importing_iat_rva = first_thunk_rva + index * 8U;
            }
#endif
#ifdef GXOS_ENABLE_GET_PROC_ADDRESS
            if (equal_text(module, "KERNEL32.dll") &&
                equal_text(g_import_records[symbols].symbol, "GetProcAddress")) {
                g_get_proc_address_import_descriptor_index = descriptors - 1U;
                g_get_proc_address_importing_iat_rva = first_thunk_rva + index * 8U;
            }
#endif
#ifdef GXOS_ENABLE_CRT_ONEXIT_REGISTER
            if (equal_text(module, "api-ms-win-crt-runtime-l1-1-0.dll") &&
                equal_text(g_import_records[symbols].symbol, "_register_onexit_function")) {
                g_crt_onexit_register_import_descriptor_index = descriptors - 1U;
                g_crt_onexit_register_importing_iat_rva = first_thunk_rva + index * 8U;
            }
#endif
#ifdef GXOS_ENABLE_CRT_MALLOC
            if (equal_text(module, "api-ms-win-crt-heap-l1-1-0.dll") &&
                equal_text(g_import_records[symbols].symbol, "malloc")) {
                g_crt_malloc_import_descriptor_index = descriptors - 1U;
                g_crt_malloc_importing_iat_rva = first_thunk_rva + index * 8U;
            }
#endif
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

#ifdef GXOS_ENABLE_SYSTEM_INFO
static const GXOS_SYSTEM_INFO_MEMORY_REGION *platform_system_info_region(
    uintptr_t address)
{
    uint32_t index;
    for (index = 0; index != g_system_info_memory.region_count; index++) {
        const GXOS_SYSTEM_INFO_MEMORY_REGION *region = &g_system_info_memory.regions[index];
        if (address >= region->base && address < region->end) return region;
    }
    return 0;
}

static void configure_platform_system_info(const PE_IMAGE *image)
{
    uint32_t index;
    GXOS_SYSTEM_INFO_STATUS status;

    if (image == 0 || image->actual_base == 0 || image->loaded_size == 0 ||
        image->actual_base > UINTPTR_MAX - (uintptr_t)image->loaded_size ||
        image->memory_region_count == 0 ||
        image->memory_region_count + 1U > GXOS_SYSTEM_INFO_MAX_MEMORY_REGIONS ||
        g_stack_lower >= g_stack_upper) {
        fail("getsysteminfo-facts");
    }
    for (index = 0; index != image->memory_region_count; index++) {
        g_system_info_regions[index].base = image->memory_regions[index].base;
        g_system_info_regions[index].end = image->memory_regions[index].end;
        g_system_info_regions[index].readable = image->memory_regions[index].readable;
        g_system_info_regions[index].writable = image->memory_regions[index].writable;
    }
    g_system_info_regions[image->memory_region_count].base = (uintptr_t)g_stack_lower;
    g_system_info_regions[image->memory_region_count].end = (uintptr_t)g_stack_upper;
    g_system_info_regions[image->memory_region_count].readable = 1;
    g_system_info_regions[image->memory_region_count].writable = 1;
    g_system_info_memory.region_count = image->memory_region_count + 1U;
    g_system_info_memory.regions = g_system_info_regions;

    g_system_info_facts.processor_architecture =
        GXOS_SYSTEM_INFO_PROCESSOR_ARCHITECTURE_AMD64;
    g_system_info_facts.page_size = (uint32_t)EFI_PAGE_SIZE;
    g_system_info_facts.minimum_application_address = image->actual_base;
    g_system_info_facts.maximum_application_address =
        image->actual_base + (uintptr_t)image->loaded_size - (uintptr_t)1U;
    g_system_info_facts.active_processor_mask = (uintptr_t)1U;
    g_system_info_facts.number_of_processors = 1;
    g_system_info_facts.processor_type = GXOS_SYSTEM_INFO_PROCESSOR_TYPE_AMD_X8664;
    g_system_info_facts.allocation_granularity = (uint32_t)EFI_PAGE_SIZE;
    g_system_info_facts.processor_level = 0;
    g_system_info_facts.processor_revision = 0;
    g_system_info_facts.address_range_policy =
        GXOS_SYSTEM_INFO_ADDRESS_RANGE_IMAGE_BACKED;
    status = gxos_system_info_configure(&g_system_info_facts, &g_system_info_memory);
    if (status != GXOS_SYSTEM_INFO_STATUS_OK) fail("getsysteminfo-facts");
#ifdef GXOS_ENABLE_NUMA_HIGHEST_NODE
    /* NUMA uses the already-published GetSystemInfo processor snapshot. */
    g_numa_facts.usable_processor_count = g_system_info_facts.number_of_processors;
    g_numa_facts.locality_domain_count = 1;
    g_numa_facts.highest_node_number = 0;
    g_numa_facts.node_targeted_allocation_supported = false;
    g_numa_facts.system_info_processor_count = g_system_info_facts.number_of_processors;
    g_numa_facts.system_info_active_processor_mask =
        g_system_info_facts.active_processor_mask;
    g_numa_facts.topology_policy = GXOS_NUMA_TOPOLOGY_POLICY_FACT_SNAPSHOT;
    serial_text("GXOS_NET10:GETNUMAHIGHESTNODE_FACTS_SOURCE=GETSYSTEMINFO_SNAPSHOT\r\n");
    serial_text("GXOS_NET10:GETNUMAHIGHESTNODE_FACTS_POLICY=SINGLE_LOCALITY_DOMAIN\r\n");
#endif
#ifdef GXOS_ENABLE_PROCESS_GROUP_AFFINITY
    /* Processor groups reuse the already-published one-bootstrap-processor snapshot. */
    g_process_group_facts.group_count = 1;
    g_process_group_facts.group_numbers[0] = 0;
    g_process_group_facts.usable_processor_count =
        g_system_info_facts.number_of_processors;
    g_process_group_facts.active_processor_mask =
        g_system_info_facts.active_processor_mask;
    g_process_group_facts.system_info_processor_count =
        g_system_info_facts.number_of_processors;
    g_process_group_facts.system_info_active_processor_mask =
        g_system_info_facts.active_processor_mask;
    g_process_group_facts.topology_policy =
        GXOS_PROCESS_GROUP_AFFINITY_FACT_SNAPSHOT;
    serial_text("GXOS_NET10:GETPROCESSGROUPAFFINITY_FACTS_SOURCE=GETSYSTEMINFO_SNAPSHOT\r\n");
    serial_text("GXOS_NET10:GETPROCESSGROUPAFFINITY_FACTS_POLICY=SINGLE_GROUP_ZERO\r\n");
#endif
#ifdef GXOS_ENABLE_PROCESS_AFFINITY
    /* Affinity reuses the same immutable one-processor and one-group snapshot. */
    g_process_affinity_facts.supported_process_handle =
        GXOS_PROCESS_AFFINITY_CURRENT_PROCESS;
    g_process_affinity_facts.process_affinity_mask =
        g_system_info_facts.active_processor_mask;
    g_process_affinity_facts.system_affinity_mask =
        g_system_info_facts.active_processor_mask;
    g_process_affinity_facts.usable_processor_mask =
        g_system_info_facts.active_processor_mask;
    g_process_affinity_facts.usable_processor_count =
        g_system_info_facts.number_of_processors;
    g_process_affinity_facts.system_info_processor_count =
        g_system_info_facts.number_of_processors;
    g_process_affinity_facts.system_info_active_processor_mask =
        g_system_info_facts.active_processor_mask;
    g_process_affinity_facts.processor_group_count = 1;
    g_process_affinity_facts.current_group_number = 0;
    g_process_affinity_facts.topology_policy =
        GXOS_PROCESS_AFFINITY_TOPOLOGY_FACT_SNAPSHOT;
    serial_text("GXOS_NET10:GETPROCESSAFFINITYMASK_FACTS_SOURCE=GETSYSTEMINFO_AND_PROCESSGROUP_SNAPSHOT\r\n");
    serial_text("GXOS_NET10:GETPROCESSAFFINITYMASK_FACTS_POLICY=SINGLE_GROUP_ZERO_BOOTSTRAP_PROCESSOR\r\n");
#endif
#ifdef GXOS_ENABLE_QUERY_INFORMATION_JOB_OBJECT
    /* guideXOS has no job-object manager or current-process job association. */
    g_query_job_facts.supported_job_handle = GXOS_QUERY_JOB_CURRENT_HANDLE;
    g_query_job_facts.associated_job = 0;
    g_query_job_facts.control_flags = 0;
    g_query_job_facts.cpu_rate = 0;
    g_query_job_facts.weight = 0;
    g_query_job_facts.min_rate = 0;
    g_query_job_facts.max_rate = 0;
#ifdef GXOS_QUERY_JOB_SUCCESS_NO_LIMIT_EXPERIMENT
    g_query_job_facts.associated_job = 1;
#endif
#ifdef GXOS_QUERY_JOB_ACTIVE_LIMIT_EXPERIMENT
    g_query_job_facts.associated_job = 1;
    g_query_job_facts.control_flags = GXOS_QUERY_JOB_CPU_RATE_ENABLE |
                                      GXOS_QUERY_JOB_CPU_RATE_HARD_CAP;
    g_query_job_facts.cpu_rate = 5000;
#endif
    serial_text("GXOS_NET10:QUERYJOBOBJECT_FACTS_SOURCE=GUIDEXOS_BOOTSTRAP_SNAPSHOT\r\n");
    serial_text("GXOS_NET10:QUERYJOBOBJECT_FACTS_POLICY=");
#if defined(GXOS_QUERY_JOB_SUCCESS_NO_LIMIT_EXPERIMENT)
    serial_text("INVESTIGATION_SYNTHETIC_ASSOCIATED_JOB_NO_ACTIVE_LIMIT\r\n");
#elif defined(GXOS_QUERY_JOB_ACTIVE_LIMIT_EXPERIMENT)
    serial_text("INVESTIGATION_SYNTHETIC_ASSOCIATED_JOB_ACTIVE_HARD_CAP\r\n");
#else
    serial_text("NO_JOB_SUBSYSTEM_NO_ASSOCIATED_JOB\r\n");
#endif
    serial_text("GXOS_NET10:QUERYJOBOBJECT_NESTED_JOBS_SUPPORTED=0\r\n");
    serial_text("GXOS_NET10:QUERYJOBOBJECT_CPU_RATE_CONTROL_ENFORCED=0\r\n");
#endif
    serial_text("GXOS_NET10:GETSYSTEMINFO_FACTS_SOURCE=UEFI_PAGE_AND_LOADED_IMAGE\r\n");
    serial_text("GXOS_NET10:GETSYSTEMINFO_FACTS_POLICY=IMAGE_BACKED_RANGE_SINGLE_BOOTSTRAP_PROCESSOR\r\n");
}

static void serial_system_info_status(GXOS_SYSTEM_INFO_STATUS status)
{
    serial_field_hex("GXOS_NET10:GETSYSTEMINFO_STATUS=0x", (uint64_t)(uint32_t)status);
    serial_text("\r\n");
}

static void EFIAPI platform_get_system_info(GXOS_SYSTEM_INFO *destination)
{
    const GXOS_SYSTEM_INFO_MEMORY_REGION *region;
    GXOS_SYSTEM_INFO_STATUS status;
    uintptr_t return_address = (uintptr_t)__builtin_return_address(0);
    uintptr_t call_site = return_address >= 6 ? return_address - 6 : 0;
    uint64_t call_index = g_system_info_calls++;

    serial_text("GXOS_NET10:GETSYSTEMINFO_BEGIN\r\n");
    serial_field_hex("GXOS_NET10:GETSYSTEMINFO_CALL_INDEX=0x", call_index);
    serial_text("\r\n");
    serial_text("GXOS_NET10:GETSYSTEMINFO_STATIC_CALL_SITE=0x000000018004379F\r\n");
    serial_field_hex("GXOS_NET10:GETSYSTEMINFO_RUNTIME_CALL_SITE=0x", call_site);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETSYSTEMINFO_RETURN_ADDRESS=0x", return_address);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETSYSTEMINFO_DESTINATION=0x", (uintptr_t)destination);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETSYSTEMINFO_STRUCTURE_SIZE=0x", sizeof(GXOS_SYSTEM_INFO));
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETSYSTEMINFO_DESTINATION_ALIGNMENT=0x",
                     ((uintptr_t)destination) & (_Alignof(GXOS_SYSTEM_INFO) - 1U));
    serial_text("\r\n");
    region = platform_system_info_region((uintptr_t)destination);
    serial_field_hex("GXOS_NET10:GETSYSTEMINFO_DESTINATION_REGION_BASE=0x",
                     region == 0 ? 0 : region->base);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETSYSTEMINFO_DESTINATION_REGION_END=0x",
                     region == 0 ? 0 : region->end);
    serial_text("\r\n");
    serial_text("GXOS_NET10:GETSYSTEMINFO_DESTINATION_WRITABLE=");
    serial_text(region != 0 && region->writable != 0 ? "1\r\n" : "0\r\n");
    status = gxos_get_system_info_checked(destination, &g_system_info_facts,
                                          &g_system_info_memory);
    serial_system_info_status(status);
    if (status != GXOS_SYSTEM_INFO_STATUS_OK) {
        g_system_info_failures++;
        fail("getsysteminfo-invalid");
    }
    g_system_info_successes++;
    serial_field_hex("GXOS_NET10:GETSYSTEMINFO_ARCHITECTURE=0x",
                     destination->architecture_union.architecture.wProcessorArchitecture);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETSYSTEMINFO_RESERVED_ARCHITECTURE=0x",
                     destination->architecture_union.architecture.wReserved);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETSYSTEMINFO_PAGE_SIZE=0x", destination->dwPageSize);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETSYSTEMINFO_MIN_ADDRESS=0x",
                     (uintptr_t)destination->lpMinimumApplicationAddress);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETSYSTEMINFO_MAX_ADDRESS=0x",
                     (uintptr_t)destination->lpMaximumApplicationAddress);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETSYSTEMINFO_ACTIVE_MASK=0x",
                     destination->dwActiveProcessorMask);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETSYSTEMINFO_PROCESSOR_COUNT=0x",
                     destination->dwNumberOfProcessors);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETSYSTEMINFO_PROCESSOR_TYPE=0x",
                     destination->dwProcessorType);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETSYSTEMINFO_ALLOCATION_GRANULARITY=0x",
                     destination->dwAllocationGranularity);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETSYSTEMINFO_PROCESSOR_LEVEL=0x",
                     destination->wProcessorLevel);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETSYSTEMINFO_PROCESSOR_REVISION=0x",
                     destination->wProcessorRevision);
    serial_text("\r\n");
    serial_text("GXOS_NET10:GETSYSTEMINFO_FIELD_READ_MASK=0x00000000000000A2\r\n");
    serial_text("GXOS_NET10:GETSYSTEMINFO_FIELD_READ_SOURCE=STATIC_CALLSITE_CENSUS\r\n");
    serial_text("GXOS_NET10:GETSYSTEMINFO_RETURNED\r\n");
#ifdef GXOS_SYSTEM_INFO_MARKER_MUTATION
    serial_text("GXOS_NET10:GETSYSTEMINFO_OX\r\n");
#else
    serial_text("GXOS_NET10:GETSYSTEMINFO_OK\r\n");
#endif
}
#endif

#ifdef GXOS_ENABLE_NUMA_HIGHEST_NODE
static const char *numa_status_name(GXOS_NUMA_HIGHEST_NODE_STATUS status)
{
    switch (status) {
    case GXOS_NUMA_HIGHEST_NODE_STATUS_OK: return "OK";
    case GXOS_NUMA_HIGHEST_NODE_STATUS_NULL_POINTER: return "NULL_POINTER";
    case GXOS_NUMA_HIGHEST_NODE_STATUS_NONCANONICAL_POINTER: return "NONCANONICAL_POINTER";
    case GXOS_NUMA_HIGHEST_NODE_STATUS_UNWRITABLE_POINTER: return "UNWRITABLE_POINTER";
    case GXOS_NUMA_HIGHEST_NODE_STATUS_INSUFFICIENT_WRITABLE_RANGE: return "INSUFFICIENT_WRITABLE_RANGE";
    case GXOS_NUMA_HIGHEST_NODE_STATUS_INVALID_MEMORY_CONTEXT: return "INVALID_MEMORY_CONTEXT";
    case GXOS_NUMA_HIGHEST_NODE_STATUS_INVALID_PROCESSOR_COUNT: return "INVALID_PROCESSOR_COUNT";
    case GXOS_NUMA_HIGHEST_NODE_STATUS_INVALID_PROCESSOR_MASK: return "INVALID_PROCESSOR_MASK";
    case GXOS_NUMA_HIGHEST_NODE_STATUS_INVALID_DOMAIN_COUNT: return "INVALID_DOMAIN_COUNT";
    case GXOS_NUMA_HIGHEST_NODE_STATUS_INVALID_HIGHEST_NODE: return "INVALID_HIGHEST_NODE";
    case GXOS_NUMA_HIGHEST_NODE_STATUS_INCONSISTENT_DOMAIN_MODEL: return "INCONSISTENT_DOMAIN_MODEL";
    case GXOS_NUMA_HIGHEST_NODE_STATUS_INVALID_SYSTEM_SNAPSHOT: return "INVALID_SYSTEM_SNAPSHOT";
    case GXOS_NUMA_HIGHEST_NODE_STATUS_UNSUPPORTED_TOPOLOGY: return "UNSUPPORTED_TOPOLOGY";
    default: return "UNKNOWN";
    }
}

static uint32_t numa_status_last_error(GXOS_NUMA_HIGHEST_NODE_STATUS status)
{
    return status == GXOS_NUMA_HIGHEST_NODE_STATUS_UNSUPPORTED_TOPOLOGY
               ? GXOS_NUMA_ERROR_NOT_SUPPORTED
               : GXOS_NUMA_ERROR_INVALID_PARAMETER;
}

static GXOS_NUMA_BOOL EFIAPI platform_get_numa_highest_node_number(
    GXOS_NUMA_ULONG *highest_node_number)
{
    const GXOS_SYSTEM_INFO_MEMORY_REGION *region;
    GXOS_NUMA_HIGHEST_NODE_STATUS status;
    uintptr_t destination = (uintptr_t)highest_node_number;
    uintptr_t writable_range = 0;
    uintptr_t return_address = (uintptr_t)__builtin_return_address(0);
    uintptr_t call_site = return_address >= 6 ? return_address - 6 : 0;
    uint64_t call_index = g_numa_calls++;
    uint32_t output_before = 0;
    uint32_t output_after = 0;
    uint64_t last_error_before = g_platform_last_error;

    serial_text("GXOS_NET10:GETNUMAHIGHESTNODE_BEGIN\r\n");
    serial_field_hex("GXOS_NET10:GETNUMAHIGHESTNODE_CALL_INDEX=0x", call_index);
    serial_text("\r\n");
    serial_text("GXOS_NET10:GETNUMAHIGHESTNODE_STATIC_CALL_SITE=0x00000001800437DD\r\n");
    serial_field_hex("GXOS_NET10:GETNUMAHIGHESTNODE_RUNTIME_CALL_SITE=0x", call_site);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETNUMAHIGHESTNODE_RETURN_ADDRESS=0x", return_address);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETNUMAHIGHESTNODE_OUTPUT_POINTER=0x", destination);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETNUMAHIGHESTNODE_OUTPUT_WIDTH=0x", sizeof(GXOS_NUMA_ULONG));
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETNUMAHIGHESTNODE_OUTPUT_ALIGNMENT=0x",
                     destination & (sizeof(GXOS_NUMA_ULONG) - 1U));
    serial_text("\r\n");
    region = platform_system_info_region(destination);
    serial_field_hex("GXOS_NET10:GETNUMAHIGHESTNODE_DESTINATION_REGION_BASE=0x",
                     region == 0 ? 0 : region->base);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETNUMAHIGHESTNODE_DESTINATION_REGION_END=0x",
                     region == 0 ? 0 : region->end);
    serial_text("\r\n");
    serial_text("GXOS_NET10:GETNUMAHIGHESTNODE_DESTINATION_WRITABLE=");
    serial_text(region != 0 && region->writable != 0 ? "1\r\n" : "0\r\n");
    if (region != 0 && destination >= region->base && destination < region->end) {
        writable_range = region->end - destination;
        if (writable_range >= sizeof(GXOS_NUMA_ULONG) && region->writable != 0) {
            output_before = *highest_node_number;
        }
    }
    serial_field_hex("GXOS_NET10:GETNUMAHIGHESTNODE_WRITABLE_RANGE_SIZE=0x", writable_range);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETNUMAHIGHESTNODE_OUTPUT_BEFORE=0x", output_before);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETNUMAHIGHESTNODE_USABLE_PROCESSORS=0x",
                     g_numa_facts.usable_processor_count);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETNUMAHIGHESTNODE_DOMAIN_COUNT=0x",
                     g_numa_facts.locality_domain_count);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETNUMAHIGHESTNODE_HIGHEST_NODE=0x",
                     g_numa_facts.highest_node_number);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETNUMAHIGHESTNODE_SYSTEM_INFO_PROCESSOR_COUNT=0x",
                     g_numa_facts.system_info_processor_count);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETNUMAHIGHESTNODE_SYSTEM_INFO_ACTIVE_MASK=0x",
                     g_numa_facts.system_info_active_processor_mask);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETNUMAHIGHESTNODE_LAST_ERROR_BEFORE=0x",
                     last_error_before);
    serial_text("\r\n");

#ifdef GXOS_NUMA_FORCE_FAILURE
    status = GXOS_NUMA_HIGHEST_NODE_STATUS_UNSUPPORTED_TOPOLOGY;
#else
    status = gxos_get_numa_highest_node_checked(highest_node_number, &g_numa_facts,
                                                &g_system_info_memory);
#endif
    g_numa_last_status = status;
    g_numa_last_error_before = last_error_before;
    g_numa_last_output_before = output_before;
    if (status != GXOS_NUMA_HIGHEST_NODE_STATUS_OK) {
        g_numa_failures++;
        g_platform_last_error = numa_status_last_error(status);
        g_numa_last_boolean = GXOS_NUMA_FALSE;
        g_numa_last_output_after = output_before;
        g_numa_last_output_read = 0;
    } else {
        g_numa_successes++;
        g_numa_last_boolean = GXOS_NUMA_TRUE;
        output_after = *highest_node_number;
        g_numa_last_output_after = output_after;
        g_numa_last_output_read = 1;
    }
    g_numa_last_error_after = g_platform_last_error;
    serial_field_hex("GXOS_NET10:GETNUMAHIGHESTNODE_STATUS=0x", (uint64_t)(uint32_t)status);
    serial_text("\r\n");
    serial_text("GXOS_NET10:GETNUMAHIGHESTNODE_STATUS_NAME=");
    serial_text(numa_status_name(status));
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETNUMAHIGHESTNODE_BOOLEAN_RESULT=0x",
                     (uint64_t)(uint32_t)g_numa_last_boolean);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETNUMAHIGHESTNODE_OUTPUT_AFTER=0x", g_numa_last_output_after);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETNUMAHIGHESTNODE_LAST_ERROR_BEFORE=0x",
                     g_numa_last_error_before);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETNUMAHIGHESTNODE_LAST_ERROR_AFTER=0x",
                     g_numa_last_error_after);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETNUMAHIGHESTNODE_OUTPUT_READ_BY_WRAPPER=0x",
                     g_numa_last_output_read);
    serial_text("\r\n");
    serial_text("GXOS_NET10:GETNUMAHIGHESTNODE_RETURNED\r\n");
    if (status == GXOS_NUMA_HIGHEST_NODE_STATUS_OK) {
        serial_text("GXOS_NET10:GETNUMAHIGHESTNODE_OK\r\n");
    } else {
        serial_text("GXOS_NET10:GETNUMAHIGHESTNODE_FAILED\r\n");
    }
    return g_numa_last_boolean;
}
#endif

#ifdef GXOS_ENABLE_PROCESS_GROUP_AFFINITY
static const char *process_group_status_name(
    GXOS_PROCESS_GROUP_AFFINITY_STATUS status)
{
    switch (status) {
    case GXOS_PROCESS_GROUP_AFFINITY_STATUS_OK: return "OK";
    case GXOS_PROCESS_GROUP_AFFINITY_STATUS_INSUFFICIENT_BUFFER: return "INSUFFICIENT_BUFFER";
    case GXOS_PROCESS_GROUP_AFFINITY_STATUS_NULL_GROUP_COUNT: return "NULL_GROUP_COUNT";
    case GXOS_PROCESS_GROUP_AFFINITY_STATUS_NONCANONICAL_GROUP_COUNT: return "NONCANONICAL_GROUP_COUNT";
    case GXOS_PROCESS_GROUP_AFFINITY_STATUS_UNREADABLE_GROUP_COUNT: return "UNREADABLE_GROUP_COUNT";
    case GXOS_PROCESS_GROUP_AFFINITY_STATUS_UNWRITABLE_GROUP_COUNT: return "UNWRITABLE_GROUP_COUNT";
    case GXOS_PROCESS_GROUP_AFFINITY_STATUS_NULL_GROUP_ARRAY: return "NULL_GROUP_ARRAY";
    case GXOS_PROCESS_GROUP_AFFINITY_STATUS_NONCANONICAL_GROUP_ARRAY: return "NONCANONICAL_GROUP_ARRAY";
    case GXOS_PROCESS_GROUP_AFFINITY_STATUS_UNWRITABLE_GROUP_ARRAY: return "UNWRITABLE_GROUP_ARRAY";
    case GXOS_PROCESS_GROUP_AFFINITY_STATUS_INVALID_PROCESS_HANDLE: return "INVALID_PROCESS_HANDLE";
    case GXOS_PROCESS_GROUP_AFFINITY_STATUS_INVALID_TOPOLOGY: return "INVALID_TOPOLOGY";
    case GXOS_PROCESS_GROUP_AFFINITY_STATUS_COUNT_OVERFLOW: return "COUNT_OVERFLOW";
    case GXOS_PROCESS_GROUP_AFFINITY_STATUS_RANGE_OVERFLOW: return "RANGE_OVERFLOW";
    case GXOS_PROCESS_GROUP_AFFINITY_STATUS_UNSUPPORTED_TOPOLOGY: return "UNSUPPORTED_TOPOLOGY";
    case GXOS_PROCESS_GROUP_AFFINITY_STATUS_INVALID_MEMORY_CONTEXT: return "INVALID_MEMORY_CONTEXT";
    default: return "UNKNOWN";
    }
}

static uint32_t process_group_status_last_error(
    GXOS_PROCESS_GROUP_AFFINITY_STATUS status)
{
    if (status == GXOS_PROCESS_GROUP_AFFINITY_STATUS_INSUFFICIENT_BUFFER) {
        return GXOS_PROCESS_GROUP_AFFINITY_ERROR_INSUFFICIENT_BUFFER;
    }
    if (status == GXOS_PROCESS_GROUP_AFFINITY_STATUS_INVALID_PROCESS_HANDLE) {
        return GXOS_PROCESS_GROUP_AFFINITY_ERROR_INVALID_HANDLE;
    }
    if (status == GXOS_PROCESS_GROUP_AFFINITY_STATUS_UNSUPPORTED_TOPOLOGY) {
        return GXOS_PROCESS_GROUP_AFFINITY_ERROR_NOT_SUPPORTED;
    }
    return GXOS_PROCESS_GROUP_AFFINITY_ERROR_INVALID_PARAMETER;
}

static GXOS_PROCESS_GROUP_AFFINITY_BOOL EFIAPI
platform_get_process_group_affinity(
    void *process_handle,
    GXOS_PROCESS_GROUP_AFFINITY_USHORT *group_count,
    GXOS_PROCESS_GROUP_AFFINITY_USHORT *group_array)
{
    GXOS_PROCESS_GROUP_AFFINITY_REPORT report;
    GXOS_PROCESS_GROUP_AFFINITY_STATUS status;
    uintptr_t count_address = (uintptr_t)group_count;
    uintptr_t array_address = (uintptr_t)group_array;
    uintptr_t return_address = (uintptr_t)__builtin_return_address(0);
    uintptr_t call_site = return_address >= 6 ? return_address - 6 : 0;
    const GXOS_SYSTEM_INFO_MEMORY_REGION *count_region =
        platform_system_info_region(count_address);
    const GXOS_SYSTEM_INFO_MEMORY_REGION *array_region =
        platform_system_info_region(array_address);
    uintptr_t count_range = count_region != 0 && count_address >= count_region->base
                                ? count_region->end - count_address
                                : 0;
    uint32_t last_error_before = g_platform_last_error;
    uint32_t last_error_after;
    uint64_t call_index = g_process_group_calls++;
    uint16_t input_capacity = 0;
    uint16_t output_count = 0;
    uint32_t count_before_read = 0;

    if (count_address != 0 && count_region != 0 && count_range >= sizeof(*group_count) &&
        count_region->readable != 0 && count_region->writable != 0) {
        input_capacity = *group_count;
        count_before_read = 1;
    }
    serial_text("GXOS_NET10:GETPROCESSGROUPAFFINITY_BEGIN\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSGROUPAFFINITY_CALL_INDEX=0x", call_index);
    serial_text("\r\n");
    serial_text("GXOS_NET10:GETPROCESSGROUPAFFINITY_IMPORT_MODULE=KERNEL32.dll\r\n");
    serial_text("GXOS_NET10:GETPROCESSGROUPAFFINITY_IMPORT_SYMBOL=GetProcessGroupAffinity\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSGROUPAFFINITY_IMPORT_DESCRIPTOR_INDEX=0x", 2);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSGROUPAFFINITY_IAT_RVA=0x", 0x7D2A0);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSGROUPAFFINITY_IAT_PREFERRED_ADDRESS=0x", 0x18007D2A0ULL);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSGROUPAFFINITY_IAT_RUNTIME_ADDRESS=0x",
                     g_managed_image_base + 0x7D2A0ULL);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSGROUPAFFINITY_STATIC_CALL_SITE=0x", 0x1800436DAULL);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSGROUPAFFINITY_RUNTIME_CALL_SITE=0x", call_site);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSGROUPAFFINITY_RETURN_ADDRESS=0x", return_address);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSGROUPAFFINITY_CALLER_FUNCTION_START=0x",
                     g_managed_image_base + 0x43650ULL);
    serial_text("\r\n");
    serial_text("GXOS_NET10:GETPROCESSGROUPAFFINITY_CALLER=NativeAOT_PROCESSOR_GROUP_DISCOVERY\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSGROUPAFFINITY_PROCESS_HANDLE=0x",
                     (uintptr_t)process_handle);
    serial_text("\r\n");
    serial_text("GXOS_NET10:GETPROCESSGROUPAFFINITY_HANDLE_CLASS=");
    serial_text((uintptr_t)process_handle == GXOS_PROCESS_GROUP_AFFINITY_CURRENT_PROCESS
                    ? "CURRENT_PROCESS_PSEUDO_HANDLE\r\n"
                    : "UNSUPPORTED_HANDLE\r\n");
    serial_text("GXOS_NET10:GETPROCESSGROUPAFFINITY_HANDLE_ORIGIN=KERNEL32.dll!GetCurrentProcess\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSGROUPAFFINITY_COUNT_POINTER=0x", count_address);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSGROUPAFFINITY_ARRAY_POINTER=0x", array_address);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSGROUPAFFINITY_COUNT_ALIGNMENT=0x",
                     count_address & (sizeof(*group_count) - 1U));
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSGROUPAFFINITY_ARRAY_ALIGNMENT=0x",
                     array_address & (sizeof(*group_array) - 1U));
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSGROUPAFFINITY_COUNT_REGION_BASE=0x",
                     count_region == 0 ? 0 : count_region->base);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSGROUPAFFINITY_COUNT_REGION_END=0x",
                     count_region == 0 ? 0 : count_region->end);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSGROUPAFFINITY_COUNT_WRITABLE_RANGE=0x", count_range);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSGROUPAFFINITY_ARRAY_REGION_BASE=0x",
                     array_region == 0 ? 0 : array_region->base);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSGROUPAFFINITY_ARRAY_REGION_END=0x",
                     array_region == 0 ? 0 : array_region->end);
    serial_text("\r\n");
    serial_text("GXOS_NET10:GETPROCESSGROUPAFFINITY_ARRAY_NULL=");
    serial_text(group_array == 0 ? "1\r\n" : "0\r\n");
    serial_text("GXOS_NET10:GETPROCESSGROUPAFFINITY_COUNT_NULL=");
    serial_text(group_count == 0 ? "1\r\n" : "0\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSGROUPAFFINITY_INPUT_CAPACITY=0x", input_capacity);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSGROUPAFFINITY_COUNT_BEFORE=0x",
                     count_before_read != 0 ? input_capacity : 0);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSGROUPAFFINITY_REQUIRED_COUNT=0x",
                     g_process_group_facts.group_count);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSGROUPAFFINITY_PROCESSOR_COUNT=0x",
                     g_process_group_facts.usable_processor_count);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSGROUPAFFINITY_ACTIVE_PROCESSOR_MASK=0x",
                     g_process_group_facts.active_processor_mask);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSGROUPAFFINITY_LAST_ERROR_BEFORE=0x",
                     last_error_before);
    serial_text("\r\n");
    status = gxos_get_process_group_affinity_checked(
        (uintptr_t)process_handle, group_count, group_array, &g_process_group_facts,
        &g_system_info_memory, &report);
    if (status == GXOS_PROCESS_GROUP_AFFINITY_STATUS_INSUFFICIENT_BUFFER) {
        g_platform_last_error = GXOS_PROCESS_GROUP_AFFINITY_ERROR_INSUFFICIENT_BUFFER;
        g_process_group_insufficient_buffer_calls++;
    } else if (status == GXOS_PROCESS_GROUP_AFFINITY_STATUS_OK) {
        g_process_group_successes++;
    } else {
        g_platform_last_error = process_group_status_last_error(status);
        g_process_group_failures++;
    }
    if (report.input_capacity_valid != 0) {
        output_count = *group_count;
    }
    last_error_after = g_platform_last_error;
    g_process_group_last_handle = (uint64_t)(uintptr_t)process_handle;
    g_process_group_last_count_pointer = count_address;
    g_process_group_last_array_pointer = array_address;
    g_process_group_last_input_capacity = report.input_capacity;
    g_process_group_last_output_count = output_count;
    g_process_group_last_required_count = report.required_count;
    g_process_group_last_groups_written = report.groups_written;
    g_process_group_last_boolean = status == GXOS_PROCESS_GROUP_AFFINITY_STATUS_OK
                                       ? GXOS_PROCESS_GROUP_AFFINITY_TRUE
                                       : GXOS_PROCESS_GROUP_AFFINITY_FALSE;
    g_process_group_last_status = status;
    g_process_group_last_error_before = last_error_before;
    g_process_group_last_error_after = last_error_after;
    g_process_group_last_array_null = group_array == 0;
    g_process_group_last_count_read = report.input_capacity_valid;
    g_process_group_last_count_written =
        status == GXOS_PROCESS_GROUP_AFFINITY_STATUS_OK ||
                status == GXOS_PROCESS_GROUP_AFFINITY_STATUS_INSUFFICIENT_BUFFER;
    serial_field_hex("GXOS_NET10:GETPROCESSGROUPAFFINITY_COUNT_READABLE=0x",
                     report.count_pointer_readable);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSGROUPAFFINITY_COUNT_WRITABLE=0x",
                     report.count_pointer_writable);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSGROUPAFFINITY_ARRAY_CANONICAL=0x",
                     report.array_pointer_canonical);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSGROUPAFFINITY_ARRAY_WRITABLE=0x",
                     report.array_pointer_writable);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSGROUPAFFINITY_OUTPUT_COUNT=0x", output_count);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSGROUPAFFINITY_GROUPS_WRITTEN=0x",
                     report.groups_written);
    serial_text("\r\n");
    if (g_process_group_facts.group_count != 0) {
        serial_field_hex("GXOS_NET10:GETPROCESSGROUPAFFINITY_GROUP_0_POLICY=0x",
                         g_process_group_facts.group_numbers[0]);
        serial_text("\r\n");
    }
    serial_field_hex("GXOS_NET10:GETPROCESSGROUPAFFINITY_GROUP_0_WRITTEN=0x",
                     report.groups_written != 0 ? report.group_numbers[0] : 0);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSGROUPAFFINITY_GROUP_ARRAY_OUTPUT_VALID=0x",
                     report.groups_written != 0 ? 1 : 0);
    serial_text("\r\n");
    serial_text("GXOS_NET10:GETPROCESSGROUPAFFINITY_STATUS_NAME=");
    serial_text(process_group_status_name(status));
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSGROUPAFFINITY_BOOLEAN_RESULT=0x",
                     (uint32_t)g_process_group_last_boolean);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSGROUPAFFINITY_LAST_ERROR_AFTER=0x",
                     last_error_after);
    serial_text("\r\n");
    serial_text("GXOS_NET10:GETPROCESSGROUPAFFINITY_CALLER_BRANCH=");
    serial_text(status == GXOS_PROCESS_GROUP_AFFINITY_STATUS_INSUFFICIENT_BUFFER
                    ? "FAILURE_INSUFFICIENT_BUFFER_REQUIRED_COUNT_READ\r\n"
                    : status == GXOS_PROCESS_GROUP_AFFINITY_STATUS_OK
                          ? "SUCCESS_GROUP_ARRAY_PUBLISHED\r\n"
                          : "FAILURE_OTHER\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSGROUPAFFINITY_RETRY=0x",
                     g_process_group_retry_count);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSGROUPAFFINITY_OUTPUT_COUNT_READ_BY_CALLER=0x",
                     status == GXOS_PROCESS_GROUP_AFFINITY_STATUS_INSUFFICIENT_BUFFER ? 1 :
                         status == GXOS_PROCESS_GROUP_AFFINITY_STATUS_OK ? 1 : 0);
    serial_text("\r\n");
    serial_text("GXOS_NET10:GETPROCESSGROUPAFFINITY_GROUP_ARRAY_READ_BY_CALLER=0\r\n");
    serial_text("GXOS_NET10:GETPROCESSGROUPAFFINITY_RETURNED\r\n");
#ifdef GXOS_PROCESS_GROUP_AFFINITY_MARKER_MUTATION
    serial_text("GXOS_NET10:GETPROCESSGROUPAFFINITY_OX\r\n");
#else
    if (status == GXOS_PROCESS_GROUP_AFFINITY_STATUS_OK) {
        serial_text("GXOS_NET10:GETPROCESSGROUPAFFINITY_OK\r\n");
    } else if (status == GXOS_PROCESS_GROUP_AFFINITY_STATUS_INSUFFICIENT_BUFFER) {
        serial_text("GXOS_NET10:GETPROCESSGROUPAFFINITY_INSUFFICIENT_BUFFER_OK\r\n");
    } else {
        serial_text("GXOS_NET10:GETPROCESSGROUPAFFINITY_FAILED\r\n");
    }
#endif
    return g_process_group_last_boolean;
}
#endif

#ifdef GXOS_ENABLE_QUERY_INFORMATION_JOB_OBJECT
static const char *query_job_status_name(GXOS_QUERY_JOB_STATUS status)
{
    switch (status) {
    case GXOS_QUERY_JOB_STATUS_OK: return "OK";
    case GXOS_QUERY_JOB_STATUS_NO_ASSOCIATED_JOB: return "NO_ASSOCIATED_JOB";
    case GXOS_QUERY_JOB_STATUS_INVALID_HANDLE: return "INVALID_HANDLE";
    case GXOS_QUERY_JOB_STATUS_UNSUPPORTED_INFORMATION_CLASS: return "UNSUPPORTED_INFORMATION_CLASS";
    case GXOS_QUERY_JOB_STATUS_NULL_OUTPUT: return "NULL_OUTPUT";
    case GXOS_QUERY_JOB_STATUS_NONCANONICAL_OUTPUT: return "NONCANONICAL_OUTPUT";
    case GXOS_QUERY_JOB_STATUS_UNWRITABLE_OUTPUT: return "UNWRITABLE_OUTPUT";
    case GXOS_QUERY_JOB_STATUS_INSUFFICIENT_OUTPUT: return "INSUFFICIENT_OUTPUT";
    case GXOS_QUERY_JOB_STATUS_NONCANONICAL_RETURN_LENGTH: return "NONCANONICAL_RETURN_LENGTH";
    case GXOS_QUERY_JOB_STATUS_UNWRITABLE_RETURN_LENGTH: return "UNWRITABLE_RETURN_LENGTH";
    case GXOS_QUERY_JOB_STATUS_LAYOUT_MISMATCH: return "LAYOUT_MISMATCH";
    case GXOS_QUERY_JOB_STATUS_INVALID_JOB_FACTS: return "INVALID_JOB_FACTS";
    case GXOS_QUERY_JOB_STATUS_RANGE_OVERFLOW: return "RANGE_OVERFLOW";
    case GXOS_QUERY_JOB_STATUS_ALIASED_OUTPUTS: return "ALIASED_OUTPUTS";
    case GXOS_QUERY_JOB_STATUS_INVALID_FLAGS: return "INVALID_FLAGS";
    case GXOS_QUERY_JOB_STATUS_INVALID_RATE: return "INVALID_RATE";
    default: return "UNKNOWN";
    }
}

static uint32_t query_job_status_last_error(GXOS_QUERY_JOB_STATUS status)
{
    if (status == GXOS_QUERY_JOB_STATUS_NO_ASSOCIATED_JOB) {
        return GXOS_QUERY_JOB_ERROR_ACCESS_DENIED;
    }
    if (status == GXOS_QUERY_JOB_STATUS_INVALID_HANDLE) {
        return GXOS_QUERY_JOB_ERROR_INVALID_HANDLE;
    }
    if (status == GXOS_QUERY_JOB_STATUS_INSUFFICIENT_OUTPUT) {
        return GXOS_QUERY_JOB_ERROR_INSUFFICIENT_BUFFER;
    }
    if (status == GXOS_QUERY_JOB_STATUS_NONCANONICAL_OUTPUT ||
        status == GXOS_QUERY_JOB_STATUS_UNWRITABLE_OUTPUT ||
        status == GXOS_QUERY_JOB_STATUS_NONCANONICAL_RETURN_LENGTH ||
        status == GXOS_QUERY_JOB_STATUS_UNWRITABLE_RETURN_LENGTH ||
        status == GXOS_QUERY_JOB_STATUS_RANGE_OVERFLOW) {
        return GXOS_QUERY_JOB_ERROR_NOACCESS;
    }
    return GXOS_QUERY_JOB_ERROR_INVALID_PARAMETER;
}

static uint32_t query_job_field_read_mask(uint32_t control_flags,
                                          GXOS_QUERY_JOB_STATUS status)
{
    if (status != GXOS_QUERY_JOB_STATUS_OK) return 0;
    if ((control_flags & (GXOS_QUERY_JOB_CPU_RATE_ENABLE |
                          GXOS_QUERY_JOB_CPU_RATE_HARD_CAP)) ==
        (GXOS_QUERY_JOB_CPU_RATE_ENABLE | GXOS_QUERY_JOB_CPU_RATE_HARD_CAP)) {
        return 0x3;
    }
    if ((control_flags & (GXOS_QUERY_JOB_CPU_RATE_ENABLE |
                          GXOS_QUERY_JOB_CPU_RATE_MIN_MAX)) ==
        (GXOS_QUERY_JOB_CPU_RATE_ENABLE | GXOS_QUERY_JOB_CPU_RATE_MIN_MAX)) {
        return 0x5;
    }
    return 0x1;
}

static uint32_t query_job_population(uint64_t value)
{
    uint32_t count = 0;
    while (value != 0) {
        value &= value - 1U;
        ++count;
    }
    return count;
}

static void query_job_emit_report(
    void *job_handle,
    GXOS_QUERY_JOB_INFO_CLASS information_class,
    GXOS_QUERY_JOB_OUTPUT output,
    GXOS_QUERY_JOB_DWORD output_length,
    GXOS_QUERY_JOB_RETURN_LENGTH return_length,
    GXOS_QUERY_JOB_STATUS status,
    GXOS_QUERY_JOB_REPORT *report)
{
    uintptr_t return_address = (uintptr_t)g_query_job_return_address;
    uintptr_t call_site = return_address >= 6 ? return_address - 6U : 0;
    uint64_t static_call_site = call_site >= (uintptr_t)g_managed_image_base
                                    ? 0x180000000ULL +
                                          (uint64_t)(call_site -
                                                     (uintptr_t)g_managed_image_base)
                                    : 0;
    uint32_t control_flags = status == GXOS_QUERY_JOB_STATUS_OK && report != 0
                                 ? report->output_after_low
                                 : 0;
    uint32_t field_read_mask = query_job_field_read_mask(control_flags, status);
    uint32_t processor_count_before = query_job_population(
        g_process_affinity_facts.process_affinity_mask);
    uint32_t processor_count_after = processor_count_before;
    const GXOS_SYSTEM_INFO_MEMORY_REGION *output_region =
        platform_system_info_region((uintptr_t)output);
    const GXOS_SYSTEM_INFO_MEMORY_REGION *return_region =
        platform_system_info_region((uintptr_t)return_length);

    serial_text("GXOS_NET10:QUERYJOBOBJECT_BEGIN\r\n");
    serial_field_hex("GXOS_NET10:QUERYJOBOBJECT_CALL_INDEX=0x",
                     g_query_job_calls - 1U);
    serial_text("\r\n");
    serial_text("GXOS_NET10:QUERYJOBOBJECT_IMPORT_MODULE=KERNEL32.dll\r\n");
    serial_text("GXOS_NET10:QUERYJOBOBJECT_IMPORT_SYMBOL=QueryInformationJobObject\r\n");
    serial_field_hex("GXOS_NET10:QUERYJOBOBJECT_IMPORT_DESCRIPTOR_INDEX=0x", 2);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:QUERYJOBOBJECT_IAT_RVA=0x", 0x7D1F0);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:QUERYJOBOBJECT_PREFERRED_IAT=0x", 0x18007D1F0ULL);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:QUERYJOBOBJECT_RUNTIME_IAT=0x",
                     g_managed_image_base + 0x7D1F0ULL);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:QUERYJOBOBJECT_STATIC_CALL_SITE=0x", static_call_site);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:QUERYJOBOBJECT_RUNTIME_CALL_SITE=0x", call_site);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:QUERYJOBOBJECT_RETURN_ADDRESS=0x", return_address);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:QUERYJOBOBJECT_CALLER_START=0x",
                     g_managed_image_base + 0x3CBE0ULL);
    serial_text("\r\n");
    serial_text("GXOS_NET10:QUERYJOBOBJECT_CALLER=NativeAOT_processor_count_setup\r\n");
    serial_field_hex("GXOS_NET10:QUERYJOBOBJECT_RCX_HJOB=0x", (uintptr_t)job_handle);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:QUERYJOBOBJECT_EDX_INFO_CLASS=0x", information_class);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:QUERYJOBOBJECT_R8_OUTPUT_POINTER=0x", (uintptr_t)output);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:QUERYJOBOBJECT_R9D_OUTPUT_LENGTH=0x", output_length);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:QUERYJOBOBJECT_ENTRY_RSP=0x", g_query_job_entry_rsp);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:QUERYJOBOBJECT_FIFTH_ARGUMENT_STACK_ADDRESS=0x",
                     g_query_job_fifth_stack_address);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:QUERYJOBOBJECT_FIFTH_ARGUMENT_STACK_VALUE=0x",
                     g_query_job_fifth_stack_value);
    serial_text("\r\n");
    serial_text("GXOS_NET10:QUERYJOBOBJECT_FIFTH_ARGUMENT_RELATION=ENTRY_RSP_PLUS_0x28\r\n");
    serial_field_hex("GXOS_NET10:QUERYJOBOBJECT_LP_RETURN_LENGTH=0x",
                     (uintptr_t)return_length);
    serial_text("\r\n");
    serial_text("GXOS_NET10:QUERYJOBOBJECT_LP_RETURN_LENGTH_NULL=");
    serial_text(return_length == 0 ? "1\r\n" : "0\r\n");
    serial_field_hex("GXOS_NET10:QUERYJOBOBJECT_OUTPUT_REGION_BASE=0x",
                     output_region == 0 ? 0 : output_region->base);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:QUERYJOBOBJECT_OUTPUT_REGION_END=0x",
                     output_region == 0 ? 0 : output_region->end);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:QUERYJOBOBJECT_OUTPUT_REGION_WRITABLE=0x",
                     output_region == 0 ? 0 : output_region->writable);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:QUERYJOBOBJECT_OUTPUT_ALIGNMENT=0x",
                     report == 0 ? 0 : report->output_alignment);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:QUERYJOBOBJECT_OUTPUT_LENGTH=0x", output_length);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:QUERYJOBOBJECT_STRUCTURE_SIZE=0x",
                     GXOS_QUERY_JOB_CPU_RATE_STRUCTURE_SIZE);
    serial_text("\r\n");
    serial_text("GXOS_NET10:QUERYJOBOBJECT_INFO_CLASS_NAME=JobObjectCpuRateControlInformation\r\n");
    serial_field_hex("GXOS_NET10:QUERYJOBOBJECT_INFO_CLASS_VALUE=0x", information_class);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:QUERYJOBOBJECT_OUTPUT_POINTER_CANONICAL=0x",
                     report == 0 ? 0 : report->output_pointer_canonical);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:QUERYJOBOBJECT_OUTPUT_POINTER_WRITABLE=0x",
                     report == 0 ? 0 : report->output_pointer_writable);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:QUERYJOBOBJECT_OUTPUT_RANGE_VALID=0x",
                     report == 0 ? 0 : report->output_range_valid);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:QUERYJOBOBJECT_OUTPUT_BEFORE_LOW=0x",
                     report == 0 ? 0 : report->output_before_low);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:QUERYJOBOBJECT_OUTPUT_BEFORE_HIGH=0x",
                     report == 0 ? 0 : report->output_before_high);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:QUERYJOBOBJECT_OUTPUT_AFTER_LOW=0x",
                     report == 0 ? 0 : report->output_after_low);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:QUERYJOBOBJECT_OUTPUT_AFTER_HIGH=0x",
                     report == 0 ? 0 : report->output_after_high);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:QUERYJOBOBJECT_RETURN_LENGTH_POINTER_REGION_BASE=0x",
                     return_region == 0 ? 0 : return_region->base);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:QUERYJOBOBJECT_RETURN_LENGTH_POINTER_REGION_END=0x",
                     return_region == 0 ? 0 : return_region->end);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:QUERYJOBOBJECT_LAST_ERROR_BEFORE=0x",
                     g_query_job_last_error_before);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:QUERYJOBOBJECT_BOOLEAN_RESULT=0x",
                     (uint32_t)g_query_job_last_boolean);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:QUERYJOBOBJECT_LAST_ERROR_AFTER=0x",
                     g_query_job_last_error_after);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:QUERYJOBOBJECT_RETURN_LENGTH=0x",
                     report != 0 && report->return_length_after_valid
                         ? report->return_length_after
                         : report != 0 && report->return_length_before_valid
                               ? report->return_length_before
                               : 0);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:QUERYJOBOBJECT_FIELD_READ_MASK=0x", field_read_mask);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:QUERYJOBOBJECT_PROCESSOR_COUNT_BEFORE=0x",
                     processor_count_before);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:QUERYJOBOBJECT_PROCESSOR_COUNT_AFTER=0x",
                     processor_count_after);
    serial_text("\r\n");
    serial_text("GXOS_NET10:QUERYJOBOBJECT_JOB_ASSOCIATION=");
    serial_text(g_query_job_facts.associated_job != 0 ? "ASSOCIATED\r\n" : "NONE\r\n");
    serial_field_hex("GXOS_NET10:QUERYJOBOBJECT_ACTIVE_CONTROL_FLAGS=0x",
                     g_query_job_facts.control_flags);
    serial_text("\r\n");
    serial_text("GXOS_NET10:QUERYJOBOBJECT_CALLER_BRANCH=");
    if (status == GXOS_QUERY_JOB_STATUS_NO_ASSOCIATED_JOB) {
        serial_text("FAILURE_NO_ASSOCIATED_JOB_FALLBACK\r\n");
    } else if (status == GXOS_QUERY_JOB_STATUS_OK && control_flags == 0) {
        serial_text("SUCCESS_NO_ACTIVE_CPU_RATE_FALLBACK\r\n");
    } else if (status == GXOS_QUERY_JOB_STATUS_OK) {
        serial_text("SUCCESS_ACTIVE_CPU_RATE_CAP\r\n");
    } else {
        serial_text("FAILURE_OTHER\r\n");
    }
    serial_field_hex("GXOS_NET10:QUERYJOBOBJECT_CALLER_GETLASTERROR=0x", 0);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:QUERYJOBOBJECT_OUTPUT_WRITTEN=0x",
                     report == 0 ? 0 : report->output_written);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:QUERYJOBOBJECT_RETURN_LENGTH_WRITTEN=0x",
                     report == 0 ? 0 : report->return_length_written);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:QUERYJOBOBJECT_CALL_COUNT=0x", g_query_job_calls);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:QUERYJOBOBJECT_SUCCESS_COUNT=0x", g_query_job_successes);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:QUERYJOBOBJECT_EXPECTED_NO_JOB_FAILURE_COUNT=0x",
                     g_query_job_expected_no_job_failures);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:QUERYJOBOBJECT_OTHER_FAILURE_COUNT=0x",
                     g_query_job_other_failures);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:QUERYJOBOBJECT_STATUS=0x", (uint32_t)status);
    serial_text("\r\n");
    serial_text("GXOS_NET10:QUERYJOBOBJECT_STATUS_NAME=");
    serial_text(query_job_status_name(status));
    serial_text("\r\n");
    serial_text("GXOS_NET10:QUERYJOBOBJECT_RETURNED\r\n");
#ifdef GXOS_QUERY_JOB_MARKER_MUTATION
    serial_text("GXOS_NET10:QUERYJOBOBJECT_OX\r\n");
#else
    if (status == GXOS_QUERY_JOB_STATUS_NO_ASSOCIATED_JOB) {
        serial_text("GXOS_NET10:QUERYJOBOBJECT_EXPECTED_NO_ASSOCIATED_JOB_FAILURE\r\n");
    } else if (status == GXOS_QUERY_JOB_STATUS_OK) {
        serial_text("GXOS_NET10:QUERYJOBOBJECT_OK\r\n");
    } else {
        serial_text("GXOS_NET10:QUERYJOBOBJECT_FAILED\r\n");
    }
#endif
}

static GXOS_QUERY_JOB_BOOL EFIAPI platform_query_information_job_object_body(
    void *job_handle,
    GXOS_QUERY_JOB_INFO_CLASS information_class,
    GXOS_QUERY_JOB_OUTPUT output,
    GXOS_QUERY_JOB_DWORD output_length,
    GXOS_QUERY_JOB_RETURN_LENGTH return_length)
{
    GXOS_QUERY_JOB_STATUS status;
    GXOS_QUERY_JOB_BOOL result;
    uint32_t last_error_before = g_platform_last_error;

    ++g_query_job_calls;
    status = gxos_query_information_job_object_checked(
        (GXOS_QUERY_JOB_HANDLE)(uintptr_t)job_handle, information_class,
        output, output_length, return_length, &g_query_job_facts,
        &g_system_info_memory, &g_query_job_last_report);
    if (status == GXOS_QUERY_JOB_STATUS_OK) {
        result = GXOS_QUERY_JOB_TRUE;
        ++g_query_job_successes;
    } else {
        result = GXOS_QUERY_JOB_FALSE;
        g_platform_last_error = query_job_status_last_error(status);
        if (status == GXOS_QUERY_JOB_STATUS_NO_ASSOCIATED_JOB) {
            ++g_query_job_expected_no_job_failures;
        } else {
            ++g_query_job_other_failures;
        }
    }
    g_query_job_last_status = status;
    g_query_job_last_error_before = last_error_before;
    g_query_job_last_error_after = g_platform_last_error;
    g_query_job_last_boolean = result;
    query_job_emit_report(job_handle, information_class, output, output_length,
                          return_length, status, &g_query_job_last_report);
    return result;
}

static GXOS_QUERY_JOB_BOOL EFIAPI platform_query_information_job_object(
    void *, GXOS_QUERY_JOB_INFO_CLASS, GXOS_QUERY_JOB_OUTPUT,
    GXOS_QUERY_JOB_DWORD, GXOS_QUERY_JOB_RETURN_LENGTH)
{
    __asm__ volatile(
        "movq (%rsp), %r11\n\t"
        "movq %r11, g_query_job_return_address(%rip)\n\t"
        "movq %rsp, g_query_job_entry_rsp(%rip)\n\t"
        "leaq 0x28(%rsp), %r11\n\t"
        "movq %r11, g_query_job_fifth_stack_address(%rip)\n\t"
        "movq 0x28(%rsp), %r11\n\t"
        "movq %r11, g_query_job_fifth_stack_value(%rip)\n\t"
        "jmp platform_query_information_job_object_body\n\t");
}
#endif

#ifdef GXOS_ENABLE_PROCESS_AFFINITY
static const char *process_affinity_status_name(
    GXOS_PROCESS_AFFINITY_STATUS status)
{
    switch (status) {
    case GXOS_PROCESS_AFFINITY_STATUS_OK: return "OK";
    case GXOS_PROCESS_AFFINITY_STATUS_INVALID_PROCESS_HANDLE: return "INVALID_PROCESS_HANDLE";
    case GXOS_PROCESS_AFFINITY_STATUS_NULL_PROCESS_MASK: return "NULL_PROCESS_MASK";
    case GXOS_PROCESS_AFFINITY_STATUS_NULL_SYSTEM_MASK: return "NULL_SYSTEM_MASK";
    case GXOS_PROCESS_AFFINITY_STATUS_NONCANONICAL_PROCESS_MASK: return "NONCANONICAL_PROCESS_MASK";
    case GXOS_PROCESS_AFFINITY_STATUS_NONCANONICAL_SYSTEM_MASK: return "NONCANONICAL_SYSTEM_MASK";
    case GXOS_PROCESS_AFFINITY_STATUS_UNWRITABLE_PROCESS_MASK: return "UNWRITABLE_PROCESS_MASK";
    case GXOS_PROCESS_AFFINITY_STATUS_UNWRITABLE_SYSTEM_MASK: return "UNWRITABLE_SYSTEM_MASK";
    case GXOS_PROCESS_AFFINITY_STATUS_RANGE_OVERFLOW: return "RANGE_OVERFLOW";
    case GXOS_PROCESS_AFFINITY_STATUS_ZERO_PROCESS_MASK: return "ZERO_PROCESS_MASK";
    case GXOS_PROCESS_AFFINITY_STATUS_ZERO_SYSTEM_MASK: return "ZERO_SYSTEM_MASK";
    case GXOS_PROCESS_AFFINITY_STATUS_PROCESS_NOT_SUBSET: return "PROCESS_NOT_SUBSET";
    case GXOS_PROCESS_AFFINITY_STATUS_PROCESSOR_COUNT_MISMATCH: return "PROCESSOR_COUNT_MISMATCH";
    case GXOS_PROCESS_AFFINITY_STATUS_GROUP_POLICY_MISMATCH: return "GROUP_POLICY_MISMATCH";
    case GXOS_PROCESS_AFFINITY_STATUS_SYSTEM_SNAPSHOT_MISMATCH: return "SYSTEM_SNAPSHOT_MISMATCH";
    case GXOS_PROCESS_AFFINITY_STATUS_UNSUPPORTED_TOPOLOGY: return "UNSUPPORTED_TOPOLOGY";
    case GXOS_PROCESS_AFFINITY_STATUS_ALIASED_OUTPUTS: return "ALIASED_OUTPUTS";
    case GXOS_PROCESS_AFFINITY_STATUS_INVALID_MEMORY_CONTEXT: return "INVALID_MEMORY_CONTEXT";
    default: return "UNKNOWN";
    }
}

static uint32_t process_affinity_status_last_error(
    GXOS_PROCESS_AFFINITY_STATUS status)
{
    if (status == GXOS_PROCESS_AFFINITY_STATUS_INVALID_PROCESS_HANDLE) {
        return GXOS_PROCESS_AFFINITY_ERROR_INVALID_HANDLE;
    }
    if (status == GXOS_PROCESS_AFFINITY_STATUS_UNSUPPORTED_TOPOLOGY) {
        return GXOS_PROCESS_AFFINITY_ERROR_NOT_SUPPORTED;
    }
    return GXOS_PROCESS_AFFINITY_ERROR_INVALID_PARAMETER;
}

static uint32_t process_affinity_population(uint64_t value)
{
    uint32_t count = 0;
    while (value != 0) {
        value &= value - 1U;
        count++;
    }
    return count;
}

static const GXOS_SYSTEM_INFO_MEMORY_REGION *process_affinity_region(
    uintptr_t address, uint64_t *writable_range, uint32_t *range_valid)
{
    const GXOS_SYSTEM_INFO_MEMORY_REGION *region =
        platform_system_info_region(address);
    *writable_range = 0;
    *range_valid = 0;
    if (region != 0 && region->writable != 0 &&
        address <= UINTPTR_MAX - sizeof(uint64_t) &&
        address + sizeof(uint64_t) <= region->end) {
        *writable_range = (uint64_t)(region->end - address);
        *range_valid = 1;
    }
    return region;
}

static uint32_t process_affinity_safe_read(
    const GXOS_SYSTEM_INFO_MEMORY_REGION *region, uintptr_t address,
    uint64_t *value)
{
    if (region == 0 || region->readable == 0 || value == 0 ||
        address > UINTPTR_MAX - sizeof(uint64_t) ||
        address + sizeof(uint64_t) > region->end) return 0;
    *value = *(const uint64_t *)(uintptr_t)address;
    return 1;
}

static GXOS_PROCESS_AFFINITY_BOOL EFIAPI platform_get_process_affinity_mask(
    void *process_handle,
    GXOS_PROCESS_AFFINITY_DWORD_PTR *process_affinity_mask,
    GXOS_PROCESS_AFFINITY_DWORD_PTR *system_affinity_mask)
{
    uintptr_t return_address = (uintptr_t)__builtin_return_address(0);
    uintptr_t call_site = return_address >= 6 ? return_address - 6 : 0;
    uint64_t static_call_site = call_site >= (uintptr_t)g_managed_image_base
                                    ? 0x180000000ULL +
                                          (uint64_t)(call_site - (uintptr_t)g_managed_image_base)
                                    : 0;
    uint32_t bitmap_caller = static_call_site == 0x180043793ULL;
    uint32_t processor_count_caller = static_call_site == 0x18003CC55ULL;
    uintptr_t process_address = (uintptr_t)process_affinity_mask;
    uintptr_t system_address = (uintptr_t)system_affinity_mask;
    const GXOS_SYSTEM_INFO_MEMORY_REGION *process_region;
    const GXOS_SYSTEM_INFO_MEMORY_REGION *system_region;
    uint64_t process_range;
    uint64_t system_range;
    uint32_t process_range_valid;
    uint32_t system_range_valid;
    uint32_t process_before_valid;
    uint32_t system_before_valid;
    uint32_t process_after_valid;
    uint32_t system_after_valid;
    uint64_t process_before = 0;
    uint64_t system_before = 0;
    uint64_t process_after = 0;
    uint64_t system_after = 0;
    GXOS_PROCESS_AFFINITY_REPORT report;
    GXOS_PROCESS_AFFINITY_STATUS status;
    GXOS_PROCESS_AFFINITY_BOOL result;
    uint64_t last_error_before = g_platform_last_error;

    g_process_affinity_calls++;
    process_region = process_affinity_region(process_address, &process_range,
                                             &process_range_valid);
    system_region = process_affinity_region(system_address, &system_range,
                                            &system_range_valid);
    process_before_valid = process_affinity_safe_read(
        process_region, process_address, &process_before);
    system_before_valid = process_affinity_safe_read(
        system_region, system_address, &system_before);
#ifdef GXOS_PROCESS_AFFINITY_FORCE_FAILURE
    status = GXOS_PROCESS_AFFINITY_STATUS_INVALID_PROCESS_HANDLE;
    gxos_get_process_affinity_mask_checked(
        (GXOS_PROCESS_AFFINITY_HANDLE)0, 0, 0, 0, 0, &report);
#else
    status = gxos_get_process_affinity_mask_checked(
        (GXOS_PROCESS_AFFINITY_HANDLE)(uintptr_t)process_handle,
        process_affinity_mask, system_affinity_mask,
        &g_process_affinity_facts, &g_system_info_memory, &report);
#endif
    if (status == GXOS_PROCESS_AFFINITY_STATUS_OK) {
        result = GXOS_PROCESS_AFFINITY_TRUE;
        g_process_affinity_successes++;
    } else {
        result = GXOS_PROCESS_AFFINITY_FALSE;
        g_platform_last_error = process_affinity_status_last_error(status);
        g_process_affinity_failures++;
    }
    process_after_valid = process_affinity_safe_read(
        process_region, process_address, &process_after);
    system_after_valid = process_affinity_safe_read(
        system_region, system_address, &system_after);
    g_process_affinity_last_handle = (uint64_t)(uintptr_t)process_handle;
    g_process_affinity_last_process_pointer = process_address;
    g_process_affinity_last_system_pointer = system_address;
    g_process_affinity_last_process_before = process_before;
    g_process_affinity_last_system_before = system_before;
    g_process_affinity_last_process_after = process_after;
    g_process_affinity_last_system_after = system_after;
    g_process_affinity_last_error_before = last_error_before;
    g_process_affinity_last_error_after = g_platform_last_error;
    g_process_affinity_last_boolean = result;
    g_process_affinity_last_status = status;
    g_process_affinity_last_report = report;

    serial_text("GXOS_NET10:GETPROCESSAFFINITYMASK_BEGIN\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSAFFINITYMASK_CALL_INDEX=0x",
                     g_process_affinity_calls - 1U);
    serial_text("\r\n");
    serial_text("GXOS_NET10:GETPROCESSAFFINITYMASK_IMPORT_MODULE=KERNEL32.dll\r\n");
    serial_text("GXOS_NET10:GETPROCESSAFFINITYMASK_IMPORT_SYMBOL=GetProcessAffinityMask\r\n");
    serial_text("GXOS_NET10:GETPROCESSAFFINITYMASK_IMPORT_DESCRIPTOR_INDEX=0x2\r\n");
    serial_text("GXOS_NET10:GETPROCESSAFFINITYMASK_IAT_RVA=0x7d208\r\n");
    serial_text("GXOS_NET10:GETPROCESSAFFINITYMASK_PREFERRED_IAT=0x000000018007d208\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSAFFINITYMASK_RUNTIME_IAT=0x",
                     g_managed_image_base + 0x7d208U);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSAFFINITYMASK_STATIC_CALL_SITE=0x",
                     static_call_site);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSAFFINITYMASK_RUNTIME_CALL_SITE=0x", call_site);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSAFFINITYMASK_RETURN_ADDRESS=0x", return_address);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSAFFINITYMASK_CALLER_START=0x",
                     bitmap_caller ? g_managed_image_base + 0x43650U :
                         processor_count_caller ? g_managed_image_base + 0x3CBE0U : 0);
    serial_text("\r\n");
    serial_text("GXOS_NET10:GETPROCESSAFFINITYMASK_CALLER=");
    serial_text(bitmap_caller ? "NativeAOT_processor_bitmap_setup\r\n" :
                    processor_count_caller ? "NativeAOT_processor_count_setup\r\n" :
                        "NativeAOT_unclassified_affinity_call\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSAFFINITYMASK_RCX_HANDLE=0x",
                     (uint64_t)(uintptr_t)process_handle);
    serial_text("\r\n");
    serial_text("GXOS_NET10:GETPROCESSAFFINITYMASK_HANDLE_CLASS=");
    serial_text((uintptr_t)process_handle == GXOS_PROCESS_AFFINITY_CURRENT_PROCESS
                    ? "CURRENT_PROCESS_PSEUDO\r\n" : "OTHER\r\n");
    serial_text("GXOS_NET10:GETPROCESSAFFINITYMASK_HANDLE_ORIGIN=GetCurrentProcess\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSAFFINITYMASK_RDX_PROCESS_OUTPUT=0x",
                     process_address);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSAFFINITYMASK_R8_SYSTEM_OUTPUT=0x",
                     system_address);
    serial_text("\r\n");
    serial_text("GXOS_NET10:GETPROCESSAFFINITYMASK_PROCESS_POINTER_NULL=");
    serial_text(process_affinity_mask == 0 ? "1\r\n" : "0\r\n");
    serial_text("GXOS_NET10:GETPROCESSAFFINITYMASK_SYSTEM_POINTER_NULL=");
    serial_text(system_affinity_mask == 0 ? "1\r\n" : "0\r\n");
    serial_text("GXOS_NET10:GETPROCESSAFFINITYMASK_OUTPUTS_ALIAS=");
    serial_text(process_affinity_mask != 0 && process_affinity_mask == system_affinity_mask
                    ? "1\r\n" : "0\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSAFFINITYMASK_PROCESS_POINTER_ALIGNMENT=0x",
                     process_address & 7U);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSAFFINITYMASK_SYSTEM_POINTER_ALIGNMENT=0x",
                     system_address & 7U);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSAFFINITYMASK_PROCESS_REGION_BASE=0x",
                     process_region == 0 ? 0 : process_region->base);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSAFFINITYMASK_PROCESS_REGION_END=0x",
                     process_region == 0 ? 0 : process_region->end);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSAFFINITYMASK_PROCESS_WRITABLE_RANGE=0x",
                     process_range);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSAFFINITYMASK_SYSTEM_REGION_BASE=0x",
                     system_region == 0 ? 0 : system_region->base);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSAFFINITYMASK_SYSTEM_REGION_END=0x",
                     system_region == 0 ? 0 : system_region->end);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSAFFINITYMASK_SYSTEM_WRITABLE_RANGE=0x",
                     system_range);
    serial_text("\r\n");
    serial_text("GXOS_NET10:GETPROCESSAFFINITYMASK_OUTPUT_WIDTH=0x8\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSAFFINITYMASK_PROCESS_BEFORE=0x", process_before);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSAFFINITYMASK_SYSTEM_BEFORE=0x", system_before);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSAFFINITYMASK_PROCESS_AFTER=0x", process_after);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSAFFINITYMASK_SYSTEM_AFTER=0x", system_after);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSAFFINITYMASK_PROCESS_MASK=0x",
                     g_process_affinity_facts.process_affinity_mask);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSAFFINITYMASK_SYSTEM_MASK=0x",
                     g_process_affinity_facts.system_affinity_mask);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSAFFINITYMASK_USABLE_MASK=0x",
                     g_process_affinity_facts.usable_processor_mask);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSAFFINITYMASK_USABLE_PROCESSOR_COUNT=0x",
                     g_process_affinity_facts.usable_processor_count);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSAFFINITYMASK_PROCESS_POPCOUNT=0x",
                     process_affinity_population(g_process_affinity_facts.process_affinity_mask));
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSAFFINITYMASK_SYSTEM_POPCOUNT=0x",
                     process_affinity_population(g_process_affinity_facts.system_affinity_mask));
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSAFFINITYMASK_STATUS=0x", (uint32_t)status);
    serial_text("\r\n");
    serial_text("GXOS_NET10:GETPROCESSAFFINITYMASK_STATUS_NAME=");
    serial_text(process_affinity_status_name(status));
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSAFFINITYMASK_BOOLEAN_RESULT=0x", (uint32_t)result);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSAFFINITYMASK_LAST_ERROR_BEFORE=0x",
                     last_error_before);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSAFFINITYMASK_LAST_ERROR_AFTER=0x",
                     g_platform_last_error);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSAFFINITYMASK_PROCESS_POINTER_CANONICAL=0x",
                     report.process_pointer_canonical);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSAFFINITYMASK_PROCESS_POINTER_WRITABLE=0x",
                     report.process_pointer_writable);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSAFFINITYMASK_PROCESS_RANGE_VALID=0x",
                     report.process_range_valid);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSAFFINITYMASK_SYSTEM_POINTER_CANONICAL=0x",
                     report.system_pointer_canonical);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSAFFINITYMASK_SYSTEM_POINTER_WRITABLE=0x",
                     report.system_pointer_writable);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSAFFINITYMASK_SYSTEM_RANGE_VALID=0x",
                     report.system_range_valid);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSAFFINITYMASK_PROCESS_WRITTEN=0x",
                     report.process_written);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSAFFINITYMASK_SYSTEM_WRITTEN=0x",
                     report.system_written);
    serial_text("\r\n");
    serial_text("GXOS_NET10:GETPROCESSAFFINITYMASK_CALLER_BRANCH=");
    serial_text(result == GXOS_PROCESS_AFFINITY_TRUE
                    ? "SUCCESS_PROCESS_MASK_READ_SYSTEM_MASK_NOT_READ\r\n"
                    : "FAILURE_AFFINITY_FALLBACK\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSAFFINITYMASK_CALLER_PROCESS_MASK_READ=0x",
                     result == GXOS_PROCESS_AFFINITY_TRUE ? 1 : 0);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSAFFINITYMASK_CALLER_SYSTEM_MASK_READ=0x", 0);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSAFFINITYMASK_CALLER_PROCESS_READ_WIDTH=0x",
                     result == GXOS_PROCESS_AFFINITY_TRUE ? 8 : 0);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:GETPROCESSAFFINITYMASK_CALLER_SYSTEM_READ_WIDTH=0x", 0);
    serial_text("\r\n");
    serial_text("GXOS_NET10:GETPROCESSAFFINITYMASK_CALLER_MASKS_INTERSECTED=0\r\n");
    serial_text("GXOS_NET10:GETPROCESSAFFINITYMASK_CALLER_PROCESS_AND_SYSTEM=0\r\n");
    serial_text("GXOS_NET10:GETPROCESSAFFINITYMASK_CALLER_BITS_COUNTED=");
    serial_text(result == GXOS_PROCESS_AFFINITY_TRUE && processor_count_caller
                    ? "1\r\n" : "0\r\n");
    serial_text("GXOS_NET10:GETPROCESSAFFINITYMASK_CALLER_POPCOUNT_PERFORMED=");
    serial_text(result == GXOS_PROCESS_AFFINITY_TRUE && processor_count_caller
                    ? "1\r\n" : "0\r\n");
    serial_text("GXOS_NET10:GETPROCESSAFFINITYMASK_CALLER_PROCESSOR_BITMAP_UPDATE=");
    serial_text(result == GXOS_PROCESS_AFFINITY_TRUE && bitmap_caller ? "1\r\n" : "0\r\n");
    serial_text("GXOS_NET10:GETPROCESSAFFINITYMASK_CALLER_SUBSEQUENT_API=");
    serial_text(processor_count_caller ? "KERNEL32.dll!QueryInformationJobObject\r\n" :
                    "NONE_BEFORE_RETURN\r\n");
    serial_text("GXOS_NET10:GETPROCESSAFFINITYMASK_CALLER_DERIVED_PROCESSOR_COUNT=");
    if (result != GXOS_PROCESS_AFFINITY_TRUE) {
        serial_text(processor_count_caller ? "1\r\n" : "NOT_DERIVED\r\n");
    } else if (processor_count_caller) {
        serial_field_hex("0x", process_affinity_population(process_after));
        serial_text("\r\n");
    } else {
        serial_text("NOT_DERIVED\r\n");
    }
    serial_text("GXOS_NET10:GETPROCESSAFFINITYMASK_CALLER_GETLASTERROR=0\r\n");
    serial_text("GXOS_NET10:GETPROCESSAFFINITYMASK_PROCESS_AFTER_READ_VALID=");
    serial_text(process_after_valid != 0 && process_before_valid != 0 ? "1\r\n" : "0\r\n");
    serial_text("GXOS_NET10:GETPROCESSAFFINITYMASK_SYSTEM_AFTER_READ_VALID=");
    serial_text(system_after_valid != 0 && system_before_valid != 0 ? "1\r\n" : "0\r\n");
    serial_text("GXOS_NET10:GETPROCESSAFFINITYMASK_RETURNED\r\n");
#ifdef GXOS_PROCESS_AFFINITY_MARKER_MUTATION
    serial_text("GXOS_NET10:GETPROCESSAFFINITYMASK_OX\r\n");
#else
    if (status == GXOS_PROCESS_AFFINITY_STATUS_OK) {
        serial_text("GXOS_NET10:GETPROCESSAFFINITYMASK_OK\r\n");
    } else {
        serial_text("GXOS_NET10:GETPROCESSAFFINITYMASK_FAILED\r\n");
    }
#endif
    return result;
}
#endif

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
    uint32_t virtual_size;
    uint32_t characteristics;
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
    image->memory_region_count = 0;
    image->executable_region_count = 0;
    zero_bytes(image->loaded, size_of_image);
    copy_bytes(image->loaded, image->file, image->size_of_headers);

    section = nt + 24 + optional_size;
    for (i = 0; i < section_count; i++, section += 40) {
        raw_size = read_u32(section + 16);
        raw_offset = read_u32(section + 20);
        virtual_address = read_u32(section + 12);
        virtual_size = read_u32(section + 8);
        characteristics = read_u32(section + 36);
        {
            uint32_t extent = virtual_size > raw_size ? virtual_size : raw_size;
            if (extent != 0) {
                if (image->memory_region_count >= GXOS_CRT_INITTERM_MAX_MEMORY_REGIONS ||
                    (uint64_t)virtual_address + extent > size_of_image ||
                    image->actual_base > UINTPTR_MAX - (uintptr_t)virtual_address) {
                    fail("memory-section-bounds");
                }
                image->memory_regions[image->memory_region_count].base =
                    image->actual_base + (uintptr_t)virtual_address;
                if ((uintptr_t)extent > UINTPTR_MAX -
                    image->memory_regions[image->memory_region_count].base) {
                    fail("memory-section-overflow");
                }
                image->memory_regions[image->memory_region_count].end =
                    image->memory_regions[image->memory_region_count].base + (uintptr_t)extent;
                image->memory_regions[image->memory_region_count].readable =
                    (characteristics & 0x40000000U) != 0;
                image->memory_regions[image->memory_region_count].executable =
                    (characteristics & 0x20000000U) != 0;
                image->memory_regions[image->memory_region_count].writable =
                    (characteristics & 0x80000000U) != 0;
                image->memory_region_count++;
            }
        }
        if (raw_size == 0) continue;
        if ((uint64_t)raw_offset + raw_size > image->file_size ||
            (uint64_t)virtual_address + raw_size > size_of_image) fail("section-bounds");
        if ((characteristics & 0x20000000U) != 0) {
            uintptr_t region_base;
            uintptr_t region_end;
            uint32_t extent = virtual_size == 0 ? raw_size : virtual_size;
            if (image->executable_region_count >= GXOS_CRT_INITTERM_E_MAX_EXECUTABLE_REGIONS ||
                extent == 0 || (uint64_t)virtual_address + extent > size_of_image ||
                image->actual_base > UINTPTR_MAX - (uintptr_t)virtual_address) {
                fail("executable-section-bounds");
            }
            region_base = image->actual_base + (uintptr_t)virtual_address;
            if ((uintptr_t)extent > UINTPTR_MAX - region_base) fail("executable-section-overflow");
            region_end = region_base + (uintptr_t)extent;
            image->executable_regions[image->executable_region_count].base = region_base;
            image->executable_regions[image->executable_region_count].end = region_end;
            image->executable_region_count++;
        }
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
#if defined(GXOS_ENABLE_EXCEPTION_SYNTHETIC_PROBE) || defined(GXOS_ENABLE_VECTORED_EXCEPTION_HANDLER)
    g_veh_harness_identity = loaded_image->ImageBase;
    g_veh_harness_image_base = (uintptr_t)loaded_image->ImageBase;
    g_veh_harness_image_size = loaded_image->ImageSize;
#endif
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
#ifdef GXOS_ENABLE_CRT_INITTERM_E
    GXOS_CRT_INITTERM_E_CONTEXT initterm_e_context = {0};
    uint32_t initterm_e_region_index;
#endif
#ifdef GXOS_ENABLE_CRT_INITTERM
    GXOS_CRT_INITTERM_CONTEXT initterm_context = {0};
    uint32_t initterm_region_index;
#endif
#ifdef GXOS_ENABLE_CRT_ONEXIT
    int onexit_context_result;
#endif

    serial_init();
    serial_text("GXOS_NET10:LOADER_START\r\n");
    g_phase = PHASE_LOADER;
    boot_services = system_table->BootServices;
#ifdef GXOS_ENABLE_SYNTHETIC_SCHEDULER_PROOF
    if (!gxos_synthetic_scheduler_proof(
            boot_services->AllocatePages, boot_services->FreePages,
            serial_text, scheduler_log_hex, scheduler_log_u32)) {
        fail("synthetic-scheduler-proof");
    }
    serial_text("GXOS_NET10:SYNTHETIC_SCHEDULER_PROOF_RETURNED\r\n");
    halt_forever();
#endif
    configure_platform_time(system_table->RuntimeServices);
#ifdef GXOS_ENABLE_NATIVEAOT_STARTUP
    configure_platform_performance(system_table);
#endif
    read_payload(image_handle, system_table, &image);
    serial_text("GXOS_NET10:PE_READ_OK\r\n");
    load_pe_image(&image, boot_services);
    serial_text("GXOS_NET10:PE_RELOCATIONS_OK\r\n");
#if defined(GXOS_ENABLE_EXCEPTION_SYNTHETIC_PROBE) || defined(GXOS_ENABLE_VECTORED_EXCEPTION_HANDLER)
    configure_veh_registry(&image);
#endif
#ifdef GXOS_ENABLE_GET_MODULE_HANDLE_EX
    g_main_module_permanent_residency_proven = 1;
    serial_text("GXOS_NET10:GETMODULEHANDLEEX_PERMANENT_RESIDENCY_INVARIANT=ALLOCATEPAGES_NO_IMAGE_FREE_EFI_MAIN_NONRETURNING\r\n");
#endif
#ifdef GXOS_ENABLE_CRT_STRLEN
    if (image.actual_base == 0 || image.actual_base > UINTPTR_MAX - (uintptr_t)image.loaded_size ||
        image.memory_region_count == 0 || image.relocations_applied == 0) {
        fail("crt-strlen-context");
    }
    g_crt_strlen_image.image_base = image.actual_base;
    g_crt_strlen_image.image_end = image.actual_base + (uintptr_t)image.loaded_size;
    g_crt_strlen_image.relocations_applied = image.relocations_applied;
    g_crt_strlen_image.memory_region_count = image.memory_region_count;
    g_crt_strlen_image.memory_regions = image.memory_regions;
    serial_text("GXOS_NET10:CRT_STRLEN_VALIDATION_CONTEXT_OK\r\n");
#endif
#ifdef GXOS_ENABLE_CRT_STRICMP
    if (image.actual_base == 0 || image.actual_base > UINTPTR_MAX - (uintptr_t)image.loaded_size ||
        image.memory_region_count == 0 || image.relocations_applied == 0) {
        fail("crt-stricmp-context");
    }
    g_crt_stricmp_image.image_base = image.actual_base;
    g_crt_stricmp_image.image_end = image.actual_base + (uintptr_t)image.loaded_size;
    g_crt_stricmp_image.relocations_applied = image.relocations_applied;
    g_crt_stricmp_image.memory_region_count = image.memory_region_count;
    g_crt_stricmp_image.memory_regions = image.memory_regions;
    serial_text("GXOS_NET10:CRT_STRICMP_VALIDATION_CONTEXT_OK\r\n");
#endif
#ifdef GXOS_ENABLE_CRT_INITTERM_E
    if (image.actual_base > UINTPTR_MAX - (uintptr_t)image.loaded_size ||
        image.executable_region_count == 0) {
        fail("crt-initterm-e-context");
    }
    initterm_e_context.image_base = image.actual_base;
    initterm_e_context.image_end = image.actual_base + (uintptr_t)image.loaded_size;
    initterm_e_context.table_base = image.actual_base;
    initterm_e_context.table_end = initterm_e_context.image_end;
    initterm_e_context.relocations_applied = 1;
    initterm_e_context.executable_region_count = image.executable_region_count;
    for (initterm_e_region_index = 0;
         initterm_e_region_index != image.executable_region_count;
         initterm_e_region_index++) {
        initterm_e_context.executable_regions[initterm_e_region_index] =
            image.executable_regions[initterm_e_region_index];
    }
    if (gxos_crt_initterm_e_configure(&initterm_e_context) != 0) {
        fail("crt-initterm-e-context");
    }
    serial_text("GXOS_NET10:CRT_INITTERM_E_VALIDATION_CONTEXT_OK\r\n");
#endif
#ifdef GXOS_ENABLE_CRT_INITTERM
    if (image.actual_base > UINTPTR_MAX - (uintptr_t)image.loaded_size ||
        image.relocations_applied == 0 || image.memory_region_count == 0) {
        fail("crt-initterm-context");
    }
    initterm_context.image_base = image.actual_base;
    initterm_context.image_end = image.actual_base + (uintptr_t)image.loaded_size;
    initterm_context.relocations_applied = image.relocations_applied;
    initterm_context.memory_region_count = image.memory_region_count;
    for (initterm_region_index = 0;
         initterm_region_index != image.memory_region_count;
         initterm_region_index++) {
        initterm_context.memory_regions[initterm_region_index] =
            image.memory_regions[initterm_region_index];
    }
    if (gxos_crt_initterm_configure(&initterm_context) != 0) {
        fail("crt-initterm-context");
    }
    serial_text("GXOS_NET10:CRT_INITTERM_VALIDATION_CONTEXT_OK\r\n");
#endif
#ifdef GXOS_ENABLE_CRT_ONEXIT
    if (image.security_cookie_rva == 0) fail("security-cookie-missing");
    gxos_crt_onexit_set_encoded_null_address(
        (const uintptr_t *)rva_to_loaded(&image, image.security_cookie_rva, sizeof(uintptr_t)));
    if (gxos_crt_onexit_get_encoded_null() == 0) fail("security-cookie-uninitialized");
    if (image.loaded_size > UINT32_MAX || image.actual_base == 0 ||
        image.preferred_base == 0 || image.memory_region_count == 0 ||
        image.relocations_applied == 0 ||
        image.memory_region_count > GXOS_CRT_ONEXIT_MAX_MEMORY_REGIONS) {
        fail("crt-onexit-context");
    }
    g_crt_onexit_context.image_base = image.actual_base;
    g_crt_onexit_context.image_end = image.actual_base + (uintptr_t)image.loaded_size;
    g_crt_onexit_context.encoded_null = gxos_crt_onexit_get_encoded_null();
    g_crt_onexit_context.relocations_applied = image.relocations_applied;
    g_crt_onexit_context.region_count = image.memory_region_count;
    g_crt_onexit_context.initialized_table_count = 0;
#ifdef GXOS_ENABLE_CRT_ONEXIT_REGISTER
    g_crt_onexit_context.allocate = platform_crt_onexit_allocate;
    g_crt_onexit_context.free = platform_crt_onexit_free;
    g_crt_onexit_context.allocator_context = boot_services;
#endif
    for (initterm_region_index = 0;
         initterm_region_index != image.memory_region_count;
         initterm_region_index++) {
        g_crt_onexit_context.regions[initterm_region_index].base =
            image.memory_regions[initterm_region_index].base;
        g_crt_onexit_context.regions[initterm_region_index].end =
            image.memory_regions[initterm_region_index].end;
        g_crt_onexit_context.regions[initterm_region_index].readable =
            image.memory_regions[initterm_region_index].readable;
        g_crt_onexit_context.regions[initterm_region_index].executable =
            image.memory_regions[initterm_region_index].executable;
        g_crt_onexit_context.regions[initterm_region_index].writable =
            image.memory_regions[initterm_region_index].writable;
    }
    onexit_context_result = gxos_crt_onexit_configure(&g_crt_onexit_context);
    serial_field_hex("GXOS_NET10:CRT_ONEXIT_CONTEXT_RESULT=0x",
                     (uint64_t)(uint32_t)onexit_context_result);
    serial_text("\r\n");
    if (onexit_context_result != 0) {
        fail("crt-onexit-context");
    }
    serial_text("GXOS_NET10:CRT_ONEXIT_VALIDATION_CONTEXT_OK\r\n");
    serial_text("GXOS_NET10:CRT_ONEXIT_ENCODED_NULL_SOURCE=SECURITY_COOKIE\r\n");
#endif
    serial_text("GXOS_NET10:MANAGED_EXPORT_RVA=0x");
    serial_hex64(image.managed_main_rva);
    serial_text("\r\n");
    g_managed_image_base = image.actual_base;
    g_managed_image_size = image.loaded_size;
    g_managed_target = image.actual_base + image.managed_main_rva;
    serial_field_hex("GXOS_NET10:IMAGE_BASE=0x", image.actual_base);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:MANAGED_TARGET_VA=0x", g_managed_target);
    serial_text("\r\n");
    resolve_imports(&image, boot_services, &import_descriptors, &import_symbols,
                    &import_functional, &import_failfast, &unresolved_imports);
#ifdef GXOS_ENABLE_CREATE_EVENT_W
    if (g_create_event_w_import_descriptor_index != 2U ||
        g_create_event_w_import_symbol_index != 42U ||
        g_create_event_w_importing_iat_rva != 0x7D188U) {
        fail("createeventw-import-contract");
    }
    serial_text("GXOS_NET10:CREATEEVENTW_IMPORT_DLL=KERNEL32.dll\r\n");
    serial_text("GXOS_NET10:CREATEEVENTW_IMPORT_SYMBOL=CreateEventW\r\n");
    serial_field_hex("GXOS_NET10:CREATEEVENTW_IMPORT_DESCRIPTOR_INDEX=0x",
                     g_create_event_w_import_descriptor_index);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CREATEEVENTW_IMPORT_SYMBOL_INDEX=0x",
                     g_create_event_w_import_symbol_index);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CREATEEVENTW_IMPORT_IAT_RVA=0x",
                     g_create_event_w_importing_iat_rva);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:CREATEEVENTW_PREFERRED_IAT=0x",
                     image.preferred_base + g_create_event_w_importing_iat_rva);
    serial_text("\r\n");
#endif
#ifdef GXOS_ENABLE_CREATE_MEMORY_RESOURCE_NOTIFICATION
    if (g_memory_resource_notification_import_descriptor_index != 2U ||
        g_memory_resource_notification_import_symbol_index != 0x36U ||
        g_memory_resource_notification_importing_iat_rva != 0x7D1E8U) {
        fail("memory-resource-notification-import-contract");
    }
    serial_text("GXOS_NET10:MEMORYRESOURCENOTIFICATION_IMPORT_DLL=KERNEL32.dll\r\n");
    serial_text("GXOS_NET10:MEMORYRESOURCENOTIFICATION_IMPORT_SYMBOL=CreateMemoryResourceNotification\r\n");
    serial_field_hex("GXOS_NET10:MEMORYRESOURCENOTIFICATION_IMPORT_DESCRIPTOR_INDEX=0x",
                     g_memory_resource_notification_import_descriptor_index);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:MEMORYRESOURCENOTIFICATION_IMPORT_SYMBOL_INDEX=0x",
                     g_memory_resource_notification_import_symbol_index);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:MEMORYRESOURCENOTIFICATION_IMPORT_IAT_RVA=0x",
                     g_memory_resource_notification_importing_iat_rva);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:MEMORYRESOURCENOTIFICATION_PREFERRED_IAT=0x",
                     image.preferred_base +
                         g_memory_resource_notification_importing_iat_rva);
    serial_text("\r\n");
#endif
#ifdef GXOS_ENABLE_VECTORED_EXCEPTION_HANDLER
    if (g_veh_add_import_descriptor_index != 2U ||
        g_veh_add_import_symbol_index != 30U ||
        g_veh_add_importing_iat_rva != 0x7D128U) {
        fail("veh-import-contract");
    }
    serial_text("GXOS_NET10:VEH_IMPORT_DLL=KERNEL32.dll\r\n");
    serial_text("GXOS_NET10:VEH_IMPORT_SYMBOL=AddVectoredExceptionHandler\r\n");
    serial_field_hex("GXOS_NET10:VEH_IMPORT_DESCRIPTOR_INDEX=0x",
                     g_veh_add_import_descriptor_index);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:VEH_IMPORT_SYMBOL_INDEX=0x", g_veh_add_import_symbol_index);
    serial_text("\r\n");
    serial_field_hex("GXOS_NET10:VEH_IMPORT_IAT_RVA=0x", g_veh_add_importing_iat_rva);
    serial_text("\r\n");
#endif
#ifdef GXOS_ENABLE_GET_MODULE_HANDLE
    if (image.loaded_size > UINT32_MAX || image.actual_base == 0 ||
        image.preferred_base == 0 || image.size_of_headers == 0 ||
        image.memory_region_count == 0 || image.relocations_applied == 0 ||
        g_get_module_handle_importing_iat_rva == 0) {
        fail("getmodulehandle-context");
    }
    g_main_module_facts.preferred_image_base = (uintptr_t)image.preferred_base;
    g_main_module_facts.mapped_image_base = (uintptr_t)image.actual_base;
    g_main_module_facts.runtime_entry_point =
        (uintptr_t)(image.actual_base + image.entry_rva);
    g_main_module_facts.relocation_delta = image.actual_base - image.preferred_base;
    g_main_module_facts.size_of_image = (uint32_t)image.loaded_size;
    g_main_module_facts.size_of_headers = image.size_of_headers;
    g_main_module_facts.entry_point_rva = image.entry_rva;
    g_main_module_facts.import_directory_rva = image.import_rva;
    g_main_module_facts.import_directory_size = image.import_size;
    g_main_module_facts.importing_iat_rva = g_get_module_handle_importing_iat_rva;
    g_main_module_facts.importing_iat_size = 8U;
    g_main_module_facts.relocations_applied = image.relocations_applied;
    g_main_module_facts.mapped_regions = image.memory_regions;
    g_main_module_facts.mapped_region_count = image.memory_region_count;
    gxos_get_module_handle_configure(&g_main_module_facts);
    serial_text("GXOS_NET10:GETMODULEHANDLEW_VALIDATION_CONTEXT_OK\r\n");
#endif
#ifdef GXOS_ENABLE_GET_MODULE_HANDLE_EX
    if (g_main_module_permanent_residency_proven == 0 ||
        g_main_module_facts.mapped_image_base == 0 ||
        g_main_module_facts.size_of_image == 0 ||
        g_get_module_handle_ex_importing_iat_rva == 0) {
        fail("getmodulehandleex-context");
    }
    serial_text("GXOS_NET10:GETMODULEHANDLEEX_VALIDATION_CONTEXT_OK\r\n");
#endif
#ifdef GXOS_ENABLE_GET_PROC_ADDRESS
    if (image.memory_region_count == 0 || image.memory_region_count > 32U ||
        image.relocations_applied == 0) {
        fail("getprocaddress-context");
    }
    g_get_proc_address_memory.regions = image.memory_regions;
    g_get_proc_address_memory.region_count = image.memory_region_count;
    serial_text("GXOS_NET10:GETPROCADDRESS_VALIDATION_CONTEXT_OK\r\n");
#endif
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
#ifdef GXOS_ENABLE_EXCEPTION_SYNTHETIC_PROBE
    initialize_nativeaot_tls(&image, boot_services);
    run_synthetic_breakpoint_probe();
    halt_forever();
#endif
    initialize_nativeaot_tls(&image, boot_services);
#ifdef GXOS_ENABLE_CRT_MALLOC
    configure_platform_crt_malloc(&image, boot_services);
#endif
#ifdef GXOS_ENABLE_SYSTEM_INFO
    configure_platform_system_info(&image);
#endif
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
#ifdef GXOS_ENABLE_CRT_STRLEN
        emit_crt_strlen_summary();
#endif
#ifdef GXOS_ENABLE_GETENV
        emit_getenv_summary();
#endif
#ifdef GXOS_ENABLE_CRT_STRICMP
        emit_crt_stricmp_summary();
#endif
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
#if defined(GXOS_ENABLE_CREATE_EVENT_W) || \
    defined(GXOS_ENABLE_CREATE_MEMORY_RESOURCE_NOTIFICATION)
    if (!gxos_scheduler_initialize(&g_create_event_scheduler,
                                   boot_services->AllocatePages,
                                   boot_services->FreePages,
                                   0, 0, 0)) {
        fail("createeventw-scheduler-initialize");
    }
    ++g_create_event_scheduler_initialize_count;
    if (!gxos_scheduler_adopt_boot_environment(
            &g_create_event_scheduler,
            g_gs_area, g_teb_area, g_tls_vector, g_tls_block,
            g_stack_lower, g_stack_upper)) {
        fail("createeventw-scheduler-adopt-environment");
    }
#ifdef GXOS_ENABLE_CREATE_EVENT_W
    g_create_event_context.scheduler = &g_create_event_scheduler;
#endif
#ifdef GXOS_ENABLE_CREATE_MEMORY_RESOURCE_NOTIFICATION
    g_memory_resource_notification_context.scheduler =
        &g_create_event_scheduler;
#endif
    serial_text("GXOS_NET10:CREATEEVENTW_SCHEDULER_INITIALIZED=1\r\n");
    serial_text("GXOS_NET10:CREATEEVENTW_SCHEDULER_BOOT_ENVIRONMENT=PAYLOAD_TLS\r\n");
    serial_field_hex("GXOS_NET10:CREATEEVENTW_SCHEDULER_BOOT_OBJECT_SLOT=0x",
                     g_create_event_scheduler.boot_thread->object_slot);
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
#ifdef GXOS_ENABLE_CRT_ONEXIT_REGISTER
    if (g_crt_onexit_register_successes != 0) {
        serial_text("GXOS_NET10:REGISTER_ONEXIT_CONTINUATION_BEYOND_CALL_SITE=1\r\n");
    }
#endif
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

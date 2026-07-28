#include <stdint.h>

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

#define EFIAPI
#define EFI_SUCCESS ((EFI_STATUS)0)
#define EFI_ERROR(status) (((status) >> 63) != 0)
#define EFI_OPEN_MODE_READ ((uint64_t)1)
#define EFI_ALLOCATE_ANY_PAGES ((uint32_t)0)
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
    void *RuntimeServices;
    EFI_BOOT_SERVICES *BootServices;
    EFI_UINTN NumberOfTableEntries;
    void *ConfigurationTable;
} EFI_SYSTEM_TABLE;

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
} PE_IMAGE;

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

static uint32_t count_import_descriptors(const PE_IMAGE *image)
{
    uint32_t count = 0;
    uint32_t cursor = 0;
    while (cursor + 20 <= image->import_size && count < 4096) {
        const uint8_t *descriptor = rva_to_file(image, image->import_rva + cursor, 20);
        if (!descriptor) fail("import-bounds");
        if (read_u32(descriptor) == 0 && read_u32(descriptor + 4) == 0 &&
            read_u32(descriptor + 8) == 0 && read_u32(descriptor + 12) == 0 &&
            read_u32(descriptor + 16) == 0) {
            break;
        }
        count++;
        cursor += 20;
    }
    return count;
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
    uint32_t import_count;

    serial_init();
    serial_text("GXOS_NET10:LOADER_START\r\n");
    boot_services = system_table->BootServices;
    read_payload(image_handle, system_table, &image);
    serial_text("GXOS_NET10:PE_READ_OK\r\n");
    load_pe_image(&image, boot_services);
    serial_text("GXOS_NET10:PE_RELOCATIONS_OK\r\n");
    serial_text("GXOS_NET10:MANAGED_EXPORT_RVA=0x");
    serial_hex64(image.managed_main_rva);
    serial_text("\r\n");
    import_count = count_import_descriptors(&image);
    serial_text("GXOS_NET10:PE_IMPORT_COUNT=");
    serial_u32(import_count);
    serial_text("\r\n");
    if (import_count != 0) {
        serial_text("GXOS_NET10:GATE4_BLOCKED_IMPORTS\r\n");
        halt_forever();
    }
    fail("unexpected-import-free-path-not-implemented");
    return EFI_SUCCESS;
}

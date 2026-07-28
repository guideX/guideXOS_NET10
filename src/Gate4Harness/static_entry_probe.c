#include <stdint.h>

typedef struct {
    uint32_t Magic;
    uint16_t Version;
    uint16_t Size;
    uint32_t Architecture;
    uint32_t Flags;
    uint64_t SerialWrite;
} GX_BOOT_INFO;

extern int ManagedMain(uintptr_t boot_info_address);

uintptr_t __security_cookie = 0x2B992DDFA232ULL;

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
    serial_out8(0x3F9, 0x00);
    serial_out8(0x3FB, 0x80);
    serial_out8(0x3F8, 0x03);
    serial_out8(0x3F9, 0x00);
    serial_out8(0x3FB, 0x03);
    serial_out8(0x3FA, 0xC7);
    serial_out8(0x3FC, 0x0B);
}

static void serial_write(const uint8_t *bytes, uintptr_t length)
{
    while (length-- != 0) {
        while ((serial_in8(0x3FD) & 0x20) == 0) { }
        serial_out8(0x3F8, *bytes++);
    }
}

void efi_main(void *image_handle, void *system_table)
{
    GX_BOOT_INFO boot_info = {
        0x534F5847u, 1, 24, 0x8664u, 0, (uintptr_t)serial_write
    };
    (void)image_handle;
    (void)system_table;
    serial_init();
    serial_write((const uint8_t *)"GXOS_NET10:NATIVE_SHIM_BEFORE\r\n", 32);
    int result = ManagedMain((uintptr_t)&boot_info);
    serial_write((const uint8_t *)"GXOS_NET10:NATIVE_SHIM_RETURN\r\n", 32);
    if (result != 0) {
        serial_write((const uint8_t *)"GXOS_NET10:NATIVE_SHIM_FAIL\r\n", 30);
    }
    for (;;) __asm__ volatile ("hlt");
}

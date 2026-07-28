#include <stdint.h>

static void out8(uint16_t port, uint8_t value)
{
    __asm__ volatile ("outb %0, %1" : : "a"(value), "Nd"(port));
}

static uint8_t in8(uint16_t port)
{
    uint8_t value;
    __asm__ volatile ("inb %1, %0" : "=a"(value) : "Nd"(port));
    return value;
}

static void serial_text(const char *text)
{
    out8(0x3FB, 0x80);
    out8(0x3F8, 0x03);
    out8(0x3F9, 0x00);
    out8(0x3FB, 0x03);
    out8(0x3FA, 0xC7);
    out8(0x3FC, 0x0B);
    while (*text != 0) {
        while ((in8(0x3FD) & 0x20) == 0) { }
        out8(0x3F8, (uint8_t)*text++);
    }
}

void efi_main(void *image_handle, void *system_table)
{
    (void)image_handle;
    (void)system_table;
    serial_text("GXOS_NET10:FIRMWARE_PROBE_OK\r\n");
    __asm__ volatile ("outl %0, %1" : : "a"(0x10u), "Nd"((uint16_t)0xF4));
    for (;;) {
        __asm__ volatile ("hlt");
    }
}

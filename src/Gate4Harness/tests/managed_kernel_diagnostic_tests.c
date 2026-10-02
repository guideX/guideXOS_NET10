#include <stdio.h>
#include <string.h>

#include "../managed_kernel_diagnostic.h"

#define TEST_UART_CAPACITY 256U
#define TEST_RESPONSE_CAPACITY 512U

static unsigned failures;
static uint32_t fake_status_calls;
static uint32_t fake_restart_calls;
static uint32_t fake_expected_identity;
static uint16_t fake_expected_generation;
static uint32_t fake_expected_device;
static GXOS_MANAGED_KERNEL_DRIVER_RESTART_RESULT fake_restart_result;
static uint8_t fake_uart_rx[TEST_UART_CAPACITY];
static uint32_t fake_uart_rx_read;
static uint32_t fake_uart_rx_write;
static uint8_t fake_uart_tx[TEST_RESPONSE_CAPACITY];
static uint32_t fake_uart_tx_count;
static uint32_t fake_eoi_count;
static GXOS_MANAGED_KERNEL_DIAGNOSTIC_CONTEXT diagnostic;
static GXOS_MANAGED_KERNEL_DRIVER_WORKER_CONTEXT dummy_service;
static GXOS_MANAGED_KERNEL_DRIVER_SERVICE_HANDLE current_handle;

static void expect(int condition, const char *message)
{
    if (condition) return;
    ++failures;
    printf("FAIL: %s\n", message);
}

static GXOS_MANAGED_KERNEL_DRIVER_RESTART_RESULT fake_get_status(
    const GXOS_MANAGED_KERNEL_DRIVER_WORKER_CONTEXT *service,
    GXOS_MANAGED_KERNEL_DRIVER_SERVICE_STATUS_V1 *status)
{
    expect(service == &dummy_service, "status API receives only bound context");
    ++fake_status_calls;
    *status = (GXOS_MANAGED_KERNEL_DRIVER_SERVICE_STATUS_V1){
        .structure_size = sizeof(*status),
        .version = 1U,
        .service_slot = 0U,
        .device_identity = 1U,
        .owner_state = GXOS_MANAGED_KERNEL_DRIVER_SERVICE_RESTART_FAILED,
        .current_service_valid = 0U,
        .current_service_identity = 0U,
        .current_generation = 0U,
        .route_enabled = 0U,
        .runtime_attached = 0U,
        .last_failure_reason =
            GXOS_MANAGED_KERNEL_DRIVER_FAILURE_AUTOMATIC_REPLACEMENT_ADMISSION,
        .last_failed_identity = 0x12345678U,
        .last_failed_generation = 7U,
        .last_failed_device_identity = 1U,
        .restart_budget_remaining = 0U,
        .automatic_restart_attempts = 1U,
        .restart_failed = 1U,
        .explicit_restart_allowed = 1U,
        .explicit_restart_in_progress = 0U
    };
    return GXOS_MANAGED_KERNEL_DRIVER_RESTART_RESULT_OK;
}

static GXOS_MANAGED_KERNEL_DRIVER_RESTART_RESULT fake_restart(
    GXOS_MANAGED_KERNEL_DRIVER_WORKER_CONTEXT *service,
    uint32_t expected_identity, uint16_t expected_generation,
    uint32_t expected_device,
    GXOS_MANAGED_KERNEL_DRIVER_SERVICE_HANDLE *handle)
{
    expect(service == &dummy_service, "restart API receives only bound context");
    ++fake_restart_calls;
    fake_expected_identity = expected_identity;
    fake_expected_generation = expected_generation;
    fake_expected_device = expected_device;
    if (fake_restart_result == GXOS_MANAGED_KERNEL_DRIVER_RESTART_RESULT_OK) {
        *handle = (GXOS_MANAGED_KERNEL_DRIVER_SERVICE_HANDLE){
            .identity = 2U,
            .device_identity = expected_device,
            .generation = 8U,
            .slot = 0U};
    }
    return fake_restart_result;
}

static uint8_t fake_read_iir(void *opaque)
{
    (void)opaque;
    return fake_uart_rx_read == fake_uart_rx_write ? 0x01U : 0x04U;
}

static uint8_t fake_read_lsr(void *opaque)
{
    (void)opaque;
    return fake_uart_rx_read == fake_uart_rx_write ? 0U : 1U;
}

static uint8_t fake_read_data(void *opaque)
{
    (void)opaque;
    if (fake_uart_rx_read == fake_uart_rx_write) return 0U;
    return fake_uart_rx[fake_uart_rx_read++ % TEST_UART_CAPACITY];
}

static int fake_transmit_byte(void *opaque, uint8_t value)
{
    (void)opaque;
    if (fake_uart_tx_count >= TEST_RESPONSE_CAPACITY) return 0;
    fake_uart_tx[fake_uart_tx_count++] = value;
    return 1;
}

static void fake_eoi(void *opaque)
{
    (void)opaque;
    ++fake_eoi_count;
}

static void reset_fixture(void)
{
    GXOS_MANAGED_KERNEL_SECONDARY_UART_CONFIG resource = {
        .present = 1U, .io_base = 0x2F8U, .irq = 3U, .reserved = 0U};
    fake_status_calls = 0U;
    fake_restart_calls = 0U;
    fake_expected_identity = 0U;
    fake_expected_generation = 0U;
    fake_expected_device = 0U;
    fake_restart_result = GXOS_MANAGED_KERNEL_DRIVER_RESTART_RESULT_OK;
    fake_uart_rx_read = 0U;
    fake_uart_rx_write = 0U;
    fake_uart_tx_count = 0U;
    fake_eoi_count = 0U;
    current_handle = (GXOS_MANAGED_KERNEL_DRIVER_SERVICE_HANDLE){0};
    memset(fake_uart_rx, 0, sizeof(fake_uart_rx));
    memset(fake_uart_tx, 0, sizeof(fake_uart_tx));
    expect(gxos_managed_kernel_diagnostic_initialize(
               &diagnostic, &resource, &dummy_service, &current_handle,
               fake_get_status, fake_restart, fake_read_iir, fake_read_lsr, fake_read_data,
               fake_transmit_byte, fake_eoi, &diagnostic),
           "diagnostic context initializes with explicit UART resource");
    gxos_managed_kernel_diagnostic_set_enabled(&diagnostic, 1);
}

static void encode_request(
    uint8_t request[GXOS_MANAGED_KERNEL_DIAGNOSTIC_REQUEST_BYTES],
    uint32_t request_id, uint16_t command, uint32_t expected_identity,
    uint16_t expected_generation, uint32_t target)
{
    memset(request, 0, GXOS_MANAGED_KERNEL_DIAGNOSTIC_REQUEST_BYTES);
    request[0] = 'G'; request[1] = 'X'; request[2] = 'D'; request[3] = 'C';
    request[4] = 1U; request[5] = 0U;
    request[6] = 32U; request[7] = 0U;
    request[8] = (uint8_t)request_id;
    request[9] = (uint8_t)(request_id >> 8);
    request[10] = (uint8_t)(request_id >> 16);
    request[11] = (uint8_t)(request_id >> 24);
    request[12] = (uint8_t)command;
    request[13] = (uint8_t)(command >> 8);
    request[16] = (uint8_t)target;
    request[17] = (uint8_t)(target >> 8);
    request[18] = (uint8_t)(target >> 16);
    request[19] = (uint8_t)(target >> 24);
    request[20] = (uint8_t)expected_identity;
    request[21] = (uint8_t)(expected_identity >> 8);
    request[22] = (uint8_t)(expected_identity >> 16);
    request[23] = (uint8_t)(expected_identity >> 24);
    request[24] = (uint8_t)expected_generation;
    request[25] = (uint8_t)(expected_generation >> 8);
    {
        uint32_t crc = gxos_managed_kernel_diagnostic_crc32(request, 28U);
        request[28] = (uint8_t)crc;
        request[29] = (uint8_t)(crc >> 8);
        request[30] = (uint8_t)(crc >> 16);
        request[31] = (uint8_t)(crc >> 24);
    }
}

static void queue_bytes(const uint8_t *bytes, uint32_t count)
{
    uint32_t index;
    expect(fake_uart_rx_write - fake_uart_rx_read + count <=
               TEST_UART_CAPACITY,
           "test UART buffer stays bounded");
    for (index = 0; index != count; ++index) {
        fake_uart_rx[fake_uart_rx_write++ % TEST_UART_CAPACITY] = bytes[index];
    }
}

static void pump_uart_until_empty(void)
{
    uint32_t guard = 0U;
    while (fake_uart_rx_read != fake_uart_rx_write && guard++ != 32U) {
        (void)gxos_managed_kernel_diagnostic_irq_capture(&diagnostic);
        (void)gxos_managed_kernel_diagnostic_poll(&diagnostic);
    }
    while (__atomic_load_n(&diagnostic.rx_count, __ATOMIC_ACQUIRE) != 0U &&
           guard++ != 48U) {
        (void)gxos_managed_kernel_diagnostic_poll(&diagnostic);
    }
}

static uint32_t response_u16(uint32_t offset)
{
    return (uint32_t)fake_uart_tx[offset] |
           ((uint32_t)fake_uart_tx[offset + 1U] << 8);
}

static uint32_t response_u32(uint32_t offset)
{
    return (uint32_t)fake_uart_tx[offset] |
           ((uint32_t)fake_uart_tx[offset + 1U] << 8) |
           ((uint32_t)fake_uart_tx[offset + 2U] << 16) |
           ((uint32_t)fake_uart_tx[offset + 3U] << 24);
}

static int response_crc_valid(void)
{
    uint32_t frame_bytes;
    if (fake_uart_tx_count < 24U) return 0;
    frame_bytes = response_u16(6U);
    return frame_bytes == fake_uart_tx_count &&
           response_u32(frame_bytes - 4U) ==
               gxos_managed_kernel_diagnostic_crc32(fake_uart_tx,
                                                     frame_bytes - 4U);
}

static void test_crc_and_status_encoding(void)
{
    static const uint8_t check[] = "123456789";
    uint8_t request[GXOS_MANAGED_KERNEL_DIAGNOSTIC_REQUEST_BYTES];
    reset_fixture();
    expect(gxos_managed_kernel_diagnostic_crc32(check, 9U) == 0xCBF43926U,
           "CRC32 matches the standard reflected CRC-32 check value");
    encode_request(request, 0xA1B2C3D4U,
                   GXOS_MANAGED_KERNEL_DIAGNOSTIC_STATUS_COMMAND, 0U, 0U, 1U);
    expect(request[8] == 0xD4U && request[9] == 0xC3U &&
           request[10] == 0xB2U && request[11] == 0xA1U &&
           request[16] == 1U && request[17] == 0U,
           "request fields encode explicitly in little-endian order");
    queue_bytes(request, sizeof(request));
    pump_uart_until_empty();
    expect(fake_status_calls == 1U && fake_restart_calls == 0U,
           "valid STATUS calls only Phase 72 status API");
    expect(fake_uart_tx_count == 100U && response_u16(6U) == 100U &&
           response_u32(8U) == 0xA1B2C3D4U && response_u16(12U) == 1U &&
           response_u32(16U) == 0U && response_u32(32U) == 1U &&
           response_u32(36U) == 14U && response_u32(40U) == 0U &&
           response_u32(64U) == 0x12345678U && response_crc_valid(),
           "STATUS reply has bounded 100-byte little-endian record and CRC");
    expect(diagnostic.rx_pending == 0U && diagnostic.rx_count == 0U &&
           diagnostic.irq_bytes == sizeof(request),
           "IRQ capture and boot-thread drain account the complete request");
}

static void test_restart_encoding(void)
{
    uint8_t request[GXOS_MANAGED_KERNEL_DIAGNOSTIC_REQUEST_BYTES];
    reset_fixture();
    encode_request(request, 9U,
                   GXOS_MANAGED_KERNEL_DIAGNOSTIC_RESTART_COMMAND,
                   0x12345678U, 7U, 1U);
    queue_bytes(request, sizeof(request));
    pump_uart_until_empty();
    expect(fake_status_calls == 0U && fake_restart_calls == 1U &&
           fake_expected_identity == 0x12345678U &&
           fake_expected_generation == 7U && fake_expected_device == 1U &&
           current_handle.identity == 2U && current_handle.generation == 8U,
           "valid RESTART forwards the expected failed tuple");
    expect(fake_uart_tx_count == 36U && response_u16(6U) == 36U &&
           response_u32(16U) == 0U && response_u32(20U) == 2U &&
           response_u32(24U) == 1U && response_u16(28U) == 8U &&
           response_u16(30U) == 0U && response_crc_valid(),
           "successful RESTART reply contains only the new value handle");
}

static void test_rejections_do_not_call_service(void)
{
    uint8_t request[GXOS_MANAGED_KERNEL_DIAGNOSTIC_REQUEST_BYTES];
    uint8_t garbage[GXOS_MANAGED_KERNEL_DIAGNOSTIC_REQUEST_BYTES] = {0};
    uint32_t before_status;
    uint32_t before_restart;
    uint32_t index;
    reset_fixture();
    encode_request(request, 1U,
                   GXOS_MANAGED_KERNEL_DIAGNOSTIC_STATUS_COMMAND, 0U, 0U, 1U);
    request[0] = 'B';
    queue_bytes(request, sizeof(request));
    pump_uart_until_empty();
    expect(fake_status_calls == 0U && fake_uart_tx_count == 0U,
           "bad magic is discarded without a service call or reply");
    encode_request(request, 2U,
                   GXOS_MANAGED_KERNEL_DIAGNOSTIC_STATUS_COMMAND, 0U, 0U, 1U);
    request[4] = 2U;
    queue_bytes(request, sizeof(request));
    pump_uart_until_empty();
    encode_request(request, 3U,
                   GXOS_MANAGED_KERNEL_DIAGNOSTIC_STATUS_COMMAND, 0U, 0U, 1U);
    request[6] = 31U;
    queue_bytes(request, sizeof(request));
    pump_uart_until_empty();
    encode_request(request, 4U,
                   GXOS_MANAGED_KERNEL_DIAGNOSTIC_STATUS_COMMAND, 0U, 0U, 1U);
    request[31] ^= 0x80U;
    queue_bytes(request, sizeof(request));
    pump_uart_until_empty();
    encode_request(request, 5U,
                   GXOS_MANAGED_KERNEL_DIAGNOSTIC_STATUS_COMMAND, 0U, 0U, 2U);
    queue_bytes(request, sizeof(request));
    pump_uart_until_empty();
    encode_request(request, 6U,
                   GXOS_MANAGED_KERNEL_DIAGNOSTIC_STATUS_COMMAND, 0U, 0U, 1U);
    request[14] = 1U;
    queue_bytes(request, sizeof(request));
    pump_uart_until_empty();
    encode_request(request, 7U,
                   GXOS_MANAGED_KERNEL_DIAGNOSTIC_STATUS_COMMAND, 0U, 0U, 1U);
    request[26] = 1U;
    queue_bytes(request, sizeof(request));
    pump_uart_until_empty();
    encode_request(request, 8U, 77U, 0U, 0U, 1U);
    queue_bytes(request, sizeof(request));
    pump_uart_until_empty();
    encode_request(request, 9U,
                   GXOS_MANAGED_KERNEL_DIAGNOSTIC_STATUS_COMMAND,
                   1U, 0U, 1U);
    queue_bytes(request, sizeof(request));
    pump_uart_until_empty();
    encode_request(request, 10U,
                   GXOS_MANAGED_KERNEL_DIAGNOSTIC_RESTART_COMMAND,
                   0U, 0U, 1U);
    queue_bytes(request, sizeof(request));
    pump_uart_until_empty();
    before_status = fake_status_calls;
    before_restart = fake_restart_calls;
    encode_request(request, 11U,
                   GXOS_MANAGED_KERNEL_DIAGNOSTIC_STATUS_COMMAND, 0U, 0U, 1U);
    queue_bytes(request, 10U);
    pump_uart_until_empty();
    expect(fake_status_calls == before_status &&
           fake_restart_calls == before_restart && fake_uart_tx_count == 0U,
           "truncated request waits in bounded parser without API calls");
    queue_bytes(request, sizeof(request));
    pump_uart_until_empty();
    expect(fake_status_calls == before_status + 1U &&
           fake_restart_calls == before_restart,
           "parser resynchronizes after truncated input");
    for (index = 0; index != sizeof(garbage); ++index) garbage[index] = 0xA5U;
    (void)garbage;
    expect(diagnostic.malformed_request_count >= 9U,
           "malformed request counter records bounded rejection");
}

static void test_garbage_resynchronization_and_overflow(void)
{
    uint8_t request[GXOS_MANAGED_KERNEL_DIAGNOSTIC_REQUEST_BYTES];
    uint8_t sequence[48];
    uint8_t nested_prefix[GXOS_MANAGED_KERNEL_DIAGNOSTIC_REQUEST_BYTES + 7U];
    uint8_t fill[80];
    uint32_t index;
    reset_fixture();
    encode_request(request, 0x55U,
                   GXOS_MANAGED_KERNEL_DIAGNOSTIC_STATUS_COMMAND, 0U, 0U, 1U);
    memset(sequence, 0xA5, 5U);
    memcpy(&sequence[5], request, 10U);
    memcpy(&sequence[15], request, sizeof(request));
    queue_bytes(sequence, 47U);
    pump_uart_until_empty();
    expect(fake_status_calls == 1U && response_crc_valid(),
           "garbage and GXDC fragment resynchronize to following STATUS");

    reset_fixture();
    encode_request(request, 0x56U,
                   GXOS_MANAGED_KERNEL_DIAGNOSTIC_STATUS_COMMAND, 0U, 0U, 1U);
    nested_prefix[0] = 0xD0U;
    nested_prefix[1] = 'G';
    nested_prefix[2] = 'X';
    nested_prefix[3] = 'D';
    nested_prefix[4] = 'C';
    nested_prefix[5] = 1U;
    nested_prefix[6] = 0U;
    memcpy(&nested_prefix[7], request, sizeof(request));
    queue_bytes(nested_prefix, sizeof(nested_prefix));
    pump_uart_until_empty();
    expect(fake_status_calls == 1U && response_crc_valid() &&
           response_u32(8U) == 0x56U,
           "bad partial header resynchronizes immediately to a nested STATUS");

    reset_fixture();
    memset(fill, 0xA5, sizeof(fill));
    queue_bytes(fill, sizeof(fill));
    for (index = 0; index != 4U; ++index) {
        (void)gxos_managed_kernel_diagnostic_irq_capture(&diagnostic);
    }
    expect(diagnostic.rx_count == GXOS_MANAGED_KERNEL_DIAGNOSTIC_RX_CAPACITY &&
           diagnostic.rx_overflow_count == 0U,
           "RX ring holds 64 bytes without overwriting unread data");
    (void)gxos_managed_kernel_diagnostic_irq_capture(&diagnostic);
    expect(diagnostic.rx_count == GXOS_MANAGED_KERNEL_DIAGNOSTIC_RX_CAPACITY &&
           diagnostic.rx_overflow_count == 16U,
           "RX overflow drops new bytes and preserves queued bytes");
    while (diagnostic.rx_count != 0U) {
        (void)gxos_managed_kernel_diagnostic_poll(&diagnostic);
    }
    encode_request(request, 0x66U,
                   GXOS_MANAGED_KERNEL_DIAGNOSTIC_STATUS_COMMAND, 0U, 0U, 1U);
    queue_bytes(request, sizeof(request));
    pump_uart_until_empty();
    expect(fake_status_calls == 1U && response_crc_valid() &&
           diagnostic.parser_count == 0U,
           "parser recovers after RX overflow and accepts a later STATUS");
}

static void test_irq_and_dispatch_bounds(void)
{
    uint8_t bytes[80];
    uint8_t request[GXOS_MANAGED_KERNEL_DIAGNOSTIC_REQUEST_BYTES];
    uint32_t first_dispatch;
    reset_fixture();
    memset(bytes, 0x31, sizeof(bytes));
    queue_bytes(bytes, sizeof(bytes));
    expect(gxos_managed_kernel_diagnostic_irq_capture(&diagnostic) ==
               GXOS_MANAGED_KERNEL_DIAGNOSTIC_IRQ_DRAIN_MAX &&
           diagnostic.irq_bound_hits == 1U,
           "IRQ reads at most 16 bytes per entry");
    expect(fake_status_calls == 0U && fake_restart_calls == 0U,
           "IRQ capture never calls either service API");

    reset_fixture();
    encode_request(request, 1U,
                   GXOS_MANAGED_KERNEL_DIAGNOSTIC_STATUS_COMMAND, 0U, 0U, 1U);
    queue_bytes(request, sizeof(request));
    encode_request(request, 2U,
                   GXOS_MANAGED_KERNEL_DIAGNOSTIC_STATUS_COMMAND, 0U, 0U, 1U);
    queue_bytes(request, sizeof(request));
    for (first_dispatch = 0U; first_dispatch != 4U; ++first_dispatch) {
        (void)gxos_managed_kernel_diagnostic_irq_capture(&diagnostic);
    }
    expect(gxos_managed_kernel_diagnostic_poll(&diagnostic) == 0U &&
           diagnostic.rx_count == 48U && fake_status_calls == 0U,
           "boot-thread pass drains no more than 16 bytes");
    expect(gxos_managed_kernel_diagnostic_poll(&diagnostic) == 1U &&
           fake_status_calls == 1U && diagnostic.rx_count == 32U,
           "boot-thread pass dispatches at most one complete frame");
    expect(gxos_managed_kernel_diagnostic_poll(&diagnostic) == 0U &&
           diagnostic.rx_count == 16U && fake_status_calls == 1U,
           "boot-thread pass yields after its drain bound");
    expect(gxos_managed_kernel_diagnostic_poll(&diagnostic) == 1U &&
           fake_status_calls == 2U && diagnostic.rx_count == 0U,
           "later boot-thread pass dispatches the next complete frame");
}

int main(void)
{
    test_crc_and_status_encoding();
    test_restart_encoding();
    test_rejections_do_not_call_service();
    test_garbage_resynchronization_and_overflow();
    test_irq_and_dispatch_bounds();
    if (failures != 0U) {
        printf("MANAGED_KERNEL_DIAGNOSTIC_HOST_TESTS=FAILED failures=%u\n",
               failures);
        return 1;
    }
    printf("MANAGED_KERNEL_DIAGNOSTIC_HOST_TESTS=PASSED\n");
    return 0;
}

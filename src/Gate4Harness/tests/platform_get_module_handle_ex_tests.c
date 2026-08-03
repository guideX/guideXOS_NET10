#include <stdint.h>
#include <stdio.h>
#include <string.h>

#include "platform_get_module_handle_ex.h"

static unsigned failures;
static uint8_t image[0xD3000];
static GXOS_MODULE_HANDLE_MEMORY_REGION image_region;

static void expect(int condition, const char *name)
{
    if (!condition) {
        ++failures;
        printf("FAIL:%s\n", name);
    }
}

static GXOS_MAIN_MODULE_FACTS valid_facts(void)
{
    GXOS_MAIN_MODULE_FACTS facts;
    memset(&facts, 0, sizeof(facts));
    memset(image, 0, sizeof(image));
    image_region.base = (uintptr_t)image;
    image_region.end = image_region.base + sizeof(image);
    image_region.readable = 1;
    image_region.executable = 1;
    image_region.writable = 1;
    facts.preferred_image_base = (uintptr_t)0x180000000ULL;
    facts.mapped_image_base = (uintptr_t)image;
    facts.runtime_entry_point = facts.mapped_image_base + 0x77700U;
    facts.relocation_delta = (uint64_t)(facts.mapped_image_base -
                                        facts.preferred_image_base);
    facts.size_of_image = (uint32_t)sizeof(image);
    facts.size_of_headers = 0x400;
    facts.entry_point_rva = 0x77700;
    facts.import_directory_rva = 0xA8D4C;
    facts.import_directory_size = 0xDC;
    facts.importing_iat_rva = 0x7D1F8;
    facts.importing_iat_size = 8;
    facts.relocations_applied = 1;
    facts.mapped_regions = &image_region;
    facts.mapped_region_count = 1;
    return facts;
}

static GXOS_MODULE_HANDLE_EX_STATUS call_checked(
    uint32_t flags,
    uintptr_t address,
    GXOS_MODULE_HANDLE_HMODULE *output,
    uintptr_t lower,
    uintptr_t upper,
    uint32_t permanent,
    GXOS_MODULE_HANDLE_EX_REPORT *report)
{
    GXOS_MAIN_MODULE_FACTS facts = valid_facts();
    return gxos_get_module_handle_ex_checked(
        flags, address, output, &facts, lower, upper, permanent, report);
}

static void expect_unchanged_failure(
    uint32_t flags,
    uintptr_t address,
    const char *name)
{
    GXOS_MODULE_HANDLE_HMODULE output =
        (GXOS_MODULE_HANDLE_HMODULE)0xA5A5A5A5A5A5A5A5ULL;
    GXOS_MODULE_HANDLE_EX_REPORT report;
    uintptr_t lower = (uintptr_t)&output;
    uintptr_t upper = lower + sizeof(output);
    GXOS_MODULE_HANDLE_EX_STATUS status = call_checked(
        flags, address, &output, lower, upper, 1, &report);
    expect(status != GXOS_MODULE_HANDLE_EX_STATUS_OK, name);
    expect(output == (GXOS_MODULE_HANDLE_HMODULE)
                       0xA5A5A5A5A5A5A5A5ULL,
           "failure preserves output");
    expect(report.output_value_before == output &&
               report.output_value_after == output,
           "failure report preserves output");
    expect(report.output_written == 0, "failure does not write output");
}

static uintptr_t checked_rva_address(
    uintptr_t image_base,
    uint32_t rva,
    const char *name)
{
    expect((uintptr_t)rva <= UINTPTR_MAX - image_base, name);
    return image_base + (uintptr_t)rva;
}

static void run_positive_sequence(const uint32_t *rvas, uint32_t count)
{
    GXOS_MAIN_MODULE_FACTS facts = valid_facts();
    GXOS_MODULE_HANDLE_HMODULE output =
        (GXOS_MODULE_HANDLE_HMODULE)0xA5A5A5A5A5A5A5A5ULL;
    GXOS_MODULE_HANDLE_HMODULE expected =
        (GXOS_MODULE_HANDLE_HMODULE)facts.mapped_image_base;
    uintptr_t lower = (uintptr_t)&output;
    uintptr_t upper = lower + sizeof(output);
    uint32_t index;

    for (index = 0; index != count; ++index) {
        GXOS_MAIN_MODULE_FACTS facts_before = facts;
        GXOS_MODULE_HANDLE_EX_REPORT first_report;
        GXOS_MODULE_HANDLE_EX_REPORT repeat_report;
        GXOS_MODULE_HANDLE_EX_STATUS status;
        uintptr_t address = checked_rva_address(
            facts.mapped_image_base, rvas[index],
            "RVA addition is overflow-safe");

        status = gxos_get_module_handle_ex_checked(
            GXOS_MODULE_HANDLE_EX_EXPECTED_FLAGS, address, &output, &facts,
            lower, upper, 1, &first_report);
        expect(status == GXOS_MODULE_HANDLE_EX_STATUS_OK,
               "positive RVA succeeds");
        expect(first_report.flags == 0x5U && first_report.flags_exact == 1 &&
                   first_report.unknown_flag_bits == 0,
               "positive flags are exactly 0x5");
        expect(output == expected && first_report.result == expected &&
                   first_report.result != 0,
               "positive returns nonzero actual image base");
        expect(first_report.lookup_unique == 1 &&
                   first_report.lookup_match_count == 1,
               "positive lookup is unique");
        expect(first_report.image_identity ==
                   GXOS_MODULE_HANDLE_EX_IMAGE_MAIN_NATIVEAOT_PAYLOAD,
               "positive image identity");
        expect(first_report.selected_image_base == facts.mapped_image_base &&
                   first_report.selected_image_size == facts.size_of_image,
               "positive selects registered image descriptor");
        expect(first_report.address_rva == rvas[index],
               "positive RVA report");
        expect(first_report.output_pointer_proven_writable == 1 &&
                   first_report.output_written == 1 &&
                   first_report.output_value_after == expected,
               "positive proves and writes exact image base");
        expect(first_report.prior_pinned == 1 &&
                   first_report.resulting_pinned == 1 &&
                   first_report.residency_invariant_proven == 1,
               "positive pin state remains resident");
        expect(first_report.allocation_occurred == 0 &&
                   first_report.image_free_or_unload_invoked == 0,
               "positive has no loader allocation or free");
        expect(memcmp(&facts, &facts_before, sizeof(facts)) == 0,
               "positive leaves residency metadata unchanged");

        status = gxos_get_module_handle_ex_checked(
            GXOS_MODULE_HANDLE_EX_EXPECTED_FLAGS, address, &output, &facts,
            lower, upper, 1, &repeat_report);
        expect(status == GXOS_MODULE_HANDLE_EX_STATUS_OK && output == expected,
               "repeated request succeeds idempotently");
        expect(repeat_report.address_rva == first_report.address_rva &&
                   repeat_report.result == first_report.result &&
                   repeat_report.lookup_unique == first_report.lookup_unique &&
                   repeat_report.output_written == 1,
               "repeated request has the same result");
        expect(repeat_report.prior_pinned == 1 &&
                   repeat_report.resulting_pinned == 1 &&
                   repeat_report.allocation_occurred == 0 &&
                   repeat_report.image_free_or_unload_invoked == 0,
               "repeated request preserves permanent residency");
        expect(memcmp(&facts, &facts_before, sizeof(facts)) == 0,
               "repeated request leaves residency metadata unchanged");
    }
}

static void test_positive_rvas_and_idempotence(void)
{
    const uint32_t windows_order[] = {0x37C40U, 0x42110U, 0x29ABCU};
    const uint32_t different_order[] = {0x29ABCU, 0x37C40U, 0x42110U};

    run_positive_sequence(windows_order, 3);
    run_positive_sequence(different_order, 3);
}

static void test_image_boundaries_and_address_interpretation(void)
{
    GXOS_MAIN_MODULE_FACTS facts = valid_facts();
    GXOS_MODULE_HANDLE_HMODULE output;
    GXOS_MODULE_HANDLE_EX_REPORT report;
    uintptr_t lower;
    uintptr_t upper;
    uintptr_t image_base = facts.mapped_image_base;
    uintptr_t image_end = image_base + facts.size_of_image;
    const uint16_t utf16_like[] = {
        'n', 't', 'd', 'l', 'l', '.', 'd', 'l', 'l', 0
    };

    expect((uintptr_t)facts.size_of_image <= UINTPTR_MAX - image_base,
           "image end calculation is overflow-safe");
    output = 0;
    lower = (uintptr_t)&output;
    upper = lower + sizeof(output);
    expect(gxos_get_module_handle_ex_checked(
               GXOS_MODULE_HANDLE_EX_EXPECTED_FLAGS, image_base, &output,
               &facts, lower, upper, 1, &report) ==
               GXOS_MODULE_HANDLE_EX_STATUS_OK && output == image_base,
           "address at image base succeeds");

    output = 0;
    expect(gxos_get_module_handle_ex_checked(
               GXOS_MODULE_HANDLE_EX_EXPECTED_FLAGS, image_end - 1U, &output,
               &facts, lower, upper, 1, &report) ==
               GXOS_MODULE_HANDLE_EX_STATUS_OK && output == image_base &&
                   report.address_rva == facts.size_of_image - 1U,
           "address at image end minus one succeeds");

    memcpy(image + 0x5000U, utf16_like, sizeof(utf16_like));
    output = 0;
    expect(gxos_get_module_handle_ex_checked(
               GXOS_MODULE_HANDLE_EX_EXPECTED_FLAGS,
               image_base + 0x5000U, &output, &facts, lower, upper, 1,
               &report) == GXOS_MODULE_HANDLE_EX_STATUS_OK &&
                   output == image_base && report.address_rva == 0x5000U,
           "RDX is treated as an address, not UTF-16 text");
}

static void test_negative_inputs(void)
{
    const uintptr_t image_base = (uintptr_t)image;
    const uintptr_t image_end = image_base + sizeof(image);
    GXOS_MODULE_HANDLE_HMODULE output;
    GXOS_MODULE_HANDLE_EX_REPORT report;
    uintptr_t lower;
    uintptr_t upper;
    uint8_t tiny[sizeof(uintptr_t)];
    GXOS_MAIN_MODULE_FACTS facts = valid_facts();

    expect((uintptr_t)sizeof(image) <= UINTPTR_MAX - image_base,
           "negative-test image end calculation is overflow-safe");
    expect_unchanged_failure(0, image_base + 0x37C40U, "flags zero rejected");
    expect_unchanged_failure(0x4U, image_base + 0x37C40U,
                              "flags from-address-only rejected");
    expect_unchanged_failure(0x1U, image_base + 0x37C40U,
                              "flags pin-only rejected");
    expect_unchanged_failure(0x80000005U, image_base + 0x37C40U,
                              "unknown flag rejected");
    expect_unchanged_failure(GXOS_MODULE_HANDLE_EX_EXPECTED_FLAGS, 0,
                              "null address rejected");
    expect_unchanged_failure(GXOS_MODULE_HANDLE_EX_EXPECTED_FLAGS,
                              image_base - 1U, "address below image rejected");
    expect_unchanged_failure(GXOS_MODULE_HANDLE_EX_EXPECTED_FLAGS, image_end,
                              "address at image end rejected");
    expect_unchanged_failure(GXOS_MODULE_HANDLE_EX_EXPECTED_FLAGS,
                              image_end + 1U,
                              "address beyond image end rejected");
    expect_unchanged_failure(GXOS_MODULE_HANDLE_EX_EXPECTED_FLAGS,
                              (uintptr_t)0x0000800000000000ULL,
                              "noncanonical address rejected");
    expect_unchanged_failure(GXOS_MODULE_HANDLE_EX_EXPECTED_FLAGS,
                              (uintptr_t)0x100000000ULL,
                              "loader or kernel address rejected");

    output = (GXOS_MODULE_HANDLE_HMODULE)0x1122334455667788ULL;
    lower = (uintptr_t)&output;
    upper = lower + sizeof(output);
    expect(call_checked(GXOS_MODULE_HANDLE_EX_EXPECTED_FLAGS,
                        image_base + 0x37C40U, 0, 0, 0, 1, &report) ==
               GXOS_MODULE_HANDLE_EX_STATUS_NULL_OUTPUT,
           "null output rejected");
    expect(output == (GXOS_MODULE_HANDLE_HMODULE)
                       0x1122334455667788ULL,
           "null output leaves sentinel unrelated");
    expect(report.output_written == 0,
           "null output does not attempt a write");

    expect(call_checked(GXOS_MODULE_HANDLE_EX_EXPECTED_FLAGS,
                        image_base + 0x37C40U,
                        (GXOS_MODULE_HANDLE_HMODULE *)(void *)tiny,
                        (uintptr_t)tiny,
                        (uintptr_t)tiny + sizeof(uintptr_t) - 1U,
                        1, &report) ==
               GXOS_MODULE_HANDLE_EX_STATUS_OUTPUT_NOT_WRITABLE,
           "short output range rejected");

    facts.mapped_image_base = UINTPTR_MAX - 0x10U;
    facts.size_of_image = 0x100U;
    facts.size_of_headers = 0x40U;
    facts.entry_point_rva = 0x10U;
    facts.runtime_entry_point = facts.mapped_image_base + 0x10U;
    output = (GXOS_MODULE_HANDLE_HMODULE)0xCAFEBABECAFEBABEULL;
    lower = (uintptr_t)&output;
    upper = lower + sizeof(output);
    expect(gxos_get_module_handle_ex_checked(
               GXOS_MODULE_HANDLE_EX_EXPECTED_FLAGS, image_base + 0x37C40U,
               &output, &facts, lower, upper, 1, &report) ==
               GXOS_MODULE_HANDLE_EX_STATUS_IMAGE_RANGE_OVERFLOW,
           "overflowing image range rejected");
    expect(output == (GXOS_MODULE_HANDLE_HMODULE)
                       0xCAFEBABECAFEBABEULL,
           "overflowing image range preserves output");
    expect(facts.mapped_image_base == UINTPTR_MAX - 0x10U &&
               facts.size_of_image == 0x100U &&
               facts.entry_point_rva == 0x10U,
           "overflowing image facts remain unchanged");

    output = (GXOS_MODULE_HANDLE_HMODULE)0x9988776655443322ULL;
    lower = (uintptr_t)&output;
    upper = lower + sizeof(output);
    expect(call_checked(GXOS_MODULE_HANDLE_EX_EXPECTED_FLAGS,
                        image_base + 0x37C40U, &output, lower, upper, 0,
                        &report) ==
               GXOS_MODULE_HANDLE_EX_STATUS_IMAGE_NOT_PERMANENT,
           "unproven lifetime rejected");
    expect(output == (GXOS_MODULE_HANDLE_HMODULE)
                       0x9988776655443322ULL,
           "unproven lifetime preserves output");

    facts = valid_facts();
    facts.size_of_image = 0;
    output = (GXOS_MODULE_HANDLE_HMODULE)0x7766554433221100ULL;
    lower = (uintptr_t)&output;
    upper = lower + sizeof(output);
    expect(gxos_get_module_handle_ex_checked(
               GXOS_MODULE_HANDLE_EX_EXPECTED_FLAGS, image_base + 0x37C40U,
               &output, &facts, lower, upper, 1, &report) ==
               GXOS_MODULE_HANDLE_EX_STATUS_INVALID_IMAGE_FACTS,
           "zero-sized image metadata rejected");
    expect(output == (GXOS_MODULE_HANDLE_HMODULE)0x7766554433221100ULL &&
               report.output_written == 0,
           "zero-sized image metadata preserves output");
}

static void test_malformed_regions_preserve_facts(void)
{
    GXOS_MAIN_MODULE_FACTS facts = valid_facts();
    GXOS_MODULE_HANDLE_HMODULE output =
        (GXOS_MODULE_HANDLE_HMODULE)0x0102030405060708ULL;
    GXOS_MODULE_HANDLE_EX_REPORT report;
    uintptr_t lower = (uintptr_t)&output;
    uintptr_t upper = lower + sizeof(output);
    GXOS_MODULE_HANDLE_MEMORY_REGION saved = image_region;

    image_region.end = image_region.base - 1U;
    expect(gxos_get_module_handle_ex_checked(
               GXOS_MODULE_HANDLE_EX_EXPECTED_FLAGS,
               (uintptr_t)image + 0x37C40U, &output, &facts, lower, upper, 1,
               &report) == GXOS_MODULE_HANDLE_EX_STATUS_INVALID_IMAGE_FACTS,
           "malformed region metadata rejected");
    expect(output == (GXOS_MODULE_HANDLE_HMODULE)
                       0x0102030405060708ULL,
           "malformed metadata preserves output");
    image_region = saved;
}

int main(void)
{
    test_positive_rvas_and_idempotence();
    test_image_boundaries_and_address_interpretation();
    test_negative_inputs();
    test_malformed_regions_preserve_facts();
    if (failures != 0) {
        printf("GETMODULEHANDLEEX_HOST_FAILURES=%u\n", failures);
        return 1;
    }
    puts("GETMODULEHANDLEEX_HOST_TESTS_OK");
    return 0;
}

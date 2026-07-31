#include <stdint.h>
#include <stdio.h>
#include "../crt_strcmp.h"

static int sign_of(int value)
{
    return value < 0 ? -1 : value > 0 ? 1 : 0;
}

static int expect_sign(int actual, int expected, const char *name)
{
    if (sign_of(actual) != expected) {
        printf("CRT_STRCMP_TEST_FAILURE=%s actual=%d expected-sign=%d\n",
               name, actual, expected);
        return 1;
    }
    return 0;
}

static int expect_exact(int actual, int expected, const char *name)
{
    if (actual != expected) {
        printf("CRT_STRCMP_TEST_FAILURE=%s actual=%d expected=%d\n",
               name, actual, expected);
        return 1;
    }
    return 0;
}

static int expect_rejected(int observed, int correct_sign, const char *name)
{
    if (sign_of(observed) == correct_sign) {
        printf("CRT_STRCMP_TEST_FAILURE=%s bad-result-was-accepted=%d\n",
               name, observed);
        return 1;
    }
    return 0;
}

static int GXOS_CRT_STRCMP_MS_ABI bad_mutated_comparison(const char *lhs,
                                                         const char *rhs)
{
    return -gxos_crt_strcmp(lhs, rhs);
}

static int GXOS_CRT_STRCMP_MS_ABI bad_signed_byte_comparison(const char *lhs,
                                                             const char *rhs)
{
    while (*lhs != 0 && *lhs == *rhs) {
        lhs++;
        rhs++;
    }
    return (int)(*(const signed char *)lhs) - (int)(*(const signed char *)rhs);
}

static int GXOS_CRT_STRCMP_MS_ABI bad_prefix_comparison(const char *lhs,
                                                        const char *rhs)
{
    while (*lhs != 0 && *lhs == *rhs) {
        lhs++;
        rhs++;
    }
    return *lhs == 0 ? 0 : (*lhs < *rhs ? -1 : 1);
}

static int GXOS_CRT_STRCMP_MS_ABI bad_truncated_comparison(const char *lhs,
                                                           const char *rhs)
{
    uint32_t count = 0;
    while (count != 2 && lhs[count] != 0 && lhs[count] == rhs[count]) count++;
    return count == 2 ? 0 : (lhs[count] < rhs[count] ? -1 : 1);
}

static int GXOS_CRT_STRCMP_MS_ABI bad_forced_equality(const char *lhs,
                                                      const char *rhs)
{
    (void)lhs;
    (void)rhs;
    return 0;
}

int main(void)
{
    static const char empty[] = "";
    static const char one[] = "a";
    static const char one_other[] = "b";
    static const char equal[] = "same";
    static const char equal_copy[] = "same";
    static const char prefix[] = "abc";
    static const char prefix_long[] = "abcd";
    static const char final_a[] = "abca";
    static const char final_b[] = "abcb";
    static const char first_a[] = "apple";
    static const char first_b[] = "bpple";
    static const unsigned char high_low[] = {0x80, 0x00};
    static const unsigned char high_high[] = {0xFF, 0x00};
    static const unsigned char high_mid[] = {0x7F, 0x00};
    static const unsigned char embedded_null_a[] = {'a', 0, 'z', 0};
    static const unsigned char embedded_null_b[] = {'a', 0, 'a', 0};
    static const char long_left[] =
        "012345678901234567890123456789012345678901234567890123456789"
        "012345678901234567890123456789012345678901234567890123456789"
        "012345678901234567890123456789012345678901234567890123456789"
        "012345678901234567890123456789012345678901234567890123456789";
    static const char long_right[] =
        "012345678901234567890123456789012345678901234567890123456789"
        "012345678901234567890123456789012345678901234567890123456789"
        "012345678901234567890123456789012345678901234567890123456789"
        "012345678901234567890123456789012345678901234567890123450";
    int failures = 0;

    failures += expect_exact(gxos_crt_strcmp(equal, equal_copy), 0, "equal-strings");
    failures += expect_sign(gxos_crt_strcmp("left", "right"), -1, "unequal-strings");
    failures += expect_exact(gxos_crt_strcmp(empty, empty), 0, "empty-equal");
    failures += expect_sign(gxos_crt_strcmp(empty, one), -1, "empty-prefix");
    failures += expect_sign(gxos_crt_strcmp(prefix_long, prefix), 1, "reverse-prefix");
    failures += expect_sign(gxos_crt_strcmp(final_a, final_b), -1, "differing-final-byte");
    failures += expect_sign(gxos_crt_strcmp(first_a, first_b), -1, "differing-first-byte");
    failures += expect_sign(gxos_crt_strcmp((const char *)high_low,
                                             (const char *)high_mid), 1,
                           "embedded-high-bit-byte");
    failures += expect_sign(gxos_crt_strcmp((const char *)high_high,
                                             (const char *)high_low), 1,
                           "high-bit-ordering");
    failures += expect_sign(gxos_crt_strcmp(long_left, long_right), 1, "long-strings");
    failures += expect_exact(gxos_crt_strcmp(equal, equal), 0, "identical-pointers");
    failures += expect_sign(gxos_crt_strcmp(one, one_other), -1, "one-character");
    failures += expect_exact(gxos_crt_strcmp((const char *)embedded_null_a,
                                              (const char *)embedded_null_b),
                             0, "terminating-null-correctness");

    {
        int control;
        control = expect_rejected(bad_mutated_comparison("a", "b"), -1,
                                  "negative-mutated-comparison-rejected");
        failures += control;
        printf("CRT_STRCMP_NEGATIVE_MUTATED=%s\n", control == 0 ? "PASS" : "FAIL");
        control = expect_rejected(bad_signed_byte_comparison((const char *)high_low,
                                                               (const char *)high_mid), 1,
                                  "negative-signed-byte-bug-detected");
        failures += control;
        printf("CRT_STRCMP_NEGATIVE_SIGNED_BYTE=%s\n", control == 0 ? "PASS" : "FAIL");
        control = expect_rejected(bad_prefix_comparison(prefix, prefix_long), -1,
                                  "negative-prefix-bug-detected");
        failures += control;
        printf("CRT_STRCMP_NEGATIVE_PREFIX=%s\n", control == 0 ? "PASS" : "FAIL");
        control = expect_rejected(bad_truncated_comparison("abX", "abY"), -1,
                                  "negative-truncated-comparison-detected");
        failures += control;
        printf("CRT_STRCMP_NEGATIVE_TRUNCATED=%s\n", control == 0 ? "PASS" : "FAIL");
        control = expect_rejected(bad_forced_equality("a", "b"), -1,
                                  "negative-forced-equality-detected");
        failures += control;
        printf("CRT_STRCMP_NEGATIVE_FORCED_EQUALITY=%s\n", control == 0 ? "PASS" : "FAIL");
    }

    printf("CRT_STRCMP_TEST_EQUAL=%s\n", sign_of(gxos_crt_strcmp(equal, equal_copy)) == 0 ? "PASS" : "FAIL");
    printf("CRT_STRCMP_TEST_PREFIX=%s\n", sign_of(gxos_crt_strcmp(prefix, prefix_long)) < 0 ? "PASS" : "FAIL");
    printf("CRT_STRCMP_TEST_HIGH_BIT=%s\n", sign_of(gxos_crt_strcmp((const char *)high_low,
                                                                      (const char *)high_mid)) > 0 ? "PASS" : "FAIL");
    printf("CRT_STRCMP_TEST_LONG=%s\n", sign_of(gxos_crt_strcmp(long_left, long_right)) > 0 ? "PASS" : "FAIL");
    printf("CRT_STRCMP_HOST_TESTS=%s\n", failures == 0 ? "PASSED" : "FAILED");
    return failures == 0 ? 0 : 1;
}

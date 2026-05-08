#include <gtest/gtest.h>

#include "tests/seeds/expected_values.h"

TEST(Smoke, BuildLinks) { EXPECT_EQ(1, 1); }

// Proves tests/seeds/expected_values.h parses cleanly and is on the include path.
TEST(Smoke, SeedsHeaderIncludes) {
    EXPECT_EQ(sts2::tests::seeds::kCalcifiedHp_seed42,
              sts2::tests::seeds::kCalcifiedHp_seed42);
}

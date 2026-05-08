// Smoke test for src/render/console.{h,cc}.
// Spec: docs/test-plan/02-test-specifications.md §12.4 (T-CON-005).
//
// console::enable_ansi_and_utf8 calls Win32 APIs and returns void.
// Per §14.3 U-3, the inner branches (handle valid + GetConsoleMode
// success) are not directly testable without mocking the Win32 surface,
// so the planned coverage is a single smoke test: the call must not
// throw or crash, and subsequent stdout writes must succeed.

#include <iostream>
#include <sstream>

#include <gtest/gtest.h>

#include "sts2/render/console.h"

namespace {

// T-CON-005 — Smoke — enable_ansi_and_utf8 does not throw; cout still works.
TEST(ConsoleSmoke, T_CON_005_EnableAnsiAndUtf8DoesNotCrash) {
    EXPECT_NO_THROW(sts2::console::enable_ansi_and_utf8());

    // Verify subsequent std::cout writes still operate. We swap rdbuf to a
    // local stringstream so the test does not pollute test runner output.
    std::ostringstream captured;
    auto* const old_buf = std::cout.rdbuf(captured.rdbuf());
    std::cout << "x";
    std::cout.rdbuf(old_buf);

    EXPECT_TRUE(std::cout.good());
    EXPECT_EQ(captured.str(), "x");
}

}

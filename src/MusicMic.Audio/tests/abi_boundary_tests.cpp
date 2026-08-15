#include "abi_boundary.h"
#include "test_support.h"

#include <stdexcept>

MM_TEST(ABI_boundary_converts_all_Cpp_exceptions_to_internal_error) {
    bool failure_recorded = false;
    const MM_Result result = musicmic::InvokeAbiBoundary(
        []() -> MM_Result { throw std::runtime_error("test exception"); },
        [&failure_recorded]() noexcept { failure_recorded = true; });

    MM_REQUIRE(result == MM_RESULT_INTERNAL_ERROR);
    MM_REQUIRE(failure_recorded);
}

MM_TEST(ABI_boundary_does_not_allow_a_throwing_failure_recorder_to_escape) {
    const MM_Result result = musicmic::InvokeAbiBoundary(
        []() -> MM_Result { throw 42; },
        []() { throw std::runtime_error("secondary exception"); });

    MM_REQUIRE(result == MM_RESULT_INTERNAL_ERROR);
}

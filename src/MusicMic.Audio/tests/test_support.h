#pragma once

#include <cmath>
#include <sstream>
#include <stdexcept>
#include <string>
#include <string_view>

namespace musicmic::test {

using TestFunction = void (*)();

void Register(std::string_view name, TestFunction function);

class Registration final {
public:
    Registration(std::string_view name, TestFunction function) {
        Register(name, function);
    }
};

inline void Require(bool condition, std::string_view expression, std::string_view file, int line) {
    if (condition) {
        return;
    }

    std::ostringstream message;
    message << file << ':' << line << ": requirement failed: " << expression;
    throw std::runtime_error(message.str());
}

inline void RequireNear(float actual, float expected, float tolerance, std::string_view file, int line) {
    if (std::fabs(actual - expected) <= tolerance) {
        return;
    }

    std::ostringstream message;
    message << file << ':' << line << ": expected " << expected << ", got " << actual;
    throw std::runtime_error(message.str());
}

}  // namespace musicmic::test

#define MM_TEST(name)                                                                            \
    static void name();                                                                          \
    static const ::musicmic::test::Registration name##_registration{#name, &name};               \
    static void name()

#define MM_REQUIRE(expression)                                                                   \
    ::musicmic::test::Require(static_cast<bool>(expression), #expression, __FILE__, __LINE__)

#define MM_REQUIRE_NEAR(actual, expected, tolerance)                                              \
    ::musicmic::test::RequireNear((actual), (expected), (tolerance), __FILE__, __LINE__)

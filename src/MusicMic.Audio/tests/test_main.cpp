#include "test_support.h"

#include <exception>
#include <iostream>
#include <string>
#include <utility>
#include <vector>

namespace musicmic::test {
namespace {

std::vector<std::pair<std::string, TestFunction>>& Tests() {
    static std::vector<std::pair<std::string, TestFunction>> tests;
    return tests;
}

}  // namespace

void Register(std::string_view name, TestFunction function) {
    Tests().emplace_back(name, function);
}

}  // namespace musicmic::test

int main() {
    int failures = 0;
    for (const auto& [name, function] : musicmic::test::Tests()) {
        try {
            function();
            std::cout << "PASS " << name << '\n';
        } catch (const std::exception& error) {
            ++failures;
            std::cerr << "FAIL " << name << ": " << error.what() << '\n';
        }
    }

    if (failures != 0) {
        std::cerr << failures << " test(s) failed\n";
        return 1;
    }

    std::cout << musicmic::test::Tests().size() << " test(s) passed\n";
    return 0;
}

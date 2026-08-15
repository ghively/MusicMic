#pragma once

#include "musicmic_audio.h"

#include <utility>

namespace musicmic {

// No C++ exception may cross the C ABI. The recorder is itself isolated because
// error reporting can allocate or lock while the process is already under stress.
template <typename Operation, typename FailureRecorder>
[[nodiscard]] MM_Result InvokeAbiBoundary(
    Operation&& operation,
    FailureRecorder&& failure_recorder) noexcept {
    try {
        return std::forward<Operation>(operation)();
    } catch (...) {
        try {
            std::forward<FailureRecorder>(failure_recorder)();
        } catch (...) {
        }
        return MM_RESULT_INTERNAL_ERROR;
    }
}

}  // namespace musicmic

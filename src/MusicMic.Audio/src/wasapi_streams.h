#pragma once

#include "musicmic_audio.h"

#include <windows.h>
#include <mmreg.h>

#include <cstdint>
#include <memory>
#include <string>

namespace musicmic {

enum class StreamFailure {
    Source,
    Microphone,
    Output,
    Internal,
};

[[nodiscard]] WAVEFORMATEXTENSIBLE BuildBusWaveFormat() noexcept;
[[nodiscard]] MM_Result StreamFailureResult(StreamFailure failure) noexcept;
[[nodiscard]] bool ShouldRestartSource(
    bool injection_requested,
    bool source_healthy,
    std::uint32_t discovered_process_id) noexcept;
[[nodiscard]] bool StreamStartupSucceeded(
    bool running,
    bool source_healthy,
    bool microphone_healthy,
    bool output_healthy) noexcept;
[[nodiscard]] std::wstring FormatHResult(HRESULT result);

struct WasapiStreamSnapshot final {
    bool running{};
    bool source_healthy{};
    bool microphone_healthy{};
    bool output_healthy{};
    float source_peak{};
    float microphone_peak{};
    float output_peak{};
    StreamFailure failure{StreamFailure::Internal};
    HRESULT failure_result{S_OK};
    std::wstring error_message;
};

class WasapiStreams final {
public:
    WasapiStreams();
    ~WasapiStreams();

    WasapiStreams(const WasapiStreams&) = delete;
    WasapiStreams& operator=(const WasapiStreams&) = delete;

    [[nodiscard]] MM_Result Start(
        std::uint32_t process_id,
        std::wstring microphone_endpoint_id,
        std::wstring output_endpoint_id,
        float source_gain,
        float microphone_gain,
        std::wstring& error_message);
    [[nodiscard]] MM_Result RestartSource(
        std::uint32_t process_id,
        std::wstring& error_message);
    void StopSource() noexcept;
    void Stop() noexcept;
    void SetGains(float source_gain, float microphone_gain) noexcept;

    [[nodiscard]] WasapiStreamSnapshot Snapshot() const;

private:
    class Impl;
    std::unique_ptr<Impl> impl_;
};

}  // namespace musicmic

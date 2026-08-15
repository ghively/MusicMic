#pragma once

#include <cstddef>
#include <chrono>
#include <span>
#include <vector>

namespace musicmic {

inline constexpr std::size_t kBusChannels = 2;
inline constexpr std::size_t kBusSampleRate = 48000;

struct WorkerRecoveryDecision final {
    bool keep_rendering{};
    bool retry_source{};
    bool retry_microphone{};
    bool stop_injection{};
};

[[nodiscard]] WorkerRecoveryDecision DecideWorkerRecovery(
    bool source_healthy,
    bool microphone_healthy,
    bool output_healthy) noexcept;

[[nodiscard]] float PeakMagnitude(std::span<const float> samples) noexcept;

[[nodiscard]] std::chrono::milliseconds RetryDelay(std::size_t attempt) noexcept;

class StereoFrameQueue final {
public:
    explicit StereoFrameQueue(std::size_t capacity_frames);

    void Push(std::span<const float> interleaved_stereo) noexcept;
    [[nodiscard]] std::size_t Pop(std::span<float> interleaved_stereo) noexcept;
    void Clear() noexcept;

    [[nodiscard]] std::size_t CapacityFrames() const noexcept { return capacity_frames_; }
    [[nodiscard]] std::size_t SizeFrames() const noexcept { return size_frames_; }

private:
    void PushFrame(float left, float right) noexcept;

    std::vector<float> samples_;
    std::size_t capacity_frames_{};
    std::size_t read_frame_{};
    std::size_t size_frames_{};
};

}  // namespace musicmic

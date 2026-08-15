#include "stream_worker.h"

#include <algorithm>
#include <array>
#include <cmath>

namespace musicmic {

WorkerRecoveryDecision DecideWorkerRecovery(
    bool source_healthy,
    bool microphone_healthy,
    bool output_healthy) noexcept {
    if (!output_healthy) {
        return WorkerRecoveryDecision{false, false, false, true};
    }
    return WorkerRecoveryDecision{
        true,
        !source_healthy,
        !microphone_healthy,
        false};
}

float PeakMagnitude(std::span<const float> samples) noexcept {
    float peak = 0.0F;
    for (const float sample : samples) {
        peak = std::max(peak, std::fabs(sample));
    }
    return std::min(peak, 1.0F);
}

std::chrono::milliseconds RetryDelay(std::size_t attempt) noexcept {
    using namespace std::chrono_literals;
    constexpr std::array delays{250ms, 500ms, 1000ms, 2000ms, 5000ms};
    return delays[std::min(attempt, delays.size() - 1)];
}

StereoFrameQueue::StereoFrameQueue(std::size_t capacity_frames)
    : samples_(capacity_frames * kBusChannels), capacity_frames_(capacity_frames) {}

void StereoFrameQueue::Push(std::span<const float> interleaved_stereo) noexcept {
    const std::size_t frame_count = interleaved_stereo.size() / kBusChannels;
    const std::size_t first_frame = frame_count > capacity_frames_
        ? frame_count - capacity_frames_
        : 0;
    for (std::size_t frame = first_frame; frame < frame_count; ++frame) {
        PushFrame(
            interleaved_stereo[frame * kBusChannels],
            interleaved_stereo[frame * kBusChannels + 1]);
    }
}

std::size_t StereoFrameQueue::Pop(std::span<float> interleaved_stereo) noexcept {
    std::fill(interleaved_stereo.begin(), interleaved_stereo.end(), 0.0F);
    const std::size_t requested_frames = interleaved_stereo.size() / kBusChannels;
    const std::size_t frames = std::min(requested_frames, size_frames_);
    for (std::size_t frame = 0; frame < frames; ++frame) {
        const std::size_t queue_sample = read_frame_ * kBusChannels;
        interleaved_stereo[frame * kBusChannels] = samples_[queue_sample];
        interleaved_stereo[frame * kBusChannels + 1] = samples_[queue_sample + 1];
        read_frame_ = capacity_frames_ == 0 ? 0 : (read_frame_ + 1) % capacity_frames_;
    }
    size_frames_ -= frames;
    return frames;
}

void StereoFrameQueue::Clear() noexcept {
    read_frame_ = 0;
    size_frames_ = 0;
    std::fill(samples_.begin(), samples_.end(), 0.0F);
}

void StereoFrameQueue::PushFrame(float left, float right) noexcept {
    if (capacity_frames_ == 0) {
        return;
    }
    if (size_frames_ == capacity_frames_) {
        read_frame_ = (read_frame_ + 1) % capacity_frames_;
        --size_frames_;
    }
    const std::size_t write_frame = (read_frame_ + size_frames_) % capacity_frames_;
    samples_[write_frame * kBusChannels] = left;
    samples_[write_frame * kBusChannels + 1] = right;
    ++size_frames_;
}

}  // namespace musicmic

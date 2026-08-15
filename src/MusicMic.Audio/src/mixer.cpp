#include "mixer.h"

#include <algorithm>

namespace musicmic {

void MixStereo(
    std::span<const float> source,
    std::span<const float> microphone,
    float source_gain,
    float microphone_gain,
    std::span<float> output) noexcept {
    for (std::size_t index = 0; index < output.size(); ++index) {
        const float source_sample = index < source.size() ? source[index] : 0.0F;
        const float microphone_sample = index < microphone.size() ? microphone[index] : 0.0F;
        output[index] = std::clamp(
            source_sample * source_gain + microphone_sample * microphone_gain,
            -1.0F,
            1.0F);
    }
}

void NormalizeMonoToStereo(
    std::span<const float> mono,
    std::span<float> stereo) noexcept {
    std::fill(stereo.begin(), stereo.end(), 0.0F);
    const std::size_t frames = std::min(mono.size(), stereo.size() / 2U);
    for (std::size_t frame = 0; frame < frames; ++frame) {
        stereo[frame * 2U] = mono[frame];
        stereo[frame * 2U + 1U] = mono[frame];
    }
}

}  // namespace musicmic

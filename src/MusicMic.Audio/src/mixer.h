#pragma once

#include <span>

namespace musicmic {

// Mixes interleaved stereo float samples. A missing/short input contributes
// silence, and every output sample is bounded to the normalized [-1, 1] range.
void MixStereo(
    std::span<const float> source,
    std::span<const float> microphone,
    float source_gain,
    float microphone_gain,
    std::span<float> output) noexcept;

// Duplicates mono samples into interleaved left/right stereo samples.
// Unfilled output samples are cleared to silence.
void NormalizeMonoToStereo(
    std::span<const float> mono,
    std::span<float> stereo) noexcept;

}  // namespace musicmic

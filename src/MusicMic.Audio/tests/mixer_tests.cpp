#include "mixer.h"
#include "test_support.h"

#include <array>

using musicmic::MixStereo;
using musicmic::NormalizeMonoToStereo;

MM_TEST(MixStereo_clamps_positive_and_negative_overflow) {
    const std::array<float, 4> source{0.75F, -0.75F, 0.25F, -0.25F};
    const std::array<float, 4> microphone{0.75F, -0.75F, -0.25F, 0.25F};
    std::array<float, 4> output{};

    MixStereo(source, microphone, 1.0F, 1.0F, output);

    MM_REQUIRE_NEAR(output[0], 1.0F, 0.0001F);
    MM_REQUIRE_NEAR(output[1], -1.0F, 0.0001F);
    MM_REQUIRE_NEAR(output[2], 0.0F, 0.0001F);
    MM_REQUIRE_NEAR(output[3], 0.0F, 0.0001F);
}

MM_TEST(NormalizeMonoToStereo_duplicates_each_mono_sample) {
    const std::array<float, 3> mono{0.25F, -0.5F, 1.0F};
    std::array<float, 6> stereo{};

    NormalizeMonoToStereo(mono, stereo);

    const std::array<float, 6> expected{0.25F, 0.25F, -0.5F, -0.5F, 1.0F, 1.0F};
    MM_REQUIRE(stereo == expected);
}

MM_TEST(MixStereo_preserves_source_only_with_source_gain) {
    const std::array<float, 4> source{0.8F, -0.8F, 0.4F, -0.4F};
    const std::array<float, 4> silence{};
    std::array<float, 4> output{};

    MixStereo(source, silence, 0.5F, 1.0F, output);

    const std::array<float, 4> expected{0.4F, -0.4F, 0.2F, -0.2F};
    MM_REQUIRE(output == expected);
}

MM_TEST(MixStereo_preserves_microphone_only_with_microphone_gain) {
    const std::array<float, 4> silence{};
    const std::array<float, 4> microphone{0.8F, -0.8F, 0.4F, -0.4F};
    std::array<float, 4> output{};

    MixStereo(silence, microphone, 1.0F, 0.25F, output);

    const std::array<float, 4> expected{0.2F, -0.2F, 0.1F, -0.1F};
    MM_REQUIRE(output == expected);
}

MM_TEST(MixStereo_treats_a_short_or_missing_input_as_silence) {
    const std::array<float, 2> source{0.2F, 0.4F};
    const std::array<float, 4> microphone{0.1F, 0.1F, 0.3F, 0.3F};
    std::array<float, 4> output{};

    MixStereo(source, microphone, 1.0F, 1.0F, output);

    const std::array<float, 4> expected{0.3F, 0.5F, 0.3F, 0.3F};
    for (std::size_t index = 0; index < output.size(); ++index) {
        MM_REQUIRE_NEAR(output[index], expected[index], 0.0001F);
    }
}

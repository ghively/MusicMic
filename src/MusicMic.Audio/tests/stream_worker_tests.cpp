#include "stream_worker.h"
#include "test_support.h"

#include <array>
#include <chrono>

using musicmic::DecideWorkerRecovery;
using musicmic::EffectiveStreamHealth;
using musicmic::PeakMagnitude;
using musicmic::RetryDelay;
using musicmic::StereoFrameQueue;

MM_TEST(Stream_queue_is_bounded_and_keeps_the_newest_audio_on_overflow) {
    StereoFrameQueue queue(3);
    const std::array<float, 8> input{
        0.1F, 0.2F,
        0.3F, 0.4F,
        0.5F, 0.6F,
        0.7F, 0.8F};

    queue.Push(input);

    std::array<float, 6> output{};
    MM_REQUIRE(queue.Pop(output) == 3);
    const std::array<float, 6> expected{0.3F, 0.4F, 0.5F, 0.6F, 0.7F, 0.8F};
    MM_REQUIRE(output == expected);
    MM_REQUIRE(queue.SizeFrames() == 0);
}

MM_TEST(Stream_queue_returns_only_complete_stereo_frames) {
    StereoFrameQueue queue(2);
    const std::array<float, 3> incomplete{0.1F, 0.2F, 0.9F};
    queue.Push(incomplete);

    std::array<float, 4> output{9.0F, 9.0F, 9.0F, 9.0F};
    MM_REQUIRE(queue.Pop(output) == 1);
    MM_REQUIRE_NEAR(output[0], 0.1F, 0.0001F);
    MM_REQUIRE_NEAR(output[1], 0.2F, 0.0001F);
    MM_REQUIRE_NEAR(output[2], 0.0F, 0.0001F);
    MM_REQUIRE_NEAR(output[3], 0.0F, 0.0001F);
}

MM_TEST(Worker_continues_with_silence_and_retries_an_individual_lost_input) {
    const auto source_lost = DecideWorkerRecovery(false, true, true);
    MM_REQUIRE(source_lost.keep_rendering);
    MM_REQUIRE(source_lost.retry_source);
    MM_REQUIRE(!source_lost.retry_microphone);
    MM_REQUIRE(!source_lost.stop_injection);

    const auto microphone_lost = DecideWorkerRecovery(true, false, true);
    MM_REQUIRE(microphone_lost.keep_rendering);
    MM_REQUIRE(!microphone_lost.retry_source);
    MM_REQUIRE(microphone_lost.retry_microphone);
    MM_REQUIRE(!microphone_lost.stop_injection);
}

MM_TEST(Worker_stops_immediately_when_the_VB_CABLE_output_is_lost) {
    const auto decision = DecideWorkerRecovery(true, true, false);
    MM_REQUIRE(!decision.keep_rendering);
    MM_REQUIRE(!decision.retry_source);
    MM_REQUIRE(!decision.retry_microphone);
    MM_REQUIRE(decision.stop_injection);
}

MM_TEST(Peak_meter_reports_absolute_bounded_sample_peak) {
    const std::array<float, 5> samples{-1.4F, -0.8F, 0.25F, 1.2F, 0.0F};
    MM_REQUIRE_NEAR(PeakMagnitude(samples), 1.0F, 0.0001F);
    MM_REQUIRE_NEAR(PeakMagnitude({}), 0.0F, 0.0001F);
}

MM_TEST(Stream_retry_delay_uses_the_required_bounded_recovery_schedule) {
    using namespace std::chrono_literals;
    const std::array<std::chrono::milliseconds, 6> expected{
        250ms, 500ms, 1000ms, 2000ms, 5000ms, 5000ms};
    for (std::size_t attempt = 0; attempt < expected.size(); ++attempt) {
        MM_REQUIRE(RetryDelay(attempt) == expected[attempt]);
    }
}

MM_TEST(Stream_health_never_overrides_an_ambiguous_or_missing_discovery_identity) {
    MM_REQUIRE(EffectiveStreamHealth(true, true));
    MM_REQUIRE(!EffectiveStreamHealth(false, true));
    MM_REQUIRE(!EffectiveStreamHealth(true, false));
}

#include "wasapi_streams.h"
#include "test_support.h"

#include <audioclient.h>
#include <mmreg.h>

using musicmic::BuildBusWaveFormat;
using musicmic::StreamFailure;
using musicmic::StreamFailureResult;
using musicmic::ShouldRestartSource;
using musicmic::StreamStartupSucceeded;

MM_TEST(WASAPI_bus_format_is_48khz_float_stereo) {
    const WAVEFORMATEXTENSIBLE format = BuildBusWaveFormat();

    MM_REQUIRE(format.Format.wFormatTag == WAVE_FORMAT_EXTENSIBLE);
    MM_REQUIRE(format.Format.nChannels == 2);
    MM_REQUIRE(format.Format.nSamplesPerSec == 48000);
    MM_REQUIRE(format.Format.wBitsPerSample == 32);
    MM_REQUIRE(format.Format.nBlockAlign == 8);
    MM_REQUIRE(format.Format.nAvgBytesPerSec == 384000);
    MM_REQUIRE(format.Format.cbSize == 22);
    MM_REQUIRE(format.Samples.wValidBitsPerSample == 32);
    MM_REQUIRE(format.dwChannelMask == (SPEAKER_FRONT_LEFT | SPEAKER_FRONT_RIGHT));
    MM_REQUIRE(format.SubFormat == KSDATAFORMAT_SUBTYPE_IEEE_FLOAT);
}

MM_TEST(WASAPI_stream_failures_map_to_the_owning_input_or_output) {
    MM_REQUIRE(StreamFailureResult(StreamFailure::Source) == MM_RESULT_SOURCE_UNAVAILABLE);
    MM_REQUIRE(StreamFailureResult(StreamFailure::Microphone) == MM_RESULT_MICROPHONE_UNAVAILABLE);
    MM_REQUIRE(StreamFailureResult(StreamFailure::Output) == MM_RESULT_OUTPUT_UNAVAILABLE);
    MM_REQUIRE(StreamFailureResult(StreamFailure::Internal) == MM_RESULT_AUDIO_FAILURE);
}

MM_TEST(WASAPI_only_restarts_source_after_stable_discovery_finds_a_current_process) {
    MM_REQUIRE(ShouldRestartSource(true, false, 2400));
    MM_REQUIRE(ShouldRestartSource(true, false, 1200));
    MM_REQUIRE(!ShouldRestartSource(false, false, 2400));
    MM_REQUIRE(!ShouldRestartSource(true, true, 2400));
    MM_REQUIRE(!ShouldRestartSource(true, false, 0));
}

MM_TEST(WASAPI_startup_does_not_claim_running_after_the_render_worker_has_already_exited) {
    MM_REQUIRE(StreamStartupSucceeded(true, true, true, true));
    MM_REQUIRE(!StreamStartupSucceeded(false, true, true, true));
    MM_REQUIRE(!StreamStartupSucceeded(true, true, true, false));
}

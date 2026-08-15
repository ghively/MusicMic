#include "musicmic_audio.h"
#include "test_support.h"

#include <array>
#include <limits>

MM_TEST(Abi_rejects_calls_before_initialization_and_shutdown_is_idempotent) {
    MM_Shutdown();
    MM_REQUIRE(MM_RefreshDevices() == MM_RESULT_NOT_INITIALIZED);
    MM_REQUIRE(MM_Shutdown() == MM_RESULT_OK);
}

MM_TEST(Abi_validates_output_pointers_before_accessing_them) {
    MM_Shutdown();
    MM_REQUIRE(MM_GetStatus(nullptr) == MM_RESULT_INVALID_ARGUMENT);
}

MM_TEST(Abi_initialize_is_idempotent_and_never_claims_live_injection_in_discovery_slice) {
    MM_Shutdown();
    MM_REQUIRE(MM_Initialize() == MM_RESULT_OK);
    MM_REQUIRE(MM_Initialize() == MM_RESULT_OK);

    MM_Status status{};
    MM_REQUIRE(MM_GetStatus(&status) == MM_RESULT_OK);
    MM_REQUIRE(status.injection_requested == 0);

    uint32_t source_count = 0;
    uint32_t microphone_count = 0;
    MM_REQUIRE(MM_GetSourceCount(&source_count) == MM_RESULT_OK);
    MM_REQUIRE(MM_GetMicrophoneCount(&microphone_count) == MM_RESULT_OK);
    MM_SourceInfo source_info{};
    MM_MicrophoneInfo microphone_info{};
    MM_REQUIRE(MM_GetSourceInfo(source_count, &source_info) == MM_RESULT_NOT_FOUND);
    MM_REQUIRE(MM_GetMicrophoneInfo(microphone_count, &microphone_info) == MM_RESULT_NOT_FOUND);
    MM_REQUIRE(MM_StartInjection() != MM_RESULT_OK);
    MM_REQUIRE(MM_StopInjection() == MM_RESULT_OK);
    MM_REQUIRE(MM_Shutdown() == MM_RESULT_OK);
}

MM_TEST(Abi_last_error_reports_required_utf16_buffer_size) {
    MM_Shutdown();
    uint32_t required = 0;
    MM_REQUIRE(MM_GetLastError(nullptr, 0, &required) == MM_RESULT_BUFFER_TOO_SMALL);
    MM_REQUIRE(required >= 1);
    std::array<wchar_t, 256> buffer{};
    MM_REQUIRE(MM_GetLastError(buffer.data(), static_cast<uint32_t>(buffer.size()), &required) == MM_RESULT_OK);
    MM_REQUIRE(buffer[required - 1] == L'\0');
}

MM_TEST(Abi_discovery_getters_validate_arguments_and_indices) {
    MM_Shutdown();
    MM_REQUIRE(MM_GetSourceCount(nullptr) == MM_RESULT_INVALID_ARGUMENT);
    MM_REQUIRE(MM_GetSourceInfo(0, nullptr) == MM_RESULT_INVALID_ARGUMENT);
    MM_REQUIRE(MM_GetMicrophoneCount(nullptr) == MM_RESULT_INVALID_ARGUMENT);
    MM_REQUIRE(MM_GetMicrophoneInfo(0, nullptr) == MM_RESULT_INVALID_ARGUMENT);

    uint32_t count = 0;
    MM_REQUIRE(MM_GetSourceCount(&count) == MM_RESULT_NOT_INITIALIZED);
    MM_REQUIRE(MM_GetMicrophoneCount(&count) == MM_RESULT_NOT_INITIALIZED);
}

MM_TEST(Abi_selection_and_gain_setters_reject_invalid_values) {
    MM_Shutdown();
    MM_REQUIRE(MM_SelectSource(nullptr) == MM_RESULT_INVALID_ARGUMENT);
    MM_REQUIRE(MM_SelectMicrophone(nullptr) == MM_RESULT_INVALID_ARGUMENT);
    MM_REQUIRE(MM_SetSourceGain(-0.01F) == MM_RESULT_INVALID_ARGUMENT);
    MM_REQUIRE(MM_SetSourceGain(1.01F) == MM_RESULT_INVALID_ARGUMENT);
    MM_REQUIRE(MM_SetMicrophoneGain(std::numeric_limits<float>::quiet_NaN()) == MM_RESULT_INVALID_ARGUMENT);
}

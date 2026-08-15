#include "musicmic_audio.h"
#include "test_support.h"

#include <array>

MM_TEST(Abi_rejects_calls_before_initialization_and_shutdown_is_idempotent) {
    MM_Shutdown();
    MM_REQUIRE(MM_RefreshDevices() == MM_RESULT_NOT_INITIALIZED);
    MM_REQUIRE(MM_Shutdown() == MM_RESULT_OK);
}

MM_TEST(Abi_validates_output_pointers_before_accessing_them) {
    MM_Shutdown();
    MM_REQUIRE(MM_GetStatus(nullptr) == MM_RESULT_INVALID_ARGUMENT);
}

MM_TEST(Abi_initialize_is_idempotent_and_reports_output_unavailable) {
    MM_Shutdown();
    MM_REQUIRE(MM_Initialize() == MM_RESULT_OK);
    MM_REQUIRE(MM_Initialize() == MM_RESULT_OK);

    MM_Status status{};
    MM_REQUIRE(MM_GetStatus(&status) == MM_RESULT_OK);
    MM_REQUIRE(status.state == MM_STATE_OUTPUT_UNAVAILABLE);
    MM_REQUIRE(status.injection_requested == 0);
    MM_REQUIRE(MM_StartInjection() == MM_RESULT_OUTPUT_UNAVAILABLE);
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

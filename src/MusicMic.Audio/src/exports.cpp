#include "musicmic_audio.h"

#include "engine_state.h"

#include <algorithm>
#include <mutex>
#include <string>

namespace {

std::mutex engine_mutex;
bool initialized = false;
musicmic::EngineStateMachine engine_state;
std::wstring last_error;

MM_State ToAbiState(musicmic::EngineState state) noexcept {
    switch (state) {
    case musicmic::EngineState::Initializing: return MM_STATE_INITIALIZING;
    case musicmic::EngineState::Ready: return MM_STATE_READY;
    case musicmic::EngineState::Injecting: return MM_STATE_INJECTING;
    case musicmic::EngineState::SourceUnavailable: return MM_STATE_SOURCE_UNAVAILABLE;
    case musicmic::EngineState::MicrophoneUnavailable: return MM_STATE_MICROPHONE_UNAVAILABLE;
    case musicmic::EngineState::OutputUnavailable: return MM_STATE_OUTPUT_UNAVAILABLE;
    case musicmic::EngineState::Error: return MM_STATE_ERROR;
    }
    return MM_STATE_ERROR;
}

MM_Result RequireInitialized() {
    if (initialized) {
        return MM_RESULT_OK;
    }
    last_error = L"MusicMic audio engine is not initialized.";
    return MM_RESULT_NOT_INITIALIZED;
}

}  // namespace

MM_Result MM_CALL MM_Initialize(void) {
    std::scoped_lock lock(engine_mutex);
    if (!initialized) {
        engine_state = musicmic::EngineStateMachine{};
        engine_state.Apply(musicmic::EngineEvent::Initialized);
        initialized = true;
    }
    last_error.clear();
    return MM_RESULT_OK;
}

MM_Result MM_CALL MM_Shutdown(void) {
    std::scoped_lock lock(engine_mutex);
    initialized = false;
    engine_state = musicmic::EngineStateMachine{};
    last_error.clear();
    return MM_RESULT_OK;
}

MM_Result MM_CALL MM_RefreshDevices(void) {
    std::scoped_lock lock(engine_mutex);
    const MM_Result result = RequireInitialized();
    if (result != MM_RESULT_OK) {
        return result;
    }
    last_error = L"Core Audio device discovery is not available in this native foundation build.";
    return MM_RESULT_OUTPUT_UNAVAILABLE;
}

MM_Result MM_CALL MM_StartInjection(void) {
    std::scoped_lock lock(engine_mutex);
    const MM_Result result = RequireInitialized();
    if (result != MM_RESULT_OK) {
        return result;
    }
    engine_state.Apply(musicmic::EngineEvent::StartRequested);
    if (engine_state.State() != musicmic::EngineState::Injecting) {
        last_error = L"VB-CABLE output is unavailable; injection was not started.";
        return MM_RESULT_OUTPUT_UNAVAILABLE;
    }
    last_error.clear();
    return MM_RESULT_OK;
}

MM_Result MM_CALL MM_StopInjection(void) {
    std::scoped_lock lock(engine_mutex);
    const MM_Result result = RequireInitialized();
    if (result != MM_RESULT_OK) {
        return result;
    }
    engine_state.Apply(musicmic::EngineEvent::StopRequested);
    last_error.clear();
    return MM_RESULT_OK;
}

MM_Result MM_CALL MM_GetStatus(MM_Status* status) {
    if (status == nullptr) {
        return MM_RESULT_INVALID_ARGUMENT;
    }
    std::scoped_lock lock(engine_mutex);
    const MM_Result result = RequireInitialized();
    if (result != MM_RESULT_OK) {
        return result;
    }
    *status = MM_Status{
        ToAbiState(engine_state.State()),
        engine_state.SourceAvailable() ? 1 : 0,
        engine_state.MicrophoneAvailable() ? 1 : 0,
        engine_state.OutputAvailable() ? 1 : 0,
        engine_state.InjectionRequested() ? 1 : 0,
        0.0F,
        0.0F};
    return MM_RESULT_OK;
}

MM_Result MM_CALL MM_GetLastError(
    wchar_t* buffer,
    uint32_t buffer_length,
    uint32_t* required_length) {
    if (required_length == nullptr) {
        return MM_RESULT_INVALID_ARGUMENT;
    }
    std::scoped_lock lock(engine_mutex);
    const auto needed = static_cast<uint32_t>(last_error.size() + 1U);
    *required_length = needed;
    if (buffer == nullptr || buffer_length < needed) {
        return MM_RESULT_BUFFER_TOO_SMALL;
    }
    std::copy(last_error.begin(), last_error.end(), buffer);
    buffer[last_error.size()] = L'\0';
    return MM_RESULT_OK;
}

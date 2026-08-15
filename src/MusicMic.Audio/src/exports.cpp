#include "musicmic_audio.h"

#include "core_audio_discovery.h"
#include "engine_state.h"

#include <algorithm>
#include <cmath>
#include <mutex>
#include <string>
#include <string_view>

namespace {

std::mutex engine_mutex;
bool initialized = false;
musicmic::EngineStateMachine engine_state;
musicmic::CoreAudioDiscovery discovery;
std::wstring selected_source_id;
std::wstring selected_microphone_id;
std::wstring last_error;
float source_gain = 0.7F;
float microphone_gain = 1.0F;

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

template <std::size_t Capacity>
void CopyFixed(std::wstring_view source, wchar_t (&destination)[Capacity]) noexcept {
    const std::size_t length = std::min(source.size(), Capacity - 1U);
    std::copy_n(source.data(), length, destination);
    destination[length] = L'\0';
    std::fill(destination + length + 1U, destination + Capacity, L'\0');
}

void ApplyDiscoveryState() {
    engine_state.Apply(discovery.OutputAvailable()
        ? musicmic::EngineEvent::OutputAvailable
        : musicmic::EngineEvent::OutputLost);

    if (!selected_source_id.empty()) {
        const bool found = std::ranges::any_of(discovery.Sources(), [](const musicmic::SourceIdentity& source) {
            return source.id == selected_source_id;
        });
        engine_state.Apply(found
            ? musicmic::EngineEvent::SourceRecovered
            : musicmic::EngineEvent::SourceLost);
    }

    if (selected_microphone_id.empty()) {
        const auto default_microphone = std::ranges::find_if(
            discovery.Microphones(),
            [](const musicmic::MicrophoneIdentity& microphone) { return microphone.is_default; });
        if (default_microphone != discovery.Microphones().end()) {
            selected_microphone_id = default_microphone->id;
            engine_state.Apply(musicmic::EngineEvent::MicrophoneSelected);
        }
    } else {
        const bool found = std::ranges::any_of(
            discovery.Microphones(),
            [](const musicmic::MicrophoneIdentity& microphone) {
                return microphone.id == selected_microphone_id;
            });
        engine_state.Apply(found
            ? musicmic::EngineEvent::MicrophoneRecovered
            : musicmic::EngineEvent::MicrophoneLost);
    }
}

MM_Result RefreshLocked() {
    std::wstring discovery_error;
    if (!discovery.Refresh(discovery_error)) {
        last_error = std::move(discovery_error);
        engine_state.Apply(musicmic::EngineEvent::Failed);
        return MM_RESULT_AUDIO_FAILURE;
    }
    ApplyDiscoveryState();
    if (!discovery.OutputAvailable()) {
        last_error = L"VB-CABLE CABLE Input was not found. Install or enable VB-CABLE before starting injection.";
    } else {
        last_error.clear();
    }
    return MM_RESULT_OK;
}

}  // namespace

MM_Result MM_CALL MM_Initialize(void) {
    std::scoped_lock lock(engine_mutex);
    if (initialized) {
        return MM_RESULT_OK;
    }
    engine_state = musicmic::EngineStateMachine{};
    selected_source_id.clear();
    selected_microphone_id.clear();
    source_gain = 0.7F;
    microphone_gain = 1.0F;
    initialized = true;
    engine_state.Apply(musicmic::EngineEvent::Initialized);
    return RefreshLocked();
}

MM_Result MM_CALL MM_Shutdown(void) {
    std::scoped_lock lock(engine_mutex);
    initialized = false;
    engine_state = musicmic::EngineStateMachine{};
    discovery = musicmic::CoreAudioDiscovery{};
    selected_source_id.clear();
    selected_microphone_id.clear();
    last_error.clear();
    return MM_RESULT_OK;
}

MM_Result MM_CALL MM_RefreshDevices(void) {
    std::scoped_lock lock(engine_mutex);
    const MM_Result result = RequireInitialized();
    return result == MM_RESULT_OK ? RefreshLocked() : result;
}

MM_Result MM_CALL MM_GetSourceCount(uint32_t* count) {
    if (count == nullptr) {
        return MM_RESULT_INVALID_ARGUMENT;
    }
    std::scoped_lock lock(engine_mutex);
    const MM_Result result = RequireInitialized();
    if (result != MM_RESULT_OK) {
        return result;
    }
    *count = static_cast<uint32_t>(discovery.Sources().size());
    return MM_RESULT_OK;
}

MM_Result MM_CALL MM_GetSourceInfo(uint32_t index, MM_SourceInfo* info) {
    if (info == nullptr) {
        return MM_RESULT_INVALID_ARGUMENT;
    }
    std::scoped_lock lock(engine_mutex);
    const MM_Result result = RequireInitialized();
    if (result != MM_RESULT_OK) {
        return result;
    }
    if (index >= discovery.Sources().size()) {
        return MM_RESULT_NOT_FOUND;
    }
    const musicmic::SourceIdentity& source = discovery.Sources()[index];
    *info = {};
    CopyFixed(source.id, info->id);
    CopyFixed(source.display_name, info->display_name);
    info->process_id = source.process_id;
    info->is_spotify = source.is_spotify ? 1 : 0;
    return MM_RESULT_OK;
}

MM_Result MM_CALL MM_GetMicrophoneCount(uint32_t* count) {
    if (count == nullptr) {
        return MM_RESULT_INVALID_ARGUMENT;
    }
    std::scoped_lock lock(engine_mutex);
    const MM_Result result = RequireInitialized();
    if (result != MM_RESULT_OK) {
        return result;
    }
    *count = static_cast<uint32_t>(discovery.Microphones().size());
    return MM_RESULT_OK;
}

MM_Result MM_CALL MM_GetMicrophoneInfo(uint32_t index, MM_MicrophoneInfo* info) {
    if (info == nullptr) {
        return MM_RESULT_INVALID_ARGUMENT;
    }
    std::scoped_lock lock(engine_mutex);
    const MM_Result result = RequireInitialized();
    if (result != MM_RESULT_OK) {
        return result;
    }
    if (index >= discovery.Microphones().size()) {
        return MM_RESULT_NOT_FOUND;
    }
    const musicmic::MicrophoneIdentity& microphone = discovery.Microphones()[index];
    *info = {};
    CopyFixed(microphone.id, info->id);
    CopyFixed(microphone.display_name, info->display_name);
    info->is_default = microphone.is_default ? 1 : 0;
    return MM_RESULT_OK;
}

MM_Result MM_CALL MM_SelectSource(const wchar_t* source_id) {
    if (source_id == nullptr) {
        return MM_RESULT_INVALID_ARGUMENT;
    }
    std::scoped_lock lock(engine_mutex);
    const MM_Result result = RequireInitialized();
    if (result != MM_RESULT_OK) {
        return result;
    }
    if (*source_id == L'\0') {
        selected_source_id.clear();
        engine_state.Apply(musicmic::EngineEvent::SourceCleared);
        return MM_RESULT_OK;
    }
    const auto source = std::ranges::find_if(discovery.Sources(), [&](const musicmic::SourceIdentity& item) {
        return item.id == source_id;
    });
    if (source == discovery.Sources().end()) {
        last_error = L"The selected audio application is no longer available.";
        return MM_RESULT_NOT_FOUND;
    }
    selected_source_id = source->id;
    engine_state.Apply(musicmic::EngineEvent::SourceSelected);
    last_error.clear();
    return MM_RESULT_OK;
}

MM_Result MM_CALL MM_SelectMicrophone(const wchar_t* microphone_id) {
    if (microphone_id == nullptr) {
        return MM_RESULT_INVALID_ARGUMENT;
    }
    std::scoped_lock lock(engine_mutex);
    const MM_Result result = RequireInitialized();
    if (result != MM_RESULT_OK) {
        return result;
    }
    if (*microphone_id == L'\0') {
        selected_microphone_id.clear();
        engine_state.Apply(musicmic::EngineEvent::MicrophoneCleared);
        return MM_RESULT_OK;
    }
    const auto microphone = std::ranges::find_if(
        discovery.Microphones(),
        [&](const musicmic::MicrophoneIdentity& item) { return item.id == microphone_id; });
    if (microphone == discovery.Microphones().end()) {
        last_error = L"The selected physical microphone is no longer available.";
        return MM_RESULT_NOT_FOUND;
    }
    selected_microphone_id = microphone->id;
    engine_state.Apply(musicmic::EngineEvent::MicrophoneSelected);
    last_error.clear();
    return MM_RESULT_OK;
}

MM_Result MM_CALL MM_SetSourceGain(float normalized_gain) {
    if (!std::isfinite(normalized_gain) || normalized_gain < 0.0F || normalized_gain > 1.0F) {
        return MM_RESULT_INVALID_ARGUMENT;
    }
    std::scoped_lock lock(engine_mutex);
    const MM_Result result = RequireInitialized();
    if (result == MM_RESULT_OK) {
        source_gain = normalized_gain;
    }
    return result;
}

MM_Result MM_CALL MM_SetMicrophoneGain(float normalized_gain) {
    if (!std::isfinite(normalized_gain) || normalized_gain < 0.0F || normalized_gain > 1.0F) {
        return MM_RESULT_INVALID_ARGUMENT;
    }
    std::scoped_lock lock(engine_mutex);
    const MM_Result result = RequireInitialized();
    if (result == MM_RESULT_OK) {
        microphone_gain = normalized_gain;
    }
    return result;
}

MM_Result MM_CALL MM_StartInjection(void) {
    std::scoped_lock lock(engine_mutex);
    const MM_Result result = RequireInitialized();
    if (result != MM_RESULT_OK) {
        return result;
    }
    if (!discovery.OutputAvailable()) {
        last_error = L"VB-CABLE CABLE Input is unavailable; injection was not started.";
        return MM_RESULT_OUTPUT_UNAVAILABLE;
    }
    last_error = L"Live WASAPI streams are not active in this discovery-only engine slice; injection was not started.";
    return MM_RESULT_AUDIO_FAILURE;
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

MM_Result MM_CALL MM_HandleSystemResume(void) {
    std::scoped_lock lock(engine_mutex);
    const MM_Result result = RequireInitialized();
    return result == MM_RESULT_OK ? RefreshLocked() : result;
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

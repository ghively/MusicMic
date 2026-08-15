#include "engine_state.h"

namespace musicmic {

void EngineStateMachine::Apply(EngineEvent event) noexcept {
    switch (event) {
    case EngineEvent::Initialized:
        initialized_ = true;
        break;
    case EngineEvent::SourceSelected:
        source_selected_ = true;
        source_available_ = true;
        break;
    case EngineEvent::SourceCleared:
        if (injection_requested_) {
            break;
        }
        source_selected_ = false;
        source_available_ = false;
        break;
    case EngineEvent::SourceLost:
        source_available_ = false;
        break;
    case EngineEvent::SourceRecovered:
        source_available_ = source_selected_;
        break;
    case EngineEvent::MicrophoneSelected:
        microphone_selection_decided_ = true;
        microphone_selected_ = true;
        microphone_available_ = true;
        break;
    case EngineEvent::MicrophoneCleared:
        if (injection_requested_) {
            break;
        }
        microphone_selection_decided_ = true;
        microphone_selected_ = false;
        microphone_available_ = false;
        break;
    case EngineEvent::MicrophoneLost:
        microphone_available_ = false;
        break;
    case EngineEvent::MicrophoneRecovered:
        microphone_available_ = microphone_selected_;
        break;
    case EngineEvent::OutputAvailable:
        output_available_ = true;
        break;
    case EngineEvent::OutputLost:
        output_available_ = false;
        injection_requested_ = false;
        break;
    case EngineEvent::StartRequested:
        if (source_selected_ && microphone_selected_ && output_available_) {
            injection_requested_ = true;
        }
        break;
    case EngineEvent::StopRequested:
        injection_requested_ = false;
        break;
    case EngineEvent::Failed:
        failed_ = true;
        break;
    case EngineEvent::RecoverySucceeded:
        failed_ = false;
        break;
    }

    Recompute();
}

void EngineStateMachine::Recompute() noexcept {
    if (failed_) {
        state_ = EngineState::Error;
    } else if (!initialized_) {
        state_ = EngineState::Initializing;
    } else if (!output_available_) {
        state_ = EngineState::OutputUnavailable;
    } else if (!source_selected_ || !source_available_) {
        state_ = EngineState::SourceUnavailable;
    } else if (!microphone_selected_ || !microphone_available_) {
        state_ = EngineState::MicrophoneUnavailable;
    } else if (injection_requested_) {
        state_ = EngineState::Injecting;
    } else {
        state_ = EngineState::Ready;
    }
}

}  // namespace musicmic

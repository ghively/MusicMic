#pragma once

namespace musicmic {

enum class EngineState {
    Initializing,
    Ready,
    Injecting,
    SourceUnavailable,
    MicrophoneUnavailable,
    OutputUnavailable,
    Error,
};

enum class EngineEvent {
    Initialized,
    SourceSelected,
    SourceCleared,
    SourceLost,
    SourceRecovered,
    MicrophoneSelected,
    MicrophoneCleared,
    MicrophoneLost,
    MicrophoneRecovered,
    OutputAvailable,
    OutputLost,
    StartRequested,
    StopRequested,
    Failed,
};

class EngineStateMachine final {
public:
    void Apply(EngineEvent event) noexcept;

    [[nodiscard]] EngineState State() const noexcept { return state_; }
    [[nodiscard]] bool InjectionRequested() const noexcept { return injection_requested_; }
    [[nodiscard]] bool SourceSelected() const noexcept { return source_selected_; }
    [[nodiscard]] bool MicrophoneSelected() const noexcept { return microphone_selected_; }
    [[nodiscard]] bool SourceAvailable() const noexcept { return source_available_; }
    [[nodiscard]] bool MicrophoneAvailable() const noexcept { return microphone_available_; }
    [[nodiscard]] bool OutputAvailable() const noexcept { return output_available_; }

private:
    void Recompute() noexcept;

    EngineState state_{EngineState::Initializing};
    bool initialized_{false};
    bool failed_{false};
    bool injection_requested_{false};
    bool source_selected_{false};
    bool microphone_selected_{false};
    bool source_available_{false};
    bool microphone_available_{false};
    bool output_available_{false};
};

}  // namespace musicmic

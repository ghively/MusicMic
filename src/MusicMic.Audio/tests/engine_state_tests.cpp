#include "engine_state.h"
#include "test_support.h"

using musicmic::EngineEvent;
using musicmic::EngineState;
using musicmic::EngineStateMachine;

MM_TEST(State_machine_becomes_ready_only_after_required_selections_are_available) {
    EngineStateMachine machine;

    machine.Apply(EngineEvent::Initialized);
    machine.Apply(EngineEvent::SourceSelected);
    machine.Apply(EngineEvent::MicrophoneSelected);
    MM_REQUIRE(machine.State() == EngineState::OutputUnavailable);

    machine.Apply(EngineEvent::OutputAvailable);
    MM_REQUIRE(machine.State() == EngineState::Ready);
}

MM_TEST(State_machine_starts_and_stops_injection_without_mutating_selections) {
    EngineStateMachine machine;
    machine.Apply(EngineEvent::Initialized);
    machine.Apply(EngineEvent::SourceSelected);
    machine.Apply(EngineEvent::MicrophoneSelected);
    machine.Apply(EngineEvent::OutputAvailable);

    machine.Apply(EngineEvent::StartRequested);
    MM_REQUIRE(machine.State() == EngineState::Injecting);
    MM_REQUIRE(machine.SourceSelected());
    MM_REQUIRE(machine.MicrophoneSelected());

    machine.Apply(EngineEvent::StopRequested);
    MM_REQUIRE(machine.State() == EngineState::Ready);
    MM_REQUIRE(machine.SourceSelected());
    MM_REQUIRE(machine.MicrophoneSelected());
}

MM_TEST(Source_loss_during_injection_keeps_microphone_and_output_active) {
    EngineStateMachine machine;
    machine.Apply(EngineEvent::Initialized);
    machine.Apply(EngineEvent::SourceSelected);
    machine.Apply(EngineEvent::MicrophoneSelected);
    machine.Apply(EngineEvent::OutputAvailable);
    machine.Apply(EngineEvent::StartRequested);

    machine.Apply(EngineEvent::SourceLost);

    MM_REQUIRE(machine.State() == EngineState::SourceUnavailable);
    MM_REQUIRE(machine.InjectionRequested());
    MM_REQUIRE(machine.MicrophoneAvailable());
    MM_REQUIRE(machine.OutputAvailable());
}

MM_TEST(Microphone_loss_during_injection_keeps_source_and_output_active) {
    EngineStateMachine machine;
    machine.Apply(EngineEvent::Initialized);
    machine.Apply(EngineEvent::SourceSelected);
    machine.Apply(EngineEvent::MicrophoneSelected);
    machine.Apply(EngineEvent::OutputAvailable);
    machine.Apply(EngineEvent::StartRequested);

    machine.Apply(EngineEvent::MicrophoneLost);

    MM_REQUIRE(machine.State() == EngineState::MicrophoneUnavailable);
    MM_REQUIRE(machine.InjectionRequested());
    MM_REQUIRE(machine.SourceAvailable());
    MM_REQUIRE(machine.OutputAvailable());
}

MM_TEST(Output_loss_stops_injection_safely) {
    EngineStateMachine machine;
    machine.Apply(EngineEvent::Initialized);
    machine.Apply(EngineEvent::SourceSelected);
    machine.Apply(EngineEvent::MicrophoneSelected);
    machine.Apply(EngineEvent::OutputAvailable);
    machine.Apply(EngineEvent::StartRequested);

    machine.Apply(EngineEvent::OutputLost);

    MM_REQUIRE(machine.State() == EngineState::OutputUnavailable);
    MM_REQUIRE(!machine.InjectionRequested());
}

MM_TEST(Recovered_source_restores_injecting_state_when_injection_remains_requested) {
    EngineStateMachine machine;
    machine.Apply(EngineEvent::Initialized);
    machine.Apply(EngineEvent::SourceSelected);
    machine.Apply(EngineEvent::MicrophoneSelected);
    machine.Apply(EngineEvent::OutputAvailable);
    machine.Apply(EngineEvent::StartRequested);
    machine.Apply(EngineEvent::SourceLost);

    machine.Apply(EngineEvent::SourceRecovered);

    MM_REQUIRE(machine.State() == EngineState::Injecting);
}

MM_TEST(Selection_changes_are_rejected_while_injection_is_requested) {
    EngineStateMachine machine;
    machine.Apply(EngineEvent::Initialized);
    machine.Apply(EngineEvent::SourceSelected);
    machine.Apply(EngineEvent::MicrophoneSelected);
    machine.Apply(EngineEvent::OutputAvailable);
    machine.Apply(EngineEvent::StartRequested);

    MM_REQUIRE(!machine.SelectionChangeAllowed());
    machine.Apply(EngineEvent::SourceCleared);
    machine.Apply(EngineEvent::MicrophoneCleared);

    MM_REQUIRE(machine.State() == EngineState::Injecting);
    MM_REQUIRE(machine.SourceSelected());
    MM_REQUIRE(machine.MicrophoneSelected());
}

MM_TEST(Explicit_microphone_clear_is_not_mistaken_for_uninitialized_default_selection) {
    EngineStateMachine machine;
    machine.Apply(EngineEvent::Initialized);
    MM_REQUIRE(machine.ShouldSelectDefaultMicrophone());

    machine.Apply(EngineEvent::MicrophoneCleared);

    MM_REQUIRE(!machine.ShouldSelectDefaultMicrophone());
    MM_REQUIRE(!machine.MicrophoneSelected());
}

MM_TEST(Successful_refresh_clears_a_transient_error_without_losing_injection_intent) {
    EngineStateMachine machine;
    machine.Apply(EngineEvent::Initialized);
    machine.Apply(EngineEvent::SourceSelected);
    machine.Apply(EngineEvent::MicrophoneSelected);
    machine.Apply(EngineEvent::OutputAvailable);
    machine.Apply(EngineEvent::StartRequested);

    machine.Apply(EngineEvent::Failed);
    MM_REQUIRE(machine.State() == EngineState::Error);
    MM_REQUIRE(machine.InjectionRequested());

    machine.Apply(EngineEvent::RecoverySucceeded);
    MM_REQUIRE(machine.State() == EngineState::Injecting);
    MM_REQUIRE(machine.InjectionRequested());
}

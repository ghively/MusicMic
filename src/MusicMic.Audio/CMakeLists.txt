cmake_minimum_required(VERSION 3.25)

project(MusicMicAudio LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

if(MSVC)
    # Core Audio callbacks and the C ABI use C++ exception-aware standard
    # library code. Enable correct unwind semantics and keep native warnings
    # release-blocking, matching the direct MSVC verification configuration.
    add_compile_options(/EHsc /W4 /WX)
endif()

include(CTest)

add_library(MusicMic.Audio SHARED
    src/core_audio_discovery.cpp
    src/engine_state.cpp
    src/mixer.cpp
    src/stream_worker.cpp
    src/wasapi_streams.cpp
    src/exports.cpp
)
target_include_directories(MusicMic.Audio PUBLIC include PRIVATE src)
target_compile_definitions(MusicMic.Audio PRIVATE MUSICMIC_AUDIO_EXPORTS UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
target_link_libraries(MusicMic.Audio PRIVATE Avrt Ole32 Mmdevapi Propsys)
set_target_properties(MusicMic.Audio PROPERTIES OUTPUT_NAME "MusicMic.Audio")

if(BUILD_TESTING)
    add_executable(MusicMic.Audio.Tests
        src/core_audio_discovery.cpp
        src/mixer.cpp
        src/stream_worker.cpp
        src/wasapi_streams.cpp
        src/engine_state.cpp
        tests/mixer_tests.cpp
        tests/stream_worker_tests.cpp
        tests/wasapi_streams_tests.cpp
        tests/engine_state_tests.cpp
        tests/abi_tests.cpp
        tests/abi_boundary_tests.cpp
        tests/core_audio_discovery_tests.cpp
        tests/test_main.cpp
    )
    target_include_directories(MusicMic.Audio.Tests PRIVATE include src)
    target_compile_definitions(MusicMic.Audio.Tests PRIVATE UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
    target_link_libraries(MusicMic.Audio.Tests PRIVATE Avrt Ole32 Mmdevapi Propsys)
    target_link_libraries(MusicMic.Audio.Tests PRIVATE MusicMic.Audio)
    add_test(NAME MusicMic.Audio.Tests COMMAND MusicMic.Audio.Tests)
endif()

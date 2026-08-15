cmake_minimum_required(VERSION 3.25)

project(MusicMicAudio LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

include(CTest)

if(BUILD_TESTING)
    add_executable(MusicMic.Audio.Tests
        src/mixer.cpp
        src/engine_state.cpp
        tests/mixer_tests.cpp
        tests/engine_state_tests.cpp
        tests/test_main.cpp
    )
    target_include_directories(MusicMic.Audio.Tests PRIVATE src)
    add_test(NAME MusicMic.Audio.Tests COMMAND MusicMic.Audio.Tests)
endif()

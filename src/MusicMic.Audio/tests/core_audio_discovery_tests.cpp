#include "core_audio_discovery.h"
#include "test_support.h"

MM_TEST(Discovery_only_accepts_the_canonical_VB_CABLE_render_name) {
    MM_REQUIRE(musicmic::IsVbCableInputName(L"CABLE Input"));
    MM_REQUIRE(musicmic::IsVbCableInputName(L"CABLE Input (VB-Audio Virtual Cable)"));
    MM_REQUIRE(!musicmic::IsVbCableInputName(L"CABLE Output (VB-Audio Virtual Cable)"));
    MM_REQUIRE(!musicmic::IsVbCableInputName(L"Speakers (CABLE Input monitor)"));
}

MM_TEST(Discovery_excludes_virtual_capture_endpoints_from_microphone_choices) {
    constexpr std::uint32_t microphone_form_factor = 4;
    constexpr std::uint32_t unknown_form_factor = 10;
    MM_REQUIRE(musicmic::IsPhysicalCaptureEndpoint(L"USB Podcast Microphone", microphone_form_factor));
    MM_REQUIRE(!musicmic::IsPhysicalCaptureEndpoint(
        L"CABLE Output (VB-Audio Virtual Cable)", unknown_form_factor));
    MM_REQUIRE(!musicmic::IsPhysicalCaptureEndpoint(L"Stereo Mix (Realtek Audio)", unknown_form_factor));
}

MM_TEST(Discovery_normalizes_executable_identity_case_and_separators) {
    MM_REQUIRE(
        musicmic::NormalizeExecutableIdentity(L"C:/Program Files/Spotify/Spotify.exe") ==
        L"c:\\program files\\spotify\\spotify.exe");
}

MM_TEST(Discovery_recognizes_Spotify_from_executable_without_an_API) {
    MM_REQUIRE(
        musicmic::BuildApplicationDisplayName(L"C:\\Users\\test\\Spotify.exe", L"") ==
        L"Spotify");
    MM_REQUIRE(
        musicmic::BuildApplicationDisplayName(L"C:\\Apps\\firefox.exe", L"@resource.dll,-1") ==
        L"firefox");
}

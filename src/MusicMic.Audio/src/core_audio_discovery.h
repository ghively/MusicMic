#pragma once

#include <cstdint>
#include <string>
#include <string_view>
#include <vector>

namespace musicmic {

struct SourceIdentity final {
    std::wstring id;
    std::wstring executable_path;
    std::wstring display_name;
    std::uint32_t process_id{};
    bool is_spotify{};
};

struct MicrophoneIdentity final {
    std::wstring id;
    std::wstring display_name;
    bool is_default{};
};

[[nodiscard]] bool IsVbCableInputName(std::wstring_view friendly_name) noexcept;
[[nodiscard]] bool IsPhysicalCaptureEndpoint(
    std::wstring_view friendly_name,
    std::uint32_t endpoint_form_factor) noexcept;
[[nodiscard]] std::wstring NormalizeExecutableIdentity(std::wstring_view executable_path);
[[nodiscard]] std::wstring BuildApplicationDisplayName(
    std::wstring_view executable_path,
    std::wstring_view session_display_name);

class CoreAudioDiscovery final {
public:
    [[nodiscard]] bool Refresh(std::wstring& error_message);

    [[nodiscard]] const std::vector<SourceIdentity>& Sources() const noexcept { return sources_; }
    [[nodiscard]] const std::vector<MicrophoneIdentity>& Microphones() const noexcept { return microphones_; }
    [[nodiscard]] bool OutputAvailable() const noexcept { return !vb_cable_endpoint_id_.empty(); }
    [[nodiscard]] const std::wstring& VbCableEndpointId() const noexcept { return vb_cable_endpoint_id_; }

private:
    std::vector<SourceIdentity> sources_;
    std::vector<MicrophoneIdentity> microphones_;
    std::wstring vb_cable_endpoint_id_;
};

}  // namespace musicmic

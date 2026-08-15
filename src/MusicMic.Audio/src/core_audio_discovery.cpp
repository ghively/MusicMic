#include "core_audio_discovery.h"

#include <windows.h>

#include <audiopolicy.h>
#include <initguid.h>
#include <mmdeviceapi.h>
#include <propkeydef.h>
#include <functiondiscoverykeys_devpkey.h>
#include <propvarutil.h>
#include <wrl/client.h>

#include <algorithm>
#include <array>
#include <cwctype>
#include <iomanip>
#include <sstream>
#include <unordered_map>

namespace musicmic {
namespace {

using Microsoft::WRL::ComPtr;

class ComApartment final {
public:
    ComApartment() noexcept : result_(CoInitializeEx(nullptr, COINIT_MULTITHREADED)) {}
    ~ComApartment() {
        if (SUCCEEDED(result_)) {
            CoUninitialize();
        }
    }

    [[nodiscard]] bool Usable() const noexcept {
        return SUCCEEDED(result_) || result_ == RPC_E_CHANGED_MODE;
    }
    [[nodiscard]] HRESULT Result() const noexcept { return result_; }

private:
    HRESULT result_;
};

[[nodiscard]] std::wstring HResultMessage(HRESULT result) {
    wchar_t* message = nullptr;
    const DWORD length = FormatMessageW(
        FORMAT_MESSAGE_ALLOCATE_BUFFER | FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS,
        nullptr,
        static_cast<DWORD>(result),
        0,
        reinterpret_cast<wchar_t*>(&message),
        0,
        nullptr);
    std::wstring text = length != 0 && message != nullptr ? std::wstring(message, length) : L"Unknown error";
    if (message != nullptr) {
        LocalFree(message);
    }
    while (!text.empty() && (text.back() == L'\r' || text.back() == L'\n' || text.back() == L' ')) {
        text.pop_back();
    }
    return text;
}

[[nodiscard]] std::wstring ReadEndpointId(IMMDevice* device) {
    wchar_t* raw_id = nullptr;
    if (FAILED(device->GetId(&raw_id)) || raw_id == nullptr) {
        return {};
    }
    std::wstring id(raw_id);
    CoTaskMemFree(raw_id);
    return id;
}

[[nodiscard]] bool OpenPropertyStore(IMMDevice* device, ComPtr<IPropertyStore>& store) {
    return SUCCEEDED(device->OpenPropertyStore(STGM_READ, &store));
}

[[nodiscard]] std::wstring ReadFriendlyName(IMMDevice* device) {
    ComPtr<IPropertyStore> store;
    if (!OpenPropertyStore(device, store)) {
        return {};
    }
    PROPVARIANT value;
    PropVariantInit(&value);
    const HRESULT result = store->GetValue(PKEY_Device_FriendlyName, &value);
    std::wstring name;
    if (SUCCEEDED(result) && value.vt == VT_LPWSTR && value.pwszVal != nullptr) {
        name = value.pwszVal;
    }
    PropVariantClear(&value);
    return name;
}

[[nodiscard]] std::uint32_t ReadFormFactor(IMMDevice* device) {
    ComPtr<IPropertyStore> store;
    if (!OpenPropertyStore(device, store)) {
        return static_cast<std::uint32_t>(UnknownFormFactor);
    }
    PROPVARIANT value;
    PropVariantInit(&value);
    const HRESULT result = store->GetValue(PKEY_AudioEndpoint_FormFactor, &value);
    const auto form_factor = SUCCEEDED(result) && value.vt == VT_UI4
        ? value.ulVal
        : static_cast<unsigned long>(UnknownFormFactor);
    PropVariantClear(&value);
    return static_cast<std::uint32_t>(form_factor);
}

[[nodiscard]] std::wstring QueryExecutablePath(DWORD process_id) {
    HANDLE process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, process_id);
    if (process == nullptr) {
        return {};
    }
    std::wstring path(32768, L'\0');
    DWORD length = static_cast<DWORD>(path.size());
    if (!QueryFullProcessImageNameW(process, 0, path.data(), &length)) {
        CloseHandle(process);
        return {};
    }
    CloseHandle(process);
    path.resize(length);
    return path;
}

[[nodiscard]] std::wstring SessionDisplayName(IAudioSessionControl* session) {
    wchar_t* raw_name = nullptr;
    if (FAILED(session->GetDisplayName(&raw_name)) || raw_name == nullptr) {
        return {};
    }
    std::wstring name(raw_name);
    CoTaskMemFree(raw_name);
    return name;
}

[[nodiscard]] std::wstring StableId(std::wstring_view normalized_path) {
    std::uint64_t hash = 14695981039346656037ULL;
    for (const wchar_t character : normalized_path) {
        const std::uint16_t unit = static_cast<std::uint16_t>(character);
        hash ^= static_cast<std::uint8_t>(unit & 0xffU);
        hash *= 1099511628211ULL;
        hash ^= static_cast<std::uint8_t>((unit >> 8U) & 0xffU);
        hash *= 1099511628211ULL;
    }
    std::wostringstream output;
    output << L"exe:" << std::hex << std::setw(16) << std::setfill(L'0') << hash;
    return output.str();
}

void EnumerateSessionsOnEndpoint(
    IMMDevice* endpoint,
    std::vector<SourceIdentity>& sources) {
    ComPtr<IAudioSessionManager2> manager;
    if (FAILED(endpoint->Activate(
            __uuidof(IAudioSessionManager2),
            CLSCTX_ALL,
            nullptr,
            reinterpret_cast<void**>(manager.GetAddressOf())))) {
        return;
    }
    ComPtr<IAudioSessionEnumerator> sessions;
    if (FAILED(manager->GetSessionEnumerator(&sessions))) {
        return;
    }
    int count = 0;
    if (FAILED(sessions->GetCount(&count))) {
        return;
    }
    for (int index = 0; index < count; ++index) {
        ComPtr<IAudioSessionControl> session;
        if (FAILED(sessions->GetSession(index, &session))) {
            continue;
        }
        AudioSessionState state = AudioSessionStateInactive;
        if (FAILED(session->GetState(&state)) || state != AudioSessionStateActive) {
            continue;
        }
        ComPtr<IAudioSessionControl2> session2;
        if (FAILED(session.As(&session2)) || session2->IsSystemSoundsSession() == S_OK) {
            continue;
        }
        DWORD process_id = 0;
        if (FAILED(session2->GetProcessId(&process_id)) || process_id == 0) {
            continue;
        }
        std::wstring executable_path = QueryExecutablePath(process_id);
        if (executable_path.empty()) {
            continue;
        }
        std::wstring normalized_path = NormalizeExecutableIdentity(executable_path);
        std::wstring display_name = BuildApplicationDisplayName(
            executable_path,
            SessionDisplayName(session.Get()));
        const bool is_spotify = _wcsicmp(display_name.c_str(), L"Spotify") == 0;
        sources.push_back(SourceIdentity{
            StableId(normalized_path),
            std::move(executable_path),
            std::move(display_name),
            process_id,
            is_spotify});
    }
}

[[nodiscard]] bool LessCaseInsensitive(std::wstring_view left, std::wstring_view right) {
    return _wcsicmp(std::wstring(left).c_str(), std::wstring(right).c_str()) < 0;
}

}  // namespace

bool IsVbCableInputName(std::wstring_view friendly_name) noexcept {
    constexpr std::wstring_view canonical = L"CABLE Input";
    if (friendly_name.size() < canonical.size() ||
        _wcsnicmp(friendly_name.data(), canonical.data(), canonical.size()) != 0) {
        return false;
    }
    if (friendly_name.size() == canonical.size()) {
        return true;
    }
    return friendly_name.size() > canonical.size() + 1U &&
        friendly_name[canonical.size()] == L' ' &&
        friendly_name[canonical.size() + 1U] == L'(';
}

bool IsPhysicalCaptureEndpoint(
    std::wstring_view friendly_name,
    std::uint32_t endpoint_form_factor) noexcept {
    std::wstring lowered(friendly_name);
    std::transform(lowered.begin(), lowered.end(), lowered.begin(), [](wchar_t value) {
        return static_cast<wchar_t>(std::towlower(value));
    });
    constexpr std::array virtual_markers{
        std::wstring_view(L"cable output"),
        std::wstring_view(L"vb-audio"),
        std::wstring_view(L"voicemeeter"),
        std::wstring_view(L"virtual"),
        std::wstring_view(L"stereo mix")};
    if (std::ranges::any_of(virtual_markers, [&](std::wstring_view marker) {
            return lowered.find(marker) != std::wstring::npos;
        })) {
        return false;
    }
    return endpoint_form_factor != static_cast<std::uint32_t>(RemoteNetworkDevice) &&
        endpoint_form_factor != static_cast<std::uint32_t>(Speakers) &&
        endpoint_form_factor != static_cast<std::uint32_t>(Headphones) &&
        endpoint_form_factor != static_cast<std::uint32_t>(UnknownDigitalPassthrough) &&
        endpoint_form_factor != static_cast<std::uint32_t>(SPDIF) &&
        endpoint_form_factor != static_cast<std::uint32_t>(DigitalAudioDisplayDevice);
}

std::wstring NormalizeExecutableIdentity(std::wstring_view executable_path) {
    std::wstring normalized(executable_path);
    std::transform(normalized.begin(), normalized.end(), normalized.begin(), [](wchar_t value) {
        if (value == L'/') {
            return L'\\';
        }
        return static_cast<wchar_t>(std::towlower(value));
    });
    return normalized;
}

std::wstring BuildApplicationDisplayName(
    std::wstring_view executable_path,
    std::wstring_view session_display_name) {
    const std::size_t separator = executable_path.find_last_of(L"\\/");
    std::wstring filename(executable_path.substr(
        separator == std::wstring_view::npos ? 0 : separator + 1U));
    const std::size_t extension = filename.find_last_of(L'.');
    if (extension != std::wstring::npos) {
        filename.resize(extension);
    }
    if (_wcsicmp(filename.c_str(), L"spotify") == 0) {
        return L"Spotify";
    }
    if (!session_display_name.empty() && session_display_name.front() != L'@') {
        return std::wstring(session_display_name);
    }
    return filename.empty() ? L"Audio application" : filename;
}

std::vector<SourceIdentity> ResolveUnambiguousApplicationSources(
    std::vector<SourceIdentity> candidates) {
    struct CandidateGroup final {
        std::size_t first_index{};
        std::uint32_t process_id{};
        bool ambiguous{};
    };

    std::unordered_map<std::wstring, CandidateGroup> groups;
    groups.reserve(candidates.size());
    for (std::size_t index = 0; index < candidates.size(); ++index) {
        const std::wstring identity = NormalizeExecutableIdentity(candidates[index].executable_path);
        const auto [iterator, inserted] = groups.try_emplace(
            identity,
            CandidateGroup{index, candidates[index].process_id, false});
        if (!inserted && iterator->second.process_id != candidates[index].process_id) {
            // Process-loopback is rooted at one PID. Silently choosing among unrelated
            // active processes with the same executable would overstate what is captured.
            iterator->second.ambiguous = true;
        }
    }

    std::vector<SourceIdentity> resolved;
    resolved.reserve(groups.size());
    for (const auto& [identity, group] : groups) {
        (void)identity;
        if (!group.ambiguous) {
            resolved.push_back(std::move(candidates[group.first_index]));
        }
    }
    return resolved;
}

bool CoreAudioDiscovery::Refresh(std::wstring& error_message) {
    sources_.clear();
    microphones_.clear();
    vb_cable_endpoint_id_.clear();
    error_message.clear();

    ComApartment apartment;
    if (!apartment.Usable()) {
        error_message = L"Could not initialize COM for Core Audio discovery: " +
            HResultMessage(apartment.Result());
        return false;
    }

    ComPtr<IMMDeviceEnumerator> enumerator;
    HRESULT result = CoCreateInstance(
        __uuidof(MMDeviceEnumerator),
        nullptr,
        CLSCTX_ALL,
        IID_PPV_ARGS(&enumerator));
    if (FAILED(result)) {
        error_message = L"Could not create the Windows audio device enumerator: " + HResultMessage(result);
        return false;
    }

    ComPtr<IMMDeviceCollection> render_endpoints;
    result = enumerator->EnumAudioEndpoints(eRender, DEVICE_STATE_ACTIVE, &render_endpoints);
    if (FAILED(result)) {
        error_message = L"Could not enumerate active playback endpoints: " + HResultMessage(result);
        return false;
    }
    UINT render_count = 0;
    result = render_endpoints->GetCount(&render_count);
    if (FAILED(result)) {
        error_message = L"Could not read active playback endpoints: " + HResultMessage(result);
        return false;
    }
    std::vector<SourceIdentity> source_candidates;
    for (UINT index = 0; index < render_count; ++index) {
        ComPtr<IMMDevice> endpoint;
        if (FAILED(render_endpoints->Item(index, &endpoint))) {
            continue;
        }
        const std::wstring friendly_name = ReadFriendlyName(endpoint.Get());
        if (vb_cable_endpoint_id_.empty() && IsVbCableInputName(friendly_name)) {
            vb_cable_endpoint_id_ = ReadEndpointId(endpoint.Get());
        }
        EnumerateSessionsOnEndpoint(endpoint.Get(), source_candidates);
    }
    sources_ = ResolveUnambiguousApplicationSources(std::move(source_candidates));

    std::wstring default_capture_id;
    ComPtr<IMMDevice> default_capture;
    if (SUCCEEDED(enumerator->GetDefaultAudioEndpoint(eCapture, eConsole, &default_capture))) {
        default_capture_id = ReadEndpointId(default_capture.Get());
    }
    ComPtr<IMMDeviceCollection> capture_endpoints;
    result = enumerator->EnumAudioEndpoints(eCapture, DEVICE_STATE_ACTIVE, &capture_endpoints);
    if (FAILED(result)) {
        error_message = L"Could not enumerate active recording endpoints: " + HResultMessage(result);
        return false;
    }
    UINT capture_count = 0;
    if (FAILED(capture_endpoints->GetCount(&capture_count))) {
        error_message = L"Could not read active recording endpoints.";
        return false;
    }
    for (UINT index = 0; index < capture_count; ++index) {
        ComPtr<IMMDevice> endpoint;
        if (FAILED(capture_endpoints->Item(index, &endpoint))) {
            continue;
        }
        std::wstring friendly_name = ReadFriendlyName(endpoint.Get());
        if (!IsPhysicalCaptureEndpoint(friendly_name, ReadFormFactor(endpoint.Get()))) {
            continue;
        }
        std::wstring id = ReadEndpointId(endpoint.Get());
        if (id.empty()) {
            continue;
        }
        microphones_.push_back(MicrophoneIdentity{
            std::move(id),
            std::move(friendly_name),
            false});
        microphones_.back().is_default = microphones_.back().id == default_capture_id;
    }

    std::ranges::sort(sources_, [](const SourceIdentity& left, const SourceIdentity& right) {
        return LessCaseInsensitive(left.display_name, right.display_name);
    });
    std::ranges::sort(microphones_, [](const MicrophoneIdentity& left, const MicrophoneIdentity& right) {
        if (left.is_default != right.is_default) {
            return left.is_default;
        }
        return LessCaseInsensitive(left.display_name, right.display_name);
    });
    return true;
}

}  // namespace musicmic

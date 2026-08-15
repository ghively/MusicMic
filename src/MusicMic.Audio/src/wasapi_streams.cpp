#include "wasapi_streams.h"

#include "mixer.h"
#include "stream_worker.h"

#include <audioclient.h>
#include <audioclientactivationparams.h>
#include <avrt.h>
#include <ksmedia.h>
#include <mmdeviceapi.h>
#include <propvarutil.h>
#include <wrl/client.h>
#include <wrl/implements.h>

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cstring>
#include <future>
#include <mutex>
#include <span>
#include <thread>
#include <utility>
#include <vector>

namespace musicmic {
namespace {

using Microsoft::WRL::ComPtr;

constexpr std::size_t kQueueCapacityFrames = kBusSampleRate / 10U;

class UniqueHandle final {
public:
    UniqueHandle() = default;
    explicit UniqueHandle(HANDLE handle) noexcept : handle_(handle) {}
    ~UniqueHandle() { Reset(); }

    UniqueHandle(const UniqueHandle&) = delete;
    UniqueHandle& operator=(const UniqueHandle&) = delete;

    UniqueHandle(UniqueHandle&& other) noexcept : handle_(other.Release()) {}
    UniqueHandle& operator=(UniqueHandle&& other) noexcept {
        if (this != &other) {
            Reset(other.Release());
        }
        return *this;
    }

    [[nodiscard]] HANDLE Get() const noexcept { return handle_; }
    [[nodiscard]] explicit operator bool() const noexcept {
        return handle_ != nullptr && handle_ != INVALID_HANDLE_VALUE;
    }
    [[nodiscard]] HANDLE Release() noexcept {
        return std::exchange(handle_, nullptr);
    }
    void Reset(HANDLE handle = nullptr) noexcept {
        if (*this) {
            CloseHandle(handle_);
        }
        handle_ = handle;
    }

private:
    HANDLE handle_{};
};

class ComApartment final {
public:
    ComApartment() noexcept : result_(CoInitializeEx(nullptr, COINIT_MULTITHREADED)) {}
    ~ComApartment() {
        if (SUCCEEDED(result_)) {
            CoUninitialize();
        }
    }

    [[nodiscard]] HRESULT Result() const noexcept {
        return result_ == RPC_E_CHANGED_MODE ? S_OK : result_;
    }

private:
    HRESULT result_;
};

class MmcssRegistration final {
public:
    explicit MmcssRegistration(const wchar_t* task_name) noexcept {
        DWORD task_index = 0;
        handle_ = AvSetMmThreadCharacteristicsW(task_name, &task_index);
    }
    ~MmcssRegistration() {
        if (handle_ != nullptr) {
            AvRevertMmThreadCharacteristics(handle_);
        }
    }

    MmcssRegistration(const MmcssRegistration&) = delete;
    MmcssRegistration& operator=(const MmcssRegistration&) = delete;

private:
    HANDLE handle_{};
};

class ActivateAudioCompletionHandler final
    : public Microsoft::WRL::RuntimeClass<
          Microsoft::WRL::RuntimeClassFlags<Microsoft::WRL::ClassicCom>,
          Microsoft::WRL::FtmBase,
          IActivateAudioInterfaceCompletionHandler> {
public:
    ActivateAudioCompletionHandler()
        : completed_(CreateEventW(nullptr, TRUE, FALSE, nullptr)) {}

    HRESULT STDMETHODCALLTYPE ActivateCompleted(
        IActivateAudioInterfaceAsyncOperation* operation) override {
        HRESULT activation_result = E_UNEXPECTED;
        ComPtr<IUnknown> activated;
        HRESULT result = operation == nullptr
            ? E_POINTER
            : operation->GetActivateResult(&activation_result, &activated);
        if (SUCCEEDED(result)) {
            result = activation_result;
        }
        if (SUCCEEDED(result)) {
            result = activated.As(&audio_client_);
        }
        result_ = result;
        if (completed_) {
            SetEvent(completed_.Get());
        }
        return S_OK;
    }

    [[nodiscard]] HRESULT Wait(ComPtr<IAudioClient>& audio_client) noexcept {
        if (!completed_) {
            return HRESULT_FROM_WIN32(ERROR_NOT_ENOUGH_MEMORY);
        }
        const DWORD wait_result = WaitForSingleObject(completed_.Get(), 10000);
        if (wait_result == WAIT_TIMEOUT) {
            return HRESULT_FROM_WIN32(WAIT_TIMEOUT);
        }
        if (wait_result != WAIT_OBJECT_0) {
            return HRESULT_FROM_WIN32(GetLastError());
        }
        if (SUCCEEDED(result_)) {
            audio_client = audio_client_;
        }
        return result_;
    }

private:
    UniqueHandle completed_;
    HRESULT result_{E_PENDING};
    ComPtr<IAudioClient> audio_client_;
};

struct CaptureSession final {
    UniqueHandle sample_event;
    UniqueHandle process_handle;
    ComPtr<IAudioClient> audio_client;
    ComPtr<IAudioCaptureClient> capture_client;
};

struct RenderSession final {
    UniqueHandle sample_event;
    ComPtr<IAudioClient> audio_client;
    ComPtr<IAudioRenderClient> render_client;
    UINT32 buffer_frames{};
};

[[nodiscard]] HRESULT CreateAudioDeviceEnumerator(ComPtr<IMMDeviceEnumerator>& enumerator) {
    return CoCreateInstance(
        __uuidof(MMDeviceEnumerator),
        nullptr,
        CLSCTX_ALL,
        IID_PPV_ARGS(&enumerator));
}

[[nodiscard]] HRESULT ActivateEndpointClient(
    std::wstring_view endpoint_id,
    ComPtr<IAudioClient>& audio_client) {
    ComPtr<IMMDeviceEnumerator> enumerator;
    HRESULT result = CreateAudioDeviceEnumerator(enumerator);
    if (FAILED(result)) {
        return result;
    }
    ComPtr<IMMDevice> device;
    result = enumerator->GetDevice(std::wstring(endpoint_id).c_str(), &device);
    if (FAILED(result)) {
        return result;
    }
    return device->Activate(
        __uuidof(IAudioClient),
        CLSCTX_ALL,
        nullptr,
        reinterpret_cast<void**>(audio_client.ReleaseAndGetAddressOf()));
}

[[nodiscard]] HRESULT ActivateProcessLoopback(
    std::uint32_t process_id,
    ComPtr<IAudioClient>& audio_client) {
    AUDIOCLIENT_ACTIVATION_PARAMS parameters{};
    parameters.ActivationType = AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK;
    parameters.ProcessLoopbackParams.TargetProcessId = process_id;
    parameters.ProcessLoopbackParams.ProcessLoopbackMode =
        PROCESS_LOOPBACK_MODE_INCLUDE_TARGET_PROCESS_TREE;

    PROPVARIANT activation_parameters{};
    activation_parameters.vt = VT_BLOB;
    activation_parameters.blob.cbSize = sizeof(parameters);
    activation_parameters.blob.pBlobData = reinterpret_cast<BYTE*>(&parameters);

    ComPtr<ActivateAudioCompletionHandler> completion =
        Microsoft::WRL::Make<ActivateAudioCompletionHandler>();
    if (!completion) {
        return E_OUTOFMEMORY;
    }
    ComPtr<IActivateAudioInterfaceAsyncOperation> operation;
    HRESULT result = ActivateAudioInterfaceAsync(
        VIRTUAL_AUDIO_DEVICE_PROCESS_LOOPBACK,
        __uuidof(IAudioClient),
        &activation_parameters,
        completion.Get(),
        &operation);
    if (FAILED(result)) {
        return result;
    }
    return completion->Wait(audio_client);
}

[[nodiscard]] HRESULT InitializeCaptureClient(
    ComPtr<IAudioClient>& audio_client,
    DWORD stream_flags,
    CaptureSession& session) {
    WAVEFORMATEXTENSIBLE format = BuildBusWaveFormat();
    HRESULT result = audio_client->Initialize(
        AUDCLNT_SHAREMODE_SHARED,
        stream_flags | AUDCLNT_STREAMFLAGS_EVENTCALLBACK |
            AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM |
            AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY,
        0,
        0,
        &format.Format,
        nullptr);
    if (FAILED(result)) {
        return result;
    }
    session.sample_event.Reset(CreateEventW(nullptr, FALSE, FALSE, nullptr));
    if (!session.sample_event) {
        return HRESULT_FROM_WIN32(GetLastError());
    }
    result = audio_client->SetEventHandle(session.sample_event.Get());
    if (FAILED(result)) {
        return result;
    }
    result = audio_client->GetService(IID_PPV_ARGS(&session.capture_client));
    if (FAILED(result)) {
        return result;
    }
    session.audio_client = audio_client;
    return session.audio_client->Start();
}

[[nodiscard]] HRESULT CreateSourceSession(
    std::uint32_t process_id,
    CaptureSession& session) {
    session.process_handle.Reset(OpenProcess(
        SYNCHRONIZE | PROCESS_QUERY_LIMITED_INFORMATION,
        FALSE,
        process_id));
    if (!session.process_handle) {
        return HRESULT_FROM_WIN32(GetLastError());
    }
    DWORD exit_code = 0;
    if (!GetExitCodeProcess(session.process_handle.Get(), &exit_code)) {
        return HRESULT_FROM_WIN32(GetLastError());
    }
    if (exit_code != STILL_ACTIVE) {
        return HRESULT_FROM_WIN32(ERROR_PROCESS_ABORTED);
    }
    ComPtr<IAudioClient> audio_client;
    HRESULT result = ActivateProcessLoopback(process_id, audio_client);
    if (FAILED(result)) {
        return result;
    }
    return InitializeCaptureClient(
        audio_client,
        AUDCLNT_STREAMFLAGS_LOOPBACK,
        session);
}

[[nodiscard]] HRESULT CreateMicrophoneSession(
    std::wstring_view endpoint_id,
    CaptureSession& session) {
    ComPtr<IAudioClient> audio_client;
    HRESULT result = ActivateEndpointClient(endpoint_id, audio_client);
    if (FAILED(result)) {
        return result;
    }
    return InitializeCaptureClient(audio_client, 0, session);
}

[[nodiscard]] HRESULT CreateRenderSession(
    std::wstring_view endpoint_id,
    RenderSession& session) {
    HRESULT result = ActivateEndpointClient(endpoint_id, session.audio_client);
    if (FAILED(result)) {
        return result;
    }
    WAVEFORMATEXTENSIBLE format = BuildBusWaveFormat();
    result = session.audio_client->Initialize(
        AUDCLNT_SHAREMODE_SHARED,
        AUDCLNT_STREAMFLAGS_EVENTCALLBACK |
            AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM |
            AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY,
        0,
        0,
        &format.Format,
        nullptr);
    if (FAILED(result)) {
        return result;
    }
    session.sample_event.Reset(CreateEventW(nullptr, FALSE, FALSE, nullptr));
    if (!session.sample_event) {
        return HRESULT_FROM_WIN32(GetLastError());
    }
    result = session.audio_client->SetEventHandle(session.sample_event.Get());
    if (FAILED(result)) {
        return result;
    }
    result = session.audio_client->GetService(IID_PPV_ARGS(&session.render_client));
    if (FAILED(result)) {
        return result;
    }
    result = session.audio_client->GetBufferSize(&session.buffer_frames);
    if (FAILED(result)) {
        return result;
    }

    BYTE* initial_buffer = nullptr;
    result = session.render_client->GetBuffer(session.buffer_frames, &initial_buffer);
    if (FAILED(result)) {
        return result;
    }
    result = session.render_client->ReleaseBuffer(
        session.buffer_frames,
        AUDCLNT_BUFFERFLAGS_SILENT);
    if (FAILED(result)) {
        return result;
    }
    return session.audio_client->Start();
}

[[nodiscard]] std::wstring FailureContext(StreamFailure failure) {
    switch (failure) {
    case StreamFailure::Source: return L"Selected application process-loopback capture failed";
    case StreamFailure::Microphone: return L"Selected physical microphone capture failed";
    case StreamFailure::Output: return L"VB-CABLE CABLE Input render failed";
    case StreamFailure::Internal: return L"MusicMic audio worker failed";
    }
    return L"MusicMic audio worker failed";
}

}  // namespace

WAVEFORMATEXTENSIBLE BuildBusWaveFormat() noexcept {
    WAVEFORMATEXTENSIBLE format{};
    format.Format.wFormatTag = WAVE_FORMAT_EXTENSIBLE;
    format.Format.nChannels = static_cast<WORD>(kBusChannels);
    format.Format.nSamplesPerSec = static_cast<DWORD>(kBusSampleRate);
    format.Format.wBitsPerSample = 32;
    format.Format.nBlockAlign = static_cast<WORD>(
        format.Format.nChannels * format.Format.wBitsPerSample / 8U);
    format.Format.nAvgBytesPerSec =
        format.Format.nSamplesPerSec * format.Format.nBlockAlign;
    format.Format.cbSize = sizeof(WAVEFORMATEXTENSIBLE) - sizeof(WAVEFORMATEX);
    format.Samples.wValidBitsPerSample = 32;
    format.dwChannelMask = SPEAKER_FRONT_LEFT | SPEAKER_FRONT_RIGHT;
    format.SubFormat = KSDATAFORMAT_SUBTYPE_IEEE_FLOAT;
    return format;
}

MM_Result StreamFailureResult(StreamFailure failure) noexcept {
    switch (failure) {
    case StreamFailure::Source: return MM_RESULT_SOURCE_UNAVAILABLE;
    case StreamFailure::Microphone: return MM_RESULT_MICROPHONE_UNAVAILABLE;
    case StreamFailure::Output: return MM_RESULT_OUTPUT_UNAVAILABLE;
    case StreamFailure::Internal: return MM_RESULT_AUDIO_FAILURE;
    }
    return MM_RESULT_AUDIO_FAILURE;
}

bool ShouldRestartSource(
    bool injection_requested,
    bool source_healthy,
    std::uint32_t discovered_process_id) noexcept {
    // Discovery derives the selected stable ID from the executable path of this
    // current active session, so even a reused numeric PID is verified here.
    return injection_requested && !source_healthy && discovered_process_id != 0;
}

std::wstring FormatHResult(HRESULT result) {
    wchar_t* raw_message = nullptr;
    const DWORD message_length = FormatMessageW(
        FORMAT_MESSAGE_ALLOCATE_BUFFER | FORMAT_MESSAGE_FROM_SYSTEM |
            FORMAT_MESSAGE_IGNORE_INSERTS,
        nullptr,
        static_cast<DWORD>(result),
        0,
        reinterpret_cast<wchar_t*>(&raw_message),
        0,
        nullptr);
    std::wstring message = message_length > 0 && raw_message != nullptr
        ? std::wstring(raw_message, message_length)
        : L"Unknown Windows audio error";
    if (raw_message != nullptr) {
        LocalFree(raw_message);
    }
    while (!message.empty() &&
           (message.back() == L'\r' || message.back() == L'\n' || message.back() == L' ')) {
        message.pop_back();
    }
    wchar_t code[16]{};
    _snwprintf_s(code, _TRUNCATE, L"0x%08X", static_cast<unsigned int>(result));
    return message + L" (" + code + L")";
}

class WasapiStreams::Impl final {
public:
    Impl()
        : source_queue_(kQueueCapacityFrames),
          microphone_queue_(kQueueCapacityFrames) {}

    [[nodiscard]] MM_Result Start(
        std::uint32_t process_id,
        std::wstring microphone_endpoint_id,
        std::wstring output_endpoint_id,
        float source_gain,
        float microphone_gain,
        std::wstring& error_message) {
        if (running_.load()) {
            error_message.clear();
            return MM_RESULT_OK;
        }
        if (process_id == 0 || microphone_endpoint_id.empty() || output_endpoint_id.empty()) {
            error_message = L"A live source process, physical microphone, and CABLE Input are required.";
            return MM_RESULT_INVALID_ARGUMENT;
        }

        Stop();
        process_id_ = process_id;
        microphone_endpoint_id_ = std::move(microphone_endpoint_id);
        output_endpoint_id_ = std::move(output_endpoint_id);
        source_gain_.store(source_gain);
        microphone_gain_.store(microphone_gain);
        source_peak_.store(0.0F);
        microphone_peak_.store(0.0F);
        output_peak_.store(0.0F);
        source_healthy_.store(false);
        microphone_healthy_.store(false);
        output_healthy_.store(false);
        failure_result_.store(S_OK);
        {
            std::scoped_lock lock(error_mutex_);
            error_message_.clear();
        }
        {
            std::scoped_lock lock(source_queue_mutex_, microphone_queue_mutex_);
            source_queue_.Clear();
            microphone_queue_.Clear();
        }

        stop_event_.Reset(CreateEventW(nullptr, TRUE, FALSE, nullptr));
        if (!stop_event_) {
            const HRESULT result = HRESULT_FROM_WIN32(GetLastError());
            RecordFailure(StreamFailure::Internal, result);
            error_message = Snapshot().error_message;
            return MM_RESULT_AUDIO_FAILURE;
        }

        std::promise<HRESULT> source_started;
        std::promise<HRESULT> microphone_started;
        std::promise<HRESULT> output_started;
        std::future<HRESULT> source_result = source_started.get_future();
        std::future<HRESULT> microphone_result = microphone_started.get_future();
        std::future<HRESULT> output_result = output_started.get_future();

        try {
            source_thread_ = std::thread(
                &Impl::RunCapture,
                this,
                StreamFailure::Source,
                std::move(source_started));
            microphone_thread_ = std::thread(
                &Impl::RunCapture,
                this,
                StreamFailure::Microphone,
                std::move(microphone_started));
            output_thread_ = std::thread(&Impl::RunRender, this, std::move(output_started));
        } catch (...) {
            RecordFailure(StreamFailure::Internal, E_OUTOFMEMORY);
            error_message = Snapshot().error_message;
            StopThreads();
            return MM_RESULT_INTERNAL_ERROR;
        }

        const HRESULT source_start_result = source_result.get();
        const HRESULT microphone_start_result = microphone_result.get();
        const HRESULT output_start_result = output_result.get();
        if (FAILED(source_start_result) || FAILED(microphone_start_result) ||
            FAILED(output_start_result)) {
            StreamFailure failure = StreamFailure::Source;
            HRESULT result = source_start_result;
            if (SUCCEEDED(result)) {
                failure = StreamFailure::Microphone;
                result = microphone_start_result;
            }
            if (SUCCEEDED(result)) {
                failure = StreamFailure::Output;
                result = output_start_result;
            }
            RecordFailure(failure, result);
            error_message = Snapshot().error_message;
            StopThreads();
            return StreamFailureResult(failure);
        }

        running_.store(true);
        error_message.clear();
        return MM_RESULT_OK;
    }

    void Stop() noexcept {
        StopThreads();
        source_healthy_.store(false);
        microphone_healthy_.store(false);
        output_healthy_.store(false);
        source_peak_.store(0.0F);
        microphone_peak_.store(0.0F);
        output_peak_.store(0.0F);
        std::scoped_lock lock(source_queue_mutex_, microphone_queue_mutex_);
        source_queue_.Clear();
        microphone_queue_.Clear();
    }

    void SetGains(float source_gain, float microphone_gain) noexcept {
        source_gain_.store(source_gain);
        microphone_gain_.store(microphone_gain);
    }

    [[nodiscard]] WasapiStreamSnapshot Snapshot() const {
        WasapiStreamSnapshot snapshot{
            running_.load(),
            source_healthy_.load(),
            microphone_healthy_.load(),
            output_healthy_.load(),
            source_peak_.load(),
            microphone_peak_.load(),
            output_peak_.load(),
            static_cast<StreamFailure>(failure_.load()),
            failure_result_.load(),
            {}};
        std::scoped_lock lock(error_mutex_);
        snapshot.error_message = error_message_;
        return snapshot;
    }

private:
    void StopThreads() noexcept {
        running_.store(false);
        if (stop_event_) {
            SetEvent(stop_event_.Get());
        }
        if (source_thread_.joinable()) {
            source_thread_.join();
        }
        if (microphone_thread_.joinable()) {
            microphone_thread_.join();
        }
        if (output_thread_.joinable()) {
            output_thread_.join();
        }
        stop_event_.Reset();
    }

    void RunCapture(StreamFailure failure, std::promise<HRESULT> startup) noexcept {
        bool startup_pending = true;
        try {
            ComApartment apartment;
            HRESULT result = apartment.Result();
            if (FAILED(result)) {
                startup.set_value(result);
                RecordFailure(failure, result);
                return;
            }
            MmcssRegistration mmcss(L"Audio");
            std::size_t retry_attempt = 0;
            while (!StopRequested()) {
                CaptureSession session;
                result = failure == StreamFailure::Source
                    ? CreateSourceSession(process_id_, session)
                    : CreateMicrophoneSession(microphone_endpoint_id_, session);
                if (startup_pending) {
                    startup.set_value(result);
                    startup_pending = false;
                    if (FAILED(result)) {
                        RecordFailure(failure, result);
                        return;
                    }
                }
                if (FAILED(result)) {
                    RecordFailure(failure, result);
                    if (WaitForRetry(retry_attempt++)) {
                        break;
                    }
                    continue;
                }

                SetHealthy(failure, true);
                retry_attempt = 0;
                result = CaptureUntilStopped(failure, session);
                session.audio_client->Stop();
                if (StopRequested()) {
                    break;
                }
                SetHealthy(failure, false);
                RecordFailure(failure, result);
                const WorkerRecoveryDecision recovery = DecideWorkerRecovery(
                    source_healthy_.load(),
                    microphone_healthy_.load(),
                    output_healthy_.load());
                if (recovery.stop_injection) {
                    SetEvent(stop_event_.Get());
                    break;
                }
                // A PID is only a transient locator. Discovery must re-identify the
                // selected executable before a replacement process is captured.
                if (failure == StreamFailure::Source) {
                    break;
                }
                if (!recovery.retry_microphone) {
                    break;
                }
                if (WaitForRetry(retry_attempt++)) {
                    break;
                }
            }
        } catch (...) {
            if (startup_pending) {
                startup.set_value(E_OUTOFMEMORY);
            }
            RecordFailure(failure, E_OUTOFMEMORY);
        }
        SetHealthy(failure, false);
    }

    [[nodiscard]] HRESULT CaptureUntilStopped(
        StreamFailure failure,
        CaptureSession& session) {
        std::vector<float> silence;
        HANDLE wait_handles[3]{
            stop_event_.Get(),
            session.sample_event.Get(),
            session.process_handle.Get()};
        const DWORD handle_count = failure == StreamFailure::Source ? 3U : 2U;
        while (!StopRequested()) {
            const DWORD wait_result = WaitForMultipleObjects(
                handle_count,
                wait_handles,
                FALSE,
                INFINITE);
            if (wait_result == WAIT_OBJECT_0) {
                return S_OK;
            }
            if (wait_result == WAIT_OBJECT_0 + 2U && failure == StreamFailure::Source) {
                return HRESULT_FROM_WIN32(ERROR_PROCESS_ABORTED);
            }
            if (wait_result != WAIT_OBJECT_0 + 1U) {
                return HRESULT_FROM_WIN32(GetLastError());
            }
            const HRESULT result = DrainCapture(failure, session.capture_client.Get(), silence);
            if (FAILED(result)) {
                return result;
            }
        }
        return S_OK;
    }

    [[nodiscard]] HRESULT DrainCapture(
        StreamFailure failure,
        IAudioCaptureClient* capture_client,
        std::vector<float>& silence) {
        for (;;) {
            UINT32 packet_frames = 0;
            HRESULT result = capture_client->GetNextPacketSize(&packet_frames);
            if (FAILED(result) || packet_frames == 0) {
                return result;
            }

            BYTE* data = nullptr;
            DWORD flags = 0;
            UINT32 frames = 0;
            result = capture_client->GetBuffer(&data, &frames, &flags, nullptr, nullptr);
            if (FAILED(result)) {
                return result;
            }

            const std::size_t sample_count = static_cast<std::size_t>(frames) * kBusChannels;
            std::span<const float> samples;
            if ((flags & AUDCLNT_BUFFERFLAGS_SILENT) != 0 || data == nullptr) {
                silence.assign(sample_count, 0.0F);
                samples = silence;
            } else {
                samples = std::span(
                    reinterpret_cast<const float*>(data),
                    sample_count);
            }
            PushCapture(failure, samples);
            const HRESULT release_result = capture_client->ReleaseBuffer(frames);
            if (FAILED(release_result)) {
                return release_result;
            }
        }
    }

    void PushCapture(StreamFailure failure, std::span<const float> samples) {
        const float peak = PeakMagnitude(samples);
        if (failure == StreamFailure::Source) {
            std::scoped_lock lock(source_queue_mutex_);
            source_queue_.Push(samples);
            source_peak_.store(peak);
        } else {
            std::scoped_lock lock(microphone_queue_mutex_);
            microphone_queue_.Push(samples);
            microphone_peak_.store(peak);
        }
    }

    void RunRender(std::promise<HRESULT> startup) noexcept {
        bool startup_pending = true;
        try {
            ComApartment apartment;
            HRESULT result = apartment.Result();
            if (SUCCEEDED(result)) {
                MmcssRegistration mmcss(L"Pro Audio");
                RenderSession session;
                result = CreateRenderSession(output_endpoint_id_, session);
                startup.set_value(result);
                startup_pending = false;
                if (SUCCEEDED(result)) {
                    output_healthy_.store(true);
                    result = RenderUntilStopped(session);
                    session.audio_client->Stop();
                }
            } else {
                startup.set_value(result);
                startup_pending = false;
            }
            if (FAILED(result)) {
                RecordFailure(StreamFailure::Output, result);
                if (stop_event_) {
                    SetEvent(stop_event_.Get());
                }
            }
        } catch (...) {
            if (startup_pending) {
                startup.set_value(E_OUTOFMEMORY);
            }
            RecordFailure(StreamFailure::Output, E_OUTOFMEMORY);
            if (stop_event_) {
                SetEvent(stop_event_.Get());
            }
        }
        output_healthy_.store(false);
        running_.store(false);
    }

    [[nodiscard]] HRESULT RenderUntilStopped(RenderSession& session) {
        std::vector<float> source;
        std::vector<float> microphone;
        std::vector<float> output;
        HANDLE wait_handles[2]{stop_event_.Get(), session.sample_event.Get()};
        while (!StopRequested()) {
            const DWORD wait_result = WaitForMultipleObjects(2, wait_handles, FALSE, INFINITE);
            if (wait_result == WAIT_OBJECT_0) {
                return S_OK;
            }
            if (wait_result != WAIT_OBJECT_0 + 1U) {
                return HRESULT_FROM_WIN32(GetLastError());
            }

            UINT32 padding_frames = 0;
            HRESULT result = session.audio_client->GetCurrentPadding(&padding_frames);
            if (FAILED(result)) {
                return result;
            }
            const UINT32 available_frames = session.buffer_frames - padding_frames;
            if (available_frames == 0) {
                continue;
            }
            const std::size_t sample_count =
                static_cast<std::size_t>(available_frames) * kBusChannels;
            source.resize(sample_count);
            microphone.resize(sample_count);
            output.resize(sample_count);
            {
                std::scoped_lock lock(source_queue_mutex_);
                (void)source_queue_.Pop(source);
            }
            {
                std::scoped_lock lock(microphone_queue_mutex_);
                (void)microphone_queue_.Pop(microphone);
            }
            MixStereo(
                source,
                microphone,
                source_gain_.load(),
                microphone_gain_.load(),
                output);

            BYTE* render_buffer = nullptr;
            result = session.render_client->GetBuffer(available_frames, &render_buffer);
            if (FAILED(result)) {
                return result;
            }
            std::memcpy(render_buffer, output.data(), output.size() * sizeof(float));
            result = session.render_client->ReleaseBuffer(available_frames, 0);
            if (FAILED(result)) {
                return result;
            }
            output_peak_.store(PeakMagnitude(output));
        }
        return S_OK;
    }

    [[nodiscard]] bool StopRequested() const noexcept {
        return stop_event_ && WaitForSingleObject(stop_event_.Get(), 0) == WAIT_OBJECT_0;
    }

    [[nodiscard]] bool WaitForRetry(std::size_t attempt) const noexcept {
        return !stop_event_ ||
            WaitForSingleObject(
                stop_event_.Get(),
                static_cast<DWORD>(RetryDelay(attempt).count())) == WAIT_OBJECT_0;
    }

    void SetHealthy(StreamFailure failure, bool healthy) noexcept {
        if (failure == StreamFailure::Source) {
            source_healthy_.store(healthy);
            if (!healthy) {
                source_peak_.store(0.0F);
            }
        } else if (failure == StreamFailure::Microphone) {
            microphone_healthy_.store(healthy);
            if (!healthy) {
                microphone_peak_.store(0.0F);
            }
        }
    }

    void RecordFailure(StreamFailure failure, HRESULT result) noexcept {
        failure_.store(static_cast<int>(failure));
        failure_result_.store(result);
        try {
            std::wstring message = FailureContext(failure) + L": " + FormatHResult(result);
            std::scoped_lock lock(error_mutex_);
            error_message_ = std::move(message);
        } catch (...) {
            std::scoped_lock lock(error_mutex_);
            error_message_ = L"Windows audio worker failed and the error text could not be formatted.";
        }
    }

    std::uint32_t process_id_{};
    std::wstring microphone_endpoint_id_;
    std::wstring output_endpoint_id_;

    UniqueHandle stop_event_;
    std::thread source_thread_;
    std::thread microphone_thread_;
    std::thread output_thread_;

    mutable std::mutex source_queue_mutex_;
    mutable std::mutex microphone_queue_mutex_;
    StereoFrameQueue source_queue_;
    StereoFrameQueue microphone_queue_;

    std::atomic<float> source_gain_{0.7F};
    std::atomic<float> microphone_gain_{1.0F};
    std::atomic<float> source_peak_{0.0F};
    std::atomic<float> microphone_peak_{0.0F};
    std::atomic<float> output_peak_{0.0F};
    std::atomic<bool> running_{false};
    std::atomic<bool> source_healthy_{false};
    std::atomic<bool> microphone_healthy_{false};
    std::atomic<bool> output_healthy_{false};
    std::atomic<int> failure_{static_cast<int>(StreamFailure::Internal)};
    std::atomic<HRESULT> failure_result_{S_OK};
    mutable std::mutex error_mutex_;
    std::wstring error_message_;
};

WasapiStreams::WasapiStreams() : impl_(std::make_unique<Impl>()) {}

WasapiStreams::~WasapiStreams() {
    Stop();
}

MM_Result WasapiStreams::Start(
    std::uint32_t process_id,
    std::wstring microphone_endpoint_id,
    std::wstring output_endpoint_id,
    float source_gain,
    float microphone_gain,
    std::wstring& error_message) {
    return impl_->Start(
        process_id,
        std::move(microphone_endpoint_id),
        std::move(output_endpoint_id),
        source_gain,
        microphone_gain,
        error_message);
}

void WasapiStreams::Stop() noexcept {
    impl_->Stop();
}

void WasapiStreams::SetGains(float source_gain, float microphone_gain) noexcept {
    impl_->SetGains(source_gain, microphone_gain);
}

WasapiStreamSnapshot WasapiStreams::Snapshot() const {
    return impl_->Snapshot();
}

}  // namespace musicmic

#pragma once

#include <stdint.h>
#include <wchar.h>

#if defined(_WIN32) && defined(MUSICMIC_AUDIO_EXPORTS)
#define MM_API __declspec(dllexport)
#elif defined(_WIN32)
#define MM_API __declspec(dllimport)
#define MM_CALL __cdecl
#else
#define MM_API
#define MM_CALL
#endif

#if !defined(MM_CALL)
#define MM_CALL __cdecl
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef enum MM_Result {
    MM_RESULT_OK = 0,
    MM_RESULT_NOT_INITIALIZED = 1,
    MM_RESULT_INVALID_ARGUMENT = 2,
    MM_RESULT_BUFFER_TOO_SMALL = 3,
    MM_RESULT_NOT_FOUND = 4,
    MM_RESULT_OUTPUT_UNAVAILABLE = 5,
    MM_RESULT_AUDIO_FAILURE = 6,
    MM_RESULT_INTERNAL_ERROR = 7,
    MM_RESULT_SOURCE_UNAVAILABLE = 8,
    MM_RESULT_MICROPHONE_UNAVAILABLE = 9
} MM_Result;

typedef enum MM_State {
    MM_STATE_INITIALIZING = 0,
    MM_STATE_READY = 1,
    MM_STATE_INJECTING = 2,
    MM_STATE_SOURCE_UNAVAILABLE = 3,
    MM_STATE_MICROPHONE_UNAVAILABLE = 4,
    MM_STATE_OUTPUT_UNAVAILABLE = 5,
    MM_STATE_ERROR = 6
} MM_State;

typedef struct MM_Status {
    MM_State state;
    int32_t source_available;
    int32_t microphone_available;
    int32_t output_available;
    int32_t injection_requested;
    float source_peak;
    float microphone_peak;
    float output_peak;
} MM_Status;

enum {
    MM_DEVICE_ID_CAPACITY = 512,
    MM_DISPLAY_NAME_CAPACITY = 128
};

typedef struct MM_SourceInfo {
    wchar_t id[MM_DEVICE_ID_CAPACITY];
    wchar_t display_name[MM_DISPLAY_NAME_CAPACITY];
    uint32_t process_id;
    int32_t is_spotify;
} MM_SourceInfo;

typedef struct MM_MicrophoneInfo {
    wchar_t id[MM_DEVICE_ID_CAPACITY];
    wchar_t display_name[MM_DISPLAY_NAME_CAPACITY];
    int32_t is_default;
} MM_MicrophoneInfo;

MM_API MM_Result MM_CALL MM_Initialize(void);
MM_API MM_Result MM_CALL MM_Shutdown(void);
MM_API MM_Result MM_CALL MM_RefreshDevices(void);
MM_API MM_Result MM_CALL MM_GetSourceCount(uint32_t* count);
MM_API MM_Result MM_CALL MM_GetSourceInfo(uint32_t index, MM_SourceInfo* info);
MM_API MM_Result MM_CALL MM_GetMicrophoneCount(uint32_t* count);
MM_API MM_Result MM_CALL MM_GetMicrophoneInfo(uint32_t index, MM_MicrophoneInfo* info);
MM_API MM_Result MM_CALL MM_SelectSource(const wchar_t* source_id);
MM_API MM_Result MM_CALL MM_SelectMicrophone(const wchar_t* microphone_id);
MM_API MM_Result MM_CALL MM_SetSourceGain(float normalized_gain);
MM_API MM_Result MM_CALL MM_SetMicrophoneGain(float normalized_gain);
MM_API MM_Result MM_CALL MM_StartInjection(void);
MM_API MM_Result MM_CALL MM_StopInjection(void);
MM_API MM_Result MM_CALL MM_HandleSystemResume(void);
MM_API MM_Result MM_CALL MM_GetStatus(MM_Status* status);
MM_API MM_Result MM_CALL MM_GetLastError(wchar_t* buffer, uint32_t buffer_length, uint32_t* required_length);

#ifdef __cplusplus
}
#endif

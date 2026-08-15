#pragma once

#include <stdint.h>

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
    MM_RESULT_INTERNAL_ERROR = 7
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
} MM_Status;

MM_API MM_Result MM_CALL MM_Initialize(void);
MM_API MM_Result MM_CALL MM_Shutdown(void);
MM_API MM_Result MM_CALL MM_RefreshDevices(void);
MM_API MM_Result MM_CALL MM_StartInjection(void);
MM_API MM_Result MM_CALL MM_StopInjection(void);
MM_API MM_Result MM_CALL MM_GetStatus(MM_Status* status);
MM_API MM_Result MM_CALL MM_GetLastError(wchar_t* buffer, uint32_t buffer_length, uint32_t* required_length);

#ifdef __cplusplus
}
#endif

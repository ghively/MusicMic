using System.Runtime.InteropServices;
using System.Text;

namespace MusicMic.App.Services;

/// <summary>
/// Direct P/Invoke implementation for MusicMic.Audio.dll. The DLL is loaded from the app base
/// directory by the normal Windows loader; the project copies the x64 native output beside it.
/// </summary>
public sealed class NativeAudioApi : INativeAudioApi
{
    private const string LibraryName = "MusicMic.Audio";
    private const int DeviceIdCapacity = 512;
    private const int DisplayNameCapacity = 128;
    private string? interopError;

    public NativeAudioResult Initialize() => Invoke(NativeMethods.MM_Initialize);

    public NativeAudioResult Shutdown() => Invoke(NativeMethods.MM_Shutdown);

    public NativeAudioResult RefreshDevices() => Invoke(NativeMethods.MM_RefreshDevices);

    public NativeAudioResult GetSourceCount(out uint count)
    {
        uint nativeCount = 0;
        NativeAudioResult result = Invoke(() => NativeMethods.MM_GetSourceCount(out nativeCount));
        count = nativeCount;
        return result;
    }

    public NativeAudioResult GetSourceInfo(uint index, out NativeAudioSource source)
    {
        NativeAudioSourceNative nativeSource = default;
        NativeAudioResult result = Invoke(() => NativeMethods.MM_GetSourceInfo(index, out nativeSource));
        source = new NativeAudioSource(
            nativeSource.Id ?? string.Empty,
            nativeSource.DisplayName ?? string.Empty,
            nativeSource.ProcessId,
            nativeSource.IsSpotify != 0);
        return result;
    }

    public NativeAudioResult GetMicrophoneCount(out uint count)
    {
        uint nativeCount = 0;
        NativeAudioResult result = Invoke(() => NativeMethods.MM_GetMicrophoneCount(out nativeCount));
        count = nativeCount;
        return result;
    }

    public NativeAudioResult GetMicrophoneInfo(uint index, out NativeAudioMicrophone microphone)
    {
        NativeAudioMicrophoneNative nativeMicrophone = default;
        NativeAudioResult result = Invoke(() => NativeMethods.MM_GetMicrophoneInfo(index, out nativeMicrophone));
        microphone = new NativeAudioMicrophone(
            nativeMicrophone.Id ?? string.Empty,
            nativeMicrophone.DisplayName ?? string.Empty,
            nativeMicrophone.IsDefault != 0);
        return result;
    }

    public NativeAudioResult SelectSource(string sourceId) =>
        Invoke(() => NativeMethods.MM_SelectSource(sourceId ?? string.Empty));

    public NativeAudioResult SelectMicrophone(string microphoneId) =>
        Invoke(() => NativeMethods.MM_SelectMicrophone(microphoneId ?? string.Empty));

    public NativeAudioResult SetSourceGain(float gain) => Invoke(() => NativeMethods.MM_SetSourceGain(gain));

    public NativeAudioResult SetMicrophoneGain(float gain) => Invoke(() => NativeMethods.MM_SetMicrophoneGain(gain));

    public NativeAudioResult StartInjection() => Invoke(NativeMethods.MM_StartInjection);

    public NativeAudioResult StopInjection() => Invoke(NativeMethods.MM_StopInjection);

    public NativeAudioResult HandleSystemResume() => Invoke(NativeMethods.MM_HandleSystemResume);

    public NativeAudioResult GetStatus(out NativeAudioStatus status)
    {
        // The callback can translate a native-load exception into an MM result before the
        // P/Invoke assigns its out structure, so begin from a harmless zeroed ABI value.
        NativeAudioStatusNative nativeStatus = default;
        NativeAudioResult result = Invoke(() => NativeMethods.MM_GetStatus(out nativeStatus));
        status = new NativeAudioStatus(
            nativeStatus.State,
            nativeStatus.SourceAvailable != 0,
            nativeStatus.MicrophoneAvailable != 0,
            nativeStatus.OutputAvailable != 0,
            nativeStatus.InjectionRequested != 0,
            nativeStatus.SourcePeak,
            nativeStatus.MicrophonePeak,
            nativeStatus.OutputPeak);
        return result;
    }

    public string GetLastError()
    {
        if (!string.IsNullOrWhiteSpace(interopError))
        {
            return interopError;
        }

        uint requiredLength = 0;
        NativeAudioResult query = Invoke(() => NativeMethods.MM_GetLastError(IntPtr.Zero, 0, out requiredLength));
        if (query is not NativeAudioResult.BufferTooSmall || requiredLength == 0)
        {
            return string.Empty;
        }

        var buffer = new StringBuilder(checked((int)requiredLength));
        NativeAudioResult result = Invoke(() => NativeMethods.MM_GetLastError(buffer, requiredLength, out _));
        return result == NativeAudioResult.Ok ? buffer.ToString() : string.Empty;
    }

    private NativeAudioResult Invoke(Func<NativeAudioResult> operation)
    {
        try
        {
            interopError = null;
            return operation();
        }
        catch (DllNotFoundException exception)
        {
            interopError = $"MusicMic.Audio.dll could not be loaded: {exception.Message}";
        }
        catch (BadImageFormatException exception)
        {
            interopError = $"MusicMic.Audio.dll is not a compatible x64 binary: {exception.Message}";
        }
        catch (EntryPointNotFoundException exception)
        {
            interopError = $"MusicMic.Audio.dll does not provide the required audio ABI: {exception.Message}";
        }

        return NativeAudioResult.InternalError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeAudioStatusNative
    {
        public NativeAudioState State;
        public int SourceAvailable;
        public int MicrophoneAvailable;
        public int OutputAvailable;
        public int InjectionRequested;
        public float SourcePeak;
        public float MicrophonePeak;
        public float OutputPeak;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeAudioSourceNative
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = DeviceIdCapacity)]
        public string? Id;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = DisplayNameCapacity)]
        public string? DisplayName;

        public uint ProcessId;
        public int IsSpotify;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeAudioMicrophoneNative
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = DeviceIdCapacity)]
        public string? Id;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = DisplayNameCapacity)]
        public string? DisplayName;

        public int IsDefault;
    }

    private static class NativeMethods
    {
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern NativeAudioResult MM_Initialize();

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern NativeAudioResult MM_Shutdown();

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern NativeAudioResult MM_RefreshDevices();

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern NativeAudioResult MM_GetSourceCount(out uint count);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode, ExactSpelling = true)]
        internal static extern NativeAudioResult MM_GetSourceInfo(uint index, out NativeAudioSourceNative source);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern NativeAudioResult MM_GetMicrophoneCount(out uint count);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode, ExactSpelling = true)]
        internal static extern NativeAudioResult MM_GetMicrophoneInfo(uint index, out NativeAudioMicrophoneNative microphone);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode, ExactSpelling = true)]
        internal static extern NativeAudioResult MM_SelectSource(string sourceId);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode, ExactSpelling = true)]
        internal static extern NativeAudioResult MM_SelectMicrophone(string microphoneId);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern NativeAudioResult MM_SetSourceGain(float normalizedGain);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern NativeAudioResult MM_SetMicrophoneGain(float normalizedGain);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern NativeAudioResult MM_StartInjection();

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern NativeAudioResult MM_StopInjection();

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern NativeAudioResult MM_HandleSystemResume();

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern NativeAudioResult MM_GetStatus(out NativeAudioStatusNative status);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode, ExactSpelling = true)]
        internal static extern NativeAudioResult MM_GetLastError(IntPtr buffer, uint bufferLength, out uint requiredLength);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode, ExactSpelling = true)]
        internal static extern NativeAudioResult MM_GetLastError(StringBuilder buffer, uint bufferLength, out uint requiredLength);
    }
}

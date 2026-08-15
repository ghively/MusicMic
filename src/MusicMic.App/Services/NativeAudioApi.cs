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

    public NativeAudioResult Initialize() => NativeMethods.MM_Initialize();

    public NativeAudioResult Shutdown() => NativeMethods.MM_Shutdown();

    public NativeAudioResult RefreshDevices() => NativeMethods.MM_RefreshDevices();

    public NativeAudioResult StartInjection() => NativeMethods.MM_StartInjection();

    public NativeAudioResult StopInjection() => NativeMethods.MM_StopInjection();

    public NativeAudioResult GetStatus(out NativeAudioStatus status)
    {
        NativeAudioStatusNative nativeStatus;
        NativeAudioResult result = NativeMethods.MM_GetStatus(out nativeStatus);
        status = new NativeAudioStatus(
            nativeStatus.State,
            nativeStatus.SourceAvailable != 0,
            nativeStatus.MicrophoneAvailable != 0,
            nativeStatus.OutputAvailable != 0,
            nativeStatus.InjectionRequested != 0,
            nativeStatus.SourcePeak,
            nativeStatus.MicrophonePeak);
        return result;
    }

    public string GetLastError()
    {
        NativeAudioResult query = NativeMethods.MM_GetLastError(IntPtr.Zero, 0, out uint requiredLength);
        if (query is not NativeAudioResult.BufferTooSmall || requiredLength == 0)
        {
            return string.Empty;
        }

        var buffer = new StringBuilder(checked((int)requiredLength));
        NativeAudioResult result = NativeMethods.MM_GetLastError(buffer, requiredLength, out _);
        return result == NativeAudioResult.Ok ? buffer.ToString() : string.Empty;
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
        internal static extern NativeAudioResult MM_StartInjection();

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern NativeAudioResult MM_StopInjection();

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern NativeAudioResult MM_GetStatus(out NativeAudioStatusNative status);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode, ExactSpelling = true)]
        internal static extern NativeAudioResult MM_GetLastError(IntPtr buffer, uint bufferLength, out uint requiredLength);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode, ExactSpelling = true)]
        internal static extern NativeAudioResult MM_GetLastError(StringBuilder buffer, uint bufferLength, out uint requiredLength);
    }
}

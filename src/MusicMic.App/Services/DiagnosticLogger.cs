using System.Globalization;
using System.IO;

namespace MusicMic.App.Services;

public interface IDiagnosticLogger : IDisposable
{
    void Write(string eventName, string message);
}

public sealed class DiagnosticLogger : IDiagnosticLogger
{
    private const long DefaultMaximumFileBytes = 512 * 1024;
    private const int DefaultRetainedFileCount = 5;
    private readonly object sync = new();
    private readonly string directory;
    private readonly long maximumFileBytes;
    private readonly int retainedFileCount;
    private bool disposed;

    public DiagnosticLogger(
        string directory,
        long maximumFileBytes = DefaultMaximumFileBytes,
        int retainedFileCount = DefaultRetainedFileCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumFileBytes);
        ArgumentOutOfRangeException.ThrowIfLessThan(retainedFileCount, 2);
        this.directory = directory;
        this.maximumFileBytes = maximumFileBytes;
        this.retainedFileCount = retainedFileCount;
    }

    public static DiagnosticLogger CreateDefault() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MusicMic",
        "logs"));

    public void Write(string eventName, string message)
    {
        if (string.IsNullOrWhiteSpace(eventName))
        {
            return;
        }

        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            try
            {
                Directory.CreateDirectory(directory);
                string currentPath = Path.Combine(directory, "MusicMic.log");
                string line = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{DateTimeOffset.Now:O} [{Sanitize(eventName)}] {Sanitize(message)}{Environment.NewLine}");
                RotateIfNeeded(currentPath, line.Length * sizeof(char));
                File.AppendAllText(currentPath, line);
            }
            catch (IOException)
            {
                // Diagnostics must never interrupt the audio path or application shutdown.
            }
            catch (UnauthorizedAccessException)
            {
                // Continue without diagnostics when the profile directory is unavailable.
            }
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            disposed = true;
        }
    }

    private void RotateIfNeeded(string currentPath, int incomingBytes)
    {
        if (!File.Exists(currentPath) || new FileInfo(currentPath).Length + incomingBytes <= maximumFileBytes)
        {
            return;
        }

        string oldest = Path.Combine(directory, $"MusicMic.{retainedFileCount - 1}.log");
        if (File.Exists(oldest))
        {
            File.Delete(oldest);
        }

        for (int index = retainedFileCount - 2; index >= 1; index--)
        {
            string source = Path.Combine(directory, $"MusicMic.{index}.log");
            string destination = Path.Combine(directory, $"MusicMic.{index + 1}.log");
            if (File.Exists(source))
            {
                File.Move(source, destination, overwrite: true);
            }
        }

        File.Move(currentPath, Path.Combine(directory, "MusicMic.1.log"), overwrite: true);
    }

    private static string Sanitize(string? value) =>
        (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ');
}

public sealed class NullDiagnosticLogger : IDiagnosticLogger
{
    public static NullDiagnosticLogger Instance { get; } = new();

    private NullDiagnosticLogger() { }

    public void Write(string eventName, string message) { }

    public void Dispose() { }
}

using Microsoft.Win32;

namespace MusicMic.App.Services;

public interface IStartupRegistrationStore
{
    string? Read();

    void Write(string command);

    void Delete();
}

public sealed class StartupService : IStartupService
{
    private readonly IStartupRegistrationStore store;
    private readonly string executablePath;

    public StartupService()
        : this(
            new WindowsStartupRegistrationStore(),
            Environment.ProcessPath
                ?? throw new InvalidOperationException("MusicMic could not determine its executable path."))
    {
    }

    public StartupService(IStartupRegistrationStore store, string executablePath)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        this.executablePath = executablePath;
    }

    public bool IsEnabled => string.Equals(
        store.Read(),
        CreateCommand(),
        StringComparison.OrdinalIgnoreCase);

    public void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            store.Write(CreateCommand());
        }
        else
        {
            store.Delete();
        }
    }

    private string CreateCommand() => $"\"{executablePath}\"";
}

public sealed class WindowsStartupRegistrationStore : IStartupRegistrationStore
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "MusicMic";

    public string? Read()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(ValueName) as string;
    }

    public void Write(string command)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("MusicMic could not open the Windows startup registry key.");
        key.SetValue(ValueName, command, RegistryValueKind.String);
    }

    public void Delete()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}

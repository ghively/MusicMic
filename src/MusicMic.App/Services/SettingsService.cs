using MusicMic.Core;
using System.IO;
using System.Text.Json;

namespace MusicMic.App.Services;

public sealed class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private readonly string settingsPath;

    public SettingsService(string? settingsPath = null)
    {
        this.settingsPath = settingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MusicMic",
            "settings.json");
    }

    public string SettingsPath => settingsPath;

    public MusicMicSettings Load()
    {
        if (!File.Exists(settingsPath))
        {
            return MusicMicSettings.Default;
        }

        try
        {
            return JsonSerializer.Deserialize<MusicMicSettings>(File.ReadAllText(settingsPath), SerializerOptions)
                ?? MusicMicSettings.Default;
        }
        catch (JsonException)
        {
            return MusicMicSettings.Default;
        }
        catch (IOException)
        {
            return MusicMicSettings.Default;
        }
    }

    public void Save(MusicMicSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string? directory = Path.GetDirectoryName(settingsPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("The settings path must have a parent directory.");
        }

        Directory.CreateDirectory(directory);
        string temporaryPath = settingsPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, SerializerOptions));
        File.Move(temporaryPath, settingsPath, overwrite: true);
    }
}

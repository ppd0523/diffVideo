using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using DiffVideo.Core;

namespace DiffVideo.App.Services;

internal sealed record UserSettings
{
    public const int CurrentSchemaVersion = 1;
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public WindowUserSettings Window { get; init; } = new();
    public OutputUserSettings Output { get; init; } = new();
    public TimelineUserSettings Timeline { get; init; } = new();
    public string? LastExportDirectory { get; init; }
}

internal sealed record WindowUserSettings
{
    public double Width { get; init; } = 1440;
    public double Height { get; init; } = 900;
    public double InspectorWidth { get; init; } = 216;
    public double? Left { get; init; }
    public double? Top { get; init; }
    public bool Maximized { get; init; }
}

internal sealed record OutputUserSettings
{
    public int Width { get; init; } = 1920;
    public int Height { get; init; } = 1080;
    public int FramesPerSecond { get; init; } = 30;
    public double DurationSeconds { get; init; } = 15;
    public OutputQuality Quality { get; init; } = OutputQuality.Balanced;
}

internal sealed record TimelineUserSettings
{
    public double Height { get; init; } = 264;
    public double ZoomRatio { get; init; } = 1;
}

internal sealed class UserSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public UserSettingsStore(string? path = null)
    {
        SettingsPath = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DiffVideo", "settings.json");
    }

    public string SettingsPath { get; }

    public UserSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) { return new(); }
            var settings = JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(SettingsPath), JsonOptions);
            return settings is
            {
                SchemaVersion: UserSettings.CurrentSchemaVersion,
                Window: not null,
                Output: not null,
                Timeline: not null
            } ? settings : new();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new();
        }
    }

    public void Save(UserSettings settings)
    {
        var directory = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = SettingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(temporaryPath, SettingsPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) { File.Delete(temporaryPath); }
        }
    }
}

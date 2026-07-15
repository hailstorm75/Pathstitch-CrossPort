using System;
using System.IO;
using System.Text.Json;

namespace Pathstitch.App.Services;

public sealed record UserPreferences(
    string Appearance = "System",
    bool ReversePanDirection = false,
    bool GettingStartedDismissed = false,
    bool SupportCardDismissed = false);

public sealed class UserPreferencesStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;

    public UserPreferencesStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Pathstitch",
            "preferences.json");
    }

    public UserPreferences Load()
    {
        try
        {
            if (!File.Exists(_path))
                return new UserPreferences();

            return JsonSerializer.Deserialize<UserPreferences>(File.ReadAllText(_path))
                ?? new UserPreferences();
        }
        catch (IOException)
        {
            return new UserPreferences();
        }
        catch (JsonException)
        {
            return new UserPreferences();
        }
    }

    public void Save(UserPreferences preferences)
    {
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(_path, JsonSerializer.Serialize(preferences, JsonOptions));
        }
        catch (IOException)
        {
            // Preferences are best effort; app behavior must survive read-only profiles.
        }
    }
}

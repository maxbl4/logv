using System.IO;
using System.Text.Json;
using lgv.Highlighting;

namespace lgv.Core;

public static class SettingsStore
{
    private static readonly string _path =
        System.IO.Path.Combine(AppContext.BaseDirectory, "lgv.settings.json");

    private static readonly JsonSerializerOptions _options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private static AppSettings? _current;

    public static AppSettings Current
    {
        get
        {
            if (_current is null)
                _current = Load();
            return _current;
        }
    }

    public static AppSettings Load()
    {
        try
        {
            if (System.IO.File.Exists(_path))
            {
                var json = System.IO.File.ReadAllText(_path);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, _options);
                if (settings is not null)
                {
                    if (settings.Patterns is null || settings.Patterns.Count == 0)
                    {
                        settings.Patterns = BuiltinPatterns.GetDefaults();
                        Save(settings);
                    }
                    _current = settings;
                    return settings;
                }
            }
        }
        catch
        {
            // Fall through to defaults
        }

        var defaults = new AppSettings
        {
            Patterns = BuiltinPatterns.GetDefaults()
        };
        Save(defaults);
        _current = defaults;
        return defaults;
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, _options);
            System.IO.File.WriteAllText(_path, json);
        }
        catch
        {
            // Swallow save errors — don't crash the app
        }
    }
}

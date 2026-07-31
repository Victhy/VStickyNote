using System.IO;
using System.Text.Json;
using VSticky.Models;

namespace VSticky.Services;

public static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static string SettingsPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VSticky",
            "settings.json");

    public static AppSettings Current { get; private set; } = new();

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                Current = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            }
            else
            {
                Current = new AppSettings();
                Save();
            }
        }
        catch
        {
            Current = new AppSettings();
        }

        Normalize(Current);
        return Current;
    }

    public static void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(Current, JsonOptions);
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // ignore disk errors
        }
    }

    public static void SaveFromNote(NoteData note)
    {
        Current.UpdateFromNote(note);
        Save();
    }

    private static void Normalize(AppSettings s)
    {
        if (s.Colors is not { Count: > 0 })
        {
            s.Colors = new AppSettings().Colors;
        }

        if (s.FontChoices is not { Count: > 0 })
            s.FontChoices = new AppSettings().FontChoices;

        if (s.FontSizeChoices is not { Count: > 0 })
            s.FontSizeChoices = new AppSettings().FontSizeChoices;

        s.Opacity = Math.Clamp(s.Opacity, 0.2, 1);
        s.CornerRadius = Math.Clamp(s.CornerRadius, 0, 28);
        if (s.FontSize < 8) s.FontSize = 14;
        if (s.Width < 160) s.Width = 260;
        if (s.Height < 70) s.Height = 220;
        if (string.IsNullOrWhiteSpace(s.Language))
            s.Language = "zh-CN";

        foreach (var color in s.Colors)
        {
            if (string.IsNullOrWhiteSpace(color.Key))
                color.Key = GuessColorKey(color.Hex);
        }
    }

    private static string? GuessColorKey(string hex) => hex?.Trim().ToUpperInvariant() switch
    {
        "#C8E6C9" => "color.green",
        "#FFE0B2" => "color.orange",
        "#FFF59D" => "color.yellow",
        "#F8BBD0" => "color.pink",
        "#BBDEFB" => "color.blue",
        "#E1BEE7" => "color.purple",
        "#F5F5F5" => "color.white",
        _ => null
    };
}

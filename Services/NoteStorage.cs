using System.IO;
using System.Text.Json;
using VSticky.Models;

namespace VSticky.Services;

public static class NoteStorage
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string StoreDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VSticky");

    public static string StorePath => Path.Combine(StoreDirectory, "notes.json");

    public static bool StoreExists => File.Exists(StorePath);

    public static List<NoteData> Load()
    {
        try
        {
            if (!File.Exists(StorePath))
                return [];

            var json = File.ReadAllText(StorePath);
            return JsonSerializer.Deserialize<List<NoteData>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static void Save(IEnumerable<NoteData> notes)
    {
        Directory.CreateDirectory(StoreDirectory);
        var json = JsonSerializer.Serialize(notes.ToList(), JsonOptions);
        File.WriteAllText(StorePath, json);
    }
}

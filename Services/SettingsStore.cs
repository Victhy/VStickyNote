using System.Globalization;
using Microsoft.Data.Sqlite;
using VSticky.Models;

namespace VSticky.Services;

public static class SettingsStore
{
    public static AppSettings Current { get; private set; } = new();

    public static AppSettings Load()
    {
        try
        {
            var conn = AppDatabase.Open();
            if (conn.QueryLong("SELECT COUNT(*) FROM settings;") == 0)
            {
                Current = new AppSettings();
                Normalize(Current);
                Save();
                return Current;
            }

            Current = Read(conn);
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
            Normalize(Current);
            Write(AppDatabase.Open(), Current);
        }
        catch
        {
            // ignore disk errors
        }
    }

    public static void SaveFromNote(NoteData note)
    {
        Current.UpdateFromNote(note);
        try
        {
            Normalize(Current);
            var conn = AppDatabase.Open();
            using var tx = conn.BeginTransaction();
            try
            {
                UpsertSetting(conn, tx, "opacity", Real(Current.Opacity), "real");
                UpsertSetting(conn, tx, "corner_radius", Real(Current.CornerRadius), "real");
                UpsertSetting(conn, tx, "font_family", Current.FontFamily, "string");
                UpsertSetting(conn, tx, "font_size", Real(Current.FontSize), "real");
                UpsertSetting(conn, tx, "topmost", Bool(Current.Topmost), "bool");
                UpsertSetting(conn, tx, "width", Real(Current.Width), "real");
                UpsertSetting(conn, tx, "height", Real(Current.Height), "real");
                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }
        catch
        {
            // ignore disk errors
        }
    }

    /// <summary>旧 JSON 迁移写入，不经过 Current。</summary>
    internal static void Import(AppSettings settings)
    {
        Normalize(settings);
        Write(AppDatabase.Open(), settings);
        Current = settings;
    }

    private static AppSettings Read(SqliteConnection conn)
    {
        var s = new AppSettings();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT key, value, value_type FROM settings;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var key = reader.GetString(0);
                var value = reader.GetString(1);
                ApplyKey(s, key, value);
            }
        }

        s.Colors = ReadColors(conn);
        s.FontChoices = ReadFonts(conn);
        s.FontSizeChoices = ReadFontSizes(conn);
        return s;
    }

    private static void Write(SqliteConnection conn, AppSettings s)
    {
        using var tx = conn.BeginTransaction();
        try
        {
            UpsertSetting(conn, tx, "language", s.Language, "string");
            UpsertSetting(conn, tx, "start_with_windows", Bool(s.StartWithWindows), "bool");
            UpsertSetting(conn, tx, "hotkey_new_note_enabled", Bool(s.HotkeyNewNoteEnabled), "bool");
            UpsertSetting(conn, tx, "snap_enabled", Bool(s.SnapEnabled), "bool");
            UpsertSetting(conn, tx, "opacity", Real(s.Opacity), "real");
            UpsertSetting(conn, tx, "corner_radius", Real(s.CornerRadius), "real");
            UpsertSetting(conn, tx, "font_family", s.FontFamily, "string");
            UpsertSetting(conn, tx, "font_size", Real(s.FontSize), "real");
            UpsertSetting(conn, tx, "topmost", Bool(s.Topmost), "bool");
            UpsertSetting(conn, tx, "width", Real(s.Width), "real");
            UpsertSetting(conn, tx, "height", Real(s.Height), "real");

            ReplaceColors(conn, tx, s.Colors);
            ReplaceFonts(conn, tx, s.FontChoices);
            ReplaceFontSizes(conn, tx, s.FontSizeChoices);
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    private static void ApplyKey(AppSettings s, string key, string value)
    {
        switch (key)
        {
            case "language":
                s.Language = value;
                break;
            case "start_with_windows":
                s.StartWithWindows = ParseBool(value);
                break;
            case "hotkey_new_note_enabled":
                s.HotkeyNewNoteEnabled = ParseBool(value);
                break;
            case "snap_enabled":
                s.SnapEnabled = ParseBool(value);
                break;
            case "opacity":
                s.Opacity = ParseReal(value, s.Opacity);
                break;
            case "corner_radius":
                s.CornerRadius = ParseReal(value, s.CornerRadius);
                break;
            case "font_family":
                s.FontFamily = value;
                break;
            case "font_size":
                s.FontSize = ParseReal(value, s.FontSize);
                break;
            case "topmost":
                s.Topmost = ParseBool(value);
                break;
            case "width":
                s.Width = ParseReal(value, s.Width);
                break;
            case "height":
                s.Height = ParseReal(value, s.Height);
                break;
        }
    }

    private static List<ColorOption> ReadColors(SqliteConnection conn)
    {
        var list = new List<ColorOption>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT loc_key, name, hex
            FROM setting_colors
            ORDER BY sort_order, id;
            """;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new ColorOption
            {
                Key = reader.IsDBNull(0) ? null : reader.GetString(0),
                Name = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                Hex = reader.GetString(2)
            });
        }

        return list;
    }

    private static List<string> ReadFonts(SqliteConnection conn)
    {
        var list = new List<string>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT family_name FROM setting_fonts ORDER BY sort_order, id;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add(reader.GetString(0));
        return list;
    }

    private static List<double> ReadFontSizes(SqliteConnection conn)
    {
        var list = new List<double>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT size FROM setting_font_sizes ORDER BY sort_order, id;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add(reader.GetDouble(0));
        return list;
    }

    private static void UpsertSetting(
        SqliteConnection conn,
        SqliteTransaction tx,
        string key,
        string value,
        string type)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO settings (key, value, value_type)
            VALUES ($key, $value, $type)
            ON CONFLICT(key) DO UPDATE SET
                value = excluded.value,
                value_type = excluded.value_type;
            """;
        cmd.Parameters.AddWithValue("$key", key);
        cmd.Parameters.AddWithValue("$value", value ?? string.Empty);
        cmd.Parameters.AddWithValue("$type", type);
        cmd.ExecuteNonQuery();
    }

    private static void ReplaceColors(SqliteConnection conn, SqliteTransaction tx, List<ColorOption> colors)
    {
        using (var del = conn.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM setting_colors;";
            del.ExecuteNonQuery();
        }

        for (var i = 0; i < colors.Count; i++)
        {
            var color = colors[i];
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO setting_colors (sort_order, loc_key, name, hex)
                VALUES ($order, $key, $name, $hex);
                """;
            cmd.Parameters.AddWithValue("$order", i);
            cmd.Parameters.AddWithValue("$key", (object?)color.Key ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$name", color.Name ?? string.Empty);
            cmd.Parameters.AddWithValue("$hex", color.Hex ?? "#C8E6C9");
            cmd.ExecuteNonQuery();
        }
    }

    private static void ReplaceFonts(SqliteConnection conn, SqliteTransaction tx, List<string> fonts)
    {
        using (var del = conn.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM setting_fonts;";
            del.ExecuteNonQuery();
        }

        for (var i = 0; i < fonts.Count; i++)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO setting_fonts (sort_order, family_name)
                VALUES ($order, $name);
                """;
            cmd.Parameters.AddWithValue("$order", i);
            cmd.Parameters.AddWithValue("$name", fonts[i]);
            cmd.ExecuteNonQuery();
        }
    }

    private static void ReplaceFontSizes(SqliteConnection conn, SqliteTransaction tx, List<double> sizes)
    {
        using (var del = conn.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM setting_font_sizes;";
            del.ExecuteNonQuery();
        }

        for (var i = 0; i < sizes.Count; i++)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO setting_font_sizes (sort_order, size)
                VALUES ($order, $size);
                """;
            cmd.Parameters.AddWithValue("$order", i);
            cmd.Parameters.AddWithValue("$size", sizes[i]);
            cmd.ExecuteNonQuery();
        }
    }

    private static string Bool(bool value) => value ? "1" : "0";

    private static string Real(double value) => value.ToString("G17", CultureInfo.InvariantCulture);

    private static bool ParseBool(string value) =>
        value is "1" or "true" or "True";

    private static double ParseReal(string value, double fallback) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : fallback;

    private static void Normalize(AppSettings s)
    {
        if (s.Colors is not { Count: > 0 })
            s.Colors = new AppSettings().Colors;

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

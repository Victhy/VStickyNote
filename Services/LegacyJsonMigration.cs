using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using VSticky.Models;

namespace VSticky.Services;

/// <summary>把上一版 %LocalAppData%\VSticky\vsticky.db 拷到程序目录（仅目标库尚不存在时）。</summary>
internal static class LegacyStoreMigration
{
    public static void TryCopyLocalSqlite()
    {
        try
        {
            if (File.Exists(AppPaths.DatabasePath))
                return;

            var srcDb = Path.Combine(AppPaths.LegacyLocalDirectory, "vsticky.db");
            if (!File.Exists(srcDb))
                return;

            Directory.CreateDirectory(AppPaths.Root);
            File.Copy(srcDb, AppPaths.DatabasePath, overwrite: false);
            foreach (var suffix in new[] { "-wal", "-shm" })
            {
                var src = srcDb + suffix;
                if (File.Exists(src))
                    File.Copy(src, AppPaths.DatabasePath + suffix, overwrite: false);
            }
        }
        catch
        {
            // 拷贝失败则继续走 JSON 导入或空库
        }
    }
}

/// <summary>从旧版 %AppData%\VSticky\*.json 导入到 SQLite（仅空库时执行）。</summary>
internal static class LegacyJsonMigration
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static void TryImport(SqliteConnection conn)
    {
        try
        {
            var notesPath = Path.Combine(AppPaths.LegacyRoamingDirectory, "notes.json");
            var settingsPath = Path.Combine(AppPaths.LegacyRoamingDirectory, "settings.json");

            if (conn.QueryLong("SELECT COUNT(*) FROM notes;") == 0 && File.Exists(notesPath))
            {
                var json = File.ReadAllText(notesPath);
                var notes = JsonSerializer.Deserialize<List<NoteData>>(json, JsonOptions);
                if (notes is { Count: > 0 })
                    NoteStorage.Save(notes);
            }

            if (conn.QueryLong("SELECT COUNT(*) FROM settings;") == 0 && File.Exists(settingsPath))
            {
                var json = File.ReadAllText(settingsPath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (settings != null)
                    SettingsStore.Import(settings);
            }
        }
        catch
        {
            // 迁移失败不影响首次启动，保留 JSON 以便手动恢复
        }
    }
}

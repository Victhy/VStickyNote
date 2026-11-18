using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;

namespace VSticky.Services;

/// <summary>SQLite 连接、WAL 与 schema 初始化。</summary>
internal static class AppDatabase
{
    internal const int SchemaVersion = 1;

    private static readonly object Gate = new();
    private static SqliteConnection? _connection;
    private static bool _freshDatabase;

    /// <summary>本次启动时新建了数据库文件（尚无历史便签记录）。</summary>
    public static bool IsFreshDatabase
    {
        get
        {
            Open();
            return _freshDatabase;
        }
    }

    public static SqliteConnection Open()
    {
        lock (Gate)
        {
            if (_connection != null)
                return _connection;

            Directory.CreateDirectory(AppPaths.Root);
            LegacyStoreMigration.TryCopyLocalSqlite();
            _freshDatabase = !File.Exists(AppPaths.DatabasePath);

            var cs = new SqliteConnectionStringBuilder
            {
                DataSource = AppPaths.DatabasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared
            }.ToString();

            var conn = new SqliteConnection(cs);
            conn.Open();
            ApplyPragmas(conn);
            EnsureSchema(conn);
            _connection = conn;

            if (_freshDatabase)
                LegacyJsonMigration.TryImport(conn);

            return conn;
        }
    }

    public static void Close()
    {
        lock (Gate)
        {
            if (_connection == null)
                return;

            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                cmd.ExecuteNonQuery();
            }
            catch
            {
                // ignore
            }

            _connection.Dispose();
            _connection = null;
        }
    }

    internal static int Execute(this SqliteConnection conn, string sql, SqliteTransaction? tx = null)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        return cmd.ExecuteNonQuery();
    }

    internal static long QueryLong(this SqliteConnection conn, string sql, SqliteTransaction? tx = null)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        var value = cmd.ExecuteScalar();
        return value is null or DBNull ? 0 : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    private static void ApplyPragmas(SqliteConnection conn)
    {
        conn.Execute("PRAGMA journal_mode=WAL;");
        conn.Execute("PRAGMA synchronous=NORMAL;");
        conn.Execute("PRAGMA foreign_keys=ON;");
        conn.Execute("PRAGMA busy_timeout=5000;");
        conn.Execute("PRAGMA temp_store=MEMORY;");
    }

    private static void EnsureSchema(SqliteConnection conn)
    {
        conn.Execute("""
            CREATE TABLE IF NOT EXISTS schema_info (
                id INTEGER PRIMARY KEY CHECK (id = 1),
                version INTEGER NOT NULL
            );
            """);

        conn.Execute("""
            CREATE TABLE IF NOT EXISTS notes (
                id TEXT PRIMARY KEY NOT NULL COLLATE NOCASE,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                is_deleted INTEGER NOT NULL DEFAULT 0 CHECK (is_deleted IN (0, 1)),
                deleted_at TEXT NULL
            );
            """);

        conn.Execute("""
            CREATE TABLE IF NOT EXISTS note_bodies (
                note_id TEXT PRIMARY KEY NOT NULL COLLATE NOCASE
                    REFERENCES notes(id) ON DELETE CASCADE,
                content TEXT NOT NULL DEFAULT '',
                updated_at TEXT NOT NULL
            );
            """);

        conn.Execute("""
            CREATE TABLE IF NOT EXISTS note_geometries (
                note_id TEXT PRIMARY KEY NOT NULL COLLATE NOCASE
                    REFERENCES notes(id) ON DELETE CASCADE,
                pos_left REAL NOT NULL DEFAULT 120,
                pos_top REAL NOT NULL DEFAULT 120,
                width REAL NOT NULL DEFAULT 260,
                height REAL NOT NULL DEFAULT 220
            );
            """);

        conn.Execute("""
            CREATE TABLE IF NOT EXISTS note_styles (
                note_id TEXT PRIMARY KEY NOT NULL COLLATE NOCASE
                    REFERENCES notes(id) ON DELETE CASCADE,
                color_hex TEXT NOT NULL DEFAULT '#C8E6C9',
                opacity REAL NOT NULL DEFAULT 0.92,
                corner_radius REAL NOT NULL DEFAULT 12,
                font_family TEXT NOT NULL DEFAULT 'Microsoft YaHei UI',
                font_size REAL NOT NULL DEFAULT 14,
                is_topmost INTEGER NOT NULL DEFAULT 0 CHECK (is_topmost IN (0, 1))
            );
            """);

        conn.Execute("""
            CREATE TABLE IF NOT EXISTS note_flags (
                note_id TEXT PRIMARY KEY NOT NULL COLLATE NOCASE
                    REFERENCES notes(id) ON DELETE CASCADE,
                is_badge INTEGER NOT NULL DEFAULT 0 CHECK (is_badge IN (0, 1)),
                badge_stay_open INTEGER NOT NULL DEFAULT 0 CHECK (badge_stay_open IN (0, 1)),
                is_hidden INTEGER NOT NULL DEFAULT 0 CHECK (is_hidden IN (0, 1))
            );
            """);

        conn.Execute("""
            CREATE INDEX IF NOT EXISTS ix_notes_deleted_updated
            ON notes(is_deleted, updated_at);
            """);

        conn.Execute("""
            CREATE TABLE IF NOT EXISTS settings (
                key TEXT PRIMARY KEY NOT NULL,
                value TEXT NOT NULL,
                value_type TEXT NOT NULL CHECK (value_type IN ('string', 'int', 'real', 'bool'))
            );
            """);

        conn.Execute("""
            CREATE TABLE IF NOT EXISTS setting_colors (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                sort_order INTEGER NOT NULL,
                loc_key TEXT NULL,
                name TEXT NOT NULL DEFAULT '',
                hex TEXT NOT NULL
            );
            """);

        conn.Execute("""
            CREATE TABLE IF NOT EXISTS setting_fonts (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                sort_order INTEGER NOT NULL,
                family_name TEXT NOT NULL
            );
            """);

        conn.Execute("""
            CREATE TABLE IF NOT EXISTS setting_font_sizes (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                sort_order INTEGER NOT NULL,
                size REAL NOT NULL
            );
            """);

        conn.Execute($"INSERT OR IGNORE INTO schema_info (id, version) VALUES (1, {SchemaVersion});");
    }
}

internal static class SqliteTime
{
    public static string ToDb(DateTime dt)
    {
        var utc = dt.Kind == DateTimeKind.Utc ? dt : dt.ToUniversalTime();
        return utc.ToString("o", CultureInfo.InvariantCulture);
    }

    public static string? ToDb(DateTime? dt) => dt is null ? null : ToDb(dt.Value);

    public static DateTime FromDb(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return DateTime.Now;

        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
            return dt.Kind == DateTimeKind.Utc ? dt.ToLocalTime() : dt;

        return DateTime.Now;
    }

    public static DateTime? FromDbNullable(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : FromDb(value);
}

internal static class SqliteBool
{
    public static int ToDb(bool value) => value ? 1 : 0;

    public static bool FromDb(object? value) =>
        value is not null and not DBNull && Convert.ToInt32(value, CultureInfo.InvariantCulture) != 0;
}

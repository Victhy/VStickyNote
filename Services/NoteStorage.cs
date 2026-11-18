using Microsoft.Data.Sqlite;
using VSticky.Models;

namespace VSticky.Services;

public static class NoteStorage
{
    public static string StoreDirectory => AppPaths.Root;

    /// <summary>是否已有历史库（含软删除）。首次安装且无旧 JSON 时为 false。</summary>
    public static bool StoreExists
    {
        get
        {
            var conn = AppDatabase.Open();
            if (!AppDatabase.IsFreshDatabase)
                return true;
            return conn.QueryLong("SELECT COUNT(*) FROM notes;") > 0;
        }
    }

    public static List<NoteData> Load()
    {
        try
        {
            var conn = AppDatabase.Open();
            var list = new List<NoteData>();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT
                    n.id, n.created_at, n.updated_at, n.is_deleted, n.deleted_at,
                    IFNULL(b.content, '') AS content,
                    IFNULL(g.pos_left, 120) AS pos_left,
                    IFNULL(g.pos_top, 120) AS pos_top,
                    IFNULL(g.width, 260) AS width,
                    IFNULL(g.height, 220) AS height,
                    IFNULL(s.color_hex, '#C8E6C9') AS color_hex,
                    IFNULL(s.opacity, 0.92) AS opacity,
                    IFNULL(s.corner_radius, 12) AS corner_radius,
                    IFNULL(s.font_family, 'Microsoft YaHei UI') AS font_family,
                    IFNULL(s.font_size, 14) AS font_size,
                    IFNULL(s.is_topmost, 0) AS is_topmost,
                    IFNULL(f.is_badge, 0) AS is_badge,
                    IFNULL(f.badge_stay_open, 0) AS badge_stay_open,
                    IFNULL(f.is_hidden, 0) AS is_hidden
                FROM notes n
                LEFT JOIN note_bodies b ON b.note_id = n.id
                LEFT JOIN note_geometries g ON g.note_id = n.id
                LEFT JOIN note_styles s ON s.note_id = n.id
                LEFT JOIN note_flags f ON f.note_id = n.id
                ORDER BY n.created_at COLLATE NOCASE;
                """;

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                list.Add(ReadNote(reader));

            return list;
        }
        catch
        {
            return [];
        }
    }

    public static void Upsert(NoteData note)
    {
        try
        {
            var conn = AppDatabase.Open();
            using var tx = conn.BeginTransaction();
            try
            {
                Upsert(conn, tx, note);
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
            // 保留内存数据，避免写盘失败导致崩溃
        }
    }

    public static void Save(IEnumerable<NoteData> notes)
    {
        try
        {
            var conn = AppDatabase.Open();
            using var tx = conn.BeginTransaction();
            try
            {
                foreach (var note in notes)
                    Upsert(conn, tx, note);
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
            // 保留内存数据，避免写盘失败导致崩溃
        }
    }

    internal static void Upsert(SqliteConnection conn, SqliteTransaction tx, NoteData note)
    {
        if (string.IsNullOrWhiteSpace(note.Id))
            note.Id = Guid.NewGuid().ToString("N");

        Exec(conn, tx, """
            INSERT INTO notes (id, created_at, updated_at, is_deleted, deleted_at)
            VALUES ($id, $created_at, $updated_at, $is_deleted, $deleted_at)
            ON CONFLICT(id) DO UPDATE SET
                updated_at = excluded.updated_at,
                is_deleted = excluded.is_deleted,
                deleted_at = excluded.deleted_at;
            """,
            ("$id", note.Id),
            ("$created_at", SqliteTime.ToDb(note.CreatedAt)),
            ("$updated_at", SqliteTime.ToDb(note.UpdatedAt)),
            ("$is_deleted", SqliteBool.ToDb(note.IsDeleted)),
            ("$deleted_at", (object?)SqliteTime.ToDb(note.DeletedAt) ?? DBNull.Value));

        Exec(conn, tx, """
            INSERT INTO note_bodies (note_id, content, updated_at)
            VALUES ($id, $content, $updated_at)
            ON CONFLICT(note_id) DO UPDATE SET
                content = excluded.content,
                updated_at = excluded.updated_at;
            """,
            ("$id", note.Id),
            ("$content", note.Text ?? string.Empty),
            ("$updated_at", SqliteTime.ToDb(note.UpdatedAt)));

        Exec(conn, tx, """
            INSERT INTO note_geometries (note_id, pos_left, pos_top, width, height)
            VALUES ($id, $left, $top, $width, $height)
            ON CONFLICT(note_id) DO UPDATE SET
                pos_left = excluded.pos_left,
                pos_top = excluded.pos_top,
                width = excluded.width,
                height = excluded.height;
            """,
            ("$id", note.Id),
            ("$left", note.Left),
            ("$top", note.Top),
            ("$width", note.Width),
            ("$height", note.Height));

        Exec(conn, tx, """
            INSERT INTO note_styles (
                note_id, color_hex, opacity, corner_radius,
                font_family, font_size, is_topmost)
            VALUES ($id, $color, $opacity, $radius, $font, $size, $topmost)
            ON CONFLICT(note_id) DO UPDATE SET
                color_hex = excluded.color_hex,
                opacity = excluded.opacity,
                corner_radius = excluded.corner_radius,
                font_family = excluded.font_family,
                font_size = excluded.font_size,
                is_topmost = excluded.is_topmost;
            """,
            ("$id", note.Id),
            ("$color", string.IsNullOrWhiteSpace(note.ColorHex) ? "#C8E6C9" : note.ColorHex),
            ("$opacity", note.Opacity),
            ("$radius", note.CornerRadius),
            ("$font", string.IsNullOrWhiteSpace(note.FontFamily) ? "Microsoft YaHei UI" : note.FontFamily),
            ("$size", note.FontSize),
            ("$topmost", SqliteBool.ToDb(note.Topmost)));

        Exec(conn, tx, """
            INSERT INTO note_flags (note_id, is_badge, badge_stay_open, is_hidden)
            VALUES ($id, $badge, $stay, $hidden)
            ON CONFLICT(note_id) DO UPDATE SET
                is_badge = excluded.is_badge,
                badge_stay_open = excluded.badge_stay_open,
                is_hidden = excluded.is_hidden;
            """,
            ("$id", note.Id),
            ("$badge", SqliteBool.ToDb(note.IsBadge)),
            ("$stay", SqliteBool.ToDb(note.BadgeStayOpen)),
            ("$hidden", SqliteBool.ToDb(note.IsHidden)));
    }

    private static void Exec(
        SqliteConnection conn,
        SqliteTransaction tx,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value);
        cmd.ExecuteNonQuery();
    }

    private static NoteData ReadNote(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(reader.GetOrdinal("id")),
        CreatedAt = SqliteTime.FromDb(reader.GetString(reader.GetOrdinal("created_at"))),
        UpdatedAt = SqliteTime.FromDb(reader.GetString(reader.GetOrdinal("updated_at"))),
        IsDeleted = SqliteBool.FromDb(reader["is_deleted"]),
        DeletedAt = reader.IsDBNull(reader.GetOrdinal("deleted_at"))
            ? null
            : SqliteTime.FromDb(reader.GetString(reader.GetOrdinal("deleted_at"))),
        Text = reader.GetString(reader.GetOrdinal("content")),
        Left = reader.GetDouble(reader.GetOrdinal("pos_left")),
        Top = reader.GetDouble(reader.GetOrdinal("pos_top")),
        Width = reader.GetDouble(reader.GetOrdinal("width")),
        Height = reader.GetDouble(reader.GetOrdinal("height")),
        ColorHex = reader.GetString(reader.GetOrdinal("color_hex")),
        Opacity = reader.GetDouble(reader.GetOrdinal("opacity")),
        CornerRadius = reader.GetDouble(reader.GetOrdinal("corner_radius")),
        FontFamily = reader.GetString(reader.GetOrdinal("font_family")),
        FontSize = reader.GetDouble(reader.GetOrdinal("font_size")),
        Topmost = SqliteBool.FromDb(reader["is_topmost"]),
        IsBadge = SqliteBool.FromDb(reader["is_badge"]),
        BadgeStayOpen = SqliteBool.FromDb(reader["badge_stay_open"]),
        IsHidden = SqliteBool.FromDb(reader["is_hidden"])
    };
}

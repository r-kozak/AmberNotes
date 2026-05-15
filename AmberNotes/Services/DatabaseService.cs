using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;

namespace AmberNotes.Services;

/// <summary>
/// Manages the SQLCipher-encrypted (or plain) database lifecycle.
///
/// Schema versioning via PRAGMA user_version:
///   0 + no tables  → fresh install  → create schema v2 (UUID PKs)
///   0 + tables exist→ old schema v1 (INTEGER PKs) → auto-migrate to v2
///   2              → current schema → nothing to do
///
/// v0.6 schema changes (v2):
///   • Books.Id  — TEXT (UUID v4) instead of INTEGER AUTOINCREMENT
///   • Notes.Id  — TEXT (UUID v4) instead of INTEGER AUTOINCREMENT
///   • Notes.BookId — TEXT (UUID, FK → Books.Id)
///   • Notes.is_deleted — BOOLEAN (soft-delete; updated_at always refreshed)
///
/// Usage flow:
///   1. Construct with the database file path.
///   2. Call UnlockAsPlain() OR TryUnlockWithKey(hexKey).
///   3. Call Initialize() — handles schema creation/migration automatically.
///   4. Use OpenConnection() for all data access.
/// </summary>
public class DatabaseService
{
    private readonly string _dbPath;

    // 64-char lowercase hex — never logged
    private string? _hexKey;

    // True when this service manages an un-encrypted (public) database
    private bool _isPlain;

    public DatabaseService(string dbPath) => _dbPath = dbPath;

    /// <summary>True when the database file does not yet exist (first run).</summary>
    public bool IsNewDatabase => !File.Exists(_dbPath);

    /// <summary>True when the database is ready to accept connections (unlocked or plain).</summary>
    public bool IsUnlocked => _isPlain || _hexKey is not null;

    /// <summary>
    /// Marks this service as an unencrypted (plain SQLite) database — used for public.db.
    /// After calling this, OpenConnection() and Initialize() work without any PRAGMA key.
    /// </summary>
    public void UnlockAsPlain()
    {
        _isPlain = true;
    }

    /// <summary>
    /// Attempts to open the database with the provided 256-bit hex key.
    /// Returns true on success; false if the key is wrong or the file is corrupt.
    /// SECURITY: hexKey is never logged.
    /// </summary>
    public bool TryUnlockWithKey(string hexKey)
    {
        try
        {
            using var conn = OpenConnectionInternal(hexKey);
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = "SELECT count(*) FROM sqlite_master;";
            cmd.ExecuteScalar();

            _hexKey = hexKey;
            return true;
        }
        catch (SqliteException)
        {
            _hexKey = null;
            return false;
        }
    }

    /// <summary>
    /// Opens a new authenticated connection (encrypted or plain).
    /// Throws <see cref="InvalidOperationException"/> if not yet unlocked.
    /// The caller is responsible for disposing it.
    /// </summary>
    public SqliteConnection OpenConnection()
    {
        if (_isPlain)  return OpenConnectionPlain();
        if (_hexKey is null)
            throw new InvalidOperationException(
                "Database not unlocked. Call UnlockAsPlain() or TryUnlockWithKey() first.");
        return OpenConnectionInternal(_hexKey);
    }

    /// <summary>
    /// Ensures the database directory and all required tables exist,
    /// and automatically migrates from schema v1 (INTEGER PKs) to v2 (UUID PKs) if needed.
    /// Safe to call on every app startup.
    /// </summary>
    public void Initialize()
    {
        var directory = Path.GetDirectoryName(_dbPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        using var connection = OpenConnection();

        var schemaVersion = GetUserVersion(connection);

        if (schemaVersion == 0)
        {
            if (BooksTableExists(connection))
            {
                // Old schema v1 (INTEGER PKs from pre-v0.6) → migrate deterministically
                MigrateV1ToV2(connection);
            }
            else
            {
                // Fresh install — create v2 schema directly
                using var tx = connection.BeginTransaction();
                CreateBooksTableV2(connection, tx);
                CreateNotesTableV2(connection, tx);
                SetUserVersion(connection, tx, 2);
                tx.Commit();
            }
        }
        // schemaVersion == 2 → nothing to do

        // Always seed default book (safe no-op if books already exist)
        using var seedTx = connection.BeginTransaction();
        SeedDefaultBook(connection, seedTx);
        seedTx.Commit();
    }

    /// <summary>
    /// Re-encrypts the database with a new key using SQLCipher's PRAGMA rekey.
    /// After this, all future OpenConnection() calls use <paramref name="newHexKey"/>.
    /// Used when changing the master password (Step 29) or adopting a cloud password (Step 27 Option 1).
    /// Throws <see cref="InvalidOperationException"/> if the database is not yet unlocked.
    /// </summary>
    public void Rekey(string newHexKey)
    {
        if (_isPlain)
            throw new InvalidOperationException("Cannot rekey an unencrypted (plain) database.");
        if (_hexKey is null)
            throw new InvalidOperationException("Database must be unlocked before rekeying.");

        using var conn = OpenConnectionInternal(_hexKey);
        using var cmd  = conn.CreateCommand();
        // PRAGMA rekey changes the encryption key of the currently open, decrypted database
        cmd.CommandText = $"PRAGMA rekey = \"x'{newHexKey}'\";";
        cmd.ExecuteNonQuery();

        _hexKey = newHexKey; // update stored key for future OpenConnection() calls
    }

    /// <summary>
    /// Deletes the database file from disk.
    /// Used for the v0.2 → v0.3 migration and Cloud Wipe.
    /// </summary>
    public void DeleteDatabaseFile()
    {
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }

    // ── Internal helpers ──────────────────────────────────────────────────────

    private SqliteConnection OpenConnectionPlain()
    {
        var dir = Path.GetDirectoryName(_dbPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath,
            Mode       = SqliteOpenMode.ReadWriteCreate,
        }.ToString();

        var conn = new SqliteConnection(cs);
        conn.Open();
        return conn;
    }

    private SqliteConnection OpenConnectionInternal(string hexKey)
    {
        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath,
            Mode       = SqliteOpenMode.ReadWriteCreate,
        }.ToString();

        var conn = new SqliteConnection(cs);
        conn.Open();

        // MUST be the very first statement after Open()
        using var keyCmd = conn.CreateCommand();
        keyCmd.CommandText = $"PRAGMA key = \"x'{hexKey}'\";";
        keyCmd.ExecuteNonQuery();

        return conn;
    }

    // ── Schema v2 ─────────────────────────────────────────────────────────────

    private static void CreateBooksTableV2(SqliteConnection conn, SqliteTransaction tx)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Books (
                Id        TEXT    PRIMARY KEY,          -- UUID v4
                Name      TEXT    NOT NULL,
                IsDefault INTEGER NOT NULL DEFAULT 0    -- 0=false 1=true
            );
            """;
        cmd.ExecuteNonQuery();
    }

    private static void CreateNotesTableV2(SqliteConnection conn, SqliteTransaction tx)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Notes (
                Id           TEXT    PRIMARY KEY,       -- UUID v4
                Title        TEXT    NOT NULL DEFAULT '',
                Content      TEXT    NOT NULL DEFAULT '',
                NoteDateTime TEXT    NOT NULL,          -- ISO-8601 UTC
                CreatedAt    TEXT    NOT NULL,          -- ISO-8601 UTC
                UpdatedAt    TEXT    NOT NULL,          -- ISO-8601 UTC (LWW timestamp)
                Type         TEXT    NOT NULL DEFAULT 'Public',  -- 'Public' | 'Private'
                BookId       TEXT    NOT NULL,          -- FK → Books.Id (UUID)
                is_deleted   INTEGER NOT NULL DEFAULT 0,-- 0=active 1=soft-deleted
                FOREIGN KEY (BookId) REFERENCES Books(Id) ON DELETE CASCADE
            );
            """;
        cmd.ExecuteNonQuery();
    }

    // ── Migration v1 → v2 ─────────────────────────────────────────────────────

    /// <summary>
    /// Migrates existing schema v1 (INTEGER PKs) to v2 (UUID TEXT PKs).
    ///
    /// Deterministic UUID v5 assignment ensures the same record gets the same UUID
    /// across all copies of the database — critical for correct first-time sync merge.
    ///
    /// Migration strategy:
    ///   1. Read all existing records into memory.
    ///   2. Assign deterministic UUID v5 (name = "Title|CreatedAt|OldId") to each Note.
    ///      Books use "book:Name|OldId" as the seed.
    ///   3. Drop old tables.
    ///   4. Create new tables (v2 schema).
    ///   5. Re-insert records with UUID keys.
    ///   6. Set PRAGMA user_version = 2.
    /// </summary>
    private static void MigrateV1ToV2(SqliteConnection conn)
    {
        // ── Step 1: snapshot old data ─────────────────────────────────────────

        var oldBooks = new List<(int OldId, string Name, bool IsDefault)>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT Id, Name, IsDefault FROM Books;";
            using var r = cmd.ExecuteReader();
            while (r.Read())
                oldBooks.Add((r.GetInt32(0), r.GetString(1), r.GetInt32(2) == 1));
        }

        var oldNotes = new List<(int OldId, string Title, string Content,
            string NoteDate, string CreatedAt, string UpdatedAt, string Type, int BookId)>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT Id, Title, Content, NoteDateTime, CreatedAt, UpdatedAt, Type, BookId FROM Notes;";
            using var r = cmd.ExecuteReader();
            while (r.Read())
                oldNotes.Add((r.GetInt32(0), r.GetString(1), r.GetString(2),
                              r.GetString(3), r.GetString(4), r.GetString(5),
                              r.GetString(6), r.GetInt32(7)));
        }

        // ── Step 2: generate deterministic UUIDs ──────────────────────────────

        // Books: "book:{name}|{oldId}" — unique even if two books share the same name
        var bookIdMap = new Dictionary<int, string>();
        foreach (var (oldId, name, _) in oldBooks)
            bookIdMap[oldId] = UuidHelper.NewV5($"book:{name}|{oldId}");

        // ── Step 3-5: rebuild tables inside a transaction ─────────────────────

        using var tx = conn.BeginTransaction();

        // Drop old (Notes first to satisfy FK if enforcement were on)
        ExecNonQuery(conn, tx, "DROP TABLE IF EXISTS Notes;");
        ExecNonQuery(conn, tx, "DROP TABLE IF EXISTS Books;");

        // Create new schema
        CreateBooksTableV2(conn, tx);
        CreateNotesTableV2(conn, tx);

        // Insert books
        foreach (var (oldId, name, isDefault) in oldBooks)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO Books (Id, Name, IsDefault) VALUES ($id, $name, $def);";
            cmd.Parameters.AddWithValue("$id",   bookIdMap[oldId]);
            cmd.Parameters.AddWithValue("$name", name);
            cmd.Parameters.AddWithValue("$def",  isDefault ? 1 : 0);
            cmd.ExecuteNonQuery();
        }

        // Insert notes — Notes: "{title}|{createdAt}|{oldId}" for determinism
        foreach (var (oldId, title, content, noteDate, createdAt, updatedAt, type, oldBookId) in oldNotes)
        {
            if (!bookIdMap.TryGetValue(oldBookId, out var newBookId))
                continue; // orphaned note — skip silently

            var newNoteId = UuidHelper.NewV5($"{title}|{createdAt}|{oldId}");

            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO Notes (Id, Title, Content, NoteDateTime, CreatedAt, UpdatedAt, Type, BookId, is_deleted)
                VALUES ($id, $title, $content, $noteDate, $createdAt, $updatedAt, $type, $bookId, 0);
                """;
            cmd.Parameters.AddWithValue("$id",        newNoteId);
            cmd.Parameters.AddWithValue("$title",     title);
            cmd.Parameters.AddWithValue("$content",   content);
            cmd.Parameters.AddWithValue("$noteDate",  noteDate);
            cmd.Parameters.AddWithValue("$createdAt", createdAt);
            cmd.Parameters.AddWithValue("$updatedAt", updatedAt);
            cmd.Parameters.AddWithValue("$type",      type);
            cmd.Parameters.AddWithValue("$bookId",    newBookId);
            cmd.ExecuteNonQuery();
        }

        SetUserVersion(conn, tx, 2);
        tx.Commit();
    }

    // ── Schema helpers ────────────────────────────────────────────────────────

    private static bool BooksTableExists(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Books';";
        return Convert.ToInt64(cmd.ExecuteScalar() ?? 0L) > 0;
    }

    private static int GetUserVersion(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static void SetUserVersion(SqliteConnection conn, SqliteTransaction tx, int version)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        // PRAGMA user_version cannot use parameters — version is a trusted constant
        cmd.CommandText = $"PRAGMA user_version = {version};";
        cmd.ExecuteNonQuery();
    }

    private static void ExecNonQuery(SqliteConnection conn, SqliteTransaction tx, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText  = sql;
        cmd.ExecuteNonQuery();
    }

    // ── Seed ──────────────────────────────────────────────────────────────────

    private static void SeedDefaultBook(SqliteConnection conn, SqliteTransaction tx)
    {
        using var countCmd = conn.CreateCommand();
        countCmd.Transaction = tx;
        countCmd.CommandText = "SELECT COUNT(*) FROM Books;";
        var count = (long)(countCmd.ExecuteScalar() ?? 0L);
        if (count > 0) return;

        using var insertCmd = conn.CreateCommand();
        insertCmd.Transaction = tx;
        insertCmd.CommandText = "INSERT INTO Books (Id, Name, IsDefault) VALUES ($id, 'My Notes', 1);";
        insertCmd.Parameters.AddWithValue("$id", UuidHelper.NewV4());
        insertCmd.ExecuteNonQuery();
    }
}

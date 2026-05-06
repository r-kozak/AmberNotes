using System;
using System.IO;
using Microsoft.Data.Sqlite;

namespace AmberNotes.Services;

/// <summary>
/// Manages the SQLCipher-encrypted database lifecycle.
///
/// Usage flow:
///   1. Construct with the database file path.
///   2. Call TryUnlockWithKey(hexKey) — derived externally via CryptoService (PBKDF2).
///      Returns true on success; false if the key is wrong or the file is corrupt.
///   3. Call Initialize() once after a successful unlock (safe to call every startup).
///   4. Use OpenConnection() for all subsequent data access.
///
/// Encryption: AES-256 via SQLCipher.
///   All connections apply  PRAGMA key = "x'hexKey'"  immediately on open.
///   The raw hex key bypasses SQLCipher's own PBKDF2 because we run PBKDF2 ourselves
///   (256,000 iterations, SHA-256) so the key material already has the required entropy.
///
/// Database file location:
///   Desktop → %LOCALAPPDATA%\AmberNotes\ambernotes.db
///   Android → app-private storage (path injected via constructor)
/// </summary>
public class DatabaseService
{
    private readonly string _dbPath;

    // 64-char lowercase hex — never logged, cleared on unlock failure
    private string? _hexKey;

    public DatabaseService(string dbPath)
    {
        _dbPath = dbPath;
    }

    /// <summary>True when the database file does not yet exist (first run).</summary>
    public bool IsNewDatabase => !File.Exists(_dbPath);

    /// <summary>
    /// Attempts to open the database with the provided 256-bit hex key.
    ///
    /// Returns true  — key accepted; the key is stored for subsequent OpenConnection() calls.
    /// Returns false — key rejected (wrong password, or file is not a SQLCipher database).
    ///
    /// Migration note: if an unencrypted v0.2 database is present, SQLCipher will fail to
    /// read it and this method returns false. The caller (LoginViewModel) handles migration
    /// by deleting the old file before re-calling this method.
    ///
    /// SECURITY: hexKey is never logged.
    /// </summary>
    public bool TryUnlockWithKey(string hexKey)
    {
        try
        {
            using var conn = OpenConnectionInternal(hexKey);

            // Probe: if the key is correct SQLCipher can read the schema page
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT count(*) FROM sqlite_master;";
            cmd.ExecuteScalar();

            _hexKey = hexKey;
            return true;
        }
        catch (SqliteException)
        {
            // Wrong key or corrupt/unencrypted file
            _hexKey = null;
            return false;
        }
    }

    /// <summary>
    /// Opens a new authenticated connection.
    /// The caller is responsible for disposing it.
    /// Throws <see cref="InvalidOperationException"/> if TryUnlockWithKey() was not called first.
    /// </summary>
    public SqliteConnection OpenConnection()
    {
        if (_hexKey is null)
            throw new InvalidOperationException(
                "Database key not set. Call TryUnlockWithKey() before opening connections.");

        return OpenConnectionInternal(_hexKey);
    }

    /// <summary>
    /// Ensures the database directory and all required tables exist.
    /// Must be called after a successful TryUnlockWithKey().
    /// Safe to call on every app startup (uses CREATE TABLE IF NOT EXISTS).
    /// Also seeds the default "My Notes" book on first run.
    /// </summary>
    public void Initialize()
    {
        var directory = Path.GetDirectoryName(_dbPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        CreateBooksTable(connection, transaction);
        CreateNotesTable(connection, transaction);
        SeedDefaultBook(connection, transaction);

        transaction.Commit();
    }

    /// <summary>
    /// Deletes the database file from disk.
    /// Used for the v0.2 → v0.3 migration: removes the legacy unencrypted database
    /// so a fresh encrypted vault can be created on the same path.
    /// </summary>
    public void DeleteDatabaseFile()
    {
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }

    // ── Internal helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Opens a connection and immediately applies the SQLCipher key as raw hex bytes.
    /// Using the  x'...'  hex syntax tells SQLCipher to use our pre-derived key directly,
    /// skipping SQLCipher's own built-in PBKDF2 (we already did PBKDF2 externally).
    /// </summary>
    private SqliteConnection OpenConnectionInternal(string hexKey)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath,
            Mode       = SqliteOpenMode.ReadWriteCreate,
        }.ToString();

        var connection = new SqliteConnection(connectionString);
        connection.Open();

        // MUST be the very first statement after Open()
        using var keyCmd = connection.CreateCommand();
        keyCmd.CommandText = $"PRAGMA key = \"x'{hexKey}'\";";
        keyCmd.ExecuteNonQuery();

        return connection;
    }

    // ── Schema ────────────────────────────────────────────────────────────────

    private static void CreateBooksTable(SqliteConnection connection, SqliteTransaction transaction)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS Books (
                Id        INTEGER PRIMARY KEY AUTOINCREMENT,
                Name      TEXT    NOT NULL,
                IsDefault INTEGER NOT NULL DEFAULT 0   -- 0 = false, 1 = true
            );
            """;

        using var cmd = connection.CreateCommand();
        cmd.Transaction  = transaction;
        cmd.CommandText  = sql;
        cmd.ExecuteNonQuery();
    }

    private static void CreateNotesTable(SqliteConnection connection, SqliteTransaction transaction)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS Notes (
                Id           INTEGER PRIMARY KEY AUTOINCREMENT,
                Title        TEXT    NOT NULL DEFAULT '',
                Content      TEXT    NOT NULL DEFAULT '',
                NoteDateTime TEXT    NOT NULL,           -- ISO-8601 UTC
                CreatedAt    TEXT    NOT NULL,           -- ISO-8601 UTC
                UpdatedAt    TEXT    NOT NULL,           -- ISO-8601 UTC
                Type         TEXT    NOT NULL DEFAULT 'Open',  -- 'Open' | 'Closed'
                BookId       INTEGER NOT NULL,
                FOREIGN KEY (BookId) REFERENCES Books(Id) ON DELETE CASCADE
            );
            """;

        using var cmd = connection.CreateCommand();
        cmd.Transaction  = transaction;
        cmd.CommandText  = sql;
        cmd.ExecuteNonQuery();
    }

    // ── Seed ──────────────────────────────────────────────────────────────────

    private static void SeedDefaultBook(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var countCmd = connection.CreateCommand();
        countCmd.Transaction  = transaction;
        countCmd.CommandText  = "SELECT COUNT(*) FROM Books;";
        var count = (long)(countCmd.ExecuteScalar() ?? 0L);

        if (count > 0) return;

        using var insertCmd = connection.CreateCommand();
        insertCmd.Transaction  = transaction;
        insertCmd.CommandText  = "INSERT INTO Books (Name, IsDefault) VALUES ('My Notes', 1);";
        insertCmd.ExecuteNonQuery();
    }
}

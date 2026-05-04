using System.IO;
using Microsoft.Data.Sqlite;

namespace AmberNotes.Services;

/// <summary>
/// Manages the SQLite database lifecycle: initialization, schema creation,
/// and provides a factory for opening connections.
///
/// Database file location:
///   Desktop → %LOCALAPPDATA%\AmberNotes\ambernotes.db
///   Android → app-private storage (path injected via constructor)
/// </summary>
public class DatabaseService
{
    private readonly string _dbPath;
    private readonly string _connectionString;

    public DatabaseService(string dbPath)
    {
        _dbPath = dbPath;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();
    }

    /// <summary>
    /// Opens a new connection. Caller is responsible for disposing it.
    /// </summary>
    public SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    /// <summary>
    /// Ensures the database file and all required tables exist.
    /// Safe to call on every app startup (uses CREATE TABLE IF NOT EXISTS).
    /// Also seeds a default Book if the Books table is empty.
    /// </summary>
    public void Initialize()
    {
        // Ensure the directory exists
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
        cmd.Transaction = transaction;
        cmd.CommandText = sql;
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
        cmd.Transaction = transaction;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    // ── Seed ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Inserts a "My Notes" default book if no books exist yet.
    /// </summary>
    private static void SeedDefaultBook(SqliteConnection connection, SqliteTransaction transaction)
    {
        const string countSql = "SELECT COUNT(*) FROM Books;";
        using var countCmd = connection.CreateCommand();
        countCmd.Transaction = transaction;
        countCmd.CommandText = countSql;
        var count = (long)(countCmd.ExecuteScalar() ?? 0L);

        if (count > 0) return;

        const string insertSql = """
            INSERT INTO Books (Name, IsDefault) VALUES ('My Notes', 1);
            """;

        using var insertCmd = connection.CreateCommand();
        insertCmd.Transaction = transaction;
        insertCmd.CommandText = insertSql;
        insertCmd.ExecuteNonQuery();
    }
}

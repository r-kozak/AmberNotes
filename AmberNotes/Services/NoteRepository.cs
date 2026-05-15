using System;
using System.Collections.Generic;
using AmberNotes.Models;
using Microsoft.Data.Sqlite;

namespace AmberNotes.Services;

/// <summary>
/// Provides CRUD operations for the Notes table (schema v2 — UUID primary keys).
///
/// v0.6 changes:
///   • Id and BookId are now string (UUID TEXT).
///   • Delete() performs a soft-delete (is_deleted = 1 + UpdatedAt = UtcNow).
///   • GetAll() filters is_deleted = 0 so soft-deleted notes are hidden from UI.
///   • GetAllIncludingDeleted() returns every record — used by SyncService for Push.
///
/// All DateTime values are stored/retrieved as ISO-8601 UTC strings.
/// </summary>
public class NoteRepository
{
    private readonly DatabaseService _db;

    public NoteRepository(DatabaseService db) => _db = db;

    // ── READ ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns all active (not soft-deleted) notes ordered by NoteDateTime descending.
    /// </summary>
    public List<Note> GetAll()
    {
        var notes = new List<Note>();
        using var conn = _db.OpenConnection();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            SELECT n.Id, n.Title, n.Content, n.NoteDateTime, n.CreatedAt, n.UpdatedAt,
                   n.Type, n.BookId, b.Name AS BookName, b.IsDefault, n.is_deleted
            FROM Notes n
            LEFT JOIN Books b ON b.Id = n.BookId
            WHERE n.is_deleted = 0
            ORDER BY n.NoteDateTime DESC;
            """;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            notes.Add(MapNote(reader));
        return notes;
    }

    /// <summary>
    /// Returns ALL notes (including soft-deleted) ordered by UpdatedAt descending.
    /// Used by SyncService to push every state change (including deletions) to the cloud.
    /// </summary>
    public List<Note> GetAllIncludingDeleted()
    {
        var notes = new List<Note>();
        using var conn = _db.OpenConnection();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            SELECT n.Id, n.Title, n.Content, n.NoteDateTime, n.CreatedAt, n.UpdatedAt,
                   n.Type, n.BookId, b.Name AS BookName, b.IsDefault, n.is_deleted
            FROM Notes n
            LEFT JOIN Books b ON b.Id = n.BookId
            ORDER BY n.UpdatedAt DESC;
            """;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            notes.Add(MapNote(reader));
        return notes;
    }

    /// <summary>Returns a single note by UUID, or null if not found.</summary>
    public Note? GetById(string id)
    {
        using var conn = _db.OpenConnection();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            SELECT n.Id, n.Title, n.Content, n.NoteDateTime, n.CreatedAt, n.UpdatedAt,
                   n.Type, n.BookId, b.Name AS BookName, b.IsDefault, n.is_deleted
            FROM Notes n
            LEFT JOIN Books b ON b.Id = n.BookId
            WHERE n.Id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", id);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? MapNote(reader) : null;
    }

    // ── CREATE ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Inserts a new note. Generates a random UUID v4 for Id.
    /// Sets CreatedAt and UpdatedAt to UtcNow. Returns the inserted note.
    /// </summary>
    public Note Create(Note note)
    {
        var now = DateTime.UtcNow;
        note.Id        = UuidHelper.NewV4();
        note.CreatedAt = now;
        note.UpdatedAt = now;
        note.IsDeleted = false;

        using var conn = _db.OpenConnection();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Notes (Id, Title, Content, NoteDateTime, CreatedAt, UpdatedAt, Type, BookId, is_deleted)
            VALUES ($id, $title, $content, $noteDateTime, $createdAt, $updatedAt, $type, $bookId, 0);
            """;
        cmd.Parameters.AddWithValue("$id",           note.Id);
        cmd.Parameters.AddWithValue("$title",        note.Title);
        cmd.Parameters.AddWithValue("$content",      note.Content);
        cmd.Parameters.AddWithValue("$noteDateTime", note.NoteDateTime.ToString("o"));
        cmd.Parameters.AddWithValue("$createdAt",    note.CreatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("$updatedAt",    note.UpdatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("$type",         note.Type.ToString());
        cmd.Parameters.AddWithValue("$bookId",       note.BookId);
        cmd.ExecuteNonQuery();

        return note;
    }

    // ── UPDATE ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Updates an existing note. Sets UpdatedAt to UtcNow.
    /// </summary>
    public void Update(Note note)
    {
        note.UpdatedAt = DateTime.UtcNow;

        using var conn = _db.OpenConnection();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE Notes
            SET Title        = $title,
                Content      = $content,
                NoteDateTime = $noteDateTime,
                UpdatedAt    = $updatedAt,
                Type         = $type,
                BookId       = $bookId
            WHERE Id = $id;
            """;
        cmd.Parameters.AddWithValue("$title",        note.Title);
        cmd.Parameters.AddWithValue("$content",      note.Content);
        cmd.Parameters.AddWithValue("$noteDateTime", note.NoteDateTime.ToString("o"));
        cmd.Parameters.AddWithValue("$updatedAt",    note.UpdatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("$type",         note.Type.ToString());
        cmd.Parameters.AddWithValue("$bookId",       note.BookId);
        cmd.Parameters.AddWithValue("$id",           note.Id);
        cmd.ExecuteNonQuery();
    }

    // ── DELETE (soft) ─────────────────────────────────────────────────────────

    /// <summary>
    /// Soft-deletes a note: sets is_deleted = 1 and refreshes UpdatedAt.
    /// The record remains in the DB so other devices learn about the deletion during sync.
    /// </summary>
    public void Delete(string id)
    {
        using var conn = _db.OpenConnection();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE Notes
            SET is_deleted = 1,
                UpdatedAt  = $updatedAt
            WHERE Id = $id;
            """;
        cmd.Parameters.AddWithValue("$updatedAt", DateTime.UtcNow.ToString("o"));
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Physically removes a note from the database (used internally, e.g. during Cloud Wipe).
    /// Prefer Delete() (soft) for normal user actions.
    /// </summary>
    public void HardDelete(string id)
    {
        using var conn = _db.OpenConnection();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Notes WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    // ── UPSERT (used by SyncService merge) ────────────────────────────────────

    /// <summary>
    /// Inserts or replaces a note (REPLACE INTO — uses UUID PK conflict resolution).
    /// CreatedAt is preserved from the incoming record — only UpdatedAt drives LWW.
    /// </summary>
    public void Upsert(Note note)
    {
        using var conn = _db.OpenConnection();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Notes (Id, Title, Content, NoteDateTime, CreatedAt, UpdatedAt, Type, BookId, is_deleted)
            VALUES ($id, $title, $content, $noteDateTime, $createdAt, $updatedAt, $type, $bookId, $isDeleted)
            ON CONFLICT(Id) DO UPDATE SET
                Title        = excluded.Title,
                Content      = excluded.Content,
                NoteDateTime = excluded.NoteDateTime,
                UpdatedAt    = excluded.UpdatedAt,
                Type         = excluded.Type,
                BookId       = excluded.BookId,
                is_deleted   = excluded.is_deleted
            WHERE excluded.UpdatedAt > Notes.UpdatedAt;
            """;
        cmd.Parameters.AddWithValue("$id",           note.Id);
        cmd.Parameters.AddWithValue("$title",        note.Title);
        cmd.Parameters.AddWithValue("$content",      note.Content);
        cmd.Parameters.AddWithValue("$noteDateTime", note.NoteDateTime.ToString("o"));
        cmd.Parameters.AddWithValue("$createdAt",    note.CreatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("$updatedAt",    note.UpdatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("$type",         note.Type.ToString());
        cmd.Parameters.AddWithValue("$bookId",       note.BookId);
        cmd.Parameters.AddWithValue("$isDeleted",    note.IsDeleted ? 1 : 0);
        cmd.ExecuteNonQuery();
    }

    // ── MAPPING ───────────────────────────────────────────────────────────────

    private static Note MapNote(SqliteDataReader r) => new()
    {
        Id           = r.GetString(0),
        Title        = r.GetString(1),
        Content      = r.GetString(2),
        NoteDateTime = DateTime.Parse(r.GetString(3)),
        CreatedAt    = DateTime.Parse(r.GetString(4)).ToUniversalTime(),
        UpdatedAt    = DateTime.Parse(r.GetString(5)).ToUniversalTime(),
        Type         = Enum.Parse<NoteType>(r.GetString(6)),
        BookId       = r.GetString(7),
        IsDeleted    = r.GetInt32(10) == 1,
        Book = r.IsDBNull(8) ? null : new Book
        {
            Id        = r.GetString(7),  // n.BookId == b.Id
            Name      = r.GetString(8),
            IsDefault = r.GetInt32(9) == 1
        }
    };
}

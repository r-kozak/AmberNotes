using System;
using System.Collections.Generic;
using AmberNotes.Models;
using Microsoft.Data.Sqlite;

namespace AmberNotes.Services;

/// <summary>
/// Provides CRUD operations for the Notes table.
/// All DateTime values are stored/retrieved as ISO-8601 UTC strings.
/// </summary>
public class NoteRepository
{
    private readonly DatabaseService _db;

    public NoteRepository(DatabaseService db) => _db = db;

    // ── READ ──────────────────────────────────────────────────────────────────

    /// <summary>Returns all notes ordered by NoteDateTime descending.</summary>
    public List<Note> GetAll()
    {
        var notes = new List<Note>();
        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT n.Id, n.Title, n.Content, n.NoteDateTime, n.CreatedAt, n.UpdatedAt,
                   n.Type, n.BookId, b.Name AS BookName, b.IsDefault
            FROM Notes n
            LEFT JOIN Books b ON b.Id = n.BookId
            ORDER BY n.NoteDateTime DESC;
            """;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            notes.Add(MapNote(reader));
        return notes;
    }

    /// <summary>Returns a single note by Id, or null if not found.</summary>
    public Note? GetById(int id)
    {
        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT n.Id, n.Title, n.Content, n.NoteDateTime, n.CreatedAt, n.UpdatedAt,
                   n.Type, n.BookId, b.Name AS BookName, b.IsDefault
            FROM Notes n
            LEFT JOIN Books b ON b.Id = n.BookId
            WHERE n.Id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", id);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? MapNote(reader) : null;
    }

    // ── CREATE ────────────────────────────────────────────────────────────────

    /// <summary>Inserts a new note and sets its Id. Returns the inserted note.</summary>
    public Note Create(Note note)
    {
        var now = DateTime.UtcNow;
        note.CreatedAt = now;
        note.UpdatedAt = now;

        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Notes (Title, Content, NoteDateTime, CreatedAt, UpdatedAt, Type, BookId)
            VALUES ($title, $content, $noteDateTime, $createdAt, $updatedAt, $type, $bookId);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$title", note.Title);
        cmd.Parameters.AddWithValue("$content", note.Content);
        cmd.Parameters.AddWithValue("$noteDateTime", note.NoteDateTime.ToString("o"));
        cmd.Parameters.AddWithValue("$createdAt", note.CreatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("$updatedAt", note.UpdatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("$type", note.Type.ToString());
        cmd.Parameters.AddWithValue("$bookId", note.BookId);

        note.Id = Convert.ToInt32(cmd.ExecuteScalar());
        return note;
    }

    // ── UPDATE ────────────────────────────────────────────────────────────────

    /// <summary>Updates an existing note. Sets UpdatedAt to UtcNow.</summary>
    public void Update(Note note)
    {
        note.UpdatedAt = DateTime.UtcNow;

        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();
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
        cmd.Parameters.AddWithValue("$title", note.Title);
        cmd.Parameters.AddWithValue("$content", note.Content);
        cmd.Parameters.AddWithValue("$noteDateTime", note.NoteDateTime.ToString("o"));
        cmd.Parameters.AddWithValue("$updatedAt", note.UpdatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("$type", note.Type.ToString());
        cmd.Parameters.AddWithValue("$bookId", note.BookId);
        cmd.Parameters.AddWithValue("$id", note.Id);
        cmd.ExecuteNonQuery();
    }

    // ── DELETE ────────────────────────────────────────────────────────────────

    /// <summary>Deletes a note by Id.</summary>
    public void Delete(int id)
    {
        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Notes WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    // ── MAPPING ───────────────────────────────────────────────────────────────

    private static Note MapNote(SqliteDataReader r) => new()
    {
        Id           = r.GetInt32(0),
        Title        = r.GetString(1),
        Content      = r.GetString(2),
        NoteDateTime = DateTime.Parse(r.GetString(3)),
        CreatedAt    = DateTime.Parse(r.GetString(4)).ToUniversalTime(),
        UpdatedAt    = DateTime.Parse(r.GetString(5)).ToUniversalTime(),
        Type         = Enum.Parse<NoteType>(r.GetString(6)),
        BookId       = r.GetInt32(7),
        Book = r.IsDBNull(8) ? null : new Book
        {
            Id        = r.GetInt32(7),
            Name      = r.GetString(8),
            IsDefault = r.GetInt32(9) == 1
        }
    };
}

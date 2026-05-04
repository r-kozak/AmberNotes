using System;

namespace AmberNotes.Models;

/// <summary>
/// Represents a single note entry.
/// Maps to the "Notes" table in SQLite.
/// Has a Many-to-One relationship with <see cref="Book"/>.
/// </summary>
public class Note
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;

    /// <summary>The user-defined date/time of the note (e.g. diary entry date).</summary>
    public DateTime NoteDateTime { get; set; }

    /// <summary>Timestamp when the record was first created in the DB.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Timestamp when the record was last updated in the DB.</summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>Public = accessible to everyone; Private = secure, requires authentication.</summary>
    public NoteType Type { get; set; } = NoteType.Public;

    // ── Foreign key ──────────────────────────────────────────────────────────
    /// <summary>FK → Books.Id</summary>
    public int BookId { get; set; }

    // ── Navigation property (not persisted, populated manually) ──────────────
    public Book? Book { get; set; }
}

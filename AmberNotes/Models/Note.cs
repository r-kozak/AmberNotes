using System;

namespace AmberNotes.Models;

/// <summary>
/// Represents a single note entry.
/// Maps to the "Notes" table in SQLite.
/// Has a Many-to-One relationship with <see cref="Book"/>.
///
/// v0.6 changes:
///   • Id is now a UUID v4 string (TEXT in SQLite) instead of INTEGER.
///   • BookId is now a UUID string matching Books.Id.
///   • IsDeleted supports soft-delete; the record stays in the DB and
///     UpdatedAt is refreshed so other devices learn about the deletion.
/// </summary>
public class Note
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;

    /// <summary>The user-defined date/time of the note (e.g. diary entry date).</summary>
    public DateTime NoteDateTime { get; set; }

    /// <summary>Timestamp when the record was first created in the DB (UTC).</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Timestamp when the record was last modified (UTC).
    /// MUST be updated on every change — used for Last-Write-Wins sync.
    /// </summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>Public = accessible to everyone; Private = secure, requires authentication.</summary>
    public NoteType Type { get; set; } = NoteType.Public;

    /// <summary>
    /// Soft-delete flag.
    /// When true the note is hidden from all UI lists but kept in the DB
    /// so other devices learn about the deletion during sync.
    /// UpdatedAt is refreshed when this flag is set.
    /// </summary>
    public bool IsDeleted { get; set; }

    // ── Foreign key ──────────────────────────────────────────────────────────
    /// <summary>FK → Books.Id (UUID string)</summary>
    public string BookId { get; set; } = string.Empty;

    // ── Navigation property (not persisted, populated manually) ──────────────
    public Book? Book { get; set; }
}

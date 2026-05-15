namespace AmberNotes.Models;

/// <summary>
/// Represents a notebook (book) that groups notes together.
/// Maps to the "Books" table in SQLite.
///
/// v0.6: Id is now a UUID v4 string (TEXT) instead of INTEGER.
/// </summary>
public class Book
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Indicates whether this book is the default one.
    /// Only one book should have IsDefault = true at a time.
    /// </summary>
    public bool IsDefault { get; set; }
}

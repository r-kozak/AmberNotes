namespace AmberNotes.Models;

/// <summary>
/// Represents a notebook (book) that groups notes together.
/// Maps to the "Books" table in SQLite.
/// </summary>
public class Book
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Indicates whether this book is the default one.
    /// Only one book should have IsDefault = true at a time.
    /// </summary>
    public bool IsDefault { get; set; }
}

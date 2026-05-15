using System.Collections.Generic;
using AmberNotes.Models;

namespace AmberNotes.Services;

/// <summary>
/// Provides read operations for the Books table (schema v2 — UUID primary keys).
/// v0.6: Book.Id is now a string (UUID TEXT).
/// </summary>
public class BookRepository
{
    private readonly DatabaseService _db;

    public BookRepository(DatabaseService db) => _db = db;

    /// <summary>Returns all books ordered by name.</summary>
    public List<Book> GetAll()
    {
        var books = new List<Book>();
        using var conn = _db.OpenConnection();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Name, IsDefault FROM Books ORDER BY Name;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            books.Add(new Book
            {
                Id        = reader.GetString(0),   // UUID TEXT (was GetInt32)
                Name      = reader.GetString(1),
                IsDefault = reader.GetInt32(2) == 1
            });
        }
        return books;
    }

    /// <summary>Returns the default book, or the first book if none is marked default.</summary>
    public Book? GetDefault()
    {
        using var conn = _db.OpenConnection();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Id, Name, IsDefault FROM Books
            ORDER BY IsDefault DESC, Name ASC
            LIMIT 1;
            """;
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return new Book
        {
            Id        = reader.GetString(0),       // UUID TEXT (was GetInt32)
            Name      = reader.GetString(1),
            IsDefault = reader.GetInt32(2) == 1
        };
    }
}

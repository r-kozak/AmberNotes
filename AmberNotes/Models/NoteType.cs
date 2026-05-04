namespace AmberNotes.Models;

/// <summary>
/// Defines the type (state) of a note.
/// </summary>
public enum NoteType
{
    /// <summary>Accessible to everyone; no password required.</summary>
    Public,
    /// <summary>Secure; requires authentication to view list of private notes.</summary>
    Private
}

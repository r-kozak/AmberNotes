using System;
using System.Collections.ObjectModel;
using AmberNotes.Models;
using AmberNotes.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AmberNotes.ViewModels;

/// <summary>
/// ViewModel for the note edit/create dialog.
/// Supports both creating a new note (noteId == null) and editing an existing one.
/// </summary>
public partial class NoteEditViewModel : ViewModelBase
{
    private readonly NoteRepository _noteRepo;
    private readonly int? _noteId; // null = new note

    // ── Bindable fields ───────────────────────────────────────────────────────

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _content = string.Empty;

    [ObservableProperty]
    private DateTimeOffset _noteDate = DateTimeOffset.Now;

    [ObservableProperty]
    private NoteType _selectedType = NoteType.Public;

    [ObservableProperty]
    private Book? _selectedBook;

    [ObservableProperty]
    private string _windowTitle = "Нова нотатка";

    // ── Collections ───────────────────────────────────────────────────────────

    public ObservableCollection<Book> Books { get; } = [];
    public NoteType[] NoteTypes { get; } = Enum.GetValues<NoteType>();

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>Raised when the user saves successfully. Carries the saved Note.</summary>
    public event Action<Note>? Saved;

    /// <summary>Raised when the user cancels.</summary>
    public event Action? Cancelled;

    // ── Constructor ───────────────────────────────────────────────────────────

    public NoteEditViewModel(NoteRepository noteRepo, BookRepository bookRepo, int? noteId = null)
    {
        _noteRepo = noteRepo;
        _noteId   = noteId;

        // Load books
        foreach (var b in bookRepo.GetAll())
            Books.Add(b);

        if (noteId.HasValue)
        {
            // Edit mode — load existing note
            WindowTitle = "Редагувати нотатку";
            var note = noteRepo.GetById(noteId.Value);
            if (note is not null)
            {
                Title        = note.Title;
                Content      = note.Content;
                NoteDate     = new DateTimeOffset(note.NoteDateTime.ToLocalTime());
                SelectedType = note.Type;
                SelectedBook = Books.Count > 0
                    ? FindBookById(note.BookId) ?? Books[0]
                    : null;
            }
        }
        else
        {
            // Create mode — pre-select default book
            SelectedBook = Books.Count > 0 ? Books[0] : null;
        }
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private void Save()
    {
        if (SelectedBook is null) return;

        var note = new Note
        {
            Id           = _noteId ?? 0,
            Title        = Title.Trim(),
            Content      = Content,
            NoteDateTime = NoteDate.UtcDateTime,
            Type         = SelectedType,
            BookId       = SelectedBook.Id
        };

        if (_noteId.HasValue)
            _noteRepo.Update(note);
        else
            _noteRepo.Create(note);

        Saved?.Invoke(note);
    }

    [RelayCommand]
    private void Cancel() => Cancelled?.Invoke();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private Book? FindBookById(int id)
    {
        foreach (var b in Books)
            if (b.Id == id) return b;
        return null;
    }
}

using System.Collections.ObjectModel;
using AmberNotes.Models;
using AmberNotes.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AmberNotes.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    // Exposed for use in MainView.axaml.cs (dialog creation)
    public NoteRepository NoteRepo { get; }
    public BookRepository BookRepo { get; }

    // ── Notes list ────────────────────────────────────────────────────────────

    public ObservableCollection<Note> Notes { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditNoteCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteNoteCommand))]
    private Note? _selectedNote;

    // ── Events (for View to open dialogs) ─────────────────────────────────────

    /// <summary>
    /// Raised when the user wants to create or edit a note.
    /// The View subscribes and opens NoteEditView as a dialog.
    /// int? = noteId (null = new note).
    /// </summary>
    public event System.Action<int?>? OpenNoteEditRequested;

    // ── Constructor ───────────────────────────────────────────────────────────

    public MainViewModel(NoteRepository noteRepo, BookRepository bookRepo)
    {
        NoteRepo = noteRepo;
        BookRepo = bookRepo;
        LoadNotes();
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private void CreateNote() => OpenNoteEditRequested?.Invoke(null);

    [RelayCommand(CanExecute = nameof(HasSelectedNote))]
    private void EditNote() => OpenNoteEditRequested?.Invoke(SelectedNote!.Id);

    [RelayCommand(CanExecute = nameof(HasSelectedNote))]
    private void DeleteNote()
    {
        if (SelectedNote is null) return;
        NoteRepo.Delete(SelectedNote.Id);
        Notes.Remove(SelectedNote);
        SelectedNote = null;
    }

    // ── Public API (called by View after dialog closes) ───────────────────────

    /// <summary>Reloads the notes list from the database.</summary>
    public void RefreshNotes() => LoadNotes();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private bool HasSelectedNote => SelectedNote is not null;

    private void LoadNotes()
    {
        Notes.Clear();
        foreach (var note in NoteRepo.GetAll())
            Notes.Add(note);
    }
}

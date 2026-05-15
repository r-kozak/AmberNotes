using System;
using System.Collections.ObjectModel;
using AmberNotes.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AmberNotes.ViewModels;

/// <summary>
/// ViewModel for the notes list page.
///
/// Owns selection state and CRUD commands.
/// Navigation (create / edit) and deletion I/O are delegated back to
/// MainViewModel via callbacks so this class stays free of repo references.
///
/// v0.6: note IDs are now string (UUID) — Action callbacks updated accordingly.
/// </summary>
public partial class MainListViewModel : ViewModelBase
{
    // ── Notes collection ─────────────────────────────────────────────────────
    // Shared reference from MainViewModel — changes are reflected instantly.
    public ObservableCollection<Note> Notes { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditNoteCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteNoteCommand))]
    private Note? _selectedNote;

    // ── Callbacks ──────────────────────────────────────────────────────────────
    private readonly Action<string?> _navigateToEditor; // null = create new note
    private readonly Action<string>  _deleteNote;       // persist soft-delete via repo

    // ── Constructor ────────────────────────────────────────────────────────────
    public MainListViewModel(
        ObservableCollection<Note> notes,
        Action<string?>            navigateToEditor,
        Action<string>             deleteNote)
    {
        Notes             = notes;
        _navigateToEditor = navigateToEditor;
        _deleteNote       = deleteNote;
    }

    // ── Commands ───────────────────────────────────────────────────────────────

    [RelayCommand]
    private void CreateNote() => _navigateToEditor(null);

    [RelayCommand(CanExecute = nameof(HasSelectedNote))]
    private void EditNote() => _navigateToEditor(SelectedNote!.Id);

    [RelayCommand(CanExecute = nameof(HasSelectedNote))]
    private void DeleteNote()
    {
        if (SelectedNote is null) return;

        var note = SelectedNote;
        SelectedNote = null;          // clear selection first
        Notes.Remove(note);           // update UI immediately (soft-delete hides it)
        _deleteNote(note.Id);         // persist soft-delete to DB
    }

    // ── Helpers ────────────────────────────────────────────────────────────────
    private bool HasSelectedNote => SelectedNote is not null;
}

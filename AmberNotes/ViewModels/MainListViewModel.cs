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
    private readonly Action<int?> _navigateToEditor; // null = create new note
    private readonly Action<int>  _deleteNote;       // persist delete via repo

    // ── Constructor ────────────────────────────────────────────────────────────
    public MainListViewModel(
        ObservableCollection<Note> notes,
        Action<int?>               navigateToEditor,
        Action<int>                deleteNote)
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
        SelectedNote = null;        // clear selection first
        Notes.Remove(note);         // update UI immediately
        _deleteNote(note.Id);       // persist to DB
    }

    // ── Helpers ────────────────────────────────────────────────────────────────
    private bool HasSelectedNote => SelectedNote is not null;
}

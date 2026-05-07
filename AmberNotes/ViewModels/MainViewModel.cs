using System.Collections.ObjectModel;
using AmberNotes.Models;
using AmberNotes.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AmberNotes.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    // ── Repositories (exposed so MainView.axaml.cs can build NoteEditViewModel) ─
    public NoteRepository NoteRepo { get; }
    public BookRepository BookRepo { get; }

    // ── Notes list ────────────────────────────────────────────────────────────
    public ObservableCollection<Note> Notes { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditNoteCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteNoteCommand))]
    private Note? _selectedNote;

    // ── Mode state ────────────────────────────────────────────────────────────

    /// <summary>True when the app is in Private (encrypted) mode.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPublicMode))]
    private bool _isPrivateMode;

    /// <summary>Convenience inverse for XAML bindings.</summary>
    public bool IsPublicMode => !IsPrivateMode;

    // ── Theme state ───────────────────────────────────────────────────────────

    /// <summary>True when the current theme is AmberNoir (dark).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ThemeToggleIcon))]
    [NotifyPropertyChangedFor(nameof(ThemeToggleTip))]
    private bool _isDarkTheme;

    /// <summary>Button icon — shows what theme will be switched TO.</summary>
    public string ThemeToggleIcon => IsDarkTheme ? "☀" : "🌙";

    /// <summary>Tooltip for the theme toggle button.</summary>
    public string ThemeToggleTip =>
        IsDarkTheme ? "Перемкнути на світлу тему (Saffron Linen)"
                    : "Перемкнути на темну тему (Amber Noir)";

    // ── Events (for View to open dialogs) ─────────────────────────────────────

    /// <summary>
    /// Raised when the user wants to create or edit a note.
    /// The View subscribes and opens NoteEditView as a dialog or overlay.
    /// int? = noteId (null = new note).
    /// </summary>
    public event System.Action<int?>? OpenNoteEditRequested;

    // ── Constructor ───────────────────────────────────────────────────────────

    public MainViewModel(NoteRepository noteRepo, BookRepository bookRepo)
    {
        NoteRepo = noteRepo;
        BookRepo = bookRepo;

        // Sync initial state from singleton services
        _isPrivateMode = ModeService.Instance.IsPrivate;
        _isDarkTheme   = ThemeService.Instance.IsDark;

        // React to mode changes (Note: singletons live for app lifetime → no leak)
        ModeService.Instance.ModeChanged += mode =>
        {
            IsPrivateMode = mode == AppMode.Private;
            LoadNotes();
        };

        // React to theme changes
        ThemeService.Instance.ThemeChanged += theme =>
        {
            IsDarkTheme = theme == AppTheme.AmberNoir;
        };

        LoadNotes();
    }

    // ── CRUD Commands ─────────────────────────────────────────────────────────

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

    // ── Mode Commands ─────────────────────────────────────────────────────────

    /// <summary>Switch to Public mode (shows public notes).</summary>
    [RelayCommand]
    private void SetPublicMode() => ModeService.Instance.SetMode(AppMode.Public);

    /// <summary>Switch to Private mode (Step 17 will add password re-prompt).</summary>
    [RelayCommand]
    private void SetPrivateMode() => ModeService.Instance.SetMode(AppMode.Private);

    // ── Theme Command ─────────────────────────────────────────────────────────

    /// <summary>Alternate between AmberNoir ↔ SaffronLinen.</summary>
    [RelayCommand]
    private void ToggleTheme() => ThemeService.Instance.ToggleTheme();

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Reloads the notes list from the database (called by View after dialog closes).</summary>
    public void RefreshNotes() => LoadNotes();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private bool HasSelectedNote => SelectedNote is not null;

    /// <summary>
    /// Loads notes filtered by the current mode:
    ///   Public  mode → NoteType.Public notes
    ///   Private mode → NoteType.Private notes
    /// </summary>
    private void LoadNotes()
    {
        Notes.Clear();
        var targetType = ModeService.Instance.IsPrivate ? NoteType.Private : NoteType.Public;
        foreach (var note in NoteRepo.GetAll())
        {
            if (note.Type == targetType)
                Notes.Add(note);
        }
    }
}

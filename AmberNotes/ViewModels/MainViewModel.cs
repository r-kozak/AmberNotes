using System.Collections.ObjectModel;
using AmberNotes.Models;
using AmberNotes.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AmberNotes.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    // ── Repositories ──────────────────────────────────────────────────────────
    //   • Public repos are always available (unencrypted public.db).
    //   • Private repos are null until the user enters the master password.

    private readonly NoteRepository _publicNoteRepo;
    private readonly BookRepository _publicBookRepo;

    private NoteRepository? _privateNoteRepo;
    private BookRepository? _privateBookRepo;

    // Services held for lazy unlock of private vault
    private readonly DatabaseService _privateDbService;
    private readonly CryptoService   _cryptoSvc;

    /// <summary>Returns the note repository for the currently active mode.</summary>
    public NoteRepository NoteRepo =>
        ModeService.Instance.IsPrivate && _privateNoteRepo is not null
            ? _privateNoteRepo
            : _publicNoteRepo;

    /// <summary>Returns the book repository for the currently active mode.</summary>
    public BookRepository BookRepo =>
        ModeService.Instance.IsPrivate && _privateBookRepo is not null
            ? _privateBookRepo
            : _publicBookRepo;

    // ── Notes list ────────────────────────────────────────────────────────────
    public ObservableCollection<Note> Notes { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditNoteCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteNoteCommand))]
    private Note? _selectedNote;

    // ── Mode state ────────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPublicMode))]
    private bool _isPrivateMode;

    public bool IsPublicMode => !IsPrivateMode;

    // ── Theme state ───────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ThemeToggleIcon))]
    [NotifyPropertyChangedFor(nameof(ThemeToggleTip))]
    private bool _isDarkTheme;

    public string ThemeToggleIcon => IsDarkTheme ? "☀" : "🌙";
    public string ThemeToggleTip  =>
        IsDarkTheme ? "Перемкнути на світлу тему (Saffron Linen)"
                    : "Перемкнути на темну тему (Amber Noir)";

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>Fired when user taps "🔒 Приватний" and vault is not yet unlocked.</summary>
    public event System.Action? PrivateLoginRequested;

    public event System.Action<int?>? OpenNoteEditRequested;

    // ── Constructor ───────────────────────────────────────────────────────────

    public MainViewModel(
        NoteRepository  publicNoteRepo,
        BookRepository  publicBookRepo,
        DatabaseService privateDbService,
        CryptoService   cryptoSvc)
    {
        _publicNoteRepo   = publicNoteRepo;
        _publicBookRepo   = publicBookRepo;
        _privateDbService = privateDbService;
        _cryptoSvc        = cryptoSvc;

        // Sync initial state from singletons
        _isPrivateMode = ModeService.Instance.IsPrivate;
        _isDarkTheme   = ThemeService.Instance.IsDark;

        // React to mode changes
        ModeService.Instance.ModeChanged += mode =>
        {
            IsPrivateMode = mode == AppMode.Private;
            LoadNotes();
        };

        // React to theme changes
        ThemeService.Instance.ThemeChanged += theme =>
            IsDarkTheme = theme == AppTheme.AmberNoir;

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

    [RelayCommand]
    private void SetPublicMode() => ModeService.Instance.SetMode(AppMode.Public);

    /// <summary>
    /// Switches to Private mode.
    /// • If the private vault was already unlocked this session → switch immediately.
    /// • Otherwise → fires PrivateLoginRequested; App.axaml.cs will show LoginView.
    /// </summary>
    [RelayCommand]
    private void SetPrivateMode()
    {
        if (_privateNoteRepo is not null)
        {
            // Already unlocked this session — just switch
            ModeService.Instance.SetMode(AppMode.Private);
            return;
        }

        // Delegate to App-level navigation (AppViewModel will show LoginView full-screen)
        PrivateLoginRequested?.Invoke();
    }

    // ── Theme Command ─────────────────────────────────────────────────────────

    [RelayCommand]
    private void ToggleTheme() => ThemeService.Instance.ToggleTheme();

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by AppViewModel after successful private vault unlock.
    /// Stores the unlocked repos so SetPrivateMode can use them immediately.
    /// </summary>
    public void SetPrivateRepos(NoteRepository noteRepo, BookRepository bookRepo)
    {
        _privateNoteRepo = noteRepo;
        _privateBookRepo = bookRepo;
    }

    public void RefreshNotes() => LoadNotes();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private bool HasSelectedNote => SelectedNote is not null;

    private void LoadNotes()
    {
        Notes.Clear();

        // Guard: private repo might not be available yet (not yet unlocked)
        var repo = ModeService.Instance.IsPrivate ? _privateNoteRepo : _publicNoteRepo;
        if (repo is null) return;

        var targetType = ModeService.Instance.IsPrivate ? NoteType.Private : NoteType.Public;

        foreach (var note in repo.GetAll())
        {
            if (note.Type == targetType)
                Notes.Add(note);
        }
    }
}

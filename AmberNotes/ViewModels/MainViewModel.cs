using System.Collections.ObjectModel;
using AmberNotes.Models;
using AmberNotes.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AmberNotes.ViewModels;

/// <summary>
/// Shell ViewModel for the main dashboard.
///
/// Responsibilities:
///   • Owns the Notes collection and repo references.
///   • Controls mode (Public / Private) and theme switching.
///   • Manages in-window navigation via CurrentPage:
///       MainListViewModel  → notes list (default)
///       NoteEditViewModel  → note editor (create / edit)
/// </summary>
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
    private readonly DatabaseService  _privateDbService;
    private readonly CryptoService    _cryptoSvc;

    // Cloud storage (Google Drive)
    private readonly GoogleDriveService _driveService;

    // Salt sync (crypto anchor ↔ cloud)
    private readonly SaltSyncService _saltSyncService;

    // Full encrypted sync service
    private readonly SyncService        _syncService;
    private readonly AppSettingsService _appSettings;

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

    // ── Notes list (shared with MainListViewModel) ────────────────────────────
    public ObservableCollection<Note> Notes { get; } = [];

    // ── Navigation ────────────────────────────────────────────────────────────

    /// <summary>
    /// The currently visible sub-page inside MainView.
    /// Switches between MainListViewModel (list) and NoteEditViewModel (editor).
    /// Bound to TransitioningContentControl in MainView.axaml.
    /// </summary>
    [ObservableProperty]
    private ViewModelBase _currentPage = null!;

    private MainListViewModel? _listVm;

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

    // ── Constructor ───────────────────────────────────────────────────────────

    public MainViewModel(
        NoteRepository      publicNoteRepo,
        BookRepository      publicBookRepo,
        DatabaseService     privateDbService,
        CryptoService       cryptoSvc,
        GoogleDriveService  driveService,
        SaltSyncService     saltSyncService,
        SyncService         syncService,
        AppSettingsService  appSettings)
    {
        _publicNoteRepo   = publicNoteRepo;
        _publicBookRepo   = publicBookRepo;
        _privateDbService = privateDbService;
        _cryptoSvc        = cryptoSvc;
        _driveService     = driveService;
        _saltSyncService  = saltSyncService;
        _syncService      = syncService;
        _appSettings      = appSettings;

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

        // Create the list sub-page (shared Notes collection, callbacks for actions)
        _listVm = new MainListViewModel(
            Notes,
            navigateToEditor: GoToEditor,          // string? (UUID or null for new)
            deleteNote:       id => NoteRepo.Delete(id)); // string UUID soft-delete

        CurrentPage = _listVm;
        LoadNotes();
    }

    // ── Navigation ────────────────────────────────────────────────────────────

    /// <summary>
    /// Switches CurrentPage to a NoteEditViewModel for the given note (or new).
    /// On Save → reloads list and goes back.
    /// On Cancel → goes back without changes.
    /// </summary>
    private void GoToEditor(string? noteId)
    {
        var editVm = new NoteEditViewModel(NoteRepo, BookRepo, noteId);

        editVm.Saved     += _ => { LoadNotes(); GoBackToList(); };
        editVm.Cancelled += ()  => GoBackToList();

        CurrentPage = editVm;
    }

    /// <summary>Returns CurrentPage to the notes list.</summary>
    private void GoBackToList() => CurrentPage = _listVm!;

    /// <summary>
    /// Navigates to the Settings page.
    /// Settings button in MainView toolbar fires this command.
    /// </summary>
    [RelayCommand]
    private void GoToSettings()
    {
        var settingsVm = new SettingsViewModel(
            _driveService,
            _saltSyncService,
            _syncService,
            _privateDbService,
            _cryptoSvc,
            _appSettings,
            GoBackToList);
        CurrentPage = settingsVm;
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
            ModeService.Instance.SetMode(AppMode.Private);
            return;
        }

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

    // ── Helpers ───────────────────────────────────────────────────────────────

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

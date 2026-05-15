using System;
using System.IO;
using AmberNotes.Services;
using AmberNotes.ViewModels;
using AmberNotes.Views;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace AmberNotes
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            // ── 1. Theme: ensure AmberNoir is active from the start ───────────────
            Services.ThemeService.Instance.Initialize();

            // ── 2. Public database (always available, no password required) ───────
            var appDataFolder   = GetAppDataFolder();
            var publicDbService = new DatabaseService(Path.Combine(appDataFolder, "public.db"));
            publicDbService.UnlockAsPlain();
            publicDbService.Initialize();      // creates tables + seed book if needed

            var publicNoteRepo = new NoteRepository(publicDbService);
            var publicBookRepo = new BookRepository(publicDbService);

            // ── 3. Private database (encrypted; stays locked until user unlocks) ──
            var cryptoSvc        = new CryptoService(appDataFolder);
            var privateDbService = new DatabaseService(Path.Combine(appDataFolder, "ambernotes.db"));

        // ── 4. Google Drive service (loads persisted tokens automatically) ────
        // Config file lives next to the executable (copied from project root by .csproj).
        // Falls back silently if not found — app runs without Google Drive.
        GoogleAuthConfig.Load(Path.Combine(AppContext.BaseDirectory, GoogleAuthConfig.ConfigFileName));
        var googleAuthSvc  = new GoogleAuthService(appDataFolder);
        var googleDriveSvc = new GoogleDriveService(googleAuthSvc);

        // ── 5. App settings (PendingCloudWipe flag etc.) ──────────────────────
        var appSettingsSvc = new AppSettingsService(appDataFolder);

        // ── 6. Salt sync service (crypto anchor ↔ Google Drive) ──────────────
        var saltSyncSvc = new SaltSyncService(
            cryptoSvc,
            googleDriveSvc,
            privateDbService,
            appSettingsSvc,
            publicNoteRepo);

        // ── 7. Encrypted sync service (full two-way sync) ─────────────────────
        var syncSvc = new SyncService(
            cryptoSvc,
            googleDriveSvc,
            privateDbService,
            publicNoteRepo,
            appSettingsSvc);

        // ── 8. Always start in Public mode — no Login screen at startup ───────
        var appVm  = new AppViewModel();
        var mainVm = appVm.SwitchToMain(publicNoteRepo, publicBookRepo,
                                        privateDbService, cryptoSvc,
                                        googleDriveSvc, saltSyncSvc, syncSvc);

            // ── 9. Wire up Private login flow ─────────────────────────────────────
            //   When user taps "🔒 Приватний", MainViewModel fires PrivateLoginRequested.
            //   AppViewModel shows LoginView full-screen; on success/cancel returns to MainView.
            mainVm.PrivateLoginRequested += () =>
                appVm.ShowPrivateLogin(cryptoSvc, privateDbService, mainVm);

            // ── 10. Wire up platform lifetime ─────────────────────────────────────
            SetupLifetime(appVm);

            base.OnFrameworkInitializationCompleted();
        }

        private void SetupLifetime(AppViewModel appVm)
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.MainWindow = new MainWindow { DataContext = appVm };
            else if (ApplicationLifetime is IActivityApplicationLifetime activity)
                activity.MainViewFactory = () => new AppView { DataContext = appVm };
            else if (ApplicationLifetime is ISingleViewApplicationLifetime single)
                single.MainView = new AppView { DataContext = appVm };
        }

        // ── App data folder (all platform-specific databases & tokens) ────────────

        /// <summary>
        /// Root folder for all AmberNotes data files.
        ///   Desktop  → %LOCALAPPDATA%\AmberNotes\
        ///   Android  → /data/data/{pkg}/files/AmberNotes\
        /// </summary>
        private static string GetAppDataFolder()
        {
            var root = Environment.GetFolderPath(
                OperatingSystem.IsAndroid()
                    ? Environment.SpecialFolder.Personal
                    : Environment.SpecialFolder.LocalApplicationData);

            var folder = Path.Combine(root, "AmberNotes");
            Directory.CreateDirectory(folder);   // ensure it exists
            return folder;
        }
    }
}

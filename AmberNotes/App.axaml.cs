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
            var publicDbPath    = GetPublicDatabasePath();
            var publicDbService = new DatabaseService(publicDbPath);
            publicDbService.UnlockAsPlain();
            publicDbService.Initialize();      // creates tables + seed book if needed

            var publicNoteRepo = new NoteRepository(publicDbService);
            var publicBookRepo = new BookRepository(publicDbService);

            // ── 3. Private database (encrypted; stays locked until user unlocks) ──
            var privateDbPath    = GetPrivateDatabasePath();
            var cryptoSvc        = new CryptoService(Path.GetDirectoryName(privateDbPath)!);
            var privateDbService = new DatabaseService(privateDbPath);

            // ── 4. Always start in Public mode — no Login screen at startup ───────
            var appVm  = new AppViewModel();
            var mainVm = appVm.SwitchToMain(publicNoteRepo, publicBookRepo,
                                            privateDbService, cryptoSvc);

            // ── 5. Wire up Private login flow ─────────────────────────────────────
            //   When user taps "🔒 Приватний", MainViewModel fires PrivateLoginRequested.
            //   AppViewModel shows LoginView full-screen; on success/cancel returns to MainView.
            mainVm.PrivateLoginRequested += () =>
                appVm.ShowPrivateLogin(cryptoSvc, privateDbService, mainVm);

            // ── 6. Wire up platform lifetime ─────────────────────────────────────
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

        // ── Database path helpers ─────────────────────────────────────────────────

        /// <summary>
        /// Path for the unencrypted public database.
        ///   Desktop  → %LOCALAPPDATA%\AmberNotes\public.db
        ///   Android  → /data/data/{pkg}/files/public.db
        /// </summary>
        private static string GetPublicDatabasePath()
        {
            var folder = Environment.GetFolderPath(
                OperatingSystem.IsAndroid()
                    ? Environment.SpecialFolder.Personal
                    : Environment.SpecialFolder.LocalApplicationData);

            return Path.Combine(folder, "AmberNotes", "public.db");
        }

        /// <summary>
        /// Path for the SQLCipher-encrypted private database.
        ///   Desktop  → %LOCALAPPDATA%\AmberNotes\ambernotes.db
        ///   Android  → /data/data/{pkg}/files/ambernotes.db
        /// </summary>
        private static string GetPrivateDatabasePath()
        {
            var folder = Environment.GetFolderPath(
                OperatingSystem.IsAndroid()
                    ? Environment.SpecialFolder.Personal
                    : Environment.SpecialFolder.LocalApplicationData);

            return Path.Combine(folder, "AmberNotes", "ambernotes.db");
        }
    }
}

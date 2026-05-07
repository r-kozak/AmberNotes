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
            // ── Theme: apply default AmberNoir immediately ────────────────────
            Services.ThemeService.Instance.Initialize();

            // ── Infrastructure ────────────────────────────────────────────────
            var dbPath    = GetDatabasePath();
            var dbService = new DatabaseService(dbPath);
            var cryptoSvc = new CryptoService(Path.GetDirectoryName(dbPath)!);

            // ── App-level navigation ──────────────────────────────────────────
            var loginVm = new LoginViewModel(cryptoSvc, dbService);
            var appVm   = new AppViewModel(loginVm);

            // After successful login: build repositories and switch to main view
            loginVm.LoginSucceeded += () =>
            {
                var noteRepo = new NoteRepository(dbService);
                var bookRepo = new BookRepository(dbService);
                appVm.SwitchToMain(noteRepo, bookRepo);
            };
            // ─────────────────────────────────────────────────────────────────

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // Desktop: AppViewModel drives MainWindow via ContentControl + ViewLocator
                desktop.MainWindow = new MainWindow
                {
                    DataContext = appVm
                };
            }
            else if (ApplicationLifetime is IActivityApplicationLifetime activityLifetime)
            {
                // Android: AppView is the mobile root (mirrors MainWindow structure)
                activityLifetime.MainViewFactory = () => new AppView
                {
                    DataContext = appVm
                };
            }
            else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
            {
                singleViewPlatform.MainView = new AppView
                {
                    DataContext = appVm
                };
            }

            base.OnFrameworkInitializationCompleted();
        }

        /// <summary>
        /// Returns the platform-appropriate path for the SQLite database file.
        ///   Desktop  → %LOCALAPPDATA%\AmberNotes\ambernotes.db
        ///   Android  → /data/data/{package}/files/ambernotes.db  (Personal folder)
        /// </summary>
        private static string GetDatabasePath()
        {
            var folder = Environment.GetFolderPath(
                OperatingSystem.IsAndroid()
                    ? Environment.SpecialFolder.Personal
                    : Environment.SpecialFolder.LocalApplicationData);

            return Path.Combine(folder, "AmberNotes", "ambernotes.db");
        }
    }
}

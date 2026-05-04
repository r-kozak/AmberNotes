using AmberNotes.Services;
using AmberNotes.ViewModels;
using AmberNotes.Views;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using System;
using System.IO;

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
            // ── Database initialization ───────────────────────────────────────
            var dbPath = GetDatabasePath();
            var dbService = new DatabaseService(dbPath);
            dbService.Initialize();
            // ─────────────────────────────────────────────────────────────────

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow
                {
                    DataContext = new MainViewModel()
                };
            }
            else if (ApplicationLifetime is IActivityApplicationLifetime activityLifetime)
            {
                activityLifetime.MainViewFactory = () => new MainView { DataContext = new MainViewModel() };
            }
            else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
            {
                singleViewPlatform.MainView = new MainView
                {
                    DataContext = new MainViewModel()
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
                    ? Environment.SpecialFolder.Personal          // Android app-private storage
                    : Environment.SpecialFolder.LocalApplicationData); // Desktop

            return Path.Combine(folder, "AmberNotes", "ambernotes.db");
        }
    }
}

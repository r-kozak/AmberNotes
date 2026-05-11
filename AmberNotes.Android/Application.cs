using Android.App;
using Android.Content;
using Android.Runtime;
using AmberNotes.Services;
using Avalonia;
using Avalonia.Android;
using SQLitePCL;
using System.Threading.Tasks;
using System.IO;

namespace AmberNotes.Android
{
    [Application]
    public class Application : AvaloniaAndroidApplication<App>
    {
        protected Application(nint javaReference, JniHandleOwnership transfer)
            : base(javaReference, transfer) { }

        public override void OnCreate()
        {
            // ── 1. SQLitePCLRaw must be initialized before any SQLite usage ──
            Batteries_V2.Init();

            // ── 2. Load OAuth config from bundled asset (oauth.config.json) ──
            //   On Android, AppContext.BaseDirectory is inside the APK (read-only),
            //   so we read the file via Android Assets API instead.
            try
            {
                using var stream = Assets!.Open(GoogleAuthConfig.ConfigFileName);
                GoogleAuthConfig.Load(stream);
            }
            catch
            {
                // File absent → Google Drive stays disabled (ShowSetupHint = true in Settings).
            }

            // ── 3. Register Android-specific OAuth browser launcher ───────────
            //   Opens the system default browser (or Chrome) with the Google
            //   authorization URL. The user is redirected back via the custom
            //   URI scheme com.kozak.ambernotes://oauth2callback which is
            //   handled by MainActivity.OnNewIntent → GoogleAuthService.HandleAndroidCallback.
            GoogleAuthService.BrowserLauncher = url =>
            {
                var intent = new Intent(Intent.ActionView, global::Android.Net.Uri.Parse(url));
                intent.AddFlags(ActivityFlags.NewTask);
                ApplicationContext!.StartActivity(intent);
                return Task.CompletedTask;
            };

            base.OnCreate();
        }

        protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
        {
            return base.CustomizeAppBuilder(builder)
                .WithInterFont();
        }
    }
}

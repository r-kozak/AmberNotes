using Android.App;
using Android.Content;
using Android.Runtime;
using AmberNotes.Services;
using Avalonia;
using Avalonia.Android;
using SQLitePCL;
using System.Threading.Tasks;

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

            // ── 2. Register Android-specific OAuth browser launcher ───────────
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

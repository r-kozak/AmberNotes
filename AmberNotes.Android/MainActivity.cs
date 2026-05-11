using Android.App;
using Android.Content;
using Android.Content.PM;
using AmberNotes.Services;
using Avalonia;
using Avalonia.Android;

namespace AmberNotes.Android
{
    [Activity(
        Label = "AmberNotes",
        Theme = "@style/MyTheme.NoActionBar",
        Icon = "@drawable/icon",
        MainLauncher = true,
        LaunchMode = LaunchMode.SingleTop,
        ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
    public class MainActivity : AvaloniaMainActivity
    {
        // ── OAuth 2.0 callback ────────────────────────────────────────────────
        //
        // After the user completes Google Sign-In in the browser, Google
        // redirects to: com.kozak.ambernotes://oauth2callback?code=...
        //
        // Because LaunchMode = SingleTop, if MainActivity is already running
        // OnNewIntent is called (instead of re-creating the activity).
        // OnCreate handles the case where the app was cold-started via the URI.

        protected override void OnCreate(Android.OS.Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            HandleOAuthIntent(Intent);
        }

        protected override void OnNewIntent(Intent? intent)
        {
            base.OnNewIntent(intent);
            HandleOAuthIntent(intent);
        }

        // ── Private ───────────────────────────────────────────────────────────

        private static void HandleOAuthIntent(Intent? intent)
        {
            if (intent?.Action != Intent.ActionView) return;
            if (intent.Data?.Scheme != "com.kozak.ambernotes") return;

            var code  = intent.Data.GetQueryParameter("code");
            var error = intent.Data.GetQueryParameter("error");

            // Hand the code (or error) back to GoogleAuthService
            GoogleAuthService.HandleAndroidCallback(code, error);
        }
    }
}

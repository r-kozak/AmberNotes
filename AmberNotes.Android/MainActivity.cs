using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AmberNotes.Services;
using Avalonia.Android;

namespace AmberNotes.Android
{
    [Activity(
        Label = "AmberNotes",
        Theme = "@style/MyTheme.NoActionBar",
        Icon = "@drawable/icon",
        MainLauncher = true,
        LaunchMode = LaunchMode.SingleTop,
        ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode,
        Exported = true)]
    [IntentFilter(
        new[] { Intent.ActionView },
        Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
        DataScheme = "com.googleusercontent.apps.607220537111-qtlflmn1e03vh4p4bkn87tv76vimhe4d",
        Label = "OAuth2 Redirect")]
    public class MainActivity : AvaloniaMainActivity
    {
        // ── OAuth 2.0 callback ────────────────────────────────────────────────
        //
        // After the user completes Google Sign-In in the browser, Google
        // redirects to: {REVERSE_CLIENT_ID}:/oauth2redirect?code=...
        //
        // The REVERSE_CLIENT_ID scheme is registered in AndroidManifest.xml.
        // Application.OnCreate() loads oauth.config.json BEFORE any activity is
        // created, so GoogleAuthConfig.ReverseAndroidClientId is already set here.
        //
        // Because LaunchMode = SingleTop, if MainActivity is already running
        // OnNewIntent is called (instead of re-creating the activity).
        // OnCreate handles the case where the app was cold-started via the URI.

        protected override void OnCreate(Bundle? savedInstanceState)
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

            var data = intent.Data;
            if (data is null) return;

            // Match the reverse-client-id scheme dynamically (loaded at startup
            // from oauth.config.json by Application.OnCreate via Android Assets).
            var expectedScheme = GoogleAuthConfig.ReverseAndroidClientId;
            if (string.IsNullOrEmpty(expectedScheme)) return;
            if (data.Scheme != expectedScheme) return;

            var code  = data.GetQueryParameter("code");
            var error = data.GetQueryParameter("error");

            // Hand the code (or error) back to GoogleAuthService
            GoogleAuthService.HandleAndroidCallback(code, error);
        }
    }
}

using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using SQLitePCL;

namespace AmberNotes.Android
{
    [Application]
    public class Application : AvaloniaAndroidApplication<App>
    {
        protected Application(nint javaReference, JniHandleOwnership transfer) : base(javaReference, transfer)
        {
        }

        public override void OnCreate()
        {
            // Initialize SQLitePCLRaw native provider before any SQLite usage
            Batteries_V2.Init();
            base.OnCreate();
        }

        protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
        {
            return base.CustomizeAppBuilder(builder)
                .WithInterFont();
        }
    }
}

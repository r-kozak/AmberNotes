using AmberNotes.Models;
using AmberNotes.Services;
using AmberNotes.ViewModels;
using Avalonia.Controls;

namespace AmberNotes.Views;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.OpenNoteEditRequested += OnOpenNoteEditRequested;
    }

    private async void OnOpenNoteEditRequested(int? noteId)
    {
        if (DataContext is not MainViewModel mainVm) return;

        var editVm = new NoteEditViewModel(mainVm.NoteRepo, mainVm.BookRepo, noteId);
        var editView = new NoteEditView(editVm);

        var topLevel = TopLevel.GetTopLevel(this);

        if (topLevel is Window parentWindow)
        {
            // ── Desktop Logic: Show in a real Window ──
            var window = new Window
            {
                Title = editVm.WindowTitle,
                Content = editView,
                Width = 560,
                Height = 520,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = true
            };

            void OnSaved(Note note) => window.Close(true);
            void OnCancelled() => window.Close(false);

            editVm.Saved += OnSaved;
            editVm.Cancelled += OnCancelled;

            await window.ShowDialog(parentWindow);

            editVm.Saved -= OnSaved;
            editVm.Cancelled -= OnCancelled;
        }
        else
        {
            // ── Android/Mobile Logic: Show as Overlay ──
            DialogContent.Content = editView;
            DialogOverlay.IsVisible = true;

            var tcs = new System.Threading.Tasks.TaskCompletionSource<bool>();

            void OnSaved(Note note) { tcs.TrySetResult(true); }
            void OnCancelled() { tcs.TrySetResult(false); }

            editVm.Saved += OnSaved;
            editVm.Cancelled += OnCancelled;

            await tcs.Task;

            editVm.Saved -= OnSaved;
            editVm.Cancelled -= OnCancelled;

            DialogOverlay.IsVisible = false;
            DialogContent.Content = null;
        }

        mainVm.RefreshNotes();
    }
}

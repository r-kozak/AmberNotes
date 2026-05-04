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

        // Resolve services from the ViewModel (passed via constructor)
        var noteRepo = mainVm.NoteRepo;
        var bookRepo = mainVm.BookRepo;

        var editVm = new NoteEditViewModel(noteRepo, bookRepo, noteId);
        var dialog = new NoteEditView(editVm);

        // Find the parent Window to use as owner
        var parentWindow = TopLevel.GetTopLevel(this) as Window;
        if (parentWindow is not null)
            await dialog.ShowDialog(parentWindow);
        else
            dialog.Show();

        // Refresh list after dialog closes
        mainVm.RefreshNotes();
    }
}

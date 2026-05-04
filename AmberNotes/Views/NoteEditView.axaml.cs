using AmberNotes.ViewModels;
using Avalonia.Controls;

namespace AmberNotes.Views;

public partial class NoteEditView : Window
{
    public NoteEditView()
    {
        InitializeComponent();
    }

    public NoteEditView(NoteEditViewModel vm) : this()
    {
        DataContext = vm;
        vm.Saved      += _ => Close(true);
        vm.Cancelled  += () => Close(false);
    }
}

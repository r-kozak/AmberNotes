using AmberNotes.ViewModels;
using Avalonia.Controls;

namespace AmberNotes.Views;

public partial class NoteEditView : UserControl
{
    public NoteEditView()
    {
        InitializeComponent();
    }

    public NoteEditView(NoteEditViewModel vm) : this()
    {
        DataContext = vm;
    }
}

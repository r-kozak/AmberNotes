using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AmberNotes.ViewModels
{
    public partial class MainViewModel : ViewModelBase
    {
        [ObservableProperty]
        private string _greeting = "Привіт, Amber Notes!";

        [RelayCommand]
        private void CreateNote()
        {
            Greeting = "Бурштинову нотатку створено! ✨";
        }
    }
}

using System.Windows.Input;

namespace Termox.ViewModels;

public interface ITabViewModel
{
    string Title { get; }
    ICommand CloseTabCommand { get; }
    ICommand DisconnectCommand { get; }
}

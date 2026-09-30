using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using LoqNova.Avalonia.Services;

namespace LoqNova.Avalonia.ViewModels.Dialogs;

/// <summary>
/// Base for the settings dialogs. A dialog dismisses itself through
/// <see cref="CloseAsync"/>, which asks the dialog service to clear the host rather
/// than reaching for the window, so a dialog never needs to know how it is presented.
/// </summary>
public abstract partial class DialogViewModelBase : ViewModelBase
{
    private readonly IDialogService? _dialogs;

    protected DialogViewModelBase()
    {
    }

    protected DialogViewModelBase(IDialogService dialogs)
    {
        _dialogs = dialogs;
    }

    [RelayCommand]
    private Task CloseAsync()
    {
        _dialogs?.Close();
        return Task.CompletedTask;
    }
}
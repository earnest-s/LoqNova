using System;
using System.Collections.ObjectCollection;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LoqNova.Avalonia.ViewModels.Dialogs;

public partial class ExcludeRefreshRatesViewModel : ViewModelBase
{
    [ObservableProperty]
    private ObservableCollection<int> _excludedRates = new() { 48, 50, 59, 60 };
    
    [ObservableProperty]
    private int _newRate = 0;
    
    public ExcludeRefreshRatesViewModel()
    {
    }
    
    [RelayCommand]
    private void AddRate()
    {
        if (NewRate > 0 && !ExcludedRates.Contains(NewRate))
        {
            ExcludedRates.Add(NewRate);
            NewRate = 0;
        }
    }
    
    [RelayCommand]
    private void RemoveRate(int rate)
    {
        ExcludedRates.Remove(rate);
    }
    
    [RelayCommand]
    private async Task SaveAsync()
    {
        // Save excluded rates
    }
    
    [RelayCommand]
    private async Task CloseAsync()
    {
    }
}
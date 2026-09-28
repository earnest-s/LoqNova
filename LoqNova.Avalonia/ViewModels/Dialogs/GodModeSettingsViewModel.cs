using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoqNova.Avalonia.Services;

namespace LoqNova.Avalonia.ViewModels.Dialogs;

/// <summary>
/// God Mode state as reported by the machine. The numeric CPU/GPU limit fields
/// that previously lived here were placeholders: they were never read from or
/// written to hardware. The real values live in
/// <c>GodModeSettings.GodModeSettingsStore.Preset</c> and are applied through
/// <c>IGodModeController</c>, so they are surfaced there instead of being
/// invented here.
/// </summary>
public partial class GodModeSettingsViewModel : ViewModelBase
{
    private readonly IPerformanceService _performanceService;

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private bool _isSupported;

    public GodModeSettingsViewModel(IPerformanceService performanceService)
    {
        _performanceService = performanceService;
        LoadCurrentState();
    }

    private void LoadCurrentState()
    {
        IsSupported = _performanceService.IsGodModeSupported;
        IsEnabled = _performanceService.IsGodModeEnabled;
    }

    partial void OnIsEnabledChanged(bool value)
    {
        if (!IsSupported)
            return;

        if (value)
            _ = _performanceService.SetModeAsync(LoqNova.Lib.PowerModeState.GodMode);
        else
            _ = _performanceService.SetModeAsync(LoqNova.Lib.PowerModeState.Balance);
    }

    [RelayCommand]
    private Task ApplyAsync() => _performanceService.ApplyGodModeSettingsAsync();

    [RelayCommand]
    private Task RefreshAsync() => _performanceService.RefreshAsync();
}

using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoqNova.Avalonia.Services;
using LoqNova.Lib;
using LoqNova.Lib.Utils;

namespace LoqNova.Avalonia.ViewModels.Dialogs;

/// <summary>
/// Device information (WPF DeviceInformationWindow equivalent). Shows the five
/// device fields the WPF window shows, read from
/// <c>Compatibility.GetMachineInformationAsync</c>, plus warranty dates from
/// <c>WarrantyChecker</c>. Fields the WPF window does not display (GPU, memory,
/// storage, display, Windows build, CPU model) are intentionally not invented
/// here; they belong to the device information page if it is ever ported.
/// </summary>
public partial class DeviceInformationViewModel : ViewModelBase
{
    private const string Unknown = "-";

    [ObservableProperty]
    private string _manufacturer = Unknown;

    [ObservableProperty]
    private string _model = Unknown;

    [ObservableProperty]
    private string _machineType = Unknown;

    [ObservableProperty]
    private string _serialNumber = Unknown;

    [ObservableProperty]
    private string _biosVersion = Unknown;

    [ObservableProperty]
    private string _warrantyStart = Unknown;

    [ObservableProperty]
    private string _warrantyEnd = Unknown;

    [ObservableProperty]
    private Uri? _warrantyLink;

    /// <summary>True while warranty information is being fetched.</summary>
    [ObservableProperty]
    private bool _isLoadingWarranty;

    [ObservableProperty]
    private string _appVersion = string.Empty;

    public DeviceInformationViewModel()
    {
        var assembly = System.Reflection.Assembly.GetEntryAssembly();
        AppVersion = assembly?.GetName().Version?.ToString(3) ?? Unknown;
    }

    public async Task InitializeAsync()
    {
        await LibContainer.Initialization.ConfigureAwait(false);

        await RefreshAsync().ConfigureAwait(false);
    }

    [RelayCommand]
    private async Task RefreshAsync(bool forceRefresh = false)
    {
        try
        {
            var mi = await Compatibility.GetMachineInformationAsync().ConfigureAwait(false);

            Manufacturer = string.IsNullOrWhiteSpace(mi.Vendor) ? Unknown : mi.Vendor;
            Model = string.IsNullOrWhiteSpace(mi.Model) ? Unknown : mi.Model;
            MachineType = string.IsNullOrWhiteSpace(mi.MachineType) ? Unknown : mi.MachineType;
            SerialNumber = string.IsNullOrWhiteSpace(mi.SerialNumber) ? Unknown : mi.SerialNumber;
            BiosVersion = string.IsNullOrWhiteSpace(mi.BiosVersionRaw) ? Unknown : mi.BiosVersionRaw;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Machine information unavailable: {ex.Message}");
        }

        await LoadWarrantyAsync(forceRefresh).ConfigureAwait(false);
    }

    private async Task LoadWarrantyAsync(bool forceRefresh)
    {
        IsLoadingWarranty = true;
        WarrantyStart = Unknown;
        WarrantyEnd = Unknown;
        WarrantyLink = null;

        try
        {
            var checker = LoqNova.Lib.IoCContainer.Resolve<WarrantyChecker>();
            var mi = await Compatibility.GetMachineInformationAsync().ConfigureAwait(false);

            var warranty = await checker.GetWarrantyInfo(mi, forceRefresh).ConfigureAwait(false);
            if (warranty is not { } info)
                return;

            WarrantyStart = info.Start?.ToString("d", LoqNova.Avalonia.Localization.LocalizationHelper.CurrentCulture) ?? Unknown;
            WarrantyEnd = info.End?.ToString("d", LoqNova.Avalonia.Localization.LocalizationHelper.CurrentCulture) ?? Unknown;
            WarrantyLink = info.Link;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Couldn't load warranty info: {ex.Message}");
        }
        finally
        {
            IsLoadingWarranty = false;
        }
    }

    [RelayCommand]
    private Task CloseAsync() => Task.CompletedTask;
}

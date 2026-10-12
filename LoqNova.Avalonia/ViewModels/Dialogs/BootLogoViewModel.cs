using System;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoqNova.Avalonia.Localization;
using LoqNova.Avalonia.Services;
using LoqNova.Lib.System;
using LoqNova.Lib.Utils;
using IMainThreadDispatcher = LoqNova.Avalonia.Services.IMainThreadDispatcher;

namespace LoqNova.Avalonia.ViewModels.Dialogs;

/// <summary>
/// Boot logo settings, reproducing WPF's <c>BootLogoWindow</c>. It reads the real
/// status from <see cref="BootLogo"/> and enables or reverts the logo through the same
/// static API, reporting the same success and failure strings.
/// </summary>
public partial class BootLogoViewModel : DialogViewModelBase
{
    private readonly IMainThreadDispatcher _dispatcher;
    private readonly IFileDialogService _fileDialogService;

    private string[] _filters = [];

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private bool _isCustomLogoSet;

    [ObservableProperty]
    private bool _isDefaultLogoSet = true;

    [ObservableProperty]
    private string? _resultMessage;

    [ObservableProperty]
    private bool _isResultSuccess;

    [ObservableProperty]
    private bool _isBusy;

    public string Title => L("BootLogoWindow_Title");

    public string StatusText => L("BootLogoWindow_Status");

    public string DefaultLogoSetText => L("BootLogoWindow_DefaultLogoSet");

    public string CustomLogoSetText => L("BootLogoWindow_CustomLogoSet");

    public string CustomizeText => L("BootLogoWindow_Customize");

    public string RevertToDefaultText => L("RevertToDefault");

    public BootLogoViewModel(IMainThreadDispatcher dispatcher, IFileDialogService fileDialogService, IDialogService dialogs)
        : base(dialogs)
    {
        _dispatcher = dispatcher;
        _fileDialogService = fileDialogService;
    }

    public async Task InitializeAsync()
    {
        await LibContainer.Initialization.ConfigureAwait(false);
        await RefreshAsync().ConfigureAwait(false);
    }

    private async Task RefreshAsync()
    {
        // GetStatus queries the firmware, so it is kept off the UI thread.
        try
        {
            var status = await Task.Run(() => BootLogo.GetStatus()).ConfigureAwait(false);
            var (enabled, resolution, formats, filters) = status;

            _filters = filters;

            var description = string.Format(
                L("BootLogoWindow_Description"),
                resolution.DisplayName,
                string.Join(", ", formats.Select(format => format.ToString().ToUpperInvariant())));

            await _dispatcher.InvokeAsync(() =>
            {
                Description = description;
                IsCustomLogoSet = enabled;
                IsDefaultLogoSet = !enabled;
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Boot logo status could not be read: {ex}");
        }
    }

    [RelayCommand]
    private async Task CustomizeAsync()
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;

            var filter = $"Images|{string.Join(";", _filters)}";
            var file = await _fileDialogService
                .ShowOpenFileDialogAsync(L("BootLogoWindow_Customize"), filter)
                .ConfigureAwait(false);

            if (string.IsNullOrEmpty(file))
            {
                return;
            }

            await BootLogo.EnableAsync(file).ConfigureAwait(false);

            await SetResultAsync(L("BootLogoWindow_SetCustomSuccess"), true).ConfigureAwait(false);

            await RefreshAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
            {
                Log.Instance.Trace("Custom logo could not be set.", ex);
            }

            await SetResultAsync(
                string.Format(L("BootLogoWindow_SetCustomFailed"), GetDescription(ex)),
                false).ConfigureAwait(false);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RevertToDefaultAsync()
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;

            await BootLogo.DisableAsync().ConfigureAwait(false);

            await SetResultAsync(L("BootLogoWindow_SetDefaultSuccess"), true).ConfigureAwait(false);

            await RefreshAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
            {
                Log.Instance.Trace("Default logo could not be set.", ex);
            }

            await SetResultAsync(
                string.Format(L("BootLogoWindow_SetDefaultFailed"), GetDescription(ex)),
                false).ConfigureAwait(false);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private Task SetResultAsync(string message, bool success) =>
        _dispatcher.InvokeAsync(() =>
        {
            ResultMessage = message;
            IsResultSuccess = success;
        });

    private static string GetDescription(Exception exception) => exception switch
    {
        CantSetUEFIPrivilegeException => L("BootLogoWindow_SetError_Cannot_Set_UEFI_Privilege"),
        CantMountUEFIPartitionException => L("BootLogoWindow_SetError_Cannot_Mount_EFI_Partition"),
        NotEnoughSpaceOnUEFIPartitionException => L("BootLogoWindow_SetError_Not_Enough_Free_Space_On_EFI_Partition"),
        InvalidBootLogoImageSizeException => L("BootLogoWindow_SetError_Invalid_Image_Size"),
        InvalidBootLogoImageFormatException => L("BootLogoWindow_SetError_Invalid_Image_Format"),
        _ => exception.Message
    };

    private static string L(string key) => LocalizationHelper.GetString(key);
}

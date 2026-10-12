using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoqNova.Avalonia.Localization;
using LoqNova.Avalonia.Services;
using LoqNova.Lib;
using LoqNova.Lib.Features;
using LoqNova.Lib.Settings;

namespace LoqNova.Avalonia.ViewModels.Dialogs;

/// <summary>
/// Excluded refresh rates, reproducing WPF's <c>ExcludeRefreshRatesWindow</c>.
/// <para>
/// The list is the union of the rates the display currently reports and the ones
/// already excluded, ordered by frequency, exactly like WPF. A rate the user unchecks
/// is written to <c>ExcludedRefreshRates</c> on Save.
/// </para>
/// <para>
/// WPF raises a modal message box when no rates are found. Avalonia has no modal
/// message box, so the same text is shown inside the dialog with the same "Try again"
/// and "Cancel" choices.
/// </para>
/// </summary>
public partial class ExcludeRefreshRatesViewModel : DialogViewModelBase
{
    private readonly IMainThreadDispatcher _dispatcher;

    private RefreshRateFeature? _feature;
    private ApplicationSettings? _settings;

    [ObservableProperty]
    private ObservableCollection<RefreshRateItem> _refreshRates = new();

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>True when the display reports no rates, matching WPF's message box state.</summary>
    [ObservableProperty]
    private bool _hasNoRefreshRates;

    public string Title => Resource.ExcludeRefreshRatesWindow_Title;

    public string Description => Resource.ExcludeRefreshRatesWindow_Description;

    public string NoRefreshRatesTitle => Resource.ExcludeRefreshRatesWindow_NoRefreshRatesFound_Title;

    public string NoRefreshRatesMessage => Resource.ExcludeRefreshRatesWindow_NoRefreshRatesFound_Message;

    public string TryAgainText => Resource.TryAgain;

    public ExcludeRefreshRatesViewModel(IMainThreadDispatcher dispatcher, IDialogService dialogs)
        : base(dialogs)
    {
        _dispatcher = dispatcher;
    }

    public async Task InitializeAsync()
    {
        await LibContainer.Initialization.ConfigureAwait(false);

        _feature ??= LoqNova.Lib.IoCContainer.Resolve<RefreshRateFeature>();
        _settings ??= LoqNova.Lib.IoCContainer.Resolve<ApplicationSettings>();

        await LoadAsync().ConfigureAwait(false);
    }

    /// <summary>WPF's "Try again" branch: re-runs the whole load.</summary>
    [RelayCommand]
    private async Task RetryAsync() => await LoadAsync().ConfigureAwait(false);

    private async Task LoadAsync()
    {
        if (_feature is null || _settings is null)
        {
            return;
        }

        await _dispatcher.InvokeAsync(() =>
        {
            IsBusy = true;
            HasNoRefreshRates = false;
        }).ConfigureAwait(false);

        try
        {
            // WPF holds the loader for at least half a second so a fast query does not
            // flash the spinner.
            var minimumDelay = Task.Delay(500);

            var refreshRates = await _feature.GetAllStatesAsync().ConfigureAwait(false);
            var excluded = _settings.Store.ExcludedRefreshRates;

            if (refreshRates.Length == 0)
            {
                await minimumDelay.ConfigureAwait(false);

                await _dispatcher.InvokeAsync(() =>
                {
                    RefreshRates.Clear();
                    HasNoRefreshRates = true;
                }).ConfigureAwait(false);

                return;
            }

            var items = refreshRates
                .Union(excluded)
                .Distinct()
                .OrderBy(rate => rate.Frequency)
                .Select(rate => new RefreshRateItem(rate) { IsIncluded = !excluded.Contains(rate) })
                .ToArray();

            await minimumDelay.ConfigureAwait(false);

            await _dispatcher.InvokeAsync(() =>
            {
                RefreshRates.Clear();
                foreach (var item in items)
                {
                    RefreshRates.Add(item);
                }

                HasNoRefreshRates = false;
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Refresh rates could not be loaded: {ex}");
        }
        finally
        {
            await _dispatcher.InvokeAsync(() => IsBusy = false).ConfigureAwait(false);
        }
    }

    [RelayCommand]
    private void Save()
    {
        if (_settings is null)
        {
            return;
        }

        var excluded = RefreshRates.Where(item => !item.IsIncluded).Select(item => item.RefreshRate);

        _settings.Store.ExcludedRefreshRates.Clear();
        _settings.Store.ExcludedRefreshRates.AddRange(excluded);
        _settings.SynchronizeStore();

        CloseCommand.Execute(null);
    }
}

/// <summary>A rate row the user can include, matching WPF's inline <c>ListItem</c>.</summary>
public partial class RefreshRateItem(RefreshRate refreshRate) : ViewModelBase
{
    public RefreshRate RefreshRate { get; } = refreshRate;

    public string DisplayName => RefreshRate.DisplayName;

    /// <summary>Checked means "not excluded"; WPF stores the unchecked rows.</summary>
    [ObservableProperty]
    private bool _isIncluded = true;
}

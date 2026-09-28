using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using LoqNova.Avalonia.Services;
using LoqNova.Lib;
using LoqNova.Lib.Features;
using LoqNova.Lib.Messaging;
using LoqNova.Lib.Messaging.Messages;

namespace LoqNova.Avalonia.ViewModels.Controls;

/// <summary>
/// Shared presentation state for a dashboard feature widget. A widget is only
/// shown when the backend reports the feature as supported, which mirrors WPF's
/// <c>AbstractRefreshingControl</c>, which collapses itself by throwing
/// <see cref="NotSupportedException"/> from <c>OnRefreshAsync</c>.
/// </summary>
public abstract partial class FeatureWidgetViewModel : ViewModelBase
{
    protected readonly IMainThreadDispatcher Dispatcher;

    /// <summary>Resolves the library feature and performs the first read.</summary>
    public abstract Task InitializeAsync();

    /// <summary>MessagingCenter subscription token.</summary>
    protected readonly object Subscriber = new();

    protected FeatureWidgetViewModel(IMainThreadDispatcher dispatcher)
    {
        Dispatcher = dispatcher;
    }

    public string Title { get; init; } = string.Empty;

    public string Icon { get; init; } = string.Empty;

    /// <summary>True for a two-state feature rendered as a switch.</summary>
    public bool IsToggle { get; protected init; }

    /// <summary>True for a multi-state feature rendered as a combo box.</summary>
    public bool IsChoice { get; protected init; }

    /// <summary>False when the backend reports the feature as unsupported. Hidden when false.</summary>
    [ObservableProperty]
    private bool _isAvailable;

    /// <summary>True while a backend read or write is in flight.</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Set when the last backend operation failed. Cleared on success.</summary>
    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>Advisory text (for example running on battery), cleared when no longer applicable.</summary>
    [ObservableProperty]
    private string? _warning;
}

/// <summary>
/// Generic adapter over a library <see cref="IFeature{T}"/>. Reads and writes go
/// through that feature so availability checks, hardware guards and side effects
/// (OSD, strobe, dependent features) behave exactly as in WPF. State changes are
/// observed through <see cref="FeatureStateMessage{T}"/>, the same
/// <c>MessagingCenter</c> subscription the WPF cards use.
/// </summary>
public abstract partial class FeatureWidgetViewModel<TState> : FeatureWidgetViewModel
    where TState : struct
{
    private IFeature<TState>? _feature;

    protected FeatureWidgetViewModel(IMainThreadDispatcher dispatcher) : base(dispatcher)
    {
    }

    protected IFeature<TState> Feature => _feature ??
        throw new InvalidOperationException($"{GetType().Name}.InitializeAsync must complete before use.");

    public override async Task InitializeAsync()
    {
        await LibContainer.Initialization.ConfigureAwait(false);

        _feature = LoqNova.Lib.IoCContainer.Resolve<IFeature<TState>>();

        MessagingCenter.Subscribe<FeatureStateMessage<TState>>(Subscriber, OnFeatureStateMessage);

        await RefreshAsync().ConfigureAwait(false);
    }

    /// <summary>Re-reads the feature. Safe to call repeatedly; never throws.</summary>
    public async Task RefreshAsync()
    {
        if (_feature is null)
            return;

        try
        {
            if (!await _feature.IsSupportedAsync().ConfigureAwait(false))
            {
                IsAvailable = false;
                return;
            }

            var state = await _feature.GetStateAsync().ConfigureAwait(false);

            await Dispatcher.InvokeAsync(() =>
            {
                IsAvailable = true;
                ErrorMessage = null;
                ApplyState(state);
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                IsAvailable = false;
                ErrorMessage = ex.Message;
            }).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Applies a state through the library feature. On failure the real state is
    /// re-read so the control can never display a write that did not happen.
    /// </summary>
    protected async Task SetStateAsync(TState state)
    {
        if (_feature is null)
            return;

        try
        {
            IsBusy = true;
            await _feature.SetStateAsync(state).ConfigureAwait(false);
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() => ErrorMessage = ex.Message).ConfigureAwait(false);
        }
        finally
        {
            IsBusy = false;
        }

        await RefreshAsync().ConfigureAwait(false);
    }

    private void OnFeatureStateMessage(FeatureStateMessage<TState> message)
    {
        // WPF gates the refresh on IsVisible; visibility is handled by the page, so
        // this only forwards to the same read path.
        Dispatcher.Post(async () => await RefreshAsync().ConfigureAwait(false));
    }

    protected abstract void ApplyState(TState state);
}

/// <summary>
/// Two-state feature rendered as a toggle. <see cref="OnState"/> is the value the
/// switch represents as "on".
/// </summary>
public sealed partial class FeatureToggleWidgetViewModel<TState> : FeatureWidgetViewModel<TState>
    where TState : struct
{
    private readonly TState _onState;
    private readonly TState _offState;

    public FeatureToggleWidgetViewModel(
        IMainThreadDispatcher dispatcher,
        string title,
        string icon,
        TState onState,
        TState offState)
        : base(dispatcher)
    {
        Title = title;
        Icon = icon;
        IsToggle = true;

        _onState = onState;
        _offState = offState;
    }

    [ObservableProperty]
    private bool _isOn;

    /// <summary>Every state the machine reports, used to label the control.</summary>
    public string StateLabel { get; private set; } = string.Empty;

    protected override void ApplyState(TState state)
    {
        IsOn = EqualityComparer<TState>.Default.Equals(state, _onState);
        StateLabel = state.ToString()!;
    }

    partial void OnIsOnChanged(bool value)
    {
        if (!IsAvailable || IsBusy)
            return;

        _ = SetStateAsync(value ? _onState : _offState);
    }
}

/// <summary>
/// Multi-state feature rendered as a combo box, mirroring WPF's
/// <c>AbstractComboBoxFeatureCardControl</c>. Options come from the machine via
/// <see cref="IFeature{T}.GetAllStatesAsync"/>.
/// </summary>
public sealed partial class FeatureChoiceWidgetViewModel<TState> : FeatureWidgetViewModel<TState>
    where TState : struct
{
    public FeatureChoiceWidgetViewModel(
        IMainThreadDispatcher dispatcher,
        string title,
        string icon)
        : base(dispatcher)
    {
        Title = title;
        Icon = icon;
        IsChoice = true;
    }

    /// <summary>States the machine reports, in canonical order.</summary>
    public ObservableCollection<TState> Options { get; } = new();

    [ObservableProperty]
    private TState? _selectedState;

    /// <summary>Set while a backend read is being published, so the UI never writes back what it just read.</summary>
    private bool _suppressWrite;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync().ConfigureAwait(false);

        if (!IsAvailable)
            return;

        try
        {
            var states = await Feature.GetAllStatesAsync().ConfigureAwait(false);

            await Dispatcher.InvokeAsync(() =>
            {
                Options.Clear();
                foreach (var state in states)
                    Options.Add(state);
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    protected override void ApplyState(TState state)
    {
        if (!Options.Contains(state))
            Options.Add(state);

        _suppressWrite = true;
        try
        {
            SelectedState = state;
        }
        finally
        {
            _suppressWrite = false;
        }
    }

    partial void OnSelectedStateChanged(TState? value)
    {
        if (_suppressWrite || !IsAvailable || IsBusy || value is not { } state)
            return;

        _ = SetStateAsync(state);
    }
}

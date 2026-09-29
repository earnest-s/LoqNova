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

    /// <summary>True for a widget that performs an action rather than holding state.</summary>
    public bool IsAction { get; protected init; }

    /// <summary>True for a widget that presents a read-only status string.</summary>
    public bool IsStatus { get; protected init; }

    /// <summary>State of a two-state feature. Shared by toggle features and the GPU overclock toggle.</summary>
    [ObservableProperty]
    private bool _isOn;

    /// <summary>
    /// Set while a backend read is being published, so adopting a reading is never
    /// mistaken for a user change (which would write straight back to the hardware).
    /// </summary>
    protected bool SuppressWrite { get; set; }

    /// <summary>
    /// Assigns the two-state property from a backend reading without triggering a
    /// write. Also used to suppress the view's change event that the assignment
    /// itself causes.
    /// </summary>
    protected void SetIsOnFromBackend(bool value)
    {
        SuppressWrite = true;
        try
        {
            IsOn = value;
        }
        finally
        {
            SuppressWrite = false;
        }
    }

    /// <summary>True while a backend reading is being applied, so the view's change event is ignored.</summary>
    protected bool IsAdoptingBackendState => SuppressWrite;

    /// <summary>
    /// Applies a two-state change requested by the user. The view calls this from
    /// its change event rather than relying on a property-changed callback, because
    /// those run after the value is assigned and cannot tell a user change from a
    /// backend publish.
    /// </summary>
    public void RequestOn(bool value)
    {
        // No comparison against IsOn here: the view has already pushed the new value
        // into the property by the time this runs, so such a check would always
        // reject the user's change. Echoes from the backend are prevented by
        // SuppressWrite instead.
        if (SuppressWrite || !IsAvailable || IsBusy)
            return;

        OnIsOnRequested(value);
    }

    /// <summary>Invoked when the user, rather than the backend, changed the two-state property.</summary>
    protected virtual void OnIsOnRequested(bool value)
    {
    }

    /// <summary>
    /// Entry point for a selection reported by the view, which carries the chosen
    /// item as a plain object. Only choice widgets act on it.
    /// </summary>
    public virtual void RequestStateFrom(object? selected)
    {
    }


    /// <summary>Read-only status text, shown for widgets that report rather than set state.</summary>
    [ObservableProperty]
    private string? _statusText;

    /// <summary>Primary action command (for example turning the monitors off, or deactivating the dGPU).</summary>
    public CommunityToolkit.Mvvm.Input.AsyncRelayCommand? PrimaryActionCommand { get; set; }

    /// <summary>Caption for <see cref="PrimaryActionCommand"/>.</summary>
    public string? PrimaryActionText { get; set; }

    /// <summary>Secondary action command, matching WPF's context-menu actions.</summary>
    public CommunityToolkit.Mvvm.Input.AsyncRelayCommand? SecondaryActionCommand { get; set; }

    /// <summary>Caption for <see cref="SecondaryActionCommand"/>.</summary>
    public string? SecondaryActionText { get; set; }

    /// <summary>True when a primary action button should be shown.</summary>
    public bool HasPrimaryAction => PrimaryActionCommand is not null;

    /// <summary>True when a secondary action button should be shown.</summary>
    public bool HasSecondaryAction => SecondaryActionCommand is not null;

    /// <summary>
    /// Subscribes to backend signals in addition to <c>FeatureStateMessage</c>. WPF
    /// controls do this for features whose state also changes through a listener
    /// (display configuration, native display device arrival).
    /// </summary>
    protected virtual void SubscribeExtraSignals()
    {
    }

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

    /// <summary>Last value read from the backend, used to avoid redundant writes.</summary>
    protected TState? _lastBackendState;

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

        await Dispatcher.InvokeAsync(SubscribeExtraSignals).ConfigureAwait(false);

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
            _lastBackendState = state;

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

        // Every property below is bound, so it must be set on the UI thread. The
        // backend call is awaited off-thread, hence the explicit marshalling.
        await Dispatcher.InvokeAsync(() => IsBusy = true).ConfigureAwait(false);

        string? failure = null;

        try
        {
            await _feature.SetStateAsync(state).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            failure = ex.Message;
        }

        await Dispatcher.InvokeAsync(() => IsBusy = false).ConfigureAwait(false);

        // Re-read the real state rather than assuming the write landed, so a failed
        // or refused write can never leave a stale value selected.
        await RefreshAsync().ConfigureAwait(false);

        // Applied after the re-read: RefreshAsync clears ErrorMessage on a good read,
        // which would otherwise swallow the reason a write failed.
        if (failure is not null)
            await Dispatcher.InvokeAsync(() => ErrorMessage = failure).ConfigureAwait(false);
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

    /// <summary>Human-readable form of the state the machine reported.</summary>
    public string StateLabel { get; private set; } = string.Empty;

    protected override void ApplyState(TState state)
    {
        SetIsOnFromBackend(EqualityComparer<TState>.Default.Equals(state, _onState));
        StateLabel = state.ToString()!;
    }

    /// <summary>Writes the requested state through the library feature.</summary>
    protected override void OnIsOnRequested(bool value)
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
public partial class FeatureChoiceWidgetViewModel<TState> : FeatureWidgetViewModel<TState>
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

        SuppressWrite = true;
        try
        {
            SelectedState = state;
        }
        finally
        {
            SuppressWrite = false;
        }
    }

    /// <summary>
    /// Applies a state requested by the user. The view calls this from its
    /// selection-changed event. A property-changed callback cannot be used here:
    /// it runs after the assignment, so the previous value is already gone and a
    /// backend publish is indistinguishable from a user choice.
    /// </summary>
    public void RequestState(TState state)
    {
        // Deliberately no comparison against SelectedState: the binding has already
        // written the chosen value into the property before this is called, so the
        // previous value is not observable here and any such check would suppress a
        // genuine user change. Backend echoes are blocked by SuppressWrite.
        if (SuppressWrite || !IsAvailable || IsBusy)
            return;

        // Compared against the last value the backend reported, which is still known
        // here; SelectedState has already been overwritten by the binding.
        if (_lastBackendState is { } last && EqualityComparer<TState>.Default.Equals(last, state))
            return;

        _ = SetStateAsync(state);
    }

    }

    public override void RequestStateFrom(object? selected)
    {
        if (selected is TState state)
            RequestState(state);
    }
}

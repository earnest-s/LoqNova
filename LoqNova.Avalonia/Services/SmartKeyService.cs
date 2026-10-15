using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LoqNova.Lib;
using LoqNova.Lib.Automation;
using LoqNova.Lib.Listeners;
using LoqNova.Lib.Messaging;
using LoqNova.Lib.Messaging.Messages;
using LoqNova.Lib.Settings;
using LoqNova.Lib.SoftwareDisabler;
using Microsoft.Extensions.Logging;
using LibNotificationMessage = LoqNova.Lib.Messaging.Messages.NotificationMessage;
using LibNotificationType = LoqNova.Lib.NotificationType;

namespace LoqNova.Avalonia.Services;

/// <summary>
/// Single and double Fn+F9 handling: the Avalonia counterpart of WPF's
/// <c>SmartKeyHelper</c>.
///
/// <para>
/// The shared <see cref="SpecialKeyListener"/> deliberately does not handle Fn+F9 and
/// WPF adds the handling in its own project, so this bit of behaviour did not come
/// across with the shell. Without it the actions chosen in "Smart Key Action" and
/// "Smart Key Secondary Action" were written to the store, survived restarts, and were
/// never run.
/// </para>
///
/// <para>
/// Both presses read the same store fields the selector dialogs write
/// (<c>SmartKeySinglePressActionId</c> / <c>SmartKeyDoublePressActionId</c> and their
/// action lists), and run the pipeline through the shared automation processor, so no
/// second automation backend is involved.
/// </para>
/// </summary>
public sealed class SmartKeyService : ISmartKeyService
{
    /// <summary>A second press inside this window counts as a double press, as in WPF.</summary>
    private static readonly TimeSpan DoublePressInterval = TimeSpan.FromMilliseconds(500);

    private readonly ApplicationSettings _settings;
    private readonly FnKeysDisabler _fnKeysDisabler;
    private readonly SpecialKeyListener _specialKeyListener;
    private readonly AutomationProcessor _automationProcessor;
    private readonly ILogger<SmartKeyService> _logger;

    private DateTime _lastPressUtc = DateTime.MinValue;
    private CancellationTokenSource? _doublePressCancellationTokenSource;
    private bool _started;

    public SmartKeyService(
        ApplicationSettings settings,
        FnKeysDisabler fnKeysDisabler,
        SpecialKeyListener specialKeyListener,
        AutomationProcessor automationProcessor,
        ILogger<SmartKeyService> logger)
    {
        _settings = settings;
        _fnKeysDisabler = fnKeysDisabler;
        _specialKeyListener = specialKeyListener;
        _automationProcessor = automationProcessor;
        _logger = logger;
    }

    /// <inheritdoc />
    public Action? BringToForeground { get; set; }

    /// <inheritdoc />
    public void Start()
    {
        if (_started)
        {
            return;
        }

        _specialKeyListener.Changed += OnSpecialKeyChanged;
        _started = true;
    }

    /// <inheritdoc />
    public void Stop()
    {
        if (!_started)
        {
            return;
        }

        _specialKeyListener.Changed -= OnSpecialKeyChanged;
        _started = false;

        var pending = Interlocked.Exchange(ref _doublePressCancellationTokenSource, null);

        if (pending is not null)
        {
            try
            {
                pending.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Already finished; nothing to cancel.
            }

            pending.Dispose();
        }
    }

    private async void OnSpecialKeyChanged(object? sender, SpecialKeyListener.ChangedEventArgs e)
    {
        try
        {
            if (e.SpecialKey != SpecialKey.FnF9)
            {
                return;
            }

            // WPF ignores the Smart Key while Fn keys are being handled by Lenovo's own
            // software, because the hardware event is not ours to act on then.
            if (await _fnKeysDisabler.GetStatusAsync().ConfigureAwait(false) == SoftwareStatus.Enabled)
            {
                return;
            }

            // A new press invalidates the previous press's pending single-press timer, so
            // the two gestures cannot both fire.
            var previous = Interlocked.Exchange(ref _doublePressCancellationTokenSource, null);

            if (previous is not null)
            {
                try
                {
                    await previous.CancelAsync().ConfigureAwait(false);
                }
                catch (ObjectDisposedException)
                {
                    // Already finished; nothing to cancel.
                }
            }

            var cancellation = new CancellationTokenSource();
            _doublePressCancellationTokenSource = cancellation;
            var token = cancellation.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    var now = DateTime.UtcNow;
                    var sinceLastPress = now - _lastPressUtc;
                    _lastPressUtc = now;

                    if (sinceLastPress < DoublePressInterval)
                    {
                        await ProcessPressAsync(true).ConfigureAwait(false);
                        return;
                    }

                    // Wait out the double-press window. A second press cancels this delay
                    // and takes the double-press path above instead.
                    await Task.Delay(DoublePressInterval, token).ConfigureAwait(false);
                    await ProcessPressAsync(false).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Superseded by the next press; the double-press path owns the gesture.
                }
            }, token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Smart key press handling failed");
        }
    }

    private async Task ProcessPressAsync(bool isDoublePress)
    {
        var currentId = isDoublePress
            ? _settings.Store.SmartKeyDoublePressActionId
            : _settings.Store.SmartKeySinglePressActionId;

        var actionList = isDoublePress
            ? _settings.Store.SmartKeyDoublePressActionList
            : _settings.Store.SmartKeySinglePressActionList;

        if (!currentId.HasValue)
        {
            // Nothing configured: WPF raises the window instead.
            InvokeBringToForeground();
            return;
        }

        if (currentId.Value == Guid.Empty)
        {
            return;
        }

        if (actionList.Count == 0)
        {
            actionList.Add(currentId.Value);
            _settings.SynchronizeStore();
        }

        // WPF looks the selection up in the list and runs that entry. It also computes a
        // "next" index there but never uses it, so the selected action is what runs; this
        // keeps that behaviour, which is what the selector dialog promises.
        var currentIndex = Math.Max(0, actionList.IndexOf(currentId.Value));
        var selectedId = actionList[currentIndex];

        try
        {
            var pipelines = await _automationProcessor.GetPipelinesAsync().ConfigureAwait(false);
            var pipeline = pipelines.FirstOrDefault(p => p.Id == selectedId);

            if (pipeline is null)
            {
                return;
            }

            await _automationProcessor.RunNowAsync(pipeline.Id).ConfigureAwait(false);

            MessagingCenter.Publish(new LibNotificationMessage(
                isDoublePress ? LibNotificationType.SmartKeyDoublePress : LibNotificationType.SmartKeySinglePress,
                pipeline.Name ?? string.Empty));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Running smart key action {Id} failed", selectedId);
        }
    }

    private void InvokeBringToForeground()
    {
        try
        {
            BringToForeground?.Invoke();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Raising the window for the Smart Key failed");
        }
    }
}

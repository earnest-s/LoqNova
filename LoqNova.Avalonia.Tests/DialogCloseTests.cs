using LoqNova.Avalonia.Services;
using LoqNova.Avalonia.ViewModels;
using LoqNova.Avalonia.ViewModels.Dialogs;
using LoqNova.Lib;
using Xunit;

namespace LoqNova.Avalonia.Tests;

/// <summary>
/// The dialog base is how every Settings dialog dismisses itself. WPF closes the window;
/// here the dialog asks the presenter to close, so broken wiring would leave the overlay
/// stuck open.
/// </summary>
public class DialogCloseTests
{
    private sealed class TestDialog(IDialogService dialogs) : DialogViewModelBase(dialogs);

    private sealed class FakeDialogService : IDialogService
    {
        public int CloseCount { get; private set; }

        public bool IsOpen { get; set; }

        public ViewModelBase? Current { get; set; }

        public event Action? Closed;

        public T Resolve<T>() where T : ViewModelBase => throw new NotSupportedException();

        public Task ShowAsync(ViewModelBase dialog) => Task.CompletedTask;

        public void Close()
        {
            CloseCount++;
            Closed?.Invoke();
        }
    }

    [Fact]
    public void Close_command_dismisses_through_the_presenter()
    {
        var dialogs = new FakeDialogService();
        var dialog = new TestDialog(dialogs);

        Assert.Equal(0, dialogs.CloseCount);

        dialog.CloseCommand.Execute(null);

        Assert.Equal(1, dialogs.CloseCount);
    }

    [Fact]
    public void Notifications_options_match_the_enums()
    {
        // The constructor only builds the static option lists; the services are resolved
        // later in InitializeAsync, so nulls keep this test hermetic.
        var vm = new NotificationsSettingsViewModel(null!, null!);

        Assert.Equal(Enum.GetValues<NotificationPosition>(), vm.NotificationPositions.Select(o => o.Value));
        Assert.Equal(Enum.GetValues<NotificationDuration>(), vm.NotificationDurations.Select(o => o.Value));
        Assert.All(vm.NotificationPositions, o => Assert.False(string.IsNullOrWhiteSpace(o.Label)));
        Assert.All(vm.NotificationDurations, o => Assert.False(string.IsNullOrWhiteSpace(o.Label)));
    }

    [Fact]
    public void Detail_cards_follow_the_dont_show_switch()
    {
        var vm = new NotificationsSettingsViewModel(null!, null!);

        Assert.True(vm.AreDetailsEnabled);

        vm.DontShowNotifications = true;

        Assert.False(vm.AreDetailsEnabled);
    }
}

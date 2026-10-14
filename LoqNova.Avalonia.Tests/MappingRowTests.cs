using System.Collections.ObjectModel;
using LoqNova.Avalonia.ViewModels.Dialogs;
using Xunit;

namespace LoqNova.Avalonia.Tests;

/// <summary>
/// The Windows Power Modes / Power Plans dialogs build one <see cref="MappingRow"/> per
/// power mode. WPF selects the stored value when it is present and otherwise falls back
/// to the first option (the "Default" plan), and it must not treat seeding the initial
/// selection as a user change that writes back.
/// </summary>
public class MappingRowTests
{
    private static readonly Guid DefaultGuid = Guid.Empty;
    private static readonly Guid PlanA = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PlanB = new("22222222-2222-2222-2222-222222222222");

    private static ObservableCollection<MappingOption> Options() => new()
    {
        new MappingOption(Guid.Empty, "Default"),
        new MappingOption(Guid.Parse("11111111-1111-1111-1111-111111111111"), "Plan A"),
        new MappingOption(new Guid("22222222-2222-2222-2222-222222222222"), "Plan B"),
    };

    [Fact]
    public void Known_selection_is_restored()
    {
        var options = Options();

        var row = new MappingRow("Balanced", options, options[2].Value, true, _ => { });

        Assert.Same(options[2], row.Selected);
    }

    [Fact]
    public void Unknown_selection_falls_back_to_default_without_applying()
    {
        var options = Options();
        object? applied = null;

        // A plan GUID that is no longer installed (e.g. the plan was deleted).
        var row = new MappingRow("Balanced", options, new Guid("99999999-9999-9999-9999-999999999999"), true, v => applied = v);

        Assert.Same(options[0], row.Selected);
        Assert.Null(applied);
    }

    [Fact]
    public void Seeding_the_selection_never_writes_back()
    {
        var options = Options();
        var applied = new List<object>();

        _ = new MappingRow("Balanced", options, options[0].Value, true, v => applied.Add(v));

        Assert.Empty(applied);
    }

    [Fact]
    public void Changing_the_selection_applies_the_new_value()
    {
        var options = Options();
        var applied = new List<object>();
        var row = new MappingRow("Balanced", options, options[0].Value, true, v => applied.Add(v));

        row.Selected = options[2];

        var value = Assert.Single(applied);
        Assert.Equal(options[2].Value, value);
    }

    [Fact]
    public void Hidden_row_is_reported_not_visible()
    {
        var options = Options();

        var row = new MappingRow("God Mode", options, options[0].Value, false, _ => { });

        Assert.False(row.IsVisible);
    }
}

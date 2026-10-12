using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LoqNova.Avalonia.ViewModels.Dialogs;

/// <summary>One selectable entry: a Windows power mode, or a Windows power plan.</summary>
public sealed record MappingOption(object Value, string Label);

/// <summary>
/// A "Power Mode → Windows setting" row. WPF builds one combo box per power mode; this
/// keeps the same shape for both the Power Modes and the Power Plans dialogs.
/// </summary>
public partial class MappingRow : ObservableObject
{
    private readonly Action<object> _apply;
    private MappingOption? _selected;

    public MappingRow(
        string title,
        ObservableCollection<MappingOption> options,
        object? selectedValue,
        bool isVisible,
        Action<object> apply)
    {
        Title = title;
        Options = options;
        IsVisible = isVisible;
        _apply = apply;

        // Assigned to the field, not the property: seeding the selection must not be
        // mistaken for a user change and written back.
        _selected = options.FirstOrDefault(option => Equals(option.Value, selectedValue)) ?? options.FirstOrDefault();
    }

    public string Title { get; }

    public ObservableCollection<MappingOption> Options { get; }

    public bool IsVisible { get; }

    public MappingOption? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value) && value is not null)
            {
                _apply(value.Value);
            }
        }
    }
}

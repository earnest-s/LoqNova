using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace LoqNova.Avalonia.Views.Controls;

/// <summary>
/// A <see cref="UniformGrid"/> that derives its column count from the space
/// actually available.
///
/// A plain WrapPanel cannot be used for this: it lives inside a vertically
/// scrolling ScrollViewer, which measures its content with infinite width, so
/// the WrapPanel never wraps and simply overflows the page.
/// </summary>
public class ResponsiveUniformGrid : UniformGrid
{
    public static readonly StyledProperty<double> ItemWidthProperty =
        AvaloniaProperty.Register<ResponsiveUniformGrid, double>(nameof(ItemWidth));

    /// <summary>Target width for a single cell, in device-independent pixels.</summary>
    public double ItemWidth
    {
        get => GetValue(ItemWidthProperty);
        set => SetValue(ItemWidthProperty, value);
    }

    public static readonly StyledProperty<int> MaxColumnsProperty =
        AvaloniaProperty.Register<ResponsiveUniformGrid, int>(nameof(MaxColumns), int.MaxValue);

    /// <summary>
    /// Upper bound on the derived column count. A panel with a fixed, small
    /// number of children (the two device instruments) needs this: without it a
    /// wide window would derive more columns than there are children and leave
    /// the surplus cells empty.
    /// </summary>
    public int MaxColumns
    {
        get => GetValue(MaxColumnsProperty);
        set => SetValue(MaxColumnsProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ItemWidthProperty
            || change.Property == MaxColumnsProperty
            || change.Property == ColumnsProperty)
        {
            RecalculateColumns();
        }
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        RecalculateColumns();
    }

    private void RecalculateColumns()
    {
        if (ItemWidth <= 0)
        {
            return;
        }

        var available = Bounds.Width;
        if (double.IsNaN(available) || available <= 0)
        {
            return;
        }

        var columns = (int)Math.Floor(available / ItemWidth);
        if (columns < 1)
        {
            columns = 1;
        }

        if (columns > MaxColumns)
        {
            columns = Math.Max(1, MaxColumns);
        }

        if (columns != Columns)
        {
            Columns = columns;
        }
    }
}

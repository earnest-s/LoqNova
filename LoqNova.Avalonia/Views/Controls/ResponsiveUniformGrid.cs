using System;
using Avalonia;
using Avalonia.Controls;

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

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ItemWidthProperty || change.Property == ColumnsProperty)
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

        if (columns != Columns)
        {
            Columns = columns;
        }
    }
}

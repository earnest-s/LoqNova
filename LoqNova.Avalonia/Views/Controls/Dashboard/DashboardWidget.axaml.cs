using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Layout;
using LoqNova.Avalonia.ViewModels.Pages;

namespace LoqNova.Avalonia.Views.Controls.Dashboard;

public partial class DashboardWidget : UserControl
{
    public DashboardWidget()
    {
        InitializeComponent();
    }
    
    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

public class WidgetTemplateSelector : Avalonia.Controls.DataTemplates.IDataTemplate
{
    public bool Match(object? data)
    {
        return data is DashboardWidgetViewModel;
    }
    
    public Avalonia.Controls.Control Build(object? data)
    {
        if (data is not DashboardWidgetViewModel widget)
            return new TextBlock { Text = "Unknown Widget" };
        
        return widget.Type switch
        {
            WidgetType.Sensor => CreateSensorWidget(widget),
            WidgetType.Toggle => CreateToggleWidget(widget),
            WidgetType.ComboBox => CreateComboBoxWidget(widget),
            WidgetType.Button => CreateButtonWidget(widget),
            WidgetType.Custom => CreateCustomWidget(widget),
            _ => new TextBlock { Text = "Unknown Widget Type" }
        };
    }
    
    private Control CreateSensorWidget(DashboardWidgetViewModel widget)
    {
        return new Border
        {
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = widget.Value,
                        Classes = { "CardValue" },
                        Foreground = Brush.Parse(widget.Color),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    },
                    new TextBlock
                    {
                        Text = widget.Unit,
                        Classes = { "CardUnit" },
                        HorizontalAlignment = HorizontalAlignment.Center
                    }
                }
            },
            Width = 260,
            Height = 100
        };
    }
    
    private Control CreateToggleWidget(DashboardWidgetViewModel widget)
    {
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = widget.Value,
                    Classes = { "CardValue" },
                    Foreground = Brush.Parse(widget.Color)
                },
                new ToggleSwitch
                {
                    IsChecked = widget.IsOn,
                    OnContent = "On",
                    OffContent = "Off"
                }
            }
        };
    }
    
    private Control CreateComboBoxWidget(DashboardWidgetViewModel widget)
    {
        return new ComboBox
        {
            ItemsSource = widget.Items,
            SelectedItem = widget.SelectedItem,
            Width = 200,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Background = Brush.Parse("#2D2D2D"),
            BorderBrush = Brush.Parse("#3E3E42"),
            Foreground = Brush.Parse("#FFFFFF")
        };
    }
    
    private Control CreateButtonWidget(DashboardWidgetViewModel widget)
    {
        return new Button
        {
            Content = widget.Value,
            Classes = { "PrimaryButton" },
            Width = 120,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
    }
    
    private Control CreateCustomWidget(DashboardWidgetViewModel widget)
    {
        return new TextBlock
        {
            Text = "Custom Widget",
            Classes = { "Body" },
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
    }
}
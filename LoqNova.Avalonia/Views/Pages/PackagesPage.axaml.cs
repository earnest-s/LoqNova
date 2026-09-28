using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using LoqNova.Avalonia.ViewModels.Pages;

namespace LoqNova.Avalonia.Views.Pages;

public partial class PackagesPage : UserControl
{
    public PackagesPage()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// Equivalent of the WPF <c>PackagesPage_Initialized</c> handler: resolve the
    /// machine type, populate the OS list and load the configured download
    /// folder the first time this view receives its data context.
    /// </summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is PackagesViewModel viewModel && !viewModel.IsInitialized)
        {
            _ = viewModel.InitializeCommand.ExecuteAsync(null);
            _ = viewModel.DownloadPackagesCommand.ExecuteAsync(null); // TEMP verification
        }
    }
}

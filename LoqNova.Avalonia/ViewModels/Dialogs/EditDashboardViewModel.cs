using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LoqNova.Avalonia.ViewModels.Dialogs;

public partial class EditDashboardViewModel : ViewModelBase
{
    [ObservableProperty]
    private ObservableCollection<DashboardGroupViewModel> _groups = new();
    
    public EditDashboardViewModel()
    {
        LoadDefaultGroups();
    }
    
    private void LoadDefaultGroups()
    {
        Groups.Add(new DashboardGroupViewModel { Name = "System", Order = 0, IsExpanded = true });
        Groups.Add(new DashboardGroupViewModel { Name = "Performance", Order = 1, IsExpanded = true });
        Groups.Add(new DashboardGroupViewModel { Name = "RGB", Order = 2, IsExpanded = true });
        Groups.Add(new DashboardGroupViewModel { Name = "Hardware", Order = 3, IsExpanded = true });
        Groups.Add(new DashboardGroupViewModel { Name = "Power", Order = 4, IsExpanded = true });
    }
    
    [RelayCommand]
    private void AddGroup()
    {
        var newGroup = new DashboardGroupViewModel
        {
            Name = $"New Group {Groups.Count + 1}",
            Order = Groups.Count,
            IsExpanded = true
        };
        Groups.Add(newGroup);
    }
    
    [RelayCommand]
    private void RemoveGroup(DashboardGroupViewModel group)
    {
        if (group != null)
        {
            Groups.Remove(group);
            // Reorder
            for (int i = 0; i < Groups.Count; i++)
            {
                Groups[i].Order = i;
            }
        }
    }
    
    [RelayCommand]
    private void MoveGroupUp(DashboardGroupViewModel group)
    {
        if (group != null)
        {
            int index = Groups.IndexOf(group);
            if (index > 0)
            {
                Groups.Move(index, index - 1);
                for (int i = 0; i < Groups.Count; i++)
                {
                    Groups[i].Order = i;
                }
            }
        }
    }
    
    [RelayCommand]
    private void MoveGroupDown(DashboardGroupViewModel group)
    {
        if (group != null)
        {
            int index = Groups.IndexOf(group);
            if (index < Groups.Count - 1)
            {
                Groups.Move(index, index + 1);
                for (int i = 0; i < Groups.Count; i++)
                {
                    Groups[i].Order = i;
                }
            }
        }
    }
    
    [RelayCommand]
    private async Task SaveAsync()
    {
        // Save dashboard layout
    }
    
    [RelayCommand]
    private async Task CloseAsync()
    {
        // Close dialog
    }
}

public partial class DashboardGroupViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _name = "";
    
    [ObservableProperty]
    private int _order = 0;
    
    [ObservableProperty]
    private bool _isExpanded = true;
    
    [ObservableProperty]
    private ObservableCollection<string> _items = new();
}
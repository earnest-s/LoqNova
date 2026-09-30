using System;
using System.Threading.Tasks;
using LoqNova.Avalonia.ViewModels;

namespace LoqNova.Avalonia.Services;

/// <summary>
/// Presents a modal dialog over the main window.
/// <para>
/// WPF shows these as separate Windows; Avalonia has no equivalent of the WPF
/// <c>Window.ShowDialog</c> pattern for content-only dialogs, so the host presents
/// them as an overlay inside the existing window instead of creating one. That keeps
/// a single top-level window, which also avoids a second elevation prompt.
/// </para>
/// </summary>
public interface IDialogService
{
    /// <summary>True while a dialog is being presented.</summary>
    bool IsOpen { get; }

    /// <summary>The dialog currently presented, or null.</summary>
    ViewModelBase? Current { get; }

    /// <summary>
    /// Resolves a dialog from the container. Exposed here so a presenter does not have
    /// to reach for a global root, which is not guaranteed to be initialised by the
    /// time presenters are constructed.
    /// </summary>
    T Resolve<T>() where T : ViewModelBase;

    /// <summary>Presents <paramref name="dialog"/> modally.</summary>
    Task ShowAsync(ViewModelBase dialog);

    /// <summary>Dismisses the current dialog.</summary>
    void Close();

    /// <summary>
    /// Raised after the dialog is dismissed, so the presenter can clear its scrim
    /// without the dialog having to know how it is hosted.
    /// </summary>
    event Action? Closed;
}

using System.Threading.Tasks;

namespace LoqNova.Avalonia.Services;

/// <summary>
/// Implemented by page view models that need asynchronous work before their page can
/// display real content.
/// <para>
/// The navigation service used to construct the view model, construct the view and swap
/// the content in one step, with no hook in between. A page whose data arrives from an
/// async load was therefore attached and painted while still empty, which on this
/// near-black theme reads as the window going dark. Navigation now awaits
/// <see cref="OnNavigatedToAsync"/> before the content is swapped.
/// </para>
/// </summary>
public interface INavigationAware
{
    /// <summary>
    /// Loads whatever the page needs before it is shown. Must be safe to call on every
    /// navigation to the page.
    /// </summary>
    Task OnNavigatedToAsync();
}
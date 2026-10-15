using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using CommunityToolkit.Mvvm.Input;
using LoqNova.Avalonia.Localization;
using LoqNova.Lib.Utils;
// Aliased: the page's own "Folders" collection would otherwise hide the type.
using LibFolders = LoqNova.Lib.Utils.Folders;

namespace LoqNova.Avalonia.ViewModels.Pages;

/// <summary>
/// The About page, mirroring WPF's <c>AboutPage</c>: application name, the real
/// entry-assembly version and build, the copyright line, a culture-gated
/// translation credit, the project links, the third-party library list and the
/// application folders. Every action opens a real target through the shell: there
/// are no placeholder buttons and no invented URLs.
/// </summary>
public partial class AboutViewModel : ViewModelBase
{
    // WPF's AboutPage hard-codes these two targets in XAML. The releases URL is
    // the same one the library's update check uses.
    private const string ProjectUrl = "https://github.com/earnest-s/LoqNova.git";
    private const string ReleasesUrl = "https://github.com/earnest-s/LoqNova/releases";

    private const string CopyrightText = "© 2026 Earnest S";

    public AboutViewModel()
    {
        // WPF reads the entry assembly, not the executing one: the entry assembly
        // is the one the csproj stamps with Version / InformationalVersion.
        var assembly = Assembly.GetEntryAssembly() ?? typeof(AboutViewModel).Assembly;
        var version = assembly.GetName().Version;

        Version = version?.ToString(3) ?? string.Empty;
        Build = ReadBuild(assembly);

        // Third-party libraries, three columns like WPF's AboutPage. The WPF list
        // names the WPF front end's own dependencies; this one names the Avalonia
        // front end's, so it stays truthful about what the running application
        // actually ships (wpfui and Markdig.Wpf are dropped, the Avalonia, Skia and
        // MVVM-toolkit packages are added).
        LibraryColumns = new[]
        {
            // Column 1
            new[]
            {
                Link("Avalonia", "https://github.com/AvaloniaUI/Avalonia"),
                Link("Avalonia.Controls.ColorPicker", "https://github.com/AvaloniaUI/Avalonia.Controls.ColorPicker"),
                Link("Autofac", "https://github.com/autofac/Autofac"),
                Link("CommunityToolkit.Mvvm", "https://github.com/CommunityToolkit/dotnet"),
                Link("CsWin32", "https://github.com/microsoft/CsWin32"),
                Link("Demystifier", "https://github.com/benaadams/Ben.Demystifier")
            },
            // Column 2
            new[]
            {
                Link("HarfBuzzSharp", "https://github.com/mono/HarfBuzzSharp"),
                Link("Humanizer", "https://github.com/Humanizr/Humanizer"),
                Link("ManagedNativeWifi", "https://github.com/emoacht/ManagedNativeWifi"),
                Link("Markdig", "https://github.com/xoofx/markdig"),
                Link("NAudio.Wasapi", "https://github.com/naudio/NAudio"),
                Link("Newtonsoft.Json", "https://github.com/JamesNK/Newtonsoft.Json")
            },
            // Column 3
            new[]
            {
                Link("NvAPIWrapper", "https://github.com/falahati/NvAPIWrapper"),
                Link("octokit", "https://github.com/octokit/octokit.net"),
                Link("pubsub", "https://github.com/kekyo/PubSub"),
                Link("SkiaSharp", "https://github.com/mono/SkiaSharp"),
                Link("TaskScheduler", "https://github.com/dahall/TaskScheduler"),
                Link("WindowsDisplayAPI", "https://github.com/falahati/WindowsDisplayAPI")
            }
        };

        Links = new ObservableCollection<AboutLink>
        {
            Link(ProjectLinkText, ProjectUrl),
            Link(ReleasesLinkText, ReleasesUrl)
        };

        Folders = new ObservableCollection<AboutLink>
        {
            Folder(DataFolderText, LibFolders.AppData),
            Folder(TempFolderText, LibFolders.Temp)
        };
    }

    // ---- Localized chrome, read once from the same resources WPF uses. ----

    public string Title => LocalizationHelper.T("AboutPage_Title");

    public string AppName => LocalizationHelper.T("AboutPage_AppName");

    public string VersionLabel => LocalizationHelper.T("AboutPage_Version");

    public string BuildLabel => LocalizationHelper.T("AboutPage_Build");

    public string TranslationCredit => LocalizationHelper.T("AboutPage_TranslationCredit");

    public string LinksLabel => LocalizationHelper.T("AboutPage_Links");

    public string ProjectLinkText => LocalizationHelper.T("AboutPage_ProjectWebsiteOnGitHub");

    public string ReleasesLinkText => LocalizationHelper.T("AboutPage_LatestReleaseOnGitHub");

    public string ThirdPartyLibrariesLabel => LocalizationHelper.T("AboutPage_ThirdPartyLibraries");

    public string ApplicationFoldersLabel => LocalizationHelper.T("AboutPage_ApplicationFolders");

    public string DataFolderText => LocalizationHelper.T("AboutPage_Data");

    public string TempFolderText => LocalizationHelper.T("AboutPage_Temp");

    // ---- Real values, formatted the way WPF's code-behind formats them. ----

    /// <summary>"Version 3.1.0" — the entry assembly's major.minor.build.</summary>
    public string VersionText => $"{VersionLabel} {Version}";

    /// <summary>"Build 202610151234" — the InformationalVersion's build part.</summary>
    public string BuildText => $"{BuildLabel} {Build}";

    public string Version { get; }

    public string Build { get; }

    public string Copyright => CopyrightText;

    /// <summary>
    /// WPF shows the community translation credit for every culture except
    /// English (<c>Resource.Culture.Equals(new CultureInfo("en"))</c>).
    /// </summary>
    public bool ShowTranslationCredit =>
        !string.Equals(LocalizationHelper.CurrentCulture.Name, "en", StringComparison.OrdinalIgnoreCase);

    public ObservableCollection<AboutLink> Links { get; }

    /// <summary>Third-party libraries, one entry per WPF column.</summary>
    public IReadOnlyList<AboutLink>[] LibraryColumns { get; }

    public ObservableCollection<AboutLink> Folders { get; }

    private static string ReadBuild(Assembly assembly)
    {
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrEmpty(informational) && informational.Contains('+'))
            return informational.Split('+')[1];

        return assembly.GetName().Version?.Revision.ToString() ?? string.Empty;
    }

    private static AboutLink Link(string name, string url) =>
        new(name, url, () => Open(url));

    private static AboutLink Folder(string name, string path) =>
        new(name, path, () => OpenFolder(path));

    /// <summary>
    /// Opens a URL with the shell. Failures are swallowed: a missing browser must
    /// not take the page down, and there is nothing actionable to report.
    /// </summary>
    private static void Open(string url)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(url))
                return;

            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // No default handler / blocked by policy — nothing to do.
        }
    }

    /// <summary>
    /// Reveals a folder in Explorer, matching WPF's
    /// <c>Process.Start("explorer", Folders.…)</c> handlers.
    /// </summary>
    private static void OpenFolder(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !System.IO.Directory.Exists(path))
                return;

            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception)
        {
            // Nothing to do.
        }
    }
}

/// <summary>
/// One clickable row of the About page: a visible label plus the command that
/// opens its real target.
/// </summary>
public sealed class AboutLink
{
    public AboutLink(string name, string target, Action open)
    {
        Name = name;
        Target = target;
        Command = new RelayCommand(open);
    }

    public string Name { get; }

    /// <summary>The URL or folder the link opens; shown as a tooltip.</summary>
    public string Target { get; }

    public IRelayCommand Command { get; }
}

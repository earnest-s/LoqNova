using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.IO;
using System.Resources;
using System.Threading;
using System.Threading.Tasks;
using LoqNova.Lib.Utils;

namespace LoqNova.Avalonia.Localization;

public static class LocalizationHelper
{
    private static ResourceManager? _resourceManager;

    /// <summary>
    /// WPF keeps the chosen UI culture in a small <c>lang</c> file under the app-data
    /// folder and applies it on every launch. The same file is reused here so the two
    /// front ends share one persisted language and neither resets the other's choice.
    /// </summary>
    private static readonly string LanguagePath = Path.Combine(Folders.AppData, "lang");

    private static CultureInfo _currentCulture = CultureInfo.GetCultureInfo("en");

    public static CultureInfo CurrentCulture => _currentCulture;

    /// <summary>The culture the app falls back to.</summary>
    private static readonly CultureInfo DefaultLanguage = CultureInfo.GetCultureInfo("en");

    /// <summary>
    /// The languages the Settings page offers. Mirrors the list in WPF's
    /// <c>LocalizationHelper.Languages</c>, which lives in the WPF project and so cannot
    /// be shared. The Karakalpak entry is a deliberate WPF workaround and is kept so the
    /// two applications offer the same choices.
    /// </summary>
    public static readonly CultureInfo[] Languages =
    [
        DefaultLanguage,
        new("ar"),
        new("bg"),
        new("cs"),
        new("de"),
        new("el"),
        new("es"),
        new("fr"),
        new("hu"),
        new("it"),
        new("ja"),
        new("lv"),
        new("nl-nl"),
        new("pl"),
        new("pt"),
        new("pt-br"),
        new("ro"),
        new("ru"),
        new("sk"),
        new("tr"),
        new("uk"),
        new("vi"),
        new("zh-hans"),
        new("zh-hant"),
        new("uz-latn-uz")
    ];

    public static IReadOnlyList<string> SupportedLanguages =>
        [.. Languages.Select(culture => culture.IetfLanguageTag)];

    public static string CurrentLanguageCode => _currentCulture.IetfLanguageTag;

    /// <summary>
    /// The culture's own name, title cased the way WPF's LanguageDisplayName shows it.
    /// </summary>
    public static string LanguageDisplayName(string languageCode)
    {
        var culture = Languages.FirstOrDefault(c =>
            string.Equals(c.IetfLanguageTag, languageCode, StringComparison.InvariantCultureIgnoreCase));

        if (culture is null)
        {
            return languageCode;
        }

        var name = culture.NativeName;

        return culture.IetfLanguageTag.Equals("uz-latn-uz", StringComparison.InvariantCultureIgnoreCase)
            ? "Karakalpak"
            : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name);
    }

    public static void Initialize()
    {
        _resourceManager = new ResourceManager("LoqNova.Avalonia.Localization.Resource", typeof(LocalizationHelper).Assembly);
    }

    /// <summary>
    /// Applies the persisted language, if any. If the file is missing or holds an
    /// unknown culture the default is used, which matches WPF writing the default back
    /// on first run rather than leaving the language unset.
    /// </summary>
    public static async Task LoadLanguageAsync()
    {
        var culture = await GetLanguageFromFileAsync() ?? DefaultLanguage;
        SetLanguageInternal(culture);
    }

    private static async Task<CultureInfo?> GetLanguageFromFileAsync()
    {
        try
        {
            if (!File.Exists(LanguagePath))
                return null;

            var name = await File.ReadAllTextAsync(LanguagePath);
            var culture = new CultureInfo(name);

            // Only accept a culture the app actually offers, the same validation WPF
            // performs; anything else falls back to the default.
            return Languages.Contains(culture) ? culture : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Persists and applies a language, mirroring WPF's
    /// <c>LocalizationHelper.SetLanguageAsync(CultureInfo)</c>: the culture name is
    /// written to the shared <c>lang</c> file and then applied to the process.
    /// </summary>
    public static async Task SetLanguageAsync(string languageCode)
    {
        CultureInfo culture;

        try
        {
            culture = CultureInfo.GetCultureInfo(languageCode);
        }
        catch
        {
            culture = DefaultLanguage;
        }

        SetLanguageInternal(culture);

        try
        {
            await File.WriteAllTextAsync(LanguagePath, culture.Name);
        }
        catch
        {
            // A failed write must not leave the in-memory language unapplied; the
            // change simply is not persisted, which the caller cannot recover from.
        }
    }

    private static void SetLanguageInternal(CultureInfo culture)
    {
        _currentCulture = culture;

        // WPF keeps formatting English and only switches the UI culture, so dates and
        // numbers stay stable while the resource lookups follow the chosen language.
        var formatting = CultureInfo.GetCultureInfo("en");
        CultureInfo.DefaultThreadCurrentCulture = formatting;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        var thread = Thread.CurrentThread;
        thread.CurrentCulture = formatting;
        thread.CurrentUICulture = culture;
    }

    public static string GetString(string key)
    {
        if (_resourceManager == null)
            return key;

        try
        {
            var value = _resourceManager.GetString(key, _currentCulture);
            return value ?? key;
        }
        catch
        {
            return key;
        }
    }

    public static string T(string key) => GetString(key);
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Resources;
using System.Threading.Tasks;

namespace LoqNova.Avalonia.Localization;

public static class LocalizationHelper
{
    private static ResourceManager? _resourceManager;
    private static CultureInfo _currentCulture = CultureInfo.GetCultureInfo("en-US");
    
    public static CultureInfo CurrentCulture => _currentCulture;

    /// <summary>The culture the app falls back to.</summary>
    private static readonly CultureInfo DefaultLanguage = CultureInfo.GetCultureInfo("en-US");

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
    
    public static Task SetLanguageAsync(string languageCode)
    {
        try
        {
            _currentCulture = CultureInfo.GetCultureInfo(languageCode);
        }
        catch
        {
            _currentCulture = CultureInfo.GetCultureInfo("en-US");
        }
        return Task.CompletedTask;
    }
    
    public static string T(string key) => GetString(key);
}
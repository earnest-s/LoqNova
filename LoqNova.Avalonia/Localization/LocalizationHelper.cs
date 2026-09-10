using System;
using System.Globalization;
using System.Resources;
using System.Threading.Tasks;

namespace LoqNova.Avalonia.Localization;

public static class LocalizationHelper
{
    private static ResourceManager? _resourceManager;
    private static CultureInfo _currentCulture = CultureInfo.GetCultureInfo("en-US");
    
    public static CultureInfo CurrentCulture => _currentCulture;
    
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
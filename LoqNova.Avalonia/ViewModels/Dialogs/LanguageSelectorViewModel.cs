using System;
using System.Collections.ObjectCollection;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LoqNova.Avalonia.ViewModels.Dialogs;

public partial class LanguageSelectorViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _selectedLanguage = "en-US";
    
    public ObservableCollection<LanguageItem> Languages { get; } = new()
    {
        new LanguageItem { Code = "en-US", Name = "English (US)", NativeName = "English (US)" },
        new LanguageItem { Code = "zh-CN", Name = "Chinese (Simplified)", NativeName = "中文（简体）" },
        new LanguageItem { Code = "zh-TW", Name = "Chinese (Traditional)", NativeName = "中文（繁體）" },
        new LanguageItem { Code = "ja-JP", Name = "Japanese", NativeName = "日本語" },
        new LanguageItem { Code = "ko-KR", Name = "Korean", NativeName = "한국어" },
        new LanguageItem { Code = "de-DE", Name = "German", NativeName = "Deutsch" },
        new LanguageItem { Code = "fr-FR", Name = "French", NativeName = "Français" },
        new LanguageItem { Code = "es-ES", Name = "Spanish", NativeName = "Español" },
        new LanguageItem { Code = "ru-RU", Name = "Russian", NativeName = "Русский" },
        new LanguageItem { Code = "pt-BR", Name = "Portuguese (Brazil)", NativeName = "Português (Brasil)" },
        new LanguageItem { Code = "it-IT", Name = "Italian", NativeName = "Italiano" },
        new LanguageItem { Code = "pl-PL", Name = "Polish", NativeName = "Polski" },
        new LanguageItem { Code = "nl-NL", Name = "Dutch", NativeName = "Nederlands" },
        new LanguageItem { Code = "tr-TR", Name = "Turkish", NativeName = "Türkçe" },
        new LanguageItem { Code = "vi-VN", Name = "Vietnamese", NativeName = "Tiếng Việt" }
    };
    
    public LanguageSelectorViewModel()
    {
    }
    
    [RelayCommand]
    private async Task SaveAsync()
    {
        // Save language selection
    }
    
    [RelayCommand]
    private async Task CloseAsync()
    {
    }
}

public partial class LanguageItem : ViewModelBase
{
    [ObservableProperty]
    private string _code = "";
    
    [ObservableProperty]
    private string _name = "";
    
    [ObservableProperty]
    private string _nativeName = "";
    
    [ObservableProperty]
    private bool _isSelected = false;
}
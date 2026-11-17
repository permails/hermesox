using System.Globalization;
using HermesDesktop.Core.Models;

namespace HermesDesktop.Core.Localization;

public enum AppLanguageMode
{
    System,
    English,
    Chinese
}

public static class AppLocalization
{
    private static AppLanguageMode _mode = AppLanguageMode.System;

    public static event Action? LanguageChanged;

    public static AppLanguageMode Mode => _mode;

    public static bool IsChinese => _mode switch
    {
        AppLanguageMode.Chinese => true,
        AppLanguageMode.English => false,
        _ => DetectSystemIsChinese()
    };

    public static string EffectiveCultureTag => IsChinese ? "zh-CN" : "en-US";

    public static bool DetectSystemIsChinese(CultureInfo? uiCulture = null)
    {
        var culture = uiCulture ?? CultureInfo.CurrentUICulture;
        if (culture.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase)
            || culture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (uiCulture is null)
        {
            var installed = CultureInfo.InstalledUICulture;
            if (installed.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase)
                || installed.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    public static AppLanguageMode ParseMode(string? token) => token?.Trim().ToLowerInvariant() switch
    {
        "zh" or "zh-cn" or "zh-hans" or "chinese" => AppLanguageMode.Chinese,
        "en" or "en-us" or "english" => AppLanguageMode.English,
        _ => AppLanguageMode.System
    };

    public static string ToToken(AppLanguageMode mode) => mode switch
    {
        AppLanguageMode.Chinese => "zh",
        AppLanguageMode.English => "en",
        _ => "system"
    };

    public static void SetMode(AppLanguageMode mode)
    {
        if (_mode == mode) return;
        _mode = mode;
        LanguageChanged?.Invoke();
    }

    public static void SetPreferenceToken(string? token) => SetMode(ParseMode(token));

    public static string T(string en, string zh) => IsChinese ? zh : en;

    public static string TabLabel(V2Tab tab) => tab switch
    {
        V2Tab.Bots => T("Bots", "智能体"),
        V2Tab.Chats => T("Sessions", "会话"),
        V2Tab.Tasks => T("Tasks", "任务"),
        V2Tab.Pulse => T("Pulse", "动态"),
        V2Tab.Artifacts => T("Artifacts", "产物"),
        V2Tab.Settings => T("Settings", "设置"),
        _ => tab.ToString()
    };
}

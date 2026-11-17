using System.Globalization;
using HermesDesktop.Core.Localization;
using HermesDesktop.Core.Models;
using HermesDesktop.Core.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace HermesDesktop.Core.Tests;

public class AppLocalizationTests
{
    [Fact]
    public void DetectSystemIsChinese_RecognizesChineseAndEnglishCultures()
    {
        Assert.True(AppLocalization.DetectSystemIsChinese(new CultureInfo("zh-CN")));
        Assert.True(AppLocalization.DetectSystemIsChinese(new CultureInfo("zh-Hans")));
        Assert.True(AppLocalization.DetectSystemIsChinese(new CultureInfo("zh-TW")));
        Assert.False(AppLocalization.DetectSystemIsChinese(new CultureInfo("en-US")));
        Assert.False(AppLocalization.DetectSystemIsChinese(new CultureInfo("ja-JP")));
    }

    [Fact]
    public void SetMode_SwitchesTranslatedStringsAndRoundTripsTokens()
    {
        var previous = AppLocalization.Mode;
        try
        {
            AppLocalization.SetPreferenceToken("zh");
            Assert.Equal(AppLanguageMode.Chinese, AppLocalization.Mode);
            Assert.True(AppLocalization.IsChinese);
            Assert.Equal("会话", AppLocalization.TabLabel(V2Tab.Chats));
            Assert.Equal("zh", AppLocalization.ToToken(AppLocalization.Mode));

            AppLocalization.SetPreferenceToken("en");
            Assert.Equal(AppLanguageMode.English, AppLocalization.Mode);
            Assert.False(AppLocalization.IsChinese);
            Assert.Equal("Sessions", AppLocalization.TabLabel(V2Tab.Chats));
            Assert.Equal("en", AppLocalization.ToToken(AppLocalization.Mode));

            AppLocalization.SetPreferenceToken("system");
            Assert.Equal(AppLanguageMode.System, AppLocalization.Mode);
            Assert.Equal("system", AppLocalization.ToToken(AppLocalization.Mode));
        }
        finally
        {
            AppLocalization.SetMode(previous);
        }
    }

    [Fact]
    public void AppSettings_PersistsLanguagePreference()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"hermes-l10n-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var store = new LocalCacheStore(
                Path.Combine(tempDir, "cache.db"),
                Path.Combine(tempDir, "settings.json"));

            var initial = store.LoadSettings();
            Assert.Equal("system", initial.LanguagePreference);

            initial.LanguagePreference = "zh";
            store.SaveSettings(initial);

            var reloaded = store.LoadSettings();
            Assert.Equal("zh", reloaded.LanguagePreference);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }
}

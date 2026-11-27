// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 TheIntroDB

using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using TheIntroDB.Api;
using Xunit;

namespace TheIntroDB.Tests;

public sealed class SettingsLocalizationTests
{
    [Theory]
    [InlineData("zh-CN", true)]
    [InlineData("zh_CN", true)]
    [InlineData("zh-Hans-CN", true)]
    [InlineData("zh-SG", true)]
    [InlineData("zh", true)]
    [InlineData("zh-TW", false)]
    [InlineData("en-US", false)]
    [InlineData("fr-FR", false)]
    [InlineData("", false)]
    [InlineData(@"..\configPage", false)]
    public void LoadsEmbeddedDictionaryForRequestedLocale(string locale, bool isSimplifiedChinese)
    {
        var service = new SettingsLocalizationService();
        var messages = Assert.IsType<Dictionary<string, string>>(
            service.Get(new SettingsLocalizationService.GetTranslations { Locale = locale }));

        Assert.Equal(isSimplifiedChinese ? "\u4fdd\u5b58" : "Save", messages["save"]);
        Assert.Contains("{count}", messages["clearedEntries"]);
        Assert.True(messages.Count > 100);
    }
    [Fact]
    public void BuildsEmbeddedSettingsScriptWithEnglishDictionary()
    {
        using var scriptStream = typeof(SettingsLocalizationService).Assembly
            .GetManifestResourceStream("TheIntroDB.Configuration.configPage.js");
        Assert.NotNull(scriptStream);
        using var reader = new StreamReader(scriptStream);
        var script = reader.ReadToEnd();
        const string prefix = "let messages = Object.assign(Object.create(null), ";
        var begin = script.IndexOf(prefix, System.StringComparison.Ordinal);
        Assert.True(begin >= 0);
        begin += prefix.Length;
        var end = script.IndexOf("\n}", begin, System.StringComparison.Ordinal);
        Assert.True(end > begin);
        var embedded = JsonSerializer.Deserialize<Dictionary<string, string>>(script.Substring(begin, end - begin + 2));
        var original = Assert.IsType<Dictionary<string, string>>(new SettingsLocalizationService().Get(
            new SettingsLocalizationService.GetTranslations { Locale = "en" }));
        Assert.NotNull(embedded);
        Assert.Equal<Dictionary<string, string>>(original, embedded);
    }
}

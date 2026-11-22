// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 TheIntroDB

using System.Collections.Generic;
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
}

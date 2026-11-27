// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 TheIntroDB

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace TheIntroDB.Api
{
    [Authenticated(Roles = "Admin")]
    public sealed class SettingsLocalizationService : IService
    {
        private const string Prefix = "TheIntroDB.SettingsLocales.";
        private const string Suffix = ".json";
        private static readonly System.Reflection.Assembly Assembly = typeof(SettingsLocalizationService).Assembly;
        private static readonly Dictionary<string, string> LocaleResources = Assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(Prefix, StringComparison.Ordinal) && name.EndsWith(Suffix, StringComparison.Ordinal))
            .ToDictionary(name => name.Substring(Prefix.Length, name.Length - Prefix.Length - Suffix.Length),
                name => name, StringComparer.OrdinalIgnoreCase);

        [Route("/TheIntroDB/Settings/Translations", "GET", Summary = "Get settings-page translations")]
        public sealed class GetTranslations : IReturn<Dictionary<string, string>>
        {
            public string Locale { get; set; }
        }

        public object Get(GetTranslations request)
        {
            var messages = ReadLocale("en");
            var locale = request.Locale?.Trim().Replace('_', '-') ?? string.Empty;
            var separator = locale.IndexOf('-');
            var language = separator < 0 ? locale : locale.Substring(0, separator);

            // Neutral and Simplified Chinese variants use the existing zh-CN translation.
            var simplifiedChinese = locale.Equals("zh", StringComparison.OrdinalIgnoreCase)
                || locale.Equals("zh-SG", StringComparison.OrdinalIgnoreCase)
                || locale.Equals("zh-Hans", StringComparison.OrdinalIgnoreCase)
                || locale.StartsWith("zh-Hans-", StringComparison.OrdinalIgnoreCase);
            var selected = LocaleResources.ContainsKey(locale)
                ? locale
                : simplifiedChinese ? "zh-CN" : language;
            if (!selected.Equals("en", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var entry in ReadLocale(selected))
                {
                    messages[entry.Key] = entry.Value;
                }
            }

            return messages;
        }

        private static Dictionary<string, string> ReadLocale(string locale)
        {
            if (!LocaleResources.TryGetValue(locale, out var resource))
            {
                return new Dictionary<string, string>();
            }

            using var stream = Assembly.GetManifestResourceStream(resource);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
                ?? new Dictionary<string, string>();
        }
    }
}

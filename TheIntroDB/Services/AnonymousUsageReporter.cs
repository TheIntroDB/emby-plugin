// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 TheIntroDB

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MediaBrowser.Model.Logging;
using TheIntroDB.Configuration;

namespace TheIntroDB.Services
{
    internal static class AnonymousUsageReporter
    {
        private static readonly HttpClient HttpClient = new HttpClient();
        private static readonly string SessionId = NewSessionId();
        private const string AppKey = "A-SH-4840082526";
        private const string Host = "https://analytics.theintrodb.org";

        public static void TrackPluginLoaded(Plugin plugin, ILogger logger)
        {
            PluginConfiguration config = null;
            try
            {
                config = plugin?.Configuration;
            }
            catch (Exception ex)
            {
                logger.Debug("Failed to get config for plugin_loaded event: {0}", ex.Message);
            }
            TrackEvent(
                plugin,
                logger,
                "plugin_loaded",
                new Dictionary<string, object>
                {
                    ["host"] = "emby",
                    ["enable_intro"] = config != null && config.EnableIntro ? 1 : 0,
                    ["enable_recap"] = config != null && config.EnableRecap ? 1 : 0,
                    ["enable_credits"] = config != null && config.EnableCredits ? 1 : 0,
                    ["enable_preview"] = config != null && config.EnablePreview ? 1 : 0,
                    ["ignore_existing"] = config != null && config.IgnoreMediaWithExistingSegments ? 1 : 0,
                    ["has_theintrodb_api_key"] = config != null && !string.IsNullOrWhiteSpace(config.ApiKey) ? 1 : 0
                });
        }

        public static void TrackEvent(Plugin plugin, ILogger logger, string eventName, Dictionary<string, object> props)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await TrackEventAsync(plugin, logger, eventName, props).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    logger.Debug("TrackEventAsync failed: {0}", ex.Message);
                }
            });
        }

        private static async Task TrackEventAsync(Plugin plugin, ILogger logger, string eventName, Dictionary<string, object> props)
        {
            PluginConfiguration config = null;
            try
            {
                config = plugin?.Configuration;
            }
            catch (Exception ex)
            {
                logger.Debug("Failed to get config in TrackEventAsync: {0}", ex.Message);
            }
            if (config is null || !config.EnableAnonymousUsageReporting)
            {
                return;
            }

            if (!Uri.TryCreate(Host, UriKind.Absolute, out var hostUri))
            {
                return;
            }

            var version = plugin.GetType().Assembly.GetName().Version?.ToString() ?? "0.0.0";
            var payload = new[]
            {
                new AptabaseEvent
                {
                    timestamp = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                    sessionId = SessionId,
                    eventName = eventName,
                    systemProps = new Dictionary<string, object>
                    {
                        ["locale"] = CultureInfo.CurrentCulture.Name,
                        ["osName"] = Environment.OSVersion.Platform.ToString(),
                        ["osVersion"] = Environment.OSVersion.Version.ToString(),
                        ["isDebug"] =
#if DEBUG
                            true,
#else
                            false,
#endif
                        ["appVersion"] = version,
                        ["sdkVersion"] = "theintrodb-emby-plugin@" + version
                    },
                    props = MergeProps(
                        new Dictionary<string, object>
                        {
                            ["plugin"] = plugin.Name,
                            ["plugin_version"] = version
                        },
                        props)
                }
            };

            var json = JsonSerializer.Serialize(payload);
            var requestUri = new Uri(hostUri.AbsoluteUri.TrimEnd('/') + "/api/v0/events", UriKind.Absolute);
            using (var request = new HttpRequestMessage(HttpMethod.Post, requestUri))
            {
                request.Headers.TryAddWithoutValidation("App-Key", AppKey);
                request.Headers.UserAgent.Clear();
                request.Headers.UserAgent.Add(new ProductInfoHeaderValue("theintrodb-emby-plugin", version));
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                using (var response = await HttpClient.SendAsync(request).ConfigureAwait(false))
                {
                }
            }
        }

        private static Dictionary<string, object> MergeProps(Dictionary<string, object> baseProps, Dictionary<string, object> extraProps)
        {
            if (extraProps == null || extraProps.Count == 0)
            {
                return baseProps;
            }

            foreach (var kvp in extraProps)
            {
                baseProps[kvp.Key] = kvp.Value;
            }

            return baseProps;
        }

        private static string NewSessionId()
        {
            var epochSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var randomNumber = GetRandomInt32();
            return epochSeconds.ToString(CultureInfo.InvariantCulture) + randomNumber.ToString("D8", CultureInfo.InvariantCulture);
        }

        private static int GetRandomInt32()
        {
            var bytes = new byte[4];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }

            var value = BitConverter.ToUInt32(bytes, 0);
            return (int)(value % 100000000);
        }

        private sealed class AptabaseEvent
        {
            public string timestamp { get; set; }
            public string sessionId { get; set; }
            public string eventName { get; set; }
            public Dictionary<string, object> systemProps { get; set; }
            public Dictionary<string, object> props { get; set; }

            public AptabaseEvent()
            {
                timestamp = string.Empty;
                sessionId = string.Empty;
                eventName = string.Empty;
                systemProps = new Dictionary<string, object>();
                props = new Dictionary<string, object>();
            }
        }
    }
}

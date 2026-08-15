using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Logging;
using TheIntroDB.Configuration;

namespace TheIntroDB.Api
{
    public class TheIntroDbClient
    {
        private const int MaxRequestsPerWindow = 30;
        private static readonly TimeSpan RateLimitWindow = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan MinDelayBetweenRequests = TimeSpan.FromMilliseconds(RateLimitWindow.TotalMilliseconds / MaxRequestsPerWindow);

        private static readonly SemaphoreSlim RateLimitLock = new SemaphoreSlim(1, 1);
        private static DateTime _lastRequestUtc = DateTime.MinValue;

        private readonly HttpClient _httpClient;
        private readonly Plugin _plugin;
        private readonly ILogger _logger;

        public TheIntroDbClient(HttpClient httpClient, Plugin plugin, ILogger logger)
        {
            _httpClient = httpClient;
            _plugin = plugin;
            _logger = logger;
        }

        public async Task<MediaFetchResult> GetMediaAsync(
            int? tmdbId,
            int? tvdbId,
            string imdbId,
            bool isMovie,
            int? season,
            int? episode,
            long? durationMs,
            CancellationToken cancellationToken)
        {
            if (DateTime.UtcNow < Plugin.RateLimitExpiryUtc)
            {
                var waitUntil = Plugin.RateLimitExpiryUtc;
                var delay = waitUntil - DateTime.UtcNow;
                if (delay > TimeSpan.Zero)
                {
                    _logger.Warn(
                        "TheIntroDB API rate limit is currently active. Waiting {0}s until {1} UTC to retry...",
                        (int)delay.TotalSeconds, waitUntil);
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
            }

            var config = _plugin.Configuration ?? new PluginConfiguration();
            const string baseUrl = "https://api.theintrodb.org/v3";

            var tmdbIdValue = tmdbId.GetValueOrDefault();
            var hasTmdb = tmdbIdValue > 0;
            var tvdbIdValue = tvdbId.GetValueOrDefault();
            var hasTvdb = tvdbIdValue > 0;
            var hasImdb = !string.IsNullOrWhiteSpace(imdbId);

            if (!hasTmdb && !hasTvdb && !hasImdb)
            {
                return MediaFetchResult.NotFound();
            }

            Plugin.TrackAnonymousUsageEvent(
                "theintrodb_api_media_fetch",
                new Dictionary<string, object>
                {
                    ["host"] = "emby",
                    ["result"] = "request",
                    ["media_type"] = isMovie ? "movie" : "episode",
                    ["has_tmdb"] = hasTmdb ? 1 : 0,
                    ["has_tvdb"] = hasTvdb ? 1 : 0,
                    ["has_imdb"] = hasImdb ? 1 : 0,
                    ["has_theintrodb_api_key"] = !string.IsNullOrWhiteSpace(config.ApiKey) ? 1 : 0
                });

            string query;
            if (hasTmdb)
            {
                query = isMovie
                    ? $"?tmdb_id={tmdbIdValue}"
                    : $"?tmdb_id={tmdbIdValue}&season={season}&episode={episode}";
            }
            else if (hasTvdb)
            {
                query = isMovie
                    ? $"?tvdb_id={tvdbIdValue}"
                    : $"?tvdb_id={tvdbIdValue}&season={season}&episode={episode}";
            }
            else
            {
                var encodedImdb = Uri.EscapeDataString(imdbId);
                query = isMovie
                    ? $"?imdb_id={encodedImdb}"
                    : $"?imdb_id={encodedImdb}&season={season}&episode={episode}";
            }

            if (durationMs.HasValue && durationMs.Value > 0)
            {
                query += $"&duration_ms={durationMs.Value}";
            }

            var requestUri = new Uri(baseUrl + "/media" + query, UriKind.Absolute);
            _logger.Info("TheIntroDB API request: {0}", requestUri);

            const int maxRetries = 3;
            for (var attempt = 0; attempt < maxRetries; attempt++)
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, requestUri))
                {
                    if (!string.IsNullOrWhiteSpace(config.ApiKey))
                    {
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey.Trim());
                    }

                    request.Headers.TryAddWithoutValidation("Accept", "application/json");
                    var version = _plugin.GetType().Assembly.GetName().Version?.ToString() ?? "0.0.0";
                    request.Headers.UserAgent.Clear();
                    request.Headers.UserAgent.Add(new ProductInfoHeaderValue("theintrodb-emby-plugin", version));

                    try
                    {
                        await WaitForRateLimitAsync(cancellationToken).ConfigureAwait(false);
                        using (var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false))
                        {
                            _logger.Info("TheIntroDB API response: StatusCode={0} for {1}", response.StatusCode, requestUri);

                            if ((int)response.StatusCode == 429)
                            {
                                var retryAfterSeconds = GetRetryAfterSeconds(response.Headers);
                                Plugin.RateLimitExpiryUtc = DateTime.UtcNow.AddSeconds(retryAfterSeconds);
                                _logger.Warn(
                                    "TheIntroDB API rate limit exceeded. Retry-after: {0}s. No retry will be attempted.",
                                    retryAfterSeconds);

                                Plugin.TrackAnonymousUsageEvent(
                                    "theintrodb_api_media_fetch",
                                    new Dictionary<string, object>
                                    {
                                        ["host"] = "emby",
                                        ["result"] = "http_429",
                                        ["media_type"] = isMovie ? "movie" : "episode",
                                        ["has_tmdb"] = hasTmdb ? 1 : 0,
                                        ["has_tvdb"] = hasTvdb ? 1 : 0,
                                        ["has_imdb"] = hasImdb ? 1 : 0,
                                        ["has_theintrodb_api_key"] = !string.IsNullOrWhiteSpace(config.ApiKey) ? 1 : 0
                                    });

                                return MediaFetchResult.RateLimited();
                            }

                            if (!response.IsSuccessStatusCode)
                            {
                                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                                if (!string.IsNullOrEmpty(body) && body.Length > 500)
                                {
                                    body = body.Substring(0, 500) + "...";
                                }

                                _logger.Warn("TheIntroDB API error response body: {0}", string.IsNullOrEmpty(body) ? "(empty)" : body);

                                if ((int)response.StatusCode == 404)
                                {
                                    Plugin.TrackAnonymousUsageEvent(
                                        "theintrodb_api_media_fetch",
                                        new Dictionary<string, object>
                                        {
                                            ["host"] = "emby",
                                            ["result"] = "http_404",
                                            ["media_type"] = isMovie ? "movie" : "episode",
                                            ["has_tmdb"] = hasTmdb ? 1 : 0,
                                            ["has_tvdb"] = hasTvdb ? 1 : 0,
                                            ["has_imdb"] = hasImdb ? 1 : 0,
                                            ["has_theintrodb_api_key"] = !string.IsNullOrWhiteSpace(config.ApiKey) ? 1 : 0
                                        });
                                    return MediaFetchResult.NotFound();
                                }

                                Plugin.TrackAnonymousUsageEvent(
                                    "theintrodb_api_media_fetch",
                                    new Dictionary<string, object>
                                    {
                                        ["host"] = "emby",
                                        ["result"] = "http_error",
                                        ["status"] = (int)response.StatusCode,
                                        ["media_type"] = isMovie ? "movie" : "episode",
                                        ["has_tmdb"] = hasTmdb ? 1 : 0,
                                        ["has_tvdb"] = hasTvdb ? 1 : 0,
                                        ["has_imdb"] = hasImdb ? 1 : 0,
                                        ["has_theintrodb_api_key"] = !string.IsNullOrWhiteSpace(config.ApiKey) ? 1 : 0
                                    });
                                return MediaFetchResult.Error();
                            }

                            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                            var mediaResponse = JsonSerializer.Deserialize<MediaResponse>(json, new JsonSerializerOptions
                            {
                                PropertyNameCaseInsensitive = true
                            });

                            if (mediaResponse == null)
                            {
                                var body = json;
                                if (!string.IsNullOrEmpty(body) && body.Length > 500)
                                {
                                    body = body.Substring(0, 500) + "...";
                                }
                                _logger.Warn("TheIntroDB API deserialize returned null. Body: {0}", string.IsNullOrEmpty(body) ? "(empty)" : body);
                            }
                            _logger.Debug(
                                "TheIntroDB API parsed response: IntroCount={0}, RecapCount={1}, CreditsCount={2}, PreviewCount={3}",
                                mediaResponse?.Intro?.Count ?? 0,
                                mediaResponse?.Recap?.Count ?? 0,
                                mediaResponse?.Credits?.Count ?? 0,
                                mediaResponse?.Preview?.Count ?? 0);

                            Plugin.TrackAnonymousUsageEvent(
                                "theintrodb_api_media_fetch",
                                new Dictionary<string, object>
                                {
                                    ["host"] = "emby",
                                    ["result"] = mediaResponse == null ? "success_null" : "success",
                                    ["media_type"] = isMovie ? "movie" : "episode",
                                    ["has_tmdb"] = hasTmdb ? 1 : 0,
                                    ["has_tvdb"] = hasTvdb ? 1 : 0,
                                    ["has_imdb"] = hasImdb ? 1 : 0,
                                    ["has_theintrodb_api_key"] = !string.IsNullOrWhiteSpace(config.ApiKey) ? 1 : 0,
                                    ["intro_count"] = mediaResponse?.Intro?.Count ?? 0,
                                    ["recap_count"] = mediaResponse?.Recap?.Count ?? 0,
                                    ["credits_count"] = mediaResponse?.Credits?.Count ?? 0,
                                    ["preview_count"] = mediaResponse?.Preview?.Count ?? 0
                                });

                            return MediaFetchResult.Success(mediaResponse);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.ErrorException(string.Format("TheIntroDB API request failed for {0}", requestUri), ex);
                        Plugin.TrackAnonymousUsageEvent(
                            "theintrodb_api_media_fetch",
                            new Dictionary<string, object>
                            {
                                ["host"] = "emby",
                                ["result"] = "exception",
                                ["media_type"] = isMovie ? "movie" : "episode",
                                ["has_tmdb"] = hasTmdb ? 1 : 0,
                                ["has_tvdb"] = hasTvdb ? 1 : 0,
                                ["has_imdb"] = hasImdb ? 1 : 0,
                                ["has_theintrodb_api_key"] = !string.IsNullOrWhiteSpace(config.ApiKey) ? 1 : 0
                            });

                        if (attempt < maxRetries - 1)
                        {
                            _logger.Info("TheIntroDB API retrying after exception (attempt {0}/{1})...", attempt + 1, maxRetries);
                            continue;
                        }

                        return MediaFetchResult.Error();
                    }
                }
            }

            return MediaFetchResult.Error();
        }

        private static int GetRetryAfterSeconds(HttpResponseHeaders headers)
        {
            IEnumerable<string> usageResetValues;
            if (headers.TryGetValues("X-UsageLimit-Reset", out usageResetValues))
            {
                var usageResetValue = usageResetValues.FirstOrDefault();
                int usageResetSeconds;
                if (int.TryParse(usageResetValue, out usageResetSeconds))
                {
                    return usageResetSeconds;
                }
            }

            IEnumerable<string> rateResetValues;
            if (headers.TryGetValues("X-RateLimit-Reset", out rateResetValues))
            {
                var rateResetValue = rateResetValues.FirstOrDefault();
                int rateResetSeconds;
                if (int.TryParse(rateResetValue, out rateResetSeconds))
                {
                    return rateResetSeconds;
                }
            }

            if (headers.RetryAfter != null && headers.RetryAfter.Delta.HasValue)
            {
                return (int)headers.RetryAfter.Delta.Value.TotalSeconds;
            }

            return 300;
        }

        private static async Task WaitForRateLimitAsync(CancellationToken cancellationToken)
        {
            await RateLimitLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var now = DateTime.UtcNow;
                var elapsed = now - _lastRequestUtc;
                if (elapsed < MinDelayBetweenRequests)
                {
                    var waitTime = MinDelayBetweenRequests - elapsed;
                    await Task.Delay(waitTime, cancellationToken).ConfigureAwait(false);
                }

                _lastRequestUtc = DateTime.UtcNow;
            }
            finally
            {
                RateLimitLock.Release();
            }
        }
    }
}

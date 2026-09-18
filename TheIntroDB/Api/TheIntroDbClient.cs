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
        // Keep a safety margin below the provider ceiling of 30 requests per 10 seconds.
        private const int MaxRequestsPerWindow = 25;
        private const int UsageResetClampSeconds = 24 * 60 * 60;
        private const int MaxConsecutiveRateLimitMultiplier = 8;
        private static readonly TimeSpan RateLimitWindow = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan MinDelayBetweenRequests = TimeSpan.FromMilliseconds(RateLimitWindow.TotalMilliseconds / MaxRequestsPerWindow);
        private static readonly TimeSpan MaxRateLimitDelay = TimeSpan.FromMinutes(5);

        private static readonly SemaphoreSlim RateLimitLock = new SemaphoreSlim(1, 1);
        private static DateTime _lastRequestUtc = DateTime.MinValue;
        private static DateTime _nextAllowedSendUtc = DateTime.MinValue;
        private static int _consecutiveRateLimits;
        private static DateTime _lastParkLoggedUtc = DateTime.MinValue;

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
            CancellationToken cancellationToken,
            bool trackUsage = true)
        {
            if (IsDailyUsageExhausted(_logger))
            {
                return MediaFetchResult.RateLimited();
            }

            if (DateTime.UtcNow < Plugin.RateLimitExpiryUtc)
            {
                var waitUntil = Plugin.RateLimitExpiryUtc;
                var delay = waitUntil - DateTime.UtcNow;
                if (delay > TimeSpan.Zero)
                {
                    _logger.Debug(
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

            TrackUsage(trackUsage,
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
                                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                                var retryAfterSeconds = GetRetryAfterSeconds(response.Headers, body);
                                var consecutive = Interlocked.Increment(ref _consecutiveRateLimits);
                                var isUsageLimit = IsUsageLimitResponse(body) || retryAfterSeconds > (int)MaxRateLimitDelay.TotalSeconds;
                                var waitSeconds = isUsageLimit
                                    ? retryAfterSeconds
                                    : ApplyConsecutiveBackOff(retryAfterSeconds, consecutive);

                                Plugin.RateLimitExpiryUtc = DateTime.UtcNow.AddSeconds(waitSeconds);
                                _logger.Warn(
                                    "TheIntroDB API {0} exceeded. Will not send requests until {1} UTC. Retry-after: {2}s. Consecutive 429 responses: {3}",
                                    isUsageLimit ? "daily usage limit" : "rate limit",
                                    Plugin.RateLimitExpiryUtc,
                                    retryAfterSeconds,
                                    consecutive);

                                TrackUsage(trackUsage,
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

                            Interlocked.Exchange(ref _consecutiveRateLimits, 0);
                            UpdateRateWindowFromHeaders(response.Headers);

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
                                    TrackUsage(trackUsage,
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

                                if ((int)response.StatusCode >= 500)
                                {
                                    TrackUsage(trackUsage,
                                        "theintrodb_api_media_fetch",
                                        new Dictionary<string, object>
                                        {
                                            ["host"] = "emby",
                                            ["result"] = "http_5xx",
                                            ["status"] = (int)response.StatusCode,
                                            ["media_type"] = isMovie ? "movie" : "episode",
                                            ["has_tmdb"] = hasTmdb ? 1 : 0,
                                            ["has_tvdb"] = hasTvdb ? 1 : 0,
                                            ["has_imdb"] = hasImdb ? 1 : 0,
                                            ["has_theintrodb_api_key"] = !string.IsNullOrWhiteSpace(config.ApiKey) ? 1 : 0
                                        });
                                    return MediaFetchResult.ServerError();
                                }

                                if ((int)response.StatusCode >= 400 && (int)response.StatusCode < 500)
                                {
                                    TrackUsage(trackUsage,
                                        "theintrodb_api_media_fetch",
                                        new Dictionary<string, object>
                                        {
                                            ["host"] = "emby",
                                            ["result"] = "http_4xx",
                                            ["status"] = (int)response.StatusCode,
                                            ["media_type"] = isMovie ? "movie" : "episode",
                                            ["has_tmdb"] = hasTmdb ? 1 : 0,
                                            ["has_tvdb"] = hasTvdb ? 1 : 0,
                                            ["has_imdb"] = hasImdb ? 1 : 0,
                                            ["has_theintrodb_api_key"] = !string.IsNullOrWhiteSpace(config.ApiKey) ? 1 : 0
                                        });
                                    return MediaFetchResult.ClientError();
                                }

                                TrackUsage(trackUsage,
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

                            TrackUsage(trackUsage,
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
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.ErrorException(string.Format("TheIntroDB API request failed for {0}", requestUri), ex);
                        TrackUsage(trackUsage,
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

        private static void TrackUsage(
            bool trackUsage,
            string eventName,
            Dictionary<string, object> properties)
        {
            if (trackUsage)
            {
                Plugin.TrackUsage(eventName, properties);
            }
        }

        /// <summary>
        /// True when the daily usage bucket is exhausted (park longer than the
        /// rate-limit ceiling) and the client would skip without sending.
        /// Emits one warning per park period instead of one per skipped item.
        /// </summary>
        /// <param name="logger">Logger for the deduplicated warning.</param>
        /// <returns>True when the daily bucket is exhausted and requests should be skipped.</returns>
        internal static bool IsDailyUsageExhausted(ILogger logger)
        {
            var expiryUtc = Plugin.RateLimitExpiryUtc;
            var delay = expiryUtc - DateTime.UtcNow;
            if (delay <= MaxRateLimitDelay)
            {
                return false;
            }

            if (expiryUtc != _lastParkLoggedUtc)
            {
                // One warning per park period instead of one per skipped item.
                _lastParkLoggedUtc = expiryUtc;
                logger.Warn(
                    "TheIntroDB API daily usage limit is exhausted until {0} UTC. Skipping request.",
                    expiryUtc);
            }
            else
            {
                logger.Debug(
                    "TheIntroDB API daily usage limit is exhausted until {0} UTC.",
                    expiryUtc);
            }

            return true;
        }

        /// <summary>
        /// Computes how long to wait after a 429. Usage-limit responses carry the
        /// daily bucket's reset (seconds until UTC midnight) and must be trusted
        /// for up to 24 hours — clamping them to five minutes turns an exhausted
        /// daily budget into a probe-every-five-minutes loop. Rate-limit responses
        /// carry a 10-second window and stay clamped to the five-minute ceiling.
        /// </summary>
        private static int GetRetryAfterSeconds(HttpResponseHeaders headers, string body)
        {
            IEnumerable<string> usageResetValues;
            var isUsageLimit = IsUsageLimitResponse(body);
            if (headers.TryGetValues("X-UsageLimit-Reset", out usageResetValues))
            {
                var usageResetValue = usageResetValues.FirstOrDefault();
                int usageResetSeconds;
                if (int.TryParse(usageResetValue, out usageResetSeconds) && usageResetSeconds > 0)
                {
                    if (usageResetSeconds > (int)MaxRateLimitDelay.TotalSeconds)
                    {
                        isUsageLimit = true;
                    }

                    return ClampRetryAfter(usageResetSeconds, isUsageLimit ? UsageResetClampSeconds : (int)MaxRateLimitDelay.TotalSeconds);
                }
            }

            IEnumerable<string> rateResetValues;
            if (headers.TryGetValues("X-RateLimit-Reset", out rateResetValues))
            {
                var rateResetValue = rateResetValues.FirstOrDefault();
                int rateResetSeconds;
                if (int.TryParse(rateResetValue, out rateResetSeconds) && rateResetSeconds > 0)
                {
                    return ClampRetryAfter(rateResetSeconds, isUsageLimit ? UsageResetClampSeconds : (int)MaxRateLimitDelay.TotalSeconds);
                }
            }

            if (headers.RetryAfter != null && headers.RetryAfter.Delta.HasValue)
            {
                return ClampRetryAfter((int)headers.RetryAfter.Delta.Value.TotalSeconds, isUsageLimit ? UsageResetClampSeconds : (int)MaxRateLimitDelay.TotalSeconds);
            }

            if (headers.RetryAfter != null && headers.RetryAfter.Date.HasValue)
            {
                return ClampRetryAfter((int)Math.Ceiling(
                    (headers.RetryAfter.Date.Value.UtcDateTime - DateTime.UtcNow).TotalSeconds), isUsageLimit ? UsageResetClampSeconds : (int)MaxRateLimitDelay.TotalSeconds);
            }

            return (int)MaxRateLimitDelay.TotalSeconds;
        }

        private static bool IsUsageLimitResponse(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return false;
            }

            try
            {
                using (var document = JsonDocument.Parse(body))
                {
                    if (document.RootElement.TryGetProperty("code", out var codeElement)
                        && codeElement.ValueKind == JsonValueKind.String
                        && codeElement.GetString() is string code)
                    {
                        return code == "usage_limit_exceeded" || code == "specific_media_usage_limit_exceeded";
                    }
                }
            }
            catch (JsonException)
            {
                // Not JSON (e.g. a proxy's plain-text 429): fall back to header parsing.
            }

            return false;
        }

        /// <summary>
        /// Grows the back-off wait across consecutive 429 responses so a
        /// still-exhausted bucket is probed less and less often.
        /// </summary>
        private static int ApplyConsecutiveBackOff(int baseSeconds, int consecutive)
        {
            if (consecutive <= 1)
            {
                return baseSeconds;
            }

            var multiplier = Math.Min(consecutive, MaxConsecutiveRateLimitMultiplier);
            return Math.Min(baseSeconds * multiplier, (int)MaxRateLimitDelay.TotalSeconds);
        }

        private static int ClampRetryAfter(int seconds, int maxClamp)
        {
            return Math.Max(1, Math.Min(seconds, maxClamp));
        }

        /// <summary>
        /// When the API advertises that almost no rate-limit budget remains, hold the
        /// next request until the window resets. Insurance against external consumers
        /// of the same bucket (other integrations sharing the key or public IP).
        /// </summary>
        private static void UpdateRateWindowFromHeaders(HttpResponseHeaders headers)
        {
            IEnumerable<string> remainingValues;
            if (!headers.TryGetValues("X-RateLimit-Remaining", out remainingValues))
            {
                return;
            }

            int remaining;
            if (!int.TryParse(remainingValues.FirstOrDefault(), out remaining) || remaining > 1)
            {
                return;
            }

            IEnumerable<string> resetValues;
            if (!headers.TryGetValues("X-RateLimit-Reset", out resetValues))
            {
                return;
            }

            int resetSeconds;
            if (!int.TryParse(resetValues.FirstOrDefault(), out resetSeconds) || resetSeconds <= 0)
            {
                return;
            }

            var resetUtc = DateTime.UtcNow.AddSeconds(ClampRetryAfter(resetSeconds, (int)MaxRateLimitDelay.TotalSeconds));
            if (resetUtc > _nextAllowedSendUtc)
            {
                _nextAllowedSendUtc = resetUtc;
            }
        }

        /// <summary>
        /// Waits if necessary to respect the API rate limit (30 requests per 10 seconds),
        /// plus any hold imposed by a nearly-exhausted rate window.
        /// </summary>
        private static async Task WaitForRateLimitAsync(CancellationToken cancellationToken)
        {
            await RateLimitLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var now = DateTime.UtcNow;
                var waitUntil = now;
                if (_lastRequestUtc != DateTime.MinValue)
                {
                    var pacedSend = _lastRequestUtc + MinDelayBetweenRequests;
                    if (pacedSend > waitUntil)
                    {
                        waitUntil = pacedSend;
                    }
                }

                if (_nextAllowedSendUtc > waitUntil)
                {
                    waitUntil = _nextAllowedSendUtc;
                }

                var waitTime = waitUntil - now;
                if (waitTime > TimeSpan.Zero)
                {
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

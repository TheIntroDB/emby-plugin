using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using TheIntroDB.Api;
using TheIntroDB.Models;
using TheIntroDB.Services;

namespace TheIntroDB.Providers
{
    /// <summary>
    /// Media segment provider that fetches intro/recap/credits/preview from TheIntroDB API.
    /// This provider works with Emby's task system and can be used to populate segment data.
    /// </summary>
    public class TheIntroDbSegmentProvider
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        // One warning per series whose display order cannot be expressed, not one per
        // episode of it. Static because a provider instance is short-lived (one per
        // scan or per event), so an instance field would not dedupe a scan.
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, byte> _orderSkipsLogged =
            new System.Collections.Concurrent.ConcurrentDictionary<Guid, byte>();

        private readonly ILibraryManager _libraryManager;
        private readonly ILogger _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="TheIntroDbSegmentProvider"/> class.
        /// </summary>
        /// <param name="libraryManager">Library manager to resolve items.</param>
        /// <param name="logger">Logger instance.</param>
        public TheIntroDbSegmentProvider(
          ILibraryManager libraryManager,
          ILogger logger)
        {
            _libraryManager = libraryManager;
            _logger = logger;
            _logger.Info("TheIntroDB segment provider constructed");
        }

        /// <summary>
        /// Gets the provider name.
        /// </summary>
        public string Name => Plugin.Instance?.Name ?? "TheIntroDB";

        /// <summary>
        /// Fetches segment data for a specific media item.
        /// </summary>
        /// <param name="itemId">The item ID to fetch segments for.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <param name="initializeConfiguration">Whether configuration migration may persist changes.</param>
        /// <param name="trackUsage">Whether successful lookups may emit anonymous usage events.</param>
        /// <returns>Fetch result with segment data or empty if not found.</returns>
        public async Task<SegmentFetchResult> GetMediaSegmentsAsync(
          Guid itemId,
          CancellationToken cancellationToken,
          bool initializeConfiguration = true,
          bool trackUsage = true)
        {
            _logger.Debug("GetMediaSegmentsAsync called for ItemId={0}", itemId);

            if (Plugin.Instance is null)
            {
                _logger.Warn("Early exit: Plugin.Instance is null");
                return SegmentFetchResult.NotAttempted();
            }

            if (initializeConfiguration)
            {
                Plugin.Instance.InitConfig();
            }

            var config = Plugin.Instance.Configuration;
            if (config is null)
            {
                _logger.Warn("Early exit: Plugin configuration is not available");
                return SegmentFetchResult.NotAttempted();
            }

            var item = _libraryManager.GetItemById(itemId);
            if (item is null)
            {
                _logger.Warn("Early exit: item not found for ItemId={0}", itemId);
                return SegmentFetchResult.NotAttempted();
            }

            var itemCacheKey = "item:" + itemId.ToString("N");
            if (TheIntroDbNotFoundCache.Instance.TryGetHit(itemCacheKey))
            {
                _logger.Debug("Skipping {0}: known not found in TheIntroDB (cached)", item.Name);
                return SegmentFetchResult.NotAttempted();
            }

            int? tmdbId = null;
            int? tvdbId = null;
            string imdbId = null;
            bool isMovie = false;
            int? season = null;
            int? episode = null;
            string mapping = null;
            long? tvdbEpisodeId = null;

            if (item is Movie movie)
            {
                isMovie = true;
                tmdbId = GetTmdbId(movie);
                tvdbId = GetTvdbId(movie);
                imdbId = GetImdbId(movie);
                _logger.Debug("Movie: Name={0}, TmdbId={1}, TvdbId={2}, ImdbId={3}", item.Name, tmdbId, tvdbId, imdbId ?? "(none)");
            }
            else if (item is Episode ep)
            {
                tmdbId = GetTmdbId(ep.Series);
                tvdbId = GetTvdbId(ep.Series);
                imdbId = GetImdbId(ep) ?? GetImdbId(ep.Series);
                season = ep.ParentIndexNumber;
                episode = ep.IndexNumber;

                // Emby numbers an episode in the series' display order — the order the
                // metadata fetcher used. TVDB's default order is what TheIntroDB stores,
                // so a series with any other order has to say which one its numbers are
                // in, or the API answers a different episode (issue #31: Futurama under
                // `alternate`, where S07E01 is default S06E01).
                mapping = GetDisplayOrder(ep.Series);

                // The episode's own TVDB id pins one exact record. Emby stores provider
                // ids per item, and which id lands on an episode varies by fetcher, so
                // it is only used as a pin when it is distinct from the series id.
                tvdbEpisodeId = GetTvdbEpisodeId(ep, tvdbId);

                _logger.Debug("Episode: Name={0}, Series={1}, S{2}E{3}, TmdbId={4}, TvdbId={5}, ImdbId={6}, Order={7}, TvdbEpisodeId={8}",
                  item.Name, ep.SeriesName, season, episode, tmdbId, tvdbId, imdbId ?? "(none)", mapping ?? "default", tvdbEpisodeId);
            }
            else if (item is Video video)
            {
                isMovie = false;
                tmdbId = GetTmdbId(video);
                tvdbId = GetTvdbId(video);
                imdbId = GetImdbId(video);
                season = video.ParentIndexNumber;
                episode = video.IndexNumber;
                _logger.Debug("Video: Name={0}, TmdbId={1}, TvdbId={2}, ImdbId={3}, Season={4}, Episode={5}", item.Name, tmdbId, tvdbId, imdbId ?? "(none)", season, episode);
            }

            // A non-default display order can only be honoured with a TVDB identity: a
            // tmdb_id request is always read as TMDB numbering, so answering would look
            // right and be another episode's data. No segments beats wrong segments.
            if (!isMovie && season.HasValue && episode.HasValue && mapping != null && (!tvdbId.HasValue || tvdbId.Value <= 0))
            {
                LogOrderSkipOnce(itemId, item.Name);
                return SegmentFetchResult.NotAttempted();
            }

            // Daily usage budget exhausted: the client parks until the bucket
            // rolls over, so nothing would be sent. Skip before the fetch path
            // instead of warning once per item in the scan.
            if (TheIntroDbClient.IsDailyUsageExhausted(_logger))
            {
                return SegmentFetchResult.RateLimited();
            }

            if ((!tmdbId.HasValue || tmdbId.Value <= 0) && (!tvdbId.HasValue || tvdbId.Value <= 0) && string.IsNullOrWhiteSpace(imdbId))
            {
                var providers = item.ProviderIds == null ?
                  "(null)" :
                  string.Join(",", item.ProviderIds.Select(kvp => kvp.Key + "=" + kvp.Value));
                _logger.Warn("Early exit: no TmdbId, TvdbId, or ImdbId for {0}. ProviderIds: {1}", item.Name, providers);
                TheIntroDbNotFoundCache.Instance.RememberNotFound(itemCacheKey);
                return SegmentFetchResult.NotAttempted();
            }

            if (!isMovie && (!season.HasValue || !episode.HasValue))
            {
                _logger.Warn("Early exit: TV episode missing season/episode for {0}", item.Name);
                TheIntroDbNotFoundCache.Instance.RememberNotFound(itemCacheKey);
                return SegmentFetchResult.NotAttempted();
            }

            _logger.Debug("Segment toggles: EnableIntro={0}, EnableRecap={1}, EnableCredits={2}, EnablePreview={3}, IgnoreMediaWithExistingSegments={4}",
              config.EnableIntro, config.EnableRecap, config.EnableCredits, config.EnablePreview, config.IgnoreMediaWithExistingSegments);

            var lookupKey = BuildNotFoundKey(isMovie, tmdbId, tvdbId, imdbId, season, episode, mapping);
            if (TheIntroDbNotFoundCache.Instance.TryGetHit(lookupKey))
            {
                _logger.Debug("Skipping {0}: known not found in TheIntroDB (cached 404)", item.Name);
                return SegmentFetchResult.NotAttempted();
            }

            _logger.Info("Fetching from TheIntroDB API: tmdbId={0}, tvdbId={1}, imdbId={2}, isMovie={3}, season={4}, episode={5}, order={6}, tvdbEpisodeId={7}",
              tmdbId, tvdbId, imdbId, isMovie, season, episode, mapping ?? "default", tvdbEpisodeId);

            var client = new TheIntroDbClient(_httpClient, Plugin.Instance, _logger);
            long? durationMs = item.RunTimeTicks.HasValue && item.RunTimeTicks.Value > 0 ?
              item.RunTimeTicks.Value / TimeSpan.TicksPerMillisecond :
              (long?)null;
            var mediaResult = await client.GetMediaAsync(
                tmdbId, tvdbId, imdbId, isMovie, season, episode,
                durationMs, cancellationToken, trackUsage, mapping, tvdbEpisodeId).ConfigureAwait(false);

            if (mediaResult.IsNotFound)
            {
                _logger.Info("TheIntroDB API returned no data for {0}", item.Name);
                TheIntroDbNotFoundCache.Instance.RememberNotFound(lookupKey);
                return SegmentFetchResult.NotFound();
            }

            if (mediaResult.IsRateLimited)
            {
                // The client already warned once per park period (or per real
                // 429); warn per item here and a scan through an exhausted
                // budget produces one line per library item.
                _logger.Debug("TheIntroDB API rate limited for {0}", item.Name);
                return SegmentFetchResult.RateLimited();
            }

            if (mediaResult.IsServerError)
            {
                _logger.Warn("TheIntroDB API server error for {0}", item.Name);
                return SegmentFetchResult.ServerError();
            }

            if (mediaResult.IsClientError)
            {
                _logger.Warn("TheIntroDB API rejected the request for {0} (client error). Skipping item.", item.Name);
                return SegmentFetchResult.ClientError();
            }

            if (mediaResult.IsError)
            {
                _logger.Error("TheIntroDB API error for {0}", item.Name);
                return SegmentFetchResult.Error();
            }

            var media = mediaResult.Response;

            long? runTimeTicks = item.RunTimeTicks;
            var segments = new List<MediaSegmentData>();

            if (config.EnableIntro)
            {
                AddSegments(media.Intro, true, MediaSegmentType.Intro, itemId, runTimeTicks, segments);
            }

            if (config.EnableRecap)
            {
                AddSegments(media.Recap, true, MediaSegmentType.Recap, itemId, runTimeTicks, segments);
            }

            if (config.EnableCredits)
            {
                AddSegments(media.Credits, false, MediaSegmentType.Credits, itemId, runTimeTicks, segments);
            }

            if (config.EnablePreview)
            {
                AddSegments(media.Preview, false, MediaSegmentType.Preview, itemId, runTimeTicks, segments);
            }

            var introCount = segments.Count(s => s.Type == MediaSegmentType.Intro);
            var recapCount = segments.Count(s => s.Type == MediaSegmentType.Recap);
            var creditsCount = segments.Count(s => s.Type == MediaSegmentType.Credits);
            var previewCount = segments.Count(s => s.Type == MediaSegmentType.Preview);
            if (trackUsage)
            {
                Plugin.TrackUsage(
                  "segments_generated",
                  new Dictionary<string, object>
                  {
                      ["host"] = "emby",
                      ["media_type"] = isMovie ? "movie" : "episode",
                      ["has_tmdb"] = tmdbId.HasValue && tmdbId.Value > 0 ? 1 : 0,
                      ["has_tvdb"] = tvdbId.HasValue && tvdbId.Value > 0 ? 1 : 0,
                      ["has_imdb"] = !string.IsNullOrWhiteSpace(imdbId) ? 1 : 0,
                      ["segments_total"] = segments.Count,
                      ["segments_intro"] = introCount,
                      ["segments_recap"] = recapCount,
                      ["segments_credits"] = creditsCount,
                      ["segments_preview"] = previewCount,
                      ["enable_intro"] = config.EnableIntro ? 1 : 0,
                      ["enable_recap"] = config.EnableRecap ? 1 : 0,
                      ["enable_credits"] = config.EnableCredits ? 1 : 0,
                      ["enable_preview"] = config.EnablePreview ? 1 : 0,
                      ["has_theintrodb_api_key"] = !string.IsNullOrWhiteSpace(config.ApiKey) ? 1 : 0
                  });
            }

            _logger.Info("Returning {0} segments for {1}", segments.Count, item.Name);
            return SegmentFetchResult.Success(segments);
        }

        /// <summary>
        /// Checks if the provider supports the given item type.
        /// </summary>
        /// <param name="item">The media item to check.</param>
        /// <returns>True if the item is supported (Movie or Episode).</returns>
        public bool Supports(BaseItem item)
        {
            var supported = item is Episode || item is Movie || item is Video;
            _logger.Debug("Supports({0}, {1}): {2}", item?.Name ?? "null", item?.GetType().Name ?? "null", supported);
            return supported;
        }

        private static string BuildNotFoundKey(bool isMovie, int? tmdbId, int? tvdbId, string imdbId, int? season, int? episode, string mapping)
        {
            var type = isMovie ? "movie" : "episode";
            string idPart;
            if (tvdbId.HasValue && tvdbId.Value > 0)
            {
                idPart = "tvdb:" + tvdbId.Value;
            }
            else if (tmdbId.HasValue && tmdbId.Value > 0)
            {
                idPart = "tmdb:" + tmdbId.Value;
            }
            else
            {
                idPart = "imdb:" + imdbId;
            }

            if (isMovie)
            {
                return type + ":" + idPart;
            }

            // The order is part of the address: "default" S6E1 and "alternate" S6E1 are
            // different episodes, so a miss recorded for one must not answer for the other.
            var orderPart = string.IsNullOrWhiteSpace(mapping) ? string.Empty : ":" + mapping;
            return type + ":" + idPart + ":" + season + ":" + episode + orderPart;
        }

        /// <summary>
        /// Reads the series' display order as an explicit TVDB order name, or null when
        /// the series numbers its episodes in the default (aired) order.
        /// </summary>
        /// <remarks>
        /// Emby's Series.DisplayOrder is an enum with three values (Aired, Dvd,
        /// Absolute), not a free-form string like Jellyfin's. Aired is the ordinary
        /// order — the one TheIntroDB stores — so it is reported as no preference and
        /// costs no conversion. Emby cannot express alternate/regional ordering at all.
        /// </remarks>
        /// <param name="series">The series the episode belongs to.</param>
        /// <returns>The order name to send as mapping, or null.</returns>
        internal static string GetDisplayOrder(Series series)
        {
            if (series == null)
            {
                return null;
            }

            switch (series.DisplayOrder)
            {
                case SeriesDisplayOrder.Dvd:
                    return "dvd";
                case SeriesDisplayOrder.Absolute:
                    return "absolute";
                default:
                    return null;
            }
        }

        /// <summary>
        /// Returns the episode's own TVDB id, when it carries one that is distinct from
        /// the series id.
        /// </summary>
        /// <remarks>
        /// Emby populates provider ids per item and which id lands on an Episode depends
        /// on the fetcher: some store the episode record, others repeat the series id.
        /// Only the former is a useful pin, and sending the series id as an episode id
        /// would make the API look for an episode that does not exist.
        /// </remarks>
        /// <param name="episode">The episode.</param>
        /// <param name="seriesTvdbId">The series' TVDB id, already resolved.</param>
        /// <returns>The episode id to pin, or null.</returns>
        private static long? GetTvdbEpisodeId(BaseItem episode, int? seriesTvdbId)
        {
            if (episode?.ProviderIds == null)
            {
                return null;
            }

            var id = GetProviderId(episode, "Tvdb");
            long parsed;
            if (string.IsNullOrWhiteSpace(id)
                || !long.TryParse(id, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out parsed)
                || parsed <= 0)
            {
                return null;
            }

            if (seriesTvdbId.HasValue && seriesTvdbId.Value > 0 && parsed == seriesTvdbId.Value)
            {
                return null;
            }

            return parsed;
        }

        /// <summary>
        /// Logs the "non-default order without a TVDB id" skip once per series rather
        /// than once per episode, which would be hundreds of lines on a large show.
        /// </summary>
        /// <param name="itemId">Emby id of the item being skipped.</param>
        /// <param name="name">Item name, for the log line.</param>
        private void LogOrderSkipOnce(Guid itemId, string name)
        {
            if (_orderSkipsLogged.TryAdd(itemId, 0))
            {
                _logger.Warn(
                  "Skipping {0}: the series uses a non-default display order, but Emby carries no TVDB id to express it against. Add TVDB metadata to the series so the API can convert the episode numbers.",
                  name);
            }
        }

        private static int? GetTmdbId(BaseItem item)
        {
            if (item?.ProviderIds is null)
            {
                return null;
            }

            var id = GetProviderId(item, "Tmdb");
            if (!string.IsNullOrWhiteSpace(id))
            {
                return int.TryParse(id, out
                  var n) ? (int?)n : null;
            }

            return null;
        }

        private static int? GetTvdbId(BaseItem item)
        {
            if (item?.ProviderIds is null)
            {
                return null;
            }

            var id = GetProviderId(item, "Tvdb");
            if (!string.IsNullOrWhiteSpace(id))
            {
                return int.TryParse(id, out var n) ? (int?)n : null;
            }

            return null;
        }

        private static string GetImdbId(BaseItem item)
        {
            if (item?.ProviderIds is null)
            {
                return null;
            }

            var id = GetProviderId(item, "Imdb");
            return string.IsNullOrWhiteSpace(id) ? null : id;
        }

        private static string GetProviderId(BaseItem item, string provider)
        {
            if (item?.ProviderIds is null || string.IsNullOrWhiteSpace(provider))
            {
                return null;
            }

            foreach (var kvp in item.ProviderIds)
            {
                if (string.Equals(kvp.Key, provider, StringComparison.OrdinalIgnoreCase))
                {
                    return kvp.Value;
                }
            }

            return null;
        }

        private static void AddSegments(
          IEnumerable<SegmentTimestamp> stamps,
          bool endRequired,
          MediaSegmentType type,
          Guid itemId,
          long? runTimeTicks,
          List<MediaSegmentData> segments)
        {
            if (stamps is null)
            {
                return;
            }

            foreach (var stamp in stamps)
            {
                if (stamp is null || !stamp.HasValidRange(endRequired))
                {
                    continue;
                }

                // Null/missing start → 0 (beginning of media)
                long startMs = stamp.StartMs ?? 0;

                // Null/missing end → use media runtime as fallback
                long endMs;
                if (stamp.EndMs.HasValue && stamp.EndMs.Value > 0)
                {
                    endMs = stamp.EndMs.Value;
                }
                else if (runTimeTicks.HasValue && runTimeTicks.Value > 0)
                {
                    endMs = runTimeTicks.Value / TimeSpan.TicksPerMillisecond;
                }
                else
                {
                    // No end time and no runtime, still add the segment
                    // with EndTicks = 0 so the marker writer can handle it
                    // (it will substitute the media duration there)
                    endMs = 0;
                }

                // Allow start == 0 and end == 0 (unknown boundaries);
                // only skip if we have a concrete end that is before start
                if (endMs > 0 && endMs <= startMs)
                {
                    continue;
                }

                long startTicks = startMs * TimeSpan.TicksPerMillisecond;
                long endTicks = endMs * TimeSpan.TicksPerMillisecond;

                segments.Add(new MediaSegmentData
                {
                    ItemId = itemId,
                    Type = type,
                    StartTicks = startTicks,
                    EndTicks = endTicks
                });
            }
        }
    }
}

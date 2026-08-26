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
using MediaBrowser.Model.Logging;
using TheIntroDB.Api;
using TheIntroDB.Models;

namespace TheIntroDB.Providers
{
    /// <summary>
    /// Media segment provider that fetches intro/recap/credits/preview from TheIntroDB API.
    /// This provider works with Emby's task system and can be used to populate segment data.
    /// </summary>
    public class TheIntroDbSegmentProvider
    {
        private static readonly HttpClient _httpClient = new HttpClient();
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
            _logger.Info("GetMediaSegmentsAsync called for ItemId={0}", itemId);

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

            int? tmdbId = null;
            int? tvdbId = null;
            string imdbId = null;
            bool isMovie = false;
            int? season = null;
            int? episode = null;

            if (item is Movie movie)
            {
                isMovie = true;
                tmdbId = GetTmdbId(movie);
                tvdbId = GetTvdbId(movie);
                imdbId = GetImdbId(movie);
                _logger.Info("Movie: Name={0}, TmdbId={1}, TvdbId={2}, ImdbId={3}", item.Name, tmdbId, tvdbId, imdbId ?? "(none)");
            }
            else if (item is Episode ep)
            {
                tmdbId = GetTmdbId(ep) ?? GetTmdbId(ep.Series);
                tvdbId = GetTvdbId(ep) ?? GetTvdbId(ep.Series);
                imdbId = GetImdbId(ep) ?? GetImdbId(ep.Series);
                season = ep.ParentIndexNumber;
                episode = ep.IndexNumber;
                _logger.Info("Episode: Name={0}, Series={1}, S{2}E{3}, TmdbId={4}, TvdbId={5}, ImdbId={6}",
                  item.Name, ep.SeriesName, season, episode, tmdbId, tvdbId, imdbId ?? "(none)");
            }
            else if (item is Video video)
            {
                isMovie = false;
                tmdbId = GetTmdbId(video);
                tvdbId = GetTvdbId(video);
                imdbId = GetImdbId(video);
                season = video.ParentIndexNumber;
                episode = video.IndexNumber;
                _logger.Info("Video: Name={0}, TmdbId={1}, TvdbId={2}, ImdbId={3}, Season={4}, Episode={5}", item.Name, tmdbId, tvdbId, imdbId ?? "(none)", season, episode);
            }

            if ((!tmdbId.HasValue || tmdbId.Value <= 0) && (!tvdbId.HasValue || tvdbId.Value <= 0) && string.IsNullOrWhiteSpace(imdbId))
            {
                var providers = item.ProviderIds == null ?
                  "(null)" :
                  string.Join(",", item.ProviderIds.Select(kvp => kvp.Key + "=" + kvp.Value));
                _logger.Warn("Early exit: no TmdbId, TvdbId, or ImdbId for {0}. ProviderIds: {1}", item.Name, providers);
                return SegmentFetchResult.NotAttempted();
            }

            if (!isMovie && (!season.HasValue || !episode.HasValue))
            {
                _logger.Warn("Early exit: TV episode missing season/episode for {0}", item.Name);
                return SegmentFetchResult.NotAttempted();
            }

            _logger.Debug("Segment toggles: EnableIntro={0}, EnableRecap={1}, EnableCredits={2}, EnablePreview={3}, IgnoreMediaWithExistingSegments={4}",
              config.EnableIntro, config.EnableRecap, config.EnableCredits, config.EnablePreview, config.IgnoreMediaWithExistingSegments);

            _logger.Info("Fetching from TheIntroDB API: tmdbId={0}, tvdbId={1}, imdbId={2}, isMovie={3}, season={4}, episode={5}",
              tmdbId, tvdbId, imdbId, isMovie, season, episode);

            var client = new TheIntroDbClient(_httpClient, Plugin.Instance, _logger);
            long? durationMs = item.RunTimeTicks.HasValue && item.RunTimeTicks.Value > 0 ?
              item.RunTimeTicks.Value / TimeSpan.TicksPerMillisecond :
              (long?)null;
            var mediaResult = await client.GetMediaAsync(
                tmdbId, tvdbId, imdbId, isMovie, season, episode,
                durationMs, cancellationToken, trackUsage).ConfigureAwait(false);

            if (mediaResult.IsNotFound)
            {
                _logger.Info("TheIntroDB API returned no data for {0}", item.Name);
                return SegmentFetchResult.NotFound();
            }

            if (mediaResult.IsRateLimited)
            {
                _logger.Warn("TheIntroDB API rate limited for {0}", item.Name);
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

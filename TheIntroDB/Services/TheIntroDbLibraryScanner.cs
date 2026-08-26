using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Logging;
using TheIntroDB.Configuration;
using TheIntroDB.Data;
using TheIntroDB.Models;
using TheIntroDB.Providers;

namespace TheIntroDB.Services
{
    /// <summary>
    /// Scans the library and fetches TheIntroDB segments for supported items.
    /// </summary>
    public class TheIntroDbLibraryScanner
    {
        private const int MaxConsecutiveApiFailures = 20;
        private const int MaxRateLimitRetriesPerItem = 2;
        private static readonly TimeSpan MaxRateLimitRetryDelay = TimeSpan.FromMinutes(5);

        private readonly ILibraryManager _libraryManager;
        private readonly ILogger _logger;
        private readonly TheIntroDbSegmentProvider _segmentProvider;
        private readonly ITheIntroDbSegmentRepository _repository;
        private readonly TheIntroDbChapterMarkerWriter _chapterMarkerWriter;

        /// <summary>
        /// Initializes a new instance of the <see cref="TheIntroDbLibraryScanner"/> class.
        /// </summary>
        /// <param name="libraryManager">Library manager used to enumerate media items.</param>
        /// <param name="segmentProvider">Provider used to fetch segment data for each item.</param>
        /// <param name="logger">Logger instance.</param>
        public TheIntroDbLibraryScanner(
          ILibraryManager libraryManager,
          TheIntroDbSegmentProvider segmentProvider,
          ITheIntroDbSegmentRepository repository,
          TheIntroDbChapterMarkerWriter chapterMarkerWriter,
          ILogger logger)
        {
            _libraryManager = libraryManager;
            _segmentProvider = segmentProvider;
            _repository = repository;
            _chapterMarkerWriter = chapterMarkerWriter;
            _logger = logger;
        }

        /// <summary>
        /// Scans all supported media items in the library for TheIntroDB segments.
        /// </summary>
        /// <param name="progress">Progress callback.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Total number of segments found.</returns>
        public async Task<int> ScanLibraryAsync(
          Action<string, int, int> progress,
          CancellationToken cancellationToken,
          bool preview = false)
        {
            _logger.Info("Starting library scan for TheIntroDB segments");

            var plugin = Plugin.Instance;
            if (plugin == null)
            {
                _logger.Error("TheIntroDB plugin instance is not available");
                return 0;
            }

            if (!preview)
            {
                plugin.InitConfig();
            }

            var config = plugin.Configuration;
            var requestedTypes = GetRequestedTypes(config);
            if (requestedTypes.Count == 0)
            {
                _logger.Info("TheIntroDB scan skipped: all segment types disabled in plugin settings");
                return 0;
            }

            var items = GetFilteredItems(config);

            // Pre-filter: when IgnoreMediaWithExistingSegments is enabled,
            // fetch all item IDs that already have segments in ONE batch query
            // and exclude them from the scan. This avoids thousands of
            // individual HasAnySegments/GetSegments calls inside the loop.
            BaseItem[] itemsToScan;
            int cachedSegmentCount = 0;

            if (config.IgnoreMediaWithExistingSegments)
            {
                var knownIds = _repository.GetSegmentedIds();
                var knownSet = new HashSet<long>(knownIds);

                itemsToScan = items
                    .Where(i => !knownSet.Contains(i.InternalId))
                    .ToArray();

                cachedSegmentCount = knownIds.Count;

                _logger.Info("TheIntroDB scan: {0} total items, {1} already have segments (cached), {2} items to scan",
                    items.Length, cachedSegmentCount, itemsToScan.Length);
            }
            else
            {
                itemsToScan = items;
            }

            itemsToScan = OrderItemsForScan(itemsToScan, _repository.GetLastCheckedUtc());

            var totalSegments = cachedSegmentCount;
            var processed = 0;
            var total = itemsToScan.Length;
            var consecutiveApiFailures = 0;
            var lookupBudget = new ScanLookupBudget(config.MaxLookupsPerRun);
            var stopScan = false;

            for (var i = 0; i < itemsToScan.Length; i++)
            {
                var item = itemsToScan[i];

                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                try
                {
                    if (!preview)
                    {
                        plugin.InitConfig();
                    }
                    config = plugin.Configuration;
                    requestedTypes = GetRequestedTypes(config);
                    if (requestedTypes.Count == 0)
                    {
                        processed++;
                        continue;
                    }

                    SegmentFetchResult result = null;
                    var rateLimitRetries = 0;
                    while (true)
                    {
                        if (!lookupBudget.TryBeginLookup())
                        {
                            _logger.Info("TheIntroDB scan stopped at the configured lookup limit of {0}",
                                config.MaxLookupsPerRun);
                            stopScan = true;
                            break;
                        }

                        result = await _segmentProvider.GetMediaSegmentsAsync(
                            item.Id, cancellationToken, !preview, !preview).ConfigureAwait(false);

                        if (!result.WasApiAttempted)
                        {
                            lookupBudget.CancelLatestLookup();
                            break;
                        }

                        if (!result.IsRateLimited)
                        {
                            break;
                        }

                        if (!CanRetryAfterRateLimit(rateLimitRetries))
                        {
                            _logger.Warn("TheIntroDB scan stopped after HTTP 429 persisted for {0} retries on {1}.",
                                MaxRateLimitRetriesPerItem, item.Name);
                            stopScan = true;
                            break;
                        }

                        rateLimitRetries++;
                        await WaitForRateLimitAsync(cancellationToken).ConfigureAwait(false);
                    }

                    if (stopScan)
                    {
                        break;
                    }

                    if (!result.WasApiAttempted)
                    {
                        processed++;
                        continue;
                    }

                    if (!result.IsLookupCompleted)
                    {
                        if (ShouldCountTowardFailureLimit(result))
                        {
                            consecutiveApiFailures++;
                            if (consecutiveApiFailures >= MaxConsecutiveApiFailures)
                            {
                                _logger.Error(
                                    "TheIntroDB scan aborting: {0} consecutive failures (API may be down). Skipping remaining {1} items.",
                                    consecutiveApiFailures, itemsToScan.Length - i);
                                break;
                            }
                        }
                        else if (result.IsServerError)
                        {
                            _logger.Warn(
                                "TheIntroDB API server error for {0} (not counting toward consecutive failure limit)",
                                item.Name);
                        }
                        else
                        {
                            _logger.Warn(
                                "TheIntroDB API rejected request for {0} (client error, not counting toward consecutive failure limit)",
                                item.Name);
                        }

                        continue;
                    }

                    consecutiveApiFailures = 0;

                    var storedSegments = result.Segments.Select(s => new StoredMediaSegment
                    {
                        ItemInternalId = item.InternalId,
                        Type = s.Type,
                        StartTicks = s.StartTicks,
                        EndTicks = s.EndTicks
                    }).ToList();

                    if (!preview)
                    {
                        _repository.ReplaceSegments(item.InternalId,
                          storedSegments, DateTime.UtcNow);
                    }

                    var inserted = _chapterMarkerWriter.ApplyMarkers(
                      item, storedSegments, config, preview);

                    totalSegments += storedSegments.Count;
                    processed++;

                    progress?.Invoke(string.Format(
                        preview
                            ? "Previewed {0}: {1} segments fetched, {2} markers would be added"
                            : "Processed {0}: {1} segments fetched, {2} markers added",
                        item.Name, storedSegments.Count, inserted),
                      processed, total);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    consecutiveApiFailures++;
                    if (consecutiveApiFailures >= MaxConsecutiveApiFailures)
                    {
                        _logger.Error(
                            "TheIntroDB scan aborting: {0} consecutive failures (API may be down). Skipping remaining {1} items.",
                            consecutiveApiFailures, itemsToScan.Length - i);
                        break;
                    }

                    _logger.ErrorException(string.Format(
                        "Error processing item {0}", item.Name), ex);
                }
            }

            _logger.Info("Library scan completed. Found {0} total segments ({1} cached + {2} newly scanned) in {3} items with {4} lookups. Preview={5}",
                totalSegments, cachedSegmentCount, totalSegments - cachedSegmentCount,
                cachedSegmentCount + processed, lookupBudget.Used, preview);
            return totalSegments;
        }

        private static List<string> ParseCommaSeparatedIds(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return new List<string>();
            }

            return raw.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(id => id.Trim())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToList();
        }

        private async Task WaitForRateLimitAsync(CancellationToken cancellationToken)
        {
            var delay = Plugin.RateLimitExpiryUtc - DateTime.UtcNow;
            if (delay <= TimeSpan.Zero)
            {
                return;
            }

            delay = delay > MaxRateLimitRetryDelay ? MaxRateLimitRetryDelay : delay;
            _logger.Warn("TheIntroDB scan waiting {0}s for provider rate limit expiry before retrying the same item.",
                (int)Math.Ceiling(delay.TotalSeconds));
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }

        private static bool CanRetryAfterRateLimit(int retriesCompleted)
        {
            return retriesCompleted < MaxRateLimitRetriesPerItem;
        }

        /// <summary>
        /// Returns true when a lookup result should advance the consecutive-failure
        /// counter that aborts the scan. Only genuine "API may be down" signals count
        /// (transport failures after retries — <see cref="SegmentFetchResult.Error"/>).
        /// Server (5xx) and client (4xx) responses prove the API is reachable and
        /// answering, so they never abort the scan.
        /// </summary>
        private static bool ShouldCountTowardFailureLimit(SegmentFetchResult result)
        {
            return result.WasApiAttempted
                && !result.IsLookupCompleted
                && !result.IsServerError
                && !result.IsClientError
                && !result.IsRateLimited;
        }

        private static BaseItem[] OrderItemsForScan(
            IEnumerable<BaseItem> items,
            IReadOnlyDictionary<long, DateTime> lastCheckedUtcByItemId)
        {
            var lastChecked = lastCheckedUtcByItemId ?? new Dictionary<long, DateTime>();
            return (items ?? Array.Empty<BaseItem>())
                .OrderBy(item => lastChecked.ContainsKey(item.InternalId) ? 1 : 0)
                .ThenBy(item => lastChecked.TryGetValue(item.InternalId, out var checkedUtc) ? checkedUtc : DateTime.MinValue)
                .ThenBy(item => item.InternalId)
                .ToArray();
        }

        private static string NormalizeConfiguredId(string raw)
        {
            return string.IsNullOrWhiteSpace(raw)
                ? string.Empty
                : raw.Replace("-", string.Empty).Trim();
        }

        private BaseItem ResolveItemById(string configuredId)
        {
            if (string.IsNullOrWhiteSpace(configuredId))
            {
                return null;
            }

            if (Guid.TryParse(configuredId.Trim(), out var guid))
            {
                return _libraryManager.GetItemById(guid);
            }

            var normalized = NormalizeConfiguredId(configuredId);
            return normalized.Length == 32 && Guid.TryParseExact(normalized, "N", out guid)
                ? _libraryManager.GetItemById(guid)
                : null;
        }

        private static bool MatchesConfiguredId(
            BaseItem item,
            HashSet<string> configuredIdSet,
            HashSet<long> resolvedInternalIds)
        {
            if (item == null)
            {
                return false;
            }

            return configuredIdSet.Contains(item.Id.ToString("N")) ||
                   configuredIdSet.Contains(item.InternalId.ToString()) ||
                   resolvedInternalIds.Contains(item.InternalId);
        }

        private static bool IsInSelectedLibraries(
            BaseItem item,
            HashSet<string> selectedLibraryIdSet,
            HashSet<long> resolvedLibraryInternalIds)
        {
            BaseItem current = item;
            while (current != null)
            {
                if (MatchesConfiguredId(current, selectedLibraryIdSet, resolvedLibraryInternalIds))
                {
                    return true;
                }

                current = current.GetParent();
            }

            return false;
        }

        private static bool IsSelectedShowOrMovie(BaseItem item, HashSet<string> selectedShowIdSet)
        {
            if (item == null)
            {
                return false;
            }

            if (item is Movie &&
                (selectedShowIdSet.Contains(item.Id.ToString("N")) ||
                 selectedShowIdSet.Contains(item.InternalId.ToString())))
            {
                return true;
            }

            BaseItem current = item;
            while (current != null)
            {
                if (current is Series &&
                    (selectedShowIdSet.Contains(current.Id.ToString("N")) ||
                     selectedShowIdSet.Contains(current.InternalId.ToString())))
                {
                    return true;
                }

                current = current.GetParent();
            }

            return false;
        }

        private BaseItem[] GetScopedItems(
            List<string> selectedLibraryIds,
            HashSet<long> resolvedLibraryInternalIds)
        {
            var queryAncestorIds = new HashSet<long>(resolvedLibraryInternalIds);

            foreach (var selectedLibraryId in selectedLibraryIds)
            {
                if (long.TryParse(selectedLibraryId, out var internalId))
                {
                    queryAncestorIds.Add(internalId);
                }
            }

            if (queryAncestorIds.Count == 0)
            {
                return Array.Empty<BaseItem>();
            }

            var scopedItems = _libraryManager.GetItemList(new InternalItemsQuery
            {
                AncestorIds = queryAncestorIds.ToArray(),
                IncludeItemTypes = new[] { "Movie", "Episode" },
                Recursive = true
            }) ?? Array.Empty<BaseItem>();

            return scopedItems
                .GroupBy(item => item.InternalId)
                .Select(group => group.First())
                .ToArray();
        }

        private BaseItem[] GetFilteredItems(PluginConfiguration config)
        {
            var selectedLibraryIds = ParseCommaSeparatedIds(config.SelectedLibraryIds);
            var selectedShowIds = ParseCommaSeparatedIds(config.SelectedShowIds);

            bool hasLibraryFilter = selectedLibraryIds.Count > 0;
            bool hasShowFilter = selectedShowIds.Count > 0;

            // Normalize IDs by stripping hyphens so both formats match.
            var libraryIdSet = new HashSet<string>(
                selectedLibraryIds.Select(NormalizeConfiguredId),
                StringComparer.OrdinalIgnoreCase);
            var showIdSet = new HashSet<string>(
                selectedShowIds.Select(NormalizeConfiguredId),
                StringComparer.OrdinalIgnoreCase);

            // ----- Show-only filter or combined filters: query all items and filter in memory -----
            var query = new InternalItemsQuery
            {
                IncludeItemTypes = new[] { "Movie", "Episode" },
                Recursive = true
            };

            var allItems = _libraryManager.GetItemList(query) ?? Array.Empty<BaseItem>();
            if (allItems.Length == 0)
            {
                _logger.Warn("TheIntroDB scan: no items returned for IncludeItemTypes query. Falling back to broad query and filtering.");
                var fallback = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    Recursive = true
                }) ?? Array.Empty<BaseItem>();
                allItems = fallback.Where(i => i is Episode || i is Movie).ToArray();
            }

            if (!hasShowFilter)
            {
                if (!hasLibraryFilter)
                {
                    return allItems;
                }
            }

            var resolvedLibraryInternalIds = new HashSet<long>();
            if (hasLibraryFilter)
            {
                foreach (var selectedLibraryId in selectedLibraryIds)
                {
                    if (long.TryParse(selectedLibraryId, out var internalId))
                    {
                        resolvedLibraryInternalIds.Add(internalId);
                    }

                    var resolvedLibrary = ResolveItemById(selectedLibraryId);
                    if (resolvedLibrary != null)
                    {
                        resolvedLibraryInternalIds.Add(resolvedLibrary.InternalId);
                    }
                }

                _logger.Info(
                    "TheIntroDB scan: resolved {0}/{1} selected library IDs to internal IDs",
                    resolvedLibraryInternalIds.Count,
                    selectedLibraryIds.Count);
            }

            var libraryScopedItems = hasLibraryFilter
                ? GetScopedItems(selectedLibraryIds, resolvedLibraryInternalIds)
                : Array.Empty<BaseItem>();

            if (hasLibraryFilter && !hasShowFilter && libraryScopedItems.Length > 0)
            {
                _logger.Info(
                    "TheIntroDB scan: {0} items returned from selected library scope query",
                    libraryScopedItems.Length);
                return libraryScopedItems;
            }

            var filteredItems = allItems.Where(item =>
            {
                if (item == null)
                {
                    return false;
                }

                if (hasLibraryFilter &&
                    IsInSelectedLibraries(item, libraryIdSet, resolvedLibraryInternalIds))
                {
                    return true;
                }

                return hasShowFilter && IsSelectedShowOrMovie(item, showIdSet);
            }).ToArray();

            if (hasLibraryFilter && libraryScopedItems.Length > 0)
            {
                filteredItems = filteredItems
                    .Concat(libraryScopedItems)
                    .GroupBy(item => item.InternalId)
                    .Select(group => group.First())
                    .ToArray();
            }

            _logger.Info("TheIntroDB scan: {0} items after library/show filtering (from {1} total)", filteredItems.Length, allItems.Length);
            return filteredItems;
        }

        private static HashSet<MediaSegmentType> GetRequestedTypes(PluginConfiguration config)
        {
            var set = new HashSet<MediaSegmentType>();
            if (config == null)
            {
                return set;
            }

            if (config.EnableIntro) set.Add(MediaSegmentType.Intro);
            if (config.EnableRecap) set.Add(MediaSegmentType.Recap);
            if (config.EnableCredits) set.Add(MediaSegmentType.Credits);
            if (config.EnablePreview) set.Add(MediaSegmentType.Preview);
            return set;
        }
    }
}

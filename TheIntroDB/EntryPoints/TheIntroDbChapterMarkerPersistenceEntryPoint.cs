using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using TheIntroDB.Configuration;
using TheIntroDB.Data;
using TheIntroDB.Models;
using TheIntroDB.Providers;
using TheIntroDB.Services;

namespace TheIntroDB.EntryPoints
{
    public sealed class TheIntroDbChapterMarkerPersistenceEntryPoint : IServerEntryPoint
    {
        private readonly ILibraryManager _libraryManager;
        private readonly IItemRepository _itemRepository;
        private readonly TheIntroDbSegmentRepository _segmentRepository;
        private readonly TheIntroDbChapterMarkerWriter _chapterWriter;
        private readonly TheIntroDbSegmentProvider _segmentProvider;
        private readonly ILogger _logger;

        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly ConcurrentDictionary<long, Task> _pendingTasks = new ConcurrentDictionary<long, Task>();
        private readonly ConcurrentDictionary<long, byte> _writesInProgress = new ConcurrentDictionary<long, byte>();

        public TheIntroDbChapterMarkerPersistenceEntryPoint(
            ILibraryManager libraryManager,
            IItemRepository itemRepository,
            IApplicationPaths applicationPaths,
            ILogManager logManager)
        {
            _libraryManager = libraryManager;
            _itemRepository = itemRepository;
            _logger = Plugin.Instance?.FileLogger ?? logManager.GetLogger("TheIntroDB");
            _segmentRepository = new TheIntroDbSegmentRepository(_logger, applicationPaths);
            _chapterWriter = new TheIntroDbChapterMarkerWriter(_itemRepository, _segmentRepository, _logger);
            _segmentProvider = new TheIntroDbSegmentProvider(libraryManager, _logger);
        }

        public void Run()
        {
            _libraryManager.ItemUpdated += LibraryManager_ItemUpdated;
            _libraryManager.ItemAdded += LibraryManager_ItemAdded;

            // Background marker-repair pass: restores TheIntroDB markers that Emby
            // may have overwritten (e.g. placeholder chapters regenerated for files
            // without embedded chapters). Runs on a configurable interval.
            _ = TrackTaskAsync(RunMarkerRepairLoopAsync(_cts.Token));
        }

        public void Dispose()
        {
            _libraryManager.ItemUpdated -= LibraryManager_ItemUpdated;
            _libraryManager.ItemAdded -= LibraryManager_ItemAdded;

            // Signal in-flight tasks to finish and wait for them (with timeout)
            _cts.Cancel();
            try
            {
                var snapshot = _pendingTasks.Values.ToArray();
                if (snapshot.Length > 0)
                {
                    Task.WaitAll(snapshot, TimeSpan.FromSeconds(10));
                }
            }
            catch (AggregateException)
            {
                // Swallow task exceptions during shutdown, they were already logged
            }

            _segmentRepository.Dispose();
            _cts.Dispose();
        }

        private void LibraryManager_ItemAdded(object sender, ItemChangeEventArgs e)
        {
            var item = e?.Item;
            if (item is not Episode && item is not Movie)
            {
                return;
            }

            _ = TrackTaskAsync(OnDemandFetchAsync(item, "item_added", _cts.Token));
        }

        private void LibraryManager_ItemUpdated(object sender, ItemChangeEventArgs e)
        {
            try
            {
                var item = e?.Item;
                if (item is not Episode && item is not Movie)
                {
                    return;
                }

                var config = Plugin.Instance?.Configuration;
                if (config == null)
                {
                    return;
                }

                if (!config.EnableIntro && !config.EnableRecap && !config.EnableCredits && !config.EnablePreview)
                {
                    return;
                }

                var internalId = item.InternalId;
                if (_writesInProgress.ContainsKey(internalId))
                {
                    return;
                }

                // Also trigger on-demand fetch for updated items that have no segments yet
                _ = TrackTaskAsync(OnDemandFetchAsync(item, "item_updated", _cts.Token));

                _ = TrackTaskAsync(Task.Run(() => EnsureMarkersApplied(item, config), _cts.Token));
            }
            catch (Exception ex)
            {
                _logger.Error("TheIntroDB chapter marker persistence event handler exception: " + ex.Message);
            }
        }

        /// <summary>
        /// Ensures TheIntroDB markers are applied for an item. Returns true when
        /// markers were (re)written, false when nothing was needed or the item
        /// was skipped (already processing, no segments, or markers present).
        /// </summary>
        private bool EnsureMarkersApplied(BaseItem item, PluginConfiguration config)
        {
            var internalId = item.InternalId;
            if (!_writesInProgress.TryAdd(internalId, 0))
            {
                return false;
            }

            try
            {
                var segments = _segmentRepository.GetSegments(internalId);
                if (segments == null || segments.Count == 0)
                {
                    return false;
                }

                var existingChapters = _itemRepository.GetChapters(item) ?? new List<ChapterInfo>();
                var ownedChapters = _segmentRepository.GetOwnedChapters(internalId);
                if (!NeedsMarkerApply(existingChapters, ownedChapters, config))
                {
                    return false;
                }

                return _chapterWriter.ApplyMarkers(item, segments, config) > 0;
            }
            catch (Exception ex)
            {
                _logger.Error("TheIntroDB chapter marker persistence exception: " + ex.Message);
                return false;
            }
            finally
            {
                _writesInProgress.TryRemove(internalId, out _);
            }
        }

        /// <summary>
        /// Periodic repair loop: every MarkerRepairIntervalHours, scans items that
        /// have stored segments and restores markers that Emby may have overwritten
        /// (e.g. when Emby regenerates placeholder chapters for files without
        /// embedded chapters). Only touches items whose markers are missing, so it
        /// is cheap for healthy libraries.
        /// </summary>
        private async Task RunMarkerRepairLoopAsync(CancellationToken cancellationToken)
        {
            var intervalHours = Plugin.Instance?.Configuration?.MarkerRepairIntervalHours ?? 0;
            if (intervalHours <= 0)
            {
                _logger.Debug("TheIntroDB marker repair disabled (MarkerRepairIntervalHours=0), not starting loop");
                return;
            }

            try
            {
                // Give Emby a moment to settle after plugin load so the first
                // pass doesn't race an in-progress library scan.
                await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var config = Plugin.Instance?.Configuration;
                    if (config != null && config.MarkerRepairIntervalHours > 0)
                    {
                        await RepairWipedMarkersAsync(config, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.Error("TheIntroDB marker repair pass failed: " + ex.Message);
                }

                intervalHours = Plugin.Instance?.Configuration?.MarkerRepairIntervalHours ?? 0;
                if (intervalHours <= 0)
                {
                    _logger.Debug("TheIntroDB marker repair disabled (MarkerRepairIntervalHours=0), stopping loop");
                    return;
                }

                try
                {
                    await Task.Delay(TimeSpan.FromHours(intervalHours), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        private Task RepairWipedMarkersAsync(PluginConfiguration config, CancellationToken cancellationToken)
        {
            var segmentedIds = new HashSet<long>(_segmentRepository.GetAllSegmentedItemIds());
            if (segmentedIds.Count == 0)
            {
                _logger.Debug("TheIntroDB marker repair: no items with stored segments, skipping");
                return Task.CompletedTask;
            }

            var items = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { "Movie", "Episode" },
                Recursive = true
            }) ?? Array.Empty<BaseItem>();

            var repaired = 0;
            var checkedItems = 0;

            foreach (var item in items)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                if (item == null || !segmentedIds.Contains(item.InternalId))
                {
                    continue;
                }

                checkedItems++;
                if (EnsureMarkersApplied(item, config))
                {
                    repaired++;
                }
            }

            _logger.Info("TheIntroDB marker repair pass complete: {0}/{1} segmented items needed marker restoration", repaired, checkedItems);
            return Task.CompletedTask;
        }

        private static bool NeedsMarkerApply(
            IReadOnlyList<ChapterInfo> chapters,
            IReadOnlyList<OwnedChapterMarker> ownedChapters,
            PluginConfiguration config)
        {
            var hasIntro = !config.EnableIntro;
            var hasRecap = !config.EnableRecap;
            var hasCredits = !config.EnableCredits;
            var hasPreview = !config.EnablePreview;

            foreach (var c in chapters)
            {
                if (!hasIntro)
                {
                    if (c.MarkerType == MarkerType.IntroStart ||
                        c.MarkerType == MarkerType.IntroEnd ||
                        string.Equals(c.Name, "IntroStartMarker", StringComparison.Ordinal) ||
                        string.Equals(c.Name, "IntroEndMarker", StringComparison.Ordinal))
                    {
                        hasIntro = true;
                    }
                }

                if (!hasCredits)
                {
                    if (c.MarkerType == MarkerType.CreditsStart ||
                        string.Equals(c.Name, "CreditsStartMarker", StringComparison.Ordinal))
                    {
                        hasCredits = true;
                    }
                }

                if (!hasRecap)
                {
                    if (string.Equals(c.Name, "Recap", StringComparison.Ordinal) ||
                        string.Equals(c.Name, "Recap End", StringComparison.Ordinal) ||
                        string.Equals(c.Name, "Recap (TheIntroDB)", StringComparison.Ordinal) ||
                        string.Equals(c.Name, "Recap End (TheIntroDB)", StringComparison.Ordinal) ||
                        (IsDurablyOwnedChapter(c, ownedChapters) &&
                            (ChapterMarkerPolicy.HasOwnedLabel(c.Name, "Recap") ||
                             ChapterMarkerPolicy.HasOwnedLabel(c.Name, "Recap End") ||
                             ChapterMarkerPolicy.HasOwnedLabel(c.Name, "Recap" + ChapterMarkerPolicy.TheIntroDbTag) ||
                             ChapterMarkerPolicy.HasOwnedLabel(c.Name, "Recap End" + ChapterMarkerPolicy.TheIntroDbTag))))
                    {
                        hasRecap = true;
                    }
                }

                if (!hasPreview)
                {
                    if (string.Equals(c.Name, "Preview", StringComparison.Ordinal) ||
                        string.Equals(c.Name, "Preview End", StringComparison.Ordinal) ||
                        string.Equals(c.Name, "Preview (TheIntroDB)", StringComparison.Ordinal) ||
                        string.Equals(c.Name, "Preview End (TheIntroDB)", StringComparison.Ordinal) ||
                        (IsDurablyOwnedChapter(c, ownedChapters) &&
                            (ChapterMarkerPolicy.HasOwnedLabel(c.Name, "Preview") ||
                             ChapterMarkerPolicy.HasOwnedLabel(c.Name, "Preview End") ||
                             ChapterMarkerPolicy.HasOwnedLabel(c.Name, "Preview" + ChapterMarkerPolicy.TheIntroDbTag) ||
                             ChapterMarkerPolicy.HasOwnedLabel(c.Name, "Preview End" + ChapterMarkerPolicy.TheIntroDbTag))))
                    {
                        hasPreview = true;
                    }
                }

                if (hasIntro && hasRecap && hasCredits && hasPreview)
                {
                    return false;
                }
            }

            return !(hasIntro && hasRecap && hasCredits && hasPreview);
        }

        private static bool IsDurablyOwnedChapter(
            ChapterInfo chapter,
            IReadOnlyList<OwnedChapterMarker> ownedChapters)
        {
            return ownedChapters != null && ownedChapters.Any(owned =>
                owned.MarkerType == chapter.MarkerType &&
                owned.StartTicks == chapter.StartPositionTicks &&
                string.Equals(owned.Name, chapter.Name, StringComparison.Ordinal) &&
                ChapterMarkerPolicy.HasOwnershipToken(chapter.Name, owned.OwnerToken));
        }

        private async Task OnDemandFetchAsync(BaseItem item, string trigger, CancellationToken cancellationToken)
        {
            try
            {
                var config = Plugin.Instance?.Configuration;
                if (config == null || !config.EnableOnDemandFetch)
                {
                    return;
                }

                if (!IsItemAllowedByFilters(item, config))
                {
                    _logger.Debug("On-demand fetch skipped ({0}): {1} not in selected libraries/shows", trigger, item.Name);
                    return;
                }

                var internalId = item.InternalId;

                // Gate with the same writesInProgress used by EnsureMarkersApplied to
                // prevent duplicate concurrent processing of the same item
                if (!_writesInProgress.TryAdd(internalId, 0))
                {
                    _logger.Debug("On-demand fetch skipped ({0}): already processing {1}", trigger, item.Name);
                    return;
                }

                try
                {
                    // Check if segments already exist (under the gate to avoid races)
                    var existing = _segmentRepository.GetSegments(internalId);
                    if (existing != null && existing.Count > 0)
                    {
                        _logger.Debug("On-demand fetch skipped ({0}): segments already exist for {1}", trigger, item.Name);
                        return;
                    }

                    _logger.Info("On-demand segment fetch triggered ({0}) for {1} ({2})", trigger, item.Name, item.GetType().Name);

                    var result = await _segmentProvider.GetMediaSegmentsAsync(item.Id, cancellationToken).ConfigureAwait(false);

                    if (result.Segments == null || result.Segments.Count == 0)
                    {
                        _logger.Info("On-demand segment fetch ({0}): no segments returned for {1}", trigger, item.Name);
                        return;
                    }

                    var storedSegments = result.Segments.Select(s => new StoredMediaSegment
                    {
                        ItemInternalId = internalId,
                        Type = s.Type,
                        StartTicks = s.StartTicks,
                        EndTicks = s.EndTicks
                    }).ToList();

                    _segmentRepository.ReplaceSegments(internalId, storedSegments, DateTime.UtcNow);
                    _chapterWriter.ApplyMarkers(item, storedSegments, config);

                    _logger.Info("On-demand segment fetch completed ({0}) for {1}: {2} segments, markers applied", trigger, item.Name, storedSegments.Count);
                }
                finally
                {
                    _writesInProgress.TryRemove(internalId, out _);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.Debug("On-demand segment fetch cancelled ({0}) for {1}", trigger, item?.Name ?? "null");
            }
            catch (Exception ex)
            {
                _logger.Error("On-demand segment fetch failed ({0}) for {1}: {2}", trigger, item?.Name ?? "null", ex.Message);
            }
        }

        private static bool IsItemAllowedByFilters(BaseItem item, PluginConfiguration config)
        {
            var selectedLibraryIds = string.IsNullOrWhiteSpace(config.SelectedLibraryIds)
                ? new List<string>()
                : config.SelectedLibraryIds.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(id => id.Trim()).Where(id => id.Length > 0).ToList();

            var selectedShowIds = string.IsNullOrWhiteSpace(config.SelectedShowIds)
                ? new List<string>()
                : config.SelectedShowIds.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(id => id.Trim()).Where(id => id.Length > 0).ToList();

            bool hasLibraryFilter = selectedLibraryIds.Count > 0;
            bool hasShowFilter = selectedShowIds.Count > 0;

            if (!hasLibraryFilter && !hasShowFilter)
                return true;

            var libraryIdSet = new HashSet<string>(
                selectedLibraryIds.Select(id => id.Replace("-", string.Empty).Trim()),
                StringComparer.OrdinalIgnoreCase);

            // Check library membership by walking parent chain
            if (hasLibraryFilter)
            {
                BaseItem current = item;
                while (current != null)
                {
                    if (libraryIdSet.Contains(current.Id.ToString("N")) ||
                        libraryIdSet.Contains(current.InternalId.ToString()))
                    {
                        return true;
                    }

                    current = current.GetParent();
                }
            }

            // Check show/movie membership
            if (hasShowFilter)
            {
                var showIdSet = new HashSet<string>(
                    selectedShowIds.Select(id => id.Replace("-", string.Empty).Trim()),
                    StringComparer.OrdinalIgnoreCase);

                if (item is Movie &&
                    (showIdSet.Contains(item.Id.ToString("N")) ||
                     showIdSet.Contains(item.InternalId.ToString())))
                {
                    return true;
                }

                BaseItem current = item;
                while (current != null)
                {
                    if (current is Series &&
                        (showIdSet.Contains(current.Id.ToString("N")) ||
                         showIdSet.Contains(current.InternalId.ToString())))
                    {
                        return true;
                    }

                    current = current.GetParent();
                }
            }

            return false;
        }

        private async Task TrackTaskAsync(Task task)
        {
            // Register the task so Dispose() can wait for it to complete
            var key = task.Id;
            _pendingTasks.TryAdd(key, task);
            try
            {
                await task.ConfigureAwait(false);
            }
            finally
            {
                _pendingTasks.TryRemove(key, out _);
            }
        }
    }
}

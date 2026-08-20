using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Logging;
using TheIntroDB.Configuration;
using TheIntroDB.Data;
using TheIntroDB.Models;
using TheIntroDB.Providers;

namespace TheIntroDB.EntryPoints
{
    public class TheIntroDbUsageReportingEntryPoint : IServerEntryPoint
    {
        private const long MinJumpTicks = 15 * TimeSpan.TicksPerSecond;
        private const long SegmentEndToleranceTicks = 5 * TimeSpan.TicksPerSecond;
        private static readonly TimeSpan MinReportInterval = TimeSpan.FromSeconds(30);

        private readonly ISessionManager _sessionManager;
        private readonly TheIntroDbSegmentRepository _repository;
        private readonly ILibraryManager _libraryManager;
        private readonly ILogger _logger;
        private readonly ConcurrentDictionary<string, PlaybackState> _states = new ConcurrentDictionary<string, PlaybackState>();
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly ConcurrentDictionary<long, Task> _pendingTasks = new ConcurrentDictionary<long, Task>();

        public TheIntroDbUsageReportingEntryPoint(
            ISessionManager sessionManager,
            IApplicationPaths applicationPaths,
            ILibraryManager libraryManager,
            ILogManager logManager)
        {
            _sessionManager = sessionManager;
            _libraryManager = libraryManager;
            _logger = Plugin.Instance?.FileLogger ?? logManager.GetLogger("TheIntroDB");
            _repository = new TheIntroDbSegmentRepository(_logger, applicationPaths);
        }

        public void Run()
        {
            _ = TrackTaskAsync(Task.Run(async () =>
            {
                for (var attempt = 0; attempt < 5; attempt++)
                {
                    try
                    {
                        Plugin.TrackPluginLoaded();
                        return;
                    }
                    catch (Exception ex)
                    {
                        _logger.Error("TrackPluginLoaded attempt {0}/5 failed: {1}", attempt + 1, ex.Message);
                        await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                    }
                }
            }));
            _sessionManager.PlaybackProgress += SessionManager_PlaybackProgress;
            _sessionManager.PlaybackStopped += SessionManager_PlaybackStopped;
            _sessionManager.PlaybackStart += SessionManager_PlaybackStart;
        }

        public void Dispose()
        {
            _sessionManager.PlaybackProgress -= SessionManager_PlaybackProgress;
            _sessionManager.PlaybackStopped -= SessionManager_PlaybackStopped;
            _sessionManager.PlaybackStart -= SessionManager_PlaybackStart;

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

            _repository.Dispose();
            _cts.Dispose();
        }

        private void SessionManager_PlaybackStopped(object sender, PlaybackStopEventArgs e)
        {
            if (e?.Session?.Id == null)
            {
                return;
            }

            _states.TryRemove(e.Session.Id, out _);
        }

        private void SessionManager_PlaybackProgress(object sender, PlaybackProgressEventArgs e)
        {
            try
            {
                if (e?.Session?.Id == null || e.Item == null)
                {
                    return;
                }

                var sessionId = e.Session.Id;
                var currentTicks = e.PlaybackPositionTicks ?? 0;
                var itemInternalId = e.Item.InternalId;

                var state = _states.GetOrAdd(sessionId, _ => new PlaybackState(itemInternalId, currentTicks));
                if (state.ItemInternalId != itemInternalId)
                {
                    state.ItemInternalId = itemInternalId;
                    state.LastPositionTicks = currentTicks;
                    state.LastReportedKey = null;
                    state.LastReportedUtc = DateTime.MinValue;
                    return;
                }

                var lastTicks = state.LastPositionTicks;
                state.LastPositionTicks = currentTicks;

                if (lastTicks <= 0 || currentTicks <= 0)
                {
                    return;
                }

                var delta = currentTicks - lastTicks;
                if (delta < MinJumpTicks)
                {
                    return;
                }

                var segments = _repository.GetSegments(itemInternalId);
                if (segments == null || segments.Count == 0)
                {
                    return;
                }

                StoredMediaSegment matched = null;
                foreach (var s in segments)
                {
                    if (lastTicks >= s.StartTicks && lastTicks <= s.EndTicks)
                    {
                        matched = s;
                        break;
                    }
                }

                if (matched == null)
                {
                    return;
                }

                if (currentTicks < (matched.EndTicks - SegmentEndToleranceTicks))
                {
                    return;
                }

                var reportKey = matched.Type + ":" + matched.StartTicks.ToString(CultureInfo.InvariantCulture);
                if (state.LastReportedKey == reportKey && DateTime.UtcNow - state.LastReportedUtc < MinReportInterval)
                {
                    return;
                }

                state.LastReportedKey = reportKey;
                state.LastReportedUtc = DateTime.UtcNow;

                var config = Plugin.Instance == null ? null : Plugin.Instance.Configuration;
                Plugin.TrackUsage(
                    "segment_skipped",
                    new Dictionary<string, object>
                    {
                        ["host"] = "emby",
                        ["segment_type"] = matched.Type.ToString().ToLowerInvariant(),
                        ["has_theintrodb_api_key"] = config != null && !string.IsNullOrWhiteSpace(config.ApiKey) ? 1 : 0,
                        ["jump_seconds"] = (int)(delta / TimeSpan.TicksPerSecond)
                    });
            }
            catch (Exception ex)
            {
                _logger.Error("Usage reporting playback monitor exception: " + ex.Message);
            }
        }

        private void SessionManager_PlaybackStart(object sender, PlaybackProgressEventArgs e)
        {
            try
            {
                if (e?.Item == null)
                {
                    return;
                }

                var item = e.Item;
                var config = Plugin.Instance?.Configuration;
                if (config == null || !config.EnableOnDemandFetch)
                {
                    return;
                }

                if (!IsItemAllowedByFilters(item, config))
                {
                    return;
                }

                var internalId = item.InternalId;
                var existingSegments = _repository.GetSegments(internalId);
                if (existingSegments != null && existingSegments.Count > 0)
                {
                    return;
                }

                _logger.Info("On-demand segment fetch triggered (playback_start) for {0} ({1})", item.Name, item.GetType().Name);

                _ = TrackTaskAsync(Task.Run(async () =>
                {
                    try
                    {
                        var provider = new TheIntroDbSegmentProvider(_libraryManager, _logger);
                        var result = await provider.GetMediaSegmentsAsync(item.Id, CancellationToken.None).ConfigureAwait(false);

                        if (result.Segments == null || result.Segments.Count == 0)
                        {
                            _logger.Info("On-demand segment fetch (playback_start): no segments returned for {0}", item.Name);
                            return;
                        }

                        var storedSegments = result.Segments.Select(s => new StoredMediaSegment
                        {
                            ItemInternalId = internalId,
                            Type = s.Type,
                            StartTicks = s.StartTicks,
                            EndTicks = s.EndTicks
                        }).ToList();

                        _repository.ReplaceSegments(internalId, storedSegments, DateTime.UtcNow);

                        _logger.Info("On-demand segment fetch completed (playback_start) for {0}: {1} segments", item.Name, storedSegments.Count);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error("On-demand segment fetch failed (playback_start) for {0}: {1}", item?.Name ?? "null", ex.Message);
                    }
                }));
            }
            catch (Exception ex)
            {
                _logger.Error("PlaybackStart handler exception: " + ex.Message);
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

        private sealed class PlaybackState
        {
            public long ItemInternalId;
            public long LastPositionTicks;
            public string LastReportedKey;
            public DateTime LastReportedUtc;

            public PlaybackState(long itemInternalId, long lastPositionTicks)
            {
                ItemInternalId = itemInternalId;
                LastPositionTicks = lastPositionTicks;
                LastReportedKey = null;
                LastReportedUtc = DateTime.MinValue;
            }
        }
    }
}

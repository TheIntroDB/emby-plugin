using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Tasks;
using TheIntroDB.Data;
using TheIntroDB.Providers;
using TheIntroDB.Services;

namespace TheIntroDB.Tasks
{
    /// <summary>
    /// Scheduled task that scans the library for media segments from TheIntroDB
    /// </summary>
    public class TheIntroDbMediaSegmentScanTask : IScheduledTask
    {
        private readonly ILogger _logger;
        private readonly TheIntroDbLibraryScanner _libraryScanner;

        /// <summary>
        /// Initializes a new instance of the <see cref="TheIntroDbMediaSegmentScanTask"/> class.
        /// </summary>
        /// <param name="libraryManager">Library manager</param>
        /// <param name="itemRepository">Item repository for chapter markers</param>
        /// <param name="applicationPaths">Application paths for DB storage</param>
        /// <param name="logManager">Logger manager</param>
        public TheIntroDbMediaSegmentScanTask(
            ILibraryManager libraryManager,
            IItemRepository itemRepository,
            IApplicationPaths applicationPaths,
            ILogManager logManager)
        {
            _logger = Plugin.Instance?.FileLogger ?? logManager.GetLogger("TheIntroDB");
            var segmentProvider = new TheIntroDbSegmentProvider(libraryManager, _logger);
            var repository = new TheIntroDbSegmentRepository(_logger, applicationPaths);
            var chapterWriter = new TheIntroDbChapterMarkerWriter(itemRepository, repository, _logger);
            _libraryScanner = new TheIntroDbLibraryScanner(libraryManager, segmentProvider, repository, chapterWriter, _logger);
        }

        /// <inheritdoc />
        public string Name => "TheIntroDB Media Segment Scan";

        /// <inheritdoc />
        public string Key => "TheIntroDBMediaSegmentScan";

        /// <inheritdoc />
        public string Description => "Scans your media library for intro, recap, credits, and preview segments from TheIntroDB";

        /// <inheritdoc />
        public string Category => "Library";

        /// <inheritdoc />
        public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "(unknown)";
            _logger.Info("Starting TheIntroDB media segment scan (assembly {0})", v);

            try
            {
                if (Plugin.Instance == null)
                {
                    _logger.Error("TheIntroDB plugin instance is not available");
                    return;
                }

                var config = Plugin.Instance.Configuration;
                _logger.Info("TheIntroDB settings: Intro={0}, Recap={1}, Credits={2}, Preview={3}, IgnoreExisting={4}, ReplaceOwned={5}",
                    config.EnableIntro,
                    config.EnableRecap,
                    config.EnableCredits,
                    config.EnablePreview,
                    config.IgnoreMediaWithExistingSegments,
                    config.ReplaceExistingMarkers);

                var totalSegments = await _libraryScanner.ScanLibraryAsync(
                    new Action<string, int, int>((message, current, total) =>
                    {
                        var percentComplete = total > 0 ? (double)current / total * 100 : 0;
                        progress.Report(percentComplete);
                        _logger.Info("{0} ({1}/{2})", message, current, total);
                    }),
                    cancellationToken).ConfigureAwait(false);

                _logger.Info("TheIntroDB media segment scan completed successfully. Found {0} segments.", totalSegments);
            }
            catch (Exception ex)
            {
                _logger.ErrorException("Error during TheIntroDB media segment scan", ex);
                throw;
            }
        }

        /// <inheritdoc />
        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            // Run daily at 2 AM
            return new[]
            {
                new TaskTriggerInfo
                {
                    Type = TaskTriggerInfo.TriggerDaily,
                    TimeOfDayTicks = TimeSpan.FromHours(2).Ticks,
                    MaxRuntimeTicks = TimeSpan.FromHours(6).Ticks
                }
            };
        }
    }
}

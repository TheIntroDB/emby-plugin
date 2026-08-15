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
    public class TheIntroDbMediaSegmentPreviewTask : IScheduledTask, IDisposable
    {
        private readonly ILogger _logger;
        private readonly TheIntroDbLibraryScanner _libraryScanner;
        private readonly TheIntroDbSegmentRepository _readOnlyRepository;

        public TheIntroDbMediaSegmentPreviewTask(
            ILibraryManager libraryManager,
            IItemRepository itemRepository,
            IApplicationPaths applicationPaths,
            ILogManager logManager)
        {
            _logger = Plugin.Instance?.FileLogger ?? logManager.GetLogger("TheIntroDB");
            var segmentProvider = new TheIntroDbSegmentProvider(libraryManager, _logger);
            _readOnlyRepository = new TheIntroDbSegmentRepository(_logger, applicationPaths, true);
            var repository = new PreviewSegmentRepository(_readOnlyRepository);
            var chapterWriter = new TheIntroDbChapterMarkerWriter(itemRepository, repository, _logger);
            _libraryScanner = new TheIntroDbLibraryScanner(libraryManager, segmentProvider, repository, chapterWriter, _logger);
        }

        public string Name => "TheIntroDB Media Segment Preview";

        public string Key => "TheIntroDBMediaSegmentPreview";

        public string Description => "Fetches and previews eligible intro and credits markers without changing plugin data or Emby chapters; operational logs are still written";

        public string Category => "Library";

        public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "(unknown)";
            _logger.Info("Starting TheIntroDB media segment preview (assembly {0})", version);

            int totalSegments;
            using (_readOnlyRepository.BeginReadOnlySession(cancellationToken))
            {
                totalSegments = await _libraryScanner.ScanLibraryAsync(
                    (message, current, total) =>
                    {
                        var percentComplete = total > 0 ? (double)current / total * 100 : 0;
                        progress.Report(percentComplete);
                        _logger.Info("{0} ({1}/{2})", message, current, total);
                    },
                    cancellationToken,
                    true).ConfigureAwait(false);
            }

            _logger.Info("TheIntroDB media segment preview completed. Found {0} segments.", totalSegments);
        }

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            return Array.Empty<TaskTriggerInfo>();
        }

        public void Dispose()
        {
            _readOnlyRepository.Dispose();
        }
    }
}

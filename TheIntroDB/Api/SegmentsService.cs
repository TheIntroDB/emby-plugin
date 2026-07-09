using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Services;
using TheIntroDB.Data;
using TheIntroDB.Models;
using TheIntroDB.Services;

namespace TheIntroDB.Api
{
    public sealed class SegmentsService : IService
    {
        [Route("/TheIntroDB/Segments", "GET", Summary = "Get stored segment data for an item")]
        public sealed class GetSegments : IReturn<List<StoredMediaSegment>>
        {
            [ApiMember(Name = "InternalId", Description = "The internal item id", IsRequired = true, DataType = "long", ParameterType = "query", Verb = "GET")]
            public long InternalId { get; set; }
        }

        [Route("/TheIntroDB/Segments/Chapters", "POST", Summary = "Remove TheIntroDB chapters and segments for items")]
        public sealed class DeleteChaptersRequest : IReturn<DeleteChaptersResponse>
        {
            public List<string> ItemIds { get; set; }
        }

        public sealed class DeleteChaptersResponse
        {
            public int TotalItems { get; set; }
            public int TotalChaptersRemoved { get; set; }
            public List<ItemResult> Results { get; set; } = new();
        }

        public sealed class ItemResult
        {
            public long InternalId { get; set; }
            public string Name { get; set; }
            public int ChaptersRemoved { get; set; }
            public bool SegmentsCleared { get; set; }
        }

        private readonly IApplicationPaths _applicationPaths;
        private readonly ILibraryManager _libraryManager;
        private readonly IItemRepository _itemRepository;
        private readonly ILogger _logger;

        public SegmentsService(IApplicationPaths applicationPaths, ILibraryManager libraryManager, IItemRepository itemRepository, ILogManager logManager)
        {
            _applicationPaths = applicationPaths;
            _libraryManager = libraryManager;
            _itemRepository = itemRepository;
            _logger = Plugin.Instance?.FileLogger ?? logManager.GetLogger("TheIntroDB");
        }

        public object Get(GetSegments request)
        {
            using (var repo = new TheIntroDbSegmentRepository(_logger, _applicationPaths))
            {
                var segments = repo.GetSegments(request.InternalId);
                return new List<StoredMediaSegment>(segments);
            }
        }

        public object Post(DeleteChaptersRequest request)
        {
            var response = new DeleteChaptersResponse();

            if (request.ItemIds == null || request.ItemIds.Count == 0)
            {
                return response;
            }

            var guids = request.ItemIds
                .Select(id => Guid.TryParse(id, out var g) ? g : Guid.Empty)
                .Where(g => g != Guid.Empty)
                .Distinct()
                .ToArray();

            if (guids.Length == 0)
            {
                return response;
            }

            var items = guids
                .Select(g => _libraryManager.GetItemById(g))
                .Where(i => i != null)
                .Cast<BaseItem>()
                .ToArray();

            response.TotalItems = items.Length;

            foreach (var item in items)
            {
                var chapterWriter = new TheIntroDbChapterMarkerWriter(_itemRepository, _logger);
                var chaptersRemoved = chapterWriter.RemoveMarkers(item);

                var segmentsCleared = false;
                using (var repo = new TheIntroDbSegmentRepository(_logger, _applicationPaths))
                {
                    repo.DeleteSegments(item.InternalId);
                    segmentsCleared = true;
                }

                response.TotalChaptersRemoved += chaptersRemoved;
                response.Results.Add(new ItemResult
                {
                    InternalId = item.InternalId,
                    Name = item.Name,
                    ChaptersRemoved = chaptersRemoved,
                    SegmentsCleared = segmentsCleared
                });
            }

            return response;
        }
    }
}


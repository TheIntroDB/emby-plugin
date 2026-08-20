using System;
using System.Collections.Generic;
using TheIntroDB.Models;

namespace TheIntroDB.Data
{
    public interface ITheIntroDbSegmentRepository
    {
        bool HasAllSegmentTypes(long itemInternalId, IReadOnlyCollection<MediaSegmentType> types);
        HashSet<MediaSegmentType> GetStoredSegmentTypes(long itemInternalId);
        IReadOnlyList<StoredMediaSegment> GetSegments(long itemInternalId);
        IReadOnlyList<long> GetAllSegmentedItemIds();
        IReadOnlyDictionary<long, DateTime> GetLastCheckedUtcByItemId();
        void ReplaceSegments(long itemInternalId, IReadOnlyList<StoredMediaSegment> segments, DateTime updatedUtc);
        IReadOnlyList<OwnedChapterMarker> GetOwnedChapters(long itemInternalId);
        void ReplaceOwnedChapters(long itemInternalId, IReadOnlyList<OwnedChapterMarker> chapters, DateTime updatedUtc);
    }

    public sealed class PreviewSegmentRepository : ITheIntroDbSegmentRepository
    {
        private readonly ITheIntroDbSegmentRepository _backing;

        public PreviewSegmentRepository(ITheIntroDbSegmentRepository backing)
        {
            _backing = backing ?? throw new ArgumentNullException(nameof(backing));
        }

        public bool HasAllSegmentTypes(long itemInternalId, IReadOnlyCollection<MediaSegmentType> types)
            => _backing.HasAllSegmentTypes(itemInternalId, types);

        public HashSet<MediaSegmentType> GetStoredSegmentTypes(long itemInternalId)
            => _backing.GetStoredSegmentTypes(itemInternalId);

        public IReadOnlyList<StoredMediaSegment> GetSegments(long itemInternalId)
            => _backing.GetSegments(itemInternalId);

        public IReadOnlyList<long> GetAllSegmentedItemIds()
            => _backing.GetAllSegmentedItemIds();

        public IReadOnlyDictionary<long, DateTime> GetLastCheckedUtcByItemId()
            => _backing.GetLastCheckedUtcByItemId();

        public void ReplaceSegments(long itemInternalId, IReadOnlyList<StoredMediaSegment> segments, DateTime updatedUtc)
            => throw new InvalidOperationException("Preview repository is read-only");

        public IReadOnlyList<OwnedChapterMarker> GetOwnedChapters(long itemInternalId)
            => _backing.GetOwnedChapters(itemInternalId);

        public void ReplaceOwnedChapters(long itemInternalId, IReadOnlyList<OwnedChapterMarker> chapters, DateTime updatedUtc)
            => throw new InvalidOperationException("Preview repository is read-only");
    }
}

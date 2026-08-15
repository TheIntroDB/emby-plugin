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
        void ReplaceSegments(long itemInternalId, IReadOnlyList<StoredMediaSegment> segments, DateTime updatedUtc);
    }

    public sealed class PreviewSegmentRepository : ITheIntroDbSegmentRepository
    {
        public bool HasAllSegmentTypes(long itemInternalId, IReadOnlyCollection<MediaSegmentType> types)
            => false;

        public HashSet<MediaSegmentType> GetStoredSegmentTypes(long itemInternalId)
            => new HashSet<MediaSegmentType>();

        public IReadOnlyList<StoredMediaSegment> GetSegments(long itemInternalId)
            => Array.Empty<StoredMediaSegment>();

        public IReadOnlyList<long> GetAllSegmentedItemIds()
            => Array.Empty<long>();

        public void ReplaceSegments(long itemInternalId, IReadOnlyList<StoredMediaSegment> segments, DateTime updatedUtc)
            => throw new InvalidOperationException("Preview repository is read-only");
    }
}

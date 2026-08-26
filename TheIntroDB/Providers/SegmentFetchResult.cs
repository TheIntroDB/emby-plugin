using System.Collections.Generic;
using TheIntroDB.Models;

namespace TheIntroDB.Providers
{
    public class SegmentFetchResult
    {
        public IReadOnlyList<MediaSegmentData> Segments { get; }
        public bool IsRateLimited { get; }
        public bool IsError { get; }
        public bool IsServerError { get; }
        public bool IsClientError { get; }
        public bool WasApiAttempted { get; }
        public bool IsLookupCompleted { get; }

        private SegmentFetchResult(
            IReadOnlyList<MediaSegmentData> segments,
            bool isRateLimited,
            bool isError,
            bool isServerError,
            bool isClientError,
            bool wasApiAttempted,
            bool isLookupCompleted)
        {
            Segments = segments;
            IsRateLimited = isRateLimited;
            IsError = isError;
            IsServerError = isServerError;
            IsClientError = isClientError;
            WasApiAttempted = wasApiAttempted;
            IsLookupCompleted = isLookupCompleted;
        }

        public static SegmentFetchResult Success(IReadOnlyList<MediaSegmentData> segments) =>
            new(segments, false, false, false, false, true, true);

        public static SegmentFetchResult NotFound() =>
            new(System.Array.Empty<MediaSegmentData>(), false, false, false, false, true, true);

        public static SegmentFetchResult NotAttempted() =>
            new(System.Array.Empty<MediaSegmentData>(), false, false, false, false, false, false);

        public static SegmentFetchResult RateLimited() =>
            new(System.Array.Empty<MediaSegmentData>(), true, true, false, false, true, false);

        public static SegmentFetchResult Error() =>
            new(System.Array.Empty<MediaSegmentData>(), false, true, false, false, true, false);

        public static SegmentFetchResult ServerError() =>
            new(System.Array.Empty<MediaSegmentData>(), false, true, true, false, true, true);

        public static SegmentFetchResult ClientError() =>
            new(System.Array.Empty<MediaSegmentData>(), false, true, false, true, true, false);
    }
}

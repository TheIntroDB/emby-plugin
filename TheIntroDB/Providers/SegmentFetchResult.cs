// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 TheIntroDB

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

        /// <summary>
        /// Gets a value indicating whether the API refused the item's season/episode
        /// numbers as out of range (a year-based season number, jellyfin-plugin#39).
        /// Final for the item, and deliberately neither a completed lookup nor a
        /// failure: the scan must leave the item's stored segments alone and must not
        /// advance the consecutive-failure counter.
        /// </summary>
        public bool IsUnsupported { get; }

        private SegmentFetchResult(
            IReadOnlyList<MediaSegmentData> segments,
            bool isRateLimited,
            bool isError,
            bool isServerError,
            bool isClientError,
            bool wasApiAttempted,
            bool isLookupCompleted,
            bool isUnsupported)
        {
            Segments = segments;
            IsRateLimited = isRateLimited;
            IsError = isError;
            IsServerError = isServerError;
            IsClientError = isClientError;
            WasApiAttempted = wasApiAttempted;
            IsLookupCompleted = isLookupCompleted;
            IsUnsupported = isUnsupported;
        }

        public static SegmentFetchResult Success(IReadOnlyList<MediaSegmentData> segments) =>
            new(segments, false, false, false, false, true, true, false);

        public static SegmentFetchResult NotFound() =>
            new(System.Array.Empty<MediaSegmentData>(), false, false, false, false, true, true, false);

        public static SegmentFetchResult NotAttempted() =>
            new(System.Array.Empty<MediaSegmentData>(), false, false, false, false, false, false, false);

        public static SegmentFetchResult RateLimited() =>
            new(System.Array.Empty<MediaSegmentData>(), true, true, false, false, true, false, false);

        public static SegmentFetchResult Error() =>
            new(System.Array.Empty<MediaSegmentData>(), false, true, false, false, true, false, false);

        public static SegmentFetchResult ServerError() =>
            new(System.Array.Empty<MediaSegmentData>(), false, true, true, false, true, true, false);

        public static SegmentFetchResult ClientError() =>
            new(System.Array.Empty<MediaSegmentData>(), false, true, false, true, true, false, false);

        /// <summary>
        /// Creates an unsupported-coordinates result: the API refused the season/episode
        /// numbers as out of range, so nothing can be fetched for this item and nothing
        /// should be retried from the scan's point of view.
        /// </summary>
        public static SegmentFetchResult Unsupported() =>
            new(System.Array.Empty<MediaSegmentData>(), false, false, false, false, true, false, true);
    }
}

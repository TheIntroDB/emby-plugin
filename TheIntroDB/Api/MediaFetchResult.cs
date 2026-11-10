// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 TheIntroDB

using System;

namespace TheIntroDB.Api
{
    public class MediaFetchResult
    {
        public MediaResponse Response { get; }
        public bool IsRateLimited { get; }
        public bool IsNotFound { get; }
        public bool IsServerError { get; }
        public bool IsClientError { get; }

        /// <summary>
        /// Gets a value indicating whether the API refused the item's season/episode
        /// numbers themselves as out of range. Final, not transient: the coordinates
        /// cannot be expressed on this API version, so retrying cannot help.
        /// </summary>
        public bool IsUnsupported { get; }

        public bool IsError => IsRateLimited || IsServerError || IsClientError || (!IsNotFound && !IsUnsupported && Response == null);

        private MediaFetchResult(MediaResponse response, bool isRateLimited, bool isNotFound, bool isServerError, bool isClientError, bool isUnsupported)
        {
            Response = response;
            IsRateLimited = isRateLimited;
            IsNotFound = isNotFound;
            IsServerError = isServerError;
            IsClientError = isClientError;
            IsUnsupported = isUnsupported;
        }

        public static MediaFetchResult Success(MediaResponse response) =>
            new(response, false, false, false, false, false);

        public static MediaFetchResult NotFound() =>
            new(null, false, true, false, false, false);

        public static MediaFetchResult RateLimited() =>
            new(null, true, false, false, false, false);

        public static MediaFetchResult Error() =>
            new(null, false, false, false, false, false);

        public static MediaFetchResult ServerError() =>
            new(null, false, false, true, false, false);

        public static MediaFetchResult ClientError() =>
            new(null, false, false, false, true, false);

        /// <summary>
        /// Creates an unsupported-coordinates result: the API rejected the season/episode
        /// numbers as out of range, so the answer is final for this item.
        /// </summary>
        public static MediaFetchResult Unsupported() =>
            new(null, false, false, false, false, true);
    }
}

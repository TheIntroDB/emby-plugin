using System;

namespace TheIntroDB.Api
{
    public class MediaFetchResult
    {
        public MediaResponse Response { get; }
        public bool IsRateLimited { get; }
        public bool IsNotFound { get; }
        public bool IsServerError { get; }

        public bool IsError => IsRateLimited || IsServerError || (!IsNotFound && Response == null);

        private MediaFetchResult(MediaResponse response, bool isRateLimited, bool isNotFound, bool isServerError)
        {
            Response = response;
            IsRateLimited = isRateLimited;
            IsNotFound = isNotFound;
            IsServerError = isServerError;
        }

        public static MediaFetchResult Success(MediaResponse response) =>
            new(response, false, false, false);

        public static MediaFetchResult NotFound() =>
            new(null, false, true, false);

        public static MediaFetchResult RateLimited() =>
            new(null, true, false, false);

        public static MediaFetchResult Error() =>
            new(null, false, false, false);

        public static MediaFetchResult ServerError() =>
            new(null, false, false, true);
    }
}

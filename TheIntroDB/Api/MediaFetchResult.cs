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

        public bool IsError => IsRateLimited || IsServerError || IsClientError || (!IsNotFound && Response == null);

        private MediaFetchResult(MediaResponse response, bool isRateLimited, bool isNotFound, bool isServerError, bool isClientError)
        {
            Response = response;
            IsRateLimited = isRateLimited;
            IsNotFound = isNotFound;
            IsServerError = isServerError;
            IsClientError = isClientError;
        }

        public static MediaFetchResult Success(MediaResponse response) =>
            new(response, false, false, false, false);

        public static MediaFetchResult NotFound() =>
            new(null, false, true, false, false);

        public static MediaFetchResult RateLimited() =>
            new(null, true, false, false, false);

        public static MediaFetchResult Error() =>
            new(null, false, false, false, false);

        public static MediaFetchResult ServerError() =>
            new(null, false, false, true, false);

        public static MediaFetchResult ClientError() =>
            new(null, false, false, false, true);
    }
}

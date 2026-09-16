using MediaBrowser.Model.Services;
using TheIntroDB.Services;

namespace TheIntroDB.Api
{
    /// <summary>
    /// ServiceStack endpoints for managing the TheIntroDB not-found cache.
    /// </summary>
    public sealed class NotFoundCacheService : IService
    {
        /// <summary>
        /// Clears all cached not-found lookups so the next scan re-checks every item.
        /// </summary>
        [Route("/TheIntroDB/NotFoundCache/Clear", "POST", Summary = "Clear cached not-found lookups")]
        public class Clear : IReturn<ClearResponse>
        {
        }

        /// <summary>
        /// Response for clearing the not-found cache.
        /// </summary>
        public class ClearResponse
        {
            /// <summary>
            /// Gets or sets the number of cache entries that were cleared.
            /// </summary>
            public int ClearedEntries { get; set; }
        }

        /// <summary>
        /// Clears the shared not-found cache.
        /// </summary>
        /// <param name="request">The (empty) request.</param>
        /// <returns>The number of entries cleared.</returns>
        public object Post(Clear request)
        {
            return new ClearResponse { ClearedEntries = TheIntroDbNotFoundCache.Instance.Clear() };
        }
    }
}

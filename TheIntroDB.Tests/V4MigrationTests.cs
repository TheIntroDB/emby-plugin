using System;
using System.Linq;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Entities;
using TheIntroDB.Api;
using TheIntroDB.Providers;
using Xunit;

namespace TheIntroDB.Tests
{
    /// <summary>
    /// Guards the v4 contract: the client must not silently drift back to a frozen
    /// version, the signed-in rate tier must stay under its ceiling, and a series'
    /// display order must reach the API only when it is a real non-default order.
    /// </summary>
    public class V4MigrationTests
    {
        private const int ApiMediaAuthenticatedBurstCeiling = 120;

        [Fact]
        public void ClientSpeaksV4()
        {
            Assert.Equal("v4", TheIntroDbClient.ApiVersion);
            Assert.Equal("https://api.theintrodb.org/v4", TheIntroDbClient.BaseUrl);
        }

        [Fact]
        public void AuthenticatedPacingStaysUnderTheSignedInCeiling()
        {
            // Signed-in callers get 120/10s. 100/window keeps a margin for jitter.
            Assert.True(
                TheIntroDbClient.MinDelayBetweenRequestsAuthenticated.TotalMilliseconds * (ApiMediaAuthenticatedBurstCeiling - 1) >= 10_000,
                $"Authenticated pacing of {TheIntroDbClient.MinDelayBetweenRequestsAuthenticated.TotalMilliseconds} ms exceeds the ceiling.");
            Assert.True(TheIntroDbClient.MinDelayBetweenRequestsAuthenticated < TheIntroDbClient.MinDelayBetweenRequests);
        }

        [Theory]
        [InlineData(SeriesDisplayOrder.Aired, null)]
        [InlineData(SeriesDisplayOrder.Dvd, "dvd")]
        [InlineData(SeriesDisplayOrder.Absolute, "absolute")]
        public void DisplayOrderIsSentOnlyWhenItIsNotTheDefaultOrder(SeriesDisplayOrder displayOrder, string? expected)
        {
            var series = new Series { DisplayOrder = displayOrder };
            Assert.Equal(expected, TheIntroDbSegmentProvider.GetDisplayOrder(series));
        }

        [Fact]
        public void DisplayOrderIsNullWithoutASeries()
        {
            Assert.Null(TheIntroDbSegmentProvider.GetDisplayOrder(null!));
        }

        [Fact]
        public void TvdbAddressedEpisodeCarriesItsOrderAndEpisodeId()
        {
            // A DVD-ordered series: the numbers Emby has are DVD numbers, and the API
            // stores TVDB's default order, so the order has to travel with the request.
            var query = TheIntroDbClient.BuildMediaQuery(
                tmdbId: 456,
                tvdbId: 71663,
                imdbId: "tt0096697",
                isMovie: false,
                season: 1,
                episode: 1,
                durationMs: 1400000,
                mapping: "dvd",
                tvdbEpisodeId: 55452);

            Assert.Equal("?tvdb_id=71663&season=1&episode=1&mapping=dvd&tvdb_episode_id=55452&duration_ms=1400000", query);
        }

        [Fact]
        public void DefaultOrderedEpisodeSendsNoOrderAndNoTmdbId()
        {
            var query = TheIntroDbClient.BuildMediaQuery(456, 71663, null, false, 1, 1, null, null, null);
            Assert.Equal("?tvdb_id=71663&season=1&episode=1", query);
        }

        [Fact]
        public void OrderAndEpisodeIdAreDroppedWithoutATvdbIdentity()
        {
            // A tmdb_id request is always read as TMDB numbering, so an order beside it
            // would be a lie rather than a hint (the provider skips those series).
            var query = TheIntroDbClient.BuildMediaQuery(615, 0, null, false, 7, 1, null, "dvd", 1051911);
            Assert.Equal("?tmdb_id=615&season=7&episode=1", query);
        }

        [Fact]
        public void TmdbIsOnlyUsedWhenNoTvdbIdExistsAndImdbIsLast()
        {
            Assert.Equal("?tmdb_id=615", TheIntroDbClient.BuildMediaQuery(615, 0, "tt0149460", true, null, null, null, null, null));
            Assert.Equal("?imdb_id=tt0149460", TheIntroDbClient.BuildMediaQuery(0, 0, "tt0149460", true, null, null, null, null, null));
        }

        [Fact]
        public void NotFoundCacheKeyCarriesTheOrderAndThePinnedEpisodeId()
        {
            // Both change the answer, so neither may be dropped: a 404 recorded for one
            // address must not suppress a later lookup of another, in particular after a
            // series' metadata is corrected.
            var dvd = TheIntroDbSegmentProvider.BuildNotFoundKey(false, 456, 71663, null, 1, 1, "dvd", 55452);
            var aired = TheIntroDbSegmentProvider.BuildNotFoundKey(false, 456, 71663, null, 1, 1, null, 55452);
            var otherPin = TheIntroDbSegmentProvider.BuildNotFoundKey(false, 456, 71663, null, 1, 1, "dvd", 55453);
            var noPin = TheIntroDbSegmentProvider.BuildNotFoundKey(false, 456, 71663, null, 1, 1, "dvd", null);

            Assert.Equal("episode:tvdb:71663:1:1:dvd:ep55452", dvd);
            Assert.Equal(4, new[] { dvd, aired, otherPin, noPin }.Distinct().Count());
            Assert.Equal("movie:tvdb:169", TheIntroDbSegmentProvider.BuildNotFoundKey(true, 0, 169, null, null, null, null, null));
        }
    }
}

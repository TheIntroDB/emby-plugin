using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Moq;
using TheIntroDB.EntryPoints;
using Xunit;

namespace TheIntroDB.Tests
{
    /// <summary>
    /// The on-demand fetch gates, ported from the Jellyfin plugin: an episode added before its
    /// metadata download has no TVDB/TMDB id to number it by yet, so the lookup waits for the
    /// download instead of answering against an id the API reads differently.
    /// </summary>
    public class OnDemandFetchGateTests
    {
        private static Series SeriesWith(string provider, string id)
        {
            var series = new Series();
            series.ProviderIds[provider] = id;
            return series;
        }

        [Fact]
        public void AddedMovieIsFetched()
        {
            Assert.True(TheIntroDbChapterMarkerPersistenceEntryPoint.ShouldFetchOnItemAdded(new Movie(), null));
        }

        [Theory]
        [InlineData("Tvdb")]
        [InlineData("Tmdb")]
        public void AddedEpisodeOfSeriesWithStableIdIsFetched(string provider)
        {
            Assert.True(TheIntroDbChapterMarkerPersistenceEntryPoint.ShouldFetchOnItemAdded(
                new Episode(),
                SeriesWith(provider, "123")));
        }

        [Fact]
        public void AddedEpisodeOfSeriesWithImdbOnlyWaitsForMetadata()
        {
            Assert.False(TheIntroDbChapterMarkerPersistenceEntryPoint.ShouldFetchOnItemAdded(
                new Episode(),
                SeriesWith("Imdb", "tt0397306")));
        }

        [Fact]
        public void AddedEpisodeOfUnknownSeriesWaitsForMetadata()
        {
            Assert.False(TheIntroDbChapterMarkerPersistenceEntryPoint.ShouldFetchOnItemAdded(new Episode(), null));
        }

        [Theory]
        [InlineData("Tvdb", "")]
        [InlineData("Tvdb", "   ")]
        public void AddedEpisodeOfSeriesWithBlankIdWaitsForMetadata(string provider, string id)
        {
            Assert.False(TheIntroDbChapterMarkerPersistenceEntryPoint.ShouldFetchOnItemAdded(
                new Episode(),
                SeriesWith(provider, id)));
        }

        [Fact]
        public void AddedSeriesIsNotFetched()
        {
            Assert.False(TheIntroDbChapterMarkerPersistenceEntryPoint.ShouldFetchOnItemAdded(SeriesWith("Tvdb", "123"), null));
        }

        [Theory]
        [InlineData(ItemUpdateType.MetadataDownload, true)]
        [InlineData(ItemUpdateType.MetadataDownload | ItemUpdateType.ImageUpdate, true)]
        [InlineData(ItemUpdateType.MetadataImport, false)]
        [InlineData(ItemUpdateType.MetadataEdit, false)]
        [InlineData(ItemUpdateType.ImageUpdate, false)]
        [InlineData(ItemUpdateType.None, false)]
        public void UpdatedItemIsFetchedOnlyAfterMetadataDownload(ItemUpdateType reason, bool expected)
        {
            var e = new ItemChangeEventArgs(Mock.Of<ILibraryManager>()) { Item = new Episode(), UpdateReason = reason };

            Assert.Equal(expected, TheIntroDbChapterMarkerPersistenceEntryPoint.ShouldFetchOnItemUpdated(e));
        }

        [Fact]
        public void UpdatedMovieIsFetchedAfterMetadataDownload()
        {
            var e = new ItemChangeEventArgs(Mock.Of<ILibraryManager>()) { Item = new Movie(), UpdateReason = ItemUpdateType.MetadataDownload };

            Assert.True(TheIntroDbChapterMarkerPersistenceEntryPoint.ShouldFetchOnItemUpdated(e));
        }

        [Fact]
        public void UpdatedSeriesIsNotFetched()
        {
            var e = new ItemChangeEventArgs(Mock.Of<ILibraryManager>()) { Item = new Series(), UpdateReason = ItemUpdateType.MetadataDownload };

            Assert.False(TheIntroDbChapterMarkerPersistenceEntryPoint.ShouldFetchOnItemUpdated(e));
        }

        [Fact]
        public void MissingItemIsNotFetched()
        {
            Assert.False(TheIntroDbChapterMarkerPersistenceEntryPoint.ShouldFetchOnItemUpdated(null));
        }
    }
}

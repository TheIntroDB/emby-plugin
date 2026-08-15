using System.Collections.Generic;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using Moq;
using TheIntroDB.Configuration;
using TheIntroDB.Models;
using TheIntroDB.Services;
using Xunit;

namespace TheIntroDB.Tests
{
    public class ScanSafetyTests
    {
        [Fact]
        public void LookupBudgetEnforcesExactLimit()
        {
            var budget = new ScanLookupBudget(2);

            Assert.True(budget.TryBeginLookup());
            Assert.True(budget.TryBeginLookup());
            Assert.False(budget.TryBeginLookup());
            Assert.Equal(2, budget.Used);
        }

        [Fact]
        public void LookupBudgetStopsImmediatelyAfterRateLimit()
        {
            var budget = new ScanLookupBudget(10);

            Assert.True(budget.TryBeginLookup());
            budget.StopAfterRateLimit();

            Assert.True(budget.IsRateLimited);
            Assert.False(budget.TryBeginLookup());
            Assert.Equal(1, budget.Used);
        }

        [Fact]
        public void PreviewComputesChangesWithoutSavingChapters()
        {
            var repository = new Mock<IItemRepository>();
            repository.Setup(r => r.GetChapters(It.IsAny<MediaBrowser.Controller.Entities.BaseItem>()))
                .Returns(new List<ChapterInfo>());
            var logger = new Mock<ILogger>();
            var writer = new TheIntroDbChapterMarkerWriter(repository.Object, logger.Object);
            var episode = new Episode
            {
                InternalId = 42L,
                Name = "Pilot",
                RunTimeTicks = 1_800_000_000L
            };
            var segments = new List<StoredMediaSegment>
            {
                new StoredMediaSegment
                {
                    ItemInternalId = 42L,
                    Type = MediaSegmentType.Intro,
                    StartTicks = 100_000_000L,
                    EndTicks = 200_000_000L
                }
            };

            var added = writer.ApplyMarkers(episode, segments, new PluginConfiguration(), true);

            Assert.Equal(4, added);
            repository.Verify(r => r.SaveChapters(It.IsAny<long>(), It.IsAny<List<ChapterInfo>>()), Times.Never);
        }
    }
}

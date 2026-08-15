using System;
using System.Collections.Generic;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using Moq;
using TheIntroDB.Configuration;
using TheIntroDB.Data;
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

        [Fact]
        public void TaggedTimestampCollisionNeverRemovesOrChangesExistingIntroMarker()
        {
            var existing = new List<ChapterInfo>
            {
                new ChapterInfo { MarkerType = MarkerType.IntroStart, StartPositionTicks = 100_000_000L, Name = "Intro" },
                new ChapterInfo { MarkerType = MarkerType.Chapter, StartPositionTicks = 100_000_000L, Name = "Intro (TheIntroDB)" }
            };
            var repository = new Mock<IItemRepository>();
            repository.Setup(r => r.GetChapters(It.IsAny<MediaBrowser.Controller.Entities.BaseItem>()))
                .Returns(existing);
            var writer = new TheIntroDbChapterMarkerWriter(repository.Object, Mock.Of<ILogger>());
            var episode = new Episode
            {
                InternalId = 42L,
                Name = "Collision",
                RunTimeTicks = 1_800_000_000L
            };
            var segments = new List<StoredMediaSegment>
            {
                new StoredMediaSegment
                {
                    ItemInternalId = 42L,
                    Type = MediaSegmentType.Intro,
                    StartTicks = 300_000_000L,
                    EndTicks = 400_000_000L
                }
            };

            var added = writer.ApplyMarkers(episode, segments, new PluginConfiguration(), false);

            Assert.Equal(0, added);
            Assert.Equal(2, existing.Count);
            Assert.Contains(existing, c => c.MarkerType == MarkerType.IntroStart && c.StartPositionTicks == 100_000_000L);
            repository.Verify(r => r.SaveChapters(It.IsAny<long>(), It.IsAny<List<ChapterInfo>>()), Times.Never);
        }

        [Fact]
        public void PreviewSegmentRepositoryNeverPersistsData()
        {
            var repository = new PreviewSegmentRepository();

            Assert.Empty(repository.GetAllSegmentedItemIds());
            Assert.Empty(repository.GetSegments(42L));
            Assert.Throws<InvalidOperationException>(() =>
                repository.ReplaceSegments(42L, new List<StoredMediaSegment>(), DateTime.UtcNow));
        }
    }
}

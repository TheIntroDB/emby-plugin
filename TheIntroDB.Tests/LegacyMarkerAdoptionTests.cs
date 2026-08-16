using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using Moq;
using TheIntroDB.Configuration;
using TheIntroDB.Data;
using TheIntroDB.EntryPoints;
using TheIntroDB.Models;
using TheIntroDB.Services;
using Xunit;

namespace TheIntroDB.Tests
{
    public class LegacyMarkerAdoptionTests
    {
        private static ChapterInfo Chapter(MarkerType markerType, long ticks, string name)
        {
            return new ChapterInfo { MarkerType = markerType, StartPositionTicks = ticks, Name = name };
        }

        private static void AssertOwnedPair(ChapterInfo chapter, OwnedChapterMarker row)
        {
            Assert.Equal(chapter.MarkerType, row.MarkerType);
            Assert.Equal(chapter.StartPositionTicks, row.StartTicks);
            Assert.Equal(chapter.Name, row.Name);
            Assert.True(ChapterMarkerPolicy.HasOwnershipToken(chapter.Name, row.OwnerToken));
        }

        [Fact]
        public void AdoptItemRenamesLegacyPairAndWritesLedgerRows()
        {
            var existing = new List<ChapterInfo>
            {
                Chapter(MarkerType.Chapter, 100L, "Intro (TheIntroDB)"),
                Chapter(MarkerType.IntroStart, 100L, "Intro"),
                Chapter(MarkerType.Chapter, 500L, "Recap (TheIntroDB)"),
                Chapter(MarkerType.Chapter, 50L, "Manual chapter")
            };
            var itemRepository = new Mock<IItemRepository>();
            itemRepository.Setup(r => r.GetChapters(It.IsAny<BaseItem>())).Returns(existing);
            var saved = new List<ChapterInfo>();
            itemRepository.Setup(r => r.SaveChapters(42L, It.IsAny<List<ChapterInfo>>()))
                .Callback<long, List<ChapterInfo>>((_, chapters) => saved = chapters);

            var segmentRepository = new Mock<ITheIntroDbSegmentRepository>();
            segmentRepository.Setup(r => r.GetOwnedChapters(42L)).Returns(Array.Empty<OwnedChapterMarker>());
            var writtenLedger = new List<OwnedChapterMarker>();
            segmentRepository.Setup(r => r.ReplaceOwnedChapters(42L, It.IsAny<IReadOnlyList<OwnedChapterMarker>>(), It.IsAny<DateTime>()))
                .Callback<long, IReadOnlyList<OwnedChapterMarker>, DateTime>((_, rows, _) => writtenLedger.AddRange(rows));

            var adoption = new TheIntroDbLegacyMarkerAdoption(itemRepository.Object, segmentRepository.Object, Mock.Of<ILogger>());

            var adopted = adoption.AdoptItem(new Episode { InternalId = 42L, Name = "Pilot" });

            Assert.Equal(3, adopted);
            Assert.Equal(4, saved.Count);
            Assert.Contains(saved, c => c.MarkerType == MarkerType.Chapter && c.StartPositionTicks == 50L && c.Name == "Manual chapter");

            var renamed = saved.Where(c => !(c.StartPositionTicks == 50L && c.Name == "Manual chapter")).ToList();
            Assert.Equal(3, renamed.Count);
            foreach (var chapter in renamed)
            {
                Assert.Contains(" [TheIntroDB:", chapter.Name);
                Assert.EndsWith("]", chapter.Name);
                Assert.True(ChapterMarkerPolicy.HasOwnershipToken(chapter.Name, writtenLedger.Single(row => row.Name == chapter.Name).OwnerToken));
            }

            Assert.Equal(3, writtenLedger.Count);
            Assert.Equal(3, writtenLedger.Select(row => row.OwnerToken).Distinct().Count());
            foreach (var row in writtenLedger)
            {
                var chapter = saved.Single(c => c.Name == row.Name);
                AssertOwnedPair(chapter, row);
            }

            itemRepository.Verify(r => r.SaveChapters(42L, It.IsAny<List<ChapterInfo>>()), Times.Once);
            segmentRepository.Verify(r => r.ReplaceOwnedChapters(42L, It.IsAny<IReadOnlyList<OwnedChapterMarker>>(), It.IsAny<DateTime>()), Times.Once);
        }

        [Fact]
        public void AdoptItemSkipsBareMarkersWithoutTaggedCompanion()
        {
            var existing = new List<ChapterInfo>
            {
                Chapter(MarkerType.IntroStart, 100L, "Intro"),
                Chapter(MarkerType.CreditsStart, 900L, "Credits"),
                Chapter(MarkerType.Chapter, 50L, "Recap (OtherPlugin)"),
                Chapter(MarkerType.Chapter, 200L, "Intro (TheIntroDB) [TheIntroDB:abcdefabcdefabcdefabcdefabcdef]")
            };
            var itemRepository = new Mock<IItemRepository>();
            itemRepository.Setup(r => r.GetChapters(It.IsAny<BaseItem>())).Returns(existing);
            var segmentRepository = new Mock<ITheIntroDbSegmentRepository>();
            segmentRepository.Setup(r => r.GetOwnedChapters(42L)).Returns(Array.Empty<OwnedChapterMarker>());

            var adoption = new TheIntroDbLegacyMarkerAdoption(itemRepository.Object, segmentRepository.Object, Mock.Of<ILogger>());

            var adopted = adoption.AdoptItem(new Episode { InternalId = 42L, Name = "Pilot" });

            Assert.Equal(0, adopted);
            itemRepository.Verify(r => r.SaveChapters(It.IsAny<long>(), It.IsAny<List<ChapterInfo>>()), Times.Never);
            segmentRepository.Verify(r => r.ReplaceOwnedChapters(It.IsAny<long>(), It.IsAny<IReadOnlyList<OwnedChapterMarker>>(), It.IsAny<DateTime>()), Times.Never);
        }

        [Fact]
        public void AdoptItemPreservesForeignChaptersAndPairsLogicalMarkers()
        {
            var existing = new List<ChapterInfo>
            {
                Chapter(MarkerType.Chapter, 50L, "Chapter 1"),
                Chapter(MarkerType.Chapter, 100L, "Recap (OtherPlugin)"),
                Chapter(MarkerType.CreditsStart, 900L, "Credits"),
                Chapter(MarkerType.Chapter, 900L, "Credits (TheIntroDB)"),
                Chapter(MarkerType.Chapter, 1000L, "Intro (TheIntroDB)")
            };
            var itemRepository = new Mock<IItemRepository>();
            itemRepository.Setup(r => r.GetChapters(It.IsAny<BaseItem>())).Returns(existing);
            var saved = new List<ChapterInfo>();
            itemRepository.Setup(r => r.SaveChapters(42L, It.IsAny<List<ChapterInfo>>()))
                .Callback<long, List<ChapterInfo>>((_, chapters) => saved = chapters);
            var segmentRepository = new Mock<ITheIntroDbSegmentRepository>();
            segmentRepository.Setup(r => r.GetOwnedChapters(42L)).Returns(Array.Empty<OwnedChapterMarker>());

            var adoption = new TheIntroDbLegacyMarkerAdoption(itemRepository.Object, segmentRepository.Object, Mock.Of<ILogger>());

            var adopted = adoption.AdoptItem(new Episode { InternalId = 42L, Name = "Pilot" });

            Assert.Equal(3, adopted);
            Assert.Equal(5, saved.Count);
            Assert.Contains(saved, c => c.MarkerType == MarkerType.Chapter && c.StartPositionTicks == 50L && c.Name == "Chapter 1");
            Assert.Contains(saved, c => c.MarkerType == MarkerType.Chapter && c.StartPositionTicks == 100L && c.Name == "Recap (OtherPlugin)");
            var creditsStart = saved.Single(c => c.MarkerType == MarkerType.CreditsStart && c.StartPositionTicks == 900L);
            Assert.Contains(" [TheIntroDB:", creditsStart.Name);
            var creditsCompanion = saved.Single(c => c.MarkerType == MarkerType.Chapter && c.StartPositionTicks == 900L);
            Assert.Contains(" [TheIntroDB:", creditsCompanion.Name);
            var introCompanion = saved.Single(c => c.MarkerType == MarkerType.Chapter && c.StartPositionTicks == 1000L);
            Assert.Contains(" [TheIntroDB:", introCompanion.Name);
        }

        [Fact]
        public void AdoptItemIsIdempotent()
        {
            var original = new List<ChapterInfo>
            {
                Chapter(MarkerType.Chapter, 100L, "Intro (TheIntroDB)"),
                Chapter(MarkerType.IntroStart, 100L, "Intro")
            };
            var current = original;
            var itemRepository = new Mock<IItemRepository>();
            itemRepository.Setup(r => r.GetChapters(It.IsAny<BaseItem>())).Returns(() => current);
            var saveCount = 0;
            itemRepository.Setup(r => r.SaveChapters(42L, It.IsAny<List<ChapterInfo>>()))
                .Callback<long, List<ChapterInfo>>((_, chapters) =>
                {
                    saveCount++;
                    current = chapters;
                });
            var segmentRepository = new Mock<ITheIntroDbSegmentRepository>();
            segmentRepository.Setup(r => r.GetOwnedChapters(42L)).Returns(Array.Empty<OwnedChapterMarker>());

            var adoption = new TheIntroDbLegacyMarkerAdoption(itemRepository.Object, segmentRepository.Object, Mock.Of<ILogger>());

            Assert.Equal(2, adoption.AdoptItem(new Episode { InternalId = 42L, Name = "Pilot" }));
            Assert.Equal(0, adoption.AdoptItem(new Episode { InternalId = 42L, Name = "Pilot" }));
            Assert.Equal(1, saveCount);
        }

        [Fact]
        public void AdoptItemMergesExistingOwnedRows()
        {
            const string existingToken = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";
            var existingOwned = new OwnedChapterMarker
            {
                ItemInternalId = 42L,
                MarkerType = MarkerType.IntroStart,
                StartTicks = 100L,
                Name = ChapterMarkerPolicy.AddOwnershipToken("Intro", existingToken),
                OwnerToken = existingToken
            };
            var existing = new List<ChapterInfo>
            {
                Chapter(MarkerType.IntroStart, 100L, existingOwned.Name),
                Chapter(MarkerType.Chapter, 100L, "Intro (TheIntroDB)")
            };
            var itemRepository = new Mock<IItemRepository>();
            itemRepository.Setup(r => r.GetChapters(It.IsAny<BaseItem>())).Returns(existing);
            var segmentRepository = new Mock<ITheIntroDbSegmentRepository>();
            segmentRepository.Setup(r => r.GetOwnedChapters(42L)).Returns(new[] { existingOwned });
            var writtenLedger = new List<OwnedChapterMarker>();
            segmentRepository.Setup(r => r.ReplaceOwnedChapters(42L, It.IsAny<IReadOnlyList<OwnedChapterMarker>>(), It.IsAny<DateTime>()))
                .Callback<long, IReadOnlyList<OwnedChapterMarker>, DateTime>((_, rows, _) => writtenLedger.AddRange(rows));

            var adoption = new TheIntroDbLegacyMarkerAdoption(itemRepository.Object, segmentRepository.Object, Mock.Of<ILogger>());

            var adopted = adoption.AdoptItem(new Episode { InternalId = 42L, Name = "Pilot" });

            Assert.Equal(1, adopted);
            Assert.Equal(2, writtenLedger.Count);
            Assert.Contains(writtenLedger, row => row.OwnerToken == existingToken);
            Assert.Contains(writtenLedger, row => row.OwnerToken != existingToken && row.MarkerType == MarkerType.Chapter);
        }

        [Fact]
        public void NeedsMarkerApplyAcceptsLegacyTaggedRecapAndPreview()
        {
            var recapConfig = new PluginConfiguration
            {
                EnableIntro = false,
                EnableRecap = true,
                EnableCredits = false,
                EnablePreview = false
            };
            var previewConfig = new PluginConfiguration
            {
                EnableIntro = false,
                EnableRecap = false,
                EnableCredits = false,
                EnablePreview = true
            };
            var method = typeof(TheIntroDbChapterMarkerPersistenceEntryPoint).GetMethod(
                "NeedsMarkerApply",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(method);

            bool? Needs(IReadOnlyList<ChapterInfo> chapters, PluginConfiguration config) => (bool?)method.Invoke(null,
                new object[] { chapters, Array.Empty<OwnedChapterMarker>(), config });

            Assert.False(Needs(new[] { Chapter(MarkerType.Chapter, 42L, "Recap (TheIntroDB)") }, recapConfig).GetValueOrDefault(true));
            Assert.False(Needs(new[] { Chapter(MarkerType.Chapter, 42L, "Recap End (TheIntroDB)") }, recapConfig).GetValueOrDefault(true));
            Assert.False(Needs(new[] { Chapter(MarkerType.Chapter, 42L, "Preview (TheIntroDB)") }, previewConfig).GetValueOrDefault(true));
            Assert.False(Needs(new[] { Chapter(MarkerType.Chapter, 42L, "Preview End (TheIntroDB)") }, previewConfig).GetValueOrDefault(true));
            Assert.True(Needs(new[] { Chapter(MarkerType.Chapter, 42L, "Recap") }, recapConfig).GetValueOrDefault(false));
            Assert.True(Needs(new[] { Chapter(MarkerType.Chapter, 42L, "Preview") }, previewConfig).GetValueOrDefault(false));
        }
    }
}

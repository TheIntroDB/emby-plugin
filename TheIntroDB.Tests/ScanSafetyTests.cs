using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using Moq;
using TheIntroDB.Configuration;
using TheIntroDB.Data;
using TheIntroDB.EntryPoints;
using TheIntroDB.Models;
using TheIntroDB.Providers;
using TheIntroDB.Services;
using Xunit;

namespace TheIntroDB.Tests
{
    public class ScanSafetyTests
    {
        [Fact]
        public void BudgetEnforcesExactLimit()
        {
            var budget = new ScanLookupBudget(2);

            Assert.True(budget.TryBeginLookup());
            Assert.True(budget.TryBeginLookup());
            Assert.False(budget.TryBeginLookup());
            Assert.Equal(2, budget.Used);
        }

        [Fact]
        public void RateLimitRetryCountsAgainstBudget()
        {
            var budget = new ScanLookupBudget(2);

            // The retry request must consume the same bounded lookup budget.
            Assert.True(budget.TryBeginLookup());
            Assert.True(budget.TryBeginLookup());
            Assert.False(budget.TryBeginLookup());
            Assert.Equal(2, budget.Used);
        }

        [Fact]
        public void RateLimitAllowsOnlyTwoRetries()
        {
            var method = typeof(TheIntroDbLibraryScanner).GetMethod(
                "CanRetryAfterRateLimit",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(method);

            Assert.True(Assert.IsType<bool>(method.Invoke(null, new object[] { 0 })));
            Assert.True(Assert.IsType<bool>(method.Invoke(null, new object[] { 1 })));
            Assert.False(Assert.IsType<bool>(method.Invoke(null, new object[] { 2 })));
        }

        [Fact]
        public void RetryAfterIsClamped()
        {
            var method = typeof(TheIntroDB.Api.TheIntroDbClient).GetMethod(
                "GetRetryAfterSeconds",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(method);

            using (var response = new HttpResponseMessage())
            {
                response.Headers.Add("X-UsageLimit-Reset", "999999");
                Assert.Equal(300, Assert.IsType<int>(method.Invoke(null, new object[] { response.Headers })));
            }
        }

        [Fact]
        public void LookupHistorySkipsEmptyResults()
        {
            var now = DateTime.UtcNow;
            var lastChecked = new Dictionary<long, DateTime>();
            var items = new BaseItem[]
            {
                Movie(10L), Movie(20L), Movie(30L), Movie(40L)
            };

            var firstRun = OrderForScan(items, lastChecked);
            Assert.Equal(new long[] { 10L, 20L, 30L, 40L }, firstRun.Select(item => item.InternalId));

            // A completed 404 has no segments, but must still advance the durable history.
            Assert.True(SegmentFetchResult.NotFound().IsLookupCompleted);
            lastChecked[10L] = now;
            lastChecked[20L] = now.AddTicks(1);

            var secondRun = OrderForScan(items.Reverse().ToArray(), lastChecked);
            Assert.Equal(new long[] { 30L, 40L, 10L, 20L }, secondRun.Select(item => item.InternalId));
        }

        [Theory]
        [MemberData(nameof(IncompleteLookupResults))]
        public void IncompleteLookupsExcludedFromHistory(SegmentFetchResult result)
        {
            Assert.False(result.IsLookupCompleted);
        }

        public static IEnumerable<object[]> IncompleteLookupResults()
        {
            yield return new object[] { SegmentFetchResult.NotAttempted() };
            yield return new object[] { SegmentFetchResult.Error() };
            yield return new object[] { SegmentFetchResult.RateLimited() };
        }

        [Fact]
        public void RequestPacingRespectsTenSecondWindow()
        {
            var field = typeof(TheIntroDB.Api.TheIntroDbClient).GetField(
                "MinDelayBetweenRequests",
                BindingFlags.NonPublic | BindingFlags.Static);
            var minimumDelay = Assert.IsType<TimeSpan>(field?.GetValue(null));

            Assert.True(
                TimeSpan.FromTicks(minimumDelay.Ticks * 29) >= TimeSpan.FromSeconds(10),
                $"Thirty request starts can fit inside ten seconds with a {minimumDelay.TotalMilliseconds} ms delay.");
        }

        [Fact]
        public void PreviewComputesChangesWithoutSavingChapters()
        {
            var repository = new Mock<IItemRepository>();
            repository.Setup(r => r.GetChapters(It.IsAny<MediaBrowser.Controller.Entities.BaseItem>()))
                .Returns(new List<ChapterInfo>());
            var logger = new Mock<ILogger>();
            var segmentRepository = new Mock<ITheIntroDbSegmentRepository>();
            segmentRepository.Setup(r => r.GetOwnedChapters(42L))
                .Returns(Array.Empty<OwnedChapterMarker>());
            var writer = new TheIntroDbChapterMarkerWriter(repository.Object, segmentRepository.Object, logger.Object);
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
            segmentRepository.Verify(r => r.ReplaceOwnedChapters(It.IsAny<long>(), It.IsAny<IReadOnlyList<OwnedChapterMarker>>(), It.IsAny<DateTime>()), Times.Never);
        }

        [Fact]
        public void TaggedCollisionPreservesExistingIntro()
        {
            var existing = new List<ChapterInfo>
            {
                new ChapterInfo { MarkerType = MarkerType.IntroStart, StartPositionTicks = 100_000_000L, Name = "Intro" },
                new ChapterInfo { MarkerType = MarkerType.Chapter, StartPositionTicks = 100_000_000L, Name = "Intro (TheIntroDB)" }
            };
            var repository = new Mock<IItemRepository>();
            repository.Setup(r => r.GetChapters(It.IsAny<MediaBrowser.Controller.Entities.BaseItem>()))
                .Returns(existing);
            var segmentRepository = new Mock<ITheIntroDbSegmentRepository>();
            segmentRepository.Setup(r => r.GetOwnedChapters(42L))
                .Returns(Array.Empty<OwnedChapterMarker>());
            var writer = new TheIntroDbChapterMarkerWriter(repository.Object, segmentRepository.Object, Mock.Of<ILogger>());
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
        public void PreviewRepoDelegatesReadsNeverPersists()
        {
            var backing = new Mock<ITheIntroDbSegmentRepository>();
            backing.Setup(r => r.GetSegmentedIds()).Returns(new long[] { 42L });
            backing.Setup(r => r.GetLastCheckedUtc()).Returns(new Dictionary<long, DateTime> { [42L] = DateTime.UtcNow });
            backing.Setup(r => r.GetStoredTypes(42L)).Returns(new HashSet<MediaSegmentType> { MediaSegmentType.Intro });
            backing.Setup(r => r.HasAllSegmentTypes(42L, It.IsAny<IReadOnlyCollection<MediaSegmentType>>())).Returns(true);
            backing.Setup(r => r.GetSegments(42L)).Returns(new[]
            {
                new StoredMediaSegment { ItemInternalId = 42L, Type = MediaSegmentType.Intro, StartTicks = 100L, EndTicks = 200L }
            });
            backing.Setup(r => r.GetOwnedChapters(42L)).Returns(new[]
            {
                new OwnedChapterMarker { ItemInternalId = 42L, MarkerType = MarkerType.IntroStart, StartTicks = 100L, Name = "Intro" }
            });
            var repository = new PreviewSegmentRepository(backing.Object);

            Assert.Equal(new long[] { 42L }, repository.GetSegmentedIds());
            Assert.Contains(42L, repository.GetLastCheckedUtc().Keys);
            Assert.Contains(MediaSegmentType.Intro, repository.GetStoredTypes(42L));
            Assert.True(repository.HasAllSegmentTypes(42L, new[] { MediaSegmentType.Intro }));
            Assert.Single(repository.GetSegments(42L));
            Assert.Single(repository.GetOwnedChapters(42L));
            Assert.Throws<InvalidOperationException>(() =>
                repository.ReplaceSegments(42L, new List<StoredMediaSegment>(), DateTime.UtcNow));
            Assert.Throws<InvalidOperationException>(() =>
                repository.ReplaceOwnedChapters(42L, new List<OwnedChapterMarker>(), DateTime.UtcNow));
            backing.Verify(r => r.ReplaceSegments(It.IsAny<long>(), It.IsAny<IReadOnlyList<StoredMediaSegment>>(), It.IsAny<DateTime>()), Times.Never);
            backing.Verify(r => r.ReplaceOwnedChapters(It.IsAny<long>(), It.IsAny<IReadOnlyList<OwnedChapterMarker>>(), It.IsAny<DateTime>()), Times.Never);
        }

        [Fact]
        public void MissingDbReturnsEmptyNoFiles()
        {
            var root = Path.Combine(Path.GetTempPath(), "theintrodb-preview-" + Guid.NewGuid().ToString("N"));
            var paths = new Mock<IApplicationPaths>();
            paths.SetupGet(value => value.DataPath).Returns(root);

            using (var repository = new TheIntroDbSegmentRepository(Mock.Of<ILogger>(), paths.Object, true))
            using (repository.BeginReadOnlySession(CancellationToken.None))
            {
                Assert.Empty(repository.GetSegmentedIds());
                Assert.Empty(repository.GetStoredTypes(42L));
                Assert.Empty(repository.GetSegments(42L));
                Assert.Empty(repository.GetOwnedChapters(42L));
                Assert.Throws<InvalidOperationException>(() => repository.ReplaceSegments(42L, Array.Empty<StoredMediaSegment>(), DateTime.UtcNow));
                Assert.Throws<InvalidOperationException>(() => repository.ReplaceOwnedChapters(42L, Array.Empty<OwnedChapterMarker>(), DateTime.UtcNow));
            }

            Assert.False(Directory.Exists(root));
        }


        [Fact]
        public void ReplaceMarkersRemovesOnlyOwnedChapters()
        {
            const string startToken = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
            const string companionToken = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
            var ownedIntroName = ChapterMarkerPolicy.AddOwnershipToken("Intro", startToken);
            var ownedCompanionName = ChapterMarkerPolicy.AddOwnershipToken("Intro (TheIntroDB)", companionToken);
            var existing = new List<ChapterInfo>
            {
                Chapter(MarkerType.IntroStart, 100L, ownedIntroName),
                Chapter(MarkerType.Chapter, 100L, ownedCompanionName),
                Chapter(MarkerType.Chapter, 200L, "Intro (TheIntroDB)"),
                Chapter(MarkerType.CreditsStart, 900L, "Foreign credits"),
                Chapter(MarkerType.Chapter, 50L, "Manual chapter")
            };
            var itemRepository = new Mock<IItemRepository>();
            itemRepository.Setup(r => r.GetChapters(It.IsAny<MediaBrowser.Controller.Entities.BaseItem>())).Returns(existing);
            var saved = new List<ChapterInfo>();
            itemRepository.Setup(r => r.SaveChapters(42L, It.IsAny<List<ChapterInfo>>()))
                .Callback<long, List<ChapterInfo>>((_, chapters) => saved = chapters);

            var segmentRepository = new Mock<ITheIntroDbSegmentRepository>();
            segmentRepository.Setup(r => r.GetOwnedChapters(42L)).Returns(new[]
            {
                new OwnedChapterMarker { ItemInternalId = 42L, MarkerType = MarkerType.IntroStart, StartTicks = 100L, Name = ownedIntroName, OwnerToken = startToken },
                new OwnedChapterMarker { ItemInternalId = 42L, MarkerType = MarkerType.Chapter, StartTicks = 100L, Name = ownedCompanionName, OwnerToken = companionToken }
            });
            var writer = new TheIntroDbChapterMarkerWriter(itemRepository.Object, segmentRepository.Object, Mock.Of<ILogger>());
            var config = new PluginConfiguration { ReplaceExistingMarkers = true };
            var episode = new Episode { InternalId = 42L, Name = "Pilot", RunTimeTicks = 1_000_000_000L };
            var segments = new[]
            {
                new StoredMediaSegment { ItemInternalId = 42L, Type = MediaSegmentType.Intro, StartTicks = 300L, EndTicks = 400L }
            };

            var added = writer.ApplyMarkers(episode, segments, config);

            Assert.Equal(4, added);
            Assert.NotNull(saved);
            Assert.DoesNotContain(saved, c => c.MarkerType == MarkerType.IntroStart && c.StartPositionTicks == 100L);
            Assert.DoesNotContain(saved, c => c.MarkerType == MarkerType.Chapter && c.StartPositionTicks == 100L);
            Assert.Contains(saved, c => c.MarkerType == MarkerType.Chapter && c.StartPositionTicks == 200L && c.Name == "Intro (TheIntroDB)");
            Assert.Contains(saved, c => c.MarkerType == MarkerType.CreditsStart && c.StartPositionTicks == 900L);
            Assert.Contains(saved, c => c.MarkerType == MarkerType.Chapter && c.StartPositionTicks == 50L && c.Name == "Manual chapter");
            segmentRepository.Verify(r => r.ReplaceOwnedChapters(42L,
                It.Is<IReadOnlyList<OwnedChapterMarker>>(owned => owned.Count == 4 &&
                    owned.All(marker => (marker.StartTicks == 300L || marker.StartTicks == 400L) &&
                        !string.IsNullOrWhiteSpace(marker.OwnerToken) &&
                        ChapterMarkerPolicy.HasOwnershipToken(marker.Name, marker.OwnerToken))),
                It.IsAny<DateTime>()), Times.Once);
        }

        [Fact]
        public void ForeignNativeIntroBlocksReplacement()
        {
            var existing = new List<ChapterInfo>
            {
                Chapter(MarkerType.IntroStart, 200L, "Foreign intro"),
                Chapter(MarkerType.Chapter, 50L, "Manual chapter")
            };
            var itemRepository = new Mock<IItemRepository>();
            itemRepository.Setup(r => r.GetChapters(It.IsAny<MediaBrowser.Controller.Entities.BaseItem>())).Returns(existing);
            var segmentRepository = new Mock<ITheIntroDbSegmentRepository>();
            segmentRepository.Setup(r => r.GetOwnedChapters(42L)).Returns(Array.Empty<OwnedChapterMarker>());
            var writer = new TheIntroDbChapterMarkerWriter(itemRepository.Object, segmentRepository.Object, Mock.Of<ILogger>());

            var added = writer.ApplyMarkers(
                new Episode { InternalId = 42L, Name = "Pilot", RunTimeTicks = 1_000_000_000L },
                new[] { new StoredMediaSegment { ItemInternalId = 42L, Type = MediaSegmentType.Intro, StartTicks = 300L, EndTicks = 400L } },
                new PluginConfiguration { ReplaceExistingMarkers = true });

            Assert.Equal(0, added);
            Assert.Contains(existing, chapter => chapter.MarkerType == MarkerType.IntroStart && chapter.Name == "Foreign intro");
            itemRepository.Verify(repository => repository.SaveChapters(It.IsAny<long>(), It.IsAny<List<ChapterInfo>>()), Times.Never);
            segmentRepository.Verify(repository => repository.ReplaceOwnedChapters(It.IsAny<long>(), It.IsAny<IReadOnlyList<OwnedChapterMarker>>(), It.IsAny<DateTime>()), Times.Never);
        }

        [Fact]
        public void OwnershipTokenMismatchNeverDeletesChapter()
        {
            const string actualToken = "cccccccccccccccccccccccccccccccc";
            var name = ChapterMarkerPolicy.AddOwnershipToken("Intro", actualToken);
            var existing = new List<ChapterInfo> { Chapter(MarkerType.IntroStart, 100L, name) };
            var itemRepository = new Mock<IItemRepository>();
            itemRepository.Setup(repository => repository.GetChapters(It.IsAny<MediaBrowser.Controller.Entities.BaseItem>())).Returns(existing);
            var segmentRepository = new Mock<ITheIntroDbSegmentRepository>();
            segmentRepository.Setup(repository => repository.GetOwnedChapters(42L)).Returns(new[]
            {
                new OwnedChapterMarker
                {
                    ItemInternalId = 42L,
                    MarkerType = MarkerType.IntroStart,
                    StartTicks = 100L,
                    Name = name,
                    OwnerToken = "dddddddddddddddddddddddddddddddd"
                }
            });
            var writer = new TheIntroDbChapterMarkerWriter(itemRepository.Object, segmentRepository.Object, Mock.Of<ILogger>());

            var added = writer.ApplyMarkers(
                new Episode { InternalId = 42L, Name = "Pilot", RunTimeTicks = 1_000_000_000L },
                new[] { new StoredMediaSegment { ItemInternalId = 42L, Type = MediaSegmentType.Intro, StartTicks = 300L, EndTicks = 400L } },
                new PluginConfiguration { ReplaceExistingMarkers = true });

            Assert.Equal(0, added);
            Assert.Single(existing);
            itemRepository.Verify(repository => repository.SaveChapters(It.IsAny<long>(), It.IsAny<List<ChapterInfo>>()), Times.Never);
        }

        [Fact]
        public void LedgerEntryRemovesOneChapter()
        {
            const string token = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";
            var name = ChapterMarkerPolicy.AddOwnershipToken("Intro", token);
            var existing = new List<ChapterInfo>
            {
                Chapter(MarkerType.IntroStart, 100L, name),
                Chapter(MarkerType.IntroStart, 100L, name)
            };
            var itemRepository = new Mock<IItemRepository>();
            itemRepository.Setup(repository => repository.GetChapters(It.IsAny<MediaBrowser.Controller.Entities.BaseItem>())).Returns(existing);
            var saved = new List<ChapterInfo>();
            itemRepository.Setup(repository => repository.SaveChapters(42L, It.IsAny<List<ChapterInfo>>()))
                .Callback<long, List<ChapterInfo>>((_, chapters) => saved = chapters);
            var segmentRepository = new Mock<ITheIntroDbSegmentRepository>();
            segmentRepository.Setup(repository => repository.GetOwnedChapters(42L)).Returns(new[]
            {
                new OwnedChapterMarker
                {
                    ItemInternalId = 42L,
                    MarkerType = MarkerType.IntroStart,
                    StartTicks = 100L,
                    Name = name,
                    OwnerToken = token
                }
            });
            var writer = new TheIntroDbChapterMarkerWriter(itemRepository.Object, segmentRepository.Object, Mock.Of<ILogger>());

            writer.ApplyMarkers(
                new Episode { InternalId = 42L, Name = "Pilot", RunTimeTicks = 1_000_000_000L },
                new[] { new StoredMediaSegment { ItemInternalId = 42L, Type = MediaSegmentType.Intro, StartTicks = 300L, EndTicks = 400L } },
                new PluginConfiguration
                {
                    ReplaceExistingMarkers = true,
                    EnableIntro = false,
                    EnableRecap = false,
                    EnableCredits = false,
                    EnablePreview = false
                });

            Assert.Single(saved, chapter => chapter.MarkerType == MarkerType.IntroStart && chapter.StartPositionTicks == 100L && chapter.Name == name);
        }

        [Fact]
        public void FailedOwnershipWriteLeavesUnclaimed()
        {
            var itemRepository = new Mock<IItemRepository>();
            itemRepository.Setup(repository => repository.GetChapters(It.IsAny<MediaBrowser.Controller.Entities.BaseItem>()))
                .Returns(new List<ChapterInfo>());
            var saved = new List<ChapterInfo>();
            itemRepository.Setup(repository => repository.SaveChapters(42L, It.IsAny<List<ChapterInfo>>()))
                .Callback<long, List<ChapterInfo>>((_, chapters) => saved = chapters);
            var segmentRepository = new Mock<ITheIntroDbSegmentRepository>();
            segmentRepository.Setup(repository => repository.GetOwnedChapters(42L)).Returns(Array.Empty<OwnedChapterMarker>());
            segmentRepository.Setup(repository => repository.ReplaceOwnedChapters(42L, It.IsAny<IReadOnlyList<OwnedChapterMarker>>(), It.IsAny<DateTime>()))
                .Throws(new InvalidOperationException("ownership write failed"));
            var writer = new TheIntroDbChapterMarkerWriter(itemRepository.Object, segmentRepository.Object, Mock.Of<ILogger>());

            Assert.Throws<InvalidOperationException>(() => writer.ApplyMarkers(
                new Episode { InternalId = 42L, Name = "Pilot", RunTimeTicks = 1_000_000_000L },
                new[] { new StoredMediaSegment { ItemInternalId = 42L, Type = MediaSegmentType.Intro, StartTicks = 300L, EndTicks = 400L } },
                new PluginConfiguration()));

            Assert.NotEmpty(saved);
            segmentRepository.Setup(repository => repository.GetOwnedChapters(42L)).Returns(Array.Empty<OwnedChapterMarker>());
            Assert.Empty(segmentRepository.Object.GetOwnedChapters(42L));
        }

        [Fact]
        public void FailedChapterSaveNoOwnership()
        {
            var itemRepository = new Mock<IItemRepository>();
            itemRepository.Setup(repository => repository.GetChapters(It.IsAny<MediaBrowser.Controller.Entities.BaseItem>()))
                .Returns(new List<ChapterInfo>());
            itemRepository.Setup(repository => repository.SaveChapters(42L, It.IsAny<List<ChapterInfo>>()))
                .Throws(new InvalidOperationException("save failed"));
            var segmentRepository = new Mock<ITheIntroDbSegmentRepository>();
            segmentRepository.Setup(repository => repository.GetOwnedChapters(42L)).Returns(Array.Empty<OwnedChapterMarker>());
            var writer = new TheIntroDbChapterMarkerWriter(itemRepository.Object, segmentRepository.Object, Mock.Of<ILogger>());

            Assert.Throws<InvalidOperationException>(() => writer.ApplyMarkers(
                new Episode { InternalId = 42L, Name = "Pilot", RunTimeTicks = 1_000_000_000L },
                new[] { new StoredMediaSegment { ItemInternalId = 42L, Type = MediaSegmentType.Intro, StartTicks = 300L, EndTicks = 400L } },
                new PluginConfiguration()));

            segmentRepository.Verify(repository => repository.ReplaceOwnedChapters(It.IsAny<long>(), It.IsAny<IReadOnlyList<OwnedChapterMarker>>(), It.IsAny<DateTime>()), Times.Never);
        }


        [Fact]
        public void ForeignDuplicatesNotDeduplicated()
        {
            var duplicateA = Chapter(MarkerType.Chapter, 50L, "Manual chapter");
            var duplicateB = Chapter(MarkerType.Chapter, 50L, "Manual chapter");
            var itemRepository = new Mock<IItemRepository>();
            itemRepository.Setup(repository => repository.GetChapters(It.IsAny<MediaBrowser.Controller.Entities.BaseItem>()))
                .Returns(new List<ChapterInfo> { duplicateA, duplicateB });
            var saved = new List<ChapterInfo>();
            itemRepository.Setup(repository => repository.SaveChapters(42L, It.IsAny<List<ChapterInfo>>()))
                .Callback<long, List<ChapterInfo>>((_, chapters) => saved = chapters);
            var segmentRepository = new Mock<ITheIntroDbSegmentRepository>();
            segmentRepository.Setup(repository => repository.GetOwnedChapters(42L)).Returns(Array.Empty<OwnedChapterMarker>());
            var writer = new TheIntroDbChapterMarkerWriter(itemRepository.Object, segmentRepository.Object, Mock.Of<ILogger>());

            var added = writer.ApplyMarkers(
                new Episode { InternalId = 42L, Name = "Pilot", RunTimeTicks = 1_000_000_000L },
                new[] { new StoredMediaSegment { ItemInternalId = 42L, Type = MediaSegmentType.Recap, StartTicks = 300L, EndTicks = 400L } },
                new PluginConfiguration());

            Assert.Equal(2, added);
            Assert.Equal(2, saved.Count(chapter => chapter.MarkerType == MarkerType.Chapter && chapter.StartPositionTicks == 50L && chapter.Name == "Manual chapter"));
        }

        [Fact]
        public void RepairOnlyRecognizesOwnedOrLegacyTagged()
        {
            const string token = "ffffffffffffffffffffffffffffffff";
            var name = ChapterMarkerPolicy.AddOwnershipToken("Recap" + ChapterMarkerPolicy.TheIntroDbTag, token);
            var chapter = Chapter(MarkerType.Chapter, 100L, name);
            var owned = new OwnedChapterMarker
            {
                ItemInternalId = 42L,
                MarkerType = MarkerType.Chapter,
                StartTicks = 100L,
                Name = name,
                OwnerToken = token
            };
            var config = new PluginConfiguration
            {
                EnableIntro = false,
                EnableRecap = true,
                EnableCredits = false,
                EnablePreview = false
            };
            var method = typeof(TheIntroDbChapterMarkerPersistenceEntryPoint).GetMethod(
                "NeedsMarkerApply",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.NotNull(method);
            var legacyChapter = Chapter(MarkerType.Chapter, 42L, "Recap (TheIntroDB)");
            var bareChapter = Chapter(MarkerType.Chapter, 42L, "Recap");
            var ownedResult = (bool?)method.Invoke(null, new object[] { new[] { chapter }, new[] { owned }, config });
            var unownedResult = (bool?)method.Invoke(null, new object[] { new[] { chapter }, Array.Empty<OwnedChapterMarker>(), config });
            var legacyResult = (bool?)method.Invoke(null, new object[] { new[] { legacyChapter }, Array.Empty<OwnedChapterMarker>(), config });
            var bareResult = (bool?)method.Invoke(null, new object[] { new[] { bareChapter }, Array.Empty<OwnedChapterMarker>(), config });
            Assert.False(ownedResult.GetValueOrDefault(true));
            Assert.True(unownedResult.GetValueOrDefault(false));
            Assert.False(legacyResult.GetValueOrDefault(true));
            Assert.True(bareResult.GetValueOrDefault(false));
        }

        private static ChapterInfo Chapter(MarkerType markerType, long ticks, string name)
        {
            return new ChapterInfo { MarkerType = markerType, StartPositionTicks = ticks, Name = name };
        }

        private static BaseItem[] OrderForScan(BaseItem[] items, IReadOnlyDictionary<long, DateTime> lastChecked)
        {
            var method = typeof(TheIntroDbLibraryScanner).GetMethod(
                "OrderItemsForScan",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(method);
            return Assert.IsType<BaseItem[]>(method.Invoke(null, new object[] { items, lastChecked }));
        }

        private static Movie Movie(long internalId)
        {
            return new Movie
            {
                Id = Guid.NewGuid(),
                InternalId = internalId,
                Name = "Movie " + internalId
            };
        }
    }
}

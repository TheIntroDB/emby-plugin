using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using TheIntroDB.Configuration;
using TheIntroDB.Data;
using TheIntroDB.Models;

namespace TheIntroDB.Services
{
    public sealed class TheIntroDbChapterMarkerWriter
    {
        private readonly IItemRepository _itemRepository;
        private readonly ITheIntroDbSegmentRepository _segmentRepository;
        private readonly ILogger _logger;


        public TheIntroDbChapterMarkerWriter(IItemRepository itemRepository,
          ITheIntroDbSegmentRepository segmentRepository, ILogger logger)
        {
            _itemRepository = itemRepository;
            _segmentRepository = segmentRepository;
            _logger = logger;
        }

        public int ApplyMarkers(BaseItem item, IReadOnlyList<StoredMediaSegment>
          segments, PluginConfiguration config, bool preview = false)
        {
            if (item == null || segments == null || segments.Count == 0 ||
              config == null)
            {
                return 0;
            }

            var existing = _itemRepository.GetChapters(item) ??
              new List<ChapterInfo>();
            var chapters = new List<ChapterInfo>(existing.Count + segments.Count * 2);
            chapters.AddRange(existing);

            var storedOwned = (_segmentRepository.GetOwnedChapters(item.InternalId) ??
                Array.Empty<OwnedChapterMarker>()).ToList();
            var owned = storedOwned
                .Where(marker => chapters.Any(chapter => OwnedChapterMatches(marker, chapter)))
                .ToList();
            var removed = 0;
            if (config.ReplaceExistingMarkers && owned.Count > 0)
            {
                foreach (var ownedMarker in owned)
                {
                    var chapterIndex = chapters.FindIndex(chapter => OwnedChapterMatches(ownedMarker, chapter));
                    if (chapterIndex >= 0)
                    {
                        chapters.RemoveAt(chapterIndex);
                        removed++;
                    }
                }

                owned.Clear();
            }

            var protectIntro = (config.ProtectExistingIntroMarkers ||
                config.ReplaceExistingMarkers) &&
                ChapterMarkerPolicy.HasNativeIntroMarker(chapters);
            var protectCredits = (config.ProtectExistingCreditsMarkers ||
                config.ReplaceExistingMarkers) &&
                ChapterMarkerPolicy.HasNativeCreditsMarker(chapters);

            var added = 0;
            var durationTicks = item.RunTimeTicks.HasValue &&
              item.RunTimeTicks.Value > 0 ? item.RunTimeTicks.Value : (long?)null;

            foreach (var s in segments.OrderBy(x => x.StartTicks))
            {
                var startTicks = ClampTicks(s.StartTicks, durationTicks);
                var endTicks = ClampTicks(s.EndTicks, durationTicks);

                // If EndTicks is 0 (unknown/null from API), substitute
                // the media duration so we get an end marker
                if (s.EndTicks == 0 && durationTicks.HasValue &&
                  durationTicks.Value > 0)
                {
                    endTicks = durationTicks.Value;
                }

                if (endTicks < startTicks)
                {
                    endTicks = startTicks;
                }

                var normalized = new StoredMediaSegment
                {
                    ItemInternalId = s.ItemInternalId,
                    Type = s.Type,
                    StartTicks = startTicks,
                    EndTicks = endTicks
                };

                switch (s.Type)
                {
                    case MediaSegmentType.Intro:
                        if (config.EnableIntro && !protectIntro)
                        {
                            added += AddIntroMarkers(chapters, owned, item.InternalId, normalized);
                        }
                        break;
                    case MediaSegmentType.Recap:
                        if (config.EnableRecap)
                        {
                            added += AddChapterRange(chapters, owned, item.InternalId, "Recap",
                              "Recap End", normalized);
                        }
                        break;
                    case MediaSegmentType.Credits:
                        if (config.EnableCredits && !protectCredits)
                        {
                            added += AddCreditsMarkers(chapters, owned, item.InternalId, normalized);
                        }
                        break;
                    case MediaSegmentType.Preview:
                        if (config.EnablePreview)
                        {
                            added += AddChapterRange(chapters, owned, item.InternalId, "Preview",
                              "Preview End", normalized);
                        }
                        break;
                }
            }

            if (removed == 0 && added == 0)
            {
                if (!preview && !OwnedChaptersEqual(storedOwned, owned))
                {
                    _segmentRepository.ReplaceOwnedChapters(item.InternalId,
                        DedupOwnedChapters(owned), DateTime.UtcNow);
                }

                _logger.Debug("TheIntroDB chapters unchanged for {0} ({1})",
                  item.Name, item.InternalId);
                return 0;
            }

            if (preview)
            {
                _logger.Debug("TheIntroDB preview would save {0} chapters/markers for {1} ({2})",
                    chapters.Count, item.Name, item.InternalId);
                return added;
            }

            var updatedChapters = chapters
                .Select((chapter, index) => new { Chapter = chapter, Index = index })
                .OrderBy(value => value.Chapter.StartPositionTicks)
                .ThenBy(value => value.Index)
                .Select(value => value.Chapter)
                .ToList();
            _itemRepository.SaveChapters(item.InternalId, updatedChapters);
            _segmentRepository.ReplaceOwnedChapters(item.InternalId,
                DedupOwnedChapters(owned), DateTime.UtcNow);
            _logger.Debug("TheIntroDB saved {0} chapters/markers for {1} ({2})",
              updatedChapters.Count, item.Name, item.InternalId);

            return added;
        }

        private int AddIntroMarkers(List<ChapterInfo> chapters,
          List<OwnedChapterMarker> owned, long itemInternalId, StoredMediaSegment s)
        {
            var added = 0;

            // Always add start marker, even at 0:00
            added += AddIfMissing(chapters, owned, itemInternalId,
              MarkerType.IntroStart, s.StartTicks, "Intro");
            added += AddIfMissing(chapters, owned, itemInternalId,
              MarkerType.Chapter, s.StartTicks, "Intro" + ChapterMarkerPolicy.TheIntroDbTag);

            // Always add end marker, even if it equals start
            // (e.g. point-like intro at 0:00)
            added += AddIfMissing(chapters, owned, itemInternalId,
              MarkerType.IntroEnd, s.EndTicks, "Intro End");
            added += AddIfMissing(chapters, owned, itemInternalId,
              MarkerType.Chapter, s.EndTicks, "Intro End" + ChapterMarkerPolicy.TheIntroDbTag);

            return added;
        }

        private int AddCreditsMarkers(List<ChapterInfo> chapters,
          List<OwnedChapterMarker> owned, long itemInternalId, StoredMediaSegment s)
        {
            var added = 0;

            // Start marker
            added += AddIfMissing(chapters, owned, itemInternalId,
              MarkerType.CreditsStart, s.StartTicks, "Credits");
            added += AddIfMissing(chapters, owned, itemInternalId,
              MarkerType.Chapter, s.StartTicks, "Credits" + ChapterMarkerPolicy.TheIntroDbTag);

            // End marker at media duration, credits extend to the end
            added += AddIfMissing(chapters, owned, itemInternalId,
              MarkerType.Chapter, s.EndTicks, "Credits End" + ChapterMarkerPolicy.TheIntroDbTag);

            return added;
        }

        private int AddChapterRange(List<ChapterInfo> chapters,
          List<OwnedChapterMarker> owned, long itemInternalId,
          string startName, string endName, StoredMediaSegment s)
        {
            var added = 0;

            // Always add start marker, even at 0:00
            added += AddIfMissing(chapters, owned, itemInternalId,
              MarkerType.Chapter, s.StartTicks, startName + ChapterMarkerPolicy.TheIntroDbTag);

            // Always add end marker, even if it equals start
            added += AddIfMissing(chapters, owned, itemInternalId,
              MarkerType.Chapter, s.EndTicks, endName + ChapterMarkerPolicy.TheIntroDbTag);

            return added;
        }

        private static int AddIfMissing(List<ChapterInfo> chapters,
          List<OwnedChapterMarker> owned, long itemInternalId,
          MarkerType markerType, long startTicks, string name)
        {
            if (chapters.Any(c => c.MarkerType == markerType &&
                c.StartPositionTicks == startTicks))
            {
                return 0;
            }

            var ownerToken = Guid.NewGuid().ToString("N");
            var ownedName = ChapterMarkerPolicy.AddOwnershipToken(name, ownerToken);
            chapters.Add(new ChapterInfo
            {
                Name = ownedName,
                StartPositionTicks = startTicks,
                MarkerType = markerType
            });
            owned.Add(new OwnedChapterMarker
            {
                ItemInternalId = itemInternalId,
                MarkerType = markerType,
                StartTicks = startTicks,
                Name = ownedName,
                OwnerToken = ownerToken
            });

            return 1;
        }

        private static bool OwnedChapterMatches(OwnedChapterMarker owned, ChapterInfo chapter)
        {
            return owned.MarkerType == chapter.MarkerType &&
                owned.StartTicks == chapter.StartPositionTicks &&
                string.Equals(owned.Name ?? string.Empty, chapter.Name ?? string.Empty, StringComparison.Ordinal) &&
                ChapterMarkerPolicy.HasOwnershipToken(chapter.Name, owned.OwnerToken);
        }

        private static string OwnedChapterKey(OwnedChapterMarker chapter)
        {
            return ((int)chapter.MarkerType).ToString() + ":" +
                chapter.StartTicks.ToString() + ":" +
                (chapter.Name ?? string.Empty) + ":" +
                (chapter.OwnerToken ?? string.Empty);
        }

        private static IReadOnlyList<OwnedChapterMarker> DedupOwnedChapters(
          IEnumerable<OwnedChapterMarker> chapters)
        {
            var result = new List<OwnedChapterMarker>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var chapter in chapters)
            {
                if (seen.Add(OwnedChapterKey(chapter)))
                {
                    result.Add(chapter);
                }
            }

            return result;
        }

        private static bool OwnedChaptersEqual(
          IEnumerable<OwnedChapterMarker> a, IEnumerable<OwnedChapterMarker> b)
        {
            var left = new HashSet<string>(a.Select(OwnedChapterKey), StringComparer.Ordinal);
            var right = new HashSet<string>(b.Select(OwnedChapterKey), StringComparer.Ordinal);
            return left.SetEquals(right);
        }

        private static long ClampTicks(long ticks, long? durationTicks)
        {
            if (ticks < 0)
            {
                return 0;
            }

            if (!durationTicks.HasValue || durationTicks.Value <= 0)
            {
                return ticks;
            }

            var max = durationTicks.Value - TimeSpan.TicksPerSecond;
            if (max < 0)
            {
                max = 0;
            }

            return ticks > max ? max : ticks;
        }


    }
}

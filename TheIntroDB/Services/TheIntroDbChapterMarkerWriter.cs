using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using TheIntroDB.Configuration;
using TheIntroDB.Models;

namespace TheIntroDB.Services
{
    public sealed class TheIntroDbChapterMarkerWriter
    {
        private readonly IItemRepository _itemRepository;
        private readonly ILogger _logger;


        public TheIntroDbChapterMarkerWriter(IItemRepository itemRepository,
          ILogger logger)
        {
            _itemRepository = itemRepository;
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

            var protectIntro = config.ProtectExistingIntroMarkers &&
                ChapterMarkerPolicy.HasNativeIntroMarker(existing);
            var protectCredits = config.ProtectExistingCreditsMarkers &&
                ChapterMarkerPolicy.HasNativeCreditsMarker(existing);

            ChapterMarkerPolicy.RemoveOwnedMarkers(chapters,
                config.EnableIntro && !protectIntro,
                config.EnableCredits && !protectCredits,
                config.EnableRecap,
                config.EnablePreview);

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
                            added += AddIntroMarkers(chapters, normalized);
                        }
                        break;
                    case MediaSegmentType.Recap:
                        if (config.EnableRecap)
                        {
                            added += AddChapterRange(chapters, "Recap",
                              "Recap End", normalized);
                        }
                        break;
                    case MediaSegmentType.Credits:
                        if (config.EnableCredits && !protectCredits)
                        {
                            added += AddCreditsMarkers(chapters, normalized);
                        }
                        break;
                    case MediaSegmentType.Preview:
                        if (config.EnablePreview)
                        {
                            added += AddChapterRange(chapters, "Preview",
                              "Preview End", normalized);
                        }
                        break;
                }
            }

            var deduped = Deduplicate(chapters);
            deduped.Sort((a, b) =>
              a.StartPositionTicks.CompareTo(b.StartPositionTicks));

            if (ChapterListsEqual(existing, deduped))
            {
                _logger.Debug("TheIntroDB chapters unchanged for {0} ({1}), skipping save",
                    item.Name, item.InternalId);
                return 0;
            }

            if (preview)
            {
                _logger.Debug("TheIntroDB preview would save {0} chapters/markers for {1} ({2})",
                    deduped.Count, item.Name, item.InternalId);
                return added;
            }

            _itemRepository.SaveChapters(item.InternalId, deduped);
            _logger.Debug("TheIntroDB saved {0} chapters/markers for {1} ({2})",
              deduped.Count, item.Name, item.InternalId);

            return added;
        }

        private int AddIntroMarkers(List<ChapterInfo> chapters,
          StoredMediaSegment s)
        {
            var added = 0;

            // Always add start marker — even at 0:00
            added += AddIfMissing(chapters, MarkerType.IntroStart,
              s.StartTicks, "Intro");
            added += AddIfMissing(chapters, MarkerType.Chapter,
              s.StartTicks, "Intro" + ChapterMarkerPolicy.TheIntroDbTag);

            // Always add end marker — even if it equals start
            // (e.g. point-like intro at 0:00)
            added += AddIfMissing(chapters, MarkerType.IntroEnd, s.EndTicks,
              "Intro End");
            added += AddIfMissing(chapters, MarkerType.Chapter, s.EndTicks,
              "Intro End" + ChapterMarkerPolicy.TheIntroDbTag);

            return added;
        }

        private int AddCreditsMarkers(List<ChapterInfo> chapters,
          StoredMediaSegment s)
        {
            var added = 0;

            // Start marker
            added += AddIfMissing(chapters, MarkerType.CreditsStart,
              s.StartTicks, "Credits");
            added += AddIfMissing(chapters, MarkerType.Chapter,
              s.StartTicks, "Credits" + ChapterMarkerPolicy.TheIntroDbTag);

            // End marker at media duration — credits extend to the end
            added += AddIfMissing(chapters, MarkerType.Chapter,
              s.EndTicks, "Credits End" + ChapterMarkerPolicy.TheIntroDbTag);

            return added;
        }

        private int AddChapterRange(List<ChapterInfo> chapters,
          string startName, string endName, StoredMediaSegment s)
        {
            var added = 0;

            // Always add start marker — even at 0:00
            added += AddIfMissing(chapters, MarkerType.Chapter,
              s.StartTicks, startName + ChapterMarkerPolicy.TheIntroDbTag);

            // Always add end marker — even if it equals start
            added += AddIfMissing(chapters, MarkerType.Chapter,
              s.EndTicks, endName + ChapterMarkerPolicy.TheIntroDbTag);

            return added;
        }

        private static int AddIfMissing(List<ChapterInfo> chapters,
          MarkerType markerType, long startTicks, string name)
        {
            if (chapters.Any(c => c.MarkerType == markerType &&
                c.StartPositionTicks == startTicks))
            {
                return 0;
            }

            chapters.Add(new ChapterInfo
            {
                Name = name,
                StartPositionTicks = startTicks,
                MarkerType = markerType
            });

            return 1;
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

        private static List<ChapterInfo> Deduplicate(
          IEnumerable<ChapterInfo> chapters)
        {
            var list = new List<ChapterInfo>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var c in chapters)
            {
                var key = ((int)c.MarkerType).ToString() + ":" +
                  c.StartPositionTicks.ToString() + ":" +
                  (c.Name ?? string.Empty);
                if (seen.Add(key))
                {
                    list.Add(c);
                }
            }

            return list;
        }

        private static bool ChapterListsEqual(
          IReadOnlyList<ChapterInfo> a,
          IReadOnlyList<ChapterInfo> b)
        {
            if (a.Count != b.Count)
            {
                return false;
            }

            var keyA = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < a.Count; i++)
            {
                keyA.Add(ChapterKey(a[i]));
            }

            for (int i = 0; i < b.Count; i++)
            {
                if (!keyA.Contains(ChapterKey(b[i])))
                {
                    return false;
                }
            }

            return true;
        }

        private static string ChapterKey(ChapterInfo c)
        {
            return ((int)c.MarkerType).ToString() + ":" +
                c.StartPositionTicks.ToString() + ":" +
                (c.Name ?? string.Empty);
        }
    }
}

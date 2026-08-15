using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Model.Entities;

namespace TheIntroDB.Services
{
    public static class ChapterMarkerPolicy
    {
        public const string TheIntroDbTag = " (TheIntroDB)";

        public static bool HasNativeIntroMarker(IReadOnlyCollection<ChapterInfo> chapters)
        {
            return chapters.Any(c =>
                (c.MarkerType == MarkerType.IntroStart || c.MarkerType == MarkerType.IntroEnd) &&
                !HasOwnedCompanion(chapters, c));
        }

        public static bool HasNativeCreditsMarker(IReadOnlyCollection<ChapterInfo> chapters)
        {
            return chapters.Any(c => c.MarkerType == MarkerType.CreditsStart && !HasOwnedCompanion(chapters, c));
        }

        public static void RemoveOwnedMarkers(List<ChapterInfo> chapters, bool removeIntro, bool removeCredits,
            bool removeRecap, bool removePreview)
        {
            var ownedIntroTicks = TaggedTicks(chapters, "Intro", "Intro End");
            var ownedCreditsTicks = TaggedTicks(chapters, "Credits", "Credits End");

            chapters.RemoveAll(c =>
                (removeIntro && IsTagged(c, "Intro", "Intro End")) ||
                (removeCredits && IsTagged(c, "Credits", "Credits End")) ||
                (removeRecap && IsTagged(c, "Recap", "Recap End")) ||
                (removePreview && IsTagged(c, "Preview", "Preview End")) ||
                (removeIntro && (c.MarkerType == MarkerType.IntroStart || c.MarkerType == MarkerType.IntroEnd) &&
                    ownedIntroTicks.Contains(c.StartPositionTicks)) ||
                (removeCredits && c.MarkerType == MarkerType.CreditsStart &&
                    ownedCreditsTicks.Contains(c.StartPositionTicks)));
        }

        private static bool HasOwnedCompanion(IEnumerable<ChapterInfo> chapters, ChapterInfo marker)
        {
            var prefix = marker.MarkerType == MarkerType.CreditsStart ? "Credits" : "Intro";
            return chapters.Any(c => c.MarkerType == MarkerType.Chapter &&
                c.StartPositionTicks == marker.StartPositionTicks &&
                c.Name != null && c.Name.StartsWith(prefix, StringComparison.Ordinal) &&
                c.Name.EndsWith(TheIntroDbTag, StringComparison.Ordinal));
        }

        private static HashSet<long> TaggedTicks(IEnumerable<ChapterInfo> chapters, params string[] names)
        {
            var fullNames = new HashSet<string>(names.Select(n => n + TheIntroDbTag), StringComparer.Ordinal);
            return new HashSet<long>(chapters.Where(c => c.MarkerType == MarkerType.Chapter &&
                c.Name != null && fullNames.Contains(c.Name)).Select(c => c.StartPositionTicks));
        }

        private static bool IsTagged(ChapterInfo chapter, params string[] names)
        {
            if (chapter.MarkerType != MarkerType.Chapter || chapter.Name == null)
            {
                return false;
            }

            return names.Any(n => string.Equals(chapter.Name, n + TheIntroDbTag, StringComparison.Ordinal));
        }
    }
}

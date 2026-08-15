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
                c.MarkerType == MarkerType.IntroStart || c.MarkerType == MarkerType.IntroEnd);
        }

        public static bool HasNativeCreditsMarker(IReadOnlyCollection<ChapterInfo> chapters)
        {
            return chapters.Any(c => c.MarkerType == MarkerType.CreditsStart);
        }
    }
}

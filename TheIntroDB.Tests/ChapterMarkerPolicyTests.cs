using System.Collections.Generic;
using MediaBrowser.Model.Entities;
using TheIntroDB.Configuration;
using TheIntroDB.Services;
using Xunit;

namespace TheIntroDB.Tests
{
    public class ChapterMarkerPolicyTests
    {
        [Fact]
        public void PartialNativeIntroMarkerIsProtected()
        {
            var chapters = new List<ChapterInfo>
            {
                Chapter(MarkerType.IntroStart, 100, "Intro")
            };

            Assert.True(ChapterMarkerPolicy.HasNativeIntroMarker(chapters));
        }

        [Fact]
        public void TaggedCompanionCannotDisqualifyAnExistingIntroMarker()
        {
            var chapters = new List<ChapterInfo>
            {
                Chapter(MarkerType.IntroStart, 100, "Intro"),
                Chapter(MarkerType.Chapter, 100, "Intro (TheIntroDB)")
            };

            Assert.True(ChapterMarkerPolicy.HasNativeIntroMarker(chapters));
        }

        [Fact]
        public void TaggedCompanionCannotDisqualifyAnExistingCreditsMarker()
        {
            var chapters = new List<ChapterInfo>
            {
                Chapter(MarkerType.CreditsStart, 900, "Credits"),
                Chapter(MarkerType.Chapter, 900, "Credits (TheIntroDB)")
            };

            Assert.True(ChapterMarkerPolicy.HasNativeCreditsMarker(chapters));
        }

        [Fact]
        public void NativeCreditsMarkerIsProtected()
        {
            var chapters = new List<ChapterInfo>
            {
                Chapter(MarkerType.CreditsStart, 900, "Credits")
            };

            Assert.True(ChapterMarkerPolicy.HasNativeCreditsMarker(chapters));
        }

        [Fact]
        public void SafeOperationalDefaultsAreEnabled()
        {
            var config = new PluginConfiguration();

            Assert.True(config.ProtectExistingIntroMarkers);
            Assert.True(config.ProtectExistingCreditsMarkers);
            Assert.Equal(200, config.MaxLookupsPerRun);
            Assert.False(config.EnableOnDemandFetch);
            Assert.False(config.EnableAnonymousUsageReporting);
            Assert.False(config.EnablePreview);
        }

        private static ChapterInfo Chapter(MarkerType markerType, long ticks, string name)
        {
            return new ChapterInfo
            {
                MarkerType = markerType,
                StartPositionTicks = ticks,
                Name = name
            };
        }
    }
}

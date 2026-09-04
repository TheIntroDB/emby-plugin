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
        public void NativeIntroMarkerProtected()
        {
            var chapters = new List<ChapterInfo>
            {
                Chapter(MarkerType.IntroStart, 100, "Intro")
            };

            Assert.True(ChapterMarkerPolicy.HasNativeIntroMarker(chapters));
        }

        [Fact]
        public void TaggedCannotDisqualifyExistingIntro()
        {
            var chapters = new List<ChapterInfo>
            {
                Chapter(MarkerType.IntroStart, 100, "Intro"),
                Chapter(MarkerType.Chapter, 100, "Intro (TheIntroDB)")
            };

            Assert.True(ChapterMarkerPolicy.HasNativeIntroMarker(chapters));
        }

        [Fact]
        public void TaggedCannotDisqualifyExistingCredits()
        {
            var chapters = new List<ChapterInfo>
            {
                Chapter(MarkerType.CreditsStart, 900, "Credits"),
                Chapter(MarkerType.Chapter, 900, "Credits (TheIntroDB)")
            };

            Assert.True(ChapterMarkerPolicy.HasNativeCreditsMarker(chapters));
        }

        [Fact]
        public void NativeCreditsProtected()
        {
            var chapters = new List<ChapterInfo>
            {
                Chapter(MarkerType.CreditsStart, 900, "Credits")
            };

            Assert.True(ChapterMarkerPolicy.HasNativeCreditsMarker(chapters));
        }

        [Fact]
        public void DefaultsAreEnabled()
        {
            var config = new PluginConfiguration();

            Assert.True(config.ProtectExistingIntroMarkers);
            Assert.True(config.ProtectExistingCreditsMarkers);
            Assert.Equal(400, config.MaxLookupsPerRun);
            Assert.False(config.EnableOnDemandFetch);
            Assert.False(config.EnableAnonymousUsageReporting);
            Assert.False(config.EnablePreview);
            Assert.False(config.ReplaceExistingMarkers);
        }

        [Fact]
        public void OwnershipTokenExactMatch()
        {
            const string token = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
            var name = ChapterMarkerPolicy.AddOwnershipToken("Intro", token);

            Assert.True(ChapterMarkerPolicy.HasOwnershipToken(name, token));
            Assert.True(ChapterMarkerPolicy.HasOwnedLabel(name, "Intro"));
            Assert.False(ChapterMarkerPolicy.HasOwnershipToken(name, "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"));
            Assert.False(ChapterMarkerPolicy.HasOwnershipToken("Intro (TheIntroDB)", token));
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

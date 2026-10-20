using System;
using System.Collections.Generic;
using System.Reflection;
using MediaBrowser.Model.Entities;
using TheIntroDB.Api;
using TheIntroDB.Models;
using TheIntroDB.Providers;
using Xunit;

namespace TheIntroDB.Tests
{
    /// <summary>
    /// Range checks shared with the Jellyfin plugin: the API answers
    /// <c>{"start_ms": 0, "end_ms": 0}</c> for a segment it has confirmed absent, and a
    /// credits/preview segment stores its end as "until the end of the media" when it is
    /// missing, so a 0 end must not be accepted as a boundary.
    /// </summary>
    public class SegmentTimestampTests
    {
        private const long RunTimeTicks = 1_800_000_000L; // 3 minutes

        public static TheoryData<bool, long?, long?, bool> Ranges => new TheoryData<bool, long?, long?, bool>
        {
            { true, 1000L, 2000L, true },  // intro: start and end
            { true, null, 2000L, true },   // intro: start optional
            { true, 1000L, 1000L, false }, // intro: empty range
            { true, 1000L, null, false },  // intro: missing end is invalid
            { false, 1000L, null, true },  // credits: start set, end optional
            { false, null, 5000L, true },  // credits: end set, start optional
            { false, null, null, false },  // neither boundary
            { false, 0L, 0L, false },      // credits/preview: 0/0 marks the segment as absent
            { false, null, 0L, false },    // end at the start is absent too
            { false, 2000L, 1000L, false } // end before start is invalid
        };

        [Theory]
        [MemberData(nameof(Ranges))]
        public void AcceptsOnlyRangesThatEndAfterTheyStart(bool endRequired, long? startMs, long? endMs, bool expected)
        {
            var stamp = new SegmentTimestamp { StartMs = startMs, EndMs = endMs };

            Assert.Equal(expected, stamp.HasValidRange(endRequired));
        }

        [Fact]
        public void ConfirmedAbsentSegmentStoresNoMarker()
        {
            // The reported case: preview [{start_ms: 0, end_ms: 0}] must not become a Preview
            // covering the media from 0:00 to the end.
            var segments = ConvertStamps(false, MediaSegmentType.Preview, new SegmentTimestamp { StartMs = 0, EndMs = 0 });

            Assert.Empty(segments);
        }

        [Fact]
        public void CreditsWithoutAnEndRunToTheEndOfTheMedia()
        {
            var segments = ConvertStamps(false, MediaSegmentType.Credits, new SegmentTimestamp { StartMs = 1000, EndMs = null });

            var segment = Assert.Single(segments);
            Assert.Equal(1000 * TimeSpan.TicksPerMillisecond, segment.StartTicks);
            Assert.Equal(RunTimeTicks, segment.EndTicks);
        }

        [Fact]
        public void ReversedRangeStoresNoMarker()
        {
            var segments = ConvertStamps(false, MediaSegmentType.Credits, new SegmentTimestamp { StartMs = 2000, EndMs = 1000 });

            Assert.Empty(segments);
        }

        [Fact]
        public void IntroWithoutAnEndStoresNoMarker()
        {
            var segments = ConvertStamps(true, MediaSegmentType.Intro, new SegmentTimestamp { StartMs = 1000, EndMs = null });

            Assert.Empty(segments);
        }

        private static List<MediaSegmentData> ConvertStamps(bool endRequired, MediaSegmentType type, params SegmentTimestamp[] stamps)
        {
            var method = typeof(TheIntroDbSegmentProvider).GetMethod(
                "AddSegments",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(method);

            var segments = new List<MediaSegmentData>();
            method.Invoke(null, new object[] { stamps, endRequired, type, Guid.NewGuid(), (long?)RunTimeTicks, segments });
            return segments;
        }
    }
}

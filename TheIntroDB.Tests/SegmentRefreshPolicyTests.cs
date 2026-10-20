using System;
using System.Collections.Generic;
using TheIntroDB.Services;
using Xunit;

namespace TheIntroDB.Tests
{
    public class SegmentRefreshPolicyTests
    {
        [Theory]
        [InlineData(0, false, false)] // nothing stored: fetch
        [InlineData(0, true, false)]  // nothing stored, refresh irrelevant: fetch
        [InlineData(2, false, true)]  // own segments, not due: keep
        [InlineData(2, true, false)]  // own segments, due: fetch again
        public void KeepsOnlyOwnSegmentsThatAreNotDue(int ownCount, bool refreshDue, bool expected)
        {
            Assert.Equal(expected, SegmentRefreshPolicy.ShouldKeepExistingSegments(ownCount, refreshDue));
        }

        [Theory]
        [InlineData(0.0, 22.5)]
        [InlineData(0.5, 30.0)]
        [InlineData(0.999999, 37.5)]
        public void RefreshTtlSpreadsAroundTheSetting(double jitter, double expectedDays)
        {
            Assert.Equal(expectedDays, SegmentRefreshPolicy.RefreshTtl(30, jitter).TotalDays, 3);
        }

        [Fact]
        public void NothingStoredIsNeverDue()
        {
            var lastChecked = DateTime.UtcNow.AddYears(-1);

            Assert.False(SegmentRefreshPolicy.IsRefreshDue(0, lastChecked, 30, 1, DateTime.UtcNow));
        }

        [Fact]
        public void RefreshDisabledIsNeverDue()
        {
            var lastChecked = DateTime.UtcNow.AddYears(-1);

            Assert.False(SegmentRefreshPolicy.IsRefreshDue(4, lastChecked, 0, 1, DateTime.UtcNow));
        }

        [Fact]
        public void SegmentsWithoutARecordedFetchAreRefreshedOnce()
        {
            Assert.True(SegmentRefreshPolicy.IsRefreshDue(4, null, 30, 1, DateTime.UtcNow));
        }

        [Fact]
        public void FreshSegmentsAreKeptUntilTheIntervalPassed()
        {
            var now = DateTime.UtcNow;

            Assert.False(SegmentRefreshPolicy.IsRefreshDue(4, now.AddDays(-1), 30, 1, now));
            Assert.False(SegmentRefreshPolicy.IsRefreshDue(4, now.AddDays(-22), 30, 1, now));
        }

        [Fact]
        public void SegmentsPastTheLongestSpreadAreDue()
        {
            var now = DateTime.UtcNow;

            // The TTL is at most 125 % of the setting, so 38 days is past it for any jitter.
            Assert.True(SegmentRefreshPolicy.IsRefreshDue(4, now.AddDays(-38), 30, 1, now));
        }

        [Fact]
        public void NoSegmentIsDueWhileRefreshingIsOff()
        {
            var now = DateTime.UtcNow;
            var ids = new long[] { 1L, 2L, 3L };
            var lastChecked = new Dictionary<long, DateTime> { [1L] = now.AddYears(-1), [2L] = now };

            Assert.Empty(SegmentRefreshPolicy.DueIds(ids, lastChecked, 0, now));
        }

        [Fact]
        public void OnlyItemsPastTheIntervalAreDue()
        {
            var now = DateTime.UtcNow;
            var ids = new long[] { 1L, 2L, 3L, 4L };
            var lastChecked = new Dictionary<long, DateTime>
            {
                [1L] = now.AddDays(-1),    // fresh: kept
                [2L] = now.AddDays(-38),   // past the longest spread: refreshed
                [3L] = now.AddDays(-100),  // long overdue: refreshed
                // 4 has segments but no recorded fetch: refreshed once
            };

            var due = SegmentRefreshPolicy.DueIds(ids, lastChecked, 30, now);

            Assert.Equal(3, due.Count);
            Assert.Contains(2L, due);
            Assert.Contains(3L, due);
            Assert.Contains(4L, due);
            Assert.DoesNotContain(1L, due);
        }

        [Fact]
        public void ItemsWithoutSegmentsAreNotConsidered()
        {
            // DueIds only sees the ids that already have segments, so an empty list is empty.
            Assert.Empty(SegmentRefreshPolicy.DueIds(
                Array.Empty<long>(),
                new Dictionary<long, DateTime>(),
                30,
                DateTime.UtcNow));
        }

        [Fact]
        public void JitterIsStablePerItemAndCoversTheSpread()
        {
            var min = 1.0;
            var max = 0.0;

            for (long id = 1; id <= 5000; id++)
            {
                var jitter = SegmentRefreshPolicy.JitterFor(id);

                Assert.InRange(jitter, 0.0, 0.999999);
                Assert.Equal(jitter, SegmentRefreshPolicy.JitterFor(id));
                min = Math.Min(min, jitter);
                max = Math.Max(max, jitter);
            }

            // The spread has to actually vary, or every item of a scan falls due in the
            // same later scan, which is what the jitter exists to prevent.
            Assert.True(min < 0.05, "expected ids spread below 5 % jitter");
            Assert.True(max > 0.95, "expected ids spread above 95 % jitter");
        }
    }
}

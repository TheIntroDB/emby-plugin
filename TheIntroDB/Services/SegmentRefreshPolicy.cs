using System;
using System.Collections.Generic;

namespace TheIntroDB.Services
{
    /// <summary>
    /// Decides whether an item that already has TheIntroDB segments is fetched again.
    /// With <c>IgnoreMediaWithExistingSegments</c> on an item is skipped forever, so a
    /// corrected or newly accepted submission never reaches a library that fetched the old
    /// one. The plugin's own segments are therefore fetched again once the refresh interval
    /// passed. Emby has no notion of segments served by another provider (the plugin writes
    /// the chapters itself), so the only rule is "own segments that are not due yet".
    /// </summary>
    public static class SegmentRefreshPolicy
    {
        /// <summary>
        /// Whether an item keeps its segments instead of being fetched.
        /// </summary>
        /// <param name="ownCount">This plugin's stored segments of the item.</param>
        /// <param name="refreshDue">Whether they are due for a refresh.</param>
        /// <returns>Whether to keep the existing segments.</returns>
        public static bool ShouldKeepExistingSegments(int ownCount, bool refreshDue)
            => ownCount > 0 && !refreshDue;

        /// <summary>
        /// Whether the item's own segments are due to be fetched again.
        /// </summary>
        /// <param name="ownCount">This plugin's stored segments of the item.</param>
        /// <param name="lastCheckedUtc">When the item was last fetched, if known.</param>
        /// <param name="refreshAfterDays">The configured interval in days; 0 never refreshes.</param>
        /// <param name="itemInternalId">The item's internal id, used for the per-item spread.</param>
        /// <param name="nowUtc">The current time.</param>
        /// <returns>Whether the segments are due for a refresh.</returns>
        public static bool IsRefreshDue(int ownCount, DateTime? lastCheckedUtc, int refreshAfterDays, long itemInternalId, DateTime nowUtc)
        {
            if (ownCount <= 0 || refreshAfterDays <= 0)
            {
                // Nothing stored, or refreshing turned off.
                return false;
            }

            if (!lastCheckedUtc.HasValue)
            {
                // Segments without a recorded fetch (an older database): refresh once.
                return true;
            }

            return nowUtc - lastCheckedUtc.Value >= RefreshTtl(refreshAfterDays, JitterFor(itemInternalId));
        }

        /// <summary>
        /// The items whose own segments are past the refresh interval (or have no recorded
        /// fetch), so they are scanned again although they already have segments.
        /// </summary>
        /// <param name="segmentedIds">The items that already have segments.</param>
        /// <param name="lastCheckedUtcById">When each item was last fetched, where known.</param>
        /// <param name="refreshAfterDays">The configured interval in days; 0 never refreshes.</param>
        /// <param name="nowUtc">The current time.</param>
        /// <returns>The ids that are due for a refresh.</returns>
        public static HashSet<long> DueIds(
            IEnumerable<long> segmentedIds,
            IReadOnlyDictionary<long, DateTime> lastCheckedUtcById,
            int refreshAfterDays,
            DateTime nowUtc)
        {
            var due = new HashSet<long>();
            if (refreshAfterDays <= 0 || segmentedIds == null)
            {
                return due;
            }

            foreach (var id in segmentedIds)
            {
                DateTime? lastChecked = null;
                DateTime checkedUtc;
                if (lastCheckedUtcById != null && lastCheckedUtcById.TryGetValue(id, out checkedUtc))
                {
                    lastChecked = checkedUtc;
                }

                if (IsRefreshDue(1, lastChecked, refreshAfterDays, id, nowUtc))
                {
                    due.Add(id);
                }
            }

            return due;
        }

        /// <summary>
        /// How long a fetch holds before the item is refreshed: 75 to 125 % of the setting, so
        /// the items of one scan do not all fall due in the same later scan.
        /// </summary>
        /// <param name="days">The configured refresh interval in days.</param>
        /// <param name="jitter">A value in [0, 1).</param>
        /// <returns>The time until the refresh.</returns>
        public static TimeSpan RefreshTtl(int days, double jitter)
            => TimeSpan.FromDays(days * (0.75 + (0.5 * jitter)));

        /// <summary>
        /// The spread applied to an item, derived from its id so it stays the same across
        /// scans: a value in [0, 1) that re-rolling every scan would turn into repeated
        /// refreshes of the same item.
        /// </summary>
        /// <param name="itemInternalId">The item's internal id.</param>
        /// <returns>A stable value in [0, 1).</returns>
        public static double JitterFor(long itemInternalId)
        {
            // Knuth multiplicative hash, folded into [0, 1).
            unchecked
            {
                var hashed = (ulong)itemInternalId * 2654435761UL;
                return (hashed % 100000UL) / 100000.0;
            }
        }
    }
}

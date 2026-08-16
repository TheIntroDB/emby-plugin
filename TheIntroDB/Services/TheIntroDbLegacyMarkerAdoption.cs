using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using TheIntroDB.Data;
using TheIntroDB.Models;

namespace TheIntroDB.Services
{
    /// <summary>
    /// One-time, API-free adoption of markers written by pre-token plugin releases
    /// into the durable ownership ledger. Recognizes only chapters the plugin
    /// itself wrote (exact legacy "(TheIntroDB)" tags plus their paired logical
    /// markers at the same position). Foreign, native and manual chapters are
    /// never claimed, and no media or API access happens here.
    /// </summary>
    public sealed class TheIntroDbLegacyMarkerAdoption
    {
        private readonly IItemRepository _itemRepository;
        private readonly ITheIntroDbSegmentRepository _segmentRepository;
        private readonly ILogger _logger;

        /// <summary>
        /// Legacy companion chapter names written by releases before ownership
        /// tokens existed, mapped to the logical marker they were written
        /// alongside (MarkerType + bare name), when one exists.
        /// </summary>
        private static readonly Dictionary<string, LegacyPair> LegacyCompanions =
            new Dictionary<string, LegacyPair>(StringComparer.Ordinal)
            {
                ["Intro (TheIntroDB)"] = new LegacyPair(MarkerType.IntroStart, "Intro"),
                ["Intro End (TheIntroDB)"] = new LegacyPair(MarkerType.IntroEnd, "Intro End"),
                ["Credits (TheIntroDB)"] = new LegacyPair(MarkerType.CreditsStart, "Credits"),
                ["Credits End (TheIntroDB)"] = new LegacyPair(null, null),
                ["Recap (TheIntroDB)"] = new LegacyPair(null, null),
                ["Recap End (TheIntroDB)"] = new LegacyPair(null, null),
                ["Preview (TheIntroDB)"] = new LegacyPair(null, null),
                ["Preview End (TheIntroDB)"] = new LegacyPair(null, null)
            };

        public TheIntroDbLegacyMarkerAdoption(
            IItemRepository itemRepository,
            ITheIntroDbSegmentRepository segmentRepository,
            ILogger logger)
        {
            _itemRepository = itemRepository;
            _segmentRepository = segmentRepository;
            _logger = logger;
        }

        /// <summary>
        /// Adopts legacy TheIntroDB markers for one item. Renames owned chapters to
        /// their tokenized form, records matching ownership ledger rows, and writes
        /// both stores. Returns the number of chapters adopted (0 when the item has
        /// nothing to adopt, in which case nothing is written).
        /// </summary>
        public int AdoptItem(BaseItem item)
        {
            if (item == null)
            {
                return 0;
            }

            var existing = _itemRepository.GetChapters(item) ?? new List<ChapterInfo>();
            if (existing.Count == 0)
            {
                return 0;
            }

            var adoptedKeys = new HashSet<string>(StringComparer.Ordinal);
            var pairedLogicalKeys = new HashSet<string>(StringComparer.Ordinal);

            foreach (var companion in existing)
            {
                if (companion.MarkerType != MarkerType.Chapter)
                {
                    continue;
                }

                if (!LegacyCompanions.TryGetValue(companion.Name ?? string.Empty, out var pair))
                {
                    continue;
                }

                // A duplicate of an exact legacy name is not ours: the plugin never
                // wrote two chapters with the same type and ticks.
                if (!adoptedKeys.Add(ChapterKey(companion)))
                {
                    continue;
                }

                if (!pair.MarkerType.HasValue)
                {
                    continue;
                }

                // Adopt the paired logical marker (bare name at the same position)
                // only when it exists and has not already been claimed.
                foreach (var logical in existing)
                {
                    if (logical.MarkerType != pair.MarkerType.Value ||
                        logical.StartPositionTicks != companion.StartPositionTicks ||
                        !string.Equals(logical.Name ?? string.Empty, pair.Name, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (pairedLogicalKeys.Add(ChapterKey(logical)))
                    {
                        adoptedKeys.Add(ChapterKey(logical));
                    }

                    break;
                }
            }

            if (adoptedKeys.Count == 0)
            {
                return 0;
            }

            var ownedRows = new List<OwnedChapterMarker>(
                _segmentRepository.GetOwnedChapters(item.InternalId) ??
                Array.Empty<OwnedChapterMarker>());

            var updated = new List<ChapterInfo>(existing.Count);
            foreach (var chapter in existing)
            {
                if (!adoptedKeys.Contains(ChapterKey(chapter)))
                {
                    updated.Add(chapter);
                    continue;
                }

                var token = Guid.NewGuid().ToString("N");
                var newName = ChapterMarkerPolicy.AddOwnershipToken(chapter.Name, token);

                updated.Add(new ChapterInfo
                {
                    Name = newName,
                    StartPositionTicks = chapter.StartPositionTicks,
                    MarkerType = chapter.MarkerType
                });

                ownedRows.Add(new OwnedChapterMarker
                {
                    ItemInternalId = item.InternalId,
                    MarkerType = chapter.MarkerType,
                    StartTicks = chapter.StartPositionTicks,
                    Name = newName,
                    OwnerToken = token
                });
            }

            _itemRepository.SaveChapters(item.InternalId, updated);
            _segmentRepository.ReplaceOwnedChapters(item.InternalId, ownedRows, DateTime.UtcNow);

            _logger.Info("TheIntroDB adopted {0} legacy chapter markers for {1} ({2})",
                adoptedKeys.Count, item.Name, item.InternalId);
            return adoptedKeys.Count;
        }

        private static string ChapterKey(ChapterInfo chapter)
        {
            return ((int)chapter.MarkerType).ToString() + ":" +
                chapter.StartPositionTicks.ToString() + ":" +
                (chapter.Name ?? string.Empty);
        }

        private struct LegacyPair
        {
            public LegacyPair(MarkerType? markerType, string name)
            {
                MarkerType = markerType;
                Name = name;
            }

            public MarkerType? MarkerType { get; }

            public string Name { get; }
        }
    }
}

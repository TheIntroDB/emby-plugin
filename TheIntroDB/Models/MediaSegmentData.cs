// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 TheIntroDB

using System;

namespace TheIntroDB.Models
{
    /// <summary>
    /// Represents media segment data for Emby.
    /// </summary>
    public class MediaSegmentData
    {
        /// <summary>
        /// Gets or sets the item ID this segment belongs to.
        /// </summary>
        public Guid ItemId { get; set; }

        /// <summary>
        /// Gets or sets the type of segment (Intro, Recap, Credits, Preview).
        /// </summary>
        public MediaSegmentType Type { get; set; }

        /// <summary>
        /// Gets or sets the start time in ticks.
        /// </summary>
        public long StartTicks { get; set; }

        /// <summary>
        /// Gets or sets the end time in ticks.
        /// </summary>
        public long EndTicks { get; set; }

        /// <summary>
        /// Gets the start time as a <see cref="TimeSpan"/>.
        /// </summary>
        public TimeSpan StartTime => TimeSpan.FromTicks(StartTicks);

        /// <summary>
        /// Gets the end time as a <see cref="TimeSpan"/>.
        /// </summary>
        public TimeSpan EndTime => TimeSpan.FromTicks(EndTicks);

        /// <summary>
        /// Gets the duration of the segment.
        /// </summary>
        public TimeSpan Duration => TimeSpan.FromTicks(EndTicks - StartTicks);
    }

    /// <summary>
    /// Types of media segments supported by TheIntroDB.
    /// </summary>
    public enum MediaSegmentType
    {
        /// <summary>
        /// Intro/opening sequence.
        /// </summary>
        Intro,

        /// <summary>
        /// Recap of previous episodes.
        /// </summary>
        Recap,

        /// <summary>
        /// Credits/outro sequence.
        /// </summary>
        Credits,

        /// <summary>
        /// Preview of the next episode.
        /// </summary>
        Preview
    }
}

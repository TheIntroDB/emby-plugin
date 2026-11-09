// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 TheIntroDB

namespace TheIntroDB.Models
{
    public sealed class StoredMediaSegment
    {
        public long ItemInternalId { get; set; }
        public MediaSegmentType Type { get; set; }
        public long StartTicks { get; set; }
        public long EndTicks { get; set; }
    }
}


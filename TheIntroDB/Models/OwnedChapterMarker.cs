// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 TheIntroDB

using MediaBrowser.Model.Entities;

namespace TheIntroDB.Models
{
    public sealed class OwnedChapterMarker
    {
        public long ItemInternalId { get; set; }

        public MarkerType MarkerType { get; set; }

        public long StartTicks { get; set; }

        public string Name { get; set; }

        public string OwnerToken { get; set; }
    }
}

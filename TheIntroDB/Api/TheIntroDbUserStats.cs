// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 TheIntroDB

using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TheIntroDB.Api
{
    public sealed class TheIntroDbUserStats
    {
        [JsonPropertyName("total")]
        public int Total { get; set; }

        [JsonPropertyName("accepted")]
        public int Accepted { get; set; }

        [JsonPropertyName("pending")]
        public int Pending { get; set; }

        [JsonPropertyName("rejected")]
        public int Rejected { get; set; }

        [JsonPropertyName("acceptance_rate")]
        public double AcceptanceRate { get; set; }

        [JsonPropertyName("current_streak")]
        public int CurrentStreak { get; set; }

        [JsonPropertyName("best_streak")]
        public int BestStreak { get; set; }

        [JsonPropertyName("total_time_saved_ms")]
        public long TotalTimeSavedMs { get; set; }

        [JsonPropertyName("top_media")]
        public Collection<JsonElement> TopMedia { get; set; }

        [JsonPropertyName("error")]
        public string Error { get; set; }

        public TheIntroDbUserStats()
        {
            TopMedia = new Collection<JsonElement>();
        }
    }
}

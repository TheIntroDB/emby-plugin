# TheIntroDB – Emby Plugin

<p align="center">
  <img src="https://raw.githubusercontent.com/TheIntroDB/theintrodb-assets/main/logo-banner.png">
</p>

This plugin fetches intro, recap, credits, and preview timestamps from [TheIntroDB](https://theintrodb.org) for your Emby library. It uses this data to enable intro skipping features in compatible Emby clients.

**Requirements:** Emby Server 4.7+. **TVDB metadata is recommended**: TVDB is TheIntroDB's identity, and it is what lets series with a non-default display order (DVD, absolute) resolve to the correct episode. TMDB and IMDb ids still work as fallbacks.

**Important:** Segments are **not** fetched when you press play. They are populated when the **TheIntroDB Media Segment Scan** scheduled task runs. Until that task has run for your library, skip features may not be available.

---

## Installation

1. Open Emby's WebUI and navigate to the plugin catalog.
2. Find TheIntroDB under the general plugin section.
3. Install the latest version!
4. Restart Emby Server.
5. Configure the plugin at **Dashboard → Plugins → TheIntroDB**.
6. Run the scheduled task to populate data: **Dashboard → Scheduled Tasks → TheIntroDB Media Segment Scan** and click the **Play** button (▶).

## Manual Installation

Tip: use [Emby.GitHubRepoPluginInstall](https://github.com/bakes82/Emby.GitHubRepoPluginInstall) to install from GitHub releases directly!

1. Download the latest plugin release from the [Releases](https://github.com/TheIntroDB/emby-plugin/releases) page.
2. Place `TheIntroDB.dll` into your Emby plugins folder:
   - **Linux:** `/var/lib/emby/plugins/`
   - **Windows:** `C:\Users\{user}\AppData\Roaming\Emby-Server\plugins\`
   - **macOS:** `~/.config/emby-server/plugins/` or `/Library/Application Support/Emby-Server/plugins`
3. Restart Emby Server.
4. Configure the plugin at **Dashboard → Plugins → TheIntroDB**.
5. Run the scheduled task to populate data: **Dashboard → Scheduled Tasks → TheIntroDB Media Segment Scan** and click the **Play** button (▶).


### Metadata Requirements

**TVDB is recommended.** The plugin addresses media by TVDB id when one is available, then falls back to TMDB, then IMDb.

The series' **display order** is sent with every episode lookup, so a show set to DVD or absolute order is looked up by the numbers you actually have — not another order's. (Emby cannot express alternate/regional ordering, so those series cannot be represented; Jellyfin can.) The episode's own TVDB id is sent as well when it is distinct from the series id, which pins the exact episode record. A series with a non-default order and no TVDB id is skipped and logged once, because no other id can express the order.

IMDb and TMDB ids are less accurate for TV episodes: those requests are always read in that provider's numbering.

## Configuration

TheIntroDB plugin includes some configuration options to adjust and improve your experience.

- **API Key**: You can enter your TheIntroDB API key to fetch your submissions even if they're still pending and prioritize yours in the averaging calculation. It also moves requests to the signed-in rate limits (120 requests per 10 s, 100,000 lookups per day instead of 30 and 5,000), which is what lets a full library scan finish in one run.
- **Segment Toggles**: (Intro, Recap, Credits, and Preview — all enabled by default) You can disable each segment individually so they're not applied when fetching.
- **Ignore Media That Already Has Segments**: (Enabled by default) Prevent refetching of media that already has segments, until they are due for a refresh (see below). This is recommended for large libraries.
- **Refresh TheIntroDB Segments After (Days)**: (30 by default) Items with segments from TheIntroDB are fetched again after this many days, so corrected or newly accepted submissions arrive. The interval is spread per item (75–125 %), so the items of one scan do not all fall due together. 0 never refreshes.

## How Segments Appear in Emby

TheIntroDB provides four segment types: **Intro**, **Recap**, **Credits**, and **Preview**. The plugin writes them as chapter markers, and how each one shows up depends on what Emby's clients are built to render:

| Segment | What you'll see in Emby clients |
|---|---|
| **Intro** | Emby's native **"Skip Intro"** button appears while the intro is playing. (if you have Emby Premiere)* |
| **Credits** | Emby's **"Coming Up Next"** overlay appears at the credits start (for episodes that have a next episode), letting you jump straight to it. There is no literal "Skip Credits" button. (if you have Emby Premiere)* |
| **Recap** | A regular chapter (`Recap (TheIntroDB)` → `Recap End (TheIntroDB)`) you can jump to from the chapter list. Emby has no recap skip button. |
| **Preview** | A regular chapter (`Preview (TheIntroDB)` → `Preview End (TheIntroDB)`) you can jump to from the chapter list. Emby has no preview skip button. |

> **Why no skip button for recap and preview?** This is a limitation of Emby, not the plugin. Emby's player only renders skip UI for intro markers (`IntroStart`/`IntroEnd`) and credits markers (`CreditsStart`) — those marker types are hard-coded into Emby's client apps. There is no plugin API for adding buttons to the player UI, and no "recap" or "preview" marker type exists in Emby. Until Emby adds them, recap and preview segments are exposed as chapters for manual navigation.

* Emby Premiere is required to show the "Skip Intro" button, however, the segments are available to all users from the chapter list.

## Troubleshooting

It's recommended to disable Emby's internal intro marker detection in **Dashboard → Library → select library → Advanced/options → Disable "Generate intro video markers"**.

![Disable Intro Markers](images/disable-intro-markers.png)

**Files without embedded chapters:** Emby can overwrite TheIntroDB markers on files that contain no embedded chapters. By default Emby generates placeholder chapters (~every 5 minutes) for such files during library scans, and that write replaces the whole chapter list — including TheIntroDB markers. Files with embedded chapters are unaffected because Emby skips generation for them. To prevent the overwrite entirely, uncheck **"Generate chapters for videos that don't contain embedded chapter information"** in the same library settings.

As a safety net, the plugin automatically restores markers it has stored whenever they go missing (see **Marker repair interval** in the plugin settings, default every 12 hours), so markers recover on their own even if Emby overwrites them.

Thumbnail extraction is also recommended and can be done from **Dashboard → Library → select library → Advanced/options → Enable "Video preview thumbnails"**.

![Enable Video Preview Thumbnails](images/video-preview-thumbnails.png)

And by running the scheduled task again.

![Run Scheduled Task](images/thumbnail-scan.png)


---

## Preview

![Preview](images/preview.png)

---

## Development

### Prerequisites

- [.NET 6.0 SDK](https://dotnet.microsoft.com/download/dotnet/6.0)

### Build Commands

```bash
dotnet build
```

### Quick Test Loop

1. Build: `dotnet build`
2. Copy the DLL: `cp TheIntroDB/bin/Debug/netstandard2.0/TheIntroDB.dll /var/lib/emby/plugins/` (adjust path for your OS)
3. Restart Emby Server.

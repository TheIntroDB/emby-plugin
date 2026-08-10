using MediaBrowser.Model.Plugins;

namespace TheIntroDB.Configuration
{
    /// <summary>
    /// Plugin configuration for TheIntroDB integration.
    /// </summary>
    public class PluginConfiguration : BasePluginConfiguration
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
        /// </summary>
        public PluginConfiguration()
        {
            SchemaVersion = 1;
            ApiKey = string.Empty;
            SelectedLibraryIds = string.Empty;
            SelectedShowIds = string.Empty;
            EnableIntro = true;
            EnableRecap = true;
            EnableCredits = true;
            EnablePreview = true;
            IgnoreMediaWithExistingSegments = true;
            EnableAnonymousUsageReporting = true;
            EnableFileLogging = false;
        }

        public int SchemaVersion { get; set; }

        /// <summary>
        /// Gets or sets the optional API key for TheIntroDB (Bearer token).
        /// When set, your pending and accepted submissions are weighted higher.
        /// </summary>
        public string ApiKey { get; set; }

        /// <summary>
        /// Gets or sets the comma-separated list of library folder GUIDs to restrict scanning to.
        /// </summary>
        public string SelectedLibraryIds { get; set; }

        /// <summary>
        /// Gets or sets the comma-separated list of Series or Movie GUIDs to restrict scanning to.
        /// When a Series is included, all its episodes are scanned. Individual movies can also be selected.
        /// </summary>
        public string SelectedShowIds { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to provide intro segments from TheIntroDB.
        /// </summary>
        public bool EnableIntro { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to provide recap segments from TheIntroDB.
        /// </summary>
        public bool EnableRecap { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to provide credits (outro) segments from TheIntroDB.
        /// </summary>
        public bool EnableCredits { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to provide preview segments from TheIntroDB.
        /// </summary>
        public bool EnablePreview { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to skip API requests for media that already has segments.
        /// When enabled (default), items with existing segments are not refetched from TheIntroDB when the host exposes that state.
        /// </summary>
        public bool IgnoreMediaWithExistingSegments { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether anonymous usage reporting is enabled.
        /// </summary>
        public bool EnableAnonymousUsageReporting { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether plugin logs are written to a separate file
        /// instead of the main Emby log.
        /// </summary>
        public bool EnableFileLogging { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to automatically fetch segments for newly added items and on playback start.
        /// </summary>
        public bool EnableOnDemandFetch { get; set; } = true;

        /// <summary>
        /// Gets or sets how often (in hours) the plugin checks items it has segments for
        /// and restores TheIntroDB markers that Emby may have overwritten (e.g. when Emby
        /// regenerates placeholder chapters for files without embedded chapters).
        /// 0 disables the automatic repair pass.
        /// </summary>
        public int MarkerRepairIntervalHours { get; set; } = 12;
    }
}

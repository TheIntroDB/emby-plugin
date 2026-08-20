using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using TheIntroDB.Configuration;
using TheIntroDB.Services;

namespace TheIntroDB
{
    public class Plugin : BasePlugin<PluginConfiguration>, IHasThumbImage, IHasWebPages
    {
        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer, ILogManager logManager)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
            var embyLogger = logManager.GetLogger("TheIntroDB");
            FileLogger = new PluginLogger(applicationPaths.DataPath, embyLogger);
            _logger = FileLogger;
        }

        private readonly ILogger _logger;

        public override string Name => "TheIntroDB";

        public override Guid Id => Guid.Parse("424b8e01-03d2-40a1-ba58-a2b9306f115d");

        public static Plugin Instance { get; private set; }

        public PluginLogger FileLogger { get; private set; }

        internal static DateTime RateLimitExpiryUtc { get; set; }

        internal void InitConfig()
        {
            PluginConfiguration config;
            try
            {
                config = Configuration;
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to get plugin configuration: {0}", ex.Message);
                return;
            }

            if (config == null)
            {
                return;
            }

            if (config.SchemaVersion > 0)
            {
                return;
            }

            if (config.EnableIntro || config.EnableRecap || config.EnableCredits || config.EnablePreview || config.IgnoreMediaWithExistingSegments || config.EnableAnonymousUsageReporting)
            {
                config.SchemaVersion = 1;
                try
                {
                    SaveConfiguration();
                }
                catch (Exception ex)
                {
                    _logger.Error("Failed to save configuration (schema upgrade): {0}", ex.Message);
                }
                return;
            }

            config.SchemaVersion = 1;
            config.ApiKey = config.ApiKey ?? string.Empty;
            config.EnableIntro = true;
            config.EnableRecap = true;
            config.EnableCredits = true;
            config.EnablePreview = true;
            config.IgnoreMediaWithExistingSegments = true;
            config.EnableAnonymousUsageReporting = true;

            try
            {
                SaveConfiguration();
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to save initial configuration: {0}", ex.Message);
            }
        }

        internal static void TrackUsage(string eventName, Dictionary<string, object> props)
        {
            var instance = Instance;
            if (instance == null)
            {
                return;
            }

            AnonymousUsageReporter.TrackEvent(instance, instance._logger, eventName, props);
        }

        internal static void TrackPluginLoaded()
        {
            var instance = Instance;
            if (instance == null)
            {
                return;
            }

            try
            {
                instance.InitConfig();
            }
            catch (Exception ex)
            {
                instance._logger.Error("InitConfig failed before tracking: {0}", ex.Message);
            }

            try
            {
                AnonymousUsageReporter.TrackPluginLoaded(instance, instance._logger);
            }
            catch (Exception ex)
            {
                instance._logger.Error("TrackPluginLoaded failed: {0}", ex.Message);
            }
        }

        public ImageFormat ThumbImageFormat => ImageFormat.Png;

        public Stream GetThumbImage()
        {
            var type = GetType();
            return type.Assembly.GetManifestResourceStream(type.Namespace + ".thumb.png");
        }

        public IEnumerable<PluginPageInfo> GetPages()
        {
            return new[]
            {
                new PluginPageInfo
                {
                    Name = "TheIntroDBConfigurationPage",
                    EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Configuration.configPage.html", GetType().Namespace),
                    EnableInMainMenu = true,
                    DisplayName = "TheIntroDB"
                },
                new PluginPageInfo
                {
                    Name = "TheIntroDBConfigurationPageJS",
                    EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Configuration.configPage.js", GetType().Namespace)
                },
            };
        }
    }
}

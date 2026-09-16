using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using MediaBrowser.Model.Logging;

namespace TheIntroDB.Services
{
    /// <summary>
    /// Remembers lookups the API definitively has no data for, so items known to
    /// be absent are not re-requested on every scan. Persisted to the plugin data
    /// folder; best-effort, so any I/O failure degrades to a memory-only cache.
    /// </summary>
    public sealed class TheIntroDbNotFoundCache
    {
        /// <summary>
        /// How long a not-found answer is trusted before the item is re-checked
        /// (new submissions may appear at any time).
        /// </summary>
        public static readonly TimeSpan DefaultTtl = TimeSpan.FromDays(14);

        private const int FlushThreshold = 500;

        private static readonly object FileLock = new object();

        private static readonly Lazy<TheIntroDbNotFoundCache> SharedInstance = new Lazy<TheIntroDbNotFoundCache>(() =>
        {
            var dataPath = Plugin.DataPath;
            var filePath = string.IsNullOrEmpty(dataPath)
                ? null
                : Path.Combine(dataPath, "theintrodb", "notfound-cache.json");
            return new TheIntroDbNotFoundCache(filePath, Plugin.Instance?.FileLogger);
        });

        private readonly ConcurrentDictionary<string, DateTime> _entries = new ConcurrentDictionary<string, DateTime>(StringComparer.Ordinal);
        private readonly string _filePath;
        private readonly ILogger _logger;
        private int _unsavedChanges;
        private bool _loaded;

        /// <summary>
        /// Initializes a new instance of the <see cref="TheIntroDbNotFoundCache"/> class.
        /// </summary>
        /// <param name="filePath">Path of the persistence file, or null for a memory-only cache.</param>
        /// <param name="logger">Logger used for persistence failures.</param>
        public TheIntroDbNotFoundCache(string filePath, ILogger logger)
        {
            _filePath = filePath;
            _logger = logger;
        }

        /// <summary>
        /// Gets the shared cache instance used by the segment provider and the config page.
        /// </summary>
        public static TheIntroDbNotFoundCache Instance => SharedInstance.Value;

        /// <summary>
        /// Removes all cached not-found answers, in memory and on disk.
        /// </summary>
        /// <returns>The number of entries that were cached.</returns>
        public int Clear()
        {
            lock (FileLock)
            {
                var cleared = _entries.Count;
                _entries.Clear();
                Interlocked.Exchange(ref _unsavedChanges, 0);

                if (!string.IsNullOrEmpty(_filePath) && File.Exists(_filePath))
                {
                    try
                    {
                        File.Delete(_filePath);
                    }
                    catch (Exception ex)
                    {
                        _logger.ErrorException(string.Format("Failed to delete TheIntroDB not-found cache file {0}", _filePath), ex);
                    }
                }

                return cleared;
            }
        }

        /// <summary>
        /// Returns true when the key currently holds a cached not-found answer.
        /// </summary>
        /// <param name="key">Lookup identity (e.g. "ep:tmdb:1396:1:1").</param>
        /// <returns>True when a not-expired entry exists.</returns>
        public bool TryGetHit(string key)
        {
            EnsureLoaded();
            DateTime expiryUtc;
            if (!_entries.TryGetValue(key, out expiryUtc))
            {
                return false;
            }

            if (DateTime.UtcNow < expiryUtc)
            {
                return true;
            }

            _entries.TryRemove(key, out _);
            MarkDirty();
            return false;
        }

        /// <summary>
        /// Records a not-found answer for the given lookup identity.
        /// </summary>
        /// <param name="key">Lookup identity (e.g. "ep:tmdb:1396:1:1").</param>
        public void RememberNotFound(string key)
        {
            EnsureLoaded();
            _entries[key] = DateTime.UtcNow.Add(DefaultTtl);
            MarkDirty();
        }

        /// <summary>
        /// Persists the cache to disk. A no-op when no file path was configured.
        /// </summary>
        public void Save()
        {
            if (string.IsNullOrEmpty(_filePath))
            {
                return;
            }

            lock (FileLock)
            {
                try
                {
                    var now = DateTime.UtcNow;
                    var liveEntries = _entries
                        .Where(pair => pair.Value > now)
                        .ToDictionary(pair => pair.Key, pair => pair.Value);
                    var tempPath = _filePath + ".tmp";
                    var json = JsonSerializer.Serialize(liveEntries);
                    File.WriteAllText(tempPath, json);
                    File.Copy(tempPath, _filePath, true);
                    File.Delete(tempPath);
                    Interlocked.Exchange(ref _unsavedChanges, 0);
                }
                catch (Exception ex)
                {
                    _logger.ErrorException(string.Format("Failed to save TheIntroDB not-found cache to {0}", _filePath), ex);
                }
            }
        }

        private void EnsureLoaded()
        {
            if (_loaded)
            {
                return;
            }

            lock (FileLock)
            {
                if (_loaded)
                {
                    return;
                }

                _loaded = true;
                if (string.IsNullOrEmpty(_filePath) || !File.Exists(_filePath))
                {
                    return;
                }

                try
                {
                    var json = File.ReadAllText(_filePath);
                    var persisted = JsonSerializer.Deserialize<Dictionary<string, DateTime>>(json);
                    if (persisted == null)
                    {
                        return;
                    }

                    var now = DateTime.UtcNow;
                    foreach (var pair in persisted)
                    {
                        if (pair.Value > now)
                        {
                            _entries[pair.Key] = pair.Value;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.ErrorException(string.Format("Failed to load TheIntroDB not-found cache from {0}", _filePath), ex);
                }
            }
        }

        private void MarkDirty()
        {
            if (Interlocked.Increment(ref _unsavedChanges) >= FlushThreshold)
            {
                Save();
            }
        }
    }
}

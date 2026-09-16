using System;
using System.IO;
using MediaBrowser.Model.Logging;
using Moq;
using TheIntroDB.Services;
using Xunit;

namespace TheIntroDB.Tests
{
    public class TheIntroDbNotFoundCacheTests
    {
        [Fact]
        public void RememberedKeyIsAHitAndPersistsAcrossInstances()
        {
            var path = GetTempCachePath();
            try
            {
                var cache = new TheIntroDbNotFoundCache(path, new Mock<ILogger>().Object);
                Assert.False(cache.TryGetHit("ep:tmdb:1396:1:1"));
                cache.RememberNotFound("ep:tmdb:1396:1:1");
                Assert.True(cache.TryGetHit("ep:tmdb:1396:1:1"));
                cache.Save();

                var reloaded = new TheIntroDbNotFoundCache(path, new Mock<ILogger>().Object);
                Assert.True(reloaded.TryGetHit("ep:tmdb:1396:1:1"));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void ExpiredPersistedEntriesAreDroppedOnLoad()
        {
            var path = GetTempCachePath();
            try
            {
                File.WriteAllText(path, "{\"mov:tmdb:603\":\"2000-01-01T00:00:00Z\"}");
                var cache = new TheIntroDbNotFoundCache(path, new Mock<ILogger>().Object);
                Assert.False(cache.TryGetHit("mov:tmdb:603"));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void MemoryOnlyCacheIgnoresSaveAndKeepsHits()
        {
            var cache = new TheIntroDbNotFoundCache(null, new Mock<ILogger>().Object);
            cache.RememberNotFound("ep:imdb:tt1234567:2:3");
            cache.Save();
            Assert.True(cache.TryGetHit("ep:imdb:tt1234567:2:3"));
        }

        [Fact]
        public void CorruptCacheFileDegradesToMemoryOnly()
        {
            var path = GetTempCachePath();
            try
            {
                File.WriteAllText(path, "{not valid json");
                var cache = new TheIntroDbNotFoundCache(path, new Mock<ILogger>().Object);
                cache.RememberNotFound("ep:tvdb:12345:1:1");
                Assert.True(cache.TryGetHit("ep:tvdb:12345:1:1"));
                cache.Save();
            }
            finally
            {
                File.Delete(path);
            }
        }

        private static string GetTempCachePath()
        {
            return Path.Combine(Path.GetTempPath(), "tidb-notfound-" + Path.GetRandomFileName() + ".json");
        }
    }
}

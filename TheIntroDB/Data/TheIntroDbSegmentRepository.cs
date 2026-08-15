using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Logging;
using SQLitePCL.pretty;
using TheIntroDB.Models;

namespace TheIntroDB.Data
{
    public sealed class TheIntroDbSegmentRepository : ITheIntroDbSegmentRepository, IDisposable
    {
        private readonly ILogger _logger;
        private readonly ReaderWriterLockSlim _lock;
        private readonly object _connectionLock = new object();
        private readonly string _dbFilePath;
        private IDatabaseConnection _connection;

        public TheIntroDbSegmentRepository(ILogger logger, IApplicationPaths applicationPaths)
        {
            _logger = logger;
            _lock = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);

            var dataDir = Path.Combine(applicationPaths.DataPath, "theintrodb");
            Directory.CreateDirectory(dataDir);
            _dbFilePath = Path.Combine(dataDir, "segments.db");

            Initialize();
        }

        public void Dispose()
        {
            lock (_connectionLock)
            {
                _lock.Dispose();
                _connection?.Dispose();
                _connection = null;
            }
        }

        public bool HasAllSegmentTypes(long itemInternalId, IReadOnlyCollection<MediaSegmentType> types)
        {
            if (types == null || types.Count == 0)
            {
                return true;
            }

            var stored = GetStoredSegmentTypes(itemInternalId);
            return types.All(stored.Contains);
        }

        public HashSet<MediaSegmentType> GetStoredSegmentTypes(long itemInternalId)
        {
            _lock.EnterReadLock();
            try
            {
                var set = new HashSet<MediaSegmentType>();
                var db = GetConnection();
                using (var stmt = db.PrepareStatement("SELECT DISTINCT SegmentType FROM MediaSegments WHERE ItemInternalId=@ItemInternalId"))
                {
                    BindInt64(stmt, "@ItemInternalId", itemInternalId);
                    while (stmt.MoveNext())
                    {
                        var row = stmt.Current;
                        set.Add((MediaSegmentType)row.GetInt(0));
                    }
                }

                return set;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        public IReadOnlyList<StoredMediaSegment> GetSegments(long itemInternalId)
        {
            _lock.EnterReadLock();
            try
            {
                var list = new List<StoredMediaSegment>();
                var db = GetConnection();
                using (var stmt = db.PrepareStatement(
                           "SELECT SegmentType, StartTicks, EndTicks FROM MediaSegments WHERE ItemInternalId=@ItemInternalId ORDER BY StartTicks ASC"))
                {
                    BindInt64(stmt, "@ItemInternalId", itemInternalId);
                    while (stmt.MoveNext())
                    {
                        var row = stmt.Current;
                        list.Add(new StoredMediaSegment
                        {
                            ItemInternalId = itemInternalId,
                            Type = (MediaSegmentType)row.GetInt(0),
                            StartTicks = row.GetInt64(1),
                            EndTicks = row.GetInt64(2)
                        });
                    }
                }

                return list;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        /// <summary>
        /// Returns the set of ItemInternalId values that already have
        /// at least one segment stored in the database.
        /// Used to pre-filter items before a scan when
        /// IgnoreMediaWithExistingSegments is enabled.
        /// </summary>
        public IReadOnlyList<long> GetAllSegmentedItemIds()
        {
            _lock.EnterReadLock();
            try
            {
                var ids = new List<long>();
                var db = GetConnection();
                using (var stmt = db.PrepareStatement("SELECT DISTINCT ItemInternalId FROM MediaSegments"))
                {
                    while (stmt.MoveNext())
                    {
                        var row = stmt.Current;
                        ids.Add(row.GetInt64(0));
                    }
                }
                return ids;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        public void ReplaceSegments(long itemInternalId, IReadOnlyList<StoredMediaSegment> segments, DateTime updatedUtc)
        {
            _lock.EnterWriteLock();
            try
            {
                var db = GetConnection();
                db.BeginTransaction(TransactionMode.Deferred);
                try
                {
                    using (var delete = db.PrepareStatement("DELETE FROM MediaSegments WHERE ItemInternalId=@ItemInternalId"))
                    {
                        BindInt64(delete, "@ItemInternalId", itemInternalId);
                        delete.MoveNext();
                    }

                    if (segments != null && segments.Count > 0)
                    {
                        using (var insert = db.PrepareStatement(
                                   "INSERT OR REPLACE INTO MediaSegments (ItemInternalId, SegmentType, StartTicks, EndTicks, UpdatedUtcTicks) VALUES (@ItemInternalId, @SegmentType, @StartTicks, @EndTicks, @UpdatedUtcTicks)"))
                        {
                            foreach (var s in segments)
                            {
                                BindInt64(insert, "@ItemInternalId", itemInternalId);
                                BindInt(insert, "@SegmentType", (int)s.Type);
                                BindInt64(insert, "@StartTicks", s.StartTicks);
                                BindInt64(insert, "@EndTicks", s.EndTicks);
                                BindInt64(insert, "@UpdatedUtcTicks", updatedUtc.Ticks);
                                insert.MoveNext();
                                insert.Reset();
                            }
                        }
                    }

                    db.CommitTransaction();
                }
                catch
                {
                    db.RollbackTransaction();
                    throw;
                }
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        private void Initialize()
        {
            var db = GetConnection();

            db.ExecuteAll(string.Join(";",
                "PRAGMA journal_mode=WAL",
                "PRAGMA synchronous=Normal",
                "CREATE TABLE IF NOT EXISTS MediaSegments (" +
                "ItemInternalId INTEGER NOT NULL," +
                "SegmentType INTEGER NOT NULL," +
                "StartTicks INTEGER NOT NULL," +
                "EndTicks INTEGER NOT NULL," +
                "UpdatedUtcTicks INTEGER NOT NULL," +
                "PRIMARY KEY (ItemInternalId, SegmentType, StartTicks, EndTicks)" +
                ")",
                "CREATE INDEX IF NOT EXISTS idx_MediaSegments_ItemInternalId ON MediaSegments(ItemInternalId)"
            ));

            _logger.Info("TheIntroDB segment DB ready at {0}", _dbFilePath);
        }

        private IDatabaseConnection GetConnection()
        {
            if (_connection != null)
            {
                return _connection;
            }

            lock (_connectionLock)
            {
                if (_connection != null)
                {
                    return _connection;
                }

                var flags = ConnectionFlags.Create | ConnectionFlags.ReadWrite | ConnectionFlags.PrivateCache | ConnectionFlags.NoMutex;
                _connection = SQLite3.Open(_dbFilePath, flags, null, false);
                return _connection;
            }
        }

        private static void BindInt64(IStatement stmt, string name, long value)
        {
            if (!stmt.BindParameters.TryGetValue(name, out var param))
            {
                throw new InvalidOperationException("Missing bind param " + name);
            }

            param.Bind(value);
        }

        private static void BindInt(IStatement stmt, string name, int value)
        {
            if (!stmt.BindParameters.TryGetValue(name, out var param))
            {
                throw new InvalidOperationException("Missing bind param " + name);
            }

            param.Bind(value);
        }
    }
}

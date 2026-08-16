using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using SQLitePCL.pretty;
using TheIntroDB.Models;

namespace TheIntroDB.Data
{
    public sealed class TheIntroDbSegmentRepository : ITheIntroDbSegmentRepository, IDisposable
    {
        private const string CreateOwnedChaptersTableSql =
            "CREATE TABLE IF NOT EXISTS OwnedChapters (" +
            "ItemInternalId INTEGER NOT NULL," +
            "MarkerType INTEGER NOT NULL," +
            "StartTicks INTEGER NOT NULL," +
            "Name TEXT NOT NULL," +
            "OwnerToken TEXT NOT NULL," +
            "UpdatedUtcTicks INTEGER NOT NULL," +
            "PRIMARY KEY (ItemInternalId, MarkerType, StartTicks, Name, OwnerToken)" +
            ")";
        private static readonly SemaphoreSlim DatabaseWriteLock = new SemaphoreSlim(1, 1);
        private readonly ILogger _logger;
        private readonly ReaderWriterLockSlim _lock;
        private readonly object _connectionLock = new object();
        private readonly string _dbFilePath;
        private readonly bool _readOnly;
        private IDatabaseConnection _connection;

        public TheIntroDbSegmentRepository(ILogger logger, IApplicationPaths applicationPaths)
            : this(logger, applicationPaths, false)
        {
        }

        public TheIntroDbSegmentRepository(ILogger logger, IApplicationPaths applicationPaths, bool readOnly)
        {
            _logger = logger;
            _lock = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);
            _readOnly = readOnly;

            var dataDir = Path.Combine(applicationPaths.DataPath, "theintrodb");
            _dbFilePath = Path.Combine(dataDir, "segments.db");

            if (!_readOnly)
            {
                Directory.CreateDirectory(dataDir);
                Initialize();
            }
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

        public IDisposable BeginReadOnlySession(CancellationToken cancellationToken)
        {
            if (!_readOnly)
            {
                throw new InvalidOperationException("Read-only sessions require a read-only repository.");
            }

            DatabaseWriteLock.Wait(cancellationToken);
            try
            {
                ResetConnection();
                return new ReadOnlySession(this);
            }
            catch
            {
                DatabaseWriteLock.Release();
                throw;
            }
        }

        private void EndReadOnlySession()
        {
            try
            {
                ResetConnection();
            }
            finally
            {
                DatabaseWriteLock.Release();
            }
        }

        private void ResetConnection()
        {
            lock (_connectionLock)
            {
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
                if (db == null)
                {
                    return set;
                }
                if (!TableHasColumn(db, "MediaSegments", "SegmentType"))
                {
                    return set;
                }
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
                if (db == null)
                {
                    return list;
                }
                if (!TableHasColumn(db, "MediaSegments", "SegmentType"))
                {
                    return list;
                }
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
                if (db == null)
                {
                    return ids;
                }
                if (!TableHasColumn(db, "MediaSegments", "ItemInternalId"))
                {
                    return ids;
                }
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
            EnsureWritable();
            DatabaseWriteLock.Wait();
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
                DatabaseWriteLock.Release();
            }
        }

        public IReadOnlyList<OwnedChapterMarker> GetOwnedChapters(long itemInternalId)
        {
            _lock.EnterReadLock();
            try
            {
                var chapters = new List<OwnedChapterMarker>();
                var db = GetConnection();
                if (db == null || !TableHasColumn(db, "OwnedChapters", "OwnerToken"))
                {
                    return chapters;
                }
                using (var stmt = db.PrepareStatement(
                           "SELECT MarkerType, StartTicks, Name, OwnerToken FROM OwnedChapters WHERE ItemInternalId=@ItemInternalId ORDER BY StartTicks ASC"))
                {
                    BindInt64(stmt, "@ItemInternalId", itemInternalId);
                    while (stmt.MoveNext())
                    {
                        var row = stmt.Current;
                        chapters.Add(new OwnedChapterMarker
                        {
                            ItemInternalId = itemInternalId,
                            MarkerType = (MarkerType)row.GetInt(0),
                            StartTicks = row.GetInt64(1),
                            Name = row.GetString(2),
                            OwnerToken = row.GetString(3)
                        });
                    }
                }

                return chapters;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        public void ReplaceOwnedChapters(long itemInternalId, IReadOnlyList<OwnedChapterMarker> chapters, DateTime updatedUtc)
        {
            EnsureWritable();
            DatabaseWriteLock.Wait();
            _lock.EnterWriteLock();
            try
            {
                var db = GetConnection();
                db.BeginTransaction(TransactionMode.Deferred);
                try
                {
                    using (var delete = db.PrepareStatement("DELETE FROM OwnedChapters WHERE ItemInternalId=@ItemInternalId"))
                    {
                        BindInt64(delete, "@ItemInternalId", itemInternalId);
                        delete.MoveNext();
                    }

                    if (chapters != null && chapters.Count > 0)
                    {
                        using (var insert = db.PrepareStatement(
                                   "INSERT OR REPLACE INTO OwnedChapters (ItemInternalId, MarkerType, StartTicks, Name, OwnerToken, UpdatedUtcTicks) VALUES (@ItemInternalId, @MarkerType, @StartTicks, @Name, @OwnerToken, @UpdatedUtcTicks)"))
                        {
                            foreach (var chapter in chapters)
                            {
                                BindInt64(insert, "@ItemInternalId", itemInternalId);
                                BindInt(insert, "@MarkerType", (int)chapter.MarkerType);
                                BindInt64(insert, "@StartTicks", chapter.StartTicks);
                                BindString(insert, "@Name", chapter.Name ?? string.Empty);
                                BindString(insert, "@OwnerToken", chapter.OwnerToken ?? string.Empty);
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
                DatabaseWriteLock.Release();
            }
        }

        private void Initialize()
        {
            DatabaseWriteLock.Wait();
            try
            {
                var db = GetConnection();

                db.ExecuteAll(string.Join(";",
                    "PRAGMA wal_checkpoint(TRUNCATE)",
                    "PRAGMA journal_mode=DELETE",
                    "PRAGMA synchronous=FULL",
                    "CREATE TABLE IF NOT EXISTS MediaSegments (" +
                    "ItemInternalId INTEGER NOT NULL," +
                    "SegmentType INTEGER NOT NULL," +
                    "StartTicks INTEGER NOT NULL," +
                    "EndTicks INTEGER NOT NULL," +
                    "UpdatedUtcTicks INTEGER NOT NULL," +
                    "PRIMARY KEY (ItemInternalId, SegmentType, StartTicks, EndTicks)" +
                    ")",
                    "CREATE INDEX IF NOT EXISTS idx_MediaSegments_ItemInternalId ON MediaSegments(ItemInternalId)",
                    CreateOwnedChaptersTableSql,
                    "CREATE INDEX IF NOT EXISTS idx_OwnedChapters_ItemInternalId ON OwnedChapters(ItemInternalId)"
                ));

                if (!TableHasColumn(db, "OwnedChapters", "OwnerToken"))
                {
                    db.ExecuteAll(string.Join(";",
                        "DROP TABLE IF EXISTS OwnedChapters",
                        CreateOwnedChaptersTableSql,
                        "CREATE INDEX IF NOT EXISTS idx_OwnedChapters_ItemInternalId ON OwnedChapters(ItemInternalId)"));
                    _logger.Warn("Discarded legacy TheIntroDB ownership rows without per-chapter tokens.");
                }

                _logger.Info("TheIntroDB segment DB ready at {0}", _dbFilePath);
            }
            finally
            {
                DatabaseWriteLock.Release();
            }
        }

        private static bool TableHasColumn(IDatabaseConnection db, string tableName, string columnName)
        {
            using (var stmt = db.PrepareStatement("PRAGMA table_info(" + tableName + ")"))
            {
                while (stmt.MoveNext())
                {
                    if (string.Equals(stmt.Current.GetString(1), columnName, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            return false;
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

                if (_readOnly)
                {
                    if (!File.Exists(_dbFilePath))
                    {
                        return null;
                    }

                    var walPath = _dbFilePath + "-wal";
                    if (File.Exists(walPath) && new FileInfo(walPath).Length > 0)
                    {
                        throw new InvalidOperationException("TheIntroDB preview requires a checkpointed segment database.");
                    }

                    var uri = new Uri(_dbFilePath).AbsoluteUri + "?immutable=1";
                    var readOnlyFlags = ConnectionFlags.ReadOnly | ConnectionFlags.Uri | ConnectionFlags.PrivateCache | ConnectionFlags.NoMutex;
                    _connection = SQLite3.Open(uri, readOnlyFlags, null, false);
                    return _connection;
                }

                var writeFlags = ConnectionFlags.Create | ConnectionFlags.ReadWrite | ConnectionFlags.PrivateCache | ConnectionFlags.NoMutex;
                _connection = SQLite3.Open(_dbFilePath, writeFlags, null, false);
                return _connection;
            }
        }

        private void EnsureWritable()
        {
            if (_readOnly)
            {
                throw new InvalidOperationException("TheIntroDB read-only repository does not allow writes.");
            }
        }

        private sealed class ReadOnlySession : IDisposable
        {
            private TheIntroDbSegmentRepository _owner;

            public ReadOnlySession(TheIntroDbSegmentRepository owner)
            {
                _owner = owner;
            }

            public void Dispose()
            {
                var owner = Interlocked.Exchange(ref _owner, null);
                owner?.EndReadOnlySession();
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

        private static void BindString(IStatement stmt, string name, string value)
        {
            if (!stmt.BindParameters.TryGetValue(name, out var param))
            {
                throw new InvalidOperationException("Missing bind param " + name);
            }

            param.Bind(value);
        }
    }
}

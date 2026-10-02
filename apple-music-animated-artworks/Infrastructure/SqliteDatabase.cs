using System;
using System.IO;
using Microsoft.Data.Sqlite;

namespace AnimatedArtworks.Infrastructure;

public sealed class SqliteDatabase
{
    private readonly string _connectionString;
    private volatile bool _isInitialized;

    public string FilePath { get; }
    public bool IsInitialized => _isInitialized;

    public SqliteDatabase(string filePath)
    {
        FilePath = filePath;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = filePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();
    }

    public void Initialize()
    {
        string? directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;

            CREATE TABLE IF NOT EXISTS artworks (
                apple_music_url   TEXT    NOT NULL PRIMARY KEY,
                artist            TEXT    NOT NULL,
                album             TEXT    NOT NULL,
                normalized_artist TEXT    NOT NULL,
                normalized_album  TEXT    NOT NULL,
                m3u8_url          TEXT,
                m3u8_url_tall     TEXT,
                is_animated       INTEGER NOT NULL,
                last_fetched      INTEGER NOT NULL,
                download_count    INTEGER NOT NULL DEFAULT 0,
                search_count      INTEGER NOT NULL DEFAULT 0
            ) WITHOUT ROWID;

            -- Covers the fuzzy artist/album match, which has to scan every entry.
            CREATE INDEX IF NOT EXISTS ix_artworks_fuzzy_match
                ON artworks (normalized_artist, normalized_album, is_animated, album);

            CREATE INDEX IF NOT EXISTS ix_artworks_animated_recent
                ON artworks (last_fetched DESC) WHERE is_animated = 1;

            CREATE INDEX IF NOT EXISTS ix_artworks_animated_m3u8_url
                ON artworks (m3u8_url) WHERE is_animated = 1;

            CREATE TABLE IF NOT EXISTS metadata_resolutions (
                key                      TEXT    NOT NULL PRIMARY KEY,
                artist                   TEXT    NOT NULL,
                album                    TEXT    NOT NULL,
                resolved_apple_music_url TEXT,
                status                   INTEGER NOT NULL,
                last_resolved            INTEGER NOT NULL
            ) WITHOUT ROWID;

            CREATE TABLE IF NOT EXISTS legacy_imports (
                name        TEXT    NOT NULL PRIMARY KEY,
                source_file TEXT    NOT NULL,
                entry_count INTEGER NOT NULL,
                imported_at INTEGER NOT NULL
            ) WITHOUT ROWID;
            """;
        command.ExecuteNonQuery();
    }

    public void MarkInitialized() => _isInitialized = true;

    public void TruncateWriteAheadLog()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
        command.ExecuteNonQuery();
    }

    public SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        try
        {
            connection.Open();

            // WAL keeps the database consistent with synchronous = NORMAL; only the last
            // few commits can be lost on power failure, in exchange for far fewer fsyncs.
            using var pragma = connection.CreateCommand();
            pragma.CommandText = "PRAGMA synchronous = NORMAL;";
            pragma.ExecuteNonQuery();

            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    public static long ToUnixMilliseconds(DateTime value)
    {
        // Timestamps without an explicit kind have always been written as UTC.
        DateTime utc = value.Kind == DateTimeKind.Local
            ? value.ToUniversalTime()
            : DateTime.SpecifyKind(value, DateTimeKind.Utc);

        return new DateTimeOffset(utc).ToUnixTimeMilliseconds();
    }

    public static DateTime FromUnixMilliseconds(long value)
    {
        return DateTimeOffset.FromUnixTimeMilliseconds(value).UtcDateTime;
    }
}

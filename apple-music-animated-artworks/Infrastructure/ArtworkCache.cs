using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Data.Sqlite;

namespace AnimatedArtworks.Infrastructure;

public sealed class ArtworkCache(SqliteDatabase database)
{
    private const string EntryColumns =
        "apple_music_url, artist, album, m3u8_url, m3u8_url_tall, last_fetched, download_count, search_count";

    private static readonly TimeSpan StatsTtl = TimeSpan.FromMinutes(1);

    private readonly Lock _statsLock = new();
    private ArtworkCacheStats? _stats;
    private DateTime _statsComputedAt = DateTime.MinValue;

    public ArtworkCacheEntry? GetByUrl(string appleMusicUrl)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {EntryColumns} FROM artworks WHERE apple_music_url = $url";
        command.Parameters.AddWithValue("$url", appleMusicUrl);

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadEntry(reader) : null;
    }

    public ArtworkCacheEntry? GetByArtistAndAlbum(string artist, string album)
    {
        string queryArtist = CacheKeyNormalizer.Normalize(artist);
        string queryAlbum = CacheKeyNormalizer.Normalize(album);

        if (string.IsNullOrEmpty(queryArtist) || string.IsNullOrEmpty(queryAlbum))
            return null;

        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {EntryColumns}
            FROM artworks
            WHERE apple_music_url = (
                SELECT apple_music_url
                FROM artworks
                WHERE (instr(normalized_artist, $artist) > 0 OR instr($artist, normalized_artist) > 0)
                  AND (instr(normalized_album, $album) > 0 OR instr($album, normalized_album) > 0)
                -- prefer existing m3u8-urls, then shorter album names, as they are more likely to be the
                -- original release instead of a special edition (e.g. "A Cappella Super Deluxe Version")
                ORDER BY is_animated DESC, length(album)
                LIMIT 1
            )
            """;
        command.Parameters.AddWithValue("$artist", queryArtist);
        command.Parameters.AddWithValue("$album", queryAlbum);

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadEntry(reader) : null;
    }

    public void IncrementDownloadCount(string m3U8Url)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE artworks
            SET download_count = download_count + 1
            WHERE apple_music_url = (
                SELECT apple_music_url FROM artworks WHERE is_animated = 1 AND m3u8_url = $url LIMIT 1
            )
            """;
        command.Parameters.AddWithValue("$url", m3U8Url);
        command.ExecuteNonQuery();
    }

    public void IncrementSearchCount(ArtworkCacheEntry cacheEntry)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE artworks SET search_count = search_count + 1 WHERE apple_music_url = $url";
        command.Parameters.AddWithValue("$url", cacheEntry.AppleMusicUrl);
        command.ExecuteNonQuery();
    }

    public void SaveEntry(ArtworkCacheEntry entry)
    {
        using var connection = database.OpenConnection();
        using var command = CreateUpsertCommand(connection, overwriteExisting: true);
        BindEntry(command, entry);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<ArtworkCacheEntry> GetRecentSearches(int limit = 12)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {EntryColumns}
            FROM artworks
            WHERE is_animated = 1
            ORDER BY last_fetched DESC
            LIMIT $limit
            """;
        command.Parameters.AddWithValue("$limit", limit);

        var entries = new List<ArtworkCacheEntry>(limit);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            entries.Add(ReadEntry(reader));
        }

        return entries;
    }

    public ArtworkCacheStats GetStats()
    {
        lock (_statsLock)
        {
            if (_stats is not null && DateTime.UtcNow - _statsComputedAt < StatsTtl)
            {
                return _stats;
            }

            using var connection = database.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT COALESCE(SUM(search_count), 0),
                       COALESCE(SUM(download_count), 0),
                       COUNT(*),
                       COALESCE(SUM(is_animated), 0)
                FROM artworks
                """;

            using var reader = command.ExecuteReader();
            reader.Read();

            _stats = new ArtworkCacheStats(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3));
            _statsComputedAt = DateTime.UtcNow;
            return _stats;
        }
    }

    internal static SqliteCommand CreateUpsertCommand(SqliteConnection connection, bool overwriteExisting)
    {
        var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO artworks (
                apple_music_url, artist, album, normalized_artist, normalized_album,
                m3u8_url, m3u8_url_tall, is_animated, last_fetched, download_count, search_count)
            VALUES (
                $url, $artist, $album, $normalizedArtist, $normalizedAlbum,
                $m3u8Url, $m3u8UrlTall, $isAnimated, $lastFetched, $downloadCount, $searchCount)
            {(overwriteExisting
                ? """
                  ON CONFLICT (apple_music_url) DO UPDATE SET
                      artist = excluded.artist,
                      album = excluded.album,
                      normalized_artist = excluded.normalized_artist,
                      normalized_album = excluded.normalized_album,
                      m3u8_url = excluded.m3u8_url,
                      m3u8_url_tall = excluded.m3u8_url_tall,
                      is_animated = excluded.is_animated,
                      last_fetched = excluded.last_fetched,
                      download_count = excluded.download_count,
                      search_count = excluded.search_count
                  """
                : "ON CONFLICT (apple_music_url) DO NOTHING")}
            """;

        foreach (string name in new[]
                 {
                     "$url", "$artist", "$album", "$normalizedArtist", "$normalizedAlbum", "$m3u8Url",
                     "$m3u8UrlTall", "$isAnimated", "$lastFetched", "$downloadCount", "$searchCount"
                 })
        {
            command.Parameters.Add(new SqliteParameter { ParameterName = name });
        }

        return command;
    }

    internal static void BindEntry(SqliteCommand command, ArtworkCacheEntry entry)
    {
        string artist = entry.Artist ?? string.Empty;
        string album = entry.Album ?? string.Empty;

        command.Parameters["$url"].Value = entry.AppleMusicUrl;
        command.Parameters["$artist"].Value = artist;
        command.Parameters["$album"].Value = album;
        command.Parameters["$normalizedArtist"].Value = CacheKeyNormalizer.Normalize(artist);
        command.Parameters["$normalizedAlbum"].Value = CacheKeyNormalizer.Normalize(album);
        command.Parameters["$m3u8Url"].Value = (object?)entry.M3u8Url ?? DBNull.Value;
        command.Parameters["$m3u8UrlTall"].Value = (object?)entry.M3u8UrlTall ?? DBNull.Value;
        command.Parameters["$isAnimated"].Value = CacheKeyNormalizer.HasAnimatedArtwork(entry.M3u8Url, entry.M3u8UrlTall) ? 1 : 0;
        command.Parameters["$lastFetched"].Value = SqliteDatabase.ToUnixMilliseconds(entry.LastFetched);
        command.Parameters["$downloadCount"].Value = entry.DownloadCount;
        command.Parameters["$searchCount"].Value = entry.SearchCount;
    }

    private static ArtworkCacheEntry ReadEntry(SqliteDataReader reader)
    {
        return new ArtworkCacheEntry(
            AppleMusicUrl: reader.GetString(0),
            Artist: reader.GetString(1),
            Album: reader.GetString(2),
            M3u8Url: reader.IsDBNull(3) ? null : reader.GetString(3),
            M3u8UrlTall: reader.IsDBNull(4) ? null : reader.GetString(4),
            LastFetched: SqliteDatabase.FromUnixMilliseconds(reader.GetInt64(5)),
            DownloadCount: reader.GetInt32(6),
            SearchCount: reader.GetInt32(7)
        );
    }
}

public sealed record ArtworkCacheStats(
    long TotalSearches,
    long TotalDownloads,
    long TotalCacheEntries,
    long TotalAnimatedEntries
);

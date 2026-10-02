using System;
using Microsoft.Data.Sqlite;

namespace AnimatedArtworks.Infrastructure;

public sealed class MetadataResolutionCache(SqliteDatabase database)
{
    public MetadataResolutionLookup GetLookup(string artist, string album, TimeSpan noMatchTtl)
    {
        string key = CacheKeyNormalizer.BuildMetadataKey(artist, album);

        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT status, resolved_apple_music_url, last_resolved FROM metadata_resolutions WHERE key = $key";
        command.Parameters.AddWithValue("$key", key);

        MetadataResolutionStatus status;
        string? resolvedAppleMusicUrl;
        DateTime lastResolved;

        using (var reader = command.ExecuteReader())
        {
            if (!reader.Read())
            {
                return new(MetadataResolutionStatus.None);
            }

            status = (MetadataResolutionStatus)reader.GetInt32(0);
            resolvedAppleMusicUrl = reader.IsDBNull(1) ? null : reader.GetString(1);
            lastResolved = SqliteDatabase.FromUnixMilliseconds(reader.GetInt64(2));
        }

        if (status == MetadataResolutionStatus.NoMatch)
        {
            bool isFreshNoMatch = DateTime.UtcNow - lastResolved <= noMatchTtl;
            if (isFreshNoMatch)
            {
                return new(MetadataResolutionStatus.NoMatch);
            }

            Delete(connection, key);
            return new(MetadataResolutionStatus.None);
        }

        return new(MetadataResolutionStatus.Resolved, resolvedAppleMusicUrl);
    }

    public void SaveResolvedUrl(string artist, string album, string resolvedAppleMusicUrl)
    {
        Save(new MetadataResolutionEntry(
            Artist: artist,
            Album: album,
            ResolvedAppleMusicUrl: resolvedAppleMusicUrl,
            Status: MetadataResolutionStatus.Resolved,
            LastResolved: DateTime.UtcNow
        ));
    }

    public void SaveNoMatch(string artist, string album)
    {
        Save(new MetadataResolutionEntry(
            Artist: artist,
            Album: album,
            ResolvedAppleMusicUrl: null,
            Status: MetadataResolutionStatus.NoMatch,
            LastResolved: DateTime.UtcNow
        ));
    }

    public void RemoveResolvedUrl(string artist, string album)
    {
        using var connection = database.OpenConnection();
        Delete(connection, CacheKeyNormalizer.BuildMetadataKey(artist, album));
    }

    private void Save(MetadataResolutionEntry entry)
    {
        using var connection = database.OpenConnection();
        using var command = CreateUpsertCommand(connection, overwriteExisting: true);
        BindEntry(command, entry);
        command.ExecuteNonQuery();
    }

    private static void Delete(SqliteConnection connection, string key)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM metadata_resolutions WHERE key = $key";
        command.Parameters.AddWithValue("$key", key);
        command.ExecuteNonQuery();
    }

    internal static SqliteCommand CreateUpsertCommand(SqliteConnection connection, bool overwriteExisting)
    {
        var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO metadata_resolutions (key, artist, album, resolved_apple_music_url, status, last_resolved)
            VALUES ($key, $artist, $album, $resolvedUrl, $status, $lastResolved)
            {(overwriteExisting
                ? """
                  ON CONFLICT (key) DO UPDATE SET
                      artist = excluded.artist,
                      album = excluded.album,
                      resolved_apple_music_url = excluded.resolved_apple_music_url,
                      status = excluded.status,
                      last_resolved = excluded.last_resolved
                  """
                : "ON CONFLICT (key) DO NOTHING")}
            """;

        foreach (string name in new[] { "$key", "$artist", "$album", "$resolvedUrl", "$status", "$lastResolved" })
        {
            command.Parameters.Add(new SqliteParameter { ParameterName = name });
        }

        return command;
    }

    internal static void BindEntry(SqliteCommand command, MetadataResolutionEntry entry)
    {
        command.Parameters["$key"].Value = CacheKeyNormalizer.BuildMetadataKey(entry.Artist, entry.Album);
        command.Parameters["$artist"].Value = entry.Artist ?? string.Empty;
        command.Parameters["$album"].Value = entry.Album ?? string.Empty;
        command.Parameters["$resolvedUrl"].Value = (object?)entry.ResolvedAppleMusicUrl ?? DBNull.Value;
        command.Parameters["$status"].Value = (int)entry.Status;
        command.Parameters["$lastResolved"].Value = SqliteDatabase.ToUnixMilliseconds(entry.LastResolved);
    }
}

public readonly record struct MetadataResolutionEntry(
    string Artist,
    string Album,
    string? ResolvedAppleMusicUrl,
    MetadataResolutionStatus Status,
    DateTime LastResolved
);

public readonly record struct MetadataResolutionLookup(
    MetadataResolutionStatus Status,
    string? ResolvedAppleMusicUrl = null
);

public enum MetadataResolutionStatus
{
    None,
    Resolved,
    NoMatch
}

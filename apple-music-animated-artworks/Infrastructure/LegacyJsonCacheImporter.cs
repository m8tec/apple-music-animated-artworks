using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace AnimatedArtworks.Infrastructure;

/// <summary>
/// One-time import of the former JSON cache files into SQLite. Files are streamed entry by entry,
/// so the import works for files that are far larger than the available memory.
/// </summary>
public sealed class LegacyJsonCacheImporter(
    SqliteDatabase database,
    LegacyJsonCachePaths paths,
    ILogger<LegacyJsonCacheImporter> logger)
{
    private const string ArtworksImportName = "artworks";
    private const string MetadataResolutionsImportName = "metadata_resolutions";

    public async Task ImportAsync(CancellationToken cancellationToken)
    {
        await ImportAsync<ArtworkCacheEntry>(
            ArtworksImportName,
            paths.ArtworkCacheFilePath,
            connection => ArtworkCache.CreateUpsertCommand(connection, overwriteExisting: false),
            (command, entry) =>
            {
                if (string.IsNullOrWhiteSpace(entry.AppleMusicUrl))
                {
                    return false;
                }

                ArtworkCache.BindEntry(command, entry);
                return true;
            },
            cancellationToken).ConfigureAwait(false);

        await ImportAsync<MetadataResolutionEntry>(
            MetadataResolutionsImportName,
            paths.MetadataResolutionCacheFilePath,
            connection => MetadataResolutionCache.CreateUpsertCommand(connection, overwriteExisting: false),
            (command, entry) =>
            {
                if (entry.Status == MetadataResolutionStatus.None)
                {
                    return false;
                }

                MetadataResolutionCache.BindEntry(command, entry);
                return true;
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task ImportAsync<T>(
        string importName,
        string filePath,
        Func<SqliteConnection, SqliteCommand> createCommand,
        Func<SqliteCommand, T, bool> bindEntry,
        CancellationToken cancellationToken)
    {
        using var connection = database.OpenConnection();

        if (IsAlreadyImported(connection, importName))
        {
            return;
        }

        foreach (string candidate in new[] { filePath, filePath + ".bak" })
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            logger.LogInformation("Importing legacy JSON cache {File} into SQLite...", candidate);
            var stopwatch = Stopwatch.StartNew();

            try
            {
                long count = await ImportFileAsync(connection, importName, candidate, createCommand, bindEntry, cancellationToken).ConfigureAwait(false);
                logger.LogInformation("Imported {Count} entries from {File} in {Elapsed}.", count, candidate, stopwatch.Elapsed);
                MarkFileAsMigrated(candidate);
                return;
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                // The transaction was rolled back, so a corrupt main file leaves nothing behind
                // and the backup file can be imported instead.
                logger.LogWarning(ex, "Legacy JSON cache {File} could not be imported.", candidate);
            }
        }
    }

    private static async Task<long> ImportFileAsync<T>(
        SqliteConnection connection,
        string importName,
        string filePath,
        Func<SqliteConnection, SqliteCommand> createCommand,
        Func<SqliteCommand, T, bool> bindEntry,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1 << 16, useAsync: true);

        using var transaction = connection.BeginTransaction();
        using var command = createCommand(connection);
        command.Transaction = transaction;

        long count = 0;
        await foreach (T? entry in JsonSerializer.DeserializeAsyncEnumerable<T>(stream, cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            if (entry is null || !bindEntry(command, entry))
            {
                continue;
            }

            command.ExecuteNonQuery();
            count++;
        }

        using (var marker = connection.CreateCommand())
        {
            marker.Transaction = transaction;
            marker.CommandText = """
                INSERT INTO legacy_imports (name, source_file, entry_count, imported_at)
                VALUES ($name, $sourceFile, $entryCount, $importedAt)
                """;
            marker.Parameters.AddWithValue("$name", importName);
            marker.Parameters.AddWithValue("$sourceFile", filePath);
            marker.Parameters.AddWithValue("$entryCount", count);
            marker.Parameters.AddWithValue("$importedAt", SqliteDatabase.ToUnixMilliseconds(DateTime.UtcNow));
            marker.ExecuteNonQuery();
        }

        transaction.Commit();
        return count;
    }

    private static bool IsAlreadyImported(SqliteConnection connection, string importName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM legacy_imports WHERE name = $name";
        command.Parameters.AddWithValue("$name", importName);
        return command.ExecuteScalar() is not null;
    }

    private void MarkFileAsMigrated(string filePath)
    {
        // The import is tracked in the database; renaming only signals that the file is no longer used.
        try
        {
            File.Move(filePath, filePath + ".migrated", overwrite: true);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not rename migrated legacy JSON cache {File}.", filePath);
        }
    }
}

public sealed record LegacyJsonCachePaths(string ArtworkCacheFilePath, string MetadataResolutionCacheFilePath);

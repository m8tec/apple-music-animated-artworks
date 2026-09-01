using System;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AnimatedArtworks.Infrastructure;

public static class AtomicJsonFileStore
{
    public static string ReadTextWithBackup(string filePath)
    {
        string? text = TryReadAllText(filePath);
        if (!string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        string backupPath = GetBackupPath(filePath);
        text = TryReadAllText(backupPath);
        if (!string.IsNullOrWhiteSpace(text))
        {
            TryWriteAllText(filePath, text);
            return text;
        }

        return string.Empty;
    }

    public static async Task<T?> ReadAndDeserializeWithBackupAsync<T>(string filePath, CancellationToken cancellationToken = default)
    {
        T? value = await TryReadAndDeserializeAsync<T>(filePath, cancellationToken).ConfigureAwait(false);
        if (value is not null)
        {
            return value;
        }

        string backupPath = GetBackupPath(filePath);
        value = await TryReadAndDeserializeAsync<T>(backupPath, cancellationToken).ConfigureAwait(false);
        if (value is null)
        {
            return default;
        }

        await RestoreBackupAsync(filePath, backupPath, cancellationToken).ConfigureAwait(false);
        return value;
    }

    public static async Task WriteAtomicallyAsync<T>(string filePath, T data, CancellationToken cancellationToken = default)
    {
        string directory = Path.GetDirectoryName(filePath) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string tempPath = $"{filePath}.{Guid.NewGuid():N}.tmp";
        string backupPath = GetBackupPath(filePath);

        try
        {
            await using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 4096, useAsync: true))
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };

                await JsonSerializer.SerializeAsync(stream, data, options, cancellationToken).ConfigureAwait(false);
            }

            if (File.Exists(filePath))
            {
                File.Replace(tempPath, filePath, backupPath, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tempPath, filePath);
                File.Copy(filePath, backupPath, overwrite: true);
            }
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    private static string? TryReadAllText(string filePath)
    {
        try
        {
            return File.Exists(filePath) ? File.ReadAllText(filePath) : null;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<T?> TryReadAndDeserializeAsync<T>(string filePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            return default;
        }

        try
        {
            await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 8192, useAsync: true);
            return await JsonSerializer.DeserializeAsync<T>(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return default;
        }
    }

    private static async Task RestoreBackupAsync(string filePath, string backupPath, CancellationToken cancellationToken)
    {
        try
        {
            string? directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using var destination = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read, bufferSize: 4096, useAsync: true);
            await using var source = new FileStream(backupPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true);
            await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Best effort restore only.
        }
    }

    private static void TryWriteAllText(string filePath, string content)
    {
        try
        {
            File.WriteAllText(filePath, content);
        }
        catch
        {
            // Best effort restore only.
        }
    }

    private static void TryDelete(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch
        {
            // Best effort cleanup only.
        }
    }

    private static string GetBackupPath(string filePath)
    {
        return filePath + ".bak";
    }
}
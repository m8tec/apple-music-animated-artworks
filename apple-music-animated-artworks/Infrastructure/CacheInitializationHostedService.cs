using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AnimatedArtworks.Infrastructure;

public sealed class CacheInitializationHostedService(
    SqliteDatabase database,
    LegacyJsonCacheImporter legacyImporter,
    IHostApplicationLifetime lifetime,
    ILogger<CacheInitializationHostedService> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Runs in the background so the server can already answer (with 503) during a long import.
        _ = Task.Run(() => InitializeAsync(lifetime.ApplicationStopping), CancellationToken.None);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        try
        {
            database.Initialize();
            await legacyImporter.ImportAsync(cancellationToken).ConfigureAwait(false);

            // A large import leaves a write-ahead log of the same size behind.
            database.TruncateWriteAheadLog();
            database.MarkInitialized();

            logger.LogInformation("Cache initialization completed. Database: {DatabaseFile}", database.FilePath);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("Cache initialization was cancelled during shutdown.");
        }
        catch (Exception ex)
        {
            // Without a cache every request would hit Apple Music, so exit instead of
            // staying up as a process that answers 503 forever.
            logger.LogCritical(ex, "Cache initialization failed. Shutting down.");
            Environment.ExitCode = 1;
            lifetime.StopApplication();
        }
    }
}

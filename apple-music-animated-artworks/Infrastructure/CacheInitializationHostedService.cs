using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AnimatedArtworks.Infrastructure;

public sealed class CacheInitializationHostedService(
    JsonCacheService jsonCache,
    MetadataResolutionCache metadataCache,
    ILogger<CacheInitializationHostedService> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = InitializeAsync(cancellationToken);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.WhenAll(
                jsonCache.InitializeAsync(cancellationToken).AsTask(),
                metadataCache.InitializeAsync(cancellationToken).AsTask()).ConfigureAwait(false);

            logger.LogInformation("Cache initialization completed.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("Cache initialization was cancelled during shutdown.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Cache initialization failed. API requests will remain unavailable.");
        }
    }
}

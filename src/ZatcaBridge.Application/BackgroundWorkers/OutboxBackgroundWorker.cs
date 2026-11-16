using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ZatcaBridge.Application.Services;

namespace ZatcaBridge.Application.BackgroundWorkers;

/// <summary>
/// Long-running background worker that periodically polls and dispatches pending outbox messages to ZATCA.
/// </summary>
public class OutboxBackgroundWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly OutboxWorkerOptions _options;
    private readonly ILogger<OutboxBackgroundWorker> _logger;

    public OutboxBackgroundWorker(
        IServiceScopeFactory scopeFactory,
        OutboxWorkerOptions? options = null,
        ILogger<OutboxBackgroundWorker>? logger = null)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _options = options ?? new OutboxWorkerOptions();
        _logger = logger ?? NullLogger<OutboxBackgroundWorker>.Instance;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.IsEnabled)
        {
            _logger.LogInformation("OutboxBackgroundWorker is disabled via configuration.");
            return;
        }

        _logger.LogInformation("OutboxBackgroundWorker started. Polling interval: {Interval}.", _options.PollingInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessOutboxBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Graceful cancellation requested
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception occurred during outbox batch processing cycle.");
            }

            try
            {
                await Task.Delay(_options.PollingInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("OutboxBackgroundWorker stopped gracefully.");
    }

    private async Task ProcessOutboxBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<OutboxProcessor>();
        var processedCount = await processor.ProcessPendingMessagesAsync(_options.BatchSize, cancellationToken);
        if (processedCount > 0)
        {
            _logger.LogInformation("OutboxBackgroundWorker successfully processed {Count} messages.", processedCount);
        }
    }
}

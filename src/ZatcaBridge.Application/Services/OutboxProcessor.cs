using System;
using System.Threading;
using System.Threading.Tasks;
using ZatcaBridge.Application.Common.Interfaces;
using ZatcaBridge.Application.Models;

namespace ZatcaBridge.Application.Services;

/// <summary>
/// Background processor enforcing sequential ICV dispatch, state machine transitions,
/// and resilient retry policies for ZATCA clearance and reporting outbox messages.
/// </summary>
public class OutboxProcessor
{
    private readonly IInvoiceOutboxRepository _outboxRepository;
    private readonly IZatcaGatewayClient _gatewayClient;
    private readonly int _maxRetries;

    public OutboxProcessor(
        IInvoiceOutboxRepository outboxRepository,
        IZatcaGatewayClient gatewayClient,
        int maxRetries = 5)
    {
        _outboxRepository = outboxRepository ?? throw new ArgumentNullException(nameof(outboxRepository));
        _gatewayClient = gatewayClient ?? throw new ArgumentNullException(nameof(gatewayClient));
        _maxRetries = maxRetries;
    }

    /// <summary>
    /// Processes a batch of pending outbox messages in strict sequential ICV order.
    /// </summary>
    /// <returns>The number of messages successfully processed and transitioned.</returns>
    public async Task<int> ProcessPendingMessagesAsync(int batchSize = 10, CancellationToken cancellationToken = default)
    {
        var messages = await _outboxRepository.GetPendingMessagesAsync(batchSize, cancellationToken);
        int processedCount = 0;

        foreach (var message in messages)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            await _outboxRepository.UpdateOutboxStatusAsync(message.Id, OutboxStatus.Processing, cancellationToken: cancellationToken);

            try
            {
                ZatcaGatewayResponse response = message.Type == OutboxMessageType.Clearance
                    ? await _gatewayClient.ClearInvoiceAsync(message.InvoiceHash, message.PayloadXml, cancellationToken)
                    : await _gatewayClient.ReportInvoiceAsync(message.InvoiceHash, message.PayloadXml, cancellationToken);

                if (response.IsSuccess)
                {
                    var targetStatus = message.Type == OutboxMessageType.Clearance
                        ? OutboxStatus.Cleared
                        : OutboxStatus.Reported;

                    await _outboxRepository.UpdateOutboxStatusAsync(message.Id, targetStatus, cancellationToken: cancellationToken);
                    processedCount++;
                }
                else if (response.IsRejected)
                {
                    // Permanent validation failure: mark as Rejected directly without retrying
                    await _outboxRepository.UpdateOutboxStatusAsync(
                        message.Id,
                        OutboxStatus.Rejected,
                        response.ErrorMessage ?? "Rejected by ZATCA",
                        cancellationToken
                    );
                }
                else
                {
                    // Transient failure: increment retry count; if max retries reached, fail permanently
                    var nextRetry = message.RetryCount + 1;
                    var finalStatus = nextRetry >= _maxRetries ? OutboxStatus.Failed : OutboxStatus.Pending;

                    await _outboxRepository.UpdateOutboxStatusAsync(
                        message.Id,
                        finalStatus,
                        response.ErrorMessage ?? "Transient ZATCA gateway error",
                        cancellationToken
                    );

                    // Halt batch processing to maintain cryptographic hash chain sequence integrity
                    break;
                }
            }
            catch (Exception ex)
            {
                var nextRetry = message.RetryCount + 1;
                var finalStatus = nextRetry >= _maxRetries ? OutboxStatus.Failed : OutboxStatus.Pending;

                await _outboxRepository.UpdateOutboxStatusAsync(
                    message.Id,
                    finalStatus,
                    ex.Message,
                    cancellationToken
                );

                // Halt batch processing on exception to maintain cryptographic chain sequence integrity
                break;
            }
        }

        return processedCount;
    }
}

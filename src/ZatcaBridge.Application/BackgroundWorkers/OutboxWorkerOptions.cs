using System;

namespace ZatcaBridge.Application.BackgroundWorkers;

/// <summary>
/// Configuration options for the background outbox worker.
/// </summary>
public class OutboxWorkerOptions
{
    public const string SectionName = "OutboxWorker";

    /// <summary>
    /// Indicates whether the outbox background service is enabled. Defaults to true.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Polling interval between outbox dispatch batch executions. Defaults to 5 seconds.
    /// </summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Maximum number of messages to process in a single batch. Defaults to 10.
    /// </summary>
    public int BatchSize { get; set; } = 10;
}

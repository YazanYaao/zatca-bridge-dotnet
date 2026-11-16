using System;
using Microsoft.Extensions.DependencyInjection;
using ZatcaBridge.Application.BackgroundWorkers;
using ZatcaBridge.Application.Invoices.Commands;
using ZatcaBridge.Application.Services;

namespace ZatcaBridge.Application.Extensions;

/// <summary>
/// Dependency Injection extensions for configuring ZatcaBridge Application layer services.
/// </summary>
public static class ZatcaApplicationServiceCollectionExtensions
{
    /// <summary>
    /// Adds ZatcaBridge application use cases, command handlers, outbox processor, and hosted background worker.
    /// </summary>
    public static IServiceCollection AddZatcaApplication(
        this IServiceCollection services,
        Action<OutboxWorkerOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new OutboxWorkerOptions();
        configureOptions?.Invoke(options);
        services.AddSingleton(options);

        services.AddScoped<IssueInvoiceHandler>();
        services.AddScoped<OutboxProcessor>();

        if (options.IsEnabled)
        {
            services.AddHostedService<OutboxBackgroundWorker>();
        }

        return services;
    }
}

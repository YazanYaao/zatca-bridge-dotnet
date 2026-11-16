using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ZatcaBridge.Application.Common.Interfaces;
using ZatcaBridge.Infrastructure.Cryptography;
using ZatcaBridge.Infrastructure.Gateway;
using ZatcaBridge.Infrastructure.Persistence;
using ZatcaBridge.Infrastructure.Persistence.Repositories;
using ZatcaBridge.Infrastructure.Xml;

namespace ZatcaBridge.Infrastructure.Extensions;

/// <summary>
/// Dependency Injection extensions for configuring ZatcaBridge Infrastructure layer services.
/// </summary>
public static class ZatcaInfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Registers EF Core DbContext, repositories, XML generator, and ZATCA HTTP gateway client.
    /// </summary>
    public static IServiceCollection AddZatcaInfrastructure(
        this IServiceCollection services,
        string? connectionString = null,
        Action<ZatcaGatewayOptions>? configureGateway = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // 1. Database Context
        var conn = connectionString ?? "Data Source=zatca_bridge.db";
        services.AddDbContext<ZatcaDbContext>(options =>
        {
            options.UseSqlite(conn);
        });

        // 2. Repositories (EfInvoiceOutboxRepository implements both IInvoiceOutboxRepository and IHashChainRepository)
        services.AddScoped<EfInvoiceOutboxRepository>();
        services.AddScoped<IInvoiceOutboxRepository>(sp => sp.GetRequiredService<EfInvoiceOutboxRepository>());
        services.AddScoped<IHashChainRepository>(sp => sp.GetRequiredService<EfInvoiceOutboxRepository>());
        services.AddScoped<ICsidRepository, EfCsidRepository>();

        // 3. XML Serializer & Cryptographic Services
        services.AddSingleton<IZatcaXmlGenerator, UblXmlGenerator>();
        services.AddSingleton<IZatcaCsrGenerator, ZatcaCsrGenerator>();

        // 4. ZATCA Gateway Options & Typed HTTP Client
        var gatewayOptions = new ZatcaGatewayOptions();
        configureGateway?.Invoke(gatewayOptions);
        services.AddSingleton(gatewayOptions);

        services.AddHttpClient<IZatcaGatewayClient, ZatcaHttpGatewayClient>(client =>
        {
            if (Uri.TryCreate(gatewayOptions.BaseUrl, UriKind.Absolute, out var baseUri))
            {
                client.BaseAddress = baseUri;
            }
        });

        return services;
    }
}

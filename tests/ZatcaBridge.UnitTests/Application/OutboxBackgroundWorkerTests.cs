using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using ZatcaBridge.Application.BackgroundWorkers;
using ZatcaBridge.Application.Common.Interfaces;
using ZatcaBridge.Application.Extensions;
using ZatcaBridge.Application.Invoices.Commands;
using ZatcaBridge.Application.Models;
using ZatcaBridge.Application.Services;
using ZatcaBridge.Domain.Entities;
using ZatcaBridge.Domain.ValueObjects;
using ZatcaBridge.Infrastructure.Extensions;
using ZatcaBridge.Infrastructure.Gateway;
using ZatcaBridge.Infrastructure.Persistence;
using ZatcaBridge.Infrastructure.Xml;

namespace ZatcaBridge.UnitTests.Application;

public class OutboxBackgroundWorkerTests
{
    private sealed class CountingOutboxRepository : IInvoiceOutboxRepository
    {
        public int GetPendingCallCount { get; private set; }
        public List<OutboxMessage> PendingMessages { get; } = new();
        public TaskCompletionSource<int> SecondCallTcs { get; } = new();

        public Task SaveInvoiceWithOutboxAsync(Invoice invoice, HashChainRecord newChainRecord, OutboxMessage outboxMessage, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<OutboxMessage>> GetPendingMessagesAsync(int batchSize = 10, CancellationToken cancellationToken = default)
        {
            GetPendingCallCount++;
            if (GetPendingCallCount >= 2)
            {
                SecondCallTcs.TrySetResult(GetPendingCallCount);
            }
            return Task.FromResult<IReadOnlyList<OutboxMessage>>(PendingMessages);
        }

        public Task UpdateOutboxStatusAsync(Guid messageId, OutboxStatus status, string? errorMessage = null, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class StubGatewayClient : IZatcaGatewayClient
    {
        public Task<ZatcaGatewayResponse> ClearInvoiceAsync(string invoiceHash, string ublXml, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ZatcaGatewayResponse(IsSuccess: true, IsRejected: false));

        public Task<ZatcaGatewayResponse> ReportInvoiceAsync(string invoiceHash, string ublXml, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ZatcaGatewayResponse(IsSuccess: true, IsRejected: false));
    }

    private sealed class StubHashChainRepository : IHashChainRepository
    {
        public Task<HashChainRecord?> GetLatestRecordAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<HashChainRecord?>(null);
    }

    private sealed class StubXmlGenerator : IZatcaXmlGenerator
    {
        public string GenerateXml(Invoice invoice, string? qrCodeBase64 = null) => "<Invoice/>";
        public byte[] GenerateXmlBytes(Invoice invoice, string? qrCodeBase64 = null) => Array.Empty<byte>();
    }

    [Fact]
    public async Task ExecuteAsync_ExecutesPeriodicallyAndCallsOutboxProcessor()
    {
        // Arrange
        var services = new ServiceCollection();
        var repo = new CountingOutboxRepository();
        services.AddSingleton<IInvoiceOutboxRepository>(repo);
        services.AddSingleton<IZatcaGatewayClient>(new StubGatewayClient());
        services.AddScoped<OutboxProcessor>();
        var serviceProvider = services.BuildServiceProvider();

        var options = new OutboxWorkerOptions
        {
            PollingInterval = TimeSpan.FromMilliseconds(10),
            BatchSize = 5
        };

        var worker = new OutboxBackgroundWorker(serviceProvider.GetRequiredService<IServiceScopeFactory>(), options);

        // Act: Start worker and wait for at least 2 cycles
        await worker.StartAsync(CancellationToken.None);
        var completedTask = await Task.WhenAny(repo.SecondCallTcs.Task, Task.Delay(2000));
        await worker.StopAsync(CancellationToken.None);

        // Assert: Outbox repository was queried at least 2 times
        Assert.Same(repo.SecondCallTcs.Task, completedTask);
        Assert.True(repo.GetPendingCallCount >= 2, $"Expected at least 2 cycles, got {repo.GetPendingCallCount}");
    }

    [Fact]
    public async Task ExecuteAsync_CancellationRequested_TerminatesGracefullyWithoutThrowing()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<IInvoiceOutboxRepository>(new CountingOutboxRepository());
        services.AddSingleton<IZatcaGatewayClient>(new StubGatewayClient());
        services.AddScoped<OutboxProcessor>();
        var serviceProvider = services.BuildServiceProvider();

        var options = new OutboxWorkerOptions
        {
            PollingInterval = TimeSpan.FromMilliseconds(50)
        };

        var worker = new OutboxBackgroundWorker(serviceProvider.GetRequiredService<IServiceScopeFactory>(), options);

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Pre-cancelled

        // Act & Assert: Must not throw unhandled exception
        await worker.StartAsync(cts.Token);
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_ProcessorThrowsException_CatchesAndContinuesNextCycleWithoutCrashing()
    {
        // Arrange
        var services = new ServiceCollection();
        int attempts = 0;

        // Repository that throws on first attempt and succeeds on second
        var throwingRepo = new ThrowingThenSucceedingRepo(() => attempts++);
        services.AddSingleton<IInvoiceOutboxRepository>(throwingRepo);
        services.AddSingleton<IZatcaGatewayClient>(new StubGatewayClient());
        services.AddScoped<OutboxProcessor>();
        var serviceProvider = services.BuildServiceProvider();

        var options = new OutboxWorkerOptions
        {
            PollingInterval = TimeSpan.FromMilliseconds(10)
        };

        var worker = new OutboxBackgroundWorker(serviceProvider.GetRequiredService<IServiceScopeFactory>(), options);

        // Act: Start and wait for second attempt despite first failure
        await worker.StartAsync(CancellationToken.None);
        var completedTask = await Task.WhenAny(throwingRepo.SecondAttemptTcs.Task, Task.Delay(5000));
        await worker.StopAsync(CancellationToken.None);

        // Assert: Attempted at least 2 times despite the first failure
        Assert.Same(throwingRepo.SecondAttemptTcs.Task, completedTask);
        Assert.True(attempts >= 2, $"Expected at least 2 attempts despite exception, got {attempts}");
    }

    private sealed class ThrowingThenSucceedingRepo : IInvoiceOutboxRepository
    {
        private readonly Action _onCall;
        public TaskCompletionSource<bool> SecondAttemptTcs { get; } = new();
        public ThrowingThenSucceedingRepo(Action onCall) => _onCall = onCall;

        public Task SaveInvoiceWithOutboxAsync(Invoice invoice, HashChainRecord newChainRecord, OutboxMessage outboxMessage, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<OutboxMessage>> GetPendingMessagesAsync(int batchSize = 10, CancellationToken cancellationToken = default)
        {
            _onCall();
            if (_hasThrown)
            {
                SecondAttemptTcs.TrySetResult(true);
                return Task.FromResult<IReadOnlyList<OutboxMessage>>(Array.Empty<OutboxMessage>());
            }
            _hasThrown = true;
            throw new InvalidOperationException("Simulated transient database connection drop");
        }

        private bool _hasThrown;

        public Task UpdateOutboxStatusAsync(Guid messageId, OutboxStatus status, string? errorMessage = null, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    [Fact]
    public void AddZatcaApplication_ResolvesRequiredServicesAndWorker()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddZatcaApplication();

        // Provide stubs for external dependencies required by handlers/services
        services.AddSingleton<IInvoiceOutboxRepository>(new CountingOutboxRepository());
        services.AddSingleton<IHashChainRepository>(new StubHashChainRepository());
        services.AddSingleton<IZatcaXmlGenerator>(new StubXmlGenerator());
        services.AddSingleton<IZatcaGatewayClient>(new StubGatewayClient());

        var provider = services.BuildServiceProvider();

        // Assert: Core application services resolve
        using var scope = provider.CreateScope();
        var issueHandler = scope.ServiceProvider.GetService<IssueInvoiceHandler>();
        Assert.NotNull(issueHandler);

        var outboxProcessor = scope.ServiceProvider.GetService<OutboxProcessor>();
        Assert.NotNull(outboxProcessor);

        var hostedServices = provider.GetServices<IHostedService>().ToList();
        Assert.Contains(hostedServices, s => s is OutboxBackgroundWorker);
    }

    [Fact]
    public void AddZatcaApplication_WhenDisabled_DoesNotRegisterHostedService()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddZatcaApplication(opt => opt.IsEnabled = false);

        var provider = services.BuildServiceProvider();

        // Assert
        var hostedServices = provider.GetServices<IHostedService>().ToList();
        Assert.DoesNotContain(hostedServices, s => s is OutboxBackgroundWorker);
    }

    [Fact]
    public void AddZatcaInfrastructure_ResolvesRepositoriesAndGateway()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddZatcaInfrastructure("Data Source=test_infradi.db", opt =>
        {
            opt.BaseUrl = "https://gw-fatoora.zatca.gov.sa/e-invoicing/simulation/";
        });

        var provider = services.BuildServiceProvider();

        // Assert: Database & Repositories
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetService<ZatcaDbContext>();
        Assert.NotNull(dbContext);

        var outboxRepo = scope.ServiceProvider.GetService<IInvoiceOutboxRepository>();
        Assert.NotNull(outboxRepo);

        var hashChainRepo = scope.ServiceProvider.GetService<IHashChainRepository>();
        Assert.NotNull(hashChainRepo);

        var xmlGen = provider.GetService<IZatcaXmlGenerator>();
        Assert.NotNull(xmlGen);
        Assert.IsType<UblXmlGenerator>(xmlGen);

        var gatewayClient = scope.ServiceProvider.GetService<IZatcaGatewayClient>();
        Assert.NotNull(gatewayClient);
        Assert.IsType<ZatcaHttpGatewayClient>(gatewayClient);
    }
}

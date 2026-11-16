using System;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;
using ZatcaBridge.Domain.Cryptography;
using ZatcaBridge.Infrastructure.Persistence;
using ZatcaBridge.Infrastructure.Persistence.Repositories;

namespace ZatcaBridge.UnitTests.Infrastructure;

public class EfCsidRepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ZatcaDbContext _context;
    private readonly EfCsidRepository _repository;

    public EfCsidRepositoryTests()
    {
        // Use an open in-memory SQLite connection for isolated tests
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ZatcaDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new ZatcaDbContext(options);
        _context.Database.EnsureCreated();

        _repository = new EfCsidRepository(_context);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task GetActiveAsync_WhenNoCredentialsExist_ReturnsNull()
    {
        // Act
        var active = await _repository.GetActiveAsync();

        // Assert
        Assert.Null(active);
    }

    [Fact]
    public async Task SaveAsync_WithActiveCredential_SavesAndRetrievesViaGetActiveAsync()
    {
        // Arrange
        var cred = new CsidCredential(
            id: Guid.NewGuid(),
            privateKeyPem: "-----BEGIN PRIVATE KEY-----\nMIGHAgEAMBMGByqGSM49AgEGCCqGSM49AwEHBG0wawIBAQQg...\n-----END PRIVATE KEY-----",
            publicKeyPem: "-----BEGIN PUBLIC KEY-----\nMFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE...\n-----END PUBLIC KEY-----",
            csrPem: "-----BEGIN CERTIFICATE REQUEST-----\nMIIB...-----END CERTIFICATE REQUEST-----",
            certificatePem: "-----BEGIN CERTIFICATE-----\nMIIC...-----END CERTIFICATE-----",
            secret: "csid-secret-xyz",
            requestId: "zatca-req-12345",
            environment: CsidEnvironment.Simulation,
            isActive: true,
            createdAt: DateTimeOffset.UtcNow
        );

        // Act
        await _repository.SaveAsync(cred);
        var active = await _repository.GetActiveAsync();

        // Assert
        Assert.NotNull(active);
        Assert.Equal(cred.Id, active.Id);
        Assert.Equal(cred.PrivateKeyPem, active.PrivateKeyPem);
        Assert.Equal(cred.PublicKeyPem, active.PublicKeyPem);
        Assert.Equal(cred.CsrPem, active.CsrPem);
        Assert.Equal(cred.CertificatePem, active.CertificatePem);
        Assert.Equal(cred.Secret, active.Secret);
        Assert.Equal(cred.RequestId, active.RequestId);
        Assert.Equal(cred.Environment, active.Environment);
        Assert.True(active.IsActive);
    }

    [Fact]
    public async Task SaveAsync_NewActiveCredential_AutomaticallyDeactivatesOlderActiveCredentials()
    {
        // Arrange: Credential 1 active
        var cred1 = new CsidCredential(
            id: Guid.NewGuid(),
            privateKeyPem: "KEY-1",
            certificatePem: "CERT-1",
            secret: "SEC-1",
            environment: CsidEnvironment.Simulation,
            isActive: true
        );
        await _repository.SaveAsync(cred1);

        // Arrange: Credential 2 active
        var cred2 = new CsidCredential(
            id: Guid.NewGuid(),
            privateKeyPem: "KEY-2",
            certificatePem: "CERT-2",
            secret: "SEC-2",
            environment: CsidEnvironment.Production,
            isActive: true
        );

        // Act
        await _repository.SaveAsync(cred2);

        // Assert: Active is cred2
        var active = await _repository.GetActiveAsync();
        Assert.NotNull(active);
        Assert.Equal(cred2.Id, active.Id);
        Assert.True(active.IsActive);

        // Assert: cred1 was marked inactive
        var oldCred = await _repository.GetByIdAsync(cred1.Id);
        Assert.NotNull(oldCred);
        Assert.False(oldCred.IsActive);
    }

    [Fact]
    public async Task SaveAsync_InactiveCredential_DoesNotDeactivateCurrentActiveCredential()
    {
        // Arrange: Active credential
        var cred1 = new CsidCredential(
            id: Guid.NewGuid(),
            privateKeyPem: "KEY-ACTIVE",
            isActive: true
        );
        await _repository.SaveAsync(cred1);

        // Arrange: Inactive pending credential
        var cred2 = new CsidCredential(
            id: Guid.NewGuid(),
            privateKeyPem: "KEY-PENDING",
            isActive: false
        );

        // Act
        await _repository.SaveAsync(cred2);

        // Assert
        var active = await _repository.GetActiveAsync();
        Assert.NotNull(active);
        Assert.Equal(cred1.Id, active.Id);

        var pending = await _repository.GetByIdAsync(cred2.Id);
        Assert.NotNull(pending);
        Assert.False(pending.IsActive);
    }

    [Fact]
    public async Task SetActiveAsync_ExistingInactiveCredential_ActivatesItAndDeactivatesOthers()
    {
        // Arrange
        var cred1 = new CsidCredential(id: Guid.NewGuid(), privateKeyPem: "KEY-1", isActive: true);
        var cred2 = new CsidCredential(id: Guid.NewGuid(), privateKeyPem: "KEY-2", isActive: false);
        await _repository.SaveAsync(cred1);
        await _repository.SaveAsync(cred2);

        // Act: Promote cred2 to active
        await _repository.SetActiveAsync(cred2.Id);

        // Assert
        var active = await _repository.GetActiveAsync();
        Assert.NotNull(active);
        Assert.Equal(cred2.Id, active.Id);
        Assert.True(active.IsActive);

        var oldCred = await _repository.GetByIdAsync(cred1.Id);
        Assert.NotNull(oldCred);
        Assert.False(oldCred.IsActive);
    }
}

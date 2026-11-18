using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ZatcaBridge.Domain.Cryptography;
using ZatcaBridge.Infrastructure.Persistence;
using ZatcaBridge.Infrastructure.Persistence.Entities;
using ZatcaBridge.Infrastructure.Persistence.Repositories;
using ZatcaBridge.Infrastructure.Security;

namespace ZatcaBridge.UnitTests.Infrastructure;

public class EfCsidRepositorySecurityTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ZatcaDbContext _context;
    private readonly EfCsidRepository _repository;

    public EfCsidRepositorySecurityTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ZatcaDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new ZatcaDbContext(options);
        _context.Database.EnsureCreated();

        var protector = new DataProtectionCsidSecretProtector(new EphemeralDataProtectionProvider());
        _repository = new EfCsidRepository(_context, protector);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task SaveAsync_EncryptsPrivateKeyAndSecretAtRestInDatabase()
    {
        // Arrange
        const string rawPrivateKey = "-----BEGIN EC PRIVATE KEY-----\nSUPER_SENSITIVE_ECDSA_KEY\n-----END EC PRIVATE KEY-----";
        const string rawSecret = "super-secret-csid-password-456";

        var credential = new CsidCredential(
            id: Guid.NewGuid(),
            privateKeyPem: rawPrivateKey,
            publicKeyPem: "-----BEGIN PUBLIC KEY-----\nPUBLIC_KEY\n-----END PUBLIC KEY-----",
            csrPem: "CSR",
            certificatePem: "CERT",
            secret: rawSecret,
            requestId: "req-1",
            environment: CsidEnvironment.Simulation,
            isActive: true,
            createdAt: DateTimeOffset.UtcNow
        );

        // Act
        await _repository.SaveAsync(credential);

        // Inspect raw database storage directly via DbContext
        var storedEntity = await _context.CsidCredentials.AsNoTracking().FirstOrDefaultAsync(c => c.Id == credential.Id);
        Assert.NotNull(storedEntity);

        // Assert: Database columns MUST be encrypted
        Assert.StartsWith("enc:v1:", storedEntity.PrivateKeyPem);
        Assert.DoesNotContain("SUPER_SENSITIVE_ECDSA_KEY", storedEntity.PrivateKeyPem);

        Assert.StartsWith("enc:v1:", storedEntity.Secret);
        Assert.DoesNotContain("super-secret-csid-password-456", storedEntity.Secret);

        // Assert: Domain aggregate retrieved via repository is automatically decrypted
        var retrieved = await _repository.GetActiveAsync();
        Assert.NotNull(retrieved);
        Assert.Equal(rawPrivateKey, retrieved.PrivateKeyPem);
        Assert.Equal(rawSecret, retrieved.Secret);
    }

    [Fact]
    public async Task GetActiveAsync_WithLegacyUnencryptedRow_DecodesCleanly()
    {
        // Arrange: Insert legacy raw plaintext row directly into DB without encryption
        const string rawPrivateKey = "-----BEGIN EC PRIVATE KEY-----\nLEGACY_KEY\n-----END EC PRIVATE KEY-----";
        const string rawSecret = "legacy-secret";

        var legacyEntity = new CsidCredentialEntity
        {
            Id = Guid.NewGuid(),
            PrivateKeyPem = rawPrivateKey,
            PublicKeyPem = "PUB",
            CsrPem = "CSR",
            CertificatePem = "CERT",
            Secret = rawSecret,
            RequestId = "req-legacy",
            Environment = (int)CsidEnvironment.Simulation,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _context.CsidCredentials.Add(legacyEntity);
        await _context.SaveChangesAsync();

        // Act
        var active = await _repository.GetActiveAsync();

        // Assert
        Assert.NotNull(active);
        Assert.Equal(rawPrivateKey, active.PrivateKeyPem);
        Assert.Equal(rawSecret, active.Secret);
    }
}

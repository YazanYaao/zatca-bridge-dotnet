using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using ZatcaBridge.Application.Common.Interfaces;
using ZatcaBridge.Infrastructure.Extensions;
using ZatcaBridge.Infrastructure.Security;

namespace ZatcaBridge.UnitTests.Infrastructure;

public class DataProtectionCsidSecretProtectorTests
{
    private readonly DataProtectionCsidSecretProtector _protector;

    public DataProtectionCsidSecretProtectorTests()
    {
        var dataProtectionProvider = new EphemeralDataProtectionProvider();
        _protector = new DataProtectionCsidSecretProtector(dataProtectionProvider);
    }

    [Fact]
    public void Protect_WhenInputIsProvided_ReturnsCiphertextWithEncPrefix()
    {
        // Arrange
        const string plaintext = "-----BEGIN EC PRIVATE KEY-----\nMHcCAQEEI...\n-----END EC PRIVATE KEY-----";

        // Act
        var protectedValue = _protector.Protect(plaintext);

        // Assert
        Assert.NotNull(protectedValue);
        Assert.StartsWith("enc:v1:", protectedValue);
        Assert.NotEqual(plaintext, protectedValue);
        Assert.DoesNotContain("PRIVATE KEY", protectedValue);
    }

    [Fact]
    public void Unprotect_WhenGivenProtectedValue_RecoversOriginalPlaintext()
    {
        // Arrange
        const string secret = "zatca-csid-secret-passphrase-998877";
        var protectedValue = _protector.Protect(secret);

        // Act
        var roundtripped = _protector.Unprotect(protectedValue);

        // Assert
        Assert.Equal(secret, roundtripped);
    }

    [Fact]
    public void Unprotect_WhenGivenLegacyUnencryptedString_ReturnsUnchanged()
    {
        // Arrange (legacy records stored before encryption was enabled)
        const string legacyKey = "-----BEGIN PRIVATE KEY-----\nLEGACY_PLAINTEXT_KEY\n-----END PRIVATE KEY-----";

        // Act
        var result = _protector.Unprotect(legacyKey);

        // Assert: backward compatibility allows smooth migration
        Assert.Equal(legacyKey, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Protect_And_Unprotect_WhenNullOrWhitespace_ReturnsInputAsIs(string? input)
    {
        // Act & Assert
        Assert.Equal(input, _protector.Protect(input));
        Assert.Equal(input, _protector.Unprotect(input));
    }

    [Fact]
    public void AddZatcaInfrastructure_WithKeysPathEnvVar_ConfiguresFileKeyPersistence()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "zatca_keys_" + Guid.NewGuid().ToString("N"));
        try
        {
            Environment.SetEnvironmentVariable("DATA_PROTECTION_KEYS_PATH", tempDir);
            var services = new ServiceCollection();
            services.AddZatcaInfrastructure("Data Source=:memory:");
            var sp = services.BuildServiceProvider();
            var protector = sp.GetRequiredService<ICsidSecretProtector>();

            var encrypted = protector.Protect("test-secret");
            Assert.NotNull(encrypted);
            Assert.StartsWith("enc:v1:", encrypted);
            Assert.True(Directory.Exists(tempDir));
        }
        finally
        {
            Environment.SetEnvironmentVariable("DATA_PROTECTION_KEYS_PATH", null);
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
}

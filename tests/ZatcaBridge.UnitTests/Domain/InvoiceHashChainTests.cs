using System;
using System.Security.Cryptography;
using System.Text;
using Xunit;
using ZatcaBridge.Domain.Cryptography;
using ZatcaBridge.Domain.ValueObjects;

namespace ZatcaBridge.UnitTests.Domain;

public class InvoiceHashChainTests
{
    private const string OfficialZatcaGenesisHash = "NWZlY2ViNjZmZmM4NmYzOGQ5NTI3ODZjNmQ2OTZjNzljMmRiYzIzOWRkNGU5MWI4NjliMDExMzg0NmNmNmIx";

    [Fact]
    public void GenesisPreviousInvoiceHash_MatchesOfficialZatcaConstant()
    {
        // Assert
        Assert.Equal(OfficialZatcaGenesisHash, PreviousInvoiceHash.Genesis.Value);
        Assert.True(PreviousInvoiceHash.Genesis.IsGenesis);
    }

    [Fact]
    public void ComputeHash_KnownPayload_ReturnsRaw32ByteDigestAndBase64String()
    {
        // Arrange
        const string xmlPayload = "<Invoice xmlns=\"urn:oasis:names:specification:ubl:schema:xsd:Invoice-2\"><cbc:ID>INV-001</cbc:ID></Invoice>";
        var expectedBytes = SHA256.HashData(Encoding.UTF8.GetBytes(xmlPayload));
        var expectedBase64 = Convert.ToBase64String(expectedBytes);

        // Act
        var hashResult = InvoiceHashCalculator.ComputeHash(xmlPayload);

        // Assert
        Assert.NotNull(hashResult);
        Assert.Equal(32, hashResult.Digest.Length);
        Assert.Equal(expectedBytes, hashResult.Digest);
        Assert.Equal(expectedBase64, hashResult.Base64Hash);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ComputeHash_NullOrWhitespace_ThrowsArgumentException(string? invalidInput)
    {
        Assert.Throws<ArgumentException>(() => InvoiceHashCalculator.ComputeHash(invalidInput!));
    }

    [Fact]
    public void InvoiceCounterValue_Genesis_StartsAtOne()
    {
        var genesisIcv = InvoiceCounterValue.Genesis;
        Assert.Equal(1, genesisIcv.Value);
        Assert.True(genesisIcv.IsGenesis);

        var explicitIcv = new InvoiceCounterValue(1);
        Assert.Equal(1, explicitIcv.Value);
        Assert.True(explicitIcv.IsGenesis);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void InvoiceCounterValue_NonPositiveValue_ThrowsArgumentOutOfRangeException(long nonPositiveValue)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new InvoiceCounterValue(nonPositiveValue));
    }

    [Fact]
    public void InvoiceCounterValue_Next_IncrementsStrictlyByOne()
    {
        var current = new InvoiceCounterValue(42);
        var next = current.Next();

        Assert.Equal(43, next.Value);
        Assert.False(next.IsGenesis);
    }

    [Fact]
    public void InvoiceCounterValue_EnsureNext_ValidSequence_DoesNotThrow()
    {
        var current = new InvoiceCounterValue(5);
        var next = new InvoiceCounterValue(6);

        // Should not throw
        current.EnsureNext(next);
    }

    [Theory]
    [InlineData(5)] // Equal (jump = 0)
    [InlineData(4)] // Decrement (jump = -1)
    [InlineData(7)] // Gap (jump = +2)
    [InlineData(100)] // Large jump
    public void InvoiceCounterValue_EnsureNext_NonSequentialJump_ThrowsInvalidOperationException(long invalidNextValue)
    {
        var current = new InvoiceCounterValue(5);
        var invalidNext = new InvoiceCounterValue(invalidNextValue);

        Assert.Throws<InvalidOperationException>((Action)(() => current.EnsureNext(invalidNext)));
    }

    [Fact]
    public void HashChainRecord_CreateGenesis_InitializesWithGenesisIcvAndPih()
    {
        // Arrange
        const string invoiceHash = "dGhpcyBpcyBhIHNhbXBsZSBpbnZvaWNlIGhhc2ggMzI=";

        // Act
        var record = HashChainRecord.CreateGenesis(invoiceHash);

        // Assert
        Assert.Equal(1, record.Icv.Value);
        Assert.True(record.Icv.IsGenesis);
        Assert.Equal(PreviousInvoiceHash.Genesis, record.PreviousHash);
        Assert.Equal(OfficialZatcaGenesisHash, record.PreviousHash.Value);
        Assert.Equal(invoiceHash, record.InvoiceHash);
    }

    [Fact]
    public void HashChainRecord_CreateNext_LinksCorrectly()
    {
        // Arrange
        var genesis = HashChainRecord.CreateGenesis("aGFzaC0x");
        const string nextHash = "aGFzaC0y";

        // Act
        var next = genesis.CreateNext(nextHash);

        // Assert
        Assert.Equal(2, next.Icv.Value);
        Assert.Equal("aGFzaC0x", next.PreviousHash.Value);
        Assert.Equal(nextHash, next.InvoiceHash);
    }

    [Fact]
    public void HashChainRecord_ValidateNext_ValidChaining_DoesNotThrow()
    {
        // Arrange
        var record1 = HashChainRecord.CreateGenesis("aGFzaC0x");
        var record2 = record1.CreateNext("aGFzaC0y");
        var record3 = record2.CreateNext("aGFzaC0z");

        // Act & Assert (Should succeed without exception)
        record1.ValidateNext(record2);
        record2.ValidateNext(record3);
    }

    [Fact]
    public void HashChainRecord_ValidateNext_TamperedPreviousHash_ThrowsInvalidOperationException()
    {
        // Arrange
        var record1 = HashChainRecord.CreateGenesis("aGFzaC0x");
        var tamperedNext = new HashChainRecord(
            new InvoiceCounterValue(2),
            PreviousInvoiceHash.FromHash("dGFtcGVyZWQtaGFzaA=="),
            "aGFzaC0y"
        );

        // Act & Assert
        Assert.Throws<InvalidOperationException>((Action)(() => record1.ValidateNext(tamperedNext)));
    }

    [Fact]
    public void HashChainRecord_ValidateNext_BrokenIcvSequence_ThrowsInvalidOperationException()
    {
        // Arrange
        var record1 = HashChainRecord.CreateGenesis("aGFzaC0x");
        var brokenSequenceNext = new HashChainRecord(
            new InvoiceCounterValue(3), // Jumped from 1 to 3!
            PreviousInvoiceHash.FromHash("aGFzaC0x"),
            "aGFzaC0y"
        );

        // Act & Assert
        Assert.Throws<InvalidOperationException>((Action)(() => record1.ValidateNext(brokenSequenceNext)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a valid base 64 string!!")]
    public void PreviousInvoiceHash_InvalidBase64_ThrowsArgumentException(string? invalidHash)
    {
        Assert.Throws<ArgumentException>(() => PreviousInvoiceHash.FromHash(invalidHash!));
    }
}

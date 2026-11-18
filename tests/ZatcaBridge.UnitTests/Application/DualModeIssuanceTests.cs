using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Org.BouncyCastle.Asn1.Sec;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;
using Xunit;
using ZatcaBridge.Application.Common.Interfaces;
using ZatcaBridge.Application.Invoices.Commands;
using ZatcaBridge.Application.Models;
using ZatcaBridge.Domain.Cryptography;
using ZatcaBridge.Domain.Entities;
using ZatcaBridge.Domain.Enums;
using ZatcaBridge.Domain.ValueObjects;
using ZatcaBridge.Infrastructure.Cryptography;
using ZatcaBridge.Infrastructure.Xml;

namespace ZatcaBridge.UnitTests.Application;

public class DualModeIssuanceTests
{
    private sealed class FakeHashChainRepository : IHashChainRepository
    {
        public HashChainRecord? LatestRecord { get; set; }
        public Task<HashChainRecord?> GetLatestRecordAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(LatestRecord);
    }

    private sealed class FakeInvoiceOutboxRepository : IInvoiceOutboxRepository
    {
        public Invoice? SavedInvoice { get; private set; }
        public HashChainRecord? SavedChainRecord { get; private set; }
        public OutboxMessage? SavedOutboxMessage { get; private set; }

        public Task SaveInvoiceWithOutboxAsync(
            Invoice invoice,
            HashChainRecord newChainRecord,
            OutboxMessage outboxMessage,
            CancellationToken cancellationToken = default)
        {
            SavedInvoice = invoice;
            SavedChainRecord = newChainRecord;
            SavedOutboxMessage = outboxMessage;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<OutboxMessage>> GetPendingMessagesAsync(int batchSize = 10, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<OutboxMessage>>(Array.Empty<OutboxMessage>());

        public Task UpdateOutboxStatusAsync(Guid messageId, OutboxStatus status, string? errorMessage = null, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeCsidRepository : ICsidRepository
    {
        public CsidCredential? ActiveCredential { get; set; }

        public Task<CsidCredential?> GetActiveAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(ActiveCredential);

        public Task<CsidCredential?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(ActiveCredential?.Id == id ? ActiveCredential : null);

        public Task SaveAsync(CsidCredential credential, CancellationToken cancellationToken = default)
        {
            ActiveCredential = credential;
            return Task.CompletedTask;
        }

        public Task SetActiveAsync(Guid id, CancellationToken cancellationToken = default)
        {
            if (ActiveCredential?.Id == id)
            {
                ActiveCredential = ActiveCredential with { IsActive = true };
            }
            return Task.CompletedTask;
        }
    }

    private static Party CreateSeller() =>
        new Party("Acme Saudi Co", "310123456700003", "Olaya St", "1234", "12211", "Riyadh", "Olaya", "SA");

    private static IssueInvoiceCommand CreateCommand(InvoiceTransactionType transactionType = InvoiceTransactionType.B2C) =>
        new IssueInvoiceCommand(
            InvoiceNumber: "INV-2026-DM-01",
            IssueDate: new DateOnly(2026, 11, 17),
            IssueTime: new TimeOnly(12, 0, 0),
            InvoiceType: transactionType == InvoiceTransactionType.B2B ? InvoiceTypeCode.TaxInvoice : InvoiceTypeCode.SimplifiedTaxInvoice,
            TransactionType: transactionType,
            Seller: CreateSeller(),
            Buyer: null,
            LineItems: new[]
            {
                new InvoiceLineItemDto("Consulting Services", 1.00m, 1000.00m, 0.00m, TaxCategory.Standard)
            }
        );

    private static CsidCredential CreateTestCsidCredential()
    {
        var keyGen = new ECKeyPairGenerator();
        keyGen.Init(new ECKeyGenerationParameters(SecObjectIdentifiers.SecP256k1, new SecureRandom()));
        var keyPair = keyGen.GenerateKeyPair();

        var dn = new X509Name("CN=TST-310123456700003, OU=Riyadh, O=Acme Saudi Co, C=SA");
        var certGen = new X509V3CertificateGenerator();
        certGen.SetIssuerDN(dn);
        certGen.SetSubjectDN(dn);
        certGen.SetSerialNumber(BigInteger.ValueOf(DateTime.UtcNow.Ticks));
        certGen.SetNotBefore(DateTime.UtcNow.AddDays(-1));
        certGen.SetNotAfter(DateTime.UtcNow.AddYears(1));
        certGen.SetPublicKey(keyPair.Public);
        var sigFactory = new Asn1SignatureFactory("SHA256WITHECDSA", keyPair.Private);
        var cert = certGen.Generate(sigFactory);

        string privateKeyPem;
        using (var sw = new StringWriter())
        {
            var pemWriter = new PemWriter(sw);
            pemWriter.WriteObject(new Pkcs8Generator(keyPair.Private));
            pemWriter.Writer.Flush();
            privateKeyPem = sw.ToString();
        }

        string certPem;
        using (var sw = new StringWriter())
        {
            var pemWriter = new PemWriter(sw);
            pemWriter.WriteObject(cert);
            pemWriter.Writer.Flush();
            certPem = sw.ToString();
        }

        return new CsidCredential(
            id: Guid.NewGuid(),
            privateKeyPem: privateKeyPem,
            publicKeyPem: null,
            csrPem: null,
            certificatePem: certPem,
            secret: "csid-secret-xyz",
            requestId: "zatca-req-12345",
            environment: CsidEnvironment.Simulation,
            isActive: true
        );
    }

    [Fact]
    public async Task HandleAsync_WhenNoActiveCsid_GeneratesUnsignedXmlWith5QrTags()
    {
        // Arrange
        var hashChainRepo = new FakeHashChainRepository();
        var outboxRepo = new FakeInvoiceOutboxRepository();
        var xmlGenerator = new UblXmlGenerator();
        var csidRepo = new FakeCsidRepository { ActiveCredential = null }; // Unsigned fallback
        var xadesSigner = new XadesXmlSigner(xmlGenerator);

        var handler = new IssueInvoiceHandler(hashChainRepo, outboxRepo, xmlGenerator, csidRepository: csidRepo, xadesXmlSigner: xadesSigner);
        var command = CreateCommand();

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        Assert.NotNull(result);
        Assert.False(string.IsNullOrWhiteSpace(result.UblXml));
        Assert.DoesNotContain("ds:Signature", result.UblXml);
        Assert.DoesNotContain("xades:QualifyingProperties", result.UblXml);

        var qrTags = TlvEncoder.Decode(result.QrCodeBase64);
        Assert.Equal(5, qrTags.Count); // Tags 1 to 5 only
    }

    [Fact]
    public async Task HandleAsync_WhenActiveCsidExists_GeneratesXadesSignedXmlWith9QrTagsAndCanonicalHash()
    {
        // Arrange
        var hashChainRepo = new FakeHashChainRepository();
        var outboxRepo = new FakeInvoiceOutboxRepository();
        var xmlGenerator = new UblXmlGenerator();
        var csidRepo = new FakeCsidRepository { ActiveCredential = CreateTestCsidCredential() }; // Active CSID
        var xadesSigner = new XadesXmlSigner(xmlGenerator);

        var handler = new IssueInvoiceHandler(hashChainRepo, outboxRepo, xmlGenerator, csidRepository: csidRepo, xadesXmlSigner: xadesSigner);
        var command = CreateCommand();

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        Assert.NotNull(result);
        Assert.False(string.IsNullOrWhiteSpace(result.UblXml));
        Assert.Contains("ds:Signature", result.UblXml);
        Assert.Contains("xades:QualifyingProperties", result.UblXml);

        var qrTags = TlvEncoder.Decode(result.QrCodeBase64);
        Assert.Equal(9, qrTags.Count); // Tags 1 to 9 all present

        // Tag 6 is UTF-8 bytes of 44-character Base64 canonical invoice hash (per ZATCA SDK)
        Assert.Equal(6, qrTags[5].TagNumber);
        Assert.Equal(44, qrTags[5].Value.Length);

        // Result InvoiceHash must match the canonical hash
        var expectedHash = System.Text.Encoding.UTF8.GetString(qrTags[5].Value);
        Assert.Equal(expectedHash, result.InvoiceHash);

        // Outbox and chain record must store the canonical hash and signed XML
        Assert.NotNull(outboxRepo.SavedChainRecord);
        Assert.Equal(result.InvoiceHash, outboxRepo.SavedChainRecord.InvoiceHash);
        Assert.NotNull(outboxRepo.SavedOutboxMessage);
        Assert.Equal(result.UblXml, outboxRepo.SavedOutboxMessage.PayloadXml);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ZatcaBridge.Application.Common.Interfaces;
using ZatcaBridge.Application.Common.Security;
using ZatcaBridge.Application.Models;
using ZatcaBridge.Domain.Cryptography;
using ZatcaBridge.Domain.Entities;
using ZatcaBridge.Domain.Enums;
using ZatcaBridge.Domain.ValueObjects;

namespace ZatcaBridge.Application.Invoices.Commands;

/// <summary>
/// Orchestrates domain entities, cryptographic hashing, TLV QR generation, UBL 2.1 XML serialization,
/// and atomic outbox dispatch for ZATCA Phase 2 invoice issuance.
/// </summary>
public class IssueInvoiceHandler
{
    private static readonly SemaphoreSlim DefaultLock = new(1, 1);
    private readonly IHashChainRepository _hashChainRepository;
    private readonly IInvoiceOutboxRepository _invoiceOutboxRepository;
    private readonly IZatcaXmlGenerator _xmlGenerator;
    private readonly SemaphoreSlim _issuanceLock;
    private readonly ICsidRepository? _csidRepository;
    private readonly IXadesXmlSigner? _xadesXmlSigner;

    public IssueInvoiceHandler(
        IHashChainRepository hashChainRepository,
        IInvoiceOutboxRepository invoiceOutboxRepository,
        IZatcaXmlGenerator xmlGenerator,
        SemaphoreSlim? issuanceLock = null,
        ICsidRepository? csidRepository = null,
        IXadesXmlSigner? xadesXmlSigner = null)
    {
        _hashChainRepository = hashChainRepository ?? throw new ArgumentNullException(nameof(hashChainRepository));
        _invoiceOutboxRepository = invoiceOutboxRepository ?? throw new ArgumentNullException(nameof(invoiceOutboxRepository));
        _xmlGenerator = xmlGenerator ?? throw new ArgumentNullException(nameof(xmlGenerator));
        _issuanceLock = issuanceLock ?? DefaultLock;
        _csidRepository = csidRepository;
        _xadesXmlSigner = xadesXmlSigner;
    }

    public async Task<IssueInvoiceResult> HandleAsync(IssueInvoiceCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        await _issuanceLock.WaitAsync(cancellationToken);
        try
        {
            var invoiceId = Guid.NewGuid();
            var invoiceUuid = Guid.NewGuid();

            // 1. Map DTOs to Domain InvoiceLineItem entities
            var lineItems = command.LineItems.Select(dto => new InvoiceLineItem(
                Guid.NewGuid(),
                dto.Name,
                dto.Quantity,
                dto.UnitPrice,
                dto.DiscountAmount,
                dto.TaxCategory
            )).ToList();

            // 2. Fetch the latest hash chain record to determine ICV and PIH
            var latestRecord = await _hashChainRepository.GetLatestRecordAsync(cancellationToken);
            long icv;
            string pih;

            if (latestRecord is null)
            {
                // Genesis invoice
                icv = InvoiceCounterValue.Genesis.Value;
                pih = PreviousInvoiceHash.Genesis.Value;
            }
            else
            {
                // Subsequent chained invoice
                icv = latestRecord.Icv.Next().Value;
                pih = latestRecord.InvoiceHash;
            }

            // 3. Fetch active CSID to check for certificate VAT alignment
            var activeCsid = _csidRepository != null
                ? await _csidRepository.GetActiveAsync(cancellationToken)
                : null;

            // Align Seller VAT with active CSID certificate if available to guarantee certificate-permissions compliance
            var effectiveSeller = command.Seller;
            if (activeCsid != null && !string.IsNullOrWhiteSpace(activeCsid.CertificatePem))
            {
                var certVat = CertificateMetadataExtractor.ExtractVatNumber(activeCsid.CertificatePem);
                if (!string.IsNullOrWhiteSpace(certVat) && effectiveSeller.VatNumber != certVat)
                {
                    effectiveSeller = new Party(
                        effectiveSeller.RegistrationName,
                        certVat,
                        effectiveSeller.StreetName,
                        effectiveSeller.BuildingNumber,
                        effectiveSeller.PostalCode,
                        effectiveSeller.City,
                        effectiveSeller.District,
                        effectiveSeller.CountryCode,
                        effectiveSeller.Crn
                    );
                }
            }

            // 4. Construct Domain Aggregate
            var invoice = new Invoice(
                invoiceId,
                invoiceUuid,
                command.InvoiceNumber,
                command.IssueDate,
                command.IssueTime,
                command.InvoiceType,
                command.TransactionType,
                effectiveSeller,
                command.Buyer,
                lineItems,
                command.AllowanceTotal,
                command.ChargeTotal,
                command.PrepaidAmount,
                command.PayableRoundingAmount,
                invoiceCounterValue: icv,
                previousInvoiceHash: pih
            );

            // 5. Determine Dual-Mode (Signed XAdES-BES vs Unsigned Fallback)
            string ublXml;
            string qrCodeBase64;
            string invoiceHash;

            if (activeCsid != null && _xadesXmlSigner != null)
            {
                // Signed Mode: XAdES-BES enveloped signature, Phase 2 QR (Tags 1-9), canonical SHA-256 hash
                var signingResult = _xadesXmlSigner.SignInvoice(invoice, activeCsid);
                ublXml = signingResult.SignedXml;
                qrCodeBase64 = signingResult.QrCodeBase64;
                invoiceHash = signingResult.InvoiceHashBase64;
            }
            else
            {
                // Unsigned Fallback Mode: Standard UBL 2.1 XML and Phase 1 QR (Tags 1-5)
                var qrData = ZatcaQrCodeData.FromInvoice(invoice);
                qrCodeBase64 = TlvEncoder.EncodeToBase64(qrData);
                ublXml = _xmlGenerator.GenerateXml(invoice, qrCodeBase64);
                var hashResult = InvoiceHashCalculator.ComputeHash(ublXml);
                invoiceHash = hashResult.Base64Hash;
            }

            // 7. Formulate new HashChainRecord
            var newChainRecord = latestRecord is null
                ? HashChainRecord.CreateGenesis(invoiceHash)
                : latestRecord.CreateNext(invoiceHash);

            // 8. Prepare Transactional Outbox Message
            var outboxType = invoice.TransactionType == InvoiceTransactionType.B2B
                ? OutboxMessageType.Clearance
                : OutboxMessageType.Reporting;

            var outboxMessageId = Guid.NewGuid();
            var outboxMessage = new OutboxMessage(
                outboxMessageId,
                invoice.Id,
                outboxType,
                ublXml,
                invoiceHash,
                DateTimeOffset.UtcNow,
                OutboxStatus.Pending,
                retryCount: 0,
                errorMessage: null,
                invoiceCounterValue: invoice.InvoiceCounterValue
            );

            // 9. Persist atomically
            await _invoiceOutboxRepository.SaveInvoiceWithOutboxAsync(invoice, newChainRecord, outboxMessage, cancellationToken);

            // 10. Return result
            return new IssueInvoiceResult(
                invoice.Id,
                invoice.Uuid,
                invoice.InvoiceNumber,
                invoice.InvoiceCounterValue,
                pih,
                invoiceHash,
                qrCodeBase64,
                ublXml,
                outboxMessageId
            );
        }
        finally
        {
            _issuanceLock.Release();
        }
    }
}

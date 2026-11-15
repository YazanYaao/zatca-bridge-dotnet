using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ZatcaBridge.Application.Common.Interfaces;
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
    private readonly IHashChainRepository _hashChainRepository;
    private readonly IInvoiceOutboxRepository _invoiceOutboxRepository;
    private readonly IZatcaXmlGenerator _xmlGenerator;

    public IssueInvoiceHandler(
        IHashChainRepository hashChainRepository,
        IInvoiceOutboxRepository invoiceOutboxRepository,
        IZatcaXmlGenerator xmlGenerator)
    {
        _hashChainRepository = hashChainRepository ?? throw new ArgumentNullException(nameof(hashChainRepository));
        _invoiceOutboxRepository = invoiceOutboxRepository ?? throw new ArgumentNullException(nameof(invoiceOutboxRepository));
        _xmlGenerator = xmlGenerator ?? throw new ArgumentNullException(nameof(xmlGenerator));
    }

    public async Task<IssueInvoiceResult> HandleAsync(IssueInvoiceCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

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

        // 3. Construct Domain Aggregate
        var invoice = new Invoice(
            invoiceId,
            invoiceUuid,
            command.InvoiceNumber,
            command.IssueDate,
            command.IssueTime,
            command.InvoiceType,
            command.TransactionType,
            command.Seller,
            command.Buyer,
            lineItems,
            command.AllowanceTotal,
            command.ChargeTotal,
            command.PrepaidAmount,
            command.PayableRoundingAmount,
            invoiceCounterValue: icv,
            previousInvoiceHash: pih
        );

        // 4. Encode ZATCA TLV Base64 QR code
        var qrData = ZatcaQrCodeData.FromInvoice(invoice);
        var qrCodeBase64 = TlvEncoder.EncodeToBase64(qrData);

        // 5. Generate standard UBL 2.1 XML
        var ublXml = _xmlGenerator.GenerateXml(invoice, qrCodeBase64);

        // 6. Compute canonical SHA-256 Invoice Hash
        var hashResult = InvoiceHashCalculator.ComputeHash(ublXml);
        var invoiceHash = hashResult.Base64Hash;

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
            OutboxStatus.Pending
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
}

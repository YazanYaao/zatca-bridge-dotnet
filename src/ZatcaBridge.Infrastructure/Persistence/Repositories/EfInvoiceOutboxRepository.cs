using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ZatcaBridge.Application.Common.Interfaces;
using ZatcaBridge.Application.Models;
using ZatcaBridge.Domain.Entities;
using ZatcaBridge.Domain.ValueObjects;
using ZatcaBridge.Infrastructure.Persistence.Entities;

namespace ZatcaBridge.Infrastructure.Persistence.Repositories;

/// <summary>
/// Entity Framework Core implementation of <see cref="IInvoiceOutboxRepository"/> and <see cref="IHashChainRepository"/>.
/// </summary>
public class EfInvoiceOutboxRepository : IInvoiceOutboxRepository, IHashChainRepository
{
    private readonly ZatcaDbContext _context;

    public EfInvoiceOutboxRepository(ZatcaDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<HashChainRecord?> GetLatestRecordAsync(CancellationToken cancellationToken = default)
    {
        var latestEntity = await _context.HashChainRecords
            .AsNoTracking()
            .OrderByDescending(r => r.Icv)
            .FirstOrDefaultAsync(cancellationToken);

        if (latestEntity is null)
        {
            return null;
        }

        return new HashChainRecord(
            new InvoiceCounterValue(latestEntity.Icv),
            PreviousInvoiceHash.FromHash(latestEntity.PreviousHash),
            latestEntity.InvoiceHash
        );
    }

    public async Task SaveInvoiceWithOutboxAsync(
        Invoice invoice,
        HashChainRecord newChainRecord,
        OutboxMessage outboxMessage,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        ArgumentNullException.ThrowIfNull(newChainRecord);
        ArgumentNullException.ThrowIfNull(outboxMessage);

        var invoiceEntity = new InvoiceEntity
        {
            Id = invoice.Id,
            Uuid = invoice.Uuid,
            InvoiceNumber = invoice.InvoiceNumber,
            IssueDate = invoice.IssueDate,
            IssueTime = invoice.IssueTime,
            InvoiceTypeCode = (int)invoice.InvoiceType,
            TransactionType = (int)invoice.TransactionType,
            SellerName = invoice.Seller.RegistrationName,
            SellerVatNumber = invoice.Seller.VatNumber ?? string.Empty,
            BuyerName = invoice.Buyer?.RegistrationName,
            BuyerVatNumber = invoice.Buyer?.VatNumber,
            LineExtensionAmount = invoice.MonetaryTotal.LineExtensionAmount,
            TaxExclusiveAmount = invoice.MonetaryTotal.TaxExclusiveAmount,
            TaxInclusiveAmount = invoice.MonetaryTotal.TaxInclusiveAmount,
            TotalTaxAmount = invoice.TotalTaxAmount,
            PayableAmount = invoice.MonetaryTotal.PayableAmount,
            InvoiceCounterValue = invoice.InvoiceCounterValue,
            PreviousInvoiceHash = invoice.PreviousInvoiceHash,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var chainEntity = new HashChainEntity
        {
            Icv = newChainRecord.Icv.Value,
            PreviousHash = newChainRecord.PreviousHash.Value,
            InvoiceHash = newChainRecord.InvoiceHash,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var outboxEntity = new OutboxMessageEntity
        {
            Id = outboxMessage.Id,
            InvoiceId = outboxMessage.InvoiceId,
            Type = (int)outboxMessage.Type,
            PayloadXml = outboxMessage.PayloadXml,
            InvoiceHash = outboxMessage.InvoiceHash,
            InvoiceCounterValue = invoice.InvoiceCounterValue,
            Status = (int)outboxMessage.Status,
            RetryCount = outboxMessage.RetryCount,
            ErrorMessage = outboxMessage.ErrorMessage,
            CreatedAt = outboxMessage.CreatedAt
        };

        if (_context.Database.IsRelational())
        {
            await using var tx = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
            try
            {
                _context.Invoices.Add(invoiceEntity);
                _context.HashChainRecords.Add(chainEntity);
                _context.OutboxMessages.Add(outboxEntity);

                await _context.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);
            }
            catch
            {
                await tx.RollbackAsync(cancellationToken);
                throw;
            }
        }
        else
        {
            _context.Invoices.Add(invoiceEntity);
            _context.HashChainRecords.Add(chainEntity);
            _context.OutboxMessages.Add(outboxEntity);

            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<IReadOnlyList<OutboxMessage>> GetPendingMessagesAsync(int batchSize = 10, CancellationToken cancellationToken = default)
    {
        var entities = await _context.OutboxMessages
            .AsNoTracking()
            .Where(m => m.Status == (int)OutboxStatus.Pending)
            .OrderBy(m => m.InvoiceCounterValue)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        return entities.Select(e => new OutboxMessage(
            e.Id,
            e.InvoiceId,
            (OutboxMessageType)e.Type,
            e.PayloadXml,
            e.InvoiceHash,
            e.CreatedAt,
            (OutboxStatus)e.Status,
            e.RetryCount,
            e.ErrorMessage
        )).ToList().AsReadOnly();
    }

    public async Task UpdateOutboxStatusAsync(
        Guid messageId,
        OutboxStatus status,
        string? errorMessage = null,
        CancellationToken cancellationToken = default)
    {
        var entity = await _context.OutboxMessages.FirstOrDefaultAsync(m => m.Id == messageId, cancellationToken);
        if (entity is not null)
        {
            entity.Status = (int)status;
            entity.ErrorMessage = errorMessage;
            if (status == OutboxStatus.Failed)
            {
                entity.RetryCount++;
            }
            else if (status == OutboxStatus.Completed)
            {
                entity.ProcessedAt = DateTimeOffset.UtcNow;
            }

            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}

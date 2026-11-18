using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ZatcaBridge.Application.Common.Interfaces;
using ZatcaBridge.Application.Invoices.Commands;
using ZatcaBridge.Application.Models;
using ZatcaBridge.Application.Services;
using ZatcaBridge.Domain.Entities;
using ZatcaBridge.Domain.Enums;
using ZatcaBridge.Domain.ValueObjects;
using ZatcaBridge.Infrastructure.Persistence;
using ZatcaBridge.Infrastructure.Persistence.Entities;

namespace ZatcaBridge.Api.Endpoints;

/// <summary>
/// Party DTO for API requests.
/// </summary>
public sealed record PartyDto(
    string Name,
    string VatRegistrationNumber,
    string Street,
    string BuildingNumber,
    string PostalZone,
    string City,
    string District,
    string CountryCode = "SA"
);

/// <summary>
/// DTO representing a tax category for API line items, decoupling domain Value Objects from API wire contracts.
/// </summary>
public sealed record TaxCategoryDto(
    TaxCategoryCode Code,
    decimal? Rate = null,
    string? ExemptionReasonCode = null,
    string? ExemptionReason = null
)
{
    public TaxCategory ToDomain()
    {
        return Code switch
        {
            TaxCategoryCode.Standard => Rate.HasValue && Rate.Value != 0.15m ? new TaxCategory(Code, Rate.Value) : TaxCategory.Standard,
            TaxCategoryCode.ZeroRated => TaxCategory.ZeroRated,
            TaxCategoryCode.OutOfScope => TaxCategory.OutOfScope,
            TaxCategoryCode.Exempt => TaxCategory.Exempt(
                ExemptionReasonCode ?? "VATEX-SA-OOS",
                ExemptionReason ?? "Exempt"
            ),
            _ => new TaxCategory(Code, Rate ?? 0.00m, ExemptionReasonCode, ExemptionReason)
        };
    }

    public static implicit operator TaxCategoryDto(TaxCategory tc) =>
        new(tc.Code, tc.Rate, tc.ExemptionReasonCode, tc.ExemptionReason);
}

/// <summary>
/// Invoice issuance request DTO for POST /api/v1/invoices.
/// </summary>
public sealed record IssueInvoiceRequest(
    string InvoiceNumber,
    DateOnly IssueDate,
    TimeOnly IssueTime,
    InvoiceTypeCode InvoiceType,
    InvoiceTransactionType TransactionType,
    PartyDto Seller,
    PartyDto? Buyer,
    IReadOnlyList<InvoiceLineItemDto> LineItems,
    decimal AllowanceTotal = 0.00m,
    decimal ChargeTotal = 0.00m,
    decimal PrepaidAmount = 0.00m,
    decimal PayableRoundingAmount = 0.00m
);

/// <summary>
/// Response DTO for manual outbox processing.
/// </summary>
public sealed record ProcessOutboxResponse(int ProcessedCount);

/// <summary>
/// Clean, flattened response DTO representing the latest hash chain record.
/// </summary>
public sealed record LatestChainRecordResponse(
    long InvoiceCounterValue,
    string PreviousInvoiceHash,
    string InvoiceHash
)
{
    /// <summary>
    /// Backwards compatibility property for nested ICV value object consumers.
    /// </summary>
    public IcvValueDto Icv => new(InvoiceCounterValue);
}

public sealed record IcvValueDto(long Value);

/// <summary>
/// Summary DTO for paginated invoice listings.
/// </summary>
public sealed record InvoiceSummaryDto(
    Guid Id,
    Guid Uuid,
    string InvoiceNumber,
    DateOnly IssueDate,
    TimeOnly IssueTime,
    int InvoiceTypeCode,
    int TransactionType,
    string InvoiceType,
    string TransactionTypeName,
    long Icv,
    decimal LineExtensionAmount,
    decimal TotalTaxAmount,
    decimal PayableAmount,
    string SellerName,
    string SellerVatNumber,
    string? BuyerName,
    string? BuyerVatNumber,
    DateTimeOffset CreatedAt
);

/// <summary>
/// Response DTO for paginated invoice list query.
/// </summary>
public sealed record PaginatedInvoicesResponse(
    int TotalCount,
    int Page,
    int PageSize,
    IReadOnlyList<InvoiceSummaryDto> Items
);

/// <summary>
/// Full detail DTO for single invoice inspection.
/// </summary>
public sealed record InvoiceDetailResponse(
    Guid Id,
    Guid Uuid,
    string InvoiceNumber,
    DateOnly IssueDate,
    TimeOnly IssueTime,
    int InvoiceTypeCode,
    int TransactionType,
    string InvoiceType,
    string TransactionTypeName,
    long Icv,
    string? PreviousInvoiceHash,
    string InvoiceHash,
    decimal LineExtensionAmount,
    decimal TaxExclusiveAmount,
    decimal TaxInclusiveAmount,
    decimal TotalTaxAmount,
    decimal PayableAmount,
    string SellerName,
    string SellerVatNumber,
    string? BuyerName,
    string? BuyerVatNumber,
    string XmlPayload,
    string? QrCodeBase64,
    string OutboxStatus,
    DateTimeOffset CreatedAt
);

/// <summary>
/// DTO representing an outbox message item in the queue.
/// </summary>
public sealed record OutboxMessageItemDto(
    Guid Id,
    Guid InvoiceId,
    int Type,
    string TypeName,
    string InvoiceHash,
    long InvoiceCounterValue,
    int Status,
    string StatusName,
    int RetryCount,
    string? ErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ProcessedAt
);

/// <summary>
/// Response DTO for outbox queue listing.
/// </summary>
public sealed record OutboxListResponse(
    int TotalCount,
    IReadOnlyList<OutboxMessageItemDto> Items
);

/// <summary>
/// DTO for a single hash chain link record.
/// </summary>
public sealed record ChainRecordItemDto(
    long Icv,
    string PreviousHash,
    string InvoiceHash,
    DateTimeOffset CreatedAt
);

/// <summary>
/// Response DTO for full hash chain timeline.
/// </summary>
public sealed record ChainHistoryResponse(
    IReadOnlyList<ChainRecordItemDto> Items
);

/// <summary>
/// Minimal API endpoint mappings for invoices and outbox dispatching.
/// </summary>
public static class InvoiceEndpoints
{
    public static IEndpointRouteBuilder MapInvoiceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1");

        group.MapPost("/invoices", async (
            IssueInvoiceRequest request,
            IssueInvoiceHandler handler,
            CancellationToken cancellationToken) =>
        {
            var sellerParty = new Party(
                request.Seller.Name,
                request.Seller.VatRegistrationNumber,
                request.Seller.Street,
                request.Seller.BuildingNumber,
                request.Seller.PostalZone,
                request.Seller.City,
                request.Seller.District,
                request.Seller.CountryCode
            );

            Party? buyerParty = null;
            if (request.Buyer is not null)
            {
                buyerParty = new Party(
                    request.Buyer.Name,
                    request.Buyer.VatRegistrationNumber,
                    request.Buyer.Street,
                    request.Buyer.BuildingNumber,
                    request.Buyer.PostalZone,
                    request.Buyer.City,
                    request.Buyer.District,
                    request.Buyer.CountryCode
                );
            }

            var command = new IssueInvoiceCommand(
                InvoiceNumber: request.InvoiceNumber,
                IssueDate: request.IssueDate,
                IssueTime: request.IssueTime,
                InvoiceType: request.InvoiceType,
                TransactionType: request.TransactionType,
                Seller: sellerParty,
                Buyer: buyerParty,
                LineItems: request.LineItems,
                AllowanceTotal: request.AllowanceTotal,
                ChargeTotal: request.ChargeTotal,
                PrepaidAmount: request.PrepaidAmount,
                PayableRoundingAmount: request.PayableRoundingAmount
            );

            var result = await handler.HandleAsync(command, cancellationToken);
            return Results.Created($"/api/v1/invoices/{result.InvoiceId}", result);
        });

        group.MapGet("/invoices/chain/latest", async (
            IHashChainRepository hashChainRepository,
            CancellationToken cancellationToken) =>
        {
            var record = await hashChainRepository.GetLatestRecordAsync(cancellationToken);
            if (record is null)
            {
                return Results.NotFound(new ProblemDetails
                {
                    Status = StatusCodes.Status404NotFound,
                    Title = "Not Found",
                    Detail = "No invoice hash chain records found."
                });
            }

            var response = new LatestChainRecordResponse(
                record.Icv.Value,
                record.PreviousHash.Value,
                record.InvoiceHash
            );

            return Results.Ok(response);
        });

        group.MapPost("/outbox/process", async (
            OutboxProcessor processor,
            int? batchSize,
            CancellationToken cancellationToken) =>
        {
            var effectiveBatch = batchSize.GetValueOrDefault(10);
            var processed = await processor.ProcessPendingMessagesAsync(effectiveBatch, cancellationToken);
            return Results.Ok(new ProcessOutboxResponse(processed));
        });

        // GET /api/v1/invoices (Paginated listing)
        group.MapGet("/invoices", async (
            [FromServices] ZatcaDbContext db,
            [FromQuery] int? page,
            [FromQuery] int? pageSize,
            [FromQuery] string? sort,
            CancellationToken ct) =>
        {
            var p = page.GetValueOrDefault(1);
            if (p < 1) p = 1;
            var ps = pageSize.GetValueOrDefault(20);
            if (ps < 1) ps = 1;
            if (ps > 100) ps = 100;

            var query = db.Invoices.AsNoTracking();
            var total = await query.CountAsync(ct);

            query = string.Equals(sort, "asc", StringComparison.OrdinalIgnoreCase)
                ? query.OrderBy(i => i.InvoiceCounterValue)
                : query.OrderByDescending(i => i.InvoiceCounterValue);

            var entities = await query
                .Skip((p - 1) * ps)
                .Take(ps)
                .ToListAsync(ct);

            var items = entities.Select(e => new InvoiceSummaryDto(
                Id: e.Id,
                Uuid: e.Uuid,
                InvoiceNumber: e.InvoiceNumber,
                IssueDate: e.IssueDate,
                IssueTime: e.IssueTime,
                InvoiceTypeCode: e.InvoiceTypeCode,
                TransactionType: e.TransactionType,
                InvoiceType: ((InvoiceTypeCode)e.InvoiceTypeCode).ToString(),
                TransactionTypeName: ((InvoiceTransactionType)e.TransactionType).ToString(),
                Icv: e.InvoiceCounterValue,
                LineExtensionAmount: e.LineExtensionAmount,
                TotalTaxAmount: e.TotalTaxAmount,
                PayableAmount: e.PayableAmount,
                SellerName: e.SellerName,
                SellerVatNumber: e.SellerVatNumber,
                BuyerName: e.BuyerName,
                BuyerVatNumber: e.BuyerVatNumber,
                CreatedAt: e.CreatedAt
            )).ToList();

            return Results.Ok(new PaginatedInvoicesResponse(total, p, ps, items));
        });

        // GET /api/v1/invoices/{id} (Full invoice details with XML & QR)
        group.MapGet("/invoices/{id}", async (
            string id,
            [FromServices] ZatcaDbContext db,
            CancellationToken ct) =>
        {
            InvoiceEntity? entity = null;
            if (Guid.TryParse(id, out var parsedGuid))
            {
                entity = await db.Invoices.AsNoTracking()
                    .FirstOrDefaultAsync(i => i.Id == parsedGuid || i.Uuid == parsedGuid, ct);
            }

            if (entity == null)
            {
                entity = await db.Invoices.AsNoTracking()
                    .FirstOrDefaultAsync(i => i.InvoiceNumber == id, ct);
            }

            if (entity == null)
            {
                return Results.NotFound(new ProblemDetails
                {
                    Status = StatusCodes.Status404NotFound,
                    Title = "Invoice Not Found",
                    Detail = $"Invoice with ID or Number '{id}' was not found.",
                    Type = "https://httpstatuses.io/404"
                });
            }

            var outbox = await db.OutboxMessages.AsNoTracking()
                .FirstOrDefaultAsync(o => o.InvoiceId == entity.Id, ct);

            var xml = outbox?.PayloadXml ?? string.Empty;
            var qr = ExtractQrCode(xml);
            var hash = outbox?.InvoiceHash ?? string.Empty;
            var outboxStatus = outbox != null ? ((OutboxStatus)outbox.Status).ToString() : "None";

            var detail = new InvoiceDetailResponse(
                Id: entity.Id,
                Uuid: entity.Uuid,
                InvoiceNumber: entity.InvoiceNumber,
                IssueDate: entity.IssueDate,
                IssueTime: entity.IssueTime,
                InvoiceTypeCode: entity.InvoiceTypeCode,
                TransactionType: entity.TransactionType,
                InvoiceType: ((InvoiceTypeCode)entity.InvoiceTypeCode).ToString(),
                TransactionTypeName: ((InvoiceTransactionType)entity.TransactionType).ToString(),
                Icv: entity.InvoiceCounterValue,
                PreviousInvoiceHash: entity.PreviousInvoiceHash,
                InvoiceHash: hash,
                LineExtensionAmount: entity.LineExtensionAmount,
                TaxExclusiveAmount: entity.TaxExclusiveAmount,
                TaxInclusiveAmount: entity.TaxInclusiveAmount,
                TotalTaxAmount: entity.TotalTaxAmount,
                PayableAmount: entity.PayableAmount,
                SellerName: entity.SellerName,
                SellerVatNumber: entity.SellerVatNumber,
                BuyerName: entity.BuyerName,
                BuyerVatNumber: entity.BuyerVatNumber,
                XmlPayload: xml,
                QrCodeBase64: qr,
                OutboxStatus: outboxStatus,
                CreatedAt: entity.CreatedAt
            );

            return Results.Ok(detail);
        });

        // GET /api/v1/invoices/chain (Full chronological hash chain)
        group.MapGet("/invoices/chain", async (
            [FromServices] ZatcaDbContext db,
            CancellationToken ct) =>
        {
            var records = await db.HashChainRecords.AsNoTracking()
                .OrderBy(r => r.Icv)
                .ToListAsync(ct);

            var items = records.Select(r => new ChainRecordItemDto(
                Icv: r.Icv,
                PreviousHash: r.PreviousHash,
                InvoiceHash: r.InvoiceHash,
                CreatedAt: r.CreatedAt
            )).ToList();

            return Results.Ok(new ChainHistoryResponse(items));
        });

        // GET /api/v1/outbox (Outbox queue status list)
        group.MapGet("/outbox", async (
            [FromServices] ZatcaDbContext db,
            [FromQuery] int? status,
            [FromQuery] int? limit,
            CancellationToken ct) =>
        {
            var maxLimit = Math.Min(limit.GetValueOrDefault(50), 200);
            var query = db.OutboxMessages.AsNoTracking();

            if (status.HasValue)
            {
                query = query.Where(o => o.Status == status.Value);
            }

            var total = await query.CountAsync(ct);
            var list = await query
                .OrderByDescending(o => o.InvoiceCounterValue)
                .Take(maxLimit)
                .ToListAsync(ct);

            var items = list.Select(o => new OutboxMessageItemDto(
                Id: o.Id,
                InvoiceId: o.InvoiceId,
                Type: o.Type,
                TypeName: ((OutboxMessageType)o.Type).ToString(),
                InvoiceHash: o.InvoiceHash,
                InvoiceCounterValue: o.InvoiceCounterValue,
                Status: o.Status,
                StatusName: ((OutboxStatus)o.Status).ToString(),
                RetryCount: o.RetryCount,
                ErrorMessage: o.ErrorMessage,
                CreatedAt: o.CreatedAt,
                ProcessedAt: o.ProcessedAt
            )).ToList();

            return Results.Ok(new OutboxListResponse(total, items));
        });

        return app;
    }

    private static string? ExtractQrCode(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return null;
        try
        {
            var doc = System.Xml.Linq.XDocument.Parse(xml);
            var nsCac = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";
            var nsCbc = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";

            var qrRef = doc.Descendants(System.Xml.Linq.XName.Get("AdditionalDocumentReference", nsCac))
                .FirstOrDefault(r => r.Element(System.Xml.Linq.XName.Get("ID", nsCbc))?.Value == "QR");

            return qrRef?.Element(System.Xml.Linq.XName.Get("Attachment", nsCac))
                ?.Element(System.Xml.Linq.XName.Get("EmbeddedDocumentBinaryObject", nsCbc))
                ?.Value.Trim();
        }
        catch
        {
            // Fallback string matching if XML parse fails
            int qrIdx = xml.IndexOf("<cbc:ID>QR</cbc:ID>", StringComparison.Ordinal);
            if (qrIdx == -1) return null;
            const string startTag = "<cbc:EmbeddedDocumentBinaryObject mimeCode=\"text/plain\">";
            const string endTag = "</cbc:EmbeddedDocumentBinaryObject>";
            int start = xml.IndexOf(startTag, qrIdx, StringComparison.Ordinal);
            if (start == -1) return null;
            start += startTag.Length;
            int end = xml.IndexOf(endTag, start, StringComparison.Ordinal);
            if (end == -1) return null;
            return xml.Substring(start, end - start).Trim();
        }
    }
}

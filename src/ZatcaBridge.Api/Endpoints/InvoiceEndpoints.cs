using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ZatcaBridge.Application.Common.Interfaces;
using ZatcaBridge.Application.Invoices.Commands;
using ZatcaBridge.Application.Services;
using ZatcaBridge.Domain.Entities;
using ZatcaBridge.Domain.Enums;
using ZatcaBridge.Domain.ValueObjects;

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
                return Results.NotFound(new { message = "No invoice hash chain records found." });
            }

            return Results.Ok(record);
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

        return app;
    }
}

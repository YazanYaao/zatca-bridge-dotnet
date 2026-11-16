using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using ZatcaBridge.Application.Common.Interfaces;
using ZatcaBridge.Domain.Cryptography;
using ZatcaBridge.Domain.Entities;
using ZatcaBridge.Domain.Enums;
using ZatcaBridge.Domain.ValueObjects;

namespace ZatcaBridge.Api.Endpoints;

/// <summary>
/// API request DTO for requesting a Compliance CSID.
/// </summary>
public sealed record RequestComplianceCsidApiRequest(
    string Otp,
    string CommonName,
    string? OrganizationUnit = null,
    string? OrganizationName = null,
    string CountryCode = "SA",
    string? SerialNumber = null,
    string? OrganizationIdentifier = null,
    string? InvoiceType = null,
    string? Location = null,
    string? Industry = null,
    string? OrganizationalUnitName = null,
    string? VatRegistrationNumber = null,
    CsidEnvironment? Environment = null
);

/// <summary>
/// API request DTO for testing compliance checks.
/// </summary>
public sealed record ComplianceCheckApiRequest(
    string? InvoiceNumber = null,
    PartyDto? Seller = null,
    PartyDto? Buyer = null
);

/// <summary>
/// API request DTO for requesting a Production CSID.
/// </summary>
public sealed record RequestProductionCsidApiRequest(
    string? ComplianceRequestId = null
);

/// <summary>
/// API response DTO for active onboarding CSID status (non-sensitive metadata).
/// </summary>
public sealed record OnboardingStatusResponse(
    bool IsActive,
    Guid? Id = null,
    string? Environment = null,
    string? RequestId = null,
    DateTimeOffset? CreatedAt = null,
    string? Message = null
);

/// <summary>
/// Minimal API route configuration for ZATCA Phase 2 developer portal and simulation CSID onboarding.
/// </summary>
public static class OnboardingEndpoints
{
    public static IEndpointRouteBuilder MapOnboardingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/onboarding")
            .WithTags("Onboarding");

        // 1. POST /api/v1/onboarding/compliance-csid
        group.MapPost("/compliance-csid", async (
            [FromBody] RequestComplianceCsidApiRequest request,
            [FromServices] IZatcaCsrGenerator csrGenerator,
            [FromServices] IZatcaOnboardingClient onboardingClient,
            CancellationToken ct) =>
        {
            ArgumentNullException.ThrowIfNull(request);

            if (string.IsNullOrWhiteSpace(request.Otp))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Invalid Argument",
                    detail: "OTP is required.",
                    type: "https://httpstatuses.io/400"
                );
            }

            var orgUnit = !string.IsNullOrWhiteSpace(request.OrganizationalUnitName)
                ? request.OrganizationalUnitName
                : (!string.IsNullOrWhiteSpace(request.OrganizationUnit) ? request.OrganizationUnit : "Riyadh Branch");

            var orgName = !string.IsNullOrWhiteSpace(request.OrganizationName)
                ? request.OrganizationName
                : "Test Company Ltd";

            var env = request.Environment ?? CsidEnvironment.Simulation;

            var vatNumber = !string.IsNullOrWhiteSpace(request.VatRegistrationNumber)
                ? request.VatRegistrationNumber
                : request.OrganizationIdentifier;

            var csrRequest = new CsrGenerationRequest(
                CommonName: request.CommonName,
                OrganizationUnit: orgUnit,
                OrganizationName: orgName,
                CountryCode: request.CountryCode ?? "SA",
                Environment: env,
                SerialNumber: request.SerialNumber,
                OrganizationIdentifier: vatNumber,
                InvoiceType: request.InvoiceType,
                Location: request.Location,
                Industry: request.Industry
            );

            var csrResult = csrGenerator.GenerateCsr(csrRequest);
            var result = await onboardingClient.RequestComplianceCsidAsync(
                csrResult.CsrPem,
                csrResult.PrivateKeyPem,
                request.Otp,
                ct
            );

            return Results.Ok(result);
        })
        .WithName("RequestComplianceCsid")
        .WithSummary("Generates secp256k1 keypair & CSR, exchanges with ZATCA Sandbox /compliance, and persists active CSID.");

        // 2. POST /api/v1/onboarding/compliance-check
        group.MapPost("/compliance-check", async (
            [FromBody] ComplianceCheckApiRequest? request,
            [FromServices] ICsidRepository csidRepo,
            [FromServices] IXadesXmlSigner xadesSigner,
            [FromServices] IZatcaOnboardingClient onboardingClient,
            CancellationToken ct) =>
        {
            var activeCsid = await csidRepo.GetActiveAsync(ct);
            if (activeCsid == null)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "No Active CSID",
                    detail: "No active Compliance CSID found. Run /compliance-csid first.",
                    type: "https://httpstatuses.io/400"
                );
            }

            var sellerParty = request?.Seller != null
                ? new Party(
                    request.Seller.Name,
                    request.Seller.VatRegistrationNumber,
                    request.Seller.Street,
                    request.Seller.BuildingNumber,
                    request.Seller.PostalZone,
                    request.Seller.City,
                    request.Seller.District,
                    request.Seller.CountryCode
                )
                : new Party(
                    "Acme Saudi Co",
                    "310123456700003",
                    "Olaya St",
                    "1234",
                    "12211",
                    "Riyadh",
                    "Olaya",
                    "SA"
                );

            var lineItems = new List<InvoiceLineItem>
            {
                new(Guid.NewGuid(), "Sample Compliance Item", 1.00m, 100.00m, 0.00m, TaxCategory.Standard)
            };

            var buyerParty = request?.Buyer != null
                ? new Party(
                    request.Buyer.Name,
                    request.Buyer.VatRegistrationNumber,
                    request.Buyer.Street,
                    request.Buyer.BuildingNumber,
                    request.Buyer.PostalZone,
                    request.Buyer.City,
                    request.Buyer.District,
                    request.Buyer.CountryCode
                )
                : new Party(
                    "Buyer Corp",
                    "310987654300003",
                    "Tahlia St",
                    "5678",
                    "12334",
                    "Riyadh",
                    "Sulaimaniyah",
                    "SA"
                );

            var invoice = new Invoice(
                id: Guid.NewGuid(),
                uuid: Guid.NewGuid(),
                invoiceNumber: request?.InvoiceNumber ?? $"INV-COMP-{DateTime.UtcNow.Ticks}",
                issueDate: DateOnly.FromDateTime(DateTime.UtcNow),
                issueTime: TimeOnly.FromDateTime(DateTime.UtcNow),
                invoiceType: InvoiceTypeCode.TaxInvoice,
                transactionType: InvoiceTransactionType.B2B,
                seller: sellerParty,
                buyer: buyerParty,
                lineItems: lineItems,
                invoiceCounterValue: 1,
                previousInvoiceHash: PreviousInvoiceHash.Genesis.Value
            );

            var signed = xadesSigner.SignInvoice(invoice, activeCsid);
            var checkResult = await onboardingClient.SubmitComplianceInvoiceAsync(
                signed.InvoiceHashBase64,
                invoice.Uuid,
                signed.SignedXml,
                ct
            );

            return Results.Ok(checkResult);
        })
        .WithName("SubmitComplianceCheck")
        .WithSummary("Signs a test invoice using the active Compliance CSID and validates against ZATCA /compliance/invoices.");

        // 3. POST /api/v1/onboarding/production-csid
        group.MapPost("/production-csid", async (
            [FromBody] RequestProductionCsidApiRequest? request,
            [FromServices] IZatcaOnboardingClient onboardingClient,
            CancellationToken ct) =>
        {
            var result = await onboardingClient.RequestProductionCsidAsync(request?.ComplianceRequestId, ct);
            return Results.Ok(result);
        })
        .WithName("RequestProductionCsid")
        .WithSummary("Promotes an active compliance CSID to a Production CSID via ZATCA /production/csids.");

        // 4. GET /api/v1/onboarding/status
        group.MapGet("/status", async (
            [FromServices] ICsidRepository csidRepo,
            CancellationToken ct) =>
        {
            var active = await csidRepo.GetActiveAsync(ct);
            if (active == null)
            {
                return Results.Ok(new OnboardingStatusResponse(
                    IsActive: false,
                    Message: "No active CSID onboarded."
                ));
            }

            return Results.Ok(new OnboardingStatusResponse(
                IsActive: active.IsActive,
                Id: active.Id,
                Environment: active.Environment.ToString(),
                RequestId: active.RequestId,
                CreatedAt: active.CreatedAt
            ));
        })
        .WithName("GetOnboardingStatus")
        .WithSummary("Retrieves non-sensitive metadata for the currently active CSID.");

        return app;
    }
}

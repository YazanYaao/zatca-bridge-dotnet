using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ZatcaBridge.Domain.Cryptography;

namespace ZatcaBridge.Application.Common.Interfaces;

/// <summary>
/// Result returned after requesting a Compliance CSID from ZATCA.
/// </summary>
public sealed record ComplianceCsidResult(
    Guid CredentialId,
    string RequestId,
    string DispositionMessage,
    CsidEnvironment Environment,
    bool IsActive
);

/// <summary>
/// Result returned after submitting a test invoice for compliance check.
/// </summary>
public sealed record ZatcaComplianceCheckResult(
    bool IsSuccess,
    string? Status,
    string? ReportingStatus,
    string? ClearanceStatus,
    IReadOnlyList<string> ErrorMessages,
    IReadOnlyList<string> WarningMessages,
    IReadOnlyList<string> InfoMessages
);

/// <summary>
/// Result returned after requesting a Production CSID from ZATCA.
/// </summary>
public sealed record ProductionCsidResult(
    Guid CredentialId,
    string RequestId,
    string DispositionMessage,
    CsidEnvironment Environment,
    bool IsActive
);

/// <summary>
/// Port for ZATCA developer portal / simulation onboarding and CSID issuance APIs.
/// </summary>
public interface IZatcaOnboardingClient
{
    Task<ComplianceCsidResult> RequestComplianceCsidAsync(
        string csrPem,
        string privateKeyPem,
        string otp,
        CancellationToken cancellationToken = default
    );

    Task<ZatcaComplianceCheckResult> SubmitComplianceInvoiceAsync(
        string invoiceHash,
        Guid uuid,
        string signedXml,
        CancellationToken cancellationToken = default
    );

    Task<ProductionCsidResult> RequestProductionCsidAsync(
        string? complianceRequestId = null,
        CancellationToken cancellationToken = default
    );
}

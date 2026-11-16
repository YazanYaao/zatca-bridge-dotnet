using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ZatcaBridge.Application.Common.Interfaces;
using ZatcaBridge.Domain.Cryptography;

namespace ZatcaBridge.Infrastructure.Gateway;

/// <summary>
/// HTTP client for ZATCA Phase 2 developer portal and simulation CSID onboarding.
/// </summary>
public class ZatcaOnboardingClient : IZatcaOnboardingClient
{
    private readonly HttpClient _httpClient;
    private readonly ICsidRepository _csidRepository;
    private readonly ZatcaGatewayOptions _options;

    public ZatcaOnboardingClient(
        HttpClient httpClient,
        ICsidRepository csidRepository,
        ZatcaGatewayOptions? options = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _csidRepository = csidRepository ?? throw new ArgumentNullException(nameof(csidRepository));
        _options = options ?? new ZatcaGatewayOptions();
    }

    public async Task<ComplianceCsidResult> RequestComplianceCsidAsync(
        string csrPem,
        string privateKeyPem,
        string otp,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(csrPem);
        ArgumentException.ThrowIfNullOrWhiteSpace(privateKeyPem);
        ArgumentException.ThrowIfNullOrWhiteSpace(otp);

        var trimmedCsr = csrPem.Trim();
        var base64Csr = trimmedCsr.StartsWith("-----BEGIN", StringComparison.OrdinalIgnoreCase)
            ? Convert.ToBase64String(Encoding.UTF8.GetBytes(trimmedCsr))
            : trimmedCsr;
        var payload = new { csr = base64Csr };
        var json = JsonSerializer.Serialize(payload);

        using var request = new HttpRequestMessage(HttpMethod.Post, "compliance")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("OTP", otp);
        request.Headers.TryAddWithoutValidation("Accept-Version", _options.AcceptVersion);
        request.Headers.TryAddWithoutValidation("Accept-Language", _options.AcceptLanguage);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"ZATCA Compliance CSID request failed with status {(int)response.StatusCode}: {content}");
        }

        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;

        var requestId = root.TryGetProperty("requestID", out var reqIdProp)
            ? (reqIdProp.ValueKind == JsonValueKind.Number ? reqIdProp.GetInt64().ToString() : reqIdProp.GetString() ?? string.Empty)
            : string.Empty;

        var dispositionMessage = root.TryGetProperty("dispositionMessage", out var dispProp)
            ? dispProp.GetString() ?? "ISSUED"
            : "ISSUED";

        var binarySecurityToken = root.GetProperty("binarySecurityToken").GetString()
            ?? throw new InvalidOperationException("ZATCA response missing binarySecurityToken.");

        var secret = root.GetProperty("secret").GetString()
            ?? throw new InvalidOperationException("ZATCA response missing secret.");

        var certPem = FormatAsPem(binarySecurityToken, "CERTIFICATE");

        var credential = new CsidCredential(
            id: Guid.NewGuid(),
            privateKeyPem: privateKeyPem,
            publicKeyPem: null,
            csrPem: csrPem,
            certificatePem: certPem,
            secret: secret,
            requestId: requestId,
            environment: CsidEnvironment.Simulation,
            isActive: true
        );

        await _csidRepository.SaveAsync(credential, cancellationToken);

        return new ComplianceCsidResult(
            credential.Id,
            credential.RequestId ?? requestId,
            dispositionMessage,
            credential.Environment,
            credential.IsActive
        );
    }

    public async Task<ZatcaComplianceCheckResult> SubmitComplianceInvoiceAsync(
        string invoiceHash,
        Guid uuid,
        string signedXml,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(invoiceHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(signedXml);

        var activeCsid = await _csidRepository.GetActiveAsync(cancellationToken)
            ?? throw new InvalidOperationException("No active CSID credential found in repository.");

        var token = StripPemEnvelope(activeCsid.CertificatePem, "CERTIFICATE");
        var basicAuth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{token}:{activeCsid.Secret}"));

        var base64Xml = Convert.ToBase64String(Encoding.UTF8.GetBytes(signedXml));
        var payload = new
        {
            invoiceHash = invoiceHash,
            uuid = uuid.ToString(),
            invoice = base64Xml
        };
        var json = JsonSerializer.Serialize(payload);

        using var request = new HttpRequestMessage(HttpMethod.Post, "compliance/invoices")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basicAuth);
        request.Headers.TryAddWithoutValidation("Accept-Version", _options.AcceptVersion);
        request.Headers.TryAddWithoutValidation("Accept-Language", _options.AcceptLanguage);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        string? status = null;
        string? reportingStatus = null;
        string? clearanceStatus = null;
        var errorMessages = new List<string>();
        var warningMessages = new List<string>();
        var infoMessages = new List<string>();

        if (!string.IsNullOrWhiteSpace(content))
        {
            try
            {
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;
                if (root.TryGetProperty("reportingStatus", out var repProp)) reportingStatus = repProp.GetString();
                if (root.TryGetProperty("clearanceStatus", out var clrProp)) clearanceStatus = clrProp.GetString();

                if (root.TryGetProperty("validationResults", out var valResults))
                {
                    if (valResults.TryGetProperty("status", out var stProp)) status = stProp.GetString();

                    ExtractMessages(valResults, "errorMessages", errorMessages);
                    ExtractMessages(valResults, "warningMessages", warningMessages);
                    ExtractMessages(valResults, "infoMessages", infoMessages);
                }
            }
            catch
            {
                // Fallback if raw non-JSON
            }
        }

        var isSuccess = response.IsSuccessStatusCode &&
            (status == "PASS" || reportingStatus == "REPORTED" || clearanceStatus == "CLEARED" || errorMessages.Count == 0);

        return new ZatcaComplianceCheckResult(
            IsSuccess: isSuccess,
            Status: status ?? (isSuccess ? "PASS" : "FAIL"),
            ReportingStatus: reportingStatus,
            ClearanceStatus: clearanceStatus,
            ErrorMessages: errorMessages,
            WarningMessages: warningMessages,
            InfoMessages: infoMessages
        );
    }

    public async Task<ProductionCsidResult> RequestProductionCsidAsync(
        string? complianceRequestId = null,
        CancellationToken cancellationToken = default)
    {
        var activeCsid = await _csidRepository.GetActiveAsync(cancellationToken)
            ?? throw new InvalidOperationException("No active Compliance CSID found in repository.");

        var compReqId = complianceRequestId ?? activeCsid.RequestId
            ?? throw new InvalidOperationException("No compliance request ID available to promote to Production CSID.");

        var token = StripPemEnvelope(activeCsid.CertificatePem, "CERTIFICATE");
        var basicAuth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{token}:{activeCsid.Secret}"));

        var payload = new { compliance_request_id = compReqId };
        var json = JsonSerializer.Serialize(payload);

        using var request = new HttpRequestMessage(HttpMethod.Post, "production/csids")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basicAuth);
        request.Headers.TryAddWithoutValidation("Accept-Version", _options.AcceptVersion);
        request.Headers.TryAddWithoutValidation("Accept-Language", _options.AcceptLanguage);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"ZATCA Production CSID request failed with status {(int)response.StatusCode}: {content}");
        }

        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;

        var requestId = root.TryGetProperty("requestID", out var reqIdProp)
            ? (reqIdProp.ValueKind == JsonValueKind.Number ? reqIdProp.GetInt64().ToString() : reqIdProp.GetString() ?? string.Empty)
            : string.Empty;

        var dispositionMessage = root.TryGetProperty("dispositionMessage", out var dispProp)
            ? dispProp.GetString() ?? "ISSUED"
            : "ISSUED";

        var binarySecurityToken = root.GetProperty("binarySecurityToken").GetString()
            ?? throw new InvalidOperationException("ZATCA response missing binarySecurityToken.");

        var secret = root.GetProperty("secret").GetString()
            ?? throw new InvalidOperationException("ZATCA response missing secret.");

        var certPem = FormatAsPem(binarySecurityToken, "CERTIFICATE");

        var newCredential = new CsidCredential(
            id: Guid.NewGuid(),
            privateKeyPem: activeCsid.PrivateKeyPem,
            publicKeyPem: activeCsid.PublicKeyPem,
            csrPem: activeCsid.CsrPem,
            certificatePem: certPem,
            secret: secret,
            requestId: requestId,
            environment: CsidEnvironment.Production,
            isActive: true
        );

        await _csidRepository.SaveAsync(newCredential, cancellationToken);

        return new ProductionCsidResult(
            newCredential.Id,
            newCredential.RequestId ?? requestId,
            dispositionMessage,
            newCredential.Environment,
            newCredential.IsActive
        );
    }

    private static void ExtractMessages(JsonElement parent, string propertyName, List<string> list)
    {
        if (parent.TryGetProperty(propertyName, out var array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in array.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var str = item.GetString();
                    if (!string.IsNullOrWhiteSpace(str)) list.Add(str);
                }
                else if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("message", out var msgProp))
                {
                    var msg = msgProp.GetString();
                    if (!string.IsNullOrWhiteSpace(msg)) list.Add(msg);
                }
            }
        }
    }

    private static string StripPemEnvelope(string? pem, string label)
    {
        if (string.IsNullOrWhiteSpace(pem)) return string.Empty;

        return pem
            .Replace($"-----BEGIN {label}-----", "", StringComparison.OrdinalIgnoreCase)
            .Replace($"-----END {label}-----", "", StringComparison.OrdinalIgnoreCase)
            .Replace("\r", "")
            .Replace("\n", "")
            .Trim();
    }

    private static string FormatAsPem(string base64, string label)
    {
        var clean = base64.Replace("\r", "").Replace("\n", "").Trim();
        var sb = new StringBuilder();
        sb.AppendLine($"-----BEGIN {label}-----");
        for (var i = 0; i < clean.Length; i += 64)
        {
            var len = Math.Min(64, clean.Length - i);
            sb.AppendLine(clean.Substring(i, len));
        }
        sb.AppendLine($"-----END {label}-----");
        return sb.ToString();
    }
}

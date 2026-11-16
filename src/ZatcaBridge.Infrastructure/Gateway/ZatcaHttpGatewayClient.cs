using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ZatcaBridge.Application.Common.Interfaces;

namespace ZatcaBridge.Infrastructure.Gateway;

/// <summary>
/// HTTP Gateway client for dispatching invoices to official ZATCA Phase 2 clearance and reporting endpoints.
/// </summary>
public class ZatcaHttpGatewayClient : IZatcaGatewayClient
{
    private readonly HttpClient _httpClient;
    private readonly ZatcaGatewayOptions _options;

    private sealed record ZatcaRequestDto(
        [property: JsonPropertyName("invoiceHash")] string InvoiceHash,
        [property: JsonPropertyName("uuid")] string Uuid,
        [property: JsonPropertyName("invoice")] string Invoice
    );

    public ZatcaHttpGatewayClient(HttpClient httpClient, ZatcaGatewayOptions? options = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? new ZatcaGatewayOptions();
    }

    /// <inheritdoc />
    public Task<ZatcaGatewayResponse> ClearInvoiceAsync(string invoiceHash, string ublXml, CancellationToken cancellationToken = default)
        => ClearInvoiceAsync(invoiceHash, ublXml, uuid: null, cancellationToken);

    /// <inheritdoc />
    public Task<ZatcaGatewayResponse> ReportInvoiceAsync(string invoiceHash, string ublXml, CancellationToken cancellationToken = default)
        => ReportInvoiceAsync(invoiceHash, ublXml, uuid: null, cancellationToken);

    /// <inheritdoc />
    public Task<ZatcaGatewayResponse> ClearInvoiceAsync(string invoiceHash, string ublXml, string? uuid, CancellationToken cancellationToken = default)
        => SendInvoiceAsync(invoiceHash, ublXml, uuid, _options.ClearanceEndpoint, isClearance: true, cancellationToken);

    /// <inheritdoc />
    public Task<ZatcaGatewayResponse> ReportInvoiceAsync(string invoiceHash, string ublXml, string? uuid, CancellationToken cancellationToken = default)
        => SendInvoiceAsync(invoiceHash, ublXml, uuid, _options.ReportingEndpoint, isClearance: false, cancellationToken);

    private async Task<ZatcaGatewayResponse> SendInvoiceAsync(
        string invoiceHash,
        string ublXml,
        string? uuid,
        string endpoint,
        bool isClearance,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(invoiceHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(ublXml);

        var effectiveUuid = ExtractUuid(ublXml, uuid);
        var base64Xml = Convert.ToBase64String(Encoding.UTF8.GetBytes(ublXml));
        var payload = new ZatcaRequestDto(invoiceHash, effectiveUuid, base64Xml);
        var json = JsonSerializer.Serialize(payload);

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        request.Headers.TryAddWithoutValidation("Accept-Version", _options.AcceptVersion);
        request.Headers.TryAddWithoutValidation("Accept-Language", _options.AcceptLanguage);

        if (!string.IsNullOrWhiteSpace(_options.AuthToken))
        {
            if (_options.AuthToken.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
            {
                request.Headers.TryAddWithoutValidation("Authorization", _options.AuthToken);
            }
            else
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.AuthToken);
            }
        }

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var contentString = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return ParseSuccessResponse(contentString, isClearance);
            }

            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                return ParseRejectionResponse(contentString, isClearance);
            }

            var isTransient = (int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.RequestTimeout;
            return new ZatcaGatewayResponse(
                IsSuccess: false,
                IsRejected: !isTransient,
                ErrorMessage: $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}: {contentString}",
                IsTransient: isTransient
            );
        }
        catch (HttpRequestException ex)
        {
            return new ZatcaGatewayResponse(
                IsSuccess: false,
                IsRejected: false,
                ErrorMessage: ex.Message,
                IsTransient: true
            );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new ZatcaGatewayResponse(
                IsSuccess: false,
                IsRejected: false,
                ErrorMessage: ex.Message,
                IsTransient: true
            );
        }
    }

    private static string ExtractUuid(string ublXml, string? explicitUuid)
    {
        if (!string.IsNullOrWhiteSpace(explicitUuid))
        {
            return explicitUuid;
        }

        var match = Regex.Match(ublXml, @"<(?:\w+:)?UUID[^>]*>([^<]+)</(?:\w+:)?UUID>", RegexOptions.IgnoreCase);
        if (match.Success && !string.IsNullOrWhiteSpace(match.Groups[1].Value))
        {
            return match.Groups[1].Value.Trim();
        }

        return Guid.NewGuid().ToString();
    }

    private static ZatcaGatewayResponse ParseSuccessResponse(string contentString, bool isClearance)
    {
        string? clearanceStatus = null;
        string? reportingStatus = null;
        string? clearedInvoice = null;

        if (!string.IsNullOrWhiteSpace(contentString))
        {
            try
            {
                using var doc = JsonDocument.Parse(contentString);
                var root = doc.RootElement;
                if (root.TryGetProperty("clearanceStatus", out var cs))
                {
                    clearanceStatus = cs.GetString();
                }
                if (root.TryGetProperty("reportingStatus", out var rs))
                {
                    reportingStatus = rs.GetString();
                }
                if (root.TryGetProperty("clearedInvoice", out var ci))
                {
                    clearedInvoice = ci.GetString();
                }
            }
            catch
            {
                // Fallback to default status on unparseable success payload
            }
        }

        if (isClearance)
        {
            if (string.Equals(clearanceStatus, "NOT_CLEARED", StringComparison.OrdinalIgnoreCase))
            {
                return new ZatcaGatewayResponse(
                    IsSuccess: false,
                    IsRejected: true,
                    ClearanceStatus: "NOT_CLEARED",
                    ErrorMessage: "Invoice not cleared by ZATCA",
                    IsTransient: false
                );
            }

            return new ZatcaGatewayResponse(
                IsSuccess: true,
                IsRejected: false,
                ClearanceStatus: clearanceStatus ?? "CLEARED",
                ClearedInvoiceXml: clearedInvoice,
                IsTransient: false
            );
        }
        else
        {
            if (string.Equals(reportingStatus, "NOT_REPORTED", StringComparison.OrdinalIgnoreCase))
            {
                return new ZatcaGatewayResponse(
                    IsSuccess: false,
                    IsRejected: true,
                    ReportingStatus: "NOT_REPORTED",
                    ErrorMessage: "Invoice not reported by ZATCA",
                    IsTransient: false
                );
            }

            return new ZatcaGatewayResponse(
                IsSuccess: true,
                IsRejected: false,
                ReportingStatus: reportingStatus ?? "REPORTED",
                IsTransient: false
            );
        }
    }

    private static ZatcaGatewayResponse ParseRejectionResponse(string contentString, bool isClearance)
    {
        var errorList = new List<string>();
        string? clearanceStatus = isClearance ? "NOT_CLEARED" : null;
        string? reportingStatus = !isClearance ? "NOT_REPORTED" : null;

        if (!string.IsNullOrWhiteSpace(contentString))
        {
            try
            {
                using var doc = JsonDocument.Parse(contentString);
                var root = doc.RootElement;

                if (root.TryGetProperty("clearanceStatus", out var cs))
                {
                    clearanceStatus = cs.GetString() ?? clearanceStatus;
                }
                if (root.TryGetProperty("reportingStatus", out var rs))
                {
                    reportingStatus = rs.GetString() ?? reportingStatus;
                }

                if (root.TryGetProperty("validationResults", out var vr) && vr.TryGetProperty("errorMessages", out var errors))
                {
                    if (errors.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var err in errors.EnumerateArray())
                        {
                            if (err.ValueKind == JsonValueKind.Object)
                            {
                                var code = err.TryGetProperty("code", out var c) ? c.GetString() : null;
                                var msg = err.TryGetProperty("message", out var m) ? m.GetString() : null;
                                if (!string.IsNullOrEmpty(code) && !string.IsNullOrEmpty(msg))
                                {
                                    errorList.Add($"{code}: {msg}");
                                }
                                else if (!string.IsNullOrEmpty(msg))
                                {
                                    errorList.Add(msg);
                                }
                            }
                            else if (err.ValueKind == JsonValueKind.String)
                            {
                                var s = err.GetString();
                                if (!string.IsNullOrEmpty(s))
                                {
                                    errorList.Add(s);
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // Fallback to raw content if JSON parsing fails
            }
        }

        var errorSummary = errorList.Count > 0 ? string.Join("; ", errorList) : contentString;
        if (string.IsNullOrWhiteSpace(errorSummary))
        {
            errorSummary = "400 Bad Request";
        }

        return new ZatcaGatewayResponse(
            IsSuccess: false,
            IsRejected: true,
            ClearanceStatus: clearanceStatus,
            ReportingStatus: reportingStatus,
            ErrorMessage: errorSummary,
            ValidationErrors: errorList.ToArray(),
            IsTransient: false
        );
    }
}

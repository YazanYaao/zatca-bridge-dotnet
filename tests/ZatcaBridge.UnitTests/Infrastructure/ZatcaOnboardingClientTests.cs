using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ZatcaBridge.Application.Common.Interfaces;
using ZatcaBridge.Domain.Cryptography;
using ZatcaBridge.Infrastructure.Gateway;

namespace ZatcaBridge.UnitTests.Infrastructure;

public class ZatcaOnboardingClientTests
{
    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastRequestBody { get; private set; }
        public HttpResponseMessage ResponseToReturn { get; set; } = new(HttpStatusCode.OK);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (request.Content != null)
            {
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }
            return ResponseToReturn;
        }
    }

    private sealed class InMemoryCsidRepository : ICsidRepository
    {
        public List<CsidCredential> Credentials { get; } = new();

        public Task<CsidCredential?> GetActiveAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Credentials.Find(c => c.IsActive));
        }

        public Task<CsidCredential?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Credentials.Find(c => c.Id == id));
        }

        public Task SaveAsync(CsidCredential credential, CancellationToken cancellationToken = default)
        {
            if (credential.IsActive)
            {
                for (var i = 0; i < Credentials.Count; i++)
                {
                    if (Credentials[i].IsActive)
                    {
                        Credentials[i] = Credentials[i] with { IsActive = false };
                    }
                }
            }

            var index = Credentials.FindIndex(c => c.Id == credential.Id);
            if (index >= 0)
            {
                Credentials[index] = credential;
            }
            else
            {
                Credentials.Add(credential);
            }
            return Task.CompletedTask;
        }

        public Task SetActiveAsync(Guid id, CancellationToken cancellationToken = default)
        {
            for (var i = 0; i < Credentials.Count; i++)
            {
                Credentials[i] = Credentials[i] with { IsActive = (Credentials[i].Id == id) };
            }
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task RequestComplianceCsidAsync_ValidCsrAndOtp_SendsHeadersAndPersistsCredential()
    {
        // Arrange
        var handler = new StubHttpMessageHandler
        {
            ResponseToReturn = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new
                    {
                        requestID = 12345,
                        dispositionMessage = "ISSUED",
                        binarySecurityToken = "dGVzdC1jZXJ0LWJhc2U2NA==",
                        secret = "csid-secret-123"
                    }),
                    Encoding.UTF8,
                    "application/json"
                )
            }
        };

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://gw-fatoora.zatca.gov.sa/e-invoicing/developer-portal") };
        var csidRepo = new InMemoryCsidRepository();
        var client = new ZatcaOnboardingClient(httpClient, csidRepo);

        var csrPem = "-----BEGIN CERTIFICATE REQUEST-----\nMIIB...test...\n-----END CERTIFICATE REQUEST-----";
        var privateKeyPem = "-----BEGIN EC PRIVATE KEY-----\nMIIE...key...\n-----END EC PRIVATE KEY-----";
        var otp = "123345";

        // Act
        var result = await client.RequestComplianceCsidAsync(csrPem, privateKeyPem, otp);

        // Assert: 1. Request verification
        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Post, handler.LastRequest.Method);
        Assert.Contains("compliance", handler.LastRequest.RequestUri?.AbsolutePath);
        Assert.True(handler.LastRequest.Headers.Contains("OTP"));
        Assert.Equal("123345", handler.LastRequest.Headers.GetValues("OTP").Single());
        Assert.True(handler.LastRequest.Headers.Contains("Accept-Version"));
        Assert.Equal("V2", handler.LastRequest.Headers.GetValues("Accept-Version").Single());

        Assert.NotNull(handler.LastRequestBody);
        Assert.DoesNotContain("-----BEGIN", handler.LastRequestBody); // Must be stripped of PEM envelope

        // Assert: 2. Result verification
        Assert.NotNull(result);
        Assert.Equal("12345", result.RequestId);
        Assert.Equal("ISSUED", result.DispositionMessage);
        Assert.Equal(CsidEnvironment.Simulation, result.Environment);
        Assert.True(result.IsActive);

        // Assert: 3. Persistence verification
        var activeCsid = await csidRepo.GetActiveAsync();
        Assert.NotNull(activeCsid);
        Assert.Equal("12345", activeCsid.RequestId);
        Assert.Equal("csid-secret-123", activeCsid.Secret);
        Assert.True(activeCsid.IsActive);
        Assert.Contains("dGVzdC1jZXJ0LWJhc2U2NA==", activeCsid.CertificatePem);
    }

    [Fact]
    public async Task SubmitComplianceInvoiceAsync_ValidSignedXml_SendsBasicAuthAndParsesResult()
    {
        // Arrange
        var handler = new StubHttpMessageHandler
        {
            ResponseToReturn = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new
                    {
                        reportingStatus = "REPORTED",
                        clearanceStatus = (string?)null,
                        validationResults = new
                        {
                            status = "PASS",
                            infoMessages = new[] { "Invoice accepted" },
                            warningMessages = Array.Empty<string>(),
                            errorMessages = Array.Empty<string>()
                        }
                    }),
                    Encoding.UTF8,
                    "application/json"
                )
            }
        };

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://gw-fatoora.zatca.gov.sa/e-invoicing/developer-portal") };
        var csidRepo = new InMemoryCsidRepository();
        await csidRepo.SaveAsync(new CsidCredential(
            id: Guid.NewGuid(),
            privateKeyPem: "priv-pem",
            certificatePem: "token-abc",
            secret: "secret-xyz",
            requestId: "12345",
            environment: CsidEnvironment.Simulation,
            isActive: true
        ));

        var client = new ZatcaOnboardingClient(httpClient, csidRepo);
        var uuid = Guid.NewGuid();

        // Act
        var result = await client.SubmitComplianceInvoiceAsync("invoiceHashBase64", uuid, "<Invoice>signed</Invoice>");

        // Assert: 1. Auth Header verification (Basic token:secret)
        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Post, handler.LastRequest.Method);
        Assert.Contains("compliance/invoices", handler.LastRequest.RequestUri?.AbsolutePath);

        var expectedCreds = Convert.ToBase64String(Encoding.UTF8.GetBytes("token-abc:secret-xyz"));
        Assert.NotNull(handler.LastRequest.Headers.Authorization);
        Assert.Equal("Basic", handler.LastRequest.Headers.Authorization.Scheme);
        Assert.Equal(expectedCreds, handler.LastRequest.Headers.Authorization.Parameter);

        // Assert: 2. Parsed response verification
        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        Assert.Equal("PASS", result.Status);
        Assert.Equal("REPORTED", result.ReportingStatus);
        Assert.Empty(result.ErrorMessages);
        Assert.Single(result.InfoMessages);
    }

    [Fact]
    public async Task RequestProductionCsidAsync_ValidComplianceRequestId_PromotesToProduction()
    {
        // Arrange
        var handler = new StubHttpMessageHandler
        {
            ResponseToReturn = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new
                    {
                        requestID = 98765,
                        dispositionMessage = "ISSUED",
                        binarySecurityToken = "cHJvZC1jZXJ0",
                        secret = "prod-secret-999"
                    }),
                    Encoding.UTF8,
                    "application/json"
                )
            }
        };

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://gw-fatoora.zatca.gov.sa/e-invoicing/developer-portal") };
        var csidRepo = new InMemoryCsidRepository();
        await csidRepo.SaveAsync(new CsidCredential(
            id: Guid.NewGuid(),
            privateKeyPem: "priv-key-pem",
            certificatePem: "comp-token",
            secret: "comp-secret",
            requestId: "12345",
            environment: CsidEnvironment.Simulation,
            isActive: true
        ));

        var client = new ZatcaOnboardingClient(httpClient, csidRepo);

        // Act
        var result = await client.RequestProductionCsidAsync("12345");

        // Assert: 1. Request verification
        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Post, handler.LastRequest.Method);
        Assert.Contains("production/csids", handler.LastRequest.RequestUri?.AbsolutePath);

        var expectedAuth = Convert.ToBase64String(Encoding.UTF8.GetBytes("comp-token:comp-secret"));
        Assert.Equal(expectedAuth, handler.LastRequest.Headers.Authorization?.Parameter);

        Assert.NotNull(handler.LastRequestBody);
        Assert.Contains("compliance_request_id", handler.LastRequestBody);

        // Assert: 2. Result verification
        Assert.NotNull(result);
        Assert.Equal("98765", result.RequestId);
        Assert.Equal(CsidEnvironment.Production, result.Environment);
        Assert.True(result.IsActive);

        // Assert: 3. Persistence verification (promoted to Production)
        var activeCsid = await csidRepo.GetActiveAsync();
        Assert.NotNull(activeCsid);
        Assert.Equal("98765", activeCsid.RequestId);
        Assert.Equal(CsidEnvironment.Production, activeCsid.Environment);
        Assert.Equal("prod-secret-999", activeCsid.Secret);
        Assert.True(activeCsid.IsActive);
    }

    [Fact]
    public async Task ZatcaHttpGatewayClient_WithActiveCsid_AttachesBasicAuthHeader()
    {
        // Arrange
        var handler = new StubHttpMessageHandler
        {
            ResponseToReturn = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new
                    {
                        clearanceStatus = "CLEARED",
                        clearedInvoice = "Y2xlYXJlZF94bWw="
                    }),
                    Encoding.UTF8,
                    "application/json"
                )
            }
        };

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://gw-fatoora.zatca.gov.sa/e-invoicing/developer-portal") };
        var csidRepo = new InMemoryCsidRepository();
        await csidRepo.SaveAsync(new CsidCredential(
            id: Guid.NewGuid(),
            privateKeyPem: "priv-pem",
            certificatePem: "token-prod-123",
            secret: "secret-prod-456",
            requestId: "98765",
            environment: CsidEnvironment.Production,
            isActive: true
        ));

        var gatewayOptions = new ZatcaGatewayOptions();
        var gatewayClient = new ZatcaHttpGatewayClient(httpClient, gatewayOptions, csidRepo);

        // Act
        var response = await gatewayClient.ClearInvoiceAsync("hash123", "<Invoice><cbc:UUID>11111111-1111-1111-1111-111111111111</cbc:UUID></Invoice>", "11111111-1111-1111-1111-111111111111");

        // Assert
        Assert.NotNull(response);
        Assert.True(response.IsSuccess);
        Assert.NotNull(handler.LastRequest);

        var expectedAuth = Convert.ToBase64String(Encoding.UTF8.GetBytes("token-prod-123:secret-prod-456"));
        Assert.NotNull(handler.LastRequest.Headers.Authorization);
        Assert.Equal("Basic", handler.LastRequest.Headers.Authorization.Scheme);
        Assert.Equal(expectedAuth, handler.LastRequest.Headers.Authorization.Parameter);
    }
}

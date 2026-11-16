using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ZatcaBridge.Application.Common.Interfaces;
using ZatcaBridge.Infrastructure.Gateway;

namespace ZatcaBridge.UnitTests.Infrastructure;

public class ZatcaHttpGatewayClientTests
{
    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastRequestBody { get; private set; }
        public HttpResponseMessage ResponseToReturn { get; set; } = new(HttpStatusCode.OK);
        public Exception? ExceptionToThrow { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (request.Content is not null)
            {
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            return ResponseToReturn;
        }
    }

    private static (ZatcaHttpGatewayClient Client, MockHttpMessageHandler Handler) CreateClient(ZatcaGatewayOptions? options = null)
    {
        var handler = new MockHttpMessageHandler();
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://gw-fatoora.zatca.gov.sa/e-invoicing/simulation/")
        };

        var client = new ZatcaHttpGatewayClient(httpClient, options ?? new ZatcaGatewayOptions());
        return (client, handler);
    }

    [Fact]
    public async Task ClearInvoiceAsync_SerializesZatcaRequestPayloadAndHeadersCorrectly()
    {
        // Arrange
        var (client, handler) = CreateClient();
        handler.ResponseToReturn = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(@"{""clearanceStatus"":""CLEARED""}", Encoding.UTF8, "application/json")
        };

        const string testHash = "NWZlY2ViNjZmZmM4NmYzOGQ5NTI3ODZjNmQ2OTZjNzljMmRiYzIzOWRkNGU5MWI4NjliMDExMzg0NmNmNmIx";
        const string testUuid = "c305a454-e7c5-470a-b5f7-64906f3680e0";
        const string testXml = "<Invoice><cbc:UUID>c305a454-e7c5-470a-b5f7-64906f3680e0</cbc:UUID></Invoice>";

        // Act
        var response = await client.ClearInvoiceAsync(testHash, testXml, testUuid);

        // Assert
        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Post, handler.LastRequest.Method);
        Assert.Contains("/invoices/clearance/single", handler.LastRequest.RequestUri?.ToString());

        // Assert Headers
        Assert.True(handler.LastRequest.Headers.Contains("Accept-Version"));
        Assert.Equal("V2", handler.LastRequest.Headers.GetValues("Accept-Version").First());
        Assert.True(handler.LastRequest.Headers.Contains("Accept-Language"));
        Assert.Equal("en", handler.LastRequest.Headers.GetValues("Accept-Language").First());

        // Assert Body
        Assert.NotNull(handler.LastRequestBody);
        using var jsonDoc = JsonDocument.Parse(handler.LastRequestBody);
        var root = jsonDoc.RootElement;
        Assert.Equal(testHash, root.GetProperty("invoiceHash").GetString());
        Assert.Equal(testUuid, root.GetProperty("uuid").GetString());

        var expectedBase64Xml = Convert.ToBase64String(Encoding.UTF8.GetBytes(testXml));
        Assert.Equal(expectedBase64Xml, root.GetProperty("invoice").GetString());
    }

    [Fact]
    public async Task ClearInvoiceAsync_ExtractsUuidFromXmlWhenNotExplicitlyProvided()
    {
        // Arrange
        var (client, handler) = CreateClient();
        handler.ResponseToReturn = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(@"{""clearanceStatus"":""CLEARED""}", Encoding.UTF8, "application/json")
        };

        const string testHash = "NWZlY2ViNjZ...";
        const string extractedUuid = "a1b2c3d4-e5f6-7890-abcd-ef1234567890";
        var testXml = $"<Invoice xmlns:cbc=\"urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2\"><cbc:UUID>{extractedUuid}</cbc:UUID></Invoice>";

        // Act: Call without explicit uuid
        await client.ClearInvoiceAsync(testHash, testXml);

        // Assert
        using var jsonDoc = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.Equal(extractedUuid, jsonDoc.RootElement.GetProperty("uuid").GetString());
    }

    [Fact]
    public async Task ClearInvoiceAsync_200OkCleared_ParsesResponseCorrectly()
    {
        // Arrange
        var (client, handler) = CreateClient();
        const string sampleClearedXml = "PD94bWwgdmVyc2lvbj0iMS4wIj8+PENsZWFyZWRJbnZvaWNlPjwvQ2xlYXJlZEludm9pY2U+";
        var jsonResponse = $$"""
        {
            "clearanceStatus": "CLEARED",
            "clearedInvoice": "{{sampleClearedXml}}",
            "validationResults": {
                "infoMessages": [],
                "warningMessages": [],
                "errorMessages": [],
                "status": "PASS"
            }
        }
        """;

        handler.ResponseToReturn = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
        };

        // Act
        var response = await client.ClearInvoiceAsync("dummyHash", "<Invoice/>");

        // Assert
        Assert.True(response.IsSuccess);
        Assert.False(response.IsRejected);
        Assert.False(response.IsTransient);
        Assert.Equal("CLEARED", response.ClearanceStatus);
        Assert.Equal(sampleClearedXml, response.ClearedInvoiceXml);
    }

    [Fact]
    public async Task ClearInvoiceAsync_202Accepted_ParsesResponseCorrectly()
    {
        // Arrange
        var (client, handler) = CreateClient();
        handler.ResponseToReturn = new HttpResponseMessage(HttpStatusCode.Accepted)
        {
            Content = new StringContent(@"{""clearanceStatus"":""CLEARED""}", Encoding.UTF8, "application/json")
        };

        // Act
        var response = await client.ClearInvoiceAsync("dummyHash", "<Invoice/>");

        // Assert
        Assert.True(response.IsSuccess);
        Assert.False(response.IsRejected);
        Assert.False(response.IsTransient);
        Assert.Equal("CLEARED", response.ClearanceStatus);
    }

    [Fact]
    public async Task ReportInvoiceAsync_200OkReported_ParsesResponseCorrectly()
    {
        // Arrange
        var (client, handler) = CreateClient();
        var jsonResponse = """
        {
            "reportingStatus": "REPORTED",
            "validationResults": {
                "infoMessages": [],
                "warningMessages": [],
                "errorMessages": [],
                "status": "PASS"
            }
        }
        """;

        handler.ResponseToReturn = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
        };

        // Act
        var response = await client.ReportInvoiceAsync("dummyHash", "<Invoice/>");

        // Assert
        Assert.NotNull(handler.LastRequest);
        Assert.Contains("/invoices/reporting/single", handler.LastRequest.RequestUri?.ToString());
        Assert.True(response.IsSuccess);
        Assert.False(response.IsRejected);
        Assert.False(response.IsTransient);
        Assert.Equal("REPORTED", response.ReportingStatus);
    }

    [Fact]
    public async Task ClearInvoiceAsync_400BadRequestValidationErrors_MapsToPermanentRejectionNonTransient()
    {
        // Arrange
        var (client, handler) = CreateClient();
        var jsonResponse = """
        {
            "clearanceStatus": "NOT_CLEARED",
            "validationResults": {
                "errorMessages": [
                    { "code": "BR-KSA-01", "message": "Invalid VAT registration number format" },
                    { "code": "BR-KSA-09", "message": "Line extension amount mismatch" }
                ],
                "status": "ERROR"
            }
        }
        """;

        handler.ResponseToReturn = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
        };

        // Act
        var response = await client.ClearInvoiceAsync("dummyHash", "<Invoice/>");

        // Assert
        Assert.False(response.IsSuccess);
        Assert.True(response.IsRejected);
        Assert.False(response.IsTransient);
        Assert.Equal("NOT_CLEARED", response.ClearanceStatus);
        Assert.NotNull(response.ErrorMessage);
        Assert.Contains("BR-KSA-01", response.ErrorMessage);
        Assert.NotNull(response.ValidationErrors);
        Assert.Equal(2, response.ValidationErrors.Length);
    }

    [Fact]
    public async Task ReportInvoiceAsync_400BadRequest_MapsToPermanentRejectionNonTransient()
    {
        // Arrange
        var (client, handler) = CreateClient();
        var jsonResponse = """
        {
            "reportingStatus": "NOT_REPORTED",
            "validationResults": {
                "errorMessages": [
                    { "code": "BR-KSA-51", "message": "Previous Invoice Hash does not match predecessor" }
                ]
            }
        }
        """;

        handler.ResponseToReturn = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
        };

        // Act
        var response = await client.ReportInvoiceAsync("dummyHash", "<Invoice/>");

        // Assert
        Assert.False(response.IsSuccess);
        Assert.True(response.IsRejected);
        Assert.False(response.IsTransient);
        Assert.Equal("NOT_REPORTED", response.ReportingStatus);
        Assert.NotNull(response.ErrorMessage);
        Assert.Contains("BR-KSA-51", response.ErrorMessage);
    }

    [Fact]
    public async Task ClearInvoiceAsync_503ServiceUnavailable_MapsToTransientRetryableFailure()
    {
        // Arrange
        var (client, handler) = CreateClient();
        handler.ResponseToReturn = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            ReasonPhrase = "Service Unavailable",
            Content = new StringContent("Server busy, please retry later.", Encoding.UTF8, "text/plain")
        };

        // Act
        var response = await client.ClearInvoiceAsync("dummyHash", "<Invoice/>");

        // Assert
        Assert.False(response.IsSuccess);
        Assert.False(response.IsRejected);
        Assert.True(response.IsTransient);
        Assert.NotNull(response.ErrorMessage);
        Assert.Contains("503", response.ErrorMessage);
    }

    [Fact]
    public async Task ClearInvoiceAsync_HttpRequestException_MapsToTransientRetryableFailure()
    {
        // Arrange
        var (client, handler) = CreateClient();
        handler.ExceptionToThrow = new HttpRequestException("Connection refused by gateway");

        // Act
        var response = await client.ClearInvoiceAsync("dummyHash", "<Invoice/>");

        // Assert
        Assert.False(response.IsSuccess);
        Assert.False(response.IsRejected);
        Assert.True(response.IsTransient);
        Assert.NotNull(response.ErrorMessage);
        Assert.Contains("Connection refused", response.ErrorMessage);
    }
}

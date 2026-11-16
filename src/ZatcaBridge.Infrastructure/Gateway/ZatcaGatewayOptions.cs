namespace ZatcaBridge.Infrastructure.Gateway;

/// <summary>
/// Configuration options for the ZATCA Phase 2 HTTP gateway integration.
/// </summary>
public class ZatcaGatewayOptions
{
    public const string SectionName = "ZatcaGateway";

    /// <summary>
    /// Base URL for ZATCA API (e.g. Developer Portal Sandbox, Simulation, or Production).
    /// Default is the ZATCA Developer Portal Sandbox environment.
    /// </summary>
    public string BaseUrl { get; set; } = "https://gw-fatoora.zatca.gov.sa/e-invoicing/developer-portal/";

    /// <summary>
    /// Relative or absolute path for single invoice B2B clearance.
    /// </summary>
    public string ClearanceEndpoint { get; set; } = "invoices/clearance/single";

    /// <summary>
    /// Relative or absolute path for single invoice B2C reporting.
    /// </summary>
    public string ReportingEndpoint { get; set; } = "invoices/reporting/single";

    /// <summary>
    /// ZATCA API version header value. Defaults to "V2".
    /// </summary>
    public string AcceptVersion { get; set; } = "V2";

    /// <summary>
    /// Language code header for ZATCA error messages. Defaults to "en".
    /// </summary>
    public string AcceptLanguage { get; set; } = "en";

    /// <summary>
    /// Optional authorization token (Bearer token or Basic CSID token).
    /// </summary>
    public string? AuthToken { get; set; }
}

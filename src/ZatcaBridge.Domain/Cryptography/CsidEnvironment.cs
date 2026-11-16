namespace ZatcaBridge.Domain.Cryptography;

/// <summary>
/// Target ZATCA environment for Cryptographic Stamp Identifier (CSID) onboarding and compliance.
/// </summary>
public enum CsidEnvironment
{
    /// <summary>
    /// ZATCA Developer Portal / Simulation / Sandbox environment (uses PREZATCA-Code-Signing).
    /// </summary>
    Simulation,

    /// <summary>
    /// ZATCA Live Production environment (uses ZATCA-Code-Signing).
    /// </summary>
    Production
}

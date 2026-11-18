using System;
using Microsoft.AspNetCore.DataProtection;
using ZatcaBridge.Application.Common.Interfaces;

namespace ZatcaBridge.Infrastructure.Security;

/// <summary>
/// Implements cryptographic protection of CSID private keys and secrets at rest using ASP.NET Core Data Protection.
/// </summary>
public class DataProtectionCsidSecretProtector : ICsidSecretProtector
{
    private const string Purpose = "ZatcaBridge.CsidCredentials.v1";
    private const string EncryptedPrefix = "enc:v1:";

    private readonly IDataProtector _protector;

    public DataProtectionCsidSecretProtector(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _protector = provider.CreateProtector(Purpose);
    }

    /// <inheritdoc />
    public string? Protect(string? plainText)
    {
        if (string.IsNullOrWhiteSpace(plainText))
        {
            return plainText;
        }

        // Avoid re-encrypting already-protected text
        if (plainText.StartsWith(EncryptedPrefix, StringComparison.Ordinal))
        {
            return plainText;
        }

        var cipher = _protector.Protect(plainText);
        return $"{EncryptedPrefix}{cipher}";
    }

    /// <inheritdoc />
    public string? Unprotect(string? cipherText)
    {
        if (string.IsNullOrWhiteSpace(cipherText))
        {
            return cipherText;
        }

        // Backward compatibility: if not prefixed with enc:v1:, it is legacy plaintext
        if (!cipherText.StartsWith(EncryptedPrefix, StringComparison.Ordinal))
        {
            return cipherText;
        }

        var rawCipher = cipherText[EncryptedPrefix.Length..];
        return _protector.Unprotect(rawCipher);
    }
}

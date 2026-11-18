namespace ZatcaBridge.Application.Common.Interfaces;

/// <summary>
/// Port for protecting (encrypting) and unprotecting (decrypting) sensitive CSID secrets and private keys at rest.
/// </summary>
public interface ICsidSecretProtector
{
    /// <summary>
    /// Protects the plaintext secret, returning an encrypted representation.
    /// </summary>
    string? Protect(string? plainText);

    /// <summary>
    /// Unprotects the encrypted secret, returning the original plaintext.
    /// </summary>
    string? Unprotect(string? cipherText);
}

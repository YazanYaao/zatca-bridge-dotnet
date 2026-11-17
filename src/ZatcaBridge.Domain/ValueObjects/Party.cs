using System;
using System.Text.RegularExpressions;

namespace ZatcaBridge.Domain.ValueObjects;

public sealed record Party
{
    private static readonly Regex KsaVatRegex = new(@"^3\d{13}3$", RegexOptions.Compiled);

    public string RegistrationName { get; }
    public string? VatNumber { get; }
    public string? StreetName { get; }
    public string? BuildingNumber { get; }
    public string? PostalCode { get; }
    public string? City { get; }
    public string? District { get; }
    public string CountryCode { get; }
    public string? Crn { get; }

    public Party(
        string registrationName,
        string? vatNumber = null,
        string? streetName = null,
        string? buildingNumber = null,
        string? postalCode = null,
        string? city = null,
        string? district = null,
        string countryCode = "SA",
        string? crn = null)
    {
        if (string.IsNullOrWhiteSpace(registrationName))
        {
            throw new ArgumentException("Registration name cannot be null or whitespace.", nameof(registrationName));
        }

        if (!string.IsNullOrWhiteSpace(vatNumber))
        {
            ValidateKsaVatNumber(vatNumber);
        }

        RegistrationName = registrationName.Trim();
        VatNumber = string.IsNullOrWhiteSpace(vatNumber) ? null : vatNumber.Trim();
        StreetName = streetName?.Trim();
        BuildingNumber = buildingNumber?.Trim();
        PostalCode = postalCode?.Trim();
        City = city?.Trim();
        District = district?.Trim();
        CountryCode = string.IsNullOrWhiteSpace(countryCode) ? "SA" : countryCode.Trim();
        Crn = crn?.Trim();
    }

    private static void ValidateKsaVatNumber(string vatNumber)
    {
        var trimmed = vatNumber.Trim();
        if (trimmed.Length != 15 || !KsaVatRegex.IsMatch(trimmed))
        {
            throw new ArgumentException($"Invalid KSA VAT Registration Number: '{vatNumber}'. It must be exactly 15 digits, start with '3', and end with '3'.", nameof(vatNumber));
        }
    }
}

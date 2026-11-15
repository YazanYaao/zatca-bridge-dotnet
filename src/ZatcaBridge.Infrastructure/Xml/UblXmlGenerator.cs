using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using ZatcaBridge.Application.Common.Interfaces;
using ZatcaBridge.Domain.Cryptography;
using ZatcaBridge.Domain.Entities;
using ZatcaBridge.Domain.Enums;
using ZatcaBridge.Domain.ValueObjects;

namespace ZatcaBridge.Infrastructure.Xml;

/// <summary>
/// Serializes domain <see cref="Invoice"/> aggregates into ZATCA Phase 2 compliant UBL 2.1 XML documents.
/// </summary>
public class UblXmlGenerator : IZatcaXmlGenerator
{
    private static readonly XNamespace NsInvoice = "urn:oasis:names:specification:ubl:schema:xsd:Invoice-2";
    private static readonly XNamespace NsCac = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";
    private static readonly XNamespace NsCbc = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";
    private static readonly XNamespace NsExt = "urn:oasis:names:specification:ubl:schema:xsd:CommonExtensionComponents-2";

    private const string CurrencyId = "SAR";

    public string GenerateXml(Invoice invoice, string? qrCodeBase64 = null)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        var doc = BuildXDocument(invoice, qrCodeBase64);

        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false), // UTF-8 without BOM
            Indent = true,
            OmitXmlDeclaration = false
        };

        using var memoryStream = new MemoryStream();
        using (var writer = XmlWriter.Create(memoryStream, settings))
        {
            doc.Save(writer);
        }

        return new UTF8Encoding(false).GetString(memoryStream.ToArray());
    }

    public byte[] GenerateXmlBytes(Invoice invoice, string? qrCodeBase64 = null)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        var xmlString = GenerateXml(invoice, qrCodeBase64);
        return new UTF8Encoding(false).GetBytes(xmlString);
    }

    private static XDocument BuildXDocument(Invoice invoice, string? qrCodeBase64)
    {
        var root = new XElement(NsInvoice + "Invoice",
            new XAttribute("xmlns", NsInvoice.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "cac", NsCac.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "cbc", NsCbc.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "ext", NsExt.NamespaceName),

            // ProfileID: reporting:1.0 (B2C) or clearance:1.0 (B2B)
            new XElement(NsCbc + "ProfileID", invoice.TransactionType == InvoiceTransactionType.B2B ? "clearance:1.0" : "reporting:1.0"),
            new XElement(NsCbc + "ID", invoice.InvoiceNumber),
            new XElement(NsCbc + "UUID", invoice.Uuid.ToString()),
            new XElement(NsCbc + "IssueDate", invoice.IssueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            new XElement(NsCbc + "IssueTime", invoice.IssueTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture)),

            // InvoiceTypeCode: 388 (Tax Invoice) with subtype name (0100000 = B2B, 0200000 = B2C)
            new XElement(NsCbc + "InvoiceTypeCode",
                new XAttribute("name", invoice.TransactionType == InvoiceTransactionType.B2B ? "0100000" : "0200000"),
                ((int)invoice.InvoiceType).ToString(CultureInfo.InvariantCulture)
            ),

            new XElement(NsCbc + "DocumentCurrencyCode", CurrencyId),
            new XElement(NsCbc + "TaxCurrencyCode", CurrencyId)
        );

        // AdditionalDocumentReference: ICV
        root.Add(new XElement(NsCac + "AdditionalDocumentReference",
            new XElement(NsCbc + "ID", "ICV"),
            new XElement(NsCbc + "UUID", invoice.InvoiceCounterValue.ToString(CultureInfo.InvariantCulture))
        ));

        // AdditionalDocumentReference: PIH
        if (!string.IsNullOrWhiteSpace(invoice.PreviousInvoiceHash))
        {
            root.Add(new XElement(NsCac + "AdditionalDocumentReference",
                new XElement(NsCbc + "ID", "PIH"),
                new XElement(NsCac + "Attachment",
                    new XElement(NsCbc + "EmbeddedDocumentBinaryObject",
                        new XAttribute("mimeCode", "text/plain"),
                        invoice.PreviousInvoiceHash
                    )
                )
            ));
        }

        // AdditionalDocumentReference: QR Code
        var qrData = qrCodeBase64 ?? TlvEncoder.EncodeToBase64(ZatcaQrCodeData.FromInvoice(invoice));
        root.Add(new XElement(NsCac + "AdditionalDocumentReference",
            new XElement(NsCbc + "ID", "QR"),
            new XElement(NsCac + "Attachment",
                new XElement(NsCbc + "EmbeddedDocumentBinaryObject",
                    new XAttribute("mimeCode", "text/plain"),
                    qrData
                )
            )
        ));

        // AccountingSupplierParty
        root.Add(BuildSupplierParty(invoice.Seller));

        // AccountingCustomerParty (if present)
        if (invoice.Buyer is not null)
        {
            root.Add(BuildCustomerParty(invoice.Buyer));
        }

        // TaxTotal
        root.Add(BuildTaxTotal(invoice));

        // LegalMonetaryTotal
        root.Add(BuildLegalMonetaryTotal(invoice.MonetaryTotal));

        // InvoiceLines
        for (int i = 0; i < invoice.LineItems.Count; i++)
        {
            root.Add(BuildInvoiceLine(invoice.LineItems[i], i + 1));
        }

        return new XDocument(new XDeclaration("1.0", "UTF-8", null), root);
    }

    private static XElement BuildSupplierParty(Party seller)
    {
        return new XElement(NsCac + "AccountingSupplierParty",
            new XElement(NsCac + "Party",
                BuildPostalAddress(seller),
                new XElement(NsCac + "PartyTaxScheme",
                    new XElement(NsCbc + "CompanyID", seller.VatNumber),
                    new XElement(NsCac + "TaxScheme",
                        new XElement(NsCbc + "ID", "VAT")
                    )
                ),
                new XElement(NsCac + "PartyLegalEntity",
                    new XElement(NsCbc + "RegistrationName", seller.RegistrationName)
                )
            )
        );
    }

    private static XElement BuildCustomerParty(Party buyer)
    {
        var partyElements = new XElement(NsCac + "Party",
            BuildPostalAddress(buyer)
        );

        if (!string.IsNullOrWhiteSpace(buyer.VatNumber))
        {
            partyElements.Add(
                new XElement(NsCac + "PartyTaxScheme",
                    new XElement(NsCbc + "CompanyID", buyer.VatNumber),
                    new XElement(NsCac + "TaxScheme",
                        new XElement(NsCbc + "ID", "VAT")
                    )
                )
            );
        }

        partyElements.Add(
            new XElement(NsCac + "PartyLegalEntity",
                new XElement(NsCbc + "RegistrationName", buyer.RegistrationName)
            )
        );

        return new XElement(NsCac + "AccountingCustomerParty", partyElements);
    }

    private static XElement BuildPostalAddress(Party party)
    {
        var address = new XElement(NsCac + "PostalAddress");

        if (!string.IsNullOrWhiteSpace(party.StreetName))
        {
            address.Add(new XElement(NsCbc + "StreetName", party.StreetName));
        }

        if (!string.IsNullOrWhiteSpace(party.BuildingNumber))
        {
            address.Add(new XElement(NsCbc + "BuildingNumber", party.BuildingNumber));
        }

        if (!string.IsNullOrWhiteSpace(party.City))
        {
            address.Add(new XElement(NsCbc + "CityName", party.City));
        }

        if (!string.IsNullOrWhiteSpace(party.PostalCode))
        {
            address.Add(new XElement(NsCbc + "PostalZone", party.PostalCode));
        }

        if (!string.IsNullOrWhiteSpace(party.District))
        {
            address.Add(new XElement(NsCbc + "CountrySubentity", party.District));
        }

        address.Add(new XElement(NsCac + "Country",
            new XElement(NsCbc + "IdentificationCode", party.CountryCode)
        ));

        return address;
    }

    private static XElement BuildTaxTotal(Invoice invoice)
    {
        var taxTotalElement = new XElement(NsCac + "TaxTotal",
            new XElement(NsCbc + "TaxAmount",
                new XAttribute("currencyID", CurrencyId),
                invoice.TotalTaxAmount.ToString("0.00", CultureInfo.InvariantCulture)
            )
        );

        foreach (var subtotal in invoice.TaxSubtotals)
        {
            var categoryElement = new XElement(NsCac + "TaxCategory",
                new XElement(NsCbc + "ID", MapTaxCategoryCode(subtotal.TaxCategory.Code)),
                new XElement(NsCbc + "Percent", (subtotal.TaxCategory.Rate * 100m).ToString("0.00", CultureInfo.InvariantCulture))
            );

            if (!string.IsNullOrWhiteSpace(subtotal.TaxCategory.ExemptionReasonCode))
            {
                categoryElement.Add(new XElement(NsCbc + "TaxExemptionReasonCode", subtotal.TaxCategory.ExemptionReasonCode));
            }

            if (!string.IsNullOrWhiteSpace(subtotal.TaxCategory.ExemptionReason))
            {
                categoryElement.Add(new XElement(NsCbc + "TaxExemptionReason", subtotal.TaxCategory.ExemptionReason));
            }

            categoryElement.Add(new XElement(NsCac + "TaxScheme",
                new XElement(NsCbc + "ID", "VAT")
            ));

            taxTotalElement.Add(new XElement(NsCac + "TaxSubtotal",
                new XElement(NsCbc + "TaxableAmount",
                    new XAttribute("currencyID", CurrencyId),
                    subtotal.TaxableAmount.ToString("0.00", CultureInfo.InvariantCulture)
                ),
                new XElement(NsCbc + "TaxAmount",
                    new XAttribute("currencyID", CurrencyId),
                    subtotal.TaxAmount.ToString("0.00", CultureInfo.InvariantCulture)
                ),
                categoryElement
            ));
        }

        return taxTotalElement;
    }

    private static XElement BuildLegalMonetaryTotal(LegalMonetaryTotal total)
    {
        return new XElement(NsCac + "LegalMonetaryTotal",
            new XElement(NsCbc + "LineExtensionAmount",
                new XAttribute("currencyID", CurrencyId),
                total.LineExtensionAmount.ToString("0.00", CultureInfo.InvariantCulture)
            ),
            new XElement(NsCbc + "TaxExclusiveAmount",
                new XAttribute("currencyID", CurrencyId),
                total.TaxExclusiveAmount.ToString("0.00", CultureInfo.InvariantCulture)
            ),
            new XElement(NsCbc + "TaxInclusiveAmount",
                new XAttribute("currencyID", CurrencyId),
                total.TaxInclusiveAmount.ToString("0.00", CultureInfo.InvariantCulture)
            ),
            new XElement(NsCbc + "AllowanceTotalAmount",
                new XAttribute("currencyID", CurrencyId),
                total.AllowanceTotalAmount.ToString("0.00", CultureInfo.InvariantCulture)
            ),
            new XElement(NsCbc + "ChargeTotalAmount",
                new XAttribute("currencyID", CurrencyId),
                total.ChargeTotalAmount.ToString("0.00", CultureInfo.InvariantCulture)
            ),
            new XElement(NsCbc + "PrepaidAmount",
                new XAttribute("currencyID", CurrencyId),
                total.PrepaidAmount.ToString("0.00", CultureInfo.InvariantCulture)
            ),
            new XElement(NsCbc + "PayableRoundingAmount",
                new XAttribute("currencyID", CurrencyId),
                total.PayableRoundingAmount.ToString("0.00", CultureInfo.InvariantCulture)
            ),
            new XElement(NsCbc + "PayableAmount",
                new XAttribute("currencyID", CurrencyId),
                total.PayableAmount.ToString("0.00", CultureInfo.InvariantCulture)
            )
        );
    }

    private static XElement BuildInvoiceLine(InvoiceLineItem item, int lineNumber)
    {
        return new XElement(NsCac + "InvoiceLine",
            new XElement(NsCbc + "ID", lineNumber.ToString(CultureInfo.InvariantCulture)),
            new XElement(NsCbc + "InvoicedQuantity",
                new XAttribute("unitCode", "PCE"),
                item.Quantity.ToString("0.00", CultureInfo.InvariantCulture)
            ),
            new XElement(NsCbc + "LineExtensionAmount",
                new XAttribute("currencyID", CurrencyId),
                item.LineExtensionAmount.ToString("0.00", CultureInfo.InvariantCulture)
            ),
            new XElement(NsCac + "TaxTotal",
                new XElement(NsCbc + "TaxAmount",
                    new XAttribute("currencyID", CurrencyId),
                    item.TaxAmount.ToString("0.00", CultureInfo.InvariantCulture)
                ),
                new XElement(NsCbc + "RoundingAmount",
                    new XAttribute("currencyID", CurrencyId),
                    item.TotalAmount.ToString("0.00", CultureInfo.InvariantCulture)
                )
            ),
            new XElement(NsCac + "Item",
                new XElement(NsCbc + "Name", item.Name),
                new XElement(NsCac + "ClassifiedTaxCategory",
                    new XElement(NsCbc + "ID", MapTaxCategoryCode(item.TaxCategory.Code)),
                    new XElement(NsCbc + "Percent", (item.TaxCategory.Rate * 100m).ToString("0.00", CultureInfo.InvariantCulture)),
                    new XElement(NsCac + "TaxScheme",
                        new XElement(NsCbc + "ID", "VAT")
                    )
                )
            ),
            new XElement(NsCac + "Price",
                new XElement(NsCbc + "PriceAmount",
                    new XAttribute("currencyID", CurrencyId),
                    item.UnitPrice.ToString("0.00", CultureInfo.InvariantCulture)
                )
            )
        );
    }

    private static string MapTaxCategoryCode(TaxCategoryCode code) => code switch
    {
        TaxCategoryCode.Standard => "S",
        TaxCategoryCode.ZeroRated => "Z",
        TaxCategoryCode.Exempt => "E",
        TaxCategoryCode.OutOfScope => "O",
        _ => "S"
    };
}

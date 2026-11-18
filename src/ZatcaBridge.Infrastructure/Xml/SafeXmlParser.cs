using System;
using System.IO;
using System.Xml;
using System.Xml.Linq;

namespace ZatcaBridge.Infrastructure.Xml;

/// <summary>
/// Provides secure XML parsing and document factory methods hardened against XML External Entity (XXE),
/// DTD processing attacks, and XML entity expansion (quadratic blowup / billion laughs).
/// </summary>
public static class SafeXmlParser
{
    /// <summary>
    /// Creates an <see cref="XmlReaderSettings"/> instance with DTD processing prohibited and external entity resolution disabled.
    /// </summary>
    public static XmlReaderSettings CreateSafeReaderSettings() => new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        MaxCharactersFromEntities = 0,
        IgnoreWhitespace = false
    };

    /// <summary>
    /// Safely parses an XML string into an <see cref="XDocument"/> with DTD and external entities blocked.
    /// </summary>
    public static XDocument ParseXDocument(string xml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xml);
        using var stringReader = new StringReader(xml);
        using var xmlReader = XmlReader.Create(stringReader, CreateSafeReaderSettings());
        return XDocument.Load(xmlReader, LoadOptions.PreserveWhitespace);
    }

    /// <summary>
    /// Creates an <see cref="XmlDocument"/> with <see cref="XmlDocument.XmlResolver"/> set to null.
    /// </summary>
    public static XmlDocument CreateSafeXmlDocument(bool preserveWhitespace = true)
    {
        return new XmlDocument
        {
            XmlResolver = null,
            PreserveWhitespace = preserveWhitespace
        };
    }
}

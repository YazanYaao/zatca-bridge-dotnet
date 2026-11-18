using System.Xml;
using ZatcaBridge.Infrastructure.Xml;

namespace ZatcaBridge.UnitTests.Infrastructure;

public class SafeXmlParserTests
{
    private const string MaliciousXxeXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE foo [
          <!ELEMENT foo ANY >
          <!ENTITY xxe SYSTEM "file:///c:/windows/win.ini" >
        ]>
        <Invoice xmlns="urn:oasis:names:specification:ubl:schema:xsd:Invoice-2">
            <ID>&xxe;</ID>
        </Invoice>
        """;

    private const string MaliciousBillionLaughsXml = """
        <?xml version="1.0"?>
        <!DOCTYPE lolz [
          <!ENTITY lol "lol">
          <!ENTITY lol2 "&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;">
          <!ENTITY lol3 "&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;">
        ]>
        <Invoice xmlns="urn:oasis:names:specification:ubl:schema:xsd:Invoice-2">
            <ID>&lol3;</ID>
        </Invoice>
        """;

    private const string ValidInvoiceXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <Invoice xmlns="urn:oasis:names:specification:ubl:schema:xsd:Invoice-2">
            <ID>INV-1001</ID>
        </Invoice>
        """;

    [Fact]
    public void ParseXDocument_WithXxePayload_ThrowsXmlException()
    {
        // Act & Assert: DtdProcessing.Prohibit must reject DTD/entities with XmlException
        var ex = Assert.Throws<XmlException>(() => SafeXmlParser.ParseXDocument(MaliciousXxeXml));
        Assert.Contains("DTD", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseXDocument_WithBillionLaughsBomb_ThrowsXmlException()
    {
        // Act & Assert
        Assert.Throws<XmlException>(() => SafeXmlParser.ParseXDocument(MaliciousBillionLaughsXml));
    }

    [Fact]
    public void ParseXDocument_WithValidXml_ReturnsParsedDocument()
    {
        // Act
        var doc = SafeXmlParser.ParseXDocument(ValidInvoiceXml);

        // Assert
        Assert.NotNull(doc.Root);
        Assert.Equal("Invoice", doc.Root.Name.LocalName);
    }

    [Fact]
    public void CreateSafeXmlDocument_WhenLoadingXxe_ThrowsXmlException()
    {
        // Arrange
        var xmlDoc = SafeXmlParser.CreateSafeXmlDocument();

        // Act & Assert
        var ex = Assert.Throws<XmlException>(() =>
        {
            using var stringReader = new StringReader(MaliciousXxeXml);
            using var xmlReader = XmlReader.Create(stringReader, SafeXmlParser.CreateSafeReaderSettings());
            xmlDoc.Load(xmlReader);
        });

        Assert.Contains("DTD", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateSafeXmlDocument_WithValidXml_LoadsCorrectly()
    {
        // Arrange
        var xmlDoc = SafeXmlParser.CreateSafeXmlDocument();
        using var stringReader = new StringReader(ValidInvoiceXml);
        using var xmlReader = XmlReader.Create(stringReader, SafeXmlParser.CreateSafeReaderSettings());

        // Act
        xmlDoc.Load(xmlReader);

        // Assert
        Assert.NotNull(xmlDoc.DocumentElement);
        Assert.Equal("Invoice", xmlDoc.DocumentElement.LocalName);
    }
}

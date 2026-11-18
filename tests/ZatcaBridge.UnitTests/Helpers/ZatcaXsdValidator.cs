using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Xml.Schema;

namespace ZatcaBridge.UnitTests.Helpers;

public static class ZatcaXsdValidator
{
    private static readonly Lazy<XmlSchemaSet> SchemaSetLazy = new(InitializeSchemaSet);

    public static XmlSchemaSet SchemaSet => SchemaSetLazy.Value;

    private static XmlSchemaSet InitializeSchemaSet()
    {
        var schemaSet = new XmlSchemaSet();

        var baseDir = Path.Combine(AppContext.BaseDirectory, "Schemas");
        if (!Directory.Exists(baseDir))
        {
            baseDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../Schemas"));
        }

        if (!Directory.Exists(baseDir))
        {
            throw new DirectoryNotFoundException($"UBL 2.1 Schemas directory not found at {baseDir}");
        }

        var readerSettings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Parse
        };

        var commonDir = Path.Combine(baseDir, "common");
        if (Directory.Exists(commonDir))
        {
            foreach (var file in Directory.GetFiles(commonDir, "*.xsd"))
            {
                using var fs = File.OpenRead(file);
                using var reader = XmlReader.Create(fs, readerSettings, file);
                var schema = XmlSchema.Read(reader, null);
                if (schema != null)
                {
                    schemaSet.Add(schema);
                }
            }
        }

        var maindocDir = Path.Combine(baseDir, "maindoc");
        var mainDocs = new[]
        {
            "UBL-Invoice-2.1.xsd",
            "UBL-CreditNote-2.1.xsd",
            "UBL-DebitNote-2.1.xsd"
        };

        foreach (var mainDoc in mainDocs)
        {
            var file = Path.Combine(maindocDir, mainDoc);
            if (File.Exists(file))
            {
                using var fs = File.OpenRead(file);
                using var reader = XmlReader.Create(fs, readerSettings, file);
                var schema = XmlSchema.Read(reader, null);
                if (schema != null)
                {
                    schemaSet.Add(schema);
                }
            }
        }

        schemaSet.Compile();
        return schemaSet;
    }

    public static (bool IsValid, IReadOnlyList<string> Errors) Validate(string xmlContent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xmlContent);

        var errors = new List<string>();
        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.LoadXml(xmlContent);
        doc.Schemas = SchemaSetLazy.Value;

        doc.Validate((_, args) =>
        {
            if (args.Severity == XmlSeverityType.Error)
            {
                errors.Add(args.Message);
            }
        });

        return (errors.Count == 0, errors);
    }
}

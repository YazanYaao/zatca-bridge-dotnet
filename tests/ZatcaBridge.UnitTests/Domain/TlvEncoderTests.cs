using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;
using ZatcaBridge.Domain.Cryptography;
using ZatcaBridge.Domain.ValueObjects;

namespace ZatcaBridge.UnitTests.Domain;

public class TlvEncoderTests
{
    [Fact]
    public void Encode_SingleTag_ReturnsCorrectTagLengthValueBytes()
    {
        // Arrange
        var tag = TlvTag.FromUtf8(1, "Test");

        // Act
        var bytes = TlvEncoder.Encode(new[] { tag });

        // Assert
        // Tag = 1 (1 byte), Length = 4 (1 byte), Value = 'T','e','s','t' (4 bytes)
        var expected = new byte[] { 0x01, 0x04, 0x54, 0x65, 0x73, 0x74 };
        Assert.Equal(expected, bytes);
    }

    [Fact]
    public void Encode_ArabicSellerName_SetsLengthToUtf8ByteCountNotStringLength()
    {
        // Arrange
        // "أكمي" is 4 characters, but 8 bytes in UTF-8
        const string arabicText = "أكمي";
        var expectedUtf8Bytes = Encoding.UTF8.GetBytes(arabicText);
        Assert.Equal(4, arabicText.Length);
        Assert.Equal(8, expectedUtf8Bytes.Length);

        var tag = TlvTag.FromUtf8(1, arabicText);

        // Act
        var encoded = TlvEncoder.Encode(new[] { tag });

        // Assert
        Assert.Equal(0x01, encoded[0]); // Tag 1
        Assert.Equal(0x08, encoded[1]); // Length must be 8 bytes, NOT 4!
        var valueBytes = encoded.Skip(2).Take(8).ToArray();
        Assert.Equal(expectedUtf8Bytes, valueBytes);
        Assert.Equal(arabicText, Encoding.UTF8.GetString(valueBytes));
    }

    [Fact]
    public void Encode_Tags1To5_GeneratesValidBase64AndMatchesExpectedOutput()
    {
        // Arrange
        const string seller = "Bobs Burgers";
        const string vatNumber = "310122393500003";
        var timestamp = new DateTimeOffset(2022, 4, 25, 15, 30, 0, TimeSpan.Zero);
        const decimal totalWithVat = 115.00m;
        const decimal vatTotal = 15.00m;

        var qrData = new ZatcaQrCodeData(seller, vatNumber, timestamp, totalWithVat, vatTotal);

        // Calculate expected bytes manually
        var expectedList = new List<byte>();

        void AppendTag(byte tag, string val)
        {
            var b = Encoding.UTF8.GetBytes(val);
            expectedList.Add(tag);
            expectedList.Add((byte)b.Length);
            expectedList.AddRange(b);
        }

        AppendTag(1, seller);
        AppendTag(2, vatNumber);
        AppendTag(3, "2022-04-25T15:30:00Z");
        AppendTag(4, "115.00");
        AppendTag(5, "15.00");

        var expectedBase64 = Convert.ToBase64String(expectedList.ToArray());

        // Act
        var base64 = TlvEncoder.EncodeToBase64(qrData);

        // Assert
        Assert.Equal(expectedBase64, base64);
    }

    [Fact]
    public void Encode_Phase2CryptographicTags6To9_IncludesTagsWithCorrectPayloads()
    {
        // Arrange
        var invoiceHash = new byte[32];
        Array.Fill(invoiceHash, (byte)0xAA);

        var signature = new byte[64];
        Array.Fill(signature, (byte)0xBB);

        var publicKey = new byte[65];
        Array.Fill(publicKey, (byte)0xCC);

        var stampSignature = new byte[64];
        Array.Fill(stampSignature, (byte)0xDD);

        var qrData = new ZatcaQrCodeData(
            sellerName: "Acme Corp",
            vatRegistrationNumber: "310123456700003",
            timestamp: new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero),
            invoiceTotal: 230.00m,
            vatTotal: 30.00m,
            invoiceHash: invoiceHash,
            digitalSignature: signature,
            publicKey: publicKey,
            certificateSignature: stampSignature
        );

        // Act
        var tags = qrData.ToTags().ToList();
        var base64 = TlvEncoder.EncodeToBase64(qrData);
        var decodedTags = TlvEncoder.Decode(Convert.FromBase64String(base64));

        // Assert
        Assert.Equal(9, tags.Count);
        Assert.Equal(9, decodedTags.Count);

        var tag6 = decodedTags.FirstOrDefault(t => t.TagNumber == 6);
        Assert.NotNull(tag6);
        Assert.Equal(invoiceHash, tag6.Value);

        var tag7 = decodedTags.FirstOrDefault(t => t.TagNumber == 7);
        Assert.NotNull(tag7);
        Assert.Equal(signature, tag7.Value);

        var tag8 = decodedTags.FirstOrDefault(t => t.TagNumber == 8);
        Assert.NotNull(tag8);
        Assert.Equal(publicKey, tag8.Value);

        var tag9 = decodedTags.FirstOrDefault(t => t.TagNumber == 9);
        Assert.NotNull(tag9);
        Assert.Equal(stampSignature, tag9.Value);
    }

    [Fact]
    public void Encode_DecimalFormatting_AlwaysOutputsTwoDecimalPlaces()
    {
        // Arrange
        var qrData = new ZatcaQrCodeData(
            "Seller",
            "310123456700003",
            new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero),
            invoiceTotal: 100m,
            vatTotal: 15.5m
        );

        // Act
        var tags = qrData.ToTags().ToDictionary(t => t.TagNumber, t => Encoding.UTF8.GetString(t.Value));

        // Assert
        Assert.Equal("100.00", tags[4]);
        Assert.Equal("15.50", tags[5]);
    }

    [Fact]
    public void Encode_ValueExceeding255Bytes_ThrowsArgumentException()
    {
        // Arrange
        var oversizedBytes = new byte[256];

        // Act & Assert
        Assert.Throws<ArgumentException>(() => TlvTag.FromBytes(1, oversizedBytes));
    }

    [Fact]
    public void Decode_ValidBase64_RoundTripsAllTagsCorrectly()
    {
        // Arrange
        var tags = new List<TlvTag>
        {
            TlvTag.FromUtf8(1, "شركة الأمل"),
            TlvTag.FromUtf8(2, "310123456700003"),
            TlvTag.FromUtf8(3, "2026-11-16T12:00:00Z"),
            TlvTag.FromDecimal(4, 575.50m),
            TlvTag.FromDecimal(5, 75.06m)
        };

        // Act
        var base64 = TlvEncoder.EncodeToBase64(tags);
        var decoded = TlvEncoder.Decode(base64);

        // Assert
        Assert.Equal(tags.Count, decoded.Count);
        for (int i = 0; i < tags.Count; i++)
        {
            Assert.Equal(tags[i].TagNumber, decoded[i].TagNumber);
            Assert.Equal(tags[i].Length, decoded[i].Length);
            Assert.Equal(tags[i].Value, decoded[i].Value);
        }
    }

    [Fact]
    public void Decode_TruncatedBytes_ThrowsArgumentException()
    {
        // Arrange: Tag 1, Length indicates 10 bytes, but only 3 bytes follow
        var corruptBytes = new byte[] { 0x01, 0x0A, 0x54, 0x65, 0x73 };

        // Act & Assert
        Assert.Throws<ArgumentException>(() => TlvEncoder.Decode(corruptBytes));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ZatcaQrCodeData_InvalidSellerName_ThrowsArgumentException(string? invalidSeller)
    {
        Assert.Throws<ArgumentException>(() =>
            new ZatcaQrCodeData(
                invalidSeller!,
                "310123456700003",
                DateTimeOffset.UtcNow,
                115.00m,
                15.00m
            ));
    }

    [Theory]
    [InlineData("123")]
    [InlineData("110123456700003")]
    [InlineData("310123456700001")]
    public void ZatcaQrCodeData_InvalidVatNumber_ThrowsArgumentException(string invalidVat)
    {
        Assert.Throws<ArgumentException>(() =>
            new ZatcaQrCodeData(
                "Valid Seller",
                invalidVat,
                DateTimeOffset.UtcNow,
                115.00m,
                15.00m
            ));
    }

    [Fact]
    public void ZatcaQrCodeData_NegativeAmounts_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ZatcaQrCodeData(
                "Valid Seller",
                "310123456700003",
                DateTimeOffset.UtcNow,
                -1.00m,
                15.00m
            ));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ZatcaQrCodeData(
                "Valid Seller",
                "310123456700003",
                DateTimeOffset.UtcNow,
                115.00m,
                -1.00m
            ));
    }
}

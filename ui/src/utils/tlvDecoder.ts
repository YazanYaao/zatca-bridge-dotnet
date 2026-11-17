export interface DecodedTlvTag {
  tag: number;
  name: string;
  description: string;
  rawLength: number;
  stringValue?: string;
  hexValue?: string;
  base64Value?: string;
  isBinary: boolean;
}

const TAG_DEFINITIONS: Record<number, { name: string; description: string; isBinary: boolean }> = {
  1: { name: 'Seller Name', description: 'Taxpayer organization or individual trade name', isBinary: false },
  2: { name: 'VAT Registration Number', description: '15-digit Tax Identification Number', isBinary: false },
  3: { name: 'Invoice Timestamp', description: 'UTC timestamp formatted as ISO 8601 (yyyy-MM-ddTHH:mm:ssZ)', isBinary: false },
  4: { name: 'Invoice Total (with VAT)', description: 'Gross payable invoice total formatted as 0.00', isBinary: false },
  5: { name: 'Total VAT', description: 'Total VAT amount formatted as 0.00', isBinary: false },
  6: { name: 'Invoice SHA-256 Digest', description: 'Cryptographic hash of the canonicalized UBL XML', isBinary: true },
  7: { name: 'ECDSA Digital Signature', description: 'Digital signature produced by CSID private key (secp256k1)', isBinary: true },
  8: { name: 'ECDSA Public Key / Cert', description: 'X.509 public key or certificate bytes of the signing key', isBinary: true },
  9: { name: 'Certificate Stamp Signature', description: 'ZATCA technical stamp signature', isBinary: true },
};

function bytesToHex(bytes: Uint8Array): string {
  return Array.from(bytes)
    .map((b) => b.toString(16).padStart(2, '0'))
    .join(' ');
}

function bytesToBase64(bytes: Uint8Array): string {
  let binary = '';
  const len = bytes.byteLength;
  for (let i = 0; i < len; i++) {
    binary += String.fromCharCode(bytes[i]);
  }
  return btoa(binary);
}

export function decodeTlvBase64(base64Payload: string): { tags: DecodedTlvTag[]; error?: string } {
  if (!base64Payload || typeof base64Payload !== 'string') {
    return { tags: [], error: 'Empty or invalid Base64 input' };
  }

  try {
    const cleanBase64 = base64Payload.replace(/\s+/g, '');
    const binaryString = atob(cleanBase64);
    const bytes = new Uint8Array(binaryString.length);
    for (let i = 0; i < binaryString.length; i++) {
      bytes[i] = binaryString.charCodeAt(i);
    }

    const tags: DecodedTlvTag[] = [];
    let offset = 0;
    const utf8Decoder = new TextDecoder('utf-8', { fatal: false });

    while (offset < bytes.length) {
      if (offset + 2 > bytes.length) {
        break;
      }

      const tagNumber = bytes[offset];
      const length = bytes[offset + 1];
      offset += 2;

      if (offset + length > bytes.length) {
        tags.push({
          tag: tagNumber,
          name: TAG_DEFINITIONS[tagNumber]?.name || `Unknown Tag ${tagNumber}`,
          description: 'Malformed tag length exceeds payload bounds',
          rawLength: length,
          isBinary: true,
        });
        break;
      }

      const valueBytes = bytes.slice(offset, offset + length);
      offset += length;

      const def = TAG_DEFINITIONS[tagNumber] || {
        name: `Tag ${tagNumber}`,
        description: 'Vendor/Custom TLV Tag',
        isBinary: tagNumber > 5,
      };

      if (!def.isBinary) {
        const decodedStr = utf8Decoder.decode(valueBytes);
        tags.push({
          tag: tagNumber,
          name: def.name,
          description: def.description,
          rawLength: length,
          stringValue: decodedStr,
          hexValue: bytesToHex(valueBytes),
          isBinary: false,
        });
      } else {
        tags.push({
          tag: tagNumber,
          name: def.name,
          description: def.description,
          rawLength: length,
          hexValue: bytesToHex(valueBytes),
          base64Value: bytesToBase64(valueBytes),
          isBinary: true,
        });
      }
    }

    return { tags };
  } catch (err: any) {
    return { tags: [], error: err.message || 'Failed to decode TLV Base64 payload' };
  }
}

using System;
using System.Buffers;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace Saham.Infrastructure.Idempotency;

/// <summary>Deterministic JSON fingerprint. No database or mutable global state.</summary>
public static class RequestFingerprint
{
    public static byte[] Compute(ReadOnlyMemory<byte> utf8Json)
    {
        if (utf8Json.Length is 0 or > 1_048_576)
            throw new ArgumentException("JSON body must contain 1..1048576 bytes.", nameof(utf8Json));

        using var document = JsonDocument.Parse(utf8Json, new JsonDocumentOptions { MaxDepth = 64 });
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
            Write(document.RootElement, writer);
        return SHA256.HashData(buffer.WrittenSpan);
    }

    private static void Write(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                // Object field order is irrelevant; array order remains significant.
                var properties = element.EnumerateObject()
                    .OrderBy(p => p.Name, StringComparer.Ordinal).ToArray();
                string? previous = null;
                foreach (var property in properties)
                {
                    // Duplicate fields are ambiguous across serializers: reject them.
                    if (previous == property.Name)
                        throw new JsonException("Duplicate JSON property.");
                    previous = property.Name;
                    writer.WritePropertyName(property.Name);
                    Write(property.Value, writer);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray()) Write(item, writer);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String: writer.WriteStringValue(element.GetString()); break;
            case JsonValueKind.Number:
                // Preserve precision. 1 and 1.0 deliberately have different fingerprints.
                writer.WriteRawValue(element.GetRawText());
                break;
            case JsonValueKind.True: writer.WriteBooleanValue(true); break;
            case JsonValueKind.False: writer.WriteBooleanValue(false); break;
            case JsonValueKind.Null: writer.WriteNullValue(); break;
            default: throw new JsonException("Unsupported JSON value.");
        }
    }
}

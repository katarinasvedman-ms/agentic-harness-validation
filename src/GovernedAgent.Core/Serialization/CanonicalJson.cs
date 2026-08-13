using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GovernedAgent.Core.Contracts;

namespace GovernedAgent.Core.Serialization;

public static class CanonicalJson
{
    public static string Serialize<T>(T value)
    {
        var element = JsonSerializer.SerializeToElement(value, ContractJson.Options);
        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions
        {
            Indented = false,
            SkipValidation = false
        }))
        {
            WriteElement(writer, element);
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    public static ActionDigestResult Digest(CanonicalAction action)
    {
        ArgumentNullException.ThrowIfNull(action);

        var canonicalJson = Serialize(action);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson));
        return new ActionDigestResult(
            "sha-256",
            Convert.ToHexStringLower(hash),
            canonicalJson);
    }

    private static void WriteElement(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element
                    .EnumerateObject()
                    .OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteElement(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteElement(writer, item);
                }
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            case JsonValueKind.Number:
                WriteNumber(writer, element);
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
            default:
                throw new JsonException(
                    $"Unsupported canonical JSON value kind '{element.ValueKind}'.");
        }
    }

    private static void WriteNumber(Utf8JsonWriter writer, JsonElement element)
    {
        if (element.TryGetInt64(out var integer))
        {
            writer.WriteNumberValue(integer);
            return;
        }

        if (element.TryGetDecimal(out var decimalValue))
        {
            writer.WriteNumberValue(decimalValue);
            return;
        }

        if (element.TryGetDouble(out var doubleValue) &&
            double.IsFinite(doubleValue))
        {
            writer.WriteNumberValue(doubleValue);
            return;
        }

        throw new JsonException("Canonical JSON does not support non-finite numbers.");
    }
}

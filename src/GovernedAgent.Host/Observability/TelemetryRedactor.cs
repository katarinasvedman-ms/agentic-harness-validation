using System.Collections;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GovernedAgent.Host.Observability;

public static partial class TelemetryRedactor
{
    private const int MaximumStringLength = 256;
    private static readonly string[] SensitiveKeyFragments =
    [
        "authorization", "cookie", "credential", "password", "passwd", "secret",
        "token", "apikey", "api_key", "privatekey", "private_key", "connectionstring",
        "connection_string", "prompt", "instruction", "toolarguments", "tool_arguments",
        "toolresult", "tool_result", "approvalartifact", "approval_artifact", "nonce"
    ];

    public static string RedactPrompt(string? prompt) => Marker("prompt", prompt);

    public static string RedactToolArguments(string? json) => RedactJson(json, "tool_arguments");

    public static string RedactToolResult(string? json) => RedactJson(json, "tool_result");

    public static string RedactApprovalMetadata(string? json) => RedactJson(json, "approval");

    public static string RedactAuditMetadata(string? json) => RedactJson(json, "audit");

    public static IReadOnlyDictionary<string, object?> RedactMetadata(
        IReadOnlyDictionary<string, object?> metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        return metadata
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .ToDictionary(
                item => item.Key,
                item => RedactValue(item.Key, item.Value),
                StringComparer.Ordinal);
    }

    private static string RedactJson(string? json, string context)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Marker(context, json);
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var sanitized = SanitizeElement(document.RootElement);
            return JsonSerializer.Serialize(sanitized);
        }
        catch (JsonException)
        {
            return Marker(context, json);
        }
    }

    private static object? SanitizeElement(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject()
                .OrderBy(property => property.Name, StringComparer.Ordinal)
                .ToDictionary(
                    property => property.Name,
                    property => IsSensitiveKey(property.Name)
                        ? (object?)Marker("sensitive", property.Value.GetRawText())
                        : SanitizeElement(property.Value),
                    StringComparer.Ordinal),
            JsonValueKind.Array => element.EnumerateArray().Select(SanitizeElement).ToArray(),
            JsonValueKind.String => SanitizeString(element.GetString()),
            JsonValueKind.Number => element.TryGetInt64(out var integer)
                ? integer
                : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };

    private static object? RedactValue(string key, object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (IsSensitiveKey(key))
        {
            return Marker("sensitive", Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture));
        }

        return value switch
        {
            string text => SanitizeString(text),
            JsonElement element => SanitizeElement(element),
            IReadOnlyDictionary<string, object?> dictionary => RedactMetadata(dictionary),
            IDictionary dictionary => dictionary.Keys.Cast<object>()
                .OrderBy(item => Convert.ToString(item, System.Globalization.CultureInfo.InvariantCulture), StringComparer.Ordinal)
                .ToDictionary(
                    item => Convert.ToString(item, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                    item => RedactValue(
                        Convert.ToString(item, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                        dictionary[item]),
                    StringComparer.Ordinal),
            IEnumerable enumerable when value is not string =>
                enumerable.Cast<object?>().Select(item => RedactValue(string.Empty, item)).ToArray(),
            bool or byte or sbyte or short or ushort or int or uint or long or ulong or
                float or double or decimal => value,
            DateTimeOffset date => date.ToString("O"),
            DateTime date => date.ToUniversalTime().ToString("O"),
            Guid guid => guid.ToString("D"),
            _ => Marker("unsupported", value.ToString())
        };
    }

    private static string SanitizeString(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value ?? string.Empty;
        }

        if (InjectionPattern().IsMatch(value))
        {
            return Marker("injection", value);
        }

        if (SecretPattern().IsMatch(value))
        {
            return Marker("secret", value);
        }

        return value.Length <= MaximumStringLength
            ? value
            : $"{value[..MaximumStringLength]}…[TRUNCATED]";
    }

    private static bool IsSensitiveKey(string key)
    {
        var normalized = key.Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(".", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
        return SensitiveKeyFragments.Any(fragment =>
            normalized.Contains(
                fragment.Replace("_", string.Empty, StringComparison.Ordinal),
                StringComparison.Ordinal));
    }

    private static string Marker(string reason, string? value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty));
        return $"[REDACTED:{reason}:sha256:{Convert.ToHexString(bytes)[..12].ToLowerInvariant()}]";
    }

    [GeneratedRegex(
        @"(?i)(ignore\s+(all\s+)?(previous|prior)\s+instructions|system\s+(prompt|override)|developer\s+message|jailbreak|do\s+not\s+tell|exfiltrat|override\s+(policy|approval)|<\s*(system|assistant)\b)")]
    private static partial Regex InjectionPattern();

    [GeneratedRegex(
        @"(?i)(bearer\s+[a-z0-9._~+/\-=]{8,}|(?:api[_-]?key|password|secret|token)\s*[:=]\s*\S+|-----BEGIN [A-Z ]*PRIVATE KEY-----)")]
    private static partial Regex SecretPattern();
}

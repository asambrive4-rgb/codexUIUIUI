using System.Text.Json;
using System.Security.Cryptography;
using CodexSwitcher.Core.Profiles;

namespace CodexSwitcher.Infrastructure.Profiles;

public sealed class CodexAuthJsonCredentialIdentityReader
    : ICredentialIdentityReader
{
    public string? TryReadAccountId(ReadOnlySpan<byte> credential)
    {
        var copy = credential.ToArray();
        try
        {
            using var document = JsonDocument.Parse(copy);
            if (!document.RootElement.TryGetProperty(
                    "tokens",
                    out var tokens) ||
                tokens.ValueKind != JsonValueKind.Object ||
                !tokens.TryGetProperty(
                    "account_id",
                    out var accountId) ||
                accountId.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var value = accountId.GetString();
            return string.IsNullOrWhiteSpace(value)
                ? null
                : value;
        }
        catch (JsonException)
        {
            return null;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(copy);
        }
    }
}

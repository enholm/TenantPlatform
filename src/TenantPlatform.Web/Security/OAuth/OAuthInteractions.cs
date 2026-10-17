using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using OpenIddict.Abstractions;

namespace TenantPlatform.Web.Security.OAuth;

public sealed record OAuthInteraction(Dictionary<string, string?> Parameters, Guid? UserId, DateTimeOffset ExpiresUtc);
public sealed class OAuthInteractions(IDataProtectionProvider protection, TimeProvider clock)
{
    private readonly IDataProtector protector = protection.CreateProtector("TenantPlatform.OAuth.Interaction.v1");
    public string Create(OpenIddictRequest request, Guid? userId) => protector.Protect(JsonSerializer.Serialize(new OAuthInteraction(
        request.GetParameters().Where(x => x.Key is not "interaction" and not "decision" and not "__RequestVerificationToken")
            .ToDictionary(x => x.Key, x => (string?)x.Value.ToString()), userId, clock.GetUtcNow().AddMinutes(10))));
    public OAuthInteraction? Read(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 16384) return null;
        try
        {
            var item = JsonSerializer.Deserialize<OAuthInteraction>(protector.Unprotect(value));
            return item is not null && item.ExpiresUtc > clock.GetUtcNow() ? item : null;
        }
        catch (Exception error) when (error is CryptographicException or JsonException or FormatException) { return null; }
    }
    public string? LoginReturn(string? value)
    {
        var item = Read(value);
        return item is { UserId: null } ? "/connect/authorize" + QueryString.Create(item.Parameters) : null;
    }
    public bool Matches(OAuthInteraction interaction, OpenIddictRequest request) =>
        interaction.Parameters.Count == request.GetParameters().Count(x => x.Key is not "interaction" and not "decision" and not "__RequestVerificationToken") &&
        interaction.Parameters.All(x => string.Equals(x.Value, request.GetParameter(x.Key)?.ToString(), StringComparison.Ordinal));
}

using Microsoft.Extensions.Options;

namespace Croesus.OwnedApi.Security;

/// <summary>
/// Validated startup configuration for the owned API. The API exists to give the reference BFF a real
/// audience to call, so a missing or malformed value fails the host rather than allowing the API to
/// start in a posture where it would accept a token it was never meant to accept.
/// </summary>
internal sealed class OwnedApiSettings
{
    private OwnedApiSettings(Uri instance, Guid tenantId, Guid clientId, string requiredScope)
    {
        Instance = instance;
        TenantId = tenantId;
        ClientId = clientId;
        RequiredScope = requiredScope;
    }

    public Uri Instance { get; }

    public Guid TenantId { get; }

    public Guid ClientId { get; }

    /// <summary>The delegated scope a caller must present. An app-only token never satisfies it.</summary>
    public string RequiredScope { get; }

    public static OwnedApiSettings Load(IConfiguration configuration)
    {
        var failures = new List<string>();

        var instanceText = configuration["AzureAd:Instance"];
        if (!Uri.TryCreate(instanceText, UriKind.Absolute, out var instance)
            || instance.Scheme != Uri.UriSchemeHttps)
        {
            failures.Add("AzureAd:Instance must be an absolute HTTPS URI.");
            instance = new Uri("https://login.microsoftonline.com/");
        }

        if (!Guid.TryParse(configuration["AzureAd:TenantId"], out var tenantId) || tenantId == Guid.Empty)
        {
            failures.Add("AzureAd:TenantId must be a non-empty GUID. The owned API is single-tenant.");
        }

        if (!Guid.TryParse(configuration["AzureAd:ClientId"], out var clientId) || clientId == Guid.Empty)
        {
            failures.Add("AzureAd:ClientId must be a non-empty GUID identifying the API registration.");
        }

        var requiredScope = configuration["Authorization:RequiredScope"];
        if (string.IsNullOrWhiteSpace(requiredScope))
        {
            failures.Add("Authorization:RequiredScope must name the delegated scope this API exposes.");
            requiredScope = string.Empty;
        }
        else if (requiredScope.Contains(' ', StringComparison.Ordinal))
        {
            failures.Add("Authorization:RequiredScope must be a single scope name, not a space-delimited list.");
        }

        if (failures.Count > 0)
        {
            throw new OptionsValidationException(
                nameof(OwnedApiSettings),
                typeof(OwnedApiSettings),
                failures);
        }

        return new OwnedApiSettings(
            new Uri(instance.AbsoluteUri.TrimEnd('/') + "/"),
            tenantId,
            clientId,
            requiredScope);
    }
}

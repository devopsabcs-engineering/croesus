namespace Croesus.BffYarp.Tests;

/// <summary>
/// Configuration used by every test host. All values are synthetic. Nothing here is, or resembles, a real
/// credential, and no test reaches Microsoft Entra.
/// </summary>
internal static class TestConfiguration
{
    public const string ClientId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
    public const string TenantId = "11111111-1111-1111-1111-111111111111";
    public const string ObjectId = "33333333-3333-3333-3333-333333333333";
    public const string DownstreamOrigin = "https://owned-api.invalid";
    public const string DownstreamScope = "api://bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb/access_as_user";

    /// <summary>
    /// An opaque value with no JWT shape. A leakage check that only looks for three dot-separated base64url
    /// segments would not catch this, which is the point of testing with it.
    /// </summary>
    public const string SentinelSecret = "croesus-sentinel-6f2a9c41d0b7e85a";

    public static string Issuer => $"https://login.microsoftonline.com/{TenantId}/v2.0";

    public static Dictionary<string, string?> Values() => new()
    {
        ["Authentication:Mode"] = "SingleTenant",
        ["AzureAd:Instance"] = "https://login.microsoftonline.com/",
        ["AzureAd:TenantId"] = TenantId,
        ["AzureAd:ClientId"] = ClientId,
        ["AzureAd:ClientSecret"] = "test-only-not-a-real-credential",
        ["AzureAd:CallbackPath"] = "/signin-oidc",
        ["DownstreamApi:Scopes:0"] = DownstreamScope,
        ["Session:AbsoluteExpiryMinutes"] = "60",
        ["Session:IdleExpiryMinutes"] = "20",
        ["ProxyPolicy:AllowedDestinationOrigins:0"] = DownstreamOrigin,
        ["ProxyPolicy:RoutePrefixes:0"] = "/api",
        ["ReverseProxy:Routes:owned-api:ClusterId"] = "owned-api",
        ["ReverseProxy:Routes:owned-api:Match:Path"] = "/api/{**catch-all}",
        ["ReverseProxy:Clusters:owned-api:Destinations:primary:Address"] = DownstreamOrigin
    };
}

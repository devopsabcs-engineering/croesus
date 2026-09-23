using System.Security.Claims;
using Croesus.OwnedApi.Models;

namespace Croesus.OwnedApi.Security;

/// <summary>
/// Turns a validated principal and the request it arrived on into the claims-only record the API returns.
/// </summary>
internal static class CallEvidenceProjection
{
    // Inbound claim mapping rewrites several of these to WS-Federation style URIs. Both forms are read so
    // the projection does not silently return null because a mapping setting changed somewhere upstream.
    private static readonly string[] TenantClaimTypes =
        ["tid", "http://schemas.microsoft.com/identity/claims/tenantid"];

    private static readonly string[] ObjectIdClaimTypes =
        ["oid", "http://schemas.microsoft.com/identity/claims/objectidentifier"];

    private static readonly string[] ClientApplicationClaimTypes =
        ["azp", "appid", "http://schemas.microsoft.com/identity/claims/appid"];

    private static readonly string[] ScopeClaimTypes =
        ["scp", "http://schemas.microsoft.com/identity/claims/scope"];

    public static CallEvidence Project(ClaimsPrincipal user, HttpRequest request) =>
        new(
            Audience: First(user, "aud"),
            Issuer: First(user, "iss"),
            TenantId: First(user, TenantClaimTypes),
            SubjectObjectId: First(user, ObjectIdClaimTypes),
            CallingApplicationId: First(user, ClientApplicationClaimTypes),
            Scopes: ScopeClaimTypes
                .SelectMany(user.FindAll)
                .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray(),
            ReceivedCookie: request.Headers.ContainsKey("Cookie"));

    private static string? First(ClaimsPrincipal user, params string[] claimTypes) =>
        claimTypes.Select(user.FindFirst).FirstOrDefault(claim => claim is not null)?.Value;
}

using System.Security.Claims;

namespace Croesus.OwnedApi.Security;

/// <summary>
/// Scope enforcement for the owned API.
/// <para>
/// The check is written out rather than delegated to an attribute because the audience boundary is the
/// thing this proof-of-concept is arguing about. A reader can see exactly which claim is consulted and
/// that an app-only token, which carries roles and no <c>scp</c>, never satisfies it.
/// </para>
/// </summary>
internal static class DelegatedScopePolicy
{
    public const string Name = "RequireDelegatedScope";

    /// <summary>Short and mapped forms of the delegated scope claim. Which one arrives depends on whether
    /// inbound claim mapping is enabled, so both are read.</summary>
    private static readonly string[] ScopeClaimTypes =
    [
        "scp",
        "http://schemas.microsoft.com/identity/claims/scope"
    ];

    public static bool HasScope(ClaimsPrincipal user, string requiredScope)
    {
        if (string.IsNullOrWhiteSpace(requiredScope) || user.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        return ScopeClaimTypes
            .SelectMany(user.FindAll)
            .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Any(scope => string.Equals(scope, requiredScope, StringComparison.Ordinal));
    }
}

using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;

namespace Croesus.ModernBff.Security;

internal sealed class TenantPolicy(BffAuthenticationSettings settings)
{
    private const string MappedTenantClaimType =
        "http://schemas.microsoft.com/identity/claims/tenantid";

    public string ValidateIssuer(
        string issuer,
        SecurityToken securityToken,
        TokenValidationParameters validationParameters)
    {
        _ = securityToken;
        _ = validationParameters;

        var tenantId = ReadIssuerTenant(issuer);
        EnsureTenantAllowed(tenantId);
        return issuer;
    }

    public void ValidatePrincipal(ClaimsPrincipal? principal, SecurityToken? securityToken)
    {
        var tenantClaim = principal?.FindFirstValue("tid")
            ?? principal?.FindFirstValue(MappedTenantClaimType);
        if (!Guid.TryParse(tenantClaim, out var tenantId) || tenantId == Guid.Empty)
        {
            throw new SecurityTokenValidationException("The validated identity has no valid tenant claim.");
        }

        var issuerTenantId = ReadIssuerTenant(securityToken?.Issuer);
        if (issuerTenantId != tenantId)
        {
            throw new SecurityTokenValidationException("The issuer tenant does not match the tenant claim.");
        }

        EnsureTenantAllowed(tenantId);
    }

    public bool IsAllowed(Guid tenantId) =>
        settings.Mode == AuthorityMode.SingleTenant
            ? settings.SingleTenantId == tenantId
            : settings.AllowedTenantIds.Contains(tenantId);

    private Guid ReadIssuerTenant(string? issuer)
    {
        if (!Uri.TryCreate(issuer, UriKind.Absolute, out var issuerUri)
            || issuerUri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(issuerUri.Authority, settings.Instance.Authority, StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(issuerUri.Query)
            || !string.IsNullOrEmpty(issuerUri.Fragment))
        {
            throw new SecurityTokenInvalidIssuerException("The token issuer is not an accepted Entra issuer.");
        }

        var segments = issuerUri.AbsolutePath.Trim('/').Split('/');
        if (segments.Length != 2
            || !Guid.TryParse(segments[0], out var tenantId)
            || tenantId == Guid.Empty
            || !string.Equals(segments[1], "v2.0", StringComparison.Ordinal))
        {
            throw new SecurityTokenInvalidIssuerException("The token issuer has an invalid tenant-specific v2 shape.");
        }

        return tenantId;
    }

    private void EnsureTenantAllowed(Guid tenantId)
    {
        if (!IsAllowed(tenantId))
        {
            throw new SecurityTokenInvalidIssuerException("The token issuer tenant is not allowed.");
        }
    }
}

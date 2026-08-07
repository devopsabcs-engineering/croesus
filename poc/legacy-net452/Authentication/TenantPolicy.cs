using System;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using Croesus.LegacyNet452.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace Croesus.LegacyNet452.Authentication
{
    public static class TenantPolicy
    {
        private const string IssuerHost = "login.microsoftonline.com";

        public static void EnsureAllowedIdentity(
            ClaimsIdentity identity,
            LegacyAuthenticationSettings settings)
        {
            if (identity == null)
            {
                throw new SecurityTokenValidationException("The validated identity is missing.");
            }

            var tenantClaim = identity.FindFirst("tid");
            Guid tenantId;
            if (tenantClaim == null || !Guid.TryParse(tenantClaim.Value, out tenantId))
            {
                throw new SecurityTokenValidationException("A valid tenant claim is required.");
            }

            if (!settings.IsTenantAllowed(tenantId))
            {
                throw new SecurityTokenValidationException("The tenant is not allowed.");
            }
        }

        public static string ValidateOrganizationsIssuer(
            string issuer,
            SecurityToken securityToken,
            TokenValidationParameters validationParameters)
        {
            var jwt = securityToken as JwtSecurityToken;
            var tenantValue = jwt == null
                ? null
                : jwt.Claims.FirstOrDefault(claim => claim.Type == "tid")?.Value;

            Guid tenantId;
            Uri issuerUri;
            if (!Guid.TryParse(tenantValue, out tenantId) ||
                !Uri.TryCreate(issuer, UriKind.Absolute, out issuerUri) ||
                !string.Equals(issuerUri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal) ||
                !string.Equals(issuerUri.Host, IssuerHost, StringComparison.OrdinalIgnoreCase) ||
                !issuerUri.IsDefaultPort ||
                !string.IsNullOrEmpty(issuerUri.Query) ||
                !string.IsNullOrEmpty(issuerUri.Fragment) ||
                !string.IsNullOrEmpty(issuerUri.UserInfo))
            {
                throw new SecurityTokenInvalidIssuerException("The issuer is not a valid Entra tenant issuer.");
            }

            var expectedPath = "/" + tenantId.ToString("D") + "/v2.0";
            if (!string.Equals(
                issuerUri.AbsolutePath.TrimEnd('/'),
                expectedPath,
                StringComparison.OrdinalIgnoreCase))
            {
                throw new SecurityTokenInvalidIssuerException("The issuer does not match the token tenant.");
            }

            return issuer;
        }
    }
}

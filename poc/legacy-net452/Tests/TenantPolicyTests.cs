using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Croesus.LegacyNet452.Authentication;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Croesus.LegacyNet452.Tests
{
    public sealed class TenantPolicyTests
    {
        [Fact]
        public void EnsureAllowedIdentityAcceptsAllowlistedTenant()
        {
            var identity = new ClaimsIdentity(
                new[] { new Claim("tid", TestSettings.AllowedTenantId) },
                "validated");

            TenantPolicy.EnsureAllowedIdentity(identity, TestSettings.Organizations());
        }

        [Fact]
        public void EnsureAllowedIdentityAcceptsMappedAllowlistedTenant()
        {
            var identity = new ClaimsIdentity(
                new[]
                {
                    new Claim(
                        "http://schemas.microsoft.com/identity/claims/tenantid",
                        TestSettings.AllowedTenantId)
                },
                "validated");

            TenantPolicy.EnsureAllowedIdentity(identity, TestSettings.Organizations());
        }

        [Fact]
        public void EnsureAllowedIdentityRejectsTenantOutsideAllowlist()
        {
            var identity = new ClaimsIdentity(
                new[] { new Claim("tid", TestSettings.HomeTenantId) },
                "validated");

            Assert.Throws<SecurityTokenValidationException>(
                () => TenantPolicy.EnsureAllowedIdentity(
                    identity,
                    TestSettings.Organizations()));
        }

        [Fact]
        public void ValidateOrganizationsIssuerBindsIssuerToTid()
        {
            var token = CreateToken(TestSettings.AllowedTenantId);
            var issuer = "https://login.microsoftonline.com/" +
                TestSettings.AllowedTenantId + "/v2.0";

            var result = TenantPolicy.ValidateOrganizationsIssuer(
                issuer,
                token,
                new TokenValidationParameters());

            Assert.Equal(issuer, result);
        }

        [Fact]
        public void ValidateOrganizationsIssuerRejectsTenantMismatch()
        {
            var token = CreateToken(TestSettings.AllowedTenantId);
            var issuer = "https://login.microsoftonline.com/" +
                TestSettings.HomeTenantId + "/v2.0";

            Assert.Throws<SecurityTokenInvalidIssuerException>(
                () => TenantPolicy.ValidateOrganizationsIssuer(
                    issuer,
                    token,
                    new TokenValidationParameters()));
        }

        [Fact]
        public void ValidateOrganizationsIssuerRejectsUnexpectedHost()
        {
            var token = CreateToken(TestSettings.AllowedTenantId);
            var issuer = "https://example.test/" + TestSettings.AllowedTenantId + "/v2.0";

            Assert.Throws<SecurityTokenInvalidIssuerException>(
                () => TenantPolicy.ValidateOrganizationsIssuer(
                    issuer,
                    token,
                    new TokenValidationParameters()));
        }

        private static JwtSecurityToken CreateToken(string tenantId)
        {
            return new JwtSecurityToken(
                claims: new List<Claim> { new Claim("tid", tenantId) });
        }
    }
}

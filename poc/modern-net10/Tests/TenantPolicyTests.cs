using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Croesus.ModernBff.Security;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace Croesus.ModernBff.Tests;

public sealed class TenantPolicyTests
{
    [Fact]
    public void OrganizationsModeAcceptsAnAllowlistedTenant()
    {
        var policy = CreateOrganizationsPolicy();
        var tenantId = Guid.Parse(TestConfiguration.TenantId);

        Assert.True(policy.IsAllowed(tenantId));
        Assert.Equal(
            $"https://login.microsoftonline.com/{tenantId:D}/v2.0",
            policy.ValidateIssuer(
                $"https://login.microsoftonline.com/{tenantId:D}/v2.0",
                securityToken: null!,
                new TokenValidationParameters()));
    }

    [Fact]
    public void OrganizationsModeRejectsATenantOutsideTheAllowlist()
    {
        var policy = CreateOrganizationsPolicy();
        var deniedTenantId = Guid.Parse(TestConfiguration.OtherTenantId);

        Assert.False(policy.IsAllowed(deniedTenantId));
        Assert.Throws<SecurityTokenInvalidIssuerException>(() => policy.ValidateIssuer(
            $"https://login.microsoftonline.com/{deniedTenantId:D}/v2.0",
            securityToken: null!,
            new TokenValidationParameters()));
    }

    [Fact]
    public void PrincipalValidationAcceptsTokenIssuerWhenPrincipalHasNoIssuerClaim()
    {
        var policy = CreateOrganizationsPolicy();
        var claims = new[] { new Claim("tid", TestConfiguration.TenantId) };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
        var token = CreateToken(TestConfiguration.TenantId);

        policy.ValidatePrincipal(principal, token);
    }

    [Fact]
    public void PrincipalValidationRejectsTokenIssuerTenantMismatch()
    {
        var policy = CreateOrganizationsPolicy();
        var claims = new[] { new Claim("tid", TestConfiguration.TenantId) };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
        var token = CreateToken(TestConfiguration.OtherTenantId);

        Assert.Throws<SecurityTokenValidationException>(
            () => policy.ValidatePrincipal(principal, token));
    }

    private static JwtSecurityToken CreateToken(string tenantId) =>
        new(issuer: $"https://login.microsoftonline.com/{tenantId}/v2.0");

    private static TenantPolicy CreateOrganizationsPolicy()
    {
        var values = TestConfiguration.SingleTenantValues();
        values["Authentication:Mode"] = "Organizations";
        values["Authentication:AllowedTenantIds:0"] = TestConfiguration.TenantId;
        values["AzureAd:TenantId"] = "organizations";
        var settings = BffAuthenticationSettings.Load(
            TestConfiguration.Build(values),
            Environments.Development);

        return new TenantPolicy(settings);
    }
}

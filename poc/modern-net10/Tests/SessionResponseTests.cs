using System.Security.Claims;
using Croesus.ModernBff.Models;

namespace Croesus.ModernBff.Tests;

public sealed class SessionResponseTests
{
    [Fact]
    public void FromPrincipalPrefersShortTenantClaimAndNormalizesGuid()
    {
        var shortTenantId = Guid.NewGuid();
        var mappedTenantId = Guid.NewGuid();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("tid", shortTenantId.ToString("B").ToUpperInvariant()),
            new Claim(
                "http://schemas.microsoft.com/identity/claims/tenantid",
                mappedTenantId.ToString("D"))
        ],
        "Test"));

        var response = SessionResponse.FromPrincipal(principal);

        Assert.Equal(shortTenantId.ToString("D"), response.TenantId);
    }
}
using System.Security.Claims;
using Croesus.OwnedApi.Security;

namespace Croesus.OwnedApi.Tests;

public class DelegatedScopePolicyTests
{
    private const string Required = "access_as_user";

    [Fact]
    public void A_token_carrying_the_scope_is_accepted()
    {
        Assert.True(DelegatedScopePolicy.HasScope(Authenticated(("scp", Required)), Required));
    }

    [Fact]
    public void The_mapped_claim_type_is_read_as_well_as_the_short_one()
    {
        var user = Authenticated(("http://schemas.microsoft.com/identity/claims/scope", Required));

        Assert.True(DelegatedScopePolicy.HasScope(user, Required));
    }

    [Fact]
    public void One_scope_among_several_is_enough()
    {
        var user = Authenticated(("scp", $"openid {Required} profile"));

        Assert.True(DelegatedScopePolicy.HasScope(user, Required));
    }

    [Fact]
    public void A_different_scope_is_rejected()
    {
        Assert.False(DelegatedScopePolicy.HasScope(Authenticated(("scp", "User.Read")), Required));
    }

    [Fact]
    public void Scope_comparison_is_case_sensitive_because_Entra_scope_names_are()
    {
        Assert.False(DelegatedScopePolicy.HasScope(Authenticated(("scp", "Access_As_User")), Required));
    }

    [Fact]
    public void An_app_only_token_is_rejected_because_it_carries_roles_and_no_scope()
    {
        var user = Authenticated(("roles", Required));

        Assert.False(DelegatedScopePolicy.HasScope(user, Required));
    }

    [Fact]
    public void An_unauthenticated_principal_is_rejected_even_when_it_asserts_the_scope()
    {
        var identity = new ClaimsIdentity([new Claim("scp", Required)]);

        Assert.False(DelegatedScopePolicy.HasScope(new ClaimsPrincipal(identity), Required));
    }

    private static ClaimsPrincipal Authenticated(params (string Type, string Value)[] claims)
    {
        var identity = new ClaimsIdentity(
            claims.Select(claim => new Claim(claim.Type, claim.Value)),
            authenticationType: "Bearer");

        return new ClaimsPrincipal(identity);
    }
}

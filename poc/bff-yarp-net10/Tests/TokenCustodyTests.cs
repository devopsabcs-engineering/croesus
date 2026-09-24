using System.Net;
using System.Security.Claims;
using Croesus.BffYarp.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Croesus.BffYarp.Tests;

/// <summary>
/// Step 3.2 evidence: the browser holds a reference, not a credential.
/// <para>
/// The weak version of this test asserts the sentinel is absent from the <c>Set-Cookie</c> header. That
/// assertion is included because the plan asks for it, but on its own it proves very little: the cookie is
/// Data Protection encrypted, so a raw sentinel would not appear in it even with the ticket store removed.
/// The load-bearing assertions are the two that follow it. The cookie payload is decrypted with the
/// application's own Data Protection provider and shown to carry only the session-key claim, and the
/// distributed cache is shown to hold the ticket that carries the tokens. Removing
/// <c>CookieAuthenticationOptions.SessionStore</c> flips both of those.
/// </para>
/// </summary>
public sealed class TokenCustodyTests(BffFactory factory) : IClassFixture<BffFactory>
{
    private const string SessionIdClaim = "Microsoft.AspNetCore.Authentication.Cookies-SessionId";

    [Fact]
    public async Task SignIn_SetCookieHeader_DoesNotContainSentinelSecret()
    {
        var client = factory.CreateSecureClient();

        var response = await client.GetAsync(TestSignInStartupFilter.SignInPath);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var setCookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.DoesNotContain(TestConfiguration.SentinelSecret, setCookie, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SignIn_CookieIsHardened()
    {
        var client = factory.CreateSecureClient();

        var response = await client.GetAsync(TestSignInStartupFilter.SignInPath);
        var setCookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));

        Assert.StartsWith("__Host-Croesus.BffYarp.Session=", setCookie, StringComparison.Ordinal);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SignIn_DecryptedCookieTicket_CarriesOnlyASessionReference()
    {
        var client = factory.CreateSecureClient();
        var response = await client.GetAsync(TestSignInStartupFilter.SignInPath);
        var ticket = UnprotectSessionTicket(response);

        // The cookie ticket holds exactly one claim, and that claim is the store key.
        var claim = Assert.Single(ticket.Principal.Claims);
        Assert.Equal(SessionIdClaim, claim.Type);
        Assert.StartsWith(ServerTicketStore.KeyPrefix, claim.Value, StringComparison.Ordinal);

        Assert.Null(ticket.Properties.GetTokenValue("access_token"));
        Assert.Null(ticket.Properties.GetTokenValue("refresh_token"));
    }

    [Fact]
    public async Task SignIn_TokensAreHeldInTheDistributedTicketStore()
    {
        var client = factory.CreateSecureClient();
        var response = await client.GetAsync(TestSignInStartupFilter.SignInPath);
        var cookieTicket = UnprotectSessionTicket(response);
        var storeKey = cookieTicket.Principal.FindFirstValue(SessionIdClaim)!;

        var cache = factory.Services.GetRequiredService<IDistributedCache>();
        var stored = cache.Get(storeKey);

        Assert.NotNull(stored);
        var serverTicket = TicketSerializer.Default.Deserialize(stored);
        Assert.NotNull(serverTicket);
        Assert.Equal(TestConfiguration.SentinelSecret, serverTicket.Properties.GetTokenValue("access_token"));
        Assert.Equal(TestConfiguration.ObjectId, serverTicket.Principal.FindFirstValue("oid"));
    }

    [Fact]
    public void SaveTokensRemainsFalseAndMapInboundClaimsIsDisabled()
    {
        var options = factory.Services
            .GetRequiredService<IOptionsMonitor<Microsoft.AspNetCore.Authentication.OpenIdConnect.OpenIdConnectOptions>>()
            .Get(Microsoft.AspNetCore.Authentication.OpenIdConnect.OpenIdConnectDefaults.AuthenticationScheme);

        Assert.False(options.SaveTokens);
        Assert.False(options.MapInboundClaims);
        Assert.True(options.UsePkce);
    }

    [Fact]
    public void InboundClaimNamesAreNotRewritten()
    {
        // The option alone did not stop the rename, so the static maps are the assertion that matters.
        _ = factory.Services;

        Assert.Empty(System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler.DefaultInboundClaimTypeMap);
        Assert.Empty(Microsoft.IdentityModel.JsonWebTokens.JsonWebTokenHandler.DefaultInboundClaimTypeMap);
    }

    [Fact]
    public void SessionStoreIsTheServerTicketStore()
    {
        var options = factory.Services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);

        Assert.IsType<ServerTicketStore>(options.SessionStore);
    }

    private AuthenticationTicket UnprotectSessionTicket(HttpResponseMessage response)
    {
        var setCookie = response.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith("__Host-Croesus.BffYarp.Session=", StringComparison.Ordinal));
        var encoded = setCookie.Split(';')[0].Split('=', 2)[1];

        var protector = factory.Services
            .GetRequiredService<IDataProtectionProvider>()
            .CreateProtector(
                "Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationMiddleware",
                CookieAuthenticationDefaults.AuthenticationScheme,
                "v2");

        var ticket = new TicketDataFormat(protector).Unprotect(encoded);
        Assert.NotNull(ticket);
        return ticket;
    }
}

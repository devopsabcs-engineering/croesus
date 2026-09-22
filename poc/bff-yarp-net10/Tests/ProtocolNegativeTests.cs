using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Web;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Croesus.BffYarp.Tests;

/// <summary>
/// Hosts the application with a synthetic identity provider: a static OpenID Connect configuration and a
/// stubbed token endpoint. No test in this file reaches Microsoft Entra, and none needs a tenant, a
/// registration, or a credential.
/// <para>
/// One deliberate limitation is recorded here rather than glossed over. Microsoft.Identity.Web's own
/// authorization-code redemption is replaced with a no-op, so the OpenID Connect handler performs the
/// redemption against the stub. These tests therefore exercise state, correlation, nonce, and callback
/// replay. They do not exercise the library's redemption path or Microsoft Entra's single-use enforcement
/// of an authorization code, and they are not evidence about either.
/// </para>
/// </summary>
public class SyntheticOidcFactory : BffFactory
{
    public static readonly SymmetricSecurityKey SigningKey =
        new(Encoding.UTF8.GetBytes("synthetic-test-signing-key-material-0123456789abcdef"));

    /// <summary>The nonce the stubbed token endpoint puts into the minted ID token.</summary>
    public string? IdTokenNonce { get; set; }

    public int TokenEndpointCalls { get; private set; }

    /// <summary>The cause of the most recent callback rejection, so a test can attribute it to a control.</summary>
    public Exception? LastRemoteFailure { get; private set; }

    protected override void ConfigureAdditionalTestServices(IServiceCollection services)
    {
        services.PostConfigure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme, options =>
        {
            var configuration = new OpenIdConnectConfiguration
            {
                Issuer = TestConfiguration.Issuer,
                AuthorizationEndpoint = $"https://login.microsoftonline.com/{TestConfiguration.TenantId}/oauth2/v2.0/authorize",
                TokenEndpoint = $"https://login.microsoftonline.com/{TestConfiguration.TenantId}/oauth2/v2.0/token",
                EndSessionEndpoint = $"https://login.microsoftonline.com/{TestConfiguration.TenantId}/oauth2/v2.0/logout"
            };
            configuration.SigningKeys.Add(SigningKey);

            options.Configuration = configuration;
            options.ConfigurationManager =
                new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
            options.TokenValidationParameters.IssuerSigningKey = SigningKey;
            options.TokenValidationParameters.ValidateIssuerSigningKey = true;
            options.TokenValidationParameters.ValidAudience = TestConfiguration.ClientId;
            options.Backchannel = new HttpClient(new StubTokenEndpoint(this));
            options.Events.OnAuthorizationCodeReceived = _ => Task.CompletedTask;

            var applicationRemoteFailure = options.Events.OnRemoteFailure;
            options.Events.OnRemoteFailure = context =>
            {
                LastRemoteFailure = context.Failure;
                return applicationRemoteFailure(context);
            };
        });
    }

    internal string MintIdToken(string? nonce)
    {
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(
            issuer: TestConfiguration.Issuer,
            audience: TestConfiguration.ClientId,
            claims:
            [
                new Claim("sub", TestConfiguration.ObjectId),
                new Claim("oid", TestConfiguration.ObjectId),
                new Claim("tid", TestConfiguration.TenantId),
                new Claim("name", "Test User"),
                new Claim("amr", "pwd"),
                new Claim("auth_time", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString()),
                new Claim("iat", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
                new Claim("nonce", nonce ?? "unset")
            ],
            notBefore: now.AddMinutes(-1),
            expires: now.AddMinutes(10),
            signingCredentials: new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed class StubTokenEndpoint(SyntheticOidcFactory owner) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            owner.TokenEndpointCalls++;
            var payload = JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["token_type"] = "Bearer",
                ["expires_in"] = 3600,
                ["access_token"] = "synthetic-access-token",
                ["id_token"] = owner.MintIdToken(owner.IdTokenNonce)
            });

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            });
        }
    }
}

/// <summary>
/// Step 3.6 evidence: the callback rejects what it must. Each test has a matching positive control in
/// <see cref="Callback_WithMatchingStateAndNonce_IsAccepted"/>, so a rejection cannot be mistaken for the
/// pipeline simply refusing everything.
/// </summary>
public sealed class ProtocolNegativeTests
{
    [Fact]
    public async Task Callback_WithMatchingStateAndNonce_IsAccepted()
    {
        using var factory = new SyntheticOidcFactory();
        var client = factory.CreateSecureClient();
        var challenge = await StartSignInAsync(client);
        factory.IdTokenNonce = challenge.Nonce;

        var response = await client.GetAsync($"/signin-oidc?code=synthetic-code&state={Uri.EscapeDataString(challenge.State)}");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(1, factory.TokenEndpointCalls);
        Assert.Contains(
            response.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith("__Host-Croesus.BffYarp.Session=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Callback_WithAMismatchedState_IsRejected()
    {
        using var factory = new SyntheticOidcFactory();
        var client = factory.CreateSecureClient();
        await StartSignInAsync(client);

        var response = await client.GetAsync("/signin-oidc?code=synthetic-code&state=a-state-this-application-never-issued");

        await AssertRejectedAsync(response);
        Assert.Contains("State", factory.LastRemoteFailure?.Message ?? string.Empty, StringComparison.Ordinal);
        Assert.Equal(0, factory.TokenEndpointCalls);
    }

    [Fact]
    public async Task Callback_WithNoState_IsRejected()
    {
        using var factory = new SyntheticOidcFactory();
        var client = factory.CreateSecureClient();
        await StartSignInAsync(client);

        var response = await client.GetAsync("/signin-oidc?code=synthetic-code");

        await AssertRejectedAsync(response);
        Assert.Contains("message.State is null or empty", factory.LastRemoteFailure?.Message ?? string.Empty, StringComparison.Ordinal);
        Assert.Equal(0, factory.TokenEndpointCalls);
    }

    [Fact]
    public async Task Callback_WithoutTheCorrelationCookie_IsRejected()
    {
        using var factory = new SyntheticOidcFactory();
        var client = factory.CreateSecureClient();
        var challenge = await StartSignInAsync(client);
        factory.IdTokenNonce = challenge.Nonce;

        // A caller that replays a captured state value from a different browser has no correlation cookie.
        using var freshClient = factory.CreateSecureClient();
        var response = await freshClient.GetAsync(
            $"/signin-oidc?code=synthetic-code&state={Uri.EscapeDataString(challenge.State)}");

        await AssertRejectedAsync(response);
        Assert.Contains("Correlation failed", factory.LastRemoteFailure?.Message ?? string.Empty, StringComparison.Ordinal);
        Assert.Equal(0, factory.TokenEndpointCalls);
    }

    [Fact]
    public async Task Callback_WithAMismatchedNonce_IsRejected()
    {
        using var factory = new SyntheticOidcFactory();
        var client = factory.CreateSecureClient();
        var challenge = await StartSignInAsync(client);
        factory.IdTokenNonce = "a-nonce-this-application-never-issued";

        var response = await client.GetAsync(
            $"/signin-oidc?code=synthetic-code&state={Uri.EscapeDataString(challenge.State)}");

        await AssertRejectedAsync(response);
        AssertFailedBecauseOfNonce(factory);
        Assert.Equal(1, factory.TokenEndpointCalls);
        Assert.DoesNotContain(
            response.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies : [],
            value => value.StartsWith("__Host-Croesus.BffYarp.Session=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Callback_WithNoNonce_IsRejected()
    {
        using var factory = new SyntheticOidcFactory();
        var client = factory.CreateSecureClient();
        var challenge = await StartSignInAsync(client);
        factory.IdTokenNonce = null;

        var response = await client.GetAsync(
            $"/signin-oidc?code=synthetic-code&state={Uri.EscapeDataString(challenge.State)}");

        await AssertRejectedAsync(response);
        AssertFailedBecauseOfNonce(factory);
    }

    [Fact]
    public async Task ReplayedCallback_IsRejectedTheSecondTime()
    {
        using var factory = new SyntheticOidcFactory();
        var client = factory.CreateSecureClient();
        var challenge = await StartSignInAsync(client);
        factory.IdTokenNonce = challenge.Nonce;
        var callback = $"/signin-oidc?code=synthetic-code&state={Uri.EscapeDataString(challenge.State)}";

        var first = await client.GetAsync(callback);
        var replay = await client.GetAsync(callback);

        Assert.Equal(HttpStatusCode.Found, first.StatusCode);
        await AssertRejectedAsync(replay);
        Assert.Contains("Correlation failed", factory.LastRemoteFailure?.Message ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProtocolCookies_AreConfiguredSeparatelyFromTheSessionCookie()
    {
        using var factory = new SyntheticOidcFactory();
        var client = factory.CreateSecureClient();

        var challengeResponse = await client.GetAsync("/bff/login");
        var protocolCookies = challengeResponse.Headers.GetValues("Set-Cookie").ToArray();

        // The nonce and correlation cookies protect a single cross-site callback. Their SameSite setting is
        // a separate decision from the session cookie's Lax setting and is asserted separately here.
        var correlation = Assert.Single(protocolCookies, value => value.StartsWith(".AspNetCore.Correlation.", StringComparison.Ordinal));
        var nonce = Assert.Single(protocolCookies, value => value.StartsWith(".AspNetCore.OpenIdConnect.Nonce.", StringComparison.Ordinal));

        foreach (var cookie in new[] { correlation, nonce })
        {
            Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("samesite=none", cookie, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotContain(
            protocolCookies,
            value => value.StartsWith("__Host-Croesus.BffYarp.Session=", StringComparison.Ordinal));
    }

    private static async Task<(string State, string Nonce)> StartSignInAsync(HttpClient client)
    {
        var response = await client.GetAsync("/bff/login");
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        var query = HttpUtility.ParseQueryString(response.Headers.Location!.Query);

        var state = query["state"];
        var nonce = query["nonce"];
        Assert.False(string.IsNullOrEmpty(state));
        Assert.False(string.IsNullOrEmpty(nonce));
        return (state, nonce);
    }

    private static async Task AssertRejectedAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("authentication_failed", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    private static void AssertFailedBecauseOfNonce(SyntheticOidcFactory factory)
    {
        var failure = factory.LastRemoteFailure;
        Assert.NotNull(failure);
        var flattened = failure is AggregateException aggregate
            ? aggregate.Flatten().InnerExceptions.Cast<Exception>().ToArray()
            : [failure];
        Assert.Contains(flattened, inner => inner is OpenIdConnectProtocolInvalidNonceException);
    }
}

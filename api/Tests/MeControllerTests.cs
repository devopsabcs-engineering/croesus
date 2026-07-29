using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Graph;
using Microsoft.Identity.Client;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Croesus.Api.Tests;

/// <summary>
/// Exercises the happy path of <c>GET /api/me</c> end to end through the real routing, the real
/// <c>MeController</c>, and the real <c>[Authorize]</c>/<c>[RequiredScope]</c> gate, with only the two external
/// trust boundaries replaced by deterministic stand-ins:
/// <list type="bullet">
/// <item>the JWT bearer signing anchor (local HS256 key, fixed valid audience) so no Microsoft Entra call
/// happens;</item>
/// <item><see cref="ITokenAcquisition"/> (returns a canned On-Behalf-Of result) so no live token exchange
/// happens;</item>
/// <item><see cref="GraphServiceClient"/> (returns a canned <c>/me</c> profile) so no Microsoft Graph call
/// happens.</item>
/// </list>
/// The test asserts the SHAPE of the two-leg evidence Croesus emits (leg 1 = inbound API-audience token,
/// leg 2 = downstream Graph-audience token) and the projected user profile. It deliberately does NOT assert
/// decoded token-B <c>jti</c>/<c>iat</c> or a certificate thumbprint, because leg 2 is a canned stand-in here —
/// asserting those would test the stub, not Croesus behavior. Live OBO evidence is captured out of band.
/// </summary>
public sealed class MeControllerTests : IClassFixture<MeControllerTests.OboSuccessFactory>
{
    private readonly OboSuccessFactory _factory;

    public MeControllerTests(OboSuccessFactory factory) => _factory = factory;

    [Fact]
    public async Task Me_ApiAudienceTokenWithScope_ReturnsProfile_AndTwoLegEvidenceShape()
    {
        var client = _factory.CreateClient();
        var apiToken = NegativeControlTests.TestAuth.CreateToken(
            NegativeControlTests.TestAuth.ApiAudience, "access_as_user");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);

        var response = await client.GetAsync("/api/me");

        // The inbound token is correctly audienced (aud == this API) and scoped (access_as_user), so the auth
        // gate admits it; the stubbed OBO exchange and Graph call then succeed deterministically -> 200.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        // Projected user profile comes from the canned Graph /me response.
        var user = root.GetProperty("user");
        Assert.Equal("Ada Lovelace", user.GetProperty("displayName").GetString());
        Assert.Equal("ada@contoso.com", user.GetProperty("userPrincipalName").GetString());
        Assert.Equal("user-oid-123", user.GetProperty("id").GetString());

        // Leg 1 evidence is derived from the REAL inbound token claims (not stubbed): aud == this API and
        // scp == access_as_user prove the token that entered Croesus was audienced for this API.
        var leg1 = root.GetProperty("evidence").GetProperty("leg1");
        Assert.Equal(NegativeControlTests.TestAuth.ApiAudience, leg1.GetProperty("aud").GetString());
        Assert.Equal("access_as_user", leg1.GetProperty("scp").GetString());

        // Leg 2 evidence describes the downstream Graph-audience token. The audience VALUE is Croesus's own
        // constant (not from the stub), so asserting it confirms Croesus targets Graph. correlationId and
        // expiresOn are present (populated from the canned OBO result) but their VALUES are not asserted.
        var leg2 = root.GetProperty("evidence").GetProperty("leg2");
        Assert.Equal("https://graph.microsoft.com", leg2.GetProperty("aud").GetString());
        Assert.False(string.IsNullOrEmpty(leg2.GetProperty("correlationId").GetString()));
        Assert.False(string.IsNullOrEmpty(leg2.GetProperty("expiresOn").GetString()));
    }

    /// <summary>
    /// Hosts the real API but swaps the JWT signing anchor (as the negative-control factory does) and
    /// additionally replaces <see cref="ITokenAcquisition"/> and <see cref="GraphServiceClient"/> with
    /// deterministic stand-ins so the OBO happy path can be exercised without any external call.
    /// </summary>
    public sealed class OboSuccessFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");

            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AzureAd:Instance"] = "https://login.microsoftonline.com/",
                    ["AzureAd:TenantId"] = "00000000-0000-0000-0000-000000000000",
                    ["AzureAd:ClientId"] = "11111111-1111-1111-1111-111111111111",
                    ["AzureAd:Audience"] = NegativeControlTests.TestAuth.ApiAudience,
                    ["Graph:BaseUrl"] = "https://graph.microsoft.com/v1.0",
                    ["Graph:Scopes"] = "user.read"
                });
            });

            builder.ConfigureTestServices(services =>
            {
                // Swap the bearer trust anchor to the local test key + fixed audience (mirrors the
                // negative-control factory) so no Microsoft Entra metadata/key fetch occurs.
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    options.RequireHttpsMetadata = false;
                    options.Configuration = new OpenIdConnectConfiguration();
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = false,
                        ValidateAudience = true,
                        ValidAudiences = new[] { NegativeControlTests.TestAuth.ApiAudience },
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = NegativeControlTests.TestAuth.SigningKey,
                        ValidateLifetime = false,
                        NameClaimType = "name"
                    };
                });

                // Registered last so these win over Microsoft.Identity.Web's real registrations: the OBO
                // exchange returns a canned result and Graph returns a canned /me, with no network I/O.
                services.AddSingleton<ITokenAcquisition>(new StubTokenAcquisition(CreateCannedOboResult()));
                services.AddSingleton(BuildStubGraphClient());
            });
        }

        // Keep this host alive for the whole (serialized) run: disposing it mid-run would tear down shared
        // IdentityModel state another still-live host depends on, causing a spurious 401 elsewhere. Process
        // exit reclaims the host.
        protected override void Dispose(bool disposing)
        {
        }
    }

    /// <summary>
    /// Builds a <see cref="GraphServiceClient"/> whose transport always returns a fixed /me profile. A no-op
    /// authentication provider is attached because the v4 request pipeline requires one to be present before
    /// sending (it is never asked to produce a real token here — the canned transport ignores auth headers).
    /// </summary>
    private static GraphServiceClient BuildStubGraphClient()
    {
        var graph = new GraphServiceClient(new HttpClient(new CannedMeHandler()))
        {
            AuthenticationProvider = new DelegateAuthenticationProvider(_ => Task.CompletedTask)
        };

        return graph;
    }

    /// <summary>
    /// Produces a stand-in On-Behalf-Of result. The access token is an obvious non-JWT placeholder; the
    /// metadata is populated so <c>oboResult.AuthenticationResultMetadata.TokenSource</c> does not throw.
    /// </summary>
    private static AuthenticationResult CreateCannedOboResult()
    {
        var expires = DateTimeOffset.UtcNow.AddMinutes(30);

        // Positional call binds unambiguously to the constructor whose 11th parameter is
        // AuthenticationResultMetadata (the other overload's 11th parameter is a string tokenType).
        return new AuthenticationResult(
            "canned-graph-access-token-not-a-real-jwt",
            false,
            "user-oid-123",
            expires,
            expires,
            "00000000-0000-0000-0000-000000000000",
            null,
            null,
            new[] { "https://graph.microsoft.com/User.Read" },
            Guid.NewGuid(),
            new AuthenticationResultMetadata(TokenSource.IdentityProvider));
    }

    /// <summary>Returns a canned Microsoft Graph <c>/me</c> body regardless of the request URL.</summary>
    private sealed class CannedMeHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            const string json =
                "{\"displayName\":\"Ada Lovelace\",\"userPrincipalName\":\"ada@contoso.com\",\"id\":\"user-oid-123\"}";

            var message = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            return Task.FromResult(message);
        }
    }

    /// <summary>
    /// Hand-written <see cref="ITokenAcquisition"/> stand-in (no mocking library is referenced). Only the
    /// user-delegated authentication-result methods return the canned OBO result; every other interface
    /// member throws, so an unexpected code path fails loudly rather than silently returning null.
    /// </summary>
    private sealed class StubTokenAcquisition : ITokenAcquisition
    {
        private readonly AuthenticationResult _result;

        public StubTokenAcquisition(AuthenticationResult result) => _result = result;

        public Task<AuthenticationResult> GetAuthenticationResultForUserAsync(
            IEnumerable<string> scopes, string? tenantId = null, string? userFlow = null,
            ClaimsPrincipal? user = null, TokenAcquisitionOptions? tokenAcquisitionOptions = null)
            => Task.FromResult(_result);

        public Task<AuthenticationResult> GetAuthenticationResultForUserAsync(
            IEnumerable<string> scopes, string? authenticationScheme, string? tenantId = null,
            string? userFlow = null, ClaimsPrincipal? user = null,
            TokenAcquisitionOptions? tokenAcquisitionOptions = null)
            => Task.FromResult(_result);

        public Task<string> GetAccessTokenForUserAsync(
            IEnumerable<string> scopes, string? tenantId = null, string? userFlow = null,
            ClaimsPrincipal? user = null, TokenAcquisitionOptions? tokenAcquisitionOptions = null)
            => throw new NotImplementedException();

        public Task<string> GetAccessTokenForUserAsync(
            IEnumerable<string> scopes, string? authenticationScheme, string? tenantId = null,
            string? userFlow = null, ClaimsPrincipal? user = null,
            TokenAcquisitionOptions? tokenAcquisitionOptions = null)
            => throw new NotImplementedException();

        public Task<string> GetAccessTokenForAppAsync(
            string scope, string? tenant = null, TokenAcquisitionOptions? tokenAcquisitionOptions = null)
            => throw new NotImplementedException();

        public Task<string> GetAccessTokenForAppAsync(
            string scope, string? authenticationScheme, string? tenant = null,
            TokenAcquisitionOptions? tokenAcquisitionOptions = null)
            => throw new NotImplementedException();

        public Task<AuthenticationResult> GetAuthenticationResultForAppAsync(
            string scope, string? tenant = null, TokenAcquisitionOptions? tokenAcquisitionOptions = null)
            => throw new NotImplementedException();

        public Task<AuthenticationResult> GetAuthenticationResultForAppAsync(
            string scope, string? authenticationScheme, string? tenant = null,
            TokenAcquisitionOptions? tokenAcquisitionOptions = null)
            => throw new NotImplementedException();

        public void ReplyForbiddenWithWwwAuthenticateHeader(
            IEnumerable<string> scopes, MsalUiRequiredException msalServiceException,
            HttpResponse? httpResponse = null)
            => throw new NotImplementedException();

        public void ReplyForbiddenWithWwwAuthenticateHeader(
            IEnumerable<string> scopes, MsalUiRequiredException msalServiceException,
            string? authenticationScheme, HttpResponse? httpResponse = null)
            => throw new NotImplementedException();

        public Task ReplyForbiddenWithWwwAuthenticateHeaderAsync(
            IEnumerable<string> scopes, MsalUiRequiredException msalServiceException,
            HttpResponse? httpResponse = null)
            => throw new NotImplementedException();

        public string GetEffectiveAuthenticationScheme(string? authenticationScheme)
            => throw new NotImplementedException();
    }
}
